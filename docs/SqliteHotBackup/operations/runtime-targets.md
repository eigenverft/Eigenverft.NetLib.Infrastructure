# Operating dynamic SQLite backup targets

The application owns database lifetime and supplies ready SQLite files to SqliteHotBackup. The package does not create or migrate those source databases. Scheduling, authentication for administrative calls and persisted application catalogs remain application responsibilities.

Automatic mode is the default. It discovers eligible registered EF types and queries configured source providers at each inventory, backup or enabled shutdown checkpoint. Provider queries can perform application-owned I/O. There is no continuous watcher or separate required rescan step.

Manual mode suppresses EF discovery and automatic providers. Configured files and explicit runtime registrations remain active. The built-in ISqliteBackupTargetRegistry is held in memory; after restart the application must restore entries from its own persistent catalog. An empty inventory is a successful no-op, so applications needing a nonempty set must verify catalog restoration themselves.

Registration does not start a backup. Add a runtime target after the source is initialized and ready. Changes during a run affect later snapshots. Removing a registration does not cancel an existing backup, delete the file or remove a static registration. Coordinate physical source deletion with the owner of any active backup.

After a source query fails, SqliteBackupException.Result.DiscoveryFailures names the unavailable source. Other known targets may already have successful snapshots and an archive. Inventory queries instead throw SqliteBackupDiscoveryException with partial Targets and Failures. Diagnose the provider's catalog access, constructor dependencies, returned IDs/paths and cancellation behavior; do not assume an unavailable source means there are no databases.

Stable IDs are independent of deployment paths. Duplicate IDs across sources remain configuration errors; distinct IDs pointing to one physical filename share one selected copy. Runtime AddFile rejects a reused ID; remove the previous registration before deliberately replacing it and account for already captured snapshots.

Provider queries honor caller cancellation cooperatively. The progress-stall calculation governs source preparation/copying, not external catalog-query duration. Give application-owned queries suitable cancellation/timeouts. Enabled shutdown WAL checkpoints use the same source rules and log individual discovery/checkpoint problems; no backup is scheduled by shutdown integration.

The source-discovery extension is verified locally for .NET 8/.NET 10. Version 1.0.0.3 predates this API; CI/NuGet publication and consumer adoption are separate steps.
