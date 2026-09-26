# SerilogRelay Reliability Contract

## Purpose

This document describes the **current implemented reliability behavior** of
`Eigenverft.NetLib.SerilogRelay`.

It is not a future design backlog. Defaults and guarantees below should stay aligned with the
public options and regression tests.

## Configuration defaults

```text
SerilogRelayOptions
  LocalStorage
    MaxBytes                 = 64 MiB
    SentRetention            = 1 day
    UnsentMaxAge             = none

  Delivery
    MinimumBatchEvents       = 20
    MaximumBatchEvents       = 100
    PollInterval             = 5 seconds
    MaximumBatchWait         = 5 seconds

  EndpointRetry
    InitialDelay             = 5 seconds
    Multiplier               = 2
    MaximumDelay             = 5 minutes
    JitterRatio              = 0.20
    RespectRetryAfter        = true

  EmergencyMemoryBuffer
    MaxBufferedEvents        = 16384
    MaxBufferedPayloadBytes  = 64 MiB
```

The normal call remains simple:

```csharp
.WriteTo.SerilogRelay("https://logging.example/api/v1/logs")
```

## Durable local storage

Normal operation is durable-first:

1. materialize the Serilog event;
2. assign its stable `EventId`;
3. persist it to the local spool;
4. deliver pending rows in the background;
5. mark successfully accepted rows as sent.

Unsent rows do not expire by age by default. Already-sent rows use the configured sent
retention.

The default local-storage budget is 64 MiB. It is a ceiling, not pre-allocation.

When capacity is reached, reclaim prefers already-sent data before unsent backlog. If unsent
rows must be removed, oldest unsent rows are reclaimed first and loss is reported.

An incoming event that cannot fit even in an otherwise empty spool with the same configured
budget is rejected **before** existing backlog is reclaimed. One oversized event therefore does
not destroy already-retained unsent logs.

An existing spool that is larger than a newly configured budget is not purged at startup; it is
grandfathered and can shrink naturally through normal delivery/cleanup.

Configured capacity rejection is not treated as storage failure and does not spill into the
Emergency memory buffer.

## One active sink per spool

A file-backed spool has one active owner in v1.

The sink holds an exclusive lease for the spool lifetime. A second active sink/process using
the same spool path is rejected immediately. After the first sink is disposed, another sink may
open the same spool and continue the backlog.

Cross-process shared-spool coordination is not supported in v1. Use distinct spool paths for
parallel processes.

## Low-volume and startup delivery

`MinimumBatchEvents` is an efficiency target, not a delivery requirement.

A partial batch is eligible after `MaximumBatchWait`, so low-volume clients do not depend on
process shutdown to send 1-19 pending events.

Existing backlog at process startup receives an immediate delivery opportunity. Explicit
non-default unsent-age cleanup does not run first and destroy that backlog before the reconnect
attempt.

## Endpoint outage and RetryGate

Normal endpoint outages remain in durable local storage; they do not consume Emergency memory.

One shared in-memory `RetryGate` controls endpoint attempts for both normal delivery and
Emergency direct-HTTP rescue.

The default failed-attempt progression is approximately:

```text
5s -> 10s -> 20s -> 40s -> ... -> max 5m
```

with +/-20% jitter.

A valid HTTP `Retry-After` is honored as a not-before time. The effective next attempt is the
later of exponential backoff and `Retry-After`.

A successful delivery resets retry state immediately. There is no additional post-recovery
cooldown/token bucket; backlog resumes at normal delivery throughput.

Retry state is intentionally not persisted. A process restart may probe the endpoint
immediately and then re-enter normal backoff if it remains unavailable.

## Emergency memory buffer

Emergency buffering is only for **local durable-persistence failure**.

Invariant:

```text
endpoint unavailable + local spool healthy
=> no Emergency memory use
```

The Emergency buffer is bounded by both:

- 16384 events by default;
- 64 MiB serialized payload bytes by default.

The first reached bound wins. Payload-byte accounting is not an exact process-RSS guarantee.

The Emergency worker retries local persistence first. If persistence remains unavailable and an
endpoint exists, direct HTTP rescue may be attempted, but it must use the same shared
`RetryGate` as normal delivery.

Buffer overflow and unresolved volatile events at shutdown are surfaced through diagnostics;
RAM growth is not unbounded.

## Storage corruption and local failures

The current SQLite implementation retains its contained corruption-recovery behavior for
`SQLITE_CORRUPT` / `SQLITE_NOTADB`: the affected file-backed spool is quarantined and a
fresh spool is created.

Other local failures are not misclassified as corruption. Where local persistence is
unavailable, new events may enter Emergency memory subject to its hard bounds.

Storage-engine details are implementation internals, not public policy names.

## Shutdown

Disposal is idempotent across synchronous/asynchronous callers.

Shutdown:

- stops accepting new events;
- gives Emergency volatile entries a bounded drain opportunity;
- cancels background workers;
- attempts a bounded durable-backlog flush when an endpoint exists;
- passes a real cancellation deadline through shutdown delivery work.

Durable rows that are not sent before the deadline remain in the spool for the next process
start. Volatile Emergency rows that cannot be resolved before shutdown may be lost and are
diagnosed explicitly.

## Receiver semantics

The matched v1 receiver is `Eigenverft.Service.CentralLogging` at `POST /api/v1/logs`.

The transport is at-least-once. Receiver storage is idempotent by `EventId`; `BatchId` is
per-attempt correlation.

Producer `ApplicationId`, `MachineId`, and `ProcessId` are diagnostic identity and must not
be treated as authenticated identity.

Authentication/deployment scope is defined in `RELEASE-READINESS.md`.

## Known follow-up

A permanently rejected oldest event/batch is not yet isolated into a dead-letter path. Repeated
permanent 4xx responses can therefore keep that backlog pending. This is a follow-up for broader
receiver heterogeneity; it is not part of the current controlled v1 sender/receiver reliability
contract.
