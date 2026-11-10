using System;

using Microsoft.Extensions.Logging;

namespace Eigenverft.NetLib.SqliteHotBackup.Internal
{
    internal static partial class BackupLog
    {
        [LoggerMessage(1, LogLevel.Warning, "Could not remove owned temporary backup file {Path}.")]
        internal static partial void TemporaryCleanup(ILogger logger, string path, Exception exception);
        [LoggerMessage(2, LogLevel.Information, "SQLite backup completed for {Source}: {Backup}.")]
        internal static partial void CopyCompleted(ILogger logger, string source, string backup);
        [LoggerMessage(3, LogLevel.Error, "SQLite backup failed for {Source}.")]
        internal static partial void CopyFailed(ILogger logger, string source, Exception exception);
        [LoggerMessage(4, LogLevel.Warning, "Archive published, but raw backup {File} could not be deleted.")]
        internal static partial void RawCleanup(ILogger logger, string file, Exception exception);
        [LoggerMessage(5, LogLevel.Information, "SQLite backup archive completed: {Archive}.")]
        internal static partial void ArchiveCompleted(ILogger logger, string archive);
        [LoggerMessage(6, LogLevel.Error, "SQLite backup archive failed; raw backups are retained.")]
        internal static partial void ArchiveFailed(ILogger logger, Exception exception);
        [LoggerMessage(7, LogLevel.Error, "SQLite source resolution failed for {TargetId}.")]
        internal static partial void ResolutionFailed(ILogger logger, string targetId, Exception exception);
        [LoggerMessage(8, LogLevel.Information, "SQLite backup run completed: {Targets} source results, failures={HasFailures}.")]
        internal static partial void RunCompleted(ILogger logger, int targets, bool hasFailures);
        [LoggerMessage(9, LogLevel.Information, "SQLite shutdown checkpoint {Source}: busy={Busy}, pages={Pages}, checkpointed={Checkpointed}.")]
        internal static partial void CheckpointCompleted(ILogger logger, string source, int busy, int pages, int checkpointed);
        [LoggerMessage(10, LogLevel.Warning, "SQLite shutdown checkpoint failed for {TargetId}.")]
        internal static partial void CheckpointFailed(ILogger logger, string targetId, Exception exception);
        [LoggerMessage(11, LogLevel.Debug, "SQLite shutdown checkpoint canceled by the host.")]
        internal static partial void ShutdownCanceled(ILogger logger);
        [LoggerMessage(12, LogLevel.Warning, "SQLite shutdown checkpoint could not complete.")]
        internal static partial void ShutdownFailed(ILogger logger, Exception exception);
    }
}
