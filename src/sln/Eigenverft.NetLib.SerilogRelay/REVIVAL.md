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

`src/prj/Eigenverft.NetLib.SerilogRelay/SQLiteSinkHttp.cs`

The archived AxonInsight source was treated as read-only and was not modified. It was already available as an extracted directory rather than a ZIP archive, so no temporary extraction directory was required in the working repository.

The migration intentionally avoided a protocol or API redesign. The current public configuration therefore still uses `.WriteTo.SQLiteSinkHttp(...)`, and the existing SQLite schema, background sender, batching, retention, retry, HTTP payload, and shutdown-flush behavior remain recognizable from the archived implementation.

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
- compiler-generated `System.Text.Json` source-generator files are excluded from Coverlet measurement while the authored library code remains subject to the unchanged 100% line/branch/method threshold.
- direct package references were refreshed to the current stable versions used by the repository: `Microsoft.Data.Sqlite 10.0.12`, `Serilog 4.4.0`, `Microsoft.NET.Test.Sdk 18.10.1`, `MSTest 4.4.1`, and `coverlet.msbuild 10.0.1`; `Nerdbank.GitVersioning 3.10.94` was already current.

The regression suite characterizes the migrated behavior, including:

- local persistence before delivery;
- restart-safe backlog recovery;
- successful HTTP batching and `Sent = 1` updates;
- shutdown flush below the normal minimum batch size;
- failed shutdown retry behavior;
- SQLite busy/locked retry handling;
- HTTP failures and `429 Retry-After`;
- trace/span/exception/property persistence;
- historical nullable database columns;
- sender-loop failure and cancellation paths.

The current regression suite contains 15 tests. On `net10.0`, all 15 pass and authored library code reaches 100% line, branch, and method coverage. The solution also builds successfully for `net8.0` and `net10.0`.

The baseline migration intentionally leaves inherited analyzer cleanup for a separate follow-up. A full `net8.0`/`net10.0` build currently succeeds but reports 16 analyzer warnings across both target frameworks, covering the inherited culture-sensitive formatting, cancellation-token parameter ordering, repeated formatting, synchronous `ValueTask` consumption in `Dispose()`, and dispose-pattern guidance (`CA1305`, `CA1068`, `CA1863`, `CA2012`, and `CA1816`). These were not rewritten during the functional migration because doing so would go beyond the agreed light-adaptation scope.

## Bring back better

The following remain redesign goals rather than part of the initial functional migration:

- Keep target endpoint as the only essential sender configuration for the normal case.
- Infer sensible application/instance identity and spool location by default, while allowing overrides.
- Generate stable event IDs before delivery and carry them through retries.
- Keep SQLite as the durable spool initially; avoid putting a lossy in-memory queue in front of persistence.
- Define explicit behavior for SQLite busy/full/failure states and expose those failures through Serilog SelfLog or another observable diagnostic path.
- Support bearer tokens without making authentication mandatory for loopback/private deployments.
- Replace unrestricted TLS bypass with explicit options suitable for self-signed/private infrastructure, such as opt-in self-signed acceptance or certificate pinning.
- Use bounded retry/backoff with cancellation and reliable restart recovery.
- Keep the protocol implementation inside this package so sender applications only depend on the relay, not on the CentralLogging service project.
- Consider whether the public Serilog configuration should eventually move from the historical `.SQLiteSinkHttp(...)` name to a relay/CentralLogging-oriented name.
- Consider broader packaging/publication requirements when the implementation and receiver contract stabilize.

The archived sink remains the behavioral reference for the migrated baseline. Future work can now improve its weak edges from a tested, working starting point rather than reconstructing the behavior from scratch.
