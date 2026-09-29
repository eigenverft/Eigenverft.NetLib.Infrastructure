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

### Spool-wide retention and capacity semantics

`ApplicationSpool.SentEventRetention`, `ApplicationSpool.UnsentEventMaxAge`, and
`ApplicationSpool.MaxPhysicalBytes` are intentionally spool settings.

Their values are configured independently by each sink/process. Processes sharing a spool do
not negotiate, merge, persist, or elect one authoritative `ApplicationSpool` configuration.
When one process performs maintenance or capacity reclamation, that process applies its own
configured values to the shared spool.

Consequently:

- sent-event retention may remove eligible sent rows created by any process sharing the spool;
- an explicitly configured unsent maximum age may remove eligible unsent rows created by any
  process sharing the spool;
- capacity reclamation may reclaim eligible rows created by any process;
- row origin / `ProcessId` does not create a retention or capacity quota boundary.

Active delivery claims are an exception: an unsent row with a still-active claim is not removed
by `UnsentEventMaxAge` cleanup or unsent capacity reclamation. Once that claim is released or
expires, the row becomes eligible again under whichever process next applies its local spool
policy.

`UnsentEventMaxAge` is enforced by periodic application-spool maintenance while the sink is
running, including when no endpoint is configured and while a configured endpoint is in retry
backoff. With an endpoint, startup backlog still receives its established initial delivery
opportunity before the first unsent-age cleanup. The configured age is therefore the eligibility
threshold, not a promise that physical deletion occurs at the exact instant the age is crossed;
maintenance cadence and an active claim can defer removal. The current maintenance loop is
scheduled at the shorter of this process's `Delivery.PollInterval` and one minute; transient
database/recovery failures can defer a pass further.

`ApplicationSpool.MaxPhysicalBytes` is the physical ceiling applied to the shared
spool/database. It is not a quota for rows belonging to one process.

Default:

```text
MaxPhysicalBytes = 64 MiB
```

The SQLite implementation applies the configured physical page budget when a process opens the
shared spool. Different processes may configure different values; this is intentionally not
resolved through cross-process policy negotiation.

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

Transport remains at-least-once: if a sender cannot know whether a response was received, the
same logical event may be delivered again. How a receiver stores or presents repeated
`EventId` values is receiver-side behavior, not a requirement of this sink.

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

A valid HTTP `Retry-After` is honored on every non-2xx response. A successful delivery resets that process's retry state
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

## Shared-spool cross-version operating contract

Old and new versions of the same logical application may use the same spool concurrently.

Pending rows are not bound to the endpoint or bearer token of the process that created them. The
process that currently owns a claim sends those rows using its own configured endpoint and
bearer token.

A non-2xx response releases the claim before that process enters its own retry delay. A different
process/version can therefore claim the same row and attempt delivery through different current
credentials or a different endpoint. A 2xx response received by the current claim owner marks
the claimed row delivered regardless of which process originally wrote it.

This exact shape is covered by a separate-OS-process regression: the old sender remains alive
after receiving non-2xx, its claim is released, and a second process sends the same `EventId`
with its own endpoint/token and marks it sent after 2xx.

`ProcessId` remains useful row-origin metadata. It is not an ownership, endpoint, retention, or
capacity boundary.

## Dead-letter direction

The sink intentionally has no dead-letter path. A non-2xx response does not permanently classify
or move the row; it remains unsent for later delivery attempts or takeover by an updated
application version.

Those rows are still subject to the configured shared-spool bounds:
`ApplicationSpool.UnsentEventMaxAge` and capacity reclamation.

## Receiver contract

The sink targets a generic HTTP receiver rather than one mandatory server implementation.

For normal delivery, any HTTP 2xx received by the current claim owner is treated as successful
delivery. Non-2xx or transport failure leaves the row unsent and releases its claim before the
process-local retry delay.

Receiver persistence, duplicate presentation, and server-side storage policies remain receiver
concerns. Optional bearer authentication only controls the Authorization header emitted by the
sending process.
