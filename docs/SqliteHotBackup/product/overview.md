# SqliteHotBackup scope and responsibility boundaries

`Eigenverft.NetLib.SqliteHotBackup` is one independently versioned .NET 8/.NET 10
package in the Eigenverft.NetLib.Infrastructure collection. It owns the complete
awaitable backup workflow; consumers supply configuration and invoke it.

## Delivered behavior

The library discovers registered concrete EF DbContexts and context factories
from loaded assemblies, accepts explicit SQLite files, and exposes all-target,
single-target and selected-target calls plus an inventory without database I/O.
Stable IDs name logical targets; selected aliases resolving to the same normalized
path share one copy and retain their ID associations.

Each source uses the native SQLite page-stepped backup API with busy/locked
retries, cancellation and a private progress-stall limit. Temporary raw files and
ZIPs are published after completion without overwriting existing outputs. Optional
ZIP packaging includes successful sources only and deletes raw files only after
publication. A singleton gate coordinates in-process calls and optional shutdown
WAL checkpoints. Structured logs and complete run reports expose outcomes.

An individual failure permits other selected sources and successful archiving to
continue. The standard operation then throws `SqliteBackupException` with the full
report. Cancellation stops the whole run. Future calls start fresh, including
previously failed sources. Ineligible automatic candidates are explained skips
in an all-target run; deliberate selection makes ineligibility a failure.

## Consumer boundary

The application owns its EF registration, database paths, scheduler registration,
schedules, startup continuations and retry policy. Hangfire is a trigger outside
the package; its SQLite store is an ordinary explicit file target. No Hangfire or
ASP.NET Core runtime dependency exists. Registration and integration examples are
in the [consumer guide](../user/availability.md).

The optional shutdown checkpoint belongs to library host-lifecycle integration,
independently enabled from backup triggering. Administrative endpoints, their
authentication and authorization remain consumer concerns.

## Execution limits

Global `ExecutionLimit = new ProgressStallLimitCalculation()` is inherited by all
automatic and explicit targets, with partial per-ID overrides. Each deduplicated
source attempt has private state. Defaults are five minutes without copied-prefix
advance and observed rewind loss greater than twice the largest page count.
Growth scales the rewind allowance without manufacturing progress or erasing
prior loss. There is no fixed overall duration ceiling or historical abort model.

The defaults derive from freely chosen theoretical size/rate/interruption/growth
scenarios, not GlobalServer timings or hardware benchmarks. Deterministic tests
verify the selected rules; real SQLite tests verify execution and failure paths.
A productive copy can still delay subsequent serial targets for a long time.
Native operations use cooperative limits rather than forced I/O interruption.

## Origin and limits

The reference application is `Eigenverft.App.GlobalServerPwaHost` at commit
`58bf56763a078f0a212b4b2b79d98064bd4b71a4`. Its GlobalServer and PWA backup
features have equivalent inspected behavior: all 12 backup files match after
normalizing application names and line endings. Their discovery differences are
comments and formatting. The library retains the page-stepped algorithm and
separates source discovery, copying, publication, archiving and lifecycle work.
EF-only WAL eligibility is retained; explicit files can use other journal modes.
No consumer repository is migrated in this implementation.

SqliteHotBackup is maintained as a subproject of this Infrastructure repository.
Its implementation and documented package contracts are authoritative here.

Retention, restore, remote stores, cross-database transactional snapshots,
periodic ETA/stall-warning reporting and distributed scheduling/locking are
outside this delivery. A ZIP holds independent snapshots, not one transaction
across all databases. Raw deletion after archiving is not retention. Publishing
or deploying the package remains a separate operation.
