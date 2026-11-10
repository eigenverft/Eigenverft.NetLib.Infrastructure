# SqliteHotBackup availability and integration

`Eigenverft.NetLib.SqliteHotBackup` provides online SQLite backups for .NET 8 and
.NET 10. Install the package independently of the legacy Infrastructure aggregate:

```shell
dotnet add package Eigenverft.NetLib.SqliteHotBackup
```

The implementation is available in this repository; publishing a release is a
separate operation. A locally packed package can also be used for evaluation.

## Register and invoke

The application keeps its existing EF registration and source paths. The library
owns discovery, scopes, backup steps, publication, ZIP handling and run results.

```csharp
using Eigenverft.NetLib.SqliteHotBackup;
using Microsoft.Extensions.DependencyInjection;

builder.Services.AddSqliteHotBackup(options =>
{
    // Registered concrete DbContexts and IDbContextFactory<T> are discovered by default.
    options.Discovery.SetDbContextId<ApplicationDbContext>("identity");
    options.Discovery.AddFile("hangfire", Path.Combine(dataDirectory, "Hangfire.db"));

    options.Backup.Directory = backupDirectory; // Absolute, or relative to each source.
    options.Zip.Enabled = true;
    options.Zip.PublishDirectory = archiveDirectory;
    options.Zip.DeleteRawBackupsAfterZip = true;
    options.Shutdown.CheckpointWal = false; // Enable explicitly if desired.
});

ISqliteHotBackupOrchestrator backup =
    serviceProvider.GetRequiredService<ISqliteHotBackupOrchestrator>();

SqliteBackupRunResult all = await backup.BackupAllAsync(cancellationToken);
SqliteBackupRunResult one = await backup.BackupAsync("hangfire", cancellationToken);
SqliteBackupRunResult selected = await backup.BackupAsync(
    new[] { "identity", "hangfire" }, cancellationToken);
```

`SetDbContextId<T>` maps an already registered/discovered context. It does not
register EF. Set `Discovery.DiscoverDbContexts = false` for explicit files only.
File paths supplied to `AddFile` are resolved relative to `AppContext.BaseDirectory`.
Discovery examines loaded assemblies and concrete DI registrations. It does not
load application plugins, discover open generic context types or make claims
about trimming/Native AOT support. Use explicit files where those assumptions do
not fit. Inventory reads registration metadata without constructing a context or
opening a database; source eligibility is checked when running a backup.

## Keep Hangfire in the caller

The application owns Hangfire registration, startup continuations, retry policy,
job concurrency policy and schedules. Its existing Hangfire integration can call
a consumer-owned job class:

```csharp
public sealed class BackupJobs(ISqliteHotBackupOrchestrator backup)
{
    public async Task AllAsync(CancellationToken cancellationToken)
    {
        await backup.BackupAllAsync(cancellationToken);
    }

    public async Task HangfireStoreAsync(CancellationToken cancellationToken)
    {
        await backup.BackupAsync("hangfire", cancellationToken);
    }
}
```

In the application's normal service-registration phase:

```csharp
builder.Services.AddTransient<BackupJobs>();
```

After building the host, use its existing Hangfire services to register schedules:

```csharp
IRecurringJobManager jobs = app.Services.GetRequiredService<IRecurringJobManager>();
jobs.AddOrUpdate<BackupJobs>(
    "sqlite-all", job => job.AllAsync(CancellationToken.None), Cron.Daily);
jobs.AddOrUpdate<BackupJobs>(
    "sqlite-hangfire", job => job.HangfireStoreAsync(CancellationToken.None), Cron.Hourly);
```

These examples use the application's Hangfire package/imports. The backup
library has no Hangfire dependency. `Hangfire.db` is an ordinary explicit file:
being the trigger's own store does not change library behavior. Await the task so
Hangfire can observe completion, failure and cancellation. An exception can lead
to a full job retry according to the application's policy; the library neither
registers retries nor changes future recurring jobs.

## Output and failure contract

Raw backups default to `_sqlite_backups` beside each source, with
`{name}_{utc:yyyyMMdd_HHmmss}.db`. One ZIP per run is enabled by default, named
`SqliteHostBackup_{utc:yyyyMMdd_HHmmss}.zip`. Its default directory is the first
successful raw backup's directory; a relative `Zip.PublishDirectory` is resolved
from that directory. Set an absolute directory when sources live in different
folders and the archive location must be fixed. Patterns support `{name}` and
`{utc:FORMAT}` and must produce one filename. Existing files are preserved by
adding `_2`, `_3`, etc. A completed raw backup or ZIP is published from a temporary
file in its destination directory only after its writers have closed.

Only successful raw backups enter the ZIP. With the default
`DeleteRawBackupsAfterZip = true`, exactly those files are deleted after ZIP
publication. `BackupFile` remains the historical output path; check
`RawBackupRetained` to know whether it remains. Failed ZIP creation retains raw
backups. A deletion failure logs a warning and leaves `RawBackupRetained = true`;
it does not invalidate an already published archive.

Operational source failures are recorded while other selected sources continue.
The ZIP still contains successes. Then `SqliteBackupException` is thrown if a
source or the ZIP failed. Its `Result` carries the full report:

```csharp
try
{
    SqliteBackupRunResult report = await backup.BackupAllAsync(cancellationToken);
}
catch (SqliteBackupException exception)
{
    SqliteBackupRunResult report = exception.Result;
    // Optional diagnostics. Rethrow to preserve scheduler failure handling.
    throw;
}
```

Normal callers can simply await the operation; no consumer-owned failure scan is
required. Configuration/selection errors fail before backup output is created.
Caller cancellation stops the whole run with `OperationCanceledException`.
Already completed files remain; incomplete owned staging files are cleaned up on
a best-effort basis. A later call attempts its entire selection again with fresh
limits and no persisted failed-target exclusion or resume cursor.

Automatic EF candidates using another provider, in-memory SQLite, or a non-WAL
SQLite file are explained skips in an all-target run. Selecting such an ID
explicitly makes it a failure. Explicit files support SQLite journal modes beyond
WAL. Missing files and operational resolution/read errors are failures. The
library does not create missing source databases or switch their journal mode.

## Operational boundaries

Calls and optional shutdown checkpoints share one gate per registered singleton.
Multiple processes or independent service providers do not share that gate.
Backups run sequentially and form individual database snapshots; a ZIP does not
provide a transactionally consistent snapshot across databases. The native step
loop uses 32 pages per step, 25 ms between advancing steps and 100 ms for
busy/locked retries, bounded by each source's [execution limit](execution-timing.md).
Native calls and filesystem I/O have cooperative cancellation, not forced preemption.

Shutdown checkpointing is opt-in, uses the host's cancellation token, and logs
per-source errors without failing host shutdown. It does not run a backup or
register a trigger. Connection pooling is disabled on library-owned connections;
application-owned EF connections are inspected for settings and left to their
normal scope/factory ownership.

Retention, restore, remote storage, cross-database transactions and periodic
ETA/stall-warning reporting are outside the delivered scope. Productive copies
have no fixed overall duration cap. See [IDs and selection](target-selection.md)
and [execution limits](execution-timing.md) before assigning independent schedules.
