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
    SentEventRetention        = 0 (delete acknowledged rows immediately)
    UnsentEventMaxAge         = none

  Delivery
    MinimumBatchEvents        = 20
    MaximumBatchEvents        = 100
    TargetBatchPayloadBytes   = 4 MiB
    RequestTimeout            = 2 seconds
    EmergencyMaximumBatchEvents      = 256
    EmergencyTargetBatchPayloadBytes = 4 MiB
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

  StatusEvents
    Mode                      = Off
    MinimumLevel              = Warning
    SummaryInterval           = none
```

The normal call remains:

```csharp
.WriteTo.SerilogRelay("https://logging.example/api/v1/logs")
```

`StatusEvents` is a process-local operational output. `RelayOnly` stores and sends marked
status events through this sink; `AllSinks` publishes them through the application logger;
`AllSinks` requires a `LoggerProvider` that returns null until that logger is ready;
`Off` leaves only Serilog SelfLog diagnostics. Status events use the same spool, emergency RAM
buffer, batching, and shutdown delivery as application events. On admission they may reclaim sent
spool rows, but do not evict waiting spool or RAM events. They still occupy capacity, and later
normal reclamation can evict eligible unsent rows with a loss report. If both buffers reject a
`RelayOnly` status transition, it remains pending for retry. Warnings cover outages, recovery,
and pressure; errors
summarize event loss. An optional Debug summary has its own interval. Dropped status events in
RAM remain in cumulative counters but do not trigger another status event.
Known state transitions wake the status worker immediately and remain ordered in a bounded
64-entry queue; further rapid changes are summarized with counts and first/last timestamps.
The shared SQLite page budget is sampled every 30 seconds because other processes can change it.
Pending transitions are volatile until publication; process shutdown can lose them.
In `RelayOnly`, failed spool and RAM admission leaves the transition pending for retry.

## Application spool

The default file-backed spool path is application based:

```text
<LocalApplicationData>/Eigenverft/SerilogRelay/<ApplicationId>/SerilogRelay.db
```

Therefore multiple processes of the same logical application resolve to the same durable spool
unless the caller overrides the spool path.

The spool records `ApplicationId` and `ProcessId` on every event. New rows also record the
originating application's `ApplicationVersion`, resolved once from the entry assembly when the
sink is created or supplied through the options. Existing rows retain a null version after the
schema upgrade. A later sender does not substitute its own version for an older row.
`ProcessId` identifies which OS process originally created a row.

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

`ApplicationSpool.MaxPhysicalBytes` sets a SQLite page budget for the shared database.
It is not a quota for rows belonging to one process or a hard limit on all files.

Default:

```text
MaxPhysicalBytes = 64 MiB
```

The SQLite implementation applies the configured page budget when a process opens the
shared spool. Different processes may configure different values; this is intentionally not
resolved through cross-process policy negotiation.

An incoming event that cannot fit even in an otherwise empty spool under the configured
page budget is rejected before existing backlog is reclaimed.

An existing spool above a newly configured page budget is not destructively purged during
startup.

## Delivery and process-local runtime limits

The following settings belong to the running sink/process:

- `Delivery.MinimumBatchEvents`;
- `Delivery.MaximumBatchEvents`;
- `Delivery.TargetBatchPayloadBytes`;
- `Delivery.EmergencyMaximumBatchEvents`;
- `Delivery.EmergencyTargetBatchPayloadBytes`;
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
  ApplicationVersion (nullable on older rows)
  ProcessId

Delivery coordination:
  ClaimOwnerId
  ClaimBatchId
  ClaimUntilUnixMs
```

Existing spool schemas are upgraded in place. Claim-column and application-version migration is
serialized through an immediate SQLite transaction, and claim lookup/ownership indexes are
created idempotently.

### Atomic claim behavior

A sender atomically claims up to `Delivery.MaximumBatchEvents` from rows that are:

- unsent and unclaimed;
- unsent and already owned by the same sender instance; or
- unsent with an expired/missing lease timestamp.

The default internal claim lease is 30 seconds. It is a crash/active-delivery safety window,
not a retry-backoff timer.
Its clock starts after the SQLite write lock is acquired. Immediately before HTTP delivery,
after preparing the payload, the sender verifies ownership of the entire batch and renews the
lease. A lost or incomplete claim is not sent. The request deadline is also bounded by the
remaining renewed lease, with a one-second margin; a renewal that leaves no usable time skips
the HTTP attempt. A renewal failure is reported as a spool failure rather than an endpoint failure.

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
- its HTTP client, authentication, and request timeout;
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

Startup generation selection and corruption recovery share one short-lived coordination lock
at `<base-spool-filename>.recovery.lock`, independent of row claims. Its small versioned record
contains the latest generation number and OS boot identifier, so cleanup needs no additional
metadata file or SQL table. Marker replacement first flushes an empty record, then writes and
flushes the replacement; interrupted or mismatched records cannot authorize cleanup.

Normal persistence, claiming, and sending are not serialized behind that recovery lock. A local
filesystem with working cross-process file locks is required for coordinated access.

## Endpoint outage and RetryGate

Each sink/process owns its own in-memory `RetryGate`.

Retry settings are captured when the sink is created. The multiplier must be finite and at
least one; jitter must be finite and between zero and one.

Default failed-attempt progression:

```text
5s -> 10s -> 20s -> 40s -> ... -> max 5m
```

with +/-20% jitter.

A valid HTTP `Retry-After` is honored on every non-2xx response. A successful delivery resets that process's retry state
immediately.

Response bodies are read and discarded as they arrive instead of being buffered in full.
The client's `MaxResponseContentBufferSize` still limits the complete response size, even for
an unknown-length body. Request, client, lease, and shutdown deadlines remain active through
the body. A batch is acknowledged only after a complete 2xx response within these limits.

Normal endpoint outages remain in durable storage and do not consume Emergency memory.

## Emergency memory buffer

Emergency is process-local and used only when durable local persistence is unavailable.

Default bounds:

- 16384 events;
- 64 MiB serialized payload bytes.

The first reached bound wins.

Emergency direct-HTTP rescue uses the same `EndpointRetry` state as normal delivery in that
process.

During normal operation, the worker retries durable storage for buffered events before direct
HTTP. If storage fails for one event, the rest of that selected batch proceeds to direct HTTP
without repeating the same failing storage operation. It sends only events still in RAM in batches of up to
`Delivery.EmergencyMaximumBatchEvents` and the separate
`Delivery.EmergencyTargetBatchPayloadBytes` JSON target. The defaults are 256 events and 4 MiB.
There is no minimum count or wait to fill a direct emergency batch. The worker sends another
bounded batch immediately after success. One event larger than the target can be sent alone.
Failed batches remain in RAM for retry and become eligible for oldest-waiting eviction after the
active attempt ends. RAM and spool events are never mixed in one HTTP batch.

## Corruption recovery

For ordinary storage failures, the selected generation remains fixed and database operations
continue to retry. If initial schema creation or migration failed, the next database operation
retries initialization under the same process-local gate before accessing that schema. Delayed
initialization still preserves the startup delivery opportunity before unsent-age cleanup.
Storage failures from delivery, maintenance, and diagnostic access share the Emit/Emergency
availability state. Successful writes (or successful schema initialization) mark recovery;
reads alone do not establish that writes can resume. Confirmation of the exact EventId after an
ambiguous write also establishes that the affected event became durable. A capacity rejection
that leaves the event only in RAM does not establish recovery.

Each instance selects the highest numeric generation once at startup: the configured base
filename is generation zero, followed by `.g0001`, `.g0002`, etc. before its extension. The
connection string is fixed for that instance; healthy older instances are not forced to switch.

A first `SQLITE_CORRUPT` or `SQLITE_NOTADB` sets a permanent process-visible flag on the
affected sink instance. All its SQLite access stops, including emergency repersistence,
claiming/acknowledgment, maintenance, diagnostic queries, and shutdown spool draining. Its RAM
buffer retains the existing admission, batching, retry, and shutdown rules. Already active HTTP
requests may finish; the disabled instance does not acknowledge them through SQLite.

Under the recovery lock, the instance reserves the next generation with create-new semantics,
or accepts that another process already reserved a higher generation. The new filename is an
empty database; a new instance initializes its normal schema. The detecting instance remains in
RAM mode even after creating the successor. Failures to reserve it are diagnosed through
`SelfLog`, while RAM delivery remains available. Other SQLite errors keep their normal retry
behavior. No database or WAL/SHM file is moved, replaced, or salvaged in a running boot session.

At startup, a valid marker matching the highest generation and a different current OS boot
identifier permits deletion of lower generations and their WAL/SHM files under the same lock.
The helper uses the Windows native boot-environment query, Linux
`/proc/sys/kernel/random/boot_id`, or macOS `kern.bootsessionuuid`. Missing, incomplete, or
mismatched records are conservatively initialized for the current boot and defer deletion until
a later boot. Unavailable boot information skips deletion without preventing spool use.

Cleanup does not infer reboot from wall-clock or filesystem timestamps. Participating versions
must all follow generation selection; binaries predating this protocol are not coordinated by
it. Corrupt older data may be lost, and retained obsolete generations are outside the active
database's configured size budget. Startup coordination/selection failure leaves that instance
in RAM mode until it is replaced, rather than risking access to a stale generation.

## Shutdown

Shutdown is process-local and bounded by a real cancellation deadline.

Durable rows not sent before shutdown remain in the shared application spool. A later process
may send them.

With an endpoint, shutdown first lets an already active normal request finish, then sends direct
RAM batches without another SQLite persistence attempt, and finally sends separate claimed spool
batches. RAM uses the emergency count and JSON
targets; spool uses the normal count and JSON targets. Neither source waits for a minimum batch
count during shutdown.

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
