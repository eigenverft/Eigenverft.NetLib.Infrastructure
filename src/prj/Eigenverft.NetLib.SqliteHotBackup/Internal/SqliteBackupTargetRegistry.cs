using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Eigenverft.NetLib.SqliteHotBackup.Internal
{
    internal sealed class SqliteBackupTargetRegistry : ISqliteBackupTargetRegistry
    {
        private readonly object _gate = new();
        private readonly Dictionary<string, SqliteBackupFileSource> _files = new(StringComparer.OrdinalIgnoreCase);

        public void AddFile(string id, string path)
        {
            SqliteBackupFileSource file = SqliteBackupFileSource.Normalize(id, path);
            lock (_gate)
            {
                if (!_files.TryAdd(file.Id, file))
                {
                    throw new ArgumentException($"Backup target ID '{file.Id}' is already registered.", nameof(id));
                }
            }
        }

        public bool Remove(string id)
        {
            string normalized = TargetId.Normalize(id);
            lock (_gate)
            {
                return _files.Remove(normalized);
            }
        }

        public Task<IReadOnlyList<SqliteBackupFileSource>> GetSourcesAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            lock (_gate)
            {
                IReadOnlyList<SqliteBackupFileSource> snapshot = Array.AsReadOnly(_files.Values.ToArray());
                return Task.FromResult(snapshot);
            }
        }
    }
}
