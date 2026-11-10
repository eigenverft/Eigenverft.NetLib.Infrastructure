# Define ProgressStallLimitCalculation defaults and optional reporting

Created: 2026-11-10
Last edited: 2026-11-10
Status: Completed for the selected execution-limit scope on 2026-11-10

The decision analysis below is preserved from before implementation. A1.2 is now implemented. A2.2 was not selected; its detailed proposal remains historical context rather than an outstanding delivery requirement. See the completion record.

## Issue shaping and context

This is the focused timing decision for
[the managed backup extraction](2026-11-10-sqlitehotbackup-library-extraction.md).
The package is still an empty scaffold. Target selection, configurable waits,
best-effort execution, and an exception at the end of a partially failed run are
agreed. `ProgressStallLimitCalculation` is selected. At the user's request to
establish reasonable values, the initial defaults are five minutes without
advance and a maximum observed rewind-work factor of 2.0. They are derived from
freely chosen theoretical scenarios, not GlobalServer measurements or benchmarks.
The user also selected global inheritance with per-target overrides.

The design separates **A1: abort policy**, now selected, from **A2: optional
reporting**, still open. The consumer wants transient hiccups and sudden database
growth to be tolerated without permanently blocking other work. Source code is
the GlobalServer/PWA reference described in the parent issue; its busy/locked loop
is currently unbounded without cancellation.

### Configuration vocabulary

The public configuration name **`ExecutionLimit` is decided**. It describes the
condition for stopping the current target attempt, not restoration, future
scheduler retries, or a maximum database size.

The concrete calculation name **`ProgressStallLimitCalculation` is decided**.
The intended construction is:

```csharp
options.ExecutionLimit = new ProgressStallLimitCalculation();
```

`ProgressStallLimitCalculation` explicitly names what is evaluated: stalled
advance and excessive repeated work. It uses the A1.2 rule below with the newly
established five-minute/factor-2 defaults. The type is not implemented; exact
constructor/inner property/override method signatures remain API sketches.
The recipe is global by default, inherited by automatic and explicit targets,
and overridden per target ID. Unspecified settings inherit the final global
recipe; each target receives a private effective copy and fresh attempt counters.
There is no shared budget across databases. A2 reporting remains a separate choice.

### Initial values and their theoretical basis

The selected initial settings are `NoProgressTimeout = five minutes` and
`MaxRewindWorkFactor = 2.0`, with no fixed total-duration or historical ceiling.
At freely assumed effective rates of 1/10/100 MiB/s, a 10 GiB copy needs about
2 h 51 min / 17 min / 1 min 42 s. That spread supports protecting against lack of
advance rather than imposing a fixed total deadline.

Five minutes permits hypothetical 90-second and four-minute interruptions while
ending a permanent uninterrupted lock sooner than a fifteen-minute default.
Factor 2.0 permits two observed losses of 80% of one database volume (1.6 volumes)
but fails on the third (2.4). Factor 1.0 would fail on the second, while the prior
unselected factor-3 proposal would fail only on the fourth such event.
Growth from 1 to 10 GiB raises the allowance from 2 to 20 GiB of equivalent
observed page loss, retaining accumulated loss. These values are engineering
starting points, not experimentally established optima.

The complete hypothetical input tables, arithmetic, boundary cases, and override
semantics are documented in
`docs/SqliteHotBackup/developer/execution-limit-scenarios.md`. No GlobalServer
step constants, actual database contents, hardware measurements, or SQLite runs
were used in that derivation.

### Shared measurements and exit behavior

For each returned native backup step, observe total pages `N`, remaining pages
`R`, and copied-prefix counter `P = N - R`. Start with previous `P = 0`,
`Nmax = 0`, rewind work `W = 0`, and last-advance time equal to attempt start.
Use a monotonic clock starting immediately before resolving/opening the target
source and ending when `SQLITE_DONE` returns. Publication and batch ZIP handling
remain separate phases, not subject to this page-progress estimator.

SQLite can rewind counters after source writes and may never complete under
frequent restarts; a successful step alone is not convergence. Read counters in
the controlled backup path, not concurrently from a monitoring thread. Check
cutoffs before further controlled operations. A returned `SQLITE_DONE` completes
the copy; caller cancellation stops the run; a local limit fails only the target
and permits the others to proceed. Release resources/gates on all exits. None of
these policies promises forced interruption of a synchronous native/I/O call.

### A1: selected rule and compared alternatives

**A1.1 Fixed attempt deadline (not selected).** Compared value 30 minutes, global with per-target
overrides. Stop before further work when elapsed time reaches the limit. No
counter, growth event, or historical sample extends it. Simple cooperative bound;
also stops a productive copy that needs more time.

**A1.2 ProgressStallLimitCalculation — selected.** Initial defaults:
five minutes without an increase in `P`, and `W > 2 * Nmax` as the
observed rewind-work limit. On each sample set `Nmax = max(Nmax, N)`; an increase
in `P` refreshes last-advance time, a decrease adds `previousP - P` to `W`, and an
unchanged `P` does neither. Save `P` as the next previous value. Never reset `W`
when recopying advances. Stop if inactivity reaches five minutes or rewind work
exceeds its limit. Both settings are overrideable per target. Growth alone does
not reset inactivity but scales the rewind allowance. No fixed total duration is
imposed; long advancing copies can delay later serial targets. `W` is an observed
counter metric, not an exact count of SQLite restarts or physical recopied bytes.
Example: three rewinds from 900 to 100 with `Nmax = 1,000` yield 2,400 lost pages,
exceeding the 2,000-page allowance; two such rewinds remain below the limit.

**A1.3 History-derived duration ceiling (not selected).** This compared alternative
would add a learned limit to A1.2. Use an in-memory
history of the last 20 successful non-zero-page attempts per resolved source path
and SQLite page size. One sample is attempt seconds divided by final page count;
exclude failed/canceled attempts, publication/ZIP time, and duplicate alias samples.
Before five samples exist, use A1.2 alone. Otherwise freeze mean `mu` and population
deviation `sigma` at attempt start. Once pages are known:

```text
allowedSeconds = max(300, 3 * Nmax * (mu + 4 * sigma))
deadline = attemptStart + allowedSeconds
```

Only increasing `Nmax` recalculates that deadline; current slowness cannot retrain
or extend it. Exceeding it fails even an advancing copy. Process restart or a new
path/page size gives a cold start. A new slow regime can cause repeat timeouts
without contributing a successful training sample. Factors are unmeasured policy
proposals, not statistical guarantees. For `mu = 0.001`, `sigma = 0.0002`, the
allowance is 540 seconds at 100,000 pages and 5,400 seconds at 1,000,000 pages.

### A2: independently choose reporting

**A2.1 Completion summaries only.** Report duration, final outcome/reason, page
counts, and rewind work at target/run completion. No ETA stream or stall warning.
A1.3, if selected, still gathers the history required by its abort policy.

**A2.2 Current-attempt ETA and stall warning — recommended.** Close each window at
the first returned sample after at least 10 seconds, using actual elapsed time;
do not invent intermediate windows after a long operation. Compute
`(P_end - P_start) / elapsedSeconds`, including waits and stepping pauses. Keep
at most six complete windows, including zero rates. A rewind discards the current
window and rate history, not `W`. Require three complete windows. Calculate rate
mean/deviation `muR`, `sigmaR`; if `muR - sigmaR > 0`, publish
`ceil(R / (muR - sigmaR))` seconds as ETA, otherwise unavailable. Growth changes
`R` and hence ETA without being counted as copied progress. Publish at most one
progress summary per 60 seconds and always a final summary. Include target IDs,
elapsed time, `N`, `R`, rate, ETA/unavailable, and `W`. After 60 seconds without
increasing `P`, warn once; rearm only on later advance. If a shorter configured
guard expires first there may be no warning. Reporting never cancels or extends
an attempt. Fewer than 30 seconds of copy time may produce only a final summary.

The independent choices and full examples are documented for consumers in
`docs/SqliteHotBackup/user/execution-timing.md`.

## Ambiguities and possible misunderstandings

- [x] Distinguish attempt-duration limits, inactivity/repeated-work protection,
  and rate/ETA reporting. An estimate is not itself a selected abort policy.
- [x] Define counter changes, timers, growth, rewinds, cold starts, and threshold
  actions for every candidate above. No vague "useful progress" predicate remains.
- [x] Distinguish established theoretical initial values (five minutes/factor 2)
  from measurements and from unselected alternative/reporting values. A
  progressing copy still has no guaranteed duration under the selected rule.
- [x] Global configuration is inherited; execution state is per database/attempt.

## Open decisions

None blocking the selected implementation. Required final results and completion
logs are delivered. A2.2 periodic ETA/stall warnings remain an unselected
alternative outside this delivery.

## Decisions already made

Configurable per-target waiting; preserve best-effort processing and completed
backups; local failure does not disable future calls; caller cancellation remains
whole-run cancellation. Triggers and scheduler retries remain with the consumer.
The single public configuration concept for the stopping rule is named
`ExecutionLimit`, using `ProgressStallLimitCalculation`. Initial values are five
minutes without observed advance and factor 2.0 for accumulated rewind work.
No fixed total duration or learned abort model is selected. These values were
established under the user's request to choose them from theoretical scenarios.

Global defaults apply to all discovered/explicit targets, with per-ID overrides
for explicitly changed settings. Materialize private effective copies after global
configuration is complete. For selected aliases resolving to one source, a single
explicit override takes precedence over fallback defaults; conflicting explicit
effective configurations fail that source group, while other source groups
continue. Identical overrides are compatible. Unselected aliases add no override.
Always use fresh runtime counters for each deduplicated database attempt.

## Possible implementation approaches

Implement ProgressStallLimitCalculation in the existing native step/retry path. Keep A2
reporting separate from cutoff decisions. Add no persistence or scheduler logic.
Validate positive settings, use monotonic elapsed times, and test the specified
rules and evaluate the initial values before claiming measured release behavior.

## Affected features and existing functionality

Native file backup, per-target outcome aggregation, IDs/result associations,
source initialization, and logging. The existing SQLite engine has fixed step
sizes/delays and unbounded busy retries; it has no model, timing-policy selection,
or historical statistics. Publication and ZIP remain distinct phases.

## Implementation readiness

- [x] Concrete alternatives and a decision path are documented.
- [x] The public stopping-rule configuration is named `ExecutionLimit`.
- [x] `ProgressStallLimitCalculation`, five-minute inactivity, factor 2.0, and
  global defaults with per-target overrides are selected for initial implementation.
- Optional ETA/warning reporting is an unselected alternative outside this delivery.

Primary sources: [SQLite counters](https://www.sqlite.org/c3ref/backup_finish.html)
and [restarts/noncompletion](https://www.sqlite.org/backup.html).

## Completion record

Implemented A1.2 as `ProgressStallLimitCalculation` with five-minute inactivity,
factor-2 accumulated observed rewind work, private snapshots/state, monotonic
measurement, final-global inheritance and per-ID partial overrides. Active source
preparation counts toward initial inactivity; time spent resolving other targets
or waiting for the gate does not. Selected aliases sum their active preparation
cost for the one physical-source copy. DONE succeeds, a local limit fails only
its source, caller cancellation stops the run, and later calls start fresh.

Deterministic tests cover productive runs lasting longer than fixed deadlines,
inactivity equality, growth without fictitious progress, accumulated rewind debt,
rewind equality/exhaustion, independent later attempts and override isolation.
Real SQLite tests cover exclusive-lock exhaustion, cleanup and a subsequent
successful attempt. The complete package suite passes 44 tests on each framework.

Required final outcomes, reasons, durations, page counters and rewind totals are
available in results; structured logs report completion/failure. No statistical
history, fixed overall deadline, or unselected A2.2 reporting was implemented.
The theoretical analysis remains explicitly unmeasured and distinct from tests.
Native/I/O limits are cooperative, not forced interruption guarantees.
