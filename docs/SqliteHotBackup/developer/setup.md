# Developing SqliteHotBackup

Use the .NET 10 SDK for the `.slnx` solution and `net8.0;net10.0` targets. Running
both test targets also requires the .NET 8 and .NET 10 runtimes. Restore needs the
configured NuGet sources or an adequate package cache.

## Projects and dependencies

The solution is
`src/sln/Eigenverft.NetLib.SqliteHotBackup/Eigenverft.NetLib.SqliteHotBackup.slnx`.
Its library and matching MSTest project live under `src/prj/`. The library uses
EF Core SQLite 8.0.33 for .NET 8 and 10.0.14 for .NET 10, plus matching-generation
Microsoft.Extensions.Hosting.Abstractions. Microsoft.Data.Sqlite/SQLitePCLRaw
supply the native SQLite engine. There is no scheduler or web-host dependency.
The Infrastructure compatibility aggregate does not reference this package.

The package retains shared analyzers, XML documentation, nullable checks, package
validation and source-control targets. `Properties/version.json` has an independent
`1.0.0` base with shared release-reference rules and the library path filter.
NuGet metadata lives under `Properties/NugetMetadata/` and in the project file.

## Commands

Run from the repository root:

```powershell
dotnet restore src/sln/Eigenverft.NetLib.SqliteHotBackup/Eigenverft.NetLib.SqliteHotBackup.slnx
dotnet build src/sln/Eigenverft.NetLib.SqliteHotBackup/Eigenverft.NetLib.SqliteHotBackup.slnx --configuration Release --no-restore -m:1
dotnet test src/sln/Eigenverft.NetLib.SqliteHotBackup/Eigenverft.NetLib.SqliteHotBackup.slnx --configuration Release --no-build --no-restore
dotnet pack src/prj/Eigenverft.NetLib.SqliteHotBackup/Eigenverft.NetLib.SqliteHotBackup.csproj --configuration Release --no-build --no-restore
```

Builds use ignored `bin/` and `obj/`; local packages go to library `bin/Pack/`.
TRX/HTML reports use test `MSTestResults/`, coverage uses per-framework folders in
`CoverletOutput/`, and dependency reports use `NugetReport/`. Synthetic databases
are created under test `bin/<configuration>/<framework>/BackupTests/<guid>/` and
cleaned after each test. Tests never need application or production databases.
Use `-m:1` for builds as CI does: framework builds share the template's dependency
report filenames and can otherwise encounter file locks.

## Implementation map

- `SqliteHotBackupOptions` holds recipes and per-run snapshots; target-specific
  callbacks inherit the final global settings without sharing execution state.
- `TargetCatalog` checks loaded types and DI registration metadata without
  constructing contexts. Explicit files bypass EF knowledge.
- `SourceResolver` owns source eligibility and independent read-only connections.
  DI-scoped contexts belong to the run scope; factory-created contexts are
  disposed explicitly. Missing databases are never created.
- `SqliteHotBackupOrchestrator` validates selections, resolves aliases, coordinates
  calls, aggregates failures, and throws only after useful independent work.
- `SqliteCopy` owns the native backup handle and page-stepped retry loop.
  `ProgressStallState` owns monotonic inactivity and observed rewind accounting.
- `BackupFiles` publishes without overwrite and cleans only owned staging files.
  `BackupArchive` publishes the successful-source ZIP before deleting exact raw
  inputs. Cleanup errors do not invalidate an already published archive.
- `ShutdownCheckpoint` opts into best-effort `wal_checkpoint(TRUNCATE)` under the
  same gate and the host's cancellation token. It does not schedule backups.

A catalog is candidate inventory, not a promise of source eligibility. All-target
runs may skip unsupported automatic candidates; targeted calls fail them. Paths
use SQLite's resolved main filename, full-path normalization and OS-appropriate
ordinal comparison. Symlink/hard-link identity is not established.

Active source preparation contributes to initial inactivity; unrelated target
preparation and gate waiting do not. Source-local exhaustion records a failure;
caller cancellation propagates. Publication and ZIP are outside page-progress
estimation. Every exit releases the gate and native resources. An advancing copy
has no fixed total-duration ceiling. See [the timing contract](../user/execution-timing.md).

## Test organization

`ExecutionLimitTests` protects defaults, boundaries, growth, rewind debt and private
attempt state with a manual clock. `DiscoveryTests` protects direct/factory EF
ownership, lazy inventory, IDs, eligibility and shutdown. `BackupRunTests` protects
selection, alias deduplication, best-effort continuation, independent later calls,
WAL snapshots, cancellation, concurrent calls and output failures.
`PublicationTests` protects no-overwrite publication, ZIP/input/cleanup failures
and filename validation. `ConfigurationAndLimitIntegrationTests` protects options
snapshots and actual exclusive-lock exhaustion in the native loop.

Coverage reports include the library assembly. The coverage gate and measured
results are recorded in [delivery status](../management/status.md). Fault guards
for native initialization/finish errors and reflection loader failures remain
valuable even when ordinary synthetic integrations cannot induce them safely.

## Delivery and source baseline

The shared `.github/workflows/cicd.ps1` discovers solutions recursively. Both
projects follow normal CI activation and branch distribution rules. No workflow
entry or package-specific publishing gate is added here. A local pack does not
publish a release.

The GlobalServer/PWA reference was inspected at application commit
`58bf56763a078f0a212b4b2b79d98064bd4b71a4`. The stepped native algorithm, delays,
file/ZIP workflow and EF WAL eligibility are retained as baseline behavior.
Target IDs, targeted calls, best-effort result aggregation and progress-stall
limits are library contracts. Consumer migration is outside this change.
