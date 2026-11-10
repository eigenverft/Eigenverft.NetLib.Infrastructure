using System;
using System.Globalization;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.Extensions.Logging;

namespace Eigenverft.NetLib.SqliteHotBackup.Internal
{
    internal static class BackupFiles
    {
        internal static string FileName(string pattern, string name, DateTimeOffset utc, string extension)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(pattern);
            foreach (char character in Path.GetInvalidFileNameChars())
            {
                name = name.Replace(character, '_');
            }
            string result = pattern.Replace("{name}", name, StringComparison.OrdinalIgnoreCase);
            int index;
            while ((index = result.IndexOf("{utc:", StringComparison.OrdinalIgnoreCase)) >= 0)
            {
                int end = result.IndexOf('}', index);
                if (end < 0)
                {
                    throw new ArgumentException("An utc filename token is not closed.", nameof(pattern));
                }
                string format = result.Substring(index + 5, end - index - 5);
                result = result[..index] + utc.ToString(format, CultureInfo.InvariantCulture) + result[(end + 1)..];
            }
            if (string.IsNullOrWhiteSpace(result) || result.Contains('{') || result.Contains('}')
                || result.Contains('/') || result.Contains('\\') || result is "." or ".."
                || result.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
            {
                throw new ArgumentException("The pattern must produce a single valid filename using supported tokens.", nameof(pattern));
            }
            return result.EndsWith(extension, StringComparison.OrdinalIgnoreCase) ? result : result + extension;
        }

        internal static string DirectoryNear(string basis, string configured)
        {
            return Path.GetFullPath(configured, basis);
        }

        internal static string TemporaryPath(string directory) => Path.Combine(directory, ".sqlite-backup-" + Guid.NewGuid().ToString("N") + ".tmp");

        internal static async Task<string> PublishAsync(string temporary, string desired, CancellationToken ct)
        {
            int suffix = 1;
            int retries = 0;
            while (true)
            {
                ct.ThrowIfCancellationRequested();
                string candidate = suffix == 1 ? desired : Path.Combine(Path.GetDirectoryName(desired)!,
                    Path.GetFileNameWithoutExtension(desired) + "_" + suffix.ToString(CultureInfo.InvariantCulture) + Path.GetExtension(desired));
                try
                {
                    // Never overwrite: another publisher can win after the filename check.
                    File.Move(temporary, candidate, overwrite: false);
                    return candidate;
                }
                catch (IOException) when (File.Exists(candidate))
                {
                    suffix = checked(suffix + 1);
                }
                catch (IOException) when (retries++ < 9)
                {
                    await Task.Delay(80, ct).ConfigureAwait(false);
                }
            }
        }

        internal static void DeleteTemporary(string? path, ILogger logger)
        {
            if (path is null)
            {
                return;
            }
            foreach (string suffix in new[] { "", "-wal", "-shm", "-journal" })
            {
                try
                {
                    File.Delete(path + suffix);
                }
                catch (IOException exception)
                {
                    BackupLog.TemporaryCleanup(logger, path + suffix, exception);
                }
                catch (UnauthorizedAccessException exception)
                {
                    BackupLog.TemporaryCleanup(logger, path + suffix, exception);
                }
            }
        }
    }
}
