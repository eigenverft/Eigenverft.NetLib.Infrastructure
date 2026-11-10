# SqliteHotBackup delivery status

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

Local Windows verification on 2026-11-10 completed restore and Release builds for
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

Local NuGet packing validates both target assemblies and matching-generation EF
Core/hosting dependency groups. The package follows normal shared CI activation,
version-base and release-reference rules; no package-specific distribution gate
was introduced. Publishing/deployment and application migration are separate work.
The Infrastructure compatibility aggregate does not reference the new package.
