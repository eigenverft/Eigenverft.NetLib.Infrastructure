# SerilogRelay Release Readiness

## Purpose

This document tracks the remaining release gates for `Eigenverft.NetLib.SerilogRelay`.

It must describe open work as open work. A missing capability must not be converted into a
narrower v1 promise merely to remove it from the blocker list.

## Current release gates

### Gate A - Shared application-spool multi-process support

**Implemented and process-boundary validated.**

Multiple processes of the same application can open/write the same default spool and coordinate
delivery through atomic row claims with a 30-second lease.

The operating contract is:

- `ProcessId` records row origin; it is not an ownership barrier.
- Pending rows are not bound to the endpoint or bearer token of the process that created them.
- The process that currently claims a row sends it with that process's own endpoint and bearer
  token.
- A non-2xx response releases the claim before that process enters its local retry delay, allowing
  another process/version to take over immediately.
- A 2xx response received by the current claim owner marks those rows delivered, even when they
  were originally written by another process/version.
- Lease expiry permits takeover after process death/stall; stale claim owners cannot later mark a
  taken-over row sent.
- Graceful shutdown releases owned claims immediately.
- Each sender periodically discovers claimable rows written by other processes.
- Existing spools are migrated to claim columns/indexes under serialized schema migration.
- Physical corruption quarantine/recreate uses separate short-lived cross-process recovery
  coordination.

A separate-OS-process regression now covers the cross-version shape directly: an old process
sends through an old endpoint/token and receives non-2xx, releases the row, remains alive, and a
second process sharing the spool sends the same `EventId` through its own endpoint/token and
marks it delivered after 2xx.

No leader process, application-wide RetryGate, application-wide delivery budget, shared
`Retry-After` cooldown, or lifetime-exclusive spool owner is used.

#### Application-spool configuration scope

`ApplicationSpool` settings are configured by each sink/process. Processes sharing a spool do
not negotiate, merge, elect, or persist one authoritative configuration.

Each process applies its own configured values when it performs spool-wide work. Consequently:

- `SentEventRetention` cleanup can remove eligible sent rows created by any process.
- `UnsentEventMaxAge` cleanup can remove eligible unsent rows created by any process.
- capacity reclamation can reclaim eligible rows created by any process.
- `MaxPhysicalBytes` is the physical ceiling applied to the shared spool/database; it is not a
  quota for rows belonging to the configuring process.

Active unsent delivery claims are protected from age cleanup and capacity reclamation. Once a
claim is released or expires, the row is again eligible for the locally configured spool-wide
policies.

`UnsentEventMaxAge` is enforced during periodic application-spool maintenance while the sink is
running. The configured age is the eligibility threshold; actual deletion can occur on the next
maintenance pass and is deferred while a row has an active delivery claim.

This intentionally permits two versions sharing a spool to use different local
`ApplicationSpool` values. Whichever process performs cleanup/reclamation applies its values to
the shared spool. There is no additional per-process physical quota.

### Gate B - Operating-contract/documentation consistency

**Implemented for the current shared-spool contract.**

README, XML docs, tests, and `RELIABILITY.md` use the same scope vocabulary:

- Application spool = shared durable storage for processes using the same spool path.
- `ApplicationSpool` values are process-local configuration with spool-wide effects.
- `Delivery`, `EndpointRetry`, endpoint/bearer configuration, and
  `EmergencyMemoryBuffer` are process-local runtime behavior.
- `ProcessId` = row origin/diagnostic process identity, not delivery ownership.

### Gate C - Bearer authentication

**Implemented.**

SerilogRelay accepts an optional raw `bearerToken` parameter and sends it as
`Authorization: Bearer <token>`.

The token belongs to the sending sink/process. A process draining shared-spool rows uses its own
configured token; rows do not persist or inherit credentials from the process that created them.

No token configured means no Authorization header is emitted. The token is opaque; SerilogRelay
does not parse JWT claims, perform token refresh, or require a specific receiver implementation.

Producer identity fields are not authentication.

### Gate D - Final release validation

The functional shared-spool/authentication blockers above are implemented. Remaining release
work is the normal final validation pass:

1. clean restore/build;
2. execute supported target-framework tests in release CI;
3. pass repository coverage gates;
4. `dotnet pack`;
5. inspect package contents and package README;
6. perform the intended end-to-end sender -> deployed receiver smoke for the release environment.

The separate-OS-process shared-spool takeover smoke is already part of the regression suite.

## Intentional non-features / deferred work

Unless new evidence changes priority:

- alternate spool storage backends / ORM abstraction;
- a general provider framework;
- richer health/status APIs beyond current diagnostics;
- server-driven configuration/handshake;
- post-recovery global rate limiting;
- exact process-RSS accounting for Emergency memory.

A dead-letter path is intentionally not part of the current sink design. Non-2xx deliveries remain
unsent so a later retry or updated application version can deliver them, subject to configured
`UnsentEventMaxAge` and shared-spool capacity reclamation.

## Historical origin

SerilogRelay preserves the durable-first behavior of the discontinued AxonInsight
`SQLiteSinkHttp` pattern. Current decisions are defined by the implementation, tests,
`RELIABILITY.md`, and this release-readiness document.
