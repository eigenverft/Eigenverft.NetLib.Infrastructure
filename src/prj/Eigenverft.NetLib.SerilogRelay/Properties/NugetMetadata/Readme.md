# Eigenverft.NetLib.SerilogRelay

Durable Serilog relay for forwarding application logs over HTTP while keeping a local persistent SQLite spool.

## Current implementation

The package contains `SerilogRelaySink`, functionally migrated from the AxonInsight `SQLiteSinkHttp` implementation while retaining the durable sender behavior. Before the first package release, the public API was renamed to match the package purpose.

Current minimal Serilog configuration:

```csharp
.WriteTo.SerilogRelay("https://logging.example/api/v1/logs")
```

With no spool overrides, the relay resolves its persistent SQLite spool automatically from `Environment.SpecialFolder.LocalApplicationData`:

```text
<Eigenverft local application data>/Eigenverft/SerilogRelay/<ApplicationId>/SerilogRelay.db
```

`ApplicationId` defaults to the entry-assembly name (falling back to the current AppDomain friendly name) and is normalized for safe directory use. The SQLite table is internal and fixed as `SerilogRelayEvents`.

Applications that need storage control can override the directory, filename, and application identity independently:

```csharp
.WriteTo.SerilogRelay(
    endpoint: "https://logging.example/api/v1/logs",
    spoolDirectory: @"D:\AppData\Logging",
    spoolFileName: "MyWorker.db",
    applicationId: "MyWorker")
```

A relative `spoolDirectory` is resolved below the application-specific default relay directory; an absolute directory is used as supplied. `spoolFileName` is filename-only. Calling `.WriteTo.SerilogRelay()` with no endpoint keeps the same durable local spool without starting the HTTP sender.

The migrated relay currently provides:

- durable SQLite persistence before any network delivery attempt;
- fire-and-forget client defaults that automatically choose the application identity, spool directory, spool filename, and internal table name;
- automatic per-event producer identity: `ApplicationId`, `MachineId`, and `ProcessId` are persisted with each new spool row before delivery so shared-spool multi-process producers remain distinguishable after retries or sender handoff;
- `MachineId` is a stable SHA-256 platform fingerprint derived locally from the system/platform UUID (SMBIOS on Windows, DMI on Linux, IOPlatformUUID on macOS). The raw platform UUID and `MachineName` are not transmitted by the relay;
- `Sent = 0` pending rows that survive process restarts and network outages;
- a stable per-event `EventId` persisted in the local spool before delivery and reused across retries/restarts;
- the first supported spool schema requires `EventId`, `ApplicationId`, and `ProcessId` on every new row; `MachineId` remains optional when the platform fingerprint is unavailable;
- an independent background sender that loads and posts pending rows in batches;
- protocol version `1` batches for `Eigenverft.Service.CentralLogging`, with `BatchId` used as per-attempt correlation and `EventId` as the idempotency key;
- successful-delivery marking with `Sent = 1`;
- configurable minimum and maximum batch sizes;
- retention cleanup for sent and unsent rows;
- SQLite WAL mode, `synchronous=FULL`, busy timeout, and busy/locked retry handling;
- explicit SQLite corruption handling for `SQLITE_CORRUPT` / `SQLITE_NOTADB`: the file-backed spool is quarantined under `corrupted/<quarantine-id>/`, any DB/WAL/SHM files still present are moved together, `corruption.json` is written best-effort, a fresh spool is created, and a durable `spool_corrupted` event is inserted before normal logging resumes;
- adaptive sender delay and HTTP `429 Retry-After` handling;
- a shutdown flush that attempts to send remaining pending rows;
- idempotent shared sync/async disposal: concurrent/repeated disposal joins one shutdown operation and new events are rejected once shutdown begins;
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

The current implementation intentionally defers the remaining security/operations redesign items. In particular:

- TLS certificate validation still uses the inherited unrestricted certificate callback;
- bearer-token authentication is not implemented yet;
- each HTTP attempt receives a newly generated `BatchId`; this is intentional correlation metadata, while stable `EventId` values provide retry idempotency;
- operational diagnostics remain primarily Serilog `SelfLog` rather than a dedicated relay health surface.
- the default application-level spool can be shared by parallel processes. SQLite and receiver-side `EventId` idempotency preserve correctness, but sender claiming is not yet coordinated across processes, so parallel senders may temporarily issue duplicate HTTP attempts; corruption quarantine is also best-effort if another process still holds the spool files open.

These are known follow-up areas, not accidental omissions from the migration.

The longer-term preservation and redesign notes are maintained in the repository at `src/sln/Eigenverft.NetLib.SerilogRelay/REVIVAL.md`.
