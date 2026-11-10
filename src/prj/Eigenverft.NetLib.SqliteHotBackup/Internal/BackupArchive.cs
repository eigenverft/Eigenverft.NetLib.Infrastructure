using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.Extensions.Logging;

namespace Eigenverft.NetLib.SqliteHotBackup.Internal
{
    internal static class BackupArchive
    {
        internal static async Task<SqliteBackupArchiveResult?> CreateAsync(List<SqliteBackupResult> results,
            SqliteBackupZipOptions settings, TimeProvider time, ILogger logger, CancellationToken ct)
        {
            List<SqliteBackupResult> successful = results.Where(static result => result.Status == SqliteBackupStatus.Succeeded).ToList();
            if (!settings.Enabled || successful.Count == 0)
            {
                return null;
            }
            string? temporary = null;
            try
            {
                string firstDirectory = Path.GetDirectoryName(successful[0].BackupFile!)!;
                string directory = string.IsNullOrWhiteSpace(settings.PublishDirectory) ? firstDirectory
                    : BackupFiles.DirectoryNear(firstDirectory, settings.PublishDirectory);
                Directory.CreateDirectory(directory);
                string desired = Path.Combine(directory, BackupFiles.FileName(settings.FileNamePattern, "archive", time.GetUtcNow(), ".zip"));
                temporary = BackupFiles.TemporaryPath(directory);
                HashSet<string> entryNames = new(StringComparer.OrdinalIgnoreCase);
                await using (FileStream output = new(temporary, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None,
                    65536, FileOptions.Asynchronous | FileOptions.SequentialScan))
                {
                    using ZipArchive zip = new(output, ZipArchiveMode.Create, leaveOpen: true);
                    foreach (SqliteBackupResult result in successful)
                    {
                        ct.ThrowIfCancellationRequested();
                        string file = result.BackupFile!;
                        string entryName = Path.GetFileName(file);
                        int suffix = 1;
                        while (!entryNames.Add(entryName))
                        {
                            entryName = Path.GetFileNameWithoutExtension(file) + "_" + (++suffix).ToString(System.Globalization.CultureInfo.InvariantCulture) + Path.GetExtension(file);
                        }
                        ZipArchiveEntry entry = zip.CreateEntry(entryName, CompressionLevel.Optimal);
                        await using FileStream input = new(file, FileMode.Open, FileAccess.Read, FileShare.Read,
                            65536, FileOptions.Asynchronous | FileOptions.SequentialScan);
                        await using Stream destination = entry.Open();
                        await input.CopyToAsync(destination, ct).ConfigureAwait(false);
                    }
                }
                string published = await BackupFiles.PublishAsync(temporary, desired, ct).ConfigureAwait(false);
                if (settings.DeleteRawBackupsAfterZip)
                {
                    for (int index = 0; index < results.Count; index++)
                    {
                        SqliteBackupResult result = results[index];
                        if (result.Status != SqliteBackupStatus.Succeeded)
                        {
                            continue;
                        }
                        try
                        {
                            File.Delete(result.BackupFile!);
                            results[index] = result with { RawBackupRetained = false };
                        }
                        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
                        {
                            BackupLog.RawCleanup(logger, result.BackupFile!, exception);
                        }
                    }
                }
                BackupLog.ArchiveCompleted(logger, published);
                return new(SqliteBackupStatus.Succeeded, published, null);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                BackupLog.ArchiveFailed(logger, exception);
                return new(SqliteBackupStatus.Failed, null, exception.GetBaseException().Message);
            }
            finally
            {
                BackupFiles.DeleteTemporary(temporary, logger);
            }
        }
    }
}
