using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Eigenverft.NetLib.SqliteHotBackup
{
    /// <summary>Controls whether automatic sources participate in target discovery.</summary>
    public enum SqliteBackupDiscoveryMode
    {
        /// <summary>Include enabled EF discovery and registered providers, as well as explicit files.</summary>
        Automatic,
        /// <summary>Use only configured files and the runtime registry; do not invoke automatic providers or EF discovery.</summary>
        Manual
    }

    /// <summary>A consumer-owned logical ID and SQLite filename. Relative paths resolve against AppContext.BaseDirectory.</summary>
    public sealed record SqliteBackupFileSource(string Id, string FilePath)
    {
        internal static SqliteBackupFileSource Normalize(string id, string path)
        {
            string normalizedId = TargetId.Normalize(id);
            ArgumentException.ThrowIfNullOrWhiteSpace(path);
            return new(normalizedId, Path.GetFullPath(path, AppContext.BaseDirectory));
        }
    }

    /// <summary>Supplies a fresh, complete file-target snapshot without creating or initializing backup databases.</summary>
    /// <remarks>
    /// Register with AddSqliteBackupSourceProvider. Each automatic inventory, backup or enabled shutdown checkpoint
    /// uses a new DI scope. Providers own their catalog semantics and must honor cancellation. No filesystem scan is implied.
    /// </remarks>
    public interface ISqliteBackupSourceProvider
    {
        /// <summary>Returns the currently eligible IDs and paths. The library copies the result and does not retain the collection.</summary>
        Task<IReadOnlyList<SqliteBackupFileSource>> GetSourcesAsync(CancellationToken cancellationToken = default);
    }

    /// <summary>A process-local, thread-safe source of explicitly registered file targets, used in both discovery modes.</summary>
    /// <remarks>Registrations are not persisted. The consumer restores them after restart and controls database lifetime.</remarks>
    public interface ISqliteBackupTargetRegistry : ISqliteBackupSourceProvider
    {
        /// <summary>Adds one file without opening it or starting a backup. Duplicate IDs are rejected even for the same path.</summary>
        void AddFile(string id, string path);
        /// <summary>Removes a runtime registration only; never deletes a database or changes an in-flight run's snapshot.</summary>
        bool Remove(string id);
    }
}
