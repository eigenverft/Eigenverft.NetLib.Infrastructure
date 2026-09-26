# SerilogRelay Limits and Outage Design

## Purpose

This document defines the intended limits, outage behavior, and configuration model for
`Eigenverft.NetLib.SerilogRelay`.

The goal is not to promise lossless logging under every possible double failure. The goal is
to make behavior explicit, bounded, configurable, and diagnosable.

The durable-first rule remains:

1. In normal operation an event is persisted to the local SQLite spool before HTTP delivery.
2. A remote endpoint outage must use the durable spool, not the volatile emergency buffer.
3. The emergency buffer exists only when local durable persistence itself is unavailable.
4. RAM and disk use must be bounded by explicit policy.
5. If a configured hard limit forces loss, that loss must be observable and counted.

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

`SpoolPolicy` owns **durable local storage**.

It answers:

- how large the SQLite spool may grow;
- how long already-sent rows are retained locally;
- whether unsent rows may expire by age;
- what happens when the configured durable-storage budget is exhausted;
- which data is removed first under storage pressure.

It does **not** own HTTP retry timing or emergency RAM.

## Current problem

The current default `unsentRetention = 3 days` is unsafe for intermittent clients.

Cleanup runs during sink construction before the sender starts. Therefore a client that has
five-day-old unsent backlog can delete that backlog immediately on startup before it gets a
chance to reconnect.

## Recommended balanced defaults

| Setting | Default | Meaning |
|---|---:|---|
| MaxSpoolBytes | **64 MiB** | Hard durable spool budget |
| SentRetention | 1 day | Local copy after confirmed server acceptance |
| UnsentMaxAge | none | Offline duration alone does not delete unsent events |
| MaxUnsentEvents | 100000 | Secondary guard against huge numbers of tiny rows |
| PressureLowWatermark | 85% | Reclaim enough space to avoid constant limit oscillation |

Important: `MaxSpoolBytes = 64 MiB` is a **ceiling, not a reservation**. SQLite still grows on
demand. A 2-5 MiB application does not allocate a 64 MiB file merely because the configured
maximum is 64 MiB.

64 MiB is intentionally much smaller than the previously proposed 1 GiB. This is a reusable
library and cannot assume that every small desktop utility or command-line tool should be
allowed to accumulate gigabytes of logs because the server was never deployed.

Deployments that intentionally need more history can opt into 128 MiB, 256 MiB, 1 GiB, or a
deployment-specific value.

## No independent maximum event-size policy by default

There should be **no default `MaxEventBytes` that truncates or rejects an event merely because
it is large**.

If an application deliberately logs a large exception, diagnostic dump, structured payload,
or other event, it may have a valid reason.

The total spool budget still bounds resource use. Therefore a very large event can consume a
significant part of the configured spool budget, but the relay should not silently rewrite or
truncate it.

If one event cannot be durably persisted because the configured spool/disk budget is
insufficient, that is a storage-limit failure and must be reported explicitly. The caller can
choose a larger `SpoolPolicy.MaxSpoolBytes` for workloads that intentionally contain very
large events.

## Storage-pressure order

When the spool approaches its hard budget:

1. remove expired rows that have already been sent;
2. remove additional already-sent rows if necessary;
3. reclaim expired dead-letter metadata if configured;
4. if unsent data still exceeds the configured hard budget, apply
   `UnsentOverflowAction`;
5. count and report every unsent-loss episode.

Recommended balanced default:

```text
UnsentOverflowAction = DropOldest
```

For logging, keeping the most recent diagnostic history is generally more useful during an
indefinitely dead server than preserving only the beginning of an outage. This action should
remain configurable; a deployment may choose `DropNewest` instead.

No unsent event should be deleted merely because it is three, seven, or thirty days old unless
the user explicitly configures an age limit.

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
| MaxBatchesPerCycle | 20 |
| InterBatchDelay | 100 ms |
| DrainBacklogOnStartup | true |

`TargetBatchBytes` is a batching target, **not an individual-event limit**.

If one event by itself is larger than the target batch size, send that event alone without
truncating it.

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

It does not need to increment every event in SQLite whenever the whole server is unreachable.

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
| MaximumRetryAfter | 1 hour |
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

For `429`, honor a valid `Retry-After`, bounded by policy.

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

to hundreds or thousands of SQLite rows every time the endpoint probe fails adds write load
without improving retry decisions.

Endpoint retry history belongs to `RetryPolicy`.

Per-event attempt metadata is useful only when we are trying to determine whether a particular
event itself is undeliverable.

---

# 4. PermanentFailurePolicy

## Responsibility

`PermanentFailurePolicy` owns **events that may themselves be invalid or impossible for the
receiver to accept**.

This is where per-event attempt metadata belongs.

It prevents one bad oldest event from blocking every later valid log forever.

## Behavior

If a multi-event batch receives a response that may be content-specific:

1. split the batch;
2. retry smaller groups;
3. identify the offending event;
4. continue delivering unrelated valid events;
5. retain explicit failure information for the bad event.

Possible per-event metadata:

- `RejectedAttempts`;
- `LastRejectedAt`;
- `LastRejectedStatus`;
- `LastRejectedReason`.

Recommended default:

```text
MaxRejectedAttempts = 3
Action = DeadLetter
```

A deterministic identity conflict may be dead-lettered earlier when the receiver has clearly
confirmed that the same `EventId` already exists with different content.

Dead-letter means "not blocking normal delivery anymore"; it must not mean silent deletion.

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

`EmergencyPolicy` owns the **volatile fallback used only when SQLite/local persistence cannot
accept new events**.

This is a double-degradation path and therefore must be tightly bounded.

## Recommended balanced defaults

| Setting | Default |
|---|---:|
| MaxBufferedEvents | 16384 |
| MaxBufferedBytes | **64 MiB** |
| OverflowAction | DropNewest |
| RetryDelay | 250 ms |
| DirectHttpRescue | enabled when endpoint exists |

The first reached limit wins: event count or byte budget.

64 MiB is the intended default RAM ceiling for emergency buffering.

A normal endpoint outage with a healthy SQLite spool must consume **zero** emergency-buffer
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
remaining emergency byte budget, it is an explicit emergency overflow.

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

Failure to flush durable SQLite backlog is not immediate data loss: it remains for the next
start.

Failure to drain volatile emergency events can be data loss and must be reported.

---

# Diagnostics options, not a policy

`SerilogRelayDiagnosticsOptions` should expose state but should not change delivery semantics.

Useful status values:

- pending durable event count;
- approximate spool bytes / configured spool budget;
- oldest unsent age;
- last successful delivery;
- last failed delivery;
- consecutive endpoint failures;
- current retry delay / next permitted attempt;
- last HTTP/network failure class;
- spool healthy/unavailable;
- emergency buffered event count;
- emergency buffered bytes;
- emergency dropped count;
- durable spool drop/eviction count;
- dead-letter count.

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
- `Emergency.MaxBufferedBytes = 64 MiB`.

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

Events are written to SQLite. Retry probes back off according to `RetryPolicy`. Emergency RAM
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

The executable does not grow its log database forever.

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

Storage-pressure actions happen before SQLite becomes unusable where possible.

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

# Recommended implementation order

1. Replace the unsafe default three-day unsent startup deletion with capacity-based retention.
2. Add `MaximumBatchWait` and immediate startup backlog delivery.
3. Introduce the grouped policy model:
   `SpoolPolicy`, `DeliveryPolicy`, `RetryPolicy`, `PermanentFailurePolicy`,
   `EmergencyPolicy`, and `ShutdownPolicy`.
4. Add the 64 MiB durable spool ceiling and explicit overflow behavior.
5. Implement retry gating/backoff as 5s -> 10s -> 20s -> 40s ... max 5m, with jitter and
   immediate reset on success.
6. Add permanent-event isolation/dead-letter behavior and per-event rejection metadata only
   for that path.
7. Add the 64 MiB emergency byte budget alongside the existing 16384-event limit.
8. Add diagnostics/status.
9. Define matching receiver transport/storage policies separately.

The first two items remove concrete current delivery hazards. The grouped policy model should
then be introduced before additional limits are added so each new parameter has a clear owner
and semantics.
