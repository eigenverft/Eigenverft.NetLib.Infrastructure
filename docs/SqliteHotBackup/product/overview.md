# SqliteHotBackup scope and design context

`Eigenverft.NetLib.SqliteHotBackup` is being prepared as an independently
versioned package inside the Eigenverft.NetLib.Infrastructure repository.
The package name remains its own identity within the collection.

## Current state

The repository contains a library project, a matching empty test project, and
a dedicated solution for .NET 8 and .NET 10. Build settings, analyzers,
versioning, and NuGet metadata are prepared. There is no backup
engine, public API, database discovery, archive creation, retention, scheduling,
or restore capability. Build and distribution configuration follows the existing
active packages.

## Inherited purpose

The original design repository,
[`Eigenverft.NetLib.SqliteHotBackup`](https://github.com/eigenverft/Eigenverft.NetLib.SqliteHotBackup),
describes an operational backup layer for live SQLite databases in .NET
applications, especially long-running ASP.NET Core and EF Core workloads.

Its README identifies existing GlobalServer/PWA implementations as the starting
point, primarily `Features/SqliteDiscovery/`, `Features/SqliteHotBackup/`, and
`Features/HangfireJobs/` in `Eigenverft.App.GlobalServerPwaHost`, with a
near-duplicate in the PWA project. Those implementations have not been inspected
or copied as part of this scaffold. The following account preserves the design
context; it does not verify the behavior of those applications.

According to that design, the existing engine uses SQLitePCL's native online
backup calls (`sqlite3_backup_init`, stepped `sqlite3_backup_step`, and
`sqlite3_backup_finish`), retries busy/locked conditions, supports cancellation,
and publishes temporary output only after successful completion. The surrounding
feature is described as providing discovery, WAL inspection/checkpoint behavior,
naming, ZIP packaging, orchestration, and result reporting.

## Intended capabilities

These remain design goals whose first-release scope has not been finalized:

- Online, incremental/page-stepped backups with retry/backoff, cancellation,
  explicit results, and safe temporary-file publication.
- WAL-aware operation with optional checkpoint-on-shutdown support.
- Opt-in/configurable discovery of DI-registered EF Core `DbContext` types and
  `IDbContextFactory<TContext>`, plus explicit SQLite-file targets.
- One programmatic operation over the discovered/registered inventory, with
  configurable destinations and filename patterns.
- Optional ZIP packaging of successful backups, with raw files removed only
  after successful archive creation, and results distinguishing success,
  skipping, and failure.
- Configurable cleanup/retention and structured logging.

The original discovery design scans loaded application assemblies and checks
DI registrations. Explicit file registration remains the intended deterministic
alternative, including for SQLite stores outside EF Core.

## Trigger and dependency boundaries

The design calls for a programmatic orchestration API that the host may invoke
from a scheduler, worker, startup/shutdown workflow, administrative tool, or
controlled endpoint. The original examples use
`ISqliteHotBackupOrchestrator.BackupAllAsync()`; this is reference context,
not a declared API in the new package.

Hangfire is described as an existing caller with quarter-hourly, hourly, daily,
and startup schedules. Scheduler-specific packages and optional ASP.NET Core
administrative endpoints should remain separate integration concerns.
Authentication, authorization, and operational access belong to the host or an
optional integration layer.

No SQLite, EF Core, scheduler, or web-host runtime dependencies have been added
to the scaffold. The eventual package split and public APIs remain open for
implementation work.

## Extraction direction

The inherited direction is to compare the two application copies, preserve
working behavior, remove application coupling, and establish tests before API
redesign. The design names concurrency, WAL mode, interrupted backups, multiple
databases, and archive publication as important future verification topics.
Its eventual public NuGet ambition is conditional on portability beyond the
original applications; this scaffold does not establish release readiness.
