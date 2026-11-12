using System;
using System.Collections.Generic;

using Microsoft.EntityFrameworkCore;

namespace Eigenverft.NetLib.SqliteHotBackup
{
    /// <summary>Configures discovery, output, and execution of SQLite backups.</summary>
    public sealed class SqliteHotBackupOptions
    {
        private readonly Dictionary<string, List<Action<ProgressStallLimitCalculation>>> _overrides = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>Gets or sets the sources to discover and register.</summary>
        public SqliteBackupDiscoveryOptions Discovery { get; set; } = new();
        /// <summary>Gets or sets raw backup output settings.</summary>
        public SqliteBackupOutputOptions Backup { get; set; } = new();
        /// <summary>Gets or sets optional archive settings.</summary>
        public SqliteBackupZipOptions Zip { get; set; } = new();
        /// <summary>Gets or sets optional shutdown checkpoint behavior.</summary>
        public SqliteBackupShutdownOptions Shutdown { get; set; } = new();
        /// <summary>Gets or sets the default recipe, evaluated independently for each source attempt.</summary>
        public ProgressStallLimitCalculation ExecutionLimit { get; set; } = new();

        /// <summary>Overrides supplied settings for an ID; other settings inherit the final global recipe.</summary>
        public void ConfigureExecutionLimit(string targetId, Action<ProgressStallLimitCalculation> configure)
        {
            string id = TargetId.Normalize(targetId);
            ArgumentNullException.ThrowIfNull(configure);
            if (!_overrides.TryGetValue(id, out List<Action<ProgressStallLimitCalculation>>? actions))
            {
                actions = new();
                _overrides.Add(id, actions);
            }
            actions.Add(configure);
        }

        internal IEnumerable<string> OverrideIds => _overrides.Keys;
        internal bool HasOverride(string id) => _overrides.ContainsKey(id);

        internal ProgressStallLimitCalculation LimitFor(string id)
        {
            ProgressStallLimitCalculation result = ExecutionLimit.Copy();
            if (_overrides.TryGetValue(id, out List<Action<ProgressStallLimitCalculation>>? actions))
            {
                foreach (Action<ProgressStallLimitCalculation> action in actions)
                {
                    action(result);
                }
            }
            result.Validate();
            return result;
        }

        internal SqliteHotBackupOptions Copy()
        {
            ArgumentNullException.ThrowIfNull(Discovery);
            ArgumentNullException.ThrowIfNull(Backup);
            ArgumentNullException.ThrowIfNull(Zip);
            ArgumentNullException.ThrowIfNull(Shutdown);
            ArgumentNullException.ThrowIfNull(ExecutionLimit);
            SqliteHotBackupOptions copy = new()
            {
                Discovery = Discovery.Copy(),
                Backup = new() { Directory = Backup.Directory, FileNamePattern = Backup.FileNamePattern },
                Zip = new() { Enabled = Zip.Enabled, PublishDirectory = Zip.PublishDirectory, FileNamePattern = Zip.FileNamePattern, DeleteRawBackupsAfterZip = Zip.DeleteRawBackupsAfterZip },
                Shutdown = new() { CheckpointWal = Shutdown.CheckpointWal },
                ExecutionLimit = ExecutionLimit.Copy()
            };
            foreach (KeyValuePair<string, List<Action<ProgressStallLimitCalculation>>> item in _overrides)
            {
                copy._overrides.Add(item.Key, new(item.Value));
            }
            return copy;
        }

        internal void Validate()
        {
            if (!Enum.IsDefined(Discovery.Mode))
            {
                throw new ArgumentOutOfRangeException(nameof(Discovery), "Unknown SQLite backup discovery mode.");
            }
            ExecutionLimit.Validate();
            ArgumentException.ThrowIfNullOrWhiteSpace(Backup.Directory);
            Internal.BackupFiles.FileName(Backup.FileNamePattern, "source", DateTimeOffset.UnixEpoch, ".db");
            if (Zip.Enabled)
            {
                Internal.BackupFiles.FileName(Zip.FileNamePattern, "archive", DateTimeOffset.UnixEpoch, ".zip");
            }
        }
    }

    /// <summary>Configures automatic EF candidates and explicit file sources.</summary>
    public sealed class SqliteBackupDiscoveryOptions
    {
        /// <summary>Gets or sets how targets are acquired. Automatic preserves existing EF discovery and queries registered providers.</summary>
        public SqliteBackupDiscoveryMode Mode { get; set; } = SqliteBackupDiscoveryMode.Automatic;

        internal readonly Dictionary<Type, string> ContextIds = new();
        internal readonly List<(string Id, string Path)> Files = new();

        /// <summary>Gets or sets whether Automatic mode discovers registered concrete DbContexts/factories. Manual ignores this setting.</summary>
        public bool DiscoverDbContexts { get; set; } = true;

        /// <summary>Replaces the generated ID of a discovered context without registering an EF service.</summary>
        public void SetDbContextId<TContext>(string id) where TContext : DbContext
        {
            ContextIds[typeof(TContext)] = TargetId.Normalize(id);
        }

        /// <summary>Registers a file target. Relative paths are resolved against AppContext.BaseDirectory.</summary>
        public void AddFile(string id, string path)
        {
            string normalizedId = TargetId.Normalize(id);
            ArgumentException.ThrowIfNullOrWhiteSpace(path);
            Files.Add((normalizedId, System.IO.Path.GetFullPath(path, AppContext.BaseDirectory)));
        }

        internal SqliteBackupDiscoveryOptions Copy()
        {
            SqliteBackupDiscoveryOptions result = new() { Mode = Mode, DiscoverDbContexts = DiscoverDbContexts };
            foreach (KeyValuePair<Type, string> mapping in ContextIds)
            {
                result.ContextIds.Add(mapping.Key, mapping.Value);
            }
            result.Files.AddRange(Files);
            return result;
        }
    }

    /// <summary>Controls raw backup filenames and destinations.</summary>
    public sealed class SqliteBackupOutputOptions
    {
        /// <summary>Gets or sets the directory; relative values are resolved next to each source database.</summary>
        public string Directory { get; set; } = "_sqlite_backups";
        /// <summary>Gets or sets a filename pattern supporting {name} and {utc:FORMAT}.</summary>
        public string FileNamePattern { get; set; } = "{name}_{utc:yyyyMMdd_HHmmss}.db";
    }

    /// <summary>Controls creation of one ZIP containing the successful backups of a run.</summary>
    public sealed class SqliteBackupZipOptions
    {
        /// <summary>Gets or sets whether successful backups are archived.</summary>
        public bool Enabled { get; set; } = true;
        /// <summary>Gets or sets the ZIP destination; null uses the first successful raw backup's directory.</summary>
        public string? PublishDirectory { get; set; }
        /// <summary>Gets or sets the archive filename pattern.</summary>
        public string FileNamePattern { get; set; } = "SqliteHostBackup_{utc:yyyyMMdd_HHmmss}.zip";
        /// <summary>Gets or sets whether exactly the archived raw files are deleted after ZIP publication.</summary>
        public bool DeleteRawBackupsAfterZip { get; set; } = true;
    }

    /// <summary>Controls host lifecycle integration, independently of backup triggering.</summary>
    public sealed class SqliteBackupShutdownOptions
    {
        /// <summary>Gets or sets whether discovered SQLite files receive a best-effort WAL checkpoint on shutdown.</summary>
        public bool CheckpointWal { get; set; }
    }

    internal static class TargetId
    {
        internal static string Normalize(string id)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(id);
            return id.Trim();
        }
    }
}
