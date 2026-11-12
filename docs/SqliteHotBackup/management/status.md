# SqliteHotBackup delivery status

## Automatic/manual source increment

The repository now supports default Automatic discovery, opt-in Manual discovery,
scoped consumer-owned file-source providers and a process-local runtime registry.
Manual skips automatic EF/provider resolution. The package still accepts generic
IDs and SQLite paths; application identity, database creation/readiness and catalog
persistence remain outside it. App.Logging has not been changed for this increment.

On 2026-11-12, all 65 tests per .NET 8/.NET 10 target passed without skips or
compiler/analyzer warnings. Coverage is 98.64% line, 93.93% branch and 100% method
on both targets, retaining the existing 95/90/95 gates. Local packing against the
1.0.0.3 API baseline also succeeded. Tests cover fresh scoped queries, constructor
and query failures, partial successful archives, cancellation, runtime changes
during a run, restart ownership and existing backup behavior.

The new source API awaits the next CI/NuGet publication. The verified public
1.0.0.3 below predates it. Consumer adoption is deliberately deferred until the
user confirms publication readiness.

## Initial implementation

The first implementation is complete in the repository as one independently
versioned .NET 8/.NET 10 package. It includes DI registration, lazy EF/factory
discovery, explicit files, stable IDs and inventory, targeted/all-target execution,
private progress-stall limits, safe output publication, optional ZIP/raw cleanup,
best-effort failure aggregation and opt-in shutdown WAL checkpoints.

The consumer boundary remains scheduling and invocation. No Hangfire dependency,
internal schedule, consumer migration, retention, restore or remote storage is
included. Detailed periodic ETA/stall warnings were not selected and remain
outside the delivered implementation. Final results and structured completion/
failure logs are available.

## Verification

Local Windows verification on 2026-11-11 completed restore and Release builds for
both frameworks without compiler/analyzer warnings or errors. All **44 tests per
framework** pass, with no skipped tests. Tests use synthetic databases only. They
cover actual SQLite/WAL copying and lock exhaustion, EF/factory ownership, target
selection and aliases, partial failure with a successful ZIP, later calls,
cancellation cleanup, concurrent invocation, publication collisions, ZIP failures
and opt-in shutdown checkpoints. Manual-clock tests cover long productive copies,
growth, rewind debt, boundaries and independent attempt state.

Coverlet reports **98.42% line, 93.96% branch and 100% method coverage** on both
frameworks. The empty scaffold's 100% gate is replaced for this package with
95% line, 90% branch and 95% method minimums. The full library remains included;
no product fault guards were hidden with coverage exclusions. Remaining uncovered
paths include defensive native initialization/finish failures, reflection type
loader failures and OS-specific cleanup/provider guards. Their coverage would
require additional fault injection rather than ordinary synthetic integration.

The default five-minute inactivity/factor-2 rewind rule has behavioral tests, but
its values remain theoretically reasoned starting points. Test success does not
establish throughput, giant-database performance, every filesystem/native fault,
trimmed/AOT compatibility or other operating systems. There is no fixed total
copy duration or guaranteed maximum wait behind a productive serial source.

## Delivery boundary

Version [1.0.0.3](https://www.nuget.org/packages/Eigenverft.NetLib.SqliteHotBackup/1.0.0.3)
is published on nuget.org. The [shared CI run](https://github.com/eigenverft/Eigenverft.NetLib.Infrastructure/actions/runs/42980372323)
completed successfully at source commit `295e6a2`, and the public feed, package page
and package download were verified on 2026-11-11. The downloaded artifact includes
both target assemblies and the expected EF Core/hosting dependency groups. It is
available for consumer evaluation; live application integration remains untested.

Local NuGet packing validates both target assemblies and matching-generation EF
Core/hosting dependency groups. The package follows normal shared CI activation,
version-base and release-reference rules; no package-specific distribution gate
was introduced. Application deployment and migration remain separate work.
The Infrastructure compatibility aggregate does not reference the new package.
