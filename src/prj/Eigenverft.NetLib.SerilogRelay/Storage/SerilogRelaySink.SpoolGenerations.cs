using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;

using Microsoft.Data.Sqlite;

using Serilog.Debugging;

namespace Eigenverft.NetLib.SerilogRelay
{
    public partial class SerilogRelaySink
    {
        private const string GenerationMarkerHeader = "SerilogRelay.Generation.v1";
        private readonly Func<Guid?> _bootIdentityProvider = SystemBootIdentity.Get;
        // Every generation shares this coordination file; normal SQLite operations do not use it.
        private FileStream? TryAcquireRecoveryLock(TimeSpan timeout)
        {
            if (_baseDatabasePath is null)
                return null;

            long started = Stopwatch.GetTimestamp();
            while (true)
            {
                try
                {
                    return new FileStream(_baseDatabasePath + ".recovery.lock",
                        FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
                }
                catch (IOException)
                {
                    if (Stopwatch.GetElapsedTime(started) >= timeout)
                        return null;
                    Thread.Sleep(50);
                }
            }
        }

        private string SelectStartupSpoolGeneration()
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_baseDatabasePath!)!);
            using FileStream? coordination = TryAcquireRecoveryLock(RecoveryLockTimeout);
            if (coordination is null)
                throw new IOException("Could not coordinate relay spool generation selection.");

            (int number, string path) = FindLatestSpoolGeneration();
            if (_bootIdentityProvider() is Guid bootId)
            {
                (int Number, Guid BootId)? marker = ReadGenerationMarker(coordination);
                if (marker.HasValue && marker.Value.Number == number)
                {
                    if (number > 0 && marker.Value.BootId != bootId)
                        CleanupObsoleteSpoolGenerations(number);
                }
                else
                {
                    // Missing/stale metadata proves nothing. Retain old files until a later boot.
                    WriteGenerationMarker(coordination, number, bootId);
                }
            }
            return path;
        }

        private static (int Number, Guid BootId)? ReadGenerationMarker(FileStream coordination)
        {
            try
            {
                if (coordination.Length > 128)
                    return null;
                var buffer = new byte[(int)coordination.Length];
                coordination.Position = 0;
                coordination.ReadExactly(buffer);
                string[] parts = Encoding.UTF8.GetString(buffer).Split('\n');
                if (parts.Length != 4 || parts[0] != GenerationMarkerHeader || parts[3].Length != 0
                    || !int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out int number)
                    || number < 0 || !Guid.TryParseExact(parts[2], "D", out Guid bootId) || bootId == Guid.Empty)
                    return null;
                return (number, bootId);
            }
            catch (IOException)
            {
                return null;
            }
        }

        private static void WriteGenerationMarker(FileStream coordination, int number, Guid bootId)
        {
            try
            {
                // Flush invalidation before publishing a replacement record. An interrupted
                // write cannot combine the new generation number with an older boot identity.
                coordination.SetLength(0);
                coordination.Position = 0;
                coordination.Flush(flushToDisk: true);
                byte[] marker = Encoding.UTF8.GetBytes(FormattableString.Invariant(
                    $"{GenerationMarkerHeader}\n{number}\n{bootId:D}\n"));
                coordination.Write(marker);
                coordination.Flush(flushToDisk: true);
            }
            catch (IOException ex)
            {
                SelfLog.WriteLine("SerilogRelay could not record the spool generation's boot identity; automatic cleanup may be deferred. Error: {0}", ex.Message);
            }
        }

        private (int Number, string Path) FindLatestSpoolGeneration()
        {
            int latestNumber = 0;
            string latestPath = _baseDatabasePath!;
            foreach (string path in Directory.EnumerateFiles(Path.GetDirectoryName(_baseDatabasePath!)!))
            {
                int number = GetSpoolGenerationNumber(path);
                if (number > latestNumber)
                {
                    latestNumber = number;
                    latestPath = path;
                }
            }
            return (latestNumber, latestPath);
        }

        private string GetSpoolGenerationPath(int number)
            => number == 0 ? _baseDatabasePath! : Path.Combine(
                Path.GetDirectoryName(_baseDatabasePath!)!,
                Path.GetFileNameWithoutExtension(_baseDatabasePath!)
                + ".g" + number.ToString("D4", CultureInfo.InvariantCulture)
                + Path.GetExtension(_baseDatabasePath!));

        // Accept only canonical, numeric generation filenames belonging to this configured spool.
        private int GetSpoolGenerationNumber(string path)
            => GetSpoolGenerationNumberCore(path, OperatingSystem.IsWindows());

        private int GetSpoolGenerationNumberCore(string path, bool isWindows)
        {
            string fileName = Path.GetFileName(path);
            StringComparison comparison = isWindows
                ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
            if (string.Equals(fileName, Path.GetFileName(_baseDatabasePath!), comparison))
                return 0;

            string prefix = Path.GetFileNameWithoutExtension(_baseDatabasePath!) + ".g";
            string suffix = Path.GetExtension(_baseDatabasePath!);
            if (!fileName.StartsWith(prefix, comparison)
                || !fileName.EndsWith(suffix, comparison)
                || fileName.Length < prefix.Length + suffix.Length + 4)
                return -1;

            ReadOnlySpan<char> digits = fileName.AsSpan(prefix.Length,
                fileName.Length - prefix.Length - suffix.Length);
            return int.TryParse(digits, NumberStyles.None, CultureInfo.InvariantCulture, out int number)
                && number > 0 && digits.SequenceEqual(number.ToString("D4", CultureInfo.InvariantCulture))
                    ? number : -1;
        }

        // Called under the recovery lock only after the latest generation's recorded boot
        // identity differs from this boot. Earlier processes cannot survive that OS restart.
        private void CleanupObsoleteSpoolGenerations(int latestNumber)
        {
            try
            {
                foreach (string path in Directory.EnumerateFiles(Path.GetDirectoryName(_baseDatabasePath!)!))
                {
                    string databasePath = path;
                    int number = GetSpoolGenerationNumber(path);
                    if (number < 0 && (path.EndsWith("-wal", StringComparison.Ordinal)
                        || path.EndsWith("-shm", StringComparison.Ordinal)))
                    {
                        databasePath = path[..^4];
                        number = GetSpoolGenerationNumber(databasePath);
                    }
                    if (number < 0 || number >= latestNumber)
                        continue;
                    try
                    {
                        // Leave the generation filename until both SQLite sidecars are gone.
                        File.Delete(databasePath + "-wal");
                        File.Delete(databasePath + "-shm");
                        File.Delete(databasePath);
                    }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                    {
                        SelfLog.WriteLine("SerilogRelay could not delete obsolete spool generation '{0}': {1}",
                            databasePath, ex.Message);
                    }
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                SelfLog.WriteLine("SerilogRelay skipped obsolete spool generation cleanup: {0}", ex.Message);
            }
        }

        private bool TryRecoverCorruptedSpool(SqliteException exception)
        {
            lock (_signalLock)
            {
                if (_spoolDisabledForLifetime)
                    return false;
                _spoolDisabledForLifetime = true;
                Interlocked.Exchange(ref _pendingCount, 0);
                _pendingSinceUtc = null;
                _hasNewLogs = false;
            }
            MarkSpoolUnavailable(exception);

            if (_baseDatabasePath is null || _databasePath is null)
            {
                SelfLog.WriteLine("SQLite corruption was detected but the relay spool is not a recoverable file-backed database.");
                return false;
            }

            try
            {
                using FileStream? coordination = TryAcquireRecoveryLock(RecoveryLockTimeout);
                if (coordination is null)
                {
                    SelfLog.WriteLine("SerilogRelay could not acquire cross-process corruption-recovery coordination for spool '{0}'.", _databasePath);
                    return false;
                }
                (int number, _) = FindLatestSpoolGeneration();
                // Another process may already have created the successor. Never replace any file.
                if (number > GetSpoolGenerationNumber(_databasePath))
                    return true;
                string successor = GetSpoolGenerationPath(checked(number + 1));
                using (var file = new FileStream(successor, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                    file.Flush(flushToDisk: true);
                if (_bootIdentityProvider() is Guid bootId)
                    WriteGenerationMarker(coordination, number + 1, bootId);
                SelfLog.WriteLine("SerilogRelay detected SQLite corruption and reserved spool generation '{0}' for new sink instances. This instance remains in emergency memory mode. ErrorCode={1}, ExtendedErrorCode={2}.",
                    successor, exception.SqliteErrorCode, exception.SqliteExtendedErrorCode);
                return true;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or OverflowException)
            {
                SelfLog.WriteLine("SerilogRelay could not create the next spool generation; this instance remains in emergency memory mode. Error: {0}", ex.Message);
                return false;
            }
        }
    }
}
