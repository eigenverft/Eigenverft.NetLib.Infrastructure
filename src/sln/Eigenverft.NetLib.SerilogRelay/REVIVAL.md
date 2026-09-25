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

## Bring back better

Near-term thoughts:

- Keep target endpoint as the only essential sender configuration for the normal case.
- Infer sensible application/instance identity and spool location by default, while allowing overrides.
- Generate stable event IDs before delivery and carry them through retries.
- Keep SQLite as the durable spool initially; avoid putting a lossy in-memory queue in front of persistence.
- Define explicit behavior for SQLite busy/full/failure states and expose those failures through Serilog SelfLog or another observable diagnostic path.
- Support bearer tokens without making authentication mandatory for loopback/private deployments.
- Replace unrestricted TLS bypass with explicit options suitable for self-signed/private infrastructure, such as opt-in self-signed acceptance or certificate pinning.
- Use bounded retry/backoff with cancellation and reliable restart recovery.
- Keep the protocol implementation inside this package so sender applications only depend on the relay, not on the CentralLogging service project.
- Consider broader target-framework support and NuGet packaging when the implementation stabilizes and if publication becomes desirable.

These notes describe intent, not a frozen API. The archived sink is the behavioral reference; the new implementation should retain its useful guarantees while correcting its weak edges.
