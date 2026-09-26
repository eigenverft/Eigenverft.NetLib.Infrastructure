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

A sender currently loads unsent rows from the shared spool without filtering on `ProcessId`.
Therefore a process may deliver rows originally produced by another process of the same
application. This is useful for draining older backlog.

The receiver deduplicates by stable `EventId`, but the sender currently has no row-claim state.
Two processes can therefore select the same unsent row concurrently. Proper multi-process
claiming/lease semantics are still a release blocker.

The in-memory pending counter is also process-local. A process does not currently receive an
automatic signal when another process adds rows to the shared spool. This matters for failover:
if the producing process exits, another already-running process may not immediately discover the
orphaned backlog without additional shared-spool polling/claim logic.

## Target multi-process claim/lease mechanics

The target direction for closing the shared-spool sender race is deliberately small and does
not introduce a leader process or application-wide runtime policy state.

### Row identity versus claim identity

`ProcessId` remains the origin of an event: it tells which OS process created the row.

Claim ownership is a different concept. A claim should identify the currently sending runtime
instance, for example through an internal claim-owner id that may include/process-correlate with
`ProcessId` but is safe against PID reuse.

Conceptually each pending row needs enough state for:

```text
Origin:
  ProcessId

Delivery coordination:
  ClaimOwnerId
  ClaimUntilUtc
```

The exact column names/representation may be chosen during implementation; the behavioral
contract matters more than the storage spelling.

### Claim behavior

A sender should atomically claim up to its normal `Delivery.MaximumBatchEvents` from rows that
are:

- unsent and unclaimed; or
- unsent with an expired claim.

Only the process that successfully acquires that claim sends that batch.

A process may claim rows created by another process of the same application spool. This is
intentional: a surviving/new process can drain backlog left behind by an older/dead process.

Claims are leases, not permanent ownership:

- a successful send marks the claimed rows sent;
- a failed/transient delivery releases the claim or allows it to expire according to the
  implementation chosen;
- if a process dies, `ClaimUntilUtc` eventually makes those rows claimable again;
- no row is permanently tied to the process that originally created it.

### Runtime-policy scope during takeover

The process that currently sends a claimed row uses **its own** runtime configuration:

- its `Delivery` batch/cycle settings;
- its `EndpointRetry` state;
- its shutdown deadline.

There is no shared application-level RetryGate or shared delivery-rate state.

The shared spool coordinates which rows are being delivered; it does not merge process-local
runtime policies.

### Backlog discovery

Claiming also needs a cross-process discovery path.

A sender cannot rely only on its in-memory `_pendingCount`, because another process may add
rows after this process last inspected the spool. The final implementation should therefore
periodically consult the shared spool for claimable work, while retaining the current fast local
signal for rows written by the same process.

The polling/discovery mechanism should remain bounded and simple; it does not need a separate
cross-process notification service.

### Recovery coordination

Corruption recovery is different from normal row claiming because it mutates/replaces the
physical spool itself.

The target is a short-lived cross-process recovery lock used only around quarantine/recreate
operations. Normal persistence, claiming, and sending should not be serialized behind a
lifetime/global file lock.

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

That recovery path is coordinated only inside one process today. Concurrent corruption
recovery against one shared application spool is therefore still part of the multi-process
release blocker.

## Shutdown

Shutdown is process-local and bounded by a real cancellation deadline.

Durable rows not sent before shutdown remain in the shared application spool. A later process
may send them.

## Current multi-process release gaps

Shared-spool multi-process support is not considered finished yet. The remaining concrete gaps
are:

1. atomic batch/event claiming so concurrent senders do not select the same rows;
2. claim expiry/recovery behavior after a process dies;
3. timely discovery of backlog written by another process;
4. cross-process corruption-recovery coordination;
5. explicit semantics when processes sharing a spool configure different
   `ApplicationSpool` settings;
6. final decision for `ApplicationSpool.MaxPhysicalBytes` versus any optional per-process
   logical quota.

`ProcessId` is already persisted and can be used to distinguish row origin where useful.
No owner-only delivery rule is implied: another process may legitimately send older rows from
the same application spool.

## Receiver semantics

The matched receiver is `Eigenverft.Service.CentralLogging` at `POST /api/v1/logs`.

Transport is at-least-once and receiver storage is idempotent by `EventId`.

Authentication/deployment scope is tracked separately in `RELEASE-READINESS.md`.
