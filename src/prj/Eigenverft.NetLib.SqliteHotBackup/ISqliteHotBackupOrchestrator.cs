using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Eigenverft.NetLib.SqliteHotBackup
{
    /// <summary>Discovers configured targets and performs complete or selected best-effort backup runs.</summary>
    public interface ISqliteHotBackupOrchestrator
    {
        /// <summary>Lists effective IDs without instantiating DbContexts or opening databases.</summary>
        Task<IReadOnlyList<SqliteBackupTarget>> GetTargetsAsync(CancellationToken cancellationToken = default);
        /// <summary>Backs up all targets. Operational failures are reported after the other targets and optional archive.</summary>
        Task<SqliteBackupRunResult> BackupAllAsync(CancellationToken cancellationToken = default);
        /// <summary>Backs up all targets with a private per-run copy of the configured options.</summary>
        Task<SqliteBackupRunResult> BackupAllAsync(Action<SqliteHotBackupOptions> overrideOptions, CancellationToken cancellationToken = default);
        /// <summary>Backs up one configured target ID.</summary>
        Task<SqliteBackupRunResult> BackupAsync(string targetId, CancellationToken cancellationToken = default);
        /// <summary>Backs up a nonempty selection of IDs, deduplicating sources within the run.</summary>
        Task<SqliteBackupRunResult> BackupAsync(IEnumerable<string> targetIds, CancellationToken cancellationToken = default);
        /// <summary>Backs up selected IDs with private per-run options.</summary>
        Task<SqliteBackupRunResult> BackupAsync(IEnumerable<string> targetIds, Action<SqliteHotBackupOptions> overrideOptions, CancellationToken cancellationToken = default);
    }
}
