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
        /// <summary>An explicitly registered database file.</summary>
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

    /// <summary>The complete report, including successful sources when another source failed.</summary>
    public sealed record SqliteBackupRunResult(IReadOnlyList<SqliteBackupResult> Targets, SqliteBackupArchiveResult? Archive)
    {
        /// <summary>Gets whether a source or archive failed.</summary>
        public bool HasFailures => Targets.Any(static target => target.Status == SqliteBackupStatus.Failed)
            || Archive?.Status == SqliteBackupStatus.Failed;
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
