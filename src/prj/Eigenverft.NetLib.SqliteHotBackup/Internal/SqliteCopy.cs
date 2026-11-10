using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;

using SQLitePCL;

namespace Eigenverft.NetLib.SqliteHotBackup.Internal
{
    internal static class SqliteCopy
    {
        private const int PagesPerStep = 32;

        internal static async Task<SqliteBackupResult> ExecuteAsync(ResolvedSource source, IReadOnlyList<string> ids,
            TimeSpan preparationTime, SqliteHotBackupOptions options, TimeProvider time, ILogger logger, CancellationToken ct)
        {
            long started = time.GetTimestamp();
            ProgressStallState progress = new(source.Limit, time, preparationTime);
            string? temporary = null;
            try
            {
                ThrowIfLimited(progress);
                string directory = BackupFiles.DirectoryNear(Path.GetDirectoryName(source.Path)!, options.Backup.Directory);
                Directory.CreateDirectory(directory);
                string name = BackupFiles.FileName(options.Backup.FileNamePattern, Path.GetFileNameWithoutExtension(source.Path), time.GetUtcNow(), ".db");
                string desired = Path.Combine(directory, name);
                temporary = BackupFiles.TemporaryPath(directory);
                using (FileStream reservation = new(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                {
                    // Reserve only our own random staging file before SQLite opens it.
                }
                SqliteConnectionStringBuilder destinationSettings = new()
                {
                    DataSource = temporary,
                    Mode = SqliteOpenMode.ReadWrite,
                    Pooling = false,
                    DefaultTimeout = Math.Max(1, (int)Math.Ceiling(source.Limit.NoProgressTimeout.TotalSeconds))
                };
                await using (SqliteConnection input = new(source.ConnectionString))
                await using (SqliteConnection output = new(destinationSettings.ToString()))
                {
                    await input.OpenAsync(ct).ConfigureAwait(false);
                    ThrowIfLimited(progress);
                    await output.OpenAsync(ct).ConfigureAwait(false);
                    ThrowIfLimited(progress);
                    sqlite3 inputHandle = (sqlite3)input.Handle!;
                    sqlite3 outputHandle = (sqlite3)output.Handle!;
                    raw.sqlite3_busy_timeout(inputHandle, 100);
                    raw.sqlite3_busy_timeout(outputHandle, 100);
                    sqlite3_backup? backup = raw.sqlite3_backup_init(outputHandle, "main", inputHandle, "main");
                    if (backup is null)
                    {
                        throw new IOException("SQLite backup initialization failed: " + raw.sqlite3_errmsg(outputHandle).utf8_to_string());
                    }
                    int finishCode;
                    try
                    {
                        while (true)
                        {
                            ct.ThrowIfCancellationRequested();
                            ThrowIfLimited(progress);
                            int result = raw.sqlite3_backup_step(backup, PagesPerStep);
                            if (result == raw.SQLITE_OK || result == raw.SQLITE_DONE)
                            {
                                progress.Observe(raw.sqlite3_backup_pagecount(backup), raw.sqlite3_backup_remaining(backup));
                                if (result == raw.SQLITE_DONE)
                                {
                                    break;
                                }
                                await Task.Delay(25, ct).ConfigureAwait(false);
                            }
                            else if (result == raw.SQLITE_BUSY || result == raw.SQLITE_LOCKED)
                            {
                                await Task.Delay(100, ct).ConfigureAwait(false);
                            }
                            else
                            {
                                throw new IOException($"SQLite backup step failed ({result}): {raw.sqlite3_errmsg(outputHandle).utf8_to_string()}");
                            }
                        }
                    }
                    finally
                    {
                        finishCode = raw.sqlite3_backup_finish(backup);
                    }
                    if (finishCode != raw.SQLITE_OK)
                    {
                        throw new IOException($"SQLite backup finish failed ({finishCode}).");
                    }
                }
                // Publication is after disposal, in the final directory, without overwriting any file.
                string published = await BackupFiles.PublishAsync(temporary, desired, ct).ConfigureAwait(false);
                BackupLog.CopyCompleted(logger, source.Path, published);
                return new(ids, SqliteBackupStatus.Succeeded, source.Path, published, null,
                    preparationTime + time.GetElapsedTime(started), progress.TotalPages, progress.RemainingPages, progress.RewoundPages, true);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                BackupLog.CopyFailed(logger, source.Path, exception);
                return new(ids, SqliteBackupStatus.Failed, source.Path, null, exception.GetBaseException().Message,
                    preparationTime + time.GetElapsedTime(started), progress.TotalPages, progress.RemainingPages, progress.RewoundPages);
            }
            finally
            {
                BackupFiles.DeleteTemporary(temporary, logger);
            }
        }

        private static void ThrowIfLimited(ProgressStallState progress)
        {
            if (progress.ExceededReason() is string reason)
            {
                throw new TimeoutException(reason);
            }
        }
    }
}
