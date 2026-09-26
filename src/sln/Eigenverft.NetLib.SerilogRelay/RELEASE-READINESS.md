# SerilogRelay Release Readiness

## Purpose

This document tracks the remaining release gates for `Eigenverft.NetLib.SerilogRelay`.

It must describe open work as open work. A missing capability must not be converted into a
narrower v1 promise merely to remove it from the blocker list.

## Current release gates

### Gate A - Shared application-spool multi-process support

**Core coordination implemented; policy semantics still open.**

Multiple processes of the same application can open/write the same default spool and now
coordinate delivery through atomic row claims with a 30-second lease.

Implemented behavior includes:

- stable `ProcessId` row-origin metadata without owner-only delivery restrictions;
- per-sink claim-owner identity that is not ambiguous under PID reuse;
- atomic claims with per-batch claim ids;
- lease expiry and takeover by another sender;
- stale claims cannot mark rows sent after takeover;
- graceful shutdown releases owned claims immediately;
- process-local `Delivery` and `EndpointRetry` behavior remains independent;
- each sender periodically discovers claimable rows written by other processes;
- existing spools are migrated to claim columns/indexes under serialized schema migration;
- physical corruption quarantine/recreate uses a short-lived cross-process recovery lock and
  rechecks whether another process already recovered the spool.

No leader process, application-wide RetryGate, application-wide delivery budget, or
lifetime-exclusive spool owner is used.

The remaining Gate A work is the application-spool policy contract, not claim mechanics:

- define semantics when processes sharing a spool configure different `ApplicationSpool`
  settings;
- decide whether `ApplicationSpool.MaxPhysicalBytes` remains only one shared physical ceiling,
  whether an additional per-process logical quota is required, or both;
- run the final release smoke test with separate OS processes as process-boundary validation.

A process is not required to send only rows it originally created. Draining backlog from an
older/dead process of the same logical application is supported behavior.

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

**Open until the remaining ApplicationSpool policy semantics are settled.**

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
