using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.Data.Sqlite;

using Serilog.Debugging;

namespace Eigenverft.NetLib.SerilogRelay
{
    public partial class SerilogRelaySink
    {
        /// <summary>
        /// Deletes sent entries older than the configured retention and optionally expires unsent entries.
        /// </summary>
        private void CleanupOldLogsCore(TimeSpan sentRetention, TimeSpan? unsentRetention)
        {
            using var conn = new SqliteConnection(_connectionString);
            conn.Open();
            ConfigurePragmas(conn);

            using (var sentCommand = conn.CreateCommand())
            {
                sentCommand.CommandText = $@"
DELETE FROM {TableName}
 WHERE Sent = 1
   AND datetime(CreatedAt) <= datetime('now', $sentOffset);";
                sentCommand.Parameters.AddWithValue("$sentOffset", BuildSqliteOffset(sentRetention));
                sentCommand.ExecuteNonQuery();
            }

            if (!unsentRetention.HasValue)
                return;

            using var unsentCommand = conn.CreateCommand();
            unsentCommand.CommandText = $@"
DELETE FROM {TableName}
 WHERE Sent = 0
   AND datetime(CreatedAt) <= datetime('now', $unsentOffset);";
            unsentCommand.Parameters.AddWithValue("$unsentOffset", BuildSqliteOffset(unsentRetention.Value));
            unsentCommand.ExecuteNonQuery();
        }

        /// <summary>
        /// Converts a TimeSpan into one valid SQLite relative modifier.
        /// </summary>
        /// <param name="span">The timespan to convert.</param>
        /// <returns>A single relative-seconds modifier suitable for datetime('now', modifier).</returns>
        private static string BuildSqliteOffset(TimeSpan span)
            => span == TimeSpan.Zero
                ? "0 seconds"
                : FormattableString.Invariant($"{-span.TotalSeconds:R} seconds");

        private bool PersistLogEntryCore(LogEntry entry)
        {
            int reclaimAttempts = 0;
            bool emptySpoolFitChecked = false;

            while (true)
            {
                try
                {
                    PersistLogEntryOnceCore(entry);
                    return true;
                }
                catch (SqliteException ex) when (IsFullError(ex) && reclaimAttempts++ < 8)
                {
                    if (!emptySpoolFitChecked)
                    {
                        emptySpoolFitChecked = true;
                        if (!CanPersistInEmptySpoolCore(entry))
                            return false;
                    }

                    if (!TryReclaimSpoolSpaceCore())
                        return false;
                }
            }
        }

        private bool CanPersistInEmptySpoolCore(LogEntry entry)
        {
            using var conn = new SqliteConnection("Data Source=:memory:");
            conn.Open();
            ConfigurePragmas(conn);

            using (var createCommand = conn.CreateCommand())
            {
                createCommand.CommandText = TableSchema.Replace("{0}", TableName, StringComparison.Ordinal);
                createCommand.ExecuteNonQuery();
            }

            try
            {
                InsertLogEntryCore(conn, entry);
                return true;
            }
            catch (SqliteException ex) when (IsFullError(ex))
            {
                return false;
            }
        }

        private void PersistLogEntryOnceCore(LogEntry entry)
        {
            using var conn = new SqliteConnection(_connectionString);
            conn.Open();
            ConfigurePragmas(conn);
            InsertLogEntryCore(conn, entry);
        }

        private static void InsertLogEntryCore(SqliteConnection conn, LogEntry entry)
        {
            using var tx = conn.BeginTransaction();
            using var cmd = conn.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = $@"
INSERT INTO {TableName}
  (EventId, ApplicationId, MachineId, ProcessId, Timestamp, Level, RenderMessage, MessageTemplate, TraceId, SpanId, Exception, Properties, Sent)
VALUES
  ($eventId, $applicationId, $machineId, $processId, $ts, $lvl, $rendered, $tmpl, $tid, $sid, $ex, $props, 0);";

            cmd.Parameters.AddWithValue("$eventId", entry.EventId);
            cmd.Parameters.AddWithValue("$applicationId", entry.ApplicationId);
            cmd.Parameters.AddWithValue("$machineId", (object?)entry.MachineId ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$processId", entry.ProcessId);
            cmd.Parameters.AddWithValue("$ts", entry.Timestamp);
            cmd.Parameters.AddWithValue("$lvl", entry.Level);
            cmd.Parameters.AddWithValue("$rendered", entry.RenderMessage);
            cmd.Parameters.AddWithValue("$tmpl", entry.MessageTemplate);
            cmd.Parameters.AddWithValue("$tid", (object?)entry.TraceId ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$sid", (object?)entry.SpanId ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$ex", (object?)entry.Exception ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$props", (object?)entry.Properties ?? DBNull.Value);

            cmd.ExecuteNonQuery();
            tx.Commit();
        }

        private bool TryReclaimSpoolSpaceCore()
        {
            using var conn = new SqliteConnection(_connectionString);
            conn.Open();
            ConfigurePragmas(conn);
            using var tx = conn.BeginTransaction();

            int deletedSent = DeleteOldestSpoolRowsCore(conn, tx, sent: true, limit: 256);
            int deletedUnsent = deletedSent == 0
                ? DeleteOldestSpoolRowsCore(conn, tx, sent: false, limit: 64)
                : 0;
            tx.Commit();

            if (deletedUnsent > 0)
            {
                Interlocked.Add(ref _spoolDroppedCount, deletedUnsent);
                Volatile.Write(ref _pendingCountNeedsRefresh, 1);
                if (Interlocked.Exchange(ref _spoolOverflowReported, 1) == 0)
                {
                    SelfLog.WriteLine(
                        "SerilogRelay spool reached its {0}-byte budget; {1} oldest unsent events were evicted to keep disk usage bounded.",
                        _maxSpoolBytes,
                        deletedUnsent);
                }
            }

            return deletedSent + deletedUnsent > 0;
        }

        private static int DeleteOldestSpoolRowsCore(
            SqliteConnection connection,
            SqliteTransaction transaction,
            bool sent,
            int limit)
        {
            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = $@"
DELETE FROM {TableName}
 WHERE Id IN (
     SELECT Id
       FROM {TableName}
      WHERE Sent = $sent
      ORDER BY Id
      LIMIT $limit
 );";
            command.Parameters.AddWithValue("$sent", sent ? 1 : 0);
            command.Parameters.AddWithValue("$limit", limit);
            return command.ExecuteNonQuery();
        }

        private bool EventExistsCore(string eventId)
        {
            using var conn = new SqliteConnection(_connectionString);
            conn.Open();
            ConfigurePragmas(conn);
            using var cmd = conn.CreateCommand();
            cmd.CommandText = $"SELECT 1 FROM {TableName} WHERE EventId = $eventId LIMIT 1;";
            cmd.Parameters.AddWithValue("$eventId", eventId);
            return cmd.ExecuteScalar() is not null;
        }


        private void RecordSpoolRejected()
        {
            long dropped = Interlocked.Increment(ref _spoolDroppedCount);
            if (Interlocked.Exchange(ref _spoolOverflowReported, 1) != 0)
                return;

            SelfLog.WriteLine(
                "SerilogRelay spool reached its {0}-byte budget; incoming events are being dropped because no retained rows can be reclaimed. Total spool-capacity loss: {1}.",
                _maxSpoolBytes,
                dropped);
        }

        private void MarkSpoolUnavailable(Exception exception)
        {
            if (Interlocked.CompareExchange(ref _spoolUnavailable, 1, 0) != 0)
                return;

            SelfLog.WriteLine(
                "SerilogRelay local spool is unavailable; using the bounded volatile emergency buffer. Error: {0}",
                exception.Message);
        }

        private void MarkSpoolRecovered()
        {
            if (Interlocked.Exchange(ref _spoolUnavailable, 0) == 0)
                return;

            Interlocked.Exchange(ref _emergencyOverflowReported, 0);
            SelfLog.WriteLine("SerilogRelay local spool recovered; durable persistence resumed.");
        }

        private async Task<List<LogEntry>> LoadUnsentAsync(int limit, CancellationToken token)
        {
            return await ExecuteDatabaseWithRecoveryAsync(
                async () =>
                {
                    var list = new List<LogEntry>();
                    using var conn = new SqliteConnection(_connectionString);
                    await conn.OpenAsync(token).ConfigureAwait(false);
                    ConfigurePragmas(conn);
                    using var cmd = conn.CreateCommand();
                    cmd.CommandText = $@"
SELECT Id, EventId, ApplicationId, MachineId, ProcessId, Timestamp, Level, RenderMessage, MessageTemplate, TraceId, SpanId, Exception, Properties
FROM {TableName}
WHERE Sent = 0 ORDER BY Id ASC LIMIT {limit}";
                    using var reader = await cmd.ExecuteReaderAsync(token).ConfigureAwait(false);
                    while (await reader.ReadAsync(token).ConfigureAwait(false))
                    {
                        list.Add(new LogEntry
                        {
                            Id = reader.GetInt64(0),
                            EventId = reader.GetString(1),
                            ApplicationId = reader.GetString(2),
                            MachineId = reader.IsDBNull(3) ? null : reader.GetString(3),
                            ProcessId = reader.GetInt32(4),
                            Timestamp = reader.GetString(5),
                            Level = reader.GetString(6),
                            RenderMessage = reader.GetString(7),
                            MessageTemplate = reader.GetString(8),
                            TraceId = reader.IsDBNull(9) ? null : reader.GetString(9),
                            SpanId = reader.IsDBNull(10) ? null : reader.GetString(10),
                            Exception = reader.IsDBNull(11) ? null : reader.GetString(11),
                            Properties = reader.IsDBNull(12) ? null : reader.GetString(12),
                        });
                    }

                    return list;
                },
                token).ConfigureAwait(false);
        }

        private async Task MarkAsSentAsync(List<LogEntry> entries, CancellationToken token)
        {
            await ExecuteDatabaseWithRecoveryAsync(
                async () =>
                {
                    using var conn = new SqliteConnection(_connectionString);
                    await conn.OpenAsync(token).ConfigureAwait(false);
                    ConfigurePragmas(conn);
                    using var tx = conn.BeginTransaction();
                    using var cmd = conn.CreateCommand();
                    cmd.Transaction = tx;
                    cmd.CommandText = $"UPDATE {TableName} SET Sent = 1 WHERE Id IN ({string.Join(",", entries.ConvertAll(e => e.Id))})";
                    await cmd.ExecuteNonQueryAsync(token).ConfigureAwait(false);
                    tx.Commit();
                },
                token).ConfigureAwait(false);
        }

        // Retrieve the total number of unsent logs
        private long GetPendingCountCore()
        {
            using var conn = new SqliteConnection(_connectionString);
            conn.Open();
            ConfigurePragmas(conn);
            using var cmd = conn.CreateCommand();
            cmd.CommandText = $"SELECT COUNT(*) FROM {TableName} WHERE Sent = 0";
            return Convert.ToInt64(cmd.ExecuteScalar(), CultureInfo.InvariantCulture);
        }

        // Ensure the logs table exists in SQLite
        private void EnsureTableCreatedCore()
        {
            using var conn = new SqliteConnection(_connectionString);
            conn.Open();
            ConfigurePragmas(conn);
            using var cmd = conn.CreateCommand();
            cmd.CommandText = TableSchema.Replace("{0}", TableName, StringComparison.Ordinal);
            cmd.ExecuteNonQuery();
        }

        // Apply durable settings and the configured page budget to each SQLite connection.
        private void ConfigurePragmas(SqliteConnection conn)
        {
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = @"
PRAGMA journal_mode = WAL;
PRAGMA synchronous = FULL;
PRAGMA busy_timeout = 5000;
PRAGMA wal_autocheckpoint = 16;";
                cmd.ExecuteNonQuery();
            }

            long pageSize;
            using (var pageSizeCommand = conn.CreateCommand())
            {
                pageSizeCommand.CommandText = "PRAGMA page_size;";
                pageSize = Convert.ToInt64(
                    pageSizeCommand.ExecuteScalar(),
                    CultureInfo.InvariantCulture);
            }

            long maxPages = Math.Max(1L, _maxSpoolBytes / pageSize);
            long journalSizeLimit = Math.Max(
                pageSize * 8L,
                Math.Min(_maxSpoolBytes / 32L, 8L * 1024L * 1024L));

            using var budgetCommand = conn.CreateCommand();
            budgetCommand.CommandText = FormattableString.Invariant($@"
PRAGMA max_page_count = {maxPages};
PRAGMA journal_size_limit = {journalSizeLimit};");
            budgetCommand.ExecuteNonQuery();
        }

        private T ExecuteDatabaseWithRecovery<T>(Func<T> operation)
        {
            _databaseGate.Wait();
            try
            {
                try
                {
                    return operation();
                }
                catch (SqliteException ex) when (IsCorruptionError(ex))
                {
                    if (!TryRecoverCorruptedSpool(ex))
                        throw;

                    return operation();
                }
            }
            finally
            {
                _databaseGate.Release();
            }
        }

        private void ExecuteDatabaseWithRecovery(Action operation)
            => ExecuteDatabaseWithRecovery(
                () =>
                {
                    operation();
                    return true;
                });

        private async Task<T> ExecuteDatabaseWithRecoveryAsync<T>(
            Func<Task<T>> operation,
            CancellationToken token)
        {
            await _databaseGate.WaitAsync(token).ConfigureAwait(false);
            T result;
            try
            {
                try
                {
                    result = await operation().ConfigureAwait(false);
                }
                catch (SqliteException ex) when (IsCorruptionError(ex))
                {
                    if (!TryRecoverCorruptedSpool(ex))
                        throw;

                    result = await operation().ConfigureAwait(false);
                }
            }
            finally
            {
                _databaseGate.Release();
            }

            return result;
        }

        private async Task ExecuteDatabaseWithRecoveryAsync(
            Func<Task> operation,
            CancellationToken token)
        {
            await ExecuteDatabaseWithRecoveryAsync(
                async () =>
                {
                    await operation().ConfigureAwait(false);
                    return true;
                },
                token).ConfigureAwait(false);
        }

        private bool TryRecoverCorruptedSpool(SqliteException exception)
        {
            if (_databasePath is null || !File.Exists(_databasePath))
            {
                SelfLog.WriteLine(
                    "SQLite corruption was detected but the relay spool is not a recoverable file-backed database. ErrorCode={0}, ExtendedErrorCode={1}.",
                    exception.SqliteErrorCode,
                    exception.SqliteExtendedErrorCode);
                return false;
            }

            string quarantineId =
                DateTimeOffset.UtcNow.ToString("yyyyMMddTHHmmss.fffffff'Z'", CultureInfo.InvariantCulture)
                + "-"
                + Guid.NewGuid().ToString("N")[..8];

            string databaseDirectory = Path.GetDirectoryName(_databasePath)!;
            string quarantineDirectory = Path.Combine(
                databaseDirectory,
                CorruptionDirectoryName,
                quarantineId);

            try
            {
                SqliteConnection.ClearAllPools();
                Directory.CreateDirectory(quarantineDirectory);

                MoveToQuarantineIfPresent(_databasePath + "-wal", quarantineDirectory);
                MoveToQuarantineIfPresent(_databasePath + "-shm", quarantineDirectory);
                MoveToQuarantineIfPresent(_databasePath, quarantineDirectory);

                WriteCorruptionMetadata(quarantineDirectory, quarantineId, exception);

                EnsureTableCreatedCore();
                InsertCorruptionEventCore(quarantineId, exception);

                Interlocked.Exchange(ref _pendingCount, GetPendingCountCore());
                lock (_signalLock)
                {
                    _hasNewLogs = true;
                }

                SelfLog.WriteLine(
                    "SerilogRelay quarantined a corrupted SQLite spool and created a new spool. QuarantineId={0}, ErrorCode={1}, ExtendedErrorCode={2}.",
                    quarantineId,
                    exception.SqliteErrorCode,
                    exception.SqliteExtendedErrorCode);
                return true;
            }
            catch (Exception recoveryException)
            {
                SelfLog.WriteLine(
                    "SerilogRelay failed to quarantine/recreate a corrupted SQLite spool. OriginalErrorCode={0}, OriginalExtendedErrorCode={1}, RecoveryError={2}",
                    exception.SqliteErrorCode,
                    exception.SqliteExtendedErrorCode,
                    recoveryException.Message);
                return false;
            }
        }

        private void InsertCorruptionEventCore(string quarantineId, SqliteException exception)
        {
            var properties = new Dictionary<string, string>
            {
                ["RelayEventType"] = CorruptionEventType,
                ["QuarantineId"] = quarantineId,
                ["SpoolFileName"] = Path.GetFileName(_databasePath!),
                ["SqliteErrorCode"] = exception.SqliteErrorCode.ToString(CultureInfo.InvariantCulture),
                ["SqliteExtendedErrorCode"] = exception.SqliteExtendedErrorCode.ToString(CultureInfo.InvariantCulture),
                ["RecoveryAction"] = "quarantined_and_recreated",
            };

            string propertiesJson = JsonSerializer.Serialize(
                properties,
                typeof(Dictionary<string, string>),
                LogBatchJsonContext.Default);

            const string message =
                "SerilogRelay quarantined a corrupted SQLite spool and created a new spool.";

            using var conn = new SqliteConnection(_connectionString);
            conn.Open();
            ConfigurePragmas(conn);
            using var tx = conn.BeginTransaction();
            using var cmd = conn.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = $@"
INSERT INTO {TableName}
  (EventId, ApplicationId, MachineId, ProcessId, Timestamp, Level, RenderMessage, MessageTemplate, TraceId, SpanId, Exception, Properties, Sent)
VALUES
  ($eventId, $applicationId, $machineId, $processId, $ts, $lvl, $rendered, $tmpl, NULL, NULL, $ex, $props, 0);";

            cmd.Parameters.AddWithValue("$eventId", Guid.NewGuid().ToString("D"));
            cmd.Parameters.AddWithValue("$applicationId", _applicationId);
            cmd.Parameters.AddWithValue("$machineId", (object?)_machineId ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$processId", _processId);
            cmd.Parameters.AddWithValue(
                "$ts",
                DateTimeOffset.UtcNow.UtcDateTime.ToString("o", CultureInfo.InvariantCulture));
            cmd.Parameters.AddWithValue("$lvl", "Error");
            cmd.Parameters.AddWithValue("$rendered", message);
            cmd.Parameters.AddWithValue("$tmpl", message);
            cmd.Parameters.AddWithValue(
                "$ex",
                $"{exception.GetType().FullName}: {exception.Message}");
            cmd.Parameters.AddWithValue("$props", propertiesJson);

            cmd.ExecuteNonQuery();
            tx.Commit();
        }

        private void WriteCorruptionMetadata(
            string quarantineDirectory,
            string quarantineId,
            SqliteException exception)
        {
            try
            {
                var metadata = new Dictionary<string, string>
                {
                    ["DetectedUtc"] = DateTimeOffset.UtcNow.ToString("o", CultureInfo.InvariantCulture),
                    ["QuarantineId"] = quarantineId,
                    ["SpoolFileName"] = Path.GetFileName(_databasePath!),
                    ["SqliteErrorCode"] = exception.SqliteErrorCode.ToString(CultureInfo.InvariantCulture),
                    ["SqliteExtendedErrorCode"] = exception.SqliteExtendedErrorCode.ToString(CultureInfo.InvariantCulture),
                    ["SqliteMessage"] = exception.Message,
                    ["RecoveryAction"] = "quarantined_and_recreated",
                };

                string json = JsonSerializer.Serialize(
                    metadata,
                    typeof(Dictionary<string, string>),
                    LogBatchJsonContext.Default);

                File.WriteAllText(
                    Path.Combine(quarantineDirectory, "corruption.json"),
                    json);
            }
            catch (Exception metadataException)
            {
                SelfLog.WriteLine(
                    "SerilogRelay could not write corruption metadata: {0}",
                    metadataException.Message);
            }
        }

        private static void MoveToQuarantineIfPresent(
            string sourcePath,
            string quarantineDirectory)
        {
            if (!File.Exists(sourcePath))
                return;

            string targetPath = Path.Combine(
                quarantineDirectory,
                Path.GetFileName(sourcePath));

            File.Move(sourcePath, targetPath);
        }

        private static string? ResolveDatabasePath(string connectionString)
        {
            var builder = new SqliteConnectionStringBuilder(connectionString);
            if (builder.Mode == SqliteOpenMode.Memory
                || string.Equals(builder.DataSource, ":memory:", StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            return string.IsNullOrWhiteSpace(builder.DataSource)
                ? null
                : Path.GetFullPath(builder.DataSource);
        }

        private static bool IsCorruptionError(SqliteException ex)
            => ex.SqliteErrorCode == SQLitePCL.raw.SQLITE_CORRUPT
               || ex.SqliteErrorCode == SQLitePCL.raw.SQLITE_NOTADB;

        private static bool IsFullError(SqliteException ex)
            => ex.SqliteErrorCode == SQLitePCL.raw.SQLITE_FULL;

        // Detect SQLite busy or locked errors
        private static bool IsBusyError(SqliteException ex)
            => ex.SqliteErrorCode == SQLitePCL.raw.SQLITE_BUSY
               || ex.SqliteErrorCode == SQLitePCL.raw.SQLITE_LOCKED;
    }
}
