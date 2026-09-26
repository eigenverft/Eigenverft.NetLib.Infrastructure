# SerilogRelay Limits and Outage Design

## Purpose

This document defines the intended limits, outage behavior, and configuration model for
`Eigenverft.NetLib.SerilogRelay`.

The goal is not to promise lossless logging under every possible double failure. The goal is
to make behavior explicit, bounded, configurable, diagnosable, and independent of storage-engine
mechanics.

The durable-first rule remains:

1. In normal operation an event is persisted to the durable local spool before HTTP delivery.
2. A remote endpoint outage must use the durable spool, not the volatile emergency buffer.
3. The emergency buffer exists only when durable local persistence itself is unavailable.
4. Durable spool growth and volatile buffering must be bounded by explicit policy.
5. If a configured hard limit forces loss, that loss must be observable and counted.
6. Policy semantics must not depend on one concrete storage engine.

A future storage implementation may never be needed. This document therefore does **not**
introduce a provider framework, public storage interface, backend selector, or ORM abstraction.
The only design requirement is that storage-engine details stay out of policy semantics and
public behavioral configuration.

## What a policy means here

A **policy** is not one numeric setting.

A policy is the complete behavior for one failure/resource domain:

```text
policy = decision rules + limits/timers + action when a limit/failure is reached
```

Example:

`RetryPolicy.InitialDelay = 5s` is only a parameter.

The **RetryPolicy** is the complete rule that says:

- which failures are retryable;
- when another attempt is allowed;
- how the delay grows;
- whether jitter is applied;
- what `Retry-After` means;
- when the failure state resets;
- which failures must not use ordinary retry.

This distinction keeps the public API understandable. We should not turn
`.WriteTo.SerilogRelay(...)` into a flat collection of twenty unrelated primitive
parameters.

## Proposed top-level configuration

Conceptually:

```text
SerilogRelayPolicy
  Spool
  Delivery
  Retry
  PermanentFailure
  Emergency
  Shutdown

SerilogRelayDiagnosticsOptions
```

The first six are behavioral policies. Diagnostics is deliberately separate: diagnostics
reports state, but does not decide how the relay behaves.

Profiles such as `Balanced`, `IntermittentClient`, or `AlwaysOnService` may later be
convenience factories that compose these same policies. A profile is not a separate
implementation.

---

# 1. SpoolPolicy

## Responsibility

`SpoolPolicy` owns **durable local spool behavior**.

It answers:

- how much retained relay data the spool may keep;
- how long already-sent events are retained locally;
- whether unsent events may expire by age;
- what happens when the configured durable budget is exhausted;
- which relay data is discarded first under storage pressure;
- how long one persistence attempt may block before the spool is treated as unavailable.

It does **not** own HTTP retry timing, emergency RAM, or storage-engine internals.

## Current problem

The current default `unsentRetention = 3 days` is unsafe for intermittent clients.

Cleanup runs during sink construction before the sender starts. Therefore a client that has
five-day-old unsent backlog can delete that backlog immediately on startup before it gets a
chance to reconnect.

## Recommended balanced defaults

| Setting | Default | Meaning |
|---|---:|---|
| MaxSpoolBytes | **64 MiB** | Maximum retained relay-data budget |
| SentRetention | 1 day | Local copy after confirmed server acceptance |
| UnsentMaxAge | none | Offline duration alone does not delete unsent events |
| PersistTimeout | 1 second | Maximum wall-clock budget for one persistence attempt |
| DeadLetterRetention | 7 days | Bounded retention for permanently rejected events |
| UnsentOverflowAction | DropOldest | Action when the durable relay-data budget is exhausted |

`MaxSpoolBytes = 64 MiB` is a **ceiling, not a reservation**. A small application does not
pre-allocate 64 MiB merely because that is the configured maximum.

The limit describes the relay's retained-data budget, not a specific database file format or
storage-engine bookkeeping. The concrete implementation is responsible for enforcing the
configured budget reasonably and for keeping its own implementation overhead bounded.

64 MiB is intentionally a modest general-library default. Deployments that intentionally need
more history can opt into a larger value.

## No independent maximum event-size policy by default

There should be **no default `MaxEventBytes` that truncates or rejects an event merely because
it is large**.

If an application deliberately logs a large exception, diagnostic dump, structured payload,
or other event, it may have a valid reason.

A very large event can consume a significant part of the configured spool budget, but the
relay should not silently rewrite or truncate it.

If one event can never fit inside the configured spool budget even when no other retained relay
data exists, return `RejectedByPolicy` directly. Do not discard existing backlog trying to make
room for an event that cannot fit under the configured policy.

## Storage-pressure order

When accepting another event would exceed the configured durable relay-data budget:

1. remove expired already-sent data;
2. remove additional already-sent data if necessary;
3. remove expired dead-letter data;
4. if the event still cannot fit, apply `UnsentOverflowAction`;
5. count and report every unsent-loss episode.

`DropNewest` rejects the incoming event once no reclaimable sent/dead-letter data remains.

`DropOldest` removes only as much oldest unsent data as required to accept the incoming event.

Recommended balanced default:

```text
UnsentOverflowAction = DropOldest
```

For logging, keeping the most recent diagnostic history is generally more useful during an
indefinitely dead server than preserving only the beginning of an outage. This action remains
configurable; a deployment may choose `DropNewest` instead.

No unsent event should be deleted merely because it is three, seven, or thirty days old unless
the user explicitly configures an age limit.

## Capacity result is not storage failure

A configured storage limit is normal policy behavior, not a broken spool.

The persistence boundary should return a small explicit result:

```text
SpoolWriteResult
  Status:
    Persisted
    RejectedByPolicy
    StorageUnavailable
  EvictedUnsentCount
```

Only `StorageUnavailable` activates `EmergencyPolicy`.

`RejectedByPolicy` means the incoming event was intentionally rejected by the configured
spool limit/overflow rule. `DropOldest` instead returns `Persisted` with a non-zero
`EvictedUnsentCount`.

Both paths increment the appropriate loss diagnostics; neither may bypass the durable limit by
consuming emergency RAM or using direct HTTP rescue.

## Persistence timeout

Use one generic `PersistTimeout` as the caller-facing persistence latency budget.

How the concrete storage implementation handles internal locking, transactions, retries, or
contention is its own concern. The policy only cares about the observable result: if durable
persistence cannot complete successfully within the configured budget, that attempt becomes
`StorageUnavailable` and the Emergency path may take over.

Do not expose engine-specific timeout/retry knobs in `SpoolPolicy` unless they later prove to
be meaningful across storage implementations.

## Existing spool above a newly configured budget

Introducing a smaller default must never trigger destructive startup cleanup of existing
unsent backlog.

Simple upgrade rule:

- existing durable backlog is grandfathered;
- never delete unsent backlog at startup merely to satisfy the new default;
- continue normal send and retention cleanup;
- apply the configured overflow rule to new writes while usage is over budget;
- expose `SpoolOverConfiguredBudget = true` until retained relay data naturally falls within policy.

The exact storage mechanics required to enforce this are implementation details.

## Storage failure and recovery

Corruption detection, repair, quarantine, migration, compaction, indexing, transaction-log
management, and similar concerns belong to the concrete storage implementation.

At the SerilogRelay policy boundary they collapse to simple observable outcomes:

- durable persistence is available;
- durable persistence is unavailable;
- previously retained data was lost or recovered and diagnostics should report that fact.

Do not add generic SerilogRelay policy knobs for one storage engine's recovery mechanics.

## Dead-letter storage

Dead-letter data belongs to the same logical durable-spool budget as normal relay data.

The policy defines retention and behavior, not representation. A concrete storage
implementation may use a separate table, collection, record type, or another internal shape.

No public schema/migration concept is required for this design. If the current implementation
needs an internal storage-format change, that change stays inside the storage implementation.

---

# 2. DeliveryPolicy

## Responsibility

`DeliveryPolicy` owns **normal healthy delivery and backlog draining**.

It answers:

- how events are batched;
- how long a small batch may wait;
- what happens to backlog on startup;
- how aggressively backlog is drained after recovery;
- how large a normal multi-event batch should become.

It does **not** decide when a failed endpoint may be retried. That is `RetryPolicy`.

## Recommended balanced defaults

| Setting | Default |
|---|---:|
| MinimumBatchEvents | 20 |
| MaximumBatchEvents | 100 |
| TargetBatchBytes | 1 MiB |
| MaximumBatchWait | 5 seconds |
| RequestTimeout | 10 seconds |
| MaxBatchesPerCycle | 20 |
| InterBatchDelay | 100 ms |
| DrainBacklogOnStartup | true |

`TargetBatchBytes` is a batching target, **not an individual-event limit**.

If one event by itself is larger than the target batch size, send that event alone without
truncating it.

`RequestTimeout` belongs here because it bounds one transport attempt. A timeout is then
classified by `RetryPolicy` as a transient delivery failure. The value remains configurable
for deployments that intentionally send very large single events.

## Low-volume behavior

`MinimumBatchEvents = 20` remains an efficiency target.

It must not mean "never send until 20 events exist."

If the oldest pending event has waited for `MaximumBatchWait`, send the partial batch.

This fixes the current case where a long-running client with only 1-19 events can leave those
events pending indefinitely and only a graceful shutdown happens to flush them.

## Startup behavior

If durable backlog exists when the process starts:

- do not wait for 20 new events;
- do not wait for the normal batch timer before the first probe;
- allow an immediate backlog delivery attempt, subject to `RetryPolicy`.

This is particularly important for clients that run only occasionally.

## Recovery catch-up

After the endpoint recovers, reset retry state immediately and use the normal
`DeliveryPolicy` at its full configured throughput.

Do **not** add a second recovery-specific token bucket or cooldown. The existing delivery
bounds such as `MaximumBatchEvents`, `MaxBatchesPerCycle`, and `InterBatchDelay` already
limit how much work is sent in one delivery cycle. Retry jitter protects the unhealthy probe
phase; once the endpoint has successfully responded, deliberately waiting longer only extends
the backlog unnecessarily.

If real deployments later show that successful catch-up can overload CentralLogging, that
should be addressed by tuning the normal `DeliveryPolicy` limits rather than by introducing a
special post-recovery throttle.

---

# 3. RetryPolicy

## Responsibility

`RetryPolicy` owns the **endpoint-unhealthy state**.

It answers:

- what counts as a transient endpoint failure;
- when another probe is allowed;
- how the retry interval grows;
- how jitter is applied;
- how `Retry-After` affects the schedule;
- when the endpoint is considered healthy again.

It does not need to update every persisted event whenever the whole server is unreachable.

## Backoff model

The desired behavior is an exponential **retry gate**.

After a failed endpoint attempt, no new probe is allowed until `NextAttemptAt`.

Recommended sequence:

```text
failure 1 -> wait about 5 seconds
failure 2 -> wait about 10 seconds
failure 3 -> wait about 20 seconds
failure 4 -> wait about 40 seconds
failure 5 -> wait about 80 seconds
failure 6 -> wait about 160 seconds
later    -> cap around 5 minutes
```

Apply jitter so that many clients do not retry at the same instant:

```text
actual delay = backoff delay +/- 20%
```

Recommended defaults:

| Setting | Default |
|---|---:|
| InitialDelay | 5 seconds |
| Multiplier | 2 |
| MaximumDelay | 5 minutes |
| Jitter | +/-20% |
| RespectRetryAfter | true |
| ImmediateProbeAfterProcessStart | true |

## Reset behavior

As soon as a delivery succeeds:

- `ConsecutiveFailures = 0`;
- clear `NextAttemptAt`;
- reset the delay to the normal 5-second starting state;
- return immediately to normal `DeliveryPolicy`.

So after recovery there is no artificial five-minute penalty left over from the outage.

## New logs while endpoint is down

New log events continue to be durably spooled.

They must **not** bypass the retry gate and create a new HTTP request for every incoming log.
The endpoint is already known to be unhealthy until the next permitted probe time.

## Failure classification

Transient:

- connection failure;
- DNS/network failure;
- request timeout;
- HTTP `408`;
- HTTP `429`;
- HTTP `5xx`.

For `429`, support both delta and HTTP-date forms of `Retry-After`.

Use one unambiguous rule:

```text
NextAttemptAt = max(
    now + jittered exponential backoff,
    Retry-After not-before time
)
```

Jitter may delay a retry further, but it must never make the client retry earlier than the
server's valid `Retry-After`. No additional `MaximumRetryAfter` setting is needed initially;
adding one would introduce another edge-case rule without evidence that it is required.

Configuration/protocol failures:

- `401` / `403`;
- persistent `404`;
- incompatible protocol endpoint.

These should use a slow retry/probe state and expose an unhealthy status, not delete backlog.

Potentially event-specific failures:

- `400`;
- `409`;
- `413` after normal batch splitting.

Those are handed to `PermanentFailurePolicy`.

## Why not persist SendAttempts for every outage?

When the server is simply down, all pending events share the same failure cause.

Writing:

```text
SendAttempts = SendAttempts + 1
```

to hundreds or thousands of persisted events every time the endpoint probe fails adds storage writes
without improving retry decisions.

Endpoint retry history belongs to `RetryPolicy`.

For the first implementation, do not persist generic per-event attempt counters at all.
Deterministic single-event rejections are recorded once when the event is moved to dead-letter
storage.

---

# 4. PermanentFailurePolicy

## Responsibility

`PermanentFailurePolicy` owns **events that may themselves be invalid or impossible for the
receiver to accept**.

It prevents one bad oldest event from blocking every later valid log forever.

## Behavior

Keep the first implementation deterministic and small. Do not add a generic per-event
`RejectedAttempts` state machine unless real receiver behavior shows that it is needed.

If a multi-event batch receives a response that may be content-specific:

1. split the batch;
2. retry smaller groups;
3. continue until the rejecting event is isolated;
4. if the single event is deterministically rejected, move it to dead-letter storage;
5. continue delivering unrelated valid events.

The dead-letter row only needs diagnostic facts such as:

- original event payload/identity;
- `RejectedAt`;
- HTTP status / failure classification;
- optional receiver reason/body excerpt.

A confirmed `EventId` content conflict can be dead-lettered immediately. Likewise, a
single-event `413` is not helped by retrying the identical payload forever.

Dead-letter means "preserved locally but no longer blocking normal delivery"; it must not mean
silent deletion.

### Representation strategy: keep it internal

The policy does not prescribe tables, columns, entities, files, or migrations.

For the first implementation, preserve rejected-event data using the simplest internal
representation that fits the current storage implementation. Do not create a generic migration
framework or storage-provider abstraction speculatively.

If a future storage implementation actually appears, the permanent-failure semantics remain the
same while its internal representation may differ.

## Large events

There is no independent client-side event-size rejection.

If an event exceeds `DeliveryPolicy.TargetBatchBytes`, send it alone.

If the receiver or transport returns `413 Payload Too Large` for that single event, preserve
the event and record the rejection through this policy. Do not truncate it automatically.

The deployment can then intentionally raise the receiver/transport request limit if such large
events are expected.

---

# 5. EmergencyPolicy

## Responsibility

`EmergencyPolicy` owns the **volatile fallback used only when durable local persistence cannot
accept new events**.

This is a double-degradation path and therefore must be tightly bounded.

## Recommended balanced defaults

| Setting | Default |
|---|---:|
| MaxBufferedEvents | 16384 |
| MaxBufferedPayloadBytes | **64 MiB** |
| OverflowAction | DropNewest |
| LocalSpoolRetryDelay | 250 ms |
| DirectHttpRescue | enabled when endpoint exists |

The first reached limit wins: event count or buffered-payload byte budget.

The byte budget is deliberately named `MaxBufferedPayloadBytes`, not a strict process-RAM
ceiling. It should use one consistent, cheap accounting measure such as the serialized UTF-8
payload size of the materialized `LogEntry`.

Managed-object overhead, temporary serialization buffers, and the single event currently being
materialized can make process memory briefly exceed 64 MiB. Guaranteeing exact process RSS
would require significantly more machinery and is not necessary for this fallback path.

A normal endpoint outage with a healthy durable spool must consume **zero** emergency-buffer
capacity.

## Overflow

If both durable local persistence and remote delivery are unavailable long enough to fill the
emergency budget, loss is unavoidable.

The default should drop the new event rather than continuously replacing already-buffered
volatile events:

```text
OverflowAction = DropNewest
```

Every overflow episode must increment counters and emit throttled diagnostics.

There is no event-content truncation here either. If one event itself cannot fit into the
remaining emergency payload budget, it is an explicit emergency overflow.

## Emergency HTTP rescue and RetryPolicy

`LocalSpoolRetryDelay = 250 ms` controls only how often the Emergency worker retries durable
local persistence.

Direct HTTP rescue must use the **same global RetryGate** as the normal sender:

- if the RetryGate is open, Emergency may use the permitted endpoint probe;
- if the endpoint fails, the shared RetryPolicy advances `NextAttemptAt`;
- while the gate is closed, Emergency keeps retrying local persistence but does not generate
  HTTP requests every 250 ms.

This avoids two independent retry systems fighting each other and prevents a broken local
spool from bypassing endpoint backoff.

---

# 6. ShutdownPolicy

## Responsibility

`ShutdownPolicy` owns **how much time the relay may spend trying to finish work during
shutdown**.

It answers:

- how long to drain volatile emergency events;
- how long to flush durable backlog;
- whether partial batches are allowed;
- how shutdown deadlines cancel in-flight work.

## Recommended defaults

| Setting | Default |
|---|---:|
| EmergencyDrainTimeout | 3 seconds |
| DurableFlushTimeout | 5 seconds |
| FlushPartialBatch | true |

These values must be real wall-clock upper bounds.

The implementation should use deadline cancellation tokens rather than checking a deadline
only between large delivery passes.

Failure to flush durable backlog is not immediate data loss: it remains for the next
start.

Failure to drain volatile emergency events can be data loss and must be reported.

---

# Diagnostics options, not a policy

`SerilogRelayDiagnosticsOptions` should expose state but should not change delivery semantics.

Useful status values:

- pending durable event count;
- retained spool bytes / configured spool budget;
- whether an upgraded spool is currently over the configured budget;
- oldest unsent age;
- last successful delivery;
- last failed delivery;
- consecutive endpoint failures;
- current retry delay / next permitted attempt;
- last HTTP/network failure class;
- spool healthy/unavailable;
- emergency buffered event count;
- emergency buffered payload bytes;
- emergency dropped count;
- durable spool policy-drop/eviction count;
- dead-letter count;
- retained corruption archive count.

`SelfLog` remains useful, but a status snapshot makes monitoring and testing far more
reliable.

---

# Profiles

Profiles are preconfigured policy sets, not new behavior.

## Balanced

Suggested general library default:

- `Spool.MaxSpoolBytes = 64 MiB`;
- no age-based deletion of unsent rows;
- `MinimumBatchEvents = 20`;
- `MaximumBatchEvents = 100`;
- `MaximumBatchWait = 5s`;
- retry 5s, 10s, 20s, 40s ... capped at 5m with jitter;
- `Emergency.MaxBufferedEvents = 16384`;
- `Emergency.MaxBufferedPayloadBytes = 64 MiB`.

## IntermittentClient

For laptops, field devices, tools, or clients that may run once every few days:

- same bounded spool model;
- no unsent age expiry by default;
- immediate startup backlog probe;
- short maximum batch wait;
- do not assume a clean shutdown will occur.

Offline for 3, 7, or 30 days does not by itself delete unsent backlog.

## AlwaysOnService

For known server workloads:

- same policy model;
- deployment may intentionally raise spool budget;
- health monitoring should watch spool utilization and oldest-unsent age;
- catch-up throughput can be raised intentionally when CentralLogging capacity is known.

The library should not assume this high-capacity profile for every executable.

---

# Outage behavior

## Endpoint down, local spool healthy

### 1 hour

Events are written to the durable spool. Retry probes back off according to `RetryPolicy`. Emergency RAM
is not used.

### 12 hours

Same behavior. The only relevant hard limit is the configured durable spool budget.

### 1 day

Same behavior. Unsent age alone does not remove the backlog.

### 7 days

Still recoverable if the backlog fits the configured spool budget.

The relay cannot promise "seven days" independently of traffic volume. A 64 MiB budget may
represent days for a quiet client and minutes for a very noisy service.

The meaningful guarantee is therefore:

```text
durable while generated backlog <= configured spool budget
```

not:

```text
durable for exactly N days
```

## Server never exists / endpoint is permanently wrong

The executable does not grow its durable relay spool forever.

It grows only up to `SpoolPolicy.MaxSpoolBytes` (64 MiB by default), then follows the
configured unsent overflow action with explicit loss accounting.

This is the important protection for a small application that ships with a sink configuration
but whose CentralLogging server is never actually deployed.

## Client only runs every few days

Existing backlog remains on disk.

On startup the relay gets an immediate delivery opportunity instead of deleting rows because
they are older than three days.

## Low-volume client

A client with 1-19 logs sends them when `MaximumBatchWait` expires. It does not rely on a
clean shutdown.

## Endpoint down + spool unavailable

This is a genuine double failure.

New events use the bounded emergency buffer: 16384 events and 64 MiB by default.

After that budget is exhausted, loss is unavoidable and explicitly reported.

## Disk full + endpoint down

Storage-pressure actions happen before durable persistence becomes unavailable where possible.

If durable persistence ultimately fails, new events move to `EmergencyPolicy`.

## Permanent bad event

The event is isolated by `PermanentFailurePolicy`; it must not block later valid backlog.

---

# Receiver relationship

The sender should not invent an arbitrary small individual-event limit merely because batching
normally targets 1 MiB.

The receiver/transport will always have some practical request-body ceiling. That ceiling
should be explicit in the CentralLogging deployment and compatible with expected workloads.

Rules:

- large events may be sent as a one-event batch;
- old producer timestamps remain valid;
- `ReceivedAt` remains distinct from producer `Timestamp`;
- a receiver-side size rejection must not cause silent client-side truncation;
- receiver storage retention/capacity is a separate server policy.

---

# Public API compatibility

The grouped policy model must not make the common path harder.

The normal configuration stays:

```csharp
.WriteTo.SerilogRelay("https://logging.example/api/v1/logs")
```

A caller that does not care about outage tuning should not need to know that the policy model
exists.

Keep the current overload for compatibility and add one clearly distinct policy-based overload
for advanced customization, for example conceptually:

```text
SerilogRelay(endpoint, policy, ...)
```

The policy object should come with complete defaults so callers override only the settings they
actually care about. Do not require callers to construct every sub-policy explicitly.

Both overloads map into the same internal policy objects. The existing flat arguments remain
compatibility sugar, not a second implementation path.

Do not add backend/provider selection to the public API as part of this work. Existing
storage-location options remain implementation-specific compatibility settings for the current
spool; they do not define the policy model.

Do not make `policy` another optional argument on the already long existing signature if that
creates overload ambiguity. Prefer a clearly distinct overload and deprecate old flat knobs
only when there is a real release reason.

---

# Minimal implementation boundaries

The boundary reviews support a deliberately small change: keep storage mechanics behind one
coherent internal spool responsibility, but do **not** introduce a public/provider abstraction
for a hypothetical future backend.

The smallest useful shape is:

```text
SerilogRelaySink
  -> RelaySpool
  -> RetryGate
  -> RelayHttpTransport
  -> EmergencyBuffer
```

Responsibilities:

- `SerilogRelaySink`: materialize Serilog events and orchestrate lifecycle;
- `RelaySpool`: own durable persistence behavior, retention/capacity decisions, dead-letter
  handling, recovery mapping, and `SpoolWriteResult`;
- `RetryGate`: own endpoint backoff state shared by normal delivery and Emergency HTTP rescue;
- `RelayHttpTransport`: perform one HTTP attempt and return a structured `DeliveryResult`;
- `EmergencyBuffer`: own the bounded volatile queue plus payload-byte accounting.

`RelaySpool` may internally use the current concrete storage technology directly. Do not add a
provider registry, backend selector, ORM abstraction, or public storage extension point unless
a second real implementation creates evidence that such a contract is worth its cost.

Use structured results instead of booleans/exceptions for expected decisions:

```text
SpoolWriteResult:
  Status = Persisted | RejectedByPolicy | StorageUnavailable
  EvictedUnsentCount

DeliveryResult:
  Success
  TransientFailure
  ConfigurationFailure
  PotentialPermanentFailure
```

That is enough separation to keep policy semantics independent from storage mechanics without
paying today for a backend-plugability feature that may never exist.

Tests should follow these few responsibilities directly. Do not create a class/interface for
every design paragraph.

---

# Recommended implementation order

1. Fix the two concrete current delivery hazards first:
   remove default age-based deletion of unsent startup backlog, and add `MaximumBatchWait` plus
   immediate startup backlog delivery.
2. Introduce the grouped policy model and compatibility overload, keeping policy names and
   semantics free of storage-engine details.
3. Extract only the internal boundaries that are needed to keep responsibilities clear:
   `RelaySpool`, `RetryGate`, `RelayHttpTransport`, and `EmergencyBuffer`.
4. Add the 64 MiB durable spool budget, generic `PersistTimeout`, safe over-budget upgrade
   behavior, and explicit `SpoolWriteResult` semantics. Let the current storage implementation
   choose its own internal enforcement/recovery mechanics.
5. Implement retry gating/backoff as 5s -> 10s -> 20s -> 40s ... max 5m, with jitter,
   `Retry-After`, immediate reset on success, and one shared gate for Emergency HTTP rescue.
6. Add the 64 MiB emergency buffered-payload budget alongside the existing 16384-event limit.
7. Add deterministic permanent-event isolation and bounded dead-letter retention using the
   simplest representation supported by the current storage implementation.
8. Add diagnostics/status.
9. Define matching receiver transport/storage policies separately.

Do not implement storage-provider plugability as part of this work. If a second real storage
implementation is ever required, use the existing `RelaySpool` responsibility as the seam and
extract only the smallest contract proven necessary at that time.

This order fixes real current loss/delay behavior first and preserves a clean future seam
without speculatively building for a future that may never arrive.
