# Initial execution limits from hypothetical backup scenarios

This pre-implementation analysis records how the consumer
selected `ExecutionLimit` and `ProgressStallLimitCalculation` and requested
reasonable initial values derived from theoretical scenarios. This analysis uses
freely chosen database sizes, effective copy rates, growth events, and stalls.
It does not use GlobalServer timings, hardware measurements, actual databases,
or benchmark/test results.

The initial design values selected from these scenarios are:

- `NoProgressTimeout`: **five minutes**.
- `MaxRewindWorkFactor`: **2.0** times the largest observed page count.
- No fixed maximum complete-copy duration and no history-derived abort limit.
- Global configuration inherited by every target, with optional target-ID
  overrides. Every database attempt gets independent counters and clocks.

These are reasoned starting values, not measured optima or release-performance
guarantees. The implementation now exposes these settings; the theoretical
derivation remains independent of the later runtime tests.

## Size and copy-rate assumptions

MiB and GiB use powers of 1024. Assume a constant effective copy rate during each
example. The rate already represents copying under whatever storage/load/stepping
conditions that hypothetical system has; no application-specific step constants
are used. Exclude initialization, interruption, repeated work, final publication,
and ZIP creation from this simple `duration = volume / rate` table.

| Source volume | 1 MiB/s | 10 MiB/s | 100 MiB/s |
| --- | --- | --- | --- |
| 100 MiB | 1 min 40 s | 10 s | 1 s |
| 1 GiB | 17 min 4 s | 1 min 42 s | 10 s |
| 10 GiB | 2 h 50 min 40 s | 17 min 4 s | 1 min 42 s |
| 100 GiB | 28 h 26 min 40 s | 2 h 50 min 40 s | 17 min 4 s |

Even these simple assumptions span seconds to more than a day of useful work.
A fixed three/five-minute overall cutoff would reject ordinary examples solely
because their volume/rate combination needs more time. The selected timeout
therefore applies to lack of observed advance, not total duration.

## Choosing the inactivity value

Assume a lock or temporary storage interruption produces no counter advance,
then the next controlled operation can resume normal copying.

| Hypothetical interruption | Two-minute cutoff | Five-minute cutoff | Fifteen-minute cutoff |
| --- | --- | --- | --- |
| 90 seconds | Continue | Continue | Continue |
| Four minutes | Fail after two minutes | Continue | Continue |
| Ten minutes | Fail after two minutes | Fail after five minutes | Continue |
| Permanent lock | Next target after cutoff | Next target after cutoff | Next target after cutoff |

Five minutes tolerates the selected short and multi-minute hiccups while limiting
one uninterrupted lock episode much sooner than fifteen minutes. A source that
regularly needs longer interruptions can override this setting, for example to
fifteen minutes. Database growth by itself does not justify a larger inactivity
timeout: useful counter advance keeps refreshing the clock regardless of volume.
Expiration is checked cooperatively; these times do not promise forced native
I/O interruption at an exact wall-clock instant.

## Choosing the rewind allowance

Use the observed copied-prefix counter `P = total pages - remaining pages`.
Whenever `P` drops, accumulate `previousP - P` in `W`. Keep the largest observed
total `Nmax`; fail if `W > factor * Nmax`. Positive recopying refreshes inactivity
but does not remove accumulated rewind work.

Assume each observed rewind loses 80% of the current database volume and size
does not change between those events:

| Allowance factor | One loss: 0.8 volumes | Two: 1.6 volumes | Three: 2.4 volumes | Four: 3.2 volumes |
| --- | --- | --- | --- | --- |
| 1.0 | Continue | Fail | Already failed | Already failed |
| 2.0 | Continue | Continue | Fail | Already failed |
| 3.0 | Continue | Continue | Continue | Fail |

Factor 1.0 is relatively strict for two large transient setbacks. Factor 3.0
permits another nearly complete recopy before giving up. Factor **2.0** permits
two substantial setbacks while drawing the line earlier for continuing churn.
This replaces the previous unselected factor-3 proposal.

For exact counter arithmetic, `Nmax = 1,000` and a drop from `P = 900` to `P = 100`
lose 800 observed pages. Two drops total 1,600, below the 2,000 allowance; three
total 2,400 and exceed it. This is observed counter loss, not an exact accounting
of physical bytes copied or every internal SQLite restart.

## Growth and practical cost

For growth from 1 GiB to 10 GiB, the allowance grows from two to twenty GiB of
equivalent observed page loss. Previously accumulated `W` is retained. Size growth
alone does not refresh the inactivity clock; actual advance does.

| Current largest volume | Factor-2 rewind allowance | Equivalent extra work at an assumed 10 MiB/s |
| --- | --- | --- |
| 100 MiB | 200 MiB | 20 s |
| 1 GiB | 2 GiB | 3 min 25 s |
| 10 GiB | 20 GiB | 34 min 8 s |
| 100 GiB | 200 GiB | 5 h 41 min 20 s |

The volume-scaled rule deliberately tolerates more repeated work on a large
database. It does not guarantee a short wait for later serial targets. Frequent
short locks with a real advance between them may also accumulate long elapsed
time without exceeding five minutes of uninterrupted inactivity. A minimum
throughput or total-duration rule is not part of this calculation. A strict
service deadline would require another policy or caller cancellation.

## Global inheritance and target overrides

The global `ExecutionLimit` is a configuration recipe. Automatically discovered
DbContexts and explicit file targets inherit it. Target-ID overrides change only
the settings explicitly supplied; other settings inherit the final global
configuration. Apply overrides to private target copies rather than mutating the
shared default. Implemented configuration:

```csharp
options.ExecutionLimit = new ProgressStallLimitCalculation
{
    NoProgressTimeout = TimeSpan.FromMinutes(5),
    MaxRewindWorkFactor = 2.0
};

options.ConfigureExecutionLimit("archive", limit =>
{
    limit.NoProgressTimeout = TimeSpan.FromMinutes(15);
    // MaxRewindWorkFactor still inherits 2.0.
});
```

The implemented semantics are: global defaults, per-ID overrides, and fresh
per-database/per-invocation clocks and counters. A global recipe is not one shared
run budget. Another failed database cannot consume this target's allowance.

When selected IDs deduplicate to one source, use its single explicit override
instead of fallback defaults. Identical explicit overrides are compatible;
different explicit effective settings are a configuration failure for that
source group. Do not silently choose one. Continue other source groups and
include the conflict in the final failure report. Unselected aliases do not
introduce overrides into the requested selection.

## Subsequent implementation verification

Deterministic tests now cover the specified timing/counter transitions, growth,
rewind debt, independent attempts and override isolation. Real SQLite tests cover
copying, lock exhaustion, cancellation and later calls. They verify behavior,
not these hypothetical throughput assumptions or giant-database performance.
No SQLite run contributed to this derivation.
