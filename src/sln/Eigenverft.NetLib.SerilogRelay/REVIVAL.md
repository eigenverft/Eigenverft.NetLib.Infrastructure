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
- relay HTTP clients now use normal .NET/platform server-certificate validation by default. The public `dangerousAcceptAnyServerCertificate` option defaults to `false`; setting it to `true` selects .NET's `DangerousAcceptAnyServerCertificateValidator` for that sink instance. Separate immutable shared handlers keep safe and dangerous sink instances independent;
- persisted Serilog properties keep their historical stringified-value shape, but JSON escaping is now delegated to source-generated `System.Text.Json` instead of the incomplete hand-written escaper;
- synchronous and asynchronous disposal now share one shutdown task, so repeated/concurrent dispose callers join the same flush and no new event is accepted once shutdown begins;
- each event receives a stable GUID `EventId` before its first local insert; the ID is persisted with the spool row and reused across HTTP retries and process restarts;
- protocol version `1` is sent to `Eigenverft.Service.CentralLogging` `/api/v1/logs`; `BatchId` remains a new per-attempt correlation ID while `EventId` is the idempotency key;
- the client API now treats the relay as fire-and-forget infrastructure: endpoint is the only normal remote-delivery input, while application identity, LocalApplicationData-based spool directory, `SerilogRelay.db` filename, and internal `SerilogRelayEvents` table are automatic; `spoolDirectory`, `spoolFileName`, and `applicationId` remain optional overrides;
- each newly emitted event now persists producer identity together with the event: logical `ApplicationId`, pseudonymous stable `MachineId`, and `ProcessId`. This is intentionally per-event rather than batch-level because multiple processes can share one application spool and another process may later deliver the row;
- `MachineId` is derived from an internal copy of the existing tested `PhysicalMachineBinding` source (SMBIOS system UUID on Windows, DMI product UUID on Linux, IOPlatformUUID on macOS, normalized and domain-separated SHA-256 hashed). The raw platform UUID never enters the wire payload, `MachineName` is not collected automatically, and fingerprint failure degrades to a null `MachineId` without breaking logging;
- because the package is still being developed from scratch and has not been released, the current v1 spool schema is the first supported schema: `EventId`, `ApplicationId`, and `ProcessId` are required from row creation, while `MachineId` remains nullable when fingerprinting is unavailable; no pre-v1 schema compatibility code is carried;
- `SQLITE_CORRUPT` and `SQLITE_NOTADB` now trigger serialized file-backed recovery: close/clear relay SQLite pools, quarantine the active DB plus WAL/SHM sidecars when still present, write a best-effort `corruption.json`, recreate the spool/schema, persist a durable `spool_corrupted` event in the fresh spool, and retry the failed database operation; `BUSY`, `LOCKED`, `FULL`, `CANTOPEN`, and `IOERR` are not misclassified as corruption;
- non-corruption local-spool failures no longer immediately lose the event: the normal path remains durable-first SQLite, but a failed spool write falls back to an internal bounded `System.Threading.Channels` buffer of 16384 already-materialized events. One emergency worker first retries SQLite using the same stable `EventId`; if local durability is still unavailable and an endpoint exists, it can rescue the event directly over HTTP. Startup directory/open failures enter the same degraded mode. Buffer overflow and unresolved volatile events at shutdown are surfaced through `SelfLog` rather than allowing unbounded RAM growth;
- the 16384-event emergency capacity is deliberately a temporary internal safety bound, not the final storage/retention policy. The emergency buffer is not placed in front of healthy SQLite and therefore does not weaken normal durable-first semantics;
- compiler-generated `System.Text.Json` source-generator files are excluded from Coverlet measurement while the authored library code remains subject to the unchanged 100% line/branch/method threshold.
- the copied cross-platform `PhysicalMachineBinding` helper and its environment-probe bridge are marked `[ExcludeFromCodeCoverage]` in this project because the original implementation already has dedicated MachineBinding tests; Relay coverage still measures the complete persistence/wire/null-fallback integration around `MachineId`.
- direct package references were refreshed to the current stable versions used by the repository: `Microsoft.Data.Sqlite 10.0.12`, `Serilog 4.4.0`, `Microsoft.NET.Test.Sdk 18.10.1`, `MSTest 4.4.1`, and `coverlet.msbuild 10.0.1`; `Nerdbank.GitVersioning 3.10.94` was already current.

The regression suite characterizes the migrated behavior, including:

- local persistence before delivery;
- restart-safe backlog recovery;
- stable `EventId` reuse across retries and restart-safe current-schema backlog recovery;
- successful HTTP batching and `Sent = 1` updates;
- shutdown flush below the normal minimum batch size;
- repeated/concurrent sync/async disposal joining one shutdown operation and rejecting writes after shutdown begins;
- failed shutdown retry behavior;
- SQLite busy/locked retry handling;
- HTTP failures and `429 Retry-After`;
- trace/span/exception/property persistence;
- nullable optional event fields such as trace/span/exception/properties and unavailable `MachineId`;
- sender-loop failure and cancellation paths.
- real corrupted SQLite startup recovery, async read-time recovery, corruption-code classification, sidecar quarantine, non-file-backed behavior, and best-effort recovery/metadata failure paths.
- non-corruption spool degradation at startup and emit time, bounded emergency overflow, recovery back into SQLite, direct HTTP rescue, ambiguous-write `EventId` verification, emergency cancellation/shutdown accounting, and event-materialization failure handling.

The current regression suite contains 34 tests. On `net10.0`, all 34 pass and authored library code reaches 100% line, branch, and method coverage. The solution also builds successfully for `net8.0` and `net10.0`. Real temporary end-to-end runs against the current `Eigenverft.Service.CentralLogging` receiver confirmed that `.WriteTo.SerilogRelay(...)` reaches `/api/v1/logs`, preserves the canonical `EventId`, and persists the producer `ApplicationId`, 64-character hashed `MachineId`, and `ProcessId` on the receiver.

The inherited analyzer cleanup is now complete for the current relay source. The Release pack for both `net8.0` and `net10.0` completes with 0 warnings and 0 errors. Persisted rendered messages use `CultureInfo.InvariantCulture` so their text is stable across host/thread locales; the remaining analyzer fixes were private parameter ordering and format/conversion cleanup without behavioral redesign.

## Release blockers

Blocker 1 (local persistence failure / event-loss behavior) is resolved by the bounded emergency fallback described above. The remaining blockers stay separated by failure domain and retain their numbering for continuity.

### Blocker 2 - Multi-process shared-spool coordination

The application-level default spool can be opened by multiple same-application processes, and receiver-side `EventId` idempotency prevents duplicate stored events. However, each process owns its own `_pendingCount`, sender loop, signal state, and in-process database gate while observing the same SQLite spool. Parallel sender loops can race on the same unsent rows, one process cannot observe another process's in-memory pending state, and corruption recovery is only coordinated inside one process. Solve this independently after Blocker 1, either with cross-process row claiming/leases plus a recovery lock or by explicitly narrowing the supported v1 contract so one application spool has only one active sender process.

### Blocker 3 - Authentication (deployment-dependent)

Explicit sender authentication such as bearer tokens is a release blocker if v1 officially supports direct remote/external ingestion. If the first supported deployment scope is limited to loopback/private infrastructure behind a separately trusted proxy boundary, authentication can remain deferred. Event/application identity fields must never be treated as authenticated identity merely because they appear in the payload.

## Deferred

These items are useful follow-up work but are not currently required for the first package release:

- **Operational health/status (U2):** consider a small observable relay status surface for values such as pending count, last successful delivery, and last delivery failure. `SelfLog` remains the current diagnostic path.
- **Retry/backoff refinement:** the relay already has restart-safe persistence, adaptive delay, cancellation, busy/locked retry handling, and HTTP `429 Retry-After` support. Further bounded retry/backoff tuning can be revisited after real deployment behavior is known.
- **File/memory limits and prolonged endpoint outages:** design the final limits separately from the emergency-fallback change. The file spool currently has time-based retention (`sentRetention` default 1 day, `unsentRetention` default 3 days) and batch bounds, but no hard database-byte or total-event cap. The emergency buffer currently has only the fixed 16384-event safety cap, not a byte-size or age budget. A later limits pass should consider file size, total event count, event age, memory budget, oversized individual events, and explicit behavior when the remote endpoint is unavailable for horizons such as 1 hour, 12 hours, 1 day, or 7 days. Normal endpoint outages with a healthy local spool remain a file-spool concern, not an emergency-memory concern.
- **Packaging/publication refinement:** broader package publication requirements can be finalized when the sender/receiver contract and deployment scope are frozen.
- **Architecture constraints to preserve:** keep SQLite as the normal durable spool rather than adding a lossy in-memory queue in front of healthy persistence. The bounded Channel is an exceptional fallback only after local persistence fails. Keep the CentralLogging protocol implementation inside this package so sender applications do not depend on the service project.

## Future protocol ideas

These are recorded design directions, not current v1 wire requirements:

- Treat the configured endpoint as the **logging API base**, e.g. `https://host/api/v1/logs`. Normal application batches continue to POST directly to that base; future relay-operational traffic can derive a child route such as `/api/v1/logs/relay-events`, avoiding an artificial `/default` route.
- A future server-preference handshake/config route can live below the same base, for example `/api/v1/logs/config`. Client-side defaults remain authoritative unless an explicit `allowServerConfiguration`-style opt-in is enabled. Server values should be bounded hints such as batch size or interval, cached with a TTL, and fall back to local defaults when unavailable.
- The standard producer identity is now `ApplicationId` + `MachineId` + `ProcessId`. `MachineName` remains opt-in Serilog enrichment rather than relay metadata. A separate `ProcessInstanceId` or `InstallationId` should only be introduced if a concrete historical-run or installation-lifetime use case requires it.
- The archived AxonInsight setup already reflected this separation imperfectly: applications explicitly enriched logs with machine name, while a persisted setup GUID was used in the logging URL and behaved more like an installation/client identifier than a physical-machine identity.
- If multi-process support is implemented rather than excluded from the first release contract, prefer row claiming/leases and a cross-process recovery lock over process-ID-specific spool files because process-specific files would weaken restart backlog recovery.

The archived sink remains the behavioral reference for the migrated baseline. Future work can improve its weak edges from a tested, working starting point rather than reconstructing the behavior from scratch.
