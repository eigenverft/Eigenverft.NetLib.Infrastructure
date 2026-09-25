# Eigenverft.NetLib.SerilogRelay

Durable Serilog relay for forwarding application logs over HTTP while keeping a local persistent SQLite spool.

## Current implementation

The package contains `SerilogRelaySink`, functionally migrated from the AxonInsight `SQLiteSinkHttp` implementation while retaining the durable sender behavior. Before the first package release, the public API was renamed to match the package purpose.

Current Serilog configuration:

```csharp
.WriteTo.SerilogRelay(
    connectionString,
    "logs",
    endpoint: "https://logging.example/")
```

The migrated relay currently provides:

- durable SQLite persistence before any network delivery attempt;
- `Sent = 0` pending rows that survive process restarts and network outages;
- an independent background sender that loads and posts pending rows in batches;
- successful-delivery marking with `Sent = 1`;
- configurable minimum and maximum batch sizes;
- retention cleanup for sent and unsent rows;
- SQLite WAL mode, `synchronous=FULL`, busy timeout, and busy/locked retry handling;
- adaptive sender delay and HTTP `429 Retry-After` handling;
- a shutdown flush that attempts to send remaining pending rows;
- Serilog `SelfLog` diagnostics for local persistence and sender-loop failures.

The implementation targets `net8.0` and `net10.0`.

## Migration origin

This package continues the sender-side logging revival originally scaffolded as `Eigenverft.NetLib.SerilogCentralLoggingSink`.

The functional implementation was migrated from the discontinued AxonInsight infrastructure. The archived source remains the behavioral reference:

`personal-archive-issues/Discontinued/AxonInsight/src/AxonInsight.Client/AxonInsight.Installer/AxonInsight.Installer.Projects/AxonInsight.Library/Extensions/LoggerSinkConfigurationExtensions/SQLiteSinkHttp.cs`

Historical application call sites include:

`personal-archive-issues/Discontinued/AxonInsight/src/AxonInsight.Client/AxonInsight.Installer/AxonInsight.Installer.Projects/AxonInsight.Initializer/Program.cs`

The matching receiver is being revived separately as:

`Eigenverft.Service.CentralLogging`

## Current inherited limitations

This first functional migration deliberately does not redesign the historical protocol or security model. In particular:

- TLS certificate validation still uses the inherited unrestricted certificate callback;
- bearer-token authentication is not implemented yet;
- events do not yet receive stable delivery IDs for idempotent server-side handling;
- each HTTP batch receives a newly generated batch ID;
- the HTTP payload remains the migrated historical wire shape;
- operational diagnostics remain primarily Serilog `SelfLog` rather than a dedicated relay health surface.

These are known follow-up areas, not accidental omissions from the migration.

The longer-term preservation and redesign notes are maintained in the repository at `src/sln/Eigenverft.NetLib.SerilogRelay/REVIVAL.md`.
