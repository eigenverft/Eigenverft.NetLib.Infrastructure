# Eigenverft.NetLib.SqliteHotBackup

Online SQLite backups for .NET 8 and .NET 10, with Automatic/Manual discovery,
EF Core targets, dynamic consumer-owned sources and runtime file registration
in one scheduler-independent package.

```csharp
using Eigenverft.NetLib.SqliteHotBackup;
using Microsoft.Extensions.DependencyInjection;

services.AddSqliteHotBackup(options =>
{
    options.Discovery.AddFile("hangfire", databaseFile);
    options.Backup.Directory = backupDirectory;
});

ISqliteHotBackupOrchestrator backup =
    serviceProvider.GetRequiredService<ISqliteHotBackupOrchestrator>();
await backup.BackupAllAsync(cancellationToken);
await backup.BackupAsync("hangfire", cancellationToken);
await backup.BackupAsync(new[] { "identity", "hangfire" }, cancellationToken);
```

Register EF contexts normally in the application. Registered concrete DbContexts
and `IDbContextFactory<T>` implementations in loaded assemblies are discovered by
default; generated IDs are `ef:<fully qualified type name>`. Optional
`Discovery.SetDbContextId<T>("identity")` replaces that ID without registering EF.
Set `Discovery.Mode = SqliteBackupDiscoveryMode.Manual` for configured/runtime
files only. Manual performs no EF discovery or automatic provider resolution.
The existing `DiscoverDbContexts` flag still controls only EF in Automatic mode.
The library does not open backup source databases during `GetTargetsAsync`;
automatic providers may read their own catalogs.

Register new files during application lifetime through the singleton registry:

```csharp
ISqliteBackupTargetRegistry targets =
    serviceProvider.GetRequiredService<ISqliteBackupTargetRegistry>();
targets.AddFile("new-store", readyDatabaseFile);
await backup.BackupAllAsync(cancellationToken);
targets.Remove("new-store"); // Registration only; never deletes the file.
```

For automatic querying, register a consumer implementation with
`services.AddSqliteBackupSourceProvider<MyDatabaseSources>()`. It implements
`ISqliteBackupSourceProvider.GetSourcesAsync` and returns a list of
`SqliteBackupFileSource(id, path)`. Each operation creates a new DI scope and
copies each returned snapshot. Providers are not resolved in Manual mode.
The registry is thread-safe and process-local; restore it after restart from the
consumer's persistent catalog. Database creation, migrations and readiness remain
consumer-owned. There is no implicit filesystem scan or separate rescan step.

Automatic EF sources require file-backed SQLite in WAL mode. Explicit files can
use other journal modes. IDs are trimmed, ordinal case-insensitive and unique.
With complete discovery, unknown/empty selections fail before output; selected aliases for the same
resolved path share one backup. Relative file registrations use
`AppContext.BaseDirectory`; relative backup folders use each source's directory.

Raw backups default to `_sqlite_backups` beside their source. ZIP packaging is
enabled by default and includes successful sources only. The default archive
location is the first successful backup's directory. Raw inputs are deleted only
after successful ZIP publication; set `Zip.DeleteRawBackupsAfterZip = false` to
keep them or `Zip.Enabled = false` for raw backups only. Existing outputs are
preserved by adding filename suffixes.

Operational failures do not prevent other selected sources or the ZIP of
successes. Afterwards `SqliteBackupException.Result` exposes the full report.
Provider constructor/query/validation failures are included in
`Result.DiscoveryFailures`. Other known targets and archives still proceed.
`GetTargetsAsync` throws `SqliteBackupDiscoveryException` with partial Targets
and Failures when a source is unavailable. Missing selected IDs under incomplete
discovery are failed outcomes rather than being conclusively classified as unknown.
Caller cancellation stops the run. Later invocations start fresh. Await the task
so a caller or scheduler can observe its outcome.

`ExecutionLimit = new ProgressStallLimitCalculation()` defaults to five minutes
without copied-prefix advance and observed rewind work greater than twice the
largest page count. `ConfigureExecutionLimit(id, callback)` partially overrides
the final global recipe for an ID. State is private per physical source attempt;
productive copies have no fixed total-duration cap. Limits and cancellation are
cooperative, not forced interruption of native I/O.

The library owns copying, safe publication, optional archiving and opt-in shutdown
WAL checkpoints. The caller owns scheduling, startup triggers, scheduler retries
and EF registration. No Hangfire or ASP.NET Core dependency is included. Retention,
restore, remote storage and periodic ETA reporting are outside this scope.

See the [consumer guide](https://github.com/eigenverft/Eigenverft.NetLib.Infrastructure/blob/main/docs/SqliteHotBackup/user/availability.md)
for registration, Hangfire examples and output/failure details, and the
[execution-limit guide](https://github.com/eigenverft/Eigenverft.NetLib.Infrastructure/blob/main/docs/SqliteHotBackup/user/execution-timing.md)
for stopping rules and overrides.

See [dynamic sources and modes](https://github.com/eigenverft/Eigenverft.NetLib.Infrastructure/blob/main/docs/SqliteHotBackup/user/dynamic-sources.md)
for provider contracts, runtime registration, lifecycle and source-error details.

Licensed under the MIT License by Eigenverft.
