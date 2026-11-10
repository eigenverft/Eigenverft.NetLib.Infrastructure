# SqliteHotBackup execution limits

`ExecutionLimit` controls when to stop one source attempt. The implemented
`ProgressStallLimitCalculation` defaults to five minutes without copied-prefix
advance and a maximum observed rewind-work factor of 2.0. These are initial
engineering values derived from [hypothetical scenarios](../developer/execution-limit-scenarios.md),
not GlobalServer measurements or benchmarked optima.

## Inheritance and overrides

```csharp
options.ExecutionLimit = new ProgressStallLimitCalculation
{
    NoProgressTimeout = TimeSpan.FromMinutes(5),
    MaxRewindWorkFactor = 2.0
};
options.ConfigureExecutionLimit("archive", limit =>
{
    limit.NoProgressTimeout = TimeSpan.FromMinutes(15);
    // MaxRewindWorkFactor still inherits the final global value.
});
```

Using `new ProgressStallLimitCalculation()` gives the same defaults. Automatically
discovered contexts and explicit files inherit the recipe. Per-ID callbacks run
on private copies of the final global settings; callbacks for the same ID apply
in registration order. Register existing IDs and keep callbacks deterministic.
Timeouts must be positive and at most `int.MaxValue` seconds; factors must be
positive and finite. Invalid target-specific settings fail that target attempt.

Each physical source attempt has fresh clocks and counters. There is no shared
run-wide allowance and no history affecting later calls. On selected aliases,
a single explicit override wins over fallback defaults; identical explicit
effective settings are allowed. Conflicting explicit settings fail that source
group and other groups continue. Unselected aliases do not contribute settings.

## Calculation

The monitor uses monotonic elapsed time. Active source preparation counts toward
initial inactivity. Metadata resolution for other targets and waiting for the
run gate do not count; selected aliases contribute their active preparation time
to the one deduplicated copy. The first copied-prefix advance clears that initial
preparation cost. Publication and ZIP have separate error/cancellation handling
and are not governed by page-progress estimation.

After a returned `SQLITE_OK` or `SQLITE_DONE` step, observe total pages `N`,
remaining pages `R`, and copied-prefix counter `P = N - R`. Start with previous
`P = 0`, `Nmax = 0`, rewind work `W = 0`, and last advance at attempt start:

1. Set `Nmax = max(Nmax, N)`.
2. An increase in `P` refreshes the inactivity clock.
3. A decrease adds `previousP - P` to `W`.
4. Save `P` for the next comparison. An unchanged prefix does neither.
5. Before further controlled work, fail at inactivity **greater than or equal to**
   `NoProgressTimeout`, or `W > MaxRewindWorkFactor * Nmax`.

A returned `SQLITE_DONE` succeeds. A busy/locked response waits and retries without
refreshing inactivity. Growth alone does not count as advance, but increases
`Nmax` and therefore the rewind allowance. Recopying can refresh inactivity but
never erases earlier rewind loss. Counters can rewind after source writes; see
the [SQLite API](https://www.sqlite.org/c3ref/backup_finish.html) and
[backup discussion](https://www.sqlite.org/backup.html).

For `Nmax = 1,000`, two observed drops from `P = 900` to `100` total 1,600 lost
pages and remain below the 2,000 allowance. The third totals 2,400 and fails.
Equality at 2,000 is allowed. Growth to 10,000 pages raises the allowance to
20,000 without clearing accumulated loss. This is an observed counter metric,
not an exact count of all internal restarts or physical bytes recopied.

## Scope and practical limits

An advancing copy can run for hours without violating these limits. Four minutes
of interruption followed by advance can be tolerated. Continuous growth, repeated
short waits between advances or unobserved internal restarts can still produce
long runs; serial targets have no guaranteed maximum wait behind a productive
copy. There is no fixed total deadline or learned duration ceiling.

A local execution-limit failure permits other sources and successful archives to
continue, then contributes to `SqliteBackupException.Result`. Caller cancellation
stops the entire run instead. Checks are cooperative: a synchronous native call,
context factory or filesystem operation cannot be forcibly interrupted. SQLite
connection/command timeouts also apply during preparation; they do not replace
the native step monitor.

Final results include status/reason, duration, total/remaining pages and observed
rewind work. Structured logs report completion and failures. Periodic ETA output,
statistical history and stall warnings were discussed but not selected and are
not implemented. The theoretical analysis remains a design rationale, not a
promise about throughput on a particular host.
