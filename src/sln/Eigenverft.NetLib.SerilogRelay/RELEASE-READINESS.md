# SerilogRelay Release Readiness

## Purpose

This document defines the supported v1 operating contract and the remaining release gates for
`Eigenverft.NetLib.SerilogRelay`.

Historical migration notes are useful background, but they are no longer the release contract.
The current implementation and tests are authoritative for behavior.

## Supported v1 operating contract

### One active sink per file-backed spool

Version 1 supports exactly **one active `SerilogRelaySink` instance per file-backed spool
path**.

This is enforced at runtime by an exclusive lease held for the lifetime of the sink. A second
sink/process attempting to use the same spool path fails immediately with a clear error. The
lease is released when the sink is disposed.

Multiple application processes may still use SerilogRelay when each process is configured with
a distinct spool path. Cross-process row claiming, shared sender state, and coordinated
corruption recovery for one shared spool are deliberately **not** part of the v1 contract.

This is a supported-scope decision, not an unfinished multi-process implementation.

### Receiver exposure before sender authentication

Bearer authentication is not implemented yet.

Until authentication is added, the supported receiver deployment is limited to a trusted
boundary:

- loopback/local-machine deployment;
- a private/trusted network;
- or a trusted reverse proxy/gateway that controls external access.

Do **not** expose an unauthenticated CentralLogging ingestion endpoint directly to an untrusted
or public network.

`ApplicationId`, `MachineId`, `ProcessId`, `EventId`, and other payload fields are
diagnostic/protocol identity only. They are not authenticated sender identity.

Normal platform TLS certificate validation remains enabled by default. The
`dangerousAcceptAnyServerCertificate` option is for deliberate development/private test
scenarios only and does not provide authentication.

### Delivery semantics

The relay/receiver contract is at-least-once transport with idempotent receiver storage:

- each event receives a stable `EventId`;
- retries may use a new `BatchId`;
- receiver-side duplicate handling uses `EventId`;
- success is expected only after receiver persistence succeeds.

The relay is fire-and-forget from the application logging call path, but durable local storage
is attempted before normal network delivery.

### Runtime targets

The package targets:

- `net8.0`
- `net10.0`

Both target frameworks must compile successfully for release. Release CI should execute tests
for every supported runtime available in the release environment.

## Current release gates

### Gate A - Operating contract and documentation

**Resolved by the current v1 contract.**

The package README, solution README, reliability documentation, and this document must all say
the same thing:

- one active sink per file-backed spool;
- no shared-spool multi-process support in v1;
- unauthenticated receivers stay inside a trusted boundary;
- producer identity fields are not authentication.

### Gate B - Bearer authentication

**Intentionally last.**

Bearer authentication is the remaining feature gate if v1 is expected to support direct
remote/external ingestion without relying on a trusted proxy boundary.

Until it is implemented, the private/proxy-protected receiver scope above is the supported
contract.

The authentication change should stay small:

- sender option for a bearer token/credential source;
- `Authorization: Bearer ...` on relay requests;
- receiver validation/configuration in `Eigenverft.Service.CentralLogging`;
- tests proving accepted, missing, and invalid credentials;
- no use of event/application identity as an authentication substitute.

Do not expand this into a general identity-provider framework unless a real deployment requires
one.

### Gate C - Final release validation

Before package publication:

1. clean restore/build;
2. test all supported target frameworks in CI/release environment;
3. run the repository's coverage gate;
4. `dotnet pack`;
5. inspect package contents/README;
6. perform one sender-to-current-CentralLogging smoke test using the supported deployment
   boundary;
7. after bearer auth is added, repeat the smoke test with authentication enabled.

This is release verification rather than a design blocker.

## Not release blockers for v1

The following are explicitly follow-up work unless deployment evidence changes their priority:

- shared-spool multi-process coordination/row leases;
- dedicated relay health/status API beyond current `SelfLog` diagnostics;
- alternate storage backends or ORM abstraction;
- server-driven relay configuration/handshake;
- process-instance/installation identifiers beyond the current producer identity;
- post-recovery rate limiting beyond current delivery bounds;
- a general dead-letter/per-event retry subsystem;
- exact process-RAM accounting for the Emergency memory buffer;
- public receiver exposure without bearer auth.

## Known limitation to watch

Permanent receiver rejections are not yet isolated into a dead-letter path. A persistently
rejected oldest batch can therefore remain pending and be retried. With the matched v1
CentralLogging contract this should represent a protocol/configuration defect rather than normal
operation.

This is worth addressing before broad heterogeneous receiver support, but it is not currently a
blocker for the controlled v1 relay/receiver pair.

## Historical origin

SerilogRelay was migrated from the discontinued AxonInsight `SQLiteSinkHttp` pattern. The
historical implementation remains useful background for why the relay is durable-first, but new
release decisions are defined here and in `RELIABILITY.md`, not by the archived implementation.
