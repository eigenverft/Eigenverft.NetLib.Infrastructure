# Eigenverft.NetLib.SqliteHotBackup

Online SQLite backups for .NET 8 and .NET 10, with EF Core discovery and explicit
file targets in one scheduler-independent package.

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
Set `Discovery.DiscoverDbContexts = false` for explicit-file-only use.
`GetTargetsAsync` returns the candidate inventory without opening databases.

Automatic EF sources require file-backed SQLite in WAL mode. Explicit files can
use other journal modes. IDs are trimmed, ordinal case-insensitive and unique.
Unknown/empty selections fail before output; selected aliases for the same
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

Licensed under the MIT License by Eigenverft.
