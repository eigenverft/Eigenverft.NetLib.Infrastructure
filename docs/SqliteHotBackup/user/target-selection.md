# SqliteHotBackup target identities and selection

The package exposes all-target, single-target and selected-target operations.
Consumers retain scheduling and select targets by logical IDs rather than paths.

| Source | Registration or discovery | Effective ID |
| --- | --- | --- |
| Registered EF context | Type `MyApp.Data.LocalizationDbContext` | `ef:MyApp.Data.LocalizationDbContext` |
| Mapped EF context | `Discovery.SetDbContextId<LocalizationDbContext>("localization")` | `localization` |
| Explicit file | `Discovery.AddFile("hangfire", path)` | `hangfire` |
| Runtime file | `ISqliteBackupTargetRegistry.AddFile("store", path)` | `store` |
| Automatic provider file | `SqliteBackupFileSource("catalog-store", path)` | `catalog-store` |

The default EF ID uses the fully qualified type name without an assembly version,
database filename or deployment path. Direct DI and factory registration of the
same context type produce one candidate; direct registration takes precedence.
A mapping replaces the default ID and does not register EF or create a second
alias. Stable explicit IDs survive type/path changes when their configuration is
updated. Jobs must be updated if the application changes an effective ID.

## Calls and inventory

```csharp
await backup.BackupAsync("hangfire", cancellationToken);
await backup.BackupAsync(new[] { "identity", "localization" }, cancellationToken);
await backup.BackupAllAsync(cancellationToken);

IReadOnlyList<SqliteBackupTarget> targets =
    await backup.GetTargetsAsync(cancellationToken);
```

Inventory returns `Id`, `Kind`, `Name` and `SourceFile`. A file candidate includes
its configured absolute path; EF candidates have a null `SourceFile` until a run
resolves their connection. The library does not construct backup contexts, open
source files or establish eligibility during inventory; automatic providers may
read their own catalogs. Results identify the actual resolved source file when
available. Source-resolution failures may not yet have a resolved EF filename.

Automatic is the default discovery mode. Manual includes only configured files
and runtime registrations. See [dynamic sources and modes](dynamic-sources.md)
for source registration, per-call snapshots, restart ownership and partial
discovery failures.

Both all-target and selected-target operations accept per-run option overrides:

```csharp
await backup.BackupAllAsync(options =>
{
    options.Zip.FileNamePattern = "Startup_{utc:yyyyMMdd_HHmmss}.zip";
}, cancellationToken);

await backup.BackupAsync(new[] { "identity" }, options =>
{
    options.Zip.Enabled = false;
}, cancellationToken);
```

Overrides operate on a private options snapshot and do not change later calls.
For a single ID with overrides, pass a one-element ID collection.

## Validation and aliases

IDs are trimmed, nonempty and compared with `StringComparer.OrdinalIgnoreCase`.
All source kinds share one namespace. Duplicate IDs are configuration errors;
there are no discovery-order suffixes. Two types with the same full name in
different assemblies need distinct explicit mappings. Mapped contexts and
execution-limit override IDs must exist in the effective inventory when discovery
succeeds. Manual ignores automatic context-ID mappings. A provider-local invalid
snapshot is reported as a discovery failure; collisions between sources remain
configuration errors before output.

When discovery succeeds, the entire selected ID set is validated before creating backup output. Unknown
IDs and empty selections throw instead of selecting all. Repeated selection of
one ID is processed once. Use `BackupAllAsync` deliberately for the full inventory;
an empty inventory is a successful no-op.

If a provider fails, absent IDs cannot be conclusively classified as unknown.
The run backs up known selected targets, reports unavailable selected IDs as
failed and includes source errors in Result.DiscoveryFailures before throwing.
GetTargetsAsync throws SqliteBackupDiscoveryException with the partial inventory.

Distinct IDs may resolve to one database. Paths are obtained from SQLite's actual
main database filename and normalized with `Path.GetFullPath`. Windows comparison
is ordinal case-insensitive; other operating systems use ordinal comparison.
Each selected normalized path is copied once, and `SqliteBackupResult.TargetIds`
retains all selected IDs associated with it. Unselected aliases have no effect.
This does not resolve filesystem identity through symlinks or hard links.

A single explicit execution-limit override on selected aliases takes precedence
over fallback global settings. Identical explicit effective settings are
compatible; conflicting ones fail that source group while other groups continue.
See [execution limits](execution-timing.md) for inheritance and state ownership.

## Outcomes

Individual operational failures do not suppress healthy selected targets or the
ZIP of successes. At the end, `SqliteBackupException.Result` includes successful,
skipped and failed results. Ineligible automatic candidates may be skipped by an
all-target run; deliberate selection of an ineligible candidate fails. Caller
cancellation stops the whole run. Every subsequent call attempts its complete
selection again. See [integration and output](availability.md) for details.
