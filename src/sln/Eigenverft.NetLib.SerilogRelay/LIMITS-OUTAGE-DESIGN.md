# SerilogRelay Limits and Outage Implementation Guideline

## Goal

This document is the implementation guideline for the next focused
`Eigenverft.NetLib.SerilogRelay` reliability change.

The existing relay already works. This change is **not** a logging-platform redesign and is not
intended to produce a complete architecture for every theoretical failure mode.

The rule for this change is:

> Add the smallest robust behavior that fixes a concrete current failure mode or operating
> limit. Preserve existing behavior everywhere else.

When a detail can be decided locally and unambiguously during implementation, decide it there.
Do not introduce a new policy, abstraction, class, option, or subsystem only to make the design
theoretically complete.

## Scope

The implementation should focus on these concrete improvements:

1. unsent backlog must not be deleted by default merely because it is old;
2. low-volume clients must not wait forever for `MinimumBatchEvents`;
3. durable backlog must get an immediate delivery chance after startup;
4. sender and Emergency direct-HTTP rescue must share one endpoint retry gate;
5. Emergency buffering must remain bounded by event count and an additional payload-byte budget;
6. shutdown/flush work must obey a real cancellation/deadline boundary;
7. a bounded durable spool may be added if it stays a small extension of the existing spool
   implementation.

Everything else is follow-up unless it becomes necessary to implement one of these items
correctly.

---

# 1. Configuration shape

Keep the normal usage simple:

```csharp
.WriteTo.SerilogRelay("https://logging.example/api/v1/logs")
```

A caller using defaults must not need to construct policy or options objects.

Avoid adding more primitive parameters to the already long public method signature. For new
behavior, prefer one options model with complete defaults:

```text
SerilogRelayOptions
  Spool
  Delivery
  Retry
  Emergency
```

Preferred names:

- `SerilogRelayOptions`
- `SpoolOptions`
- `DeliveryOptions`
- `RetryOptions`
- `EmergencyOptions`

Do not create public policy classes for Shutdown, Diagnostics, permanent failures, storage
providers, or other rare cases unless implementation work proves they are actually needed.

Keep the existing public overload compatible. Add a distinct options-based overload only if
needed for the new settings. Both paths must use the same internal defaults and implementation.

## Suggested defaults

```text
SpoolOptions
  MaxSpoolBytes          = 64 MiB        // conditional implementation, see section 7
  SentRetention          = 1 day
  UnsentMaxAge           = none
  UnsentOverflowAction   = DropOldest

DeliveryOptions
  MinimumBatchEvents     = 20
  MaximumBatchEvents     = 100
  MaximumBatchWait       = 5 seconds

RetryOptions
  InitialDelay           = 5 seconds
  Multiplier             = 2
  MaximumDelay           = 5 minutes
  Jitter                 = +/-20%
  RespectRetryAfter      = true

EmergencyOptions
  MaxBufferedEvents      = 16384
  MaxBufferedPayloadBytes = 64 MiB
```

Do not add settings merely because they could theoretically be configurable.

---

# 2. Unsent backlog retention

## Required behavior

Unsent durable backlog must **not** be deleted by default only because it is old.

The current `unsentRetention = 3 days` behavior is unsafe for intermittently running clients,
especially because cleanup can happen during startup before delivery gets a chance.

Required default:

```text
UnsentMaxAge = none
```

Already-sent data may continue to use the current one-day retention default.

## Startup ordering

Startup must not delete old unsent backlog before the sender has had a delivery opportunity.

If an explicit non-default `UnsentMaxAge` is later supported, its cleanup must not recreate the
same startup-loss problem.

## Keep this simple

Do not design multiple retention strategies. The needed correction is simply:

- sent data may expire;
- unsent data does not expire by age by default.

---

# 3. Low-volume and startup delivery

## Minimum batch is an efficiency target

`MinimumBatchEvents = 20` remains useful for normal batching.

It must not mean that 1-19 events can remain pending indefinitely.

Add:

```text
MaximumBatchWait = 5 seconds
```

When the oldest pending event has waited at least this long, the sender may send a partial batch
below `MinimumBatchEvents`.

The existing maximum batch-size behavior remains unchanged unless implementation requires a
small local adjustment.

## Startup backlog

When the process starts and durable unsent backlog already exists:

- signal the sender immediately;
- do not wait for a new log event;
- do not wait to reach `MinimumBatchEvents`;
- allow an immediate delivery attempt, subject to `RetryGate`.

This behavior is important for clients that start only occasionally.

## Recovery after endpoint outage

After a successful HTTP delivery, reset retry state immediately and resume normal delivery at
the existing configured sender throughput.

Do **not** add a post-recovery token bucket, cooldown, or second rate limiter in this change.

The existing batch/cycle pacing is sufficient unless real deployments later show otherwise.

---

# 4. Shared RetryGate

Introduce one small internal `RetryGate`.

Its responsibility is intentionally narrow:

> May an HTTP attempt happen now, and when may the next attempt happen after failure?

The normal sender and Emergency direct-HTTP rescue must use the same instance/state.

## Required behavior

Recommended backoff:

```text
failure 1 -> about 5 seconds
failure 2 -> about 10 seconds
failure 3 -> about 20 seconds
failure 4 -> about 40 seconds
...
cap       -> about 5 minutes
```

Apply approximately +/-20% jitter.

On successful delivery:

- reset consecutive failure state;
- clear the next-attempt deadline;
- return immediately to the initial retry state.

New logs arriving while the endpoint is known to be unavailable must be persisted normally.
They must not cause a fresh HTTP request that bypasses the active gate.

## Retry-After

When the receiver returns a valid `Retry-After`, do not retry before that time.

A simple rule is sufficient:

```text
NextAttemptAt = max(
    now + jittered exponential backoff,
    Retry-After not-before time
)
```

Do not introduce a second HTTP retry state machine inside the Emergency worker.

## Persistence of retry state

Keep RetryGate state in memory.

Do not persist per-event `SendAttempts` for ordinary endpoint outages.

After process restart, an immediate endpoint probe is acceptable; if it fails, the normal
RetryGate starts again.

---

# 5. Emergency buffer

Emergency remains a **local durable-persistence failure path**, not an endpoint-outage buffer.

Required invariant:

```text
endpoint unavailable + durable spool healthy
=> no Emergency buffering
```

Emergency is used only when the current event cannot be durably persisted because local
persistence is genuinely unavailable.

## Bounds

Keep the existing event-count bound:

```text
MaxBufferedEvents = 16384
```

Add a simple payload-byte budget:

```text
MaxBufferedPayloadBytes = 64 MiB
```

The first reached limit wins.

This is **not** an exact process-RAM guarantee. Use one cheap, deterministic payload-size
measurement that fits the existing materialization/serialization path. Managed-object and
temporary-buffer overhead do not need exact accounting.

Do not truncate event content merely to fit the Emergency budget.

If an event cannot be accepted because either bound is exhausted, keep the current explicit
loss diagnostics/counter behavior.

## Emergency HTTP rescue

Emergency may still attempt direct HTTP rescue while local persistence is unavailable, but it
must obey the shared `RetryGate`.

The existing short local-persistence retry cadence may remain local to Emergency. It must not
become an independent high-frequency HTTP retry loop.

---

# 6. Shutdown deadline

Shutdown/flush logic must obey a real wall-clock deadline.

The current pattern must not continue work indefinitely through operations using
`CancellationToken.None` after the nominal deadline has elapsed.

Implementation direction:

- create a cancellation token/deadline for shutdown delivery work;
- pass it through pending-load, HTTP, delays, and sender work used by shutdown;
- when the deadline expires, stop further shutdown delivery work;
- durable unsent backlog remains for the next process start;
- unresolved volatile Emergency events remain explicitly diagnosable as potential loss.

Do not create a separate public `ShutdownPolicy` just for this change.

Preserve current shutdown timing defaults where practical; the important correction is that
the deadline is real rather than advisory.

---

# 7. Durable spool capacity

A bounded durable spool is desirable because a permanently unreachable or never-deployed
endpoint must not allow local log storage to grow forever.

Target default:

```text
MaxSpoolBytes = 64 MiB
```

This is a maximum, not pre-allocation.

## Required semantics if implemented

A configured capacity limit is **not** the same as storage failure.

If a write hits the configured limit:

- apply the configured overflow behavior;
- report/count any loss;
- do **not** activate Emergency merely because the configured capacity was reached.

Recommended default:

```text
UnsentOverflowAction = DropOldest
```

`DropNewest` may remain an option if it is cheap to support.

When reclaiming room, discard already-sent/expired data before unsent data.

If one incoming event cannot possibly fit within the configured spool budget by itself, reject
that event explicitly. Do not delete the existing backlog trying to make room for something
that still cannot fit.

## Upgrade safety

A newly introduced 64 MiB default must not destructively purge an existing larger unsent spool
during startup.

Existing backlog is grandfathered. Let normal sending/cleanup reduce it naturally; apply the
new capacity rule to new writes.

## Implementation constraint

Implement the 64 MiB cap in this change **only if it can be done robustly as a contained change
inside the existing spool code**.

The current implementation uses SQLite, so a small SQLite-specific mechanism is acceptable
inside `RelaySpool` if it stays simple.

Do not build for this change:

- a generic storage-provider system;
- a backend registry;
- an ORM abstraction;
- public storage interfaces for a hypothetical second implementation;
- a generic capacity-accounting subsystem;
- a WAL/compaction/migration architecture created only to support this limit.

If a robust cap starts requiring that level of machinery, defer the capacity implementation
and keep the behavioral contract in this document as a follow-up.

## Small internal result if useful

If it simplifies control flow, use one small result type such as:

```text
Persisted
RejectedByPolicy
StorageUnavailable
```

A tiny enum/record is enough.

Do not build a result/domain hierarchy.

---

# 8. Permanent bad events

A permanently rejected oldest event can eventually block later backlog. The problem is real,
but it is **not required for the first implementation** if solving it cleanly needs significant
new machinery.

Do not build speculatively:

- generic per-event retry state;
- persisted `SendAttempts` for all events;
- a large PermanentFailure state machine;
- a general dead-letter subsystem;
- complex batch-bisection infrastructure;
- a migration framework solely for this feature.

If implementation work exposes a small, obvious fix that is necessary for correctness, it may
be included.

Otherwise record permanent-event isolation as a focused follow-up.

---

# 9. Minimal internal structure

The current sink carries many responsibilities. Split only where the responsibility is already
clear and the extraction makes the implementation easier to test or reason about.

A sufficient first target is approximately:

```text
SerilogRelaySink.cs
SerilogRelayOptions.cs
RelaySpool.cs
RetryGate.cs

LogEntry.cs
LogBatchPayload.cs
LogBatchJsonContext.cs
```

## Responsibilities

`SerilogRelaySink` may continue to own:

- Serilog event materialization;
- sender loop;
- HTTP orchestration;
- Emergency worker;
- lifecycle/disposal.

`RelaySpool` should own the current durable-storage implementation details:

- persistence;
- pending reads/counts;
- sent marking;
- retention;
- optional capacity if implemented;
- existing corruption recovery.

`RetryGate` owns only endpoint-attempt timing/state.

Moving `LogEntry`, `LogBatchPayload`, and `LogBatchJsonContext` out of the large sink file is
reasonable if it makes the file easier to work with.

Do not create a class or interface for every section of this guideline.

Further extraction is justified only when the real implementation becomes clearer because of
it.

---

# 10. Tests required by this change

Prefer behavior tests over tests of new internal structure.

At minimum cover:

1. old unsent backlog survives startup/default cleanup;
2. low-volume backlog below `MinimumBatchEvents` is sent after `MaximumBatchWait`;
3. existing startup backlog gets an immediate delivery attempt;
4. new log events do not bypass an active `RetryGate`;
5. successful delivery resets the RetryGate;
6. valid `Retry-After` delays the next attempt appropriately;
7. normal endpoint outage with a healthy spool does not use Emergency;
8. Emergency remains bounded by event count;
9. Emergency remains bounded by payload bytes;
10. Emergency direct HTTP rescue shares the same RetryGate;
11. shutdown stops delivery work when its deadline expires;
12. existing unrelated relay behavior continues to pass.

If spool capacity is implemented in the same change, additionally cover:

- capacity limit does not activate Emergency;
- `DropOldest` / selected overflow behavior is deterministic;
- an existing oversized spool is not purged at startup;
- an individually too-large event does not cause existing backlog to be destroyed.

Reuse existing tests where practical. Do not build a new test architecture merely to mirror the
new file structure.

---

# 11. Explicitly not in scope

Do not add as part of this change unless required to fix one of the core behaviors above:

- storage-provider plugability;
- a second storage backend;
- ORM/EF Core adoption;
- a general migration framework;
- profiles such as `Balanced`, `AlwaysOnService`, or `IntermittentClient`;
- a large observability/status framework;
- generic event retry state;
- full dead-letter infrastructure;
- complex post-recovery rate limiting;
- receiver/CentralLogging redesign;
- abstractions added only for hypothetical future requirements.

Existing implementation details that already work reasonably should remain unchanged unless this
guideline explicitly corrects them.

---

# 12. Implementation order

When implementation starts, use this order:

1. add the options model/defaults without breaking the simple existing API;
2. stop default age-based deletion of unsent backlog;
3. add `MaximumBatchWait` and immediate startup backlog delivery;
4. add one shared `RetryGate` and route normal sender + Emergency HTTP rescue through it;
5. add Emergency payload-byte accounting/bound;
6. make shutdown delivery obey a real cancellation deadline;
7. extract `RelaySpool` / DTO files where that directly simplifies the changed code;
8. implement the 64 MiB spool cap only if it remains a contained robust change;
9. add/update the behavior tests above;
10. run the full existing SerilogRelay test suite and preserve current unrelated behavior.

Permanent bad-event handling remains a follow-up unless the implementation demonstrates that a
small fix is both necessary and obvious.

## Definition of done

This design pass is implemented when:

- intermittent clients no longer lose unsent backlog solely because it is old;
- low-volume clients eventually deliver without relying on shutdown;
- startup backlog gets an immediate delivery chance;
- endpoint failures are governed by one shared exponential RetryGate;
- successful recovery resumes normal delivery immediately;
- normal endpoint outages do not consume Emergency RAM;
- Emergency is bounded by event count and payload bytes;
- shutdown deadlines are real;
- any implemented spool-cap loss is explicit and does not masquerade as storage failure;
- the default one-line Serilog configuration remains valid;
- no speculative storage/provider/retry architecture was introduced.
