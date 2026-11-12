# Automatic and manual target discovery

These APIs belong to the new source-discovery increment. The last verified public package, 1.0.0.3, predates them; use a subsequently published build containing this API. Consumer registration, database creation, migrations and scheduling remain outside the package.

## Modes

`Discovery.Mode` defaults to `SqliteBackupDiscoveryMode.Automatic`.

| Target source | Automatic | Manual |
| --- | --- | --- |
| `Discovery.AddFile` configuration and per-run files | Included | Included |
| `ISqliteBackupTargetRegistry` runtime registrations | Included | Included |
| Registered concrete DbContexts/factories | Included when DiscoverDbContexts is true | Not discovered |
| Providers registered through AddSqliteBackupSourceProvider | Queried afresh | Not resolved or invoked |

The existing DiscoverDbContexts boolean still controls only EF discovery in Automatic mode. Existing applications keep their default behavior. Manual ignores automatic context-ID mappings; execution-limit override IDs must still identify an effective target when discovery succeeds.

## Explicit runtime registration

```csharp
services.AddSqliteHotBackup(options =>
{
    options.Discovery.Mode = SqliteBackupDiscoveryMode.Manual;
    options.Backup.Directory = backupDirectory;
});

ISqliteBackupTargetRegistry targets =
    serviceProvider.GetRequiredService<ISqliteBackupTargetRegistry>();

// After the consumer has finished creating/migrating its database:
targets.AddFile("customer-store", databaseFile);

await serviceProvider.GetRequiredService<ISqliteHotBackupOrchestrator>()
    .BackupAllAsync(cancellationToken);

// Removes only this runtime registration, never the database:
bool removed = targets.Remove("customer-store");
```

The built-in registry is singleton, thread-safe and process-local. It does not open, create, migrate, delete or persist databases. Restore registrations from the consumer's persistent catalog after restart. AddFile normalizes an ID and absolute filename immediately, and rejects a duplicate runtime ID even when its path is unchanged. Remove returns false for an absent runtime ID and cannot remove a configured file or automatic target.

GetSourcesAsync on the registry returns a detached read-only snapshot of only its own entries. GetTargetsAsync on the orchestrator combines all enabled sources. Physical database access is deferred to backup/checkpoint resolution.

IDs are trimmed and ordinal case-insensitive across all sources. Relative filenames resolve against AppContext.BaseDirectory. Do not remove/delete a database while an existing backup may still hold its snapshot; removing registration does not cancel that run.

## Automatic source providers

Register a consumer-owned implementation once:

```csharp
services.AddSqliteHotBackup();
services.AddSqliteBackupSourceProvider<CatalogBackupFiles>();
```

The provider implements:

```csharp
Task<IReadOnlyList<SqliteBackupFileSource>> GetSourcesAsync(
    CancellationToken cancellationToken = default);
```

Return each ready database as `new SqliteBackupFileSource(id, path)`. The consumer chooses stable IDs, filenames, readiness, application/tenant mapping, catalog persistence and any database-count limits. The package has no ApplicationId, schema-creation or application-directory convention. A provider may read a catalog, enumerate a configured directory or consult another application-owned source. Automatic mode does not imply an unconfigured filesystem scan.

The extension registers a scoped provider and defers its resolution until that provider's query. Re-registering the same type is idempotent. A pre-existing registration of that concrete provider retains its lifetime. Provider constructors and methods are not called in Manual mode. Register through the extension; registering only the interface does not add an automatic source.

Each inventory, backup or enabled shutdown checkpoint gets a new DI scope and queries providers once. Providers may depend on scoped services and are disposed with that scope. Honor the supplied cancellation token; per-database ExecutionLimit does not impose a timeout on catalog queries.

The package normalizes and copies each returned collection before moving to the next provider. Do not mutate a collection while it is being returned/enumerated. Once target acquisition finishes, the run uses a fixed inventory; later changes apply to the next call. Independent providers are not a cross-catalog transaction. Inventory and backup are separate calls and can legitimately see different snapshots.

## Errors and selection

A provider's constructor/query failure or invalid returned snapshot rejects that provider's whole snapshot. This includes null collections/items, invalid IDs/paths and duplicate IDs within that provider. Other sources remain usable. Cross-source ID collisions remain configuration errors before backup output, as with existing configured targets.

Backup calls attempt known selected targets, archive successes when enabled and then throw SqliteBackupException if discovery or execution failed. Inspect Result.DiscoveryFailures for source names/reasons as well as Result.Targets and Result.Archive. An unavailable source is never silently treated as empty.

GetTargetsAsync cannot return a successful complete inventory when a source failed. It throws SqliteBackupDiscoveryException with Targets containing the partial inventory and Failures containing the source-query errors.

A complete inventory still rejects unknown selected IDs or execution-limit override IDs before output. With failed sources, an absent ID cannot conclusively be classified as unknown. Known selected targets proceed; missing selected IDs receive failed results, and discovery failures remain in the report. Validation is retried against the next fresh inventory. Caller cancellation stops discovery/the run and does not become an operational provider failure.

All file targets continue to use independent read-only SQLite resolution, existing source-alias deduplication, execution limits and publication behavior. Shutdown checkpoints use the same mode/sources and log discovery failures while checkpointing known targets; they never trigger a backup.
