using System;
using System.Collections.Generic;
using System.Linq;

namespace Eigenverft.NetLib.SqliteHotBackup
{
    /// <summary>The origin of a registered backup candidate.</summary>
    public enum SqliteBackupSourceKind
    {
        /// <summary>An automatically discovered EF registration.</summary>
        DbContext,
        /// <summary>A database file supplied by configuration, the runtime registry or an automatic provider.</summary>
        File
    }

    /// <summary>The outcome of one source or archive operation.</summary>
    public enum SqliteBackupStatus
    {
        /// <summary>The operation completed.</summary>
        Succeeded,
        /// <summary>An automatically discovered candidate was ineligible.</summary>
        Skipped,
        /// <summary>The requested operation could not complete.</summary>
        Failed
    }

    /// <summary>An inventory candidate. EF eligibility and its actual filename are resolved only when executing a run.</summary>
    public sealed record SqliteBackupTarget(string Id, SqliteBackupSourceKind Kind, string Name, string? SourceFile);

    /// <summary>An attempt result; aliases share one result and a raw file may subsequently have been archived and removed.</summary>
    public sealed record SqliteBackupResult(
        IReadOnlyList<string> TargetIds,
        SqliteBackupStatus Status,
        string? SourceFile,
        string? BackupFile,
        string? Reason,
        TimeSpan Duration,
        long TotalPages = 0,
        long RemainingPages = 0,
        long RewoundPages = 0,
        bool RawBackupRetained = false);

    /// <summary>The independent archive outcome. Successful raw backups remain available if archiving fails.</summary>
    public sealed record SqliteBackupArchiveResult(SqliteBackupStatus Status, string? File, string? Reason);

    /// <summary>An automatic provider or runtime-registry snapshot that could not be obtained.</summary>
    public sealed record SqliteBackupDiscoveryFailure(string Source, string Reason);

    /// <summary>Reports incomplete inventory while retaining targets obtained from the other sources.</summary>
    public sealed class SqliteBackupDiscoveryException : Exception
    {
        /// <summary>Creates an inventory failure with the successful partial catalog and source failures.</summary>
        public SqliteBackupDiscoveryException(IReadOnlyList<SqliteBackupTarget> targets, IReadOnlyList<SqliteBackupDiscoveryFailure> failures)
            : base("SQLite backup discovery is incomplete. Inspect Targets and Failures.")
        {
            ArgumentNullException.ThrowIfNull(targets);
            ArgumentNullException.ThrowIfNull(failures);
            Targets = targets;
            Failures = failures;
        }

        /// <summary>Gets the partial inventory, without claiming that failed sources are empty.</summary>
        public IReadOnlyList<SqliteBackupTarget> Targets { get; }
        /// <summary>Gets the sources that could not supply a snapshot.</summary>
        public IReadOnlyList<SqliteBackupDiscoveryFailure> Failures { get; }
    }

    /// <summary>The complete report, including successful sources when another source failed.</summary>
    public sealed record SqliteBackupRunResult(IReadOnlyList<SqliteBackupResult> Targets, SqliteBackupArchiveResult? Archive)
    {
        /// <summary>Gets failed source queries. Known targets are still attempted before the run exception is thrown.</summary>
        public IReadOnlyList<SqliteBackupDiscoveryFailure> DiscoveryFailures { get; init; } = Array.Empty<SqliteBackupDiscoveryFailure>();

        /// <summary>Gets whether a source or archive failed.</summary>
        public bool HasFailures => Targets.Any(static target => target.Status == SqliteBackupStatus.Failed)
            || Archive?.Status == SqliteBackupStatus.Failed || DiscoveryFailures.Count != 0;
    }

    /// <summary>Reports operational failures after attempting the remaining independent targets and optional ZIP.</summary>
    public sealed class SqliteBackupException : Exception
    {
        /// <summary>Creates an exception retaining the complete run report.</summary>
        public SqliteBackupException(SqliteBackupRunResult result)
            : base("One or more SQLite backup operations failed. Inspect Result for all source and archive outcomes.")
        {
            ArgumentNullException.ThrowIfNull(result);
            Result = result;
        }

        /// <summary>Gets completed, skipped, and failed outcomes from the run.</summary>
        public SqliteBackupRunResult Result { get; }
    }
}
