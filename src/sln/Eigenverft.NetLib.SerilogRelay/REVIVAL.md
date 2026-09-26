# Revival notes

## Historical behavior worth preserving

The archived AxonInsight `SQLiteSinkHttp` was more than an HTTP sink. Its local SQLite database was the durable sender spool.

Historical source reference:

`personal-archive-issues/Discontinued/AxonInsight/src/AxonInsight.Client/AxonInsight.Installer/AxonInsight.Installer.Projects/AxonInsight.Library/Extensions/LoggerSinkConfigurationExtensions/SQLiteSinkHttp.cs`

The old flow was:

1. Serilog calls `Emit`.
2. The event is written to local SQLite with `Sent = 0`.
3. A sender loop loads unsent rows and posts them in batches.
4. Successful batches are marked sent.
5. Pending rows remain available across network failures and process restarts.

That durable-first shape is the core reason to revive the library.

The revival was originally scaffolded as `Eigenverft.NetLib.SerilogCentralLoggingSink` and now continues as `Eigenverft.NetLib.SerilogRelay`.

## Current migrated state

The historical sender implementation has now been functionally migrated into:

`src/prj/Eigenverft.NetLib.SerilogRelay/SerilogRelaySink.cs`

The archived AxonInsight source was treated as read-only and was not modified. It was already available as an extracted directory rather than a ZIP archive, so no temporary extraction directory was required in the working repository.

The initial migration deliberately avoided a protocol redesign, keeping the SQLite spool, background sender, batching, retention, retry, and shutdown-flush behavior recognizable from the archive. The subsequent F7 hardening introduces the first intentional wire evolution: protocol version `1` adds stable event identity for idempotent receiver ingestion while retaining the historical event fields. Before the first package release, the public naming was also aligned with the new package: the sink type is `SerilogRelaySink` and the Serilog configuration entry point is `.WriteTo.SerilogRelay(...)`.

Only small compatibility and correctness adaptations were made while bringing the code into the current library:

- namespace moved to `Eigenverft.NetLib.SerilogRelay`;
- target frameworks are `net8.0` and `net10.0`;
- Serilog was updated to the current stable `4.4.0`;
- `Microsoft.Data.Sqlite` was updated from the archived `9.0.6` reference to `10.0.12` because the older dependency graph failed the repository's high-severity NuGet vulnerability gate;
- nullable annotations/initialization and XML API documentation were added where required by the current strict project;
- each `HttpClient` now uses the inherited static handler with `disposeHandler: false`, preventing one sink instance from disposing the shared handler for later instances;
- retention cleanup still uses SQLite's own `now` clock, but composite `TimeSpan` values are now converted to one valid relative-seconds modifier instead of an invalid comma-separated single modifier;
- the sink no longer configures Serilog's process-global `SelfLog`; the host owns `SelfLog` configuration, while SQLite and HTTP delivery failures are emitted into it when configured;
- persisted Serilog properties keep their historical stringified-value shape, but JSON escaping is now delegated to source-generated `System.Text.Json` instead of the incomplete hand-written escaper;
- synchronous and asynchronous disposal now share one shutdown task, so repeated/concurrent dispose callers join the same flush and no new event is accepted once shutdown begins;
- each event receives a stable GUID `EventId` before its first local insert; the ID is persisted with the spool row and reused across HTTP retries and process restarts;
- old spool schemas are upgraded in place by adding/backfilling `EventId` and creating a unique local index without discarding pending rows;
- protocol version `1` is sent to `Eigenverft.Service.CentralLogging` `/api/v1/logs`; `BatchId` remains a new per-attempt correlation ID while `EventId` is the idempotency key;
- compiler-generated `System.Text.Json` source-generator files are excluded from Coverlet measurement while the authored library code remains subject to the unchanged 100% line/branch/method threshold.
- direct package references were refreshed to the current stable versions used by the repository: `Microsoft.Data.Sqlite 10.0.12`, `Serilog 4.4.0`, `Microsoft.NET.Test.Sdk 18.10.1`, `MSTest 4.4.1`, and `coverlet.msbuild 10.0.1`; `Nerdbank.GitVersioning 3.10.94` was already current.

The regression suite characterizes the migrated behavior, including:

- local persistence before delivery;
- restart-safe backlog recovery;
- stable `EventId` reuse across retries and restart-safe legacy-spool identity migration;
- successful HTTP batching and `Sent = 1` updates;
- shutdown flush below the normal minimum batch size;
- repeated/concurrent sync/async disposal joining one shutdown operation and rejecting writes after shutdown begins;
- failed shutdown retry behavior;
- SQLite busy/locked retry handling;
- HTTP failures and `429 Retry-After`;
- trace/span/exception/property persistence;
- historical nullable database columns;
- sender-loop failure and cancellation paths.

The current regression suite contains 18 tests. On `net10.0`, all 18 pass and authored library code reaches 100% line, branch, and method coverage. The solution also builds successfully for `net8.0` and `net10.0`. A real temporary end-to-end run against the current `Eigenverft.Service.CentralLogging` receiver confirmed that `.WriteTo.SerilogRelay(...)` reaches `/api/v1/logs` and persists the same canonical `EventId` on the receiver.

The inherited analyzer cleanup is now complete for the current relay source. The Release pack for both `net8.0` and `net10.0` completes with 0 warnings and 0 errors. Persisted rendered messages use `CultureInfo.InvariantCulture` so their text is stable across host/thread locales; the remaining analyzer fixes were private parameter ordering and format/conversion cleanup without behavioral redesign.

## Deferred follow-up

The following review items are intentionally deferred rather than missing from the current baseline:

- **TLS policy (F1):** replace the inherited unrestricted certificate acceptance with normal platform certificate validation by default, plus an explicit opt-in mechanism for private/self-signed infrastructure when that deployment model is designed.
- **Authentication (U1):** add an explicit sender-authentication mechanism such as bearer tokens when the CentralLogging deployment/authentication model is defined. Event/application fields must not be treated as authenticated identity merely because they appear in the payload.
- **Operational health/status (U2):** consider a small observable relay status surface for values such as pending count, last successful delivery, and last delivery failure. `SelfLog` remains the current diagnostic path; a larger health API is intentionally deferred.

## Bring back better

The following remain redesign goals rather than part of the initial functional migration:

- Keep target endpoint as the only essential sender configuration for the normal case.
- Infer sensible application/instance identity and spool location by default, while allowing overrides.
- Keep SQLite as the durable spool initially; avoid putting a lossy in-memory queue in front of persistence.
- Define explicit behavior for SQLite busy/full/failure states and expose those failures through Serilog SelfLog or another observable diagnostic path.
- Support bearer tokens without making authentication mandatory for loopback/private deployments.
- Replace unrestricted TLS bypass with explicit options suitable for self-signed/private infrastructure, such as opt-in self-signed acceptance or certificate pinning.
- Use bounded retry/backoff with cancellation and reliable restart recovery.
- Keep the protocol implementation inside this package so sender applications only depend on the relay, not on the CentralLogging service project.
- Consider broader packaging/publication requirements when the implementation and receiver contract stabilize.

The archived sink remains the behavioral reference for the migrated baseline. Future work can now improve its weak edges from a tested, working starting point rather than reconstructing the behavior from scratch.
