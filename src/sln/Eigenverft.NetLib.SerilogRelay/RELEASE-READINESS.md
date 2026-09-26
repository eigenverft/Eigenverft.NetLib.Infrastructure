# SerilogRelay Release Readiness

## Purpose

This document tracks the remaining release gates for `Eigenverft.NetLib.SerilogRelay`.

It must describe open work as open work. A missing capability must not be converted into a
narrower v1 promise merely to remove it from the blocker list.

## Current release gates

### Gate A - Shared application-spool multi-process support

**Open release blocker.**

Multiple processes of the same application resolve to the same default spool path. Multiple
active sinks can open and write that spool; SerilogRelay must therefore support this as a normal
operating mode rather than requiring distinct spool files.

Current useful behavior already exists:

- each row contains `ApplicationId`, `MachineId`, and `ProcessId`;
- multiple active sinks can persist into the same file-backed spool;
- any sender may currently load/send unsent rows from that spool;
- receiver idempotency is based on stable `EventId`.

The remaining work is coordination/correctness, not a single-owner restriction:

- atomic claims/leases for concurrent senders;
- expired-claim recovery after process death;
- discovery of rows added by other processes;
- cross-process corruption-recovery coordination;
- clear semantics for differing `ApplicationSpool` settings across processes.

A process is not required to send only rows it originally created. Draining backlog from an
older/dead process of the same logical application is useful behavior.

#### Shaped implementation direction

The current target for Gate A is:

1. add atomic claim/lease state to pending rows;
2. let any process sharing the application spool claim unclaimed or expired rows;
3. keep `ProcessId` as event-origin metadata rather than an ownership restriction;
4. identify the current claim owner independently enough to avoid PID-reuse ambiguity;
5. let claims expire after process death so another process can continue the backlog;
6. keep `Delivery` and `EndpointRetry` process-local to the sender that currently owns the
   claim;
7. periodically inspect the shared spool for claimable work so one process can discover backlog
   produced by another;
8. coordinate physical corruption quarantine/recreate with a short-lived cross-process recovery
   lock only.

This deliberately avoids a leader process, application-wide RetryGate, application-wide
delivery budget, or a lifetime-exclusive spool owner.

The exact claim columns/SQL transaction shape remain implementation details to be finalized
when Gate A is implemented and tested.

#### Application-spool limit scope

Retention/reclaim settings are spool-wide and are named accordingly:

- `ApplicationSpool.SentEventRetention`;
- `ApplicationSpool.UnsentEventMaxAge`.

The main unresolved limit question is:

```text
ApplicationSpool.MaxPhysicalBytes = 64 MiB
```

Today this is a physical ceiling for the whole shared spool. It is not a per-process quota.

Before release we must decide whether the product contract needs:

- one shared physical application-spool ceiling;
- an additional logical per-process quota;
- or both as separate concepts.

This decision must be explicit in API naming and documentation.

### Gate B - Operating-contract/documentation consistency

**Open until Gate A semantics are settled.**

README, XML docs, tests, and `RELIABILITY.md` must use the same scope vocabulary:

- Application spool = shared durable storage for processes using the same spool path;
- Delivery / EndpointRetry / EmergencyMemoryBuffer = process-local runtime behavior;
- `ProcessId` = row origin/diagnostic process identity, not an ownership barrier.

### Gate C - Bearer authentication

**Intentionally last.**

Bearer authentication remains the final security feature after the multi-process/storage
contract is settled.

Until bearer authentication is implemented, an ingestion endpoint must remain inside a trusted
boundary such as loopback/private network or behind a trusted proxy/gateway.

Producer identity fields are not authentication.

### Gate D - Final release validation

After functional blockers are closed:

1. clean restore/build;
2. execute supported target-framework tests in release CI;
3. pass repository coverage gates;
4. `dotnet pack`;
5. inspect package contents and package README;
6. smoke-test sender -> current CentralLogging receiver;
7. repeat the smoke test with bearer authentication once Gate C is implemented.

## Not currently release blockers

Unless new evidence changes priority:

- alternate storage backends / ORM abstraction;
- a general provider framework;
- richer health/status APIs beyond current diagnostics;
- server-driven configuration/handshake;
- post-recovery global rate limiting;
- a general dead-letter subsystem for heterogeneous receivers;
- exact process-RSS accounting for Emergency memory.

## Known follow-up

A permanently rejected oldest event/batch is not yet isolated into a dead-letter path. This
remains worth addressing before broad heterogeneous receiver support, but it is separate from
the current shared-spool multi-process blocker.

## Historical origin

SerilogRelay preserves the durable-first behavior of the discontinued AxonInsight
`SQLiteSinkHttp` pattern. Current decisions are defined by the implementation, tests,
`RELIABILITY.md`, and this release-readiness document.
