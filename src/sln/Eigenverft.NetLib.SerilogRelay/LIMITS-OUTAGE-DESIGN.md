# SerilogRelay Limits and Outage Design

## Purpose

This document defines the intended limits, outage behavior, and policy boundaries for
`Eigenverft.NetLib.SerilogRelay` and the matching `Eigenverft.Service.CentralLogging`
ingestion path.

The goal is not to promise lossless logging under every combination of failures. The goal is
to make the expected behavior explicit, bounded, observable, and suitable for both always-on
services and intermittently connected clients.

The core durability rule remains:

1. In normal operation an event is persisted to the local SQLite spool before HTTP delivery.
2. A remote endpoint outage must not move healthy-spool traffic into the volatile emergency
   buffer.
3. The volatile emergency buffer is only a fallback when local durable persistence itself is
   unavailable.
4. No policy may allow logging to consume unbounded RAM or unbounded disk.
5. Data loss at a configured hard limit must be explicit and observable rather than silent.

## Current behavior and identified gaps

The current implementation already provides several useful building blocks:

- durable SQLite-first persistence;
- stable per-event `EventId` values and receiver-side idempotency;
- a 16384-event bounded emergency Channel for local-spool failures;
- HTTP retry with adaptive delay;
- `429 Retry-After` handling;
- a 2-second HTTP timeout;
- default batches of 20 to 100 events;
- at most 20 batches in one sender pass;
- oldest-unsent-first delivery;
- a best-effort shutdown flush;
- SQLite corruption quarantine and recreation;
- `SelfLog` diagnostics.

The following current behaviors are important enough to change before treating the outage
model as complete.

### Unsent rows can expire before reconnect delivery

The public default is currently `unsentRetention = 3 days`. Cleanup runs during sink
construction, before the background sender starts, and removes unsent rows using their local
`CreatedAt` value.

That means a client that has durable backlog, remains offline for more than three days, and
then starts again can delete those old unsent rows before it has a chance to reconnect and
deliver them.

This is not a good default for laptops, field devices, tools, or other intermittent clients.

### Low-volume clients can wait indefinitely for the normal sender

The normal sender currently starts a delivery pass only when the pending count reaches
`minimumBatchSize` (default 20). A graceful shutdown bypasses that minimum, but a process
that stays alive with only a few events can retain them indefinitely, and a crash/power loss
does not execute the shutdown flush.

A minimum batch size is useful for efficiency, but it needs a maximum batch wait.

### Local spool growth is not bounded

The file spool currently has time-based cleanup but no hard byte limit and no total-event
limit. During a long endpoint outage, a healthy spool can therefore grow until the host disk
becomes the effective limit.

### Emergency memory is bounded by event count, not by bytes

16384 events is a useful event-count bound, but one event can contain a very large message,
exception, or properties payload. The emergency path therefore also needs a byte budget or an
event-size limit.

### Permanent HTTP failures can head-of-line block the backlog

Network failures, `429`, and server failures are naturally retryable. Some `4xx` responses
are different. A permanently invalid event or conflict in the oldest batch can currently
cause that same oldest batch to be retried indefinitely, preventing later valid events from
being delivered.

### Retry state is endpoint-level but not explicitly modeled

The sender already backs off after unsuccessful work, up to five minutes, and resets after
successful work. This is fundamentally endpoint/circuit state, not an individual-event
property.

Persisting a `SendAttempts` counter on every event for every network outage would cause
unnecessary SQLite writes during a long outage. Per-event failure metadata is useful for
isolating permanent/poison events, but transient endpoint backoff should remain endpoint-level.

### Receiver limits are implicit

The current CentralLogging receiver validates protocol identity and event identity, stores a
batch transactionally, accepts duplicate `EventId` values when their content matches, and
does not reject events merely because their event timestamp is old. That is desirable for
offline clients.

It currently has no explicit application-level maximum batch count, maximum event size,
maximum request body size, or storage-retention/size policy. Those should be made explicit and
kept compatible with the sender.

## Proposed policy model

Avoid growing the public Serilog extension method into a long list of unrelated primitive
parameters. Introduce one top-level policy object with a small number of cohesive sub-policies.

Conceptually:

```text
SerilogRelayPolicy
  Spool
  Delivery
  Retry
  Emergency
  Shutdown
  Observability
```

Profiles may later provide preconfigured policy objects, but all behavior should ultimately be
expressed through the same policy model.

### 1. Spool policy

Recommended balanced defaults:

| Setting | Recommended default | Meaning |
|---|---:|---|
| SentRetention | 1 day | Local copy after confirmed server acceptance |
| UnsentMaxAge | none | Do not drop durable unsent rows merely because a client was offline |
| MaxSpoolBytes | 1 GiB | Primary durable-backlog safety limit |
| MaxUnsentEvents | 250000 | Secondary guard against millions of tiny rows |
| ReservedFreeDiskBytes | 512 MiB | Do not consume the host's last free disk space |
| MaxEventBytes | 256 KiB | Prevent one event from dominating disk/RAM/request size |
| PressureLowWatermark | 90% | After pressure cleanup, create useful headroom |

`UnsentMaxAge = none` is deliberate. A bounded disk budget is a better default safety
mechanism than silently deleting a three-day-old event before a rarely connected client can
send it.

If a deployment wants legal/privacy age limits, it can explicitly set `UnsentMaxAge`.
Age-based expiration should not run before the sender has had a reconnect opportunity unless
the configured hard storage limit requires eviction.

Storage-pressure cleanup order:

1. delete expired already-sent rows;
2. aggressively delete older already-sent rows if more room is required;
3. remove expired dead-letter/quarantine metadata if configured;
4. only as a last resort, evict the oldest unsent rows until the low-watermark is reached;
5. increment explicit loss counters and emit throttled diagnostics for every unsent eviction
   episode.

The sender should never silently switch to deleting new events merely because the file has
grown.

Large individual events should preferably be materialized into a bounded representation
(truncating oversized message/exception/property fields with an explicit truncation marker)
rather than dropping the whole event.

### 2. Delivery policy

Recommended balanced defaults:

| Setting | Recommended default |
|---|---:|
| MinimumBatchEvents | 20 |
| MaximumBatchEvents | 100 |
| MaximumBatchBytes | 1 MiB |
| MaximumBatchWait | 5 seconds |
| MaxBatchesPerCycle | 20 |
| InterBatchDelay | 100 ms |
| DrainBacklogOnStartup | true |

Rules:

- `MinimumBatchEvents` remains an efficiency target, not a delivery requirement.
- If pending events have waited for `MaximumBatchWait`, send a partial batch.
- On startup, if durable backlog exists, make an immediate delivery attempt without waiting
  to accumulate 20 new events.
- Batch construction must obey both an event-count limit and a serialized-byte limit.
- Oldest unsent events remain first in normal delivery order.

This removes the current low-volume failure mode where 1-19 events can remain pending
indefinitely in a long-running process.

### 3. Retry / endpoint circuit policy

Recommended balanced defaults:

| Setting | Recommended default |
|---|---:|
| RequestTimeout | 10 seconds |
| InitialRetryDelay | 5 seconds |
| RetryMultiplier | 2 |
| MaximumRetryDelay | 5 minutes |
| RetryJitter | +/-20% |
| RespectRetryAfter | true |
| MaximumRetryAfter | 1 hour |
| ImmediateProbeAfterProcessStart | true |

The retry/circuit state should track at least:

- last successful delivery time;
- last failed delivery time;
- consecutive endpoint failures;
- current retry delay / next eligible attempt;
- last failure class/status.

This state may remain in memory initially. A process restart performing an immediate probe is
desirable; a week-old persisted backoff deadline is generally not.

HTTP outcome classification:

- network errors, timeouts, `408`, `429`, and `5xx`: transient; keep backlog and retry;
- `429`: honor valid `Retry-After` within the configured ceiling;
- `413`: reduce/split the batch rather than retrying the identical oversized request;
- `401`/`403`: configuration/authentication failure; retain backlog, back off strongly,
  surface unhealthy status, do not silently drop;
- `404`/protocol-route mismatch: configuration/protocol failure; retain backlog and use slow
  probes;
- `400`/`409` caused by event content: treat as potentially permanent and isolate the
  offending event instead of blocking the entire backlog forever.

### 4. Poison-event / attempt policy

Do not use a per-event `SendAttempts` counter to drive ordinary network backoff. A server
outage affects the endpoint, not the semantic validity of each individual log row.

Per-event metadata is useful after a response indicates that content may be permanently
undeliverable.

Recommended model:

- endpoint-level `ConsecutiveFailures` for transient failures;
- per-event `PermanentFailureCount`, `LastPermanentFailureAt`, and
  `LastPermanentFailureCode` only when isolating a `4xx` content problem;
- split a failing multi-event batch (binary split or one-by-one fallback) until the offending
  event is identified;
- move a confirmed poison event to a local dead-letter state/table so later valid events can
  continue;
- never delete a poison event silently;
- retain dead-letter metadata for a bounded period (for example 7 days) and expose its count.

A general per-event total attempt counter may still be added for diagnostics, but it should not
require updating every row on every unreachable-server retry.

### 5. Emergency policy

Recommended balanced defaults:

| Setting | Recommended default |
|---|---:|
| MaxBufferedEvents | 16384 |
| MaxBufferedBytes | 128 MiB |
| OverflowAction | DropNewest |
| RetryDelay | 250 ms |
| DirectHttpRescue | enabled when endpoint exists |

The emergency buffer remains only for local-persistence failures. A normal endpoint outage
with working SQLite must never consume this RAM budget.

Both event-count and byte limits are required. If either is exhausted, dropping is allowed
because the system has already lost both normal remote delivery and local durability.

Drops must remain observable through counters/status and throttled `SelfLog` diagnostics.

### 6. Shutdown policy

Recommended defaults:

| Setting | Recommended default |
|---|---:|
| EmergencyDrainTimeout | 3 seconds |
| DurableFlushTimeout | 5 seconds |
| FlushPartialBatch | true |

The timeout must be a real upper bound. The current shutdown loop checks a deadline outside a
delivery pass, but one pass can itself contain up to 20 HTTP requests. The implementation
should use a deadline cancellation token so the configured shutdown budget cannot
accidentally become tens of seconds.

Failure to flush durable rows is not data loss; they stay in SQLite for the next start.
Failure to drain volatile emergency rows can be data loss and must remain explicitly
reported.

## Suggested profiles

Profiles should be convenience factories over the policy model, not separate implementations.

### Balanced

Suitable as the normal default.

- 1 GiB durable spool;
- no automatic age deletion of unsent rows;
- 250000 unsent-event cap;
- 256 KiB per event;
- 20-100 event batches, 1 MiB maximum serialized batch;
- 5 second maximum batch wait;
- retry from 5 seconds up to 5 minutes;
- 16384 / 128 MiB emergency buffer.

### IntermittentClient

For laptops, field devices, maintenance tools, and applications that may run only every few
days.

- preserve unsent rows by capacity rather than age;
- immediate backlog probe on startup;
- shorter maximum batch wait (for example 2 seconds);
- same bounded disk and event-size rules;
- no assumption that a clean shutdown will happen before the client disappears.

A client being offline for 3, 7, or 30 days must not by itself cause backlog deletion when it
starts again.

### AlwaysOnService

For continuously running services with predictable disk allocation.

- larger explicit spool budget (for example 2 GiB or deployment-specific);
- same bounded event size;
- normal 5 second maximum batch wait;
- optional higher event-count cap if measured traffic requires it;
- health monitoring/alerting expected when spool utilization or oldest-unsent age grows.

Do not automatically increase batch count beyond the receiver's explicit ingestion limits.

## Outage matrix

### Remote endpoint unavailable, local spool healthy

**1 hour:** expected to be fully durable provided generated data fits the configured spool
budget. Retry backs off; no emergency memory is consumed.

**12 hours:** same semantics. Spool utilization and oldest-unsent age should make the outage
visible.

**1 day:** same semantics. Capacity, not event age, determines whether loss occurs.

**7 days:** still deliverable if the generated backlog fits the configured durable-spool
budget. No generic library can guarantee seven days without knowing event rate and event
size. When the hard storage budget is reached, sent rows are reclaimed first; oldest unsent
rows are evicted only as the last bounded-loss action and the loss is reported.

Capacity planning is therefore:

```text
required spool ~= event rate * average serialized event bytes * outage duration
```

plus SQLite/WAL/index overhead.

### Client process is offline for several days

No new events are generated while the process is not running.

Existing durable backlog must remain on disk. On the next start the sender should get a
delivery opportunity before any optional age-based unsent expiry is applied. With the
recommended default (`UnsentMaxAge = none`), merely being offline does not expire backlog.

This explicitly fixes the current three-day startup-cleanup problem.

### Low-volume client

With `MaximumBatchWait`, a client producing fewer than 20 events still sends after the time
limit. Delivery no longer depends on eventually reaching the minimum batch count or executing
a graceful shutdown.

### Endpoint unavailable and local spool temporarily busy/locked

Existing SQLite retry remains useful. If durable persistence still cannot accept an event,
that new event enters the emergency buffer.

Previously durable backlog remains on disk.

### Endpoint unavailable and local disk/spool unavailable

This is a double failure. The only remaining storage is the bounded emergency memory budget.

The system cannot guarantee lossless delivery for an arbitrary duration in this state.
Events beyond the emergency event/byte limit are dropped with explicit diagnostics.

### Disk full while endpoint is unavailable

Apply spool pressure policy before SQLite becomes unusable:

1. remove sent data;
2. reclaim optional/dead-letter data;
3. evict oldest unsent data only as the final hard-limit action;
4. if SQLite still becomes unwritable, enter emergency memory.

This is an explicitly degraded state, not a lossless guarantee.

### SQLite corruption while endpoint is available

Keep the existing quarantine/recreate behavior. New events can continue through the recovered
spool or emergency HTTP rescue. Quarantined data is preserved for diagnosis/recovery but is
not automatically guaranteed to be resendable.

### SQLite corruption while endpoint is unavailable

This is another double failure. Old corrupted data may survive only in quarantine, while new
events are limited by emergency memory until local durability recovers.

No lossless guarantee is possible; health/loss diagnostics are mandatory.

### Permanent bad event

A bad oldest event must not block all later events indefinitely. Isolate it, mark/dead-letter
it, and continue with subsequent rows.

## Receiver-side matching policies

The CentralLogging service should define explicit ingestion limits that are compatible with
the sender.

Proposed starting values:

| Setting | Proposed server value |
|---|---:|
| MaxBatchEvents | 200 |
| MaxRequestBodyBytes | 4 MiB |
| MaxEventBytes | 256 KiB |

The sender default of 100 events / 1 MiB stays comfortably below these limits.

The receiver should continue accepting old event timestamps. `Timestamp` is the producer
event time, while `ReceivedAt` records ingestion time. An intermittent client sending a
week-old event is valid and should not be rejected merely because of age.

Server-side database retention and maximum storage size should be a separate storage policy.
The current receiver store is also unbounded and therefore needs an explicit long-term
retention/capacity decision before production-scale use.

## Observability contract

A limits/outage design is incomplete if the application can only discover loss by reading a
rare `SelfLog` line.

Expose at least a lightweight status snapshot containing:

- durable pending event count;
- estimated pending bytes / spool utilization;
- oldest unsent event age;
- last successful delivery;
- last delivery failure and failure class;
- consecutive endpoint failures;
- next retry time/current delay;
- spool available/unavailable state;
- emergency buffered events and bytes;
- emergency dropped count;
- storage-pressure unsent-drop count;
- dead-letter count.

`SelfLog` remains useful for diagnostics, but counters/status make monitoring and tests much
more reliable.

## Guarantee boundary

With the proposed policies, the intended guarantee is:

- **healthy local spool:** remote outages are durable until the configured disk/event budget
  is exhausted;
- **intermittent client:** offline duration alone does not delete unsent backlog;
- **low traffic:** maximum batch wait ensures eventual background delivery without relying on
  shutdown;
- **local spool failure:** bounded emergency RAM provides best-effort continuity;
- **remote plus local failure:** lossless delivery is not guaranteed after the emergency
  budget is exhausted;
- **hard resource limit:** bounded loss is permitted, explicit, counted, and diagnosable;
- **permanent bad event:** isolated failure must not indefinitely block valid later events.

## Recommended implementation order

1. Remove the unsafe default unsent-age cleanup behavior and add regression coverage for a
   backlog older than three/seven days surviving startup and being deliverable.
2. Add `MaximumBatchWait` plus immediate startup backlog draining so low-volume clients are
   not dependent on a clean shutdown.
3. Introduce the policy object/sub-policies while preserving compatibility defaults where
   they are still safe.
4. Add spool byte/event pressure limits and event/batch byte limits.
5. Add explicit endpoint failure classification, jittered retry/circuit status, and real
   shutdown deadline cancellation.
6. Add poison-event isolation/dead-letter behavior for permanent `4xx` failures.
7. Add byte-bounded emergency buffering and the health/status snapshot.
8. Add matching CentralLogging ingestion limits and then design server-side storage
   retention/capacity separately.

The first two items remove concrete current data-loss/delivery hazards and should precede
broader tuning.
