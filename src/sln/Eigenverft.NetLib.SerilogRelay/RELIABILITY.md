# SerilogRelay Reliability Contract

## Purpose

This document describes the current implemented reliability behavior of
`Eigenverft.NetLib.SerilogRelay` and makes option scope explicit.

The important distinction for multi-process work is:

- `ApplicationSpool` options act on the durable spool shared by processes that resolve to the
  same `ApplicationId` / spool path;
- `Delivery`, `EndpointRetry`, and `EmergencyMemoryBuffer` are runtime behavior of one
  sink/process.

## Configuration defaults

```text
SerilogRelayOptions
  ApplicationSpool
    MaxPhysicalBytes          = 64 MiB
    SentEventRetention        = 1 day
    UnsentEventMaxAge         = none

  Delivery
    MinimumBatchEvents        = 20
    MaximumBatchEvents        = 100
    PollInterval              = 5 seconds
    MaximumBatchWait          = 5 seconds

  EndpointRetry
    InitialDelay              = 5 seconds
    Multiplier                = 2
    MaximumDelay              = 5 minutes
    JitterRatio               = 0.20
    RespectRetryAfter         = true

  EmergencyMemoryBuffer
    MaxBufferedEvents         = 16384
    MaxBufferedPayloadBytes   = 64 MiB
```

The normal call remains:

```csharp
.WriteTo.SerilogRelay("https://logging.example/api/v1/logs")
```

## Application spool

The default file-backed spool path is application based:

```text
<LocalApplicationData>/Eigenverft/SerilogRelay/<ApplicationId>/SerilogRelay.db
```

Therefore multiple processes of the same logical application resolve to the same durable spool
unless the caller overrides the spool path.

The spool schema already records both `ApplicationId` and `ProcessId` on every event.
`ProcessId` therefore identifies which OS process originally created a row.

Multiple active sinks can open and persist into the same file-backed spool. There is no
lifetime-exclusive owner lock.

### Spool-wide retention semantics

`ApplicationSpool.SentEventRetention` and
`ApplicationSpool.UnsentEventMaxAge` are intentionally named as spool settings.

Current cleanup queries are spool-wide:

- sent-event retention may remove sent rows created by any process sharing the spool;
- an explicitly configured unsent maximum age may remove unsent rows created by any process
  sharing the spool.

Likewise, capacity reclamation is spool-wide:

1. reclaim oldest sent rows first;
2. if necessary, reclaim oldest unsent rows;
3. row origin / `ProcessId` does not restrict reclamation.

This means process A may clean or send rows originally created by process B when both belong to
the same application spool. That behavior is not treated as a problem by itself.

One unresolved configuration question remains: if processes sharing one application spool use
different `ApplicationSpool` values, there is currently no defined precedence/ownership rule.
That needs to be resolved as part of multi-process support.

### Physical capacity

`ApplicationSpool.MaxPhysicalBytes` currently means exactly what its name says: a physical
ceiling for the shared spool/database, not a per-process quota.

Default:

```text
MaxPhysicalBytes = 64 MiB
```

The current SQLite implementation enforces this through the physical database page budget.
Therefore all processes sharing the same spool also share this physical ceiling.

This is the main unresolved limit-scope question for multi-process operation. Before release we
must decide whether:

- the application spool intentionally has one shared physical ceiling;
- a per-process logical quota is also required;
- or both concepts are needed as separate settings.

Do not silently describe the current 64 MiB value as a per-process limit.

An incoming event that cannot fit even in an otherwise empty spool under the configured
physical ceiling is rejected before existing backlog is reclaimed.

An existing spool above a newly configured physical ceiling is not destructively purged during
startup.

## Delivery and process-local runtime limits

The following settings belong to the running sink/process:

- `Delivery.MinimumBatchEvents`;
- `Delivery.MaximumBatchEvents`;
- `Delivery.PollInterval`;
- `Delivery.MaximumBatchWait`;
- all `EndpointRetry` state/settings;
- all `EmergencyMemoryBuffer` limits;
- shutdown timing.

A sender may deliver rows originally produced by another process of the same application.
This is intentional and allows a surviving/new process to drain older backlog.

## Implemented multi-process claim/lease mechanics

Shared-spool delivery now uses row claims rather than uncoordinated reads. No leader process,
application-wide RetryGate, application-wide delivery budget, or lifetime-exclusive spool owner
is introduced.

### Row identity versus claim identity

`ProcessId` remains event-origin metadata: it records which OS process created a row.

Claim ownership is independent. Each sink instance creates an internal claim-owner id composed
from its current process id plus a new random instance component, so PID reuse does not make a
later sink look like the old claim owner.

Pending rows carry:

```text
Origin:
  ProcessId

Delivery coordination:
  ClaimOwnerId
  ClaimBatchId
  ClaimUntilUnixMs
```

Existing spool schemas are upgraded in place. Claim-column migration is serialized through an
immediate SQLite transaction, and claim lookup/ownership indexes are created idempotently.

### Atomic claim behavior

A sender atomically claims up to `Delivery.MaximumBatchEvents` from rows that are:

- unsent and unclaimed;
- unsent and already owned by the same sender instance; or
- unsent with an expired/missing lease timestamp.

The default internal claim lease is 30 seconds. It is a crash/active-delivery safety window,
not a retry-backoff timer.

A process may claim rows created by another process of the same application spool. Only rows
matching the current `ClaimOwnerId` and `ClaimBatchId` are marked sent after HTTP success.
If another process has already taken over an expired claim, the stale sender can no longer mark
that row sent.

Claims are leases, not permanent ownership:

- successful delivery marks the still-owned claim sent and clears claim metadata;
- an undersized batch that cannot yet satisfy the sender's minimum batch policy releases its
  claim immediately;
- a failed HTTP attempt releases the claim immediately; the failing process then observes its own
  process-local `EndpointRetry` delay without reserving that row;
- another sender whose own retry gate permits an attempt may claim that row immediately;
- if a process crashes or stalls while an active claim is still held, another sender may take over
  after the 30-second lease expires;
- graceful shutdown releases all claims owned by that sink immediately.

Receiver-side `EventId` idempotency remains the final protection for unavoidable at-least-once
duplicates, for example if a sender response races with lease expiry.

### Runtime-policy scope during takeover

The process currently sending a claimed row uses its own runtime configuration:

- its `Delivery` batch/cycle settings;
- its `EndpointRetry` state;
- its shutdown deadline.

The shared spool coordinates row ownership only; process-local runtime policies are not merged.
In particular, one process's exponential backoff or server-provided `Retry-After` does not become
a shared cooldown for the application spool.

### Shared-spool backlog discovery

Every sender cycle refreshes the count of rows that are claimable by that sender from the shared
spool. The existing fast in-process signal remains for rows written by the same sink, while the
normal `Delivery.PollInterval` provides bounded discovery of rows written by other processes.

A running sender can therefore discover and deliver backlog inserted later by another sink
without a separate cross-process notification subsystem.

### Recovery coordination

Physical corruption quarantine/recreate is coordinated separately from row claims. A short-lived
file-backed recovery lock serializes recovery for a shared spool. After acquiring the lock, the
process runs a SQLite quick check first; if another process already recovered the spool, no
second quarantine is performed.

Normal persistence, claiming, and sending are not serialized behind that recovery lock.

## Endpoint outage and RetryGate

Each sink/process owns its own in-memory `RetryGate`.

Default failed-attempt progression:

```text
5s -> 10s -> 20s -> 40s -> ... -> max 5m
```

with +/-20% jitter.

A valid HTTP `Retry-After` is honored. A successful delivery resets that process's retry state
immediately.

Normal endpoint outages remain in durable storage and do not consume Emergency memory.

## Emergency memory buffer

Emergency is process-local and used only when durable local persistence is unavailable.

Default bounds:

- 16384 events;
- 64 MiB serialized payload bytes.

The first reached bound wins.

Emergency direct-HTTP rescue uses the same `EndpointRetry` state as normal delivery in that
process.

## Corruption recovery

The current SQLite implementation can quarantine a corrupted spool and create a replacement.

Concurrent recovery of one shared application spool is coordinated by the short-lived recovery
lock described above. The lock is used only for quarantine/recreate and is automatically
released when its file handle closes; a dead process therefore does not leave a lifetime spool
owner behind.

## Shutdown

Shutdown is process-local and bounded by a real cancellation deadline.

Durable rows not sent before shutdown remain in the shared application spool. A later process
may send them.

## Remaining multi-process release questions

The functional shared-spool coordination layer is implemented: atomic claims, lease expiry and
takeover, cross-sink backlog discovery, concurrent legacy-schema migration, graceful claim
release, and cross-process corruption-recovery coordination are covered by regression tests.

The remaining questions are policy/contract questions rather than missing sender coordination:

1. explicit semantics when processes sharing one spool configure different
   `ApplicationSpool` settings;
2. final decision for `ApplicationSpool.MaxPhysicalBytes` versus any optional per-process
   logical quota;
3. final release validation should include a smoke test using separate OS processes in addition
   to the in-process multi-sink concurrency regression suite.

`ProcessId` remains available to distinguish row origin where useful. It is not an ownership
barrier: another process may legitimately send older rows from the same application spool.

## Receiver semantics

The matched receiver is `Eigenverft.Service.CentralLogging` at `POST /api/v1/logs`.

Transport is at-least-once and receiver storage is idempotent by `EventId`.

Authentication/deployment scope is tracked separately in `RELEASE-READINESS.md`.
