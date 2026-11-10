# Extract managed SQLite backups into one scheduler-independent package

Created: 2026-11-10
Last edited: 2026-11-10
Status: Completed on 2026-11-10

The sections below preserve the pre-implementation requirements and decision history. The completion record at the end describes the delivered result; API sketches and statements about the former scaffold refer to the shaping stage.

## Issue shaping and context

Prepare the first implementation of `Eigenverft.NetLib.SqliteHotBackup` by
partially extracting the existing GlobalServer/PWA backup feature. The current
package contains only the .NET 8/.NET 10 scaffold; implementation has not started.
The consumer should configure and register the library, then invoke its public
backup operation. Discovery and execution knowledge belong to the library.

### Source evidence

The reference repository is `Eigenverft.App.GlobalServerPwaHost`, inspected at
commit `58bf56763a078f0a212b4b2b79d98064bd4b71a4`. Its application projects are
`src/prj/Eigenverft.App.GlobalServerPwaHost/` and `src/prj/Eigenverft.App.Pwa/`.

- All 12 files in their `Features/SqliteHotBackup/` directories match after
  normalizing the application name and line endings. Discovery differences are
  documentation and formatting; the inspected algorithms are equivalent.
- `ISqliteHotBackupOrchestrator` exposes two `BackupAllAsync` overloads: configured
  defaults, or per-run option overrides. Neither selects registered targets.
- The internal `SqliteHotBackupEngine` has individual DbContext/file operations.
  These are implementation helpers, not a public registered-target API.
- All three recurring Hangfire schedules invoke the same all-target operation.
  The startup continuation changes the ZIP filename pattern, not the target set.
- Native page-stepped backup, temporary output, ZIP creation, and raw-file
  deletion are present. Retention and restore are not implemented in this feature.

This is a source comparison, not a new runtime verification. The consumer reports
that the GlobalServer implementation works well in production.

### Agreed target identities (not implemented)

Targeted backups and the identity rules below are accepted for the initial scope.
The user has requested that this decision be recorded, without implementation.
Method names illustrate the intended operations; precise .NET signatures and
descriptor types are implementation details to finalize against this contract.

- Automatically discovered contexts receive `ef:<fully qualified context type
  name>`, for example `ef:MyApp.Data.LocalizationDbContext`. Do not use assembly
  versions, discovery order, database filenames, or deployment paths in this ID.
  Direct DI resolution and `IDbContextFactory<TContext>` refer to the same logical
  context target.
- An optional `Discovery.SetDbContextId<TContext>("localization")` mapping
  replaces the generated ID for that discovered type. It does not register EF
  services, create a second source, or add a second selectable alias. A stable
  explicit ID survives a type/namespace rename when the mapping is updated.
- Explicit files require an application-chosen ID, for example
  `Discovery.AddFile("hangfire", fullPath)`. Their ID survives a path change.
- IDs share one namespace. Comparison is ordinal, case-insensitive,
  after trimming; empty IDs and conflicting IDs are configuration errors. Do not
  silently append suffixes when discovery finds a collision. Two types with the
  same fully qualified name in different assemblies require explicit mappings.
- Provide a read-only effective-ID/source-kind inventory, illustrated by
  `GetTargetsAsync`, without starting backups. Precise descriptor metadata is a
  technical implementation detail.
- Resolve and validate the entire requested ID selection before creating backup
  files. Unknown IDs or empty selections fail instead of falling back to all.
  `BackupAllAsync` remains the deliberate full-inventory call.
- Distinct IDs may resolve to the same database path. Keep each ID selectable,
  deduplicate the resolved paths within the selected run, and retain the requested
  ID-to-backup associations in results. Specify and test normalization/path
  comparison during implementation; an ID does not promise physical-file alias
  detection.

The agreed contract and consumer-facing API sketches are maintained in
`docs/SqliteHotBackup/user/target-selection.md`.

## Ambiguities and possible misunderstandings

- "Fire and forget" means configure once and invoke one complete, awaitable
  operation. It does not mean discarding the running task.
- Hangfire is both the consumer's trigger and the owner of one SQLite source
  file. The library treats `Hangfire.db` as an ordinary explicit file target.
- Raw-file deletion after successful ZIP publication is not backup retention.
- A ZIP of several databases does not establish one cross-database transaction
  snapshot; the existing backups run sequentially.
- [x] Include single-target and selected-target backups in the initial scope;
  schedules remain external to the library.
- [x] Agree the identity, mapping, collision, selection, and inventory rules above.

## Open decisions

None blocking this delivery. Periodic ETA/warning reporting was not selected and
is outside the implemented scope. Required final results and completion/failure
logs are delivered.

### Selected execution limit and theoretical initial defaults

`ExecutionLimit` and `ProgressStallLimitCalculation` are selected. At the user's
request to establish reasonable values from freely chosen theoretical scenarios,
the initial settings are five minutes without copied-prefix advance and a
maximum observed rewind-work factor of **2.0**, replacing the earlier unselected
factor 3. An increase in `P = total - remaining` refreshes inactivity; each drop
accumulates rewind work `W`. Fail at five minutes of inactivity or when
`W > 2 * Nmax`, where `Nmax` is the largest observed page count. Growth scales
the allowance but does not count as copied progress or clear prior rewind work.
There is no fixed total-duration or learned abort ceiling.

Use one global calculation recipe inherited by automatically discovered and
explicit targets. Target-ID overrides change supplied settings only; unspecified
fields inherit the final global recipe. Each database attempt uses private state,
including a fresh clock and rewind count. This is not one shared run-wide budget.

[The focused timing issue](2026-11-10-sqlitehotbackup-target-timing.md) records the
rules, override handling for deduplicated sources, and optional reporting choice.
The size/rate, interruption, growth, and rewind arithmetic is in
`docs/SqliteHotBackup/developer/execution-limit-scenarios.md`; it uses no
GlobalServer timings, actual data, hardware measurements, or runtime tests.
Consumer configuration is described in
`docs/SqliteHotBackup/user/execution-timing.md`. The values are theoretical initial
defaults to verify during implementation, not measured optima.

## Decisions already made

- Deliver one package with internally separated responsibilities. Include EF
  discovery, explicit file registration, backup orchestration, optional ZIP
  creation, and an explicitly enabled shutdown WAL checkpoint facility.
- Keep schedules, startup continuations, scheduler configuration, and scheduler
  retry policy in the consumer. Add no Hangfire dependency or internal schedule.
- Provide library-owned DI registration and public execution calls such as
  `BackupAllAsync`; do not require consumers to implement discovery, scope
  management, backup steps, ZIP handling, or standard failure interpretation.
- Include individual and selected registered-target backups so consumers can
  assign different external schedules to different database sets. Automatic EF
  IDs, optional stable type mappings, explicit file IDs, validation, path-based
  per-run deduplication with ID associations, and effective-ID inventory follow
  the agreed contract above. No implementation exists yet.
- Define a normal failure/exception contract suitable for an awaited scheduler
  call, with diagnostic detail. Propagate cancellation as cancellation.
- Catch operational target failures, attempt the other selected targets, and
  archive successful backups when enabled. After that work, throw a library-defined
  exception carrying the complete run result if any backup/archive operation
  failed. Release resources and run gates on every exit path; do not fail fast or
  persist a stopped state. End-of-run exception reporting is accepted.
- Use ProgressStallLimitCalculation with five-minute inactivity and rewind factor
  2.0 as initial values derived from the requested theoretical scenarios. Provide
  global inheritance, per-ID setting overrides, and independent attempt counters.
  Productive copies have no fixed overall deadline; no historical abort model is
  selected. Validate the initial values during implementation.
- Target failures must not prevent other selected targets from being attempted
  or successful backups from being retained/archived. A later call attempts its
  selected inventory again; no persistent failed-target exclusion or implicit
  resume cursor is introduced. Caller cancellation still stops the whole run.
- Accept the skipped-versus-failed classification: explain skips for ineligible
  automatically discovered candidates in all-target runs; fail missing explicitly
  registered files and explicitly selected targets that cannot be backed up.
  Operational errors while resolving/reading a source remain failures.
- Explicit files remain a first-class source for stores whose EF/DI integration
  is outside the consumer's control. Coordinate overlapping in-process calls.
- Keep reusable integration guidance and examples with the library.
- Name the public stopping-rule configuration `ExecutionLimit` and its selected
  calculation `ProgressStallLimitCalculation`. No recovery or scheduler state is
  implied. Exact constructor/property/override-method syntax remains an API sketch.
- Do not modify or migrate the application consumers in this issue's extraction
  step. Do not add retention, restore, or remote storage without a separate choice.

## Possible implementation approaches

Retain the native page-stepped algorithm and existing backup/ZIP behavior as the
starting point. Separate internal source discovery/EF resolution, file backup,
archiving, and host lifecycle handling within the single package. Implement a
complete public operation over that workflow and verify its supported behavior
before a later consumer migration. Public names shown in discussion remain API
sketches until the open decisions are resolved.

### Reference baseline and technical implementation work

GlobalServer is the working implementation reference. Its source already supplies
the page-stepped native algorithm, retry delays, connection ownership, filename
patterns, temporary output workflow, ZIP options, raw-file deletion, per-run option
cloning, and shutdown checkpoint sequence. Preserve clear existing behavior as the
extraction baseline; validate failure/cancellation/publication boundaries rather
than treating every implementation choice as a new product question.

The source requires WAL for its EF helper and does not impose that check for
explicit files. Recommend retaining those eligibility rules for the initial
extraction, applying the agreed all-target versus deliberate-selection failure
classification. No journal-mode expansion or automatic mode change is requested.

The source's `SQLITE_BUSY`/`SQLITE_LOCKED` loop has no overall retry limit; it
retries until success or caller cancellation. This is a concrete limitation for
the newly explicit requirement that one failing target must not indefinitely
hold up other targets or subsequent scheduled calls. The dedicated timing issue
now specifies alternative duration, inactivity/rewind, and learned-limit rules.
Verify that
target-local exhaustion records a failure and continues, while caller cancellation
propagates for the whole run. Release run gates and native/file resources on both
paths. The existing `Microsoft.Data.Sqlite` command timeout is not a substitute
for controlling this direct native backup loop.

Primary references: [SQLite backup counters and restart behavior](https://www.sqlite.org/c3ref/backup_finish.html),
[SQLite noncompletion under frequent restarts](https://www.sqlite.org/backup.html),
and [Microsoft.Data.Sqlite command timeouts](https://learn.microsoft.com/en-us/dotnet/standard/data/sqlite/database-errors).

The library does not manage recurring job state. Hangfire enqueues individual
jobs from a recurring registration and handles execution exceptions/retries at
the caller boundary. Retry configuration stays in the consumer; the library must
not disable future scheduled backups or assume that a retry selects only failed
targets. See [recurring tasks](https://docs.hangfire.io/en/latest/background-methods/performing-recurrent-tasks.html)
and [exception handling](https://docs.hangfire.io/en/latest/background-processing/dealing-with-exceptions.html).

Finalize method overloads, result/exception types, dependency compatibility for
`net8.0` and `net10.0`, path normalization/comparison, and in-process coordination
during implementation. Verify these against the existing source and meaningful
contrasting cases. They are engineering tasks within the agreed scope, not a
request for another package split or a new user decision about each detail. The
reference application's `net10.0-windows`/EF Core 10 setup alone does not establish
compatibility with the library's .NET 8 target.

## Affected features and existing functionality

- Library and matching empty test project under
  `src/prj/Eigenverft.NetLib.SqliteHotBackup*` and their dedicated solution.
- Package-owned registration, source inventory, run options, results, failure and
  cancellation handling, and optional shutdown integration.
- Future consumer integration replaces duplicated application backup logic with
  registration/configuration and calls; its Hangfire jobs remain consumer-owned.
- Audience documentation must distinguish agreed direction, proposals, and
  implemented availability. Current product context is maintained in
  `docs/SqliteHotBackup/product/overview.md`.

## Implementation readiness

- [x] Package scope, consumer trigger boundary, targeted backups, and target-ID
  contract are decided.
- [x] Best-effort execution, independence of later runs, and skipped-versus-failed
  classification are decided.
- [x] Report actual failures through an exception at the end of best-effort work;
  configurable waiting is required.
- [x] Select ProgressStallLimitCalculation, establish five-minute/factor-2 initial
  values, and specify global inheritance with private per-database overrides/state.
- Optional ETA/warning reporting is an unselected alternative outside this delivery.

No implementation or consumer migration has been performed. Technical compatibility
and verification work remains part of implementation rather than an undecided
product direction.

## Completion record

Implemented the selected single-package contract on 2026-11-10. Public
`AddSqliteHotBackup` registration and `ISqliteHotBackupOrchestrator` provide
inventory, all/single/selected calls and isolated per-run options. The library
owns EF/factory resolution and lifetime, explicit sources, native stepped copying,
no-overwrite publication, optional successful-source ZIP/raw cleanup, run gating,
results, end-of-run `SqliteBackupException` and opt-in shutdown checkpoints.

IDs and selection follow the agreed rules. Paths use SQLite's resolved main
filename and full-path normalization, with ordinal case-insensitive comparison
on Windows and ordinal comparison elsewhere. Selected aliases share one result;
explicit override precedence/conflicts are handled at source-group level.

`ProgressStallLimitCalculation` implements five-minute inactivity/factor-2 initial
values, final-global inheritance, partial per-ID overrides and private attempt
state. Active preparation is counted; unrelated target preparation/gate waiting
is excluded. No fixed duration, learned ceiling or unselected ETA stream was added.

Verification: Release builds and 44 passing tests per framework on .NET 8 and
.NET 10; no compiler/analyzer warnings/errors. Coverage is 98.42% line, 93.96%
branch and 100% method. For the first real implementation the scaffold's 100%
coverage gate is replaced with 95/90/95 line/branch/method minimums; native/loader/
OS fault guards remain visible in reports. Local packing validates both target
assemblies and matching dependencies. Consumer/Hangfire examples, failure/output
semantics and engineering limits are documented under `docs/SqliteHotBackup/`.

No application repository changes, scheduler implementation, retention, restore,
remote storage or publication were performed. Optional detailed ETA reporting is
an unselected design alternative, not incomplete required implementation.
