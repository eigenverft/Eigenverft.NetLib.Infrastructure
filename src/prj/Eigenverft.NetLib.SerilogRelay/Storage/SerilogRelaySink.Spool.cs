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
        private void CleanupApplicationSpoolRetentionCore(TimeSpan sentRetention, TimeSpan? unsentRetention)
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
   AND datetime(CreatedAt) <= datetime('now', $unsentOffset)
   AND (
        ClaimOwnerId IS NULL
        OR ClaimUntilUnixMs IS NULL
        OR ClaimUntilUnixMs <= $now
       );";
            unsentCommand.Parameters.AddWithValue("$unsentOffset", BuildSqliteOffset(unsentRetention.Value));
            unsentCommand.Parameters.AddWithValue(
                "$now",
                DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
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

        // A false result means the event is rejected by the spool capacity policy.
        // Storage exceptions propagate so the caller can use the emergency buffer.
        private bool TryPersistLogEntryCore(LogEntry entry)
        {
            try
            {
                PersistLogEntryOnceCore(entry);
                return true;
            }
            catch (SqliteException ex) when (IsFullError(ex))
            {
                if (!CanFitInEmptyApplicationSpoolCore(entry))
                    return false;

                return TryReclaimAndPersistApplicationSpoolCore(entry);
            }
        }

        private bool CanFitInEmptyApplicationSpoolCore(LogEntry entry)
        {
            // This disposable database only probes capacity; it stores no backlog.
            using var conn = new SqliteConnection("Data Source=:memory:");
            conn.Open();
            ConfigurePragmas(conn);

            EnsureTableSchemaCore(conn);

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
            using var transaction = conn.BeginTransaction();
            InsertLogEntryCore(conn, entry, transaction);
            transaction.Commit();
        }

        private static void InsertLogEntryCore(SqliteConnection conn, LogEntry entry, SqliteTransaction transaction)
        {
            using var cmd = conn.CreateCommand();
            cmd.Transaction = transaction;
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
        }

        private bool TryReclaimAndPersistApplicationSpoolCore(LogEntry entry)
        {
            using var conn = new SqliteConnection(_connectionString);
            conn.Open();
            ConfigurePragmas(conn);

            for (int unsentLimit = 0; unsentLimit <= 512; unsentLimit += 64)
            {
                using var transaction = conn.BeginTransaction();
                DeleteOldestApplicationSpoolRowsCore(conn, transaction, sent: true, limit: int.MaxValue);
                int deletedUnsent = DeleteOldestApplicationSpoolRowsCore(conn, transaction, sent: false, limit: unsentLimit);

                try
                {
                    InsertLogEntryCore(conn, entry, transaction);
                    transaction.Commit();
                }
                catch (SqliteException ex) when (IsFullError(ex))
                {
                    // The transaction restores reclaimed rows when the replacement still does not fit.
                    if (deletedUnsent < unsentLimit)
                        return false;
                    continue;
                }

                if (deletedUnsent > 0)
                {
                    Interlocked.Add(ref _applicationSpoolDroppedCount, deletedUnsent);
                    Volatile.Write(ref _pendingCountNeedsRefresh, 1);
                    if (Interlocked.Exchange(ref _applicationSpoolOverflowReported, 1) == 0)
                        SelfLog.WriteLine("SerilogRelay spool reached its {0}-byte budget; {1} oldest unsent events were evicted to keep disk usage bounded.", _maxApplicationSpoolPhysicalBytes, deletedUnsent);
                }

                return true;
            }

            return false;
        }

        private static int DeleteOldestApplicationSpoolRowsCore(
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
        AND (
             $sent = 1
             OR ClaimOwnerId IS NULL
             OR ClaimUntilUnixMs IS NULL
             OR ClaimUntilUnixMs <= $now
            )
      ORDER BY Id
      LIMIT $limit
 );";
            command.Parameters.AddWithValue("$sent", sent ? 1 : 0);
            command.Parameters.AddWithValue(
                "$now",
                DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
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

        private async Task<ClaimedLogBatch> ClaimPendingAsync(
            int limit,
            DateTimeOffset now,
            CancellationToken token)
        {
            string claimBatchId = Guid.NewGuid().ToString("N");
            long nowUnixMs = now.ToUnixTimeMilliseconds();
            long claimUntilUnixMs = now.Add(ClaimLeaseDuration).ToUnixTimeMilliseconds();

            return await ExecuteDatabaseWithRecoveryAsync(
                async () =>
                {
                    using var conn = new SqliteConnection(_connectionString);
                    await conn.OpenAsync(token).ConfigureAwait(false);
                    ConfigurePragmas(conn);

                    using (var claim = conn.CreateCommand())
                    {
                        claim.CommandText = $@"
UPDATE {TableName}
   SET ClaimOwnerId = $owner,
       ClaimBatchId = $claimBatchId,
       ClaimUntilUnixMs = $claimUntil
 WHERE Id IN (
       SELECT Id
         FROM {TableName}
        WHERE Sent = 0
          AND (
                ClaimOwnerId IS NULL
                OR ClaimOwnerId = $owner
                OR ClaimUntilUnixMs IS NULL
                OR ClaimUntilUnixMs <= $now
              )
        ORDER BY Id
        LIMIT $limit
 )
   AND Sent = 0
   AND (
         ClaimOwnerId IS NULL
         OR ClaimOwnerId = $owner
         OR ClaimUntilUnixMs IS NULL
         OR ClaimUntilUnixMs <= $now
       );";
                        claim.Parameters.AddWithValue("$owner", _claimOwnerId);
                        claim.Parameters.AddWithValue("$claimBatchId", claimBatchId);
                        claim.Parameters.AddWithValue("$claimUntil", claimUntilUnixMs);
                        claim.Parameters.AddWithValue("$now", nowUnixMs);
                        claim.Parameters.AddWithValue("$limit", limit);
                        await claim.ExecuteNonQueryAsync(token).ConfigureAwait(false);
                    }

                    var entries = new List<LogEntry>();
                    using var select = conn.CreateCommand();
                    select.CommandText = $@"
SELECT Id, EventId, ApplicationId, MachineId, ProcessId, Timestamp, Level, RenderMessage, MessageTemplate, TraceId, SpanId, Exception, Properties
  FROM {TableName}
 WHERE Sent = 0
   AND ClaimOwnerId = $owner
   AND ClaimBatchId = $claimBatchId
 ORDER BY Id ASC;";
                    select.Parameters.AddWithValue("$owner", _claimOwnerId);
                    select.Parameters.AddWithValue("$claimBatchId", claimBatchId);

                    long payloadBytes = JsonSerializer.SerializeToUtf8Bytes(CreateBatchPayload(entries), LogBatchJsonContext.Default.LogBatchPayload).Length;
                    bool payloadTargetReached = false;
                    using (var reader = await select.ExecuteReaderAsync(token).ConfigureAwait(false))
                    {
                        while (await reader.ReadAsync(token).ConfigureAwait(false))
                        {
                            var entry = new LogEntry
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
                            };
                            int entryBytes = JsonSerializer.SerializeToUtf8Bytes(entry, LogBatchJsonContext.Default.LogEntry).Length;
                            int countDigitsAdded = (entries.Count + 1).ToString(CultureInfo.InvariantCulture).Length
                                - entries.Count.ToString(CultureInfo.InvariantCulture).Length;
                            long nextPayloadBytes = payloadBytes + entryBytes + countDigitsAdded + (entries.Count == 0 ? 0 : 1);

                            if (entries.Count > 0 && nextPayloadBytes > _targetBatchPayloadBytes)
                            {
                                payloadTargetReached = true;
                                break;
                            }

                            // Always include the first event so an oversized event can leave the spool.
                            entries.Add(entry);
                            payloadBytes = nextPayloadBytes;
                            if (payloadBytes >= _targetBatchPayloadBytes)
                            {
                                payloadTargetReached = true;
                                break;
                            }
                        }
                    }

                    if (payloadTargetReached)
                    {
                        // Release the unread suffix before HTTP; acknowledgment covers only this payload.
                        using var release = conn.CreateCommand();
                        release.CommandText = $@"
UPDATE {TableName}
   SET ClaimOwnerId = NULL,
       ClaimBatchId = NULL,
       ClaimUntilUnixMs = NULL
 WHERE Sent = 0
   AND ClaimOwnerId = $owner
   AND ClaimBatchId = $claimBatchId
   AND Id > $lastIncludedId;";
                        release.Parameters.AddWithValue("$owner", _claimOwnerId);
                        release.Parameters.AddWithValue("$claimBatchId", claimBatchId);
                        release.Parameters.AddWithValue("$lastIncludedId", entries[entries.Count - 1].Id);
                        await release.ExecuteNonQueryAsync(token).ConfigureAwait(false);
                    }

                    return new ClaimedLogBatch(claimBatchId, entries, payloadTargetReached);
                },
                token).ConfigureAwait(false);
        }

        private async Task AcknowledgeClaimAsync(ClaimedLogBatch batch, CancellationToken token)
        {
            if (batch.Entries.Count == 0)
                return;

            await ExecuteDatabaseWithRecoveryAsync(
                async () =>
                {
                    using var conn = new SqliteConnection(_connectionString);
                    await conn.OpenAsync(token).ConfigureAwait(false);
                    ConfigurePragmas(conn);
                    using var cmd = conn.CreateCommand();
                    cmd.CommandText = _applicationSpoolSentEventRetention == TimeSpan.Zero
                        ? $@"
DELETE FROM {TableName}
 WHERE Sent = 0
   AND ClaimOwnerId = $owner
   AND ClaimBatchId = $claimBatchId;"
                        : $@"
UPDATE {TableName}
   SET Sent = 1,
       ClaimOwnerId = NULL,
       ClaimBatchId = NULL,
       ClaimUntilUnixMs = NULL
 WHERE Sent = 0
   AND ClaimOwnerId = $owner
   AND ClaimBatchId = $claimBatchId;";
                    cmd.Parameters.AddWithValue("$owner", _claimOwnerId);
                    cmd.Parameters.AddWithValue("$claimBatchId", batch.ClaimBatchId);
                    await cmd.ExecuteNonQueryAsync(token).ConfigureAwait(false);
                },
                token).ConfigureAwait(false);
        }

        private async Task ReleaseClaimAsync(ClaimedLogBatch batch, CancellationToken token)
        {
            await ReleaseClaimsCoreAsync(batch.ClaimBatchId, token).ConfigureAwait(false);
        }

        private async Task ReleaseAllOwnedClaimsAsync(CancellationToken token)
        {
            await ReleaseClaimsCoreAsync(claimBatchId: null, token).ConfigureAwait(false);
        }

        private async Task ReleaseClaimsCoreAsync(string? claimBatchId, CancellationToken token)
        {
            await ExecuteDatabaseWithRecoveryAsync(
                async () =>
                {
                    using var conn = new SqliteConnection(_connectionString);
                    await conn.OpenAsync(token).ConfigureAwait(false);
                    ConfigurePragmas(conn);
                    using var cmd = conn.CreateCommand();
                    cmd.CommandText = claimBatchId is null
                        ? $@"
UPDATE {TableName}
   SET ClaimOwnerId = NULL,
       ClaimBatchId = NULL,
       ClaimUntilUnixMs = NULL
 WHERE Sent = 0
   AND ClaimOwnerId = $owner;"
                        : $@"
UPDATE {TableName}
   SET ClaimOwnerId = NULL,
       ClaimBatchId = NULL,
       ClaimUntilUnixMs = NULL
 WHERE Sent = 0
   AND ClaimOwnerId = $owner
   AND ClaimBatchId = $claimBatchId;";
                    cmd.Parameters.AddWithValue("$owner", _claimOwnerId);
                    if (claimBatchId is not null)
                        cmd.Parameters.AddWithValue("$claimBatchId", claimBatchId);

                    await cmd.ExecuteNonQueryAsync(token).ConfigureAwait(false);
                },
                token).ConfigureAwait(false);
        }

        private long GetClaimablePendingCountCore(DateTimeOffset now)
        {
            using var conn = new SqliteConnection(_connectionString);
            conn.Open();
            ConfigurePragmas(conn);
            using var cmd = conn.CreateCommand();
            cmd.CommandText = $@"
SELECT COUNT(*)
  FROM {TableName}
 WHERE Sent = 0
   AND (
         ClaimOwnerId IS NULL
         OR ClaimOwnerId = $owner
         OR ClaimUntilUnixMs IS NULL
         OR ClaimUntilUnixMs <= $now
       );";
            cmd.Parameters.AddWithValue("$owner", _claimOwnerId);
            cmd.Parameters.AddWithValue("$now", now.ToUnixTimeMilliseconds());
            return Convert.ToInt64(cmd.ExecuteScalar(), CultureInfo.InvariantCulture);
        }

        // Retrieve the total number of unsent logs, regardless of active claims.
        private long GetPendingCountCore()
        {
            using var conn = new SqliteConnection(_connectionString);
            conn.Open();
            ConfigurePragmas(conn);
            using var cmd = conn.CreateCommand();
            cmd.CommandText = $"SELECT COUNT(*) FROM {TableName} WHERE Sent = 0";
            return Convert.ToInt64(cmd.ExecuteScalar(), CultureInfo.InvariantCulture);
        }

        // Ensure the logs table and multi-process claim columns/indexes exist.
        private void EnsureTableCreatedCore()
        {
            using var conn = new SqliteConnection(_connectionString);
            conn.Open();
            ConfigurePragmas(conn);
            EnsureTableSchemaCore(conn);
        }

        private static void EnsureTableSchemaCore(SqliteConnection conn)
        {
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = TableSchema.Replace("{0}", TableName, StringComparison.Ordinal);
                cmd.ExecuteNonQuery();
            }

            using var migration = conn.BeginTransaction(deferred: false);
            EnsureColumnExistsCore(conn, migration, "ClaimOwnerId", "TEXT");
            EnsureColumnExistsCore(conn, migration, "ClaimBatchId", "TEXT");
            EnsureColumnExistsCore(conn, migration, "ClaimUntilUnixMs", "INTEGER");

            using var index = conn.CreateCommand();
            index.Transaction = migration;
            index.CommandText = $@"
CREATE INDEX IF NOT EXISTS IX_{TableName}_Claimable
    ON {TableName}(Sent, ClaimUntilUnixMs, Id);
CREATE INDEX IF NOT EXISTS IX_{TableName}_ClaimOwner
    ON {TableName}(ClaimOwnerId, ClaimBatchId);";
            index.ExecuteNonQuery();
            migration.Commit();
        }

        private static void EnsureColumnExistsCore(
            SqliteConnection connection,
            SqliteTransaction transaction,
            string columnName,
            string columnDefinition)
        {
            using (var info = connection.CreateCommand())
            {
                info.Transaction = transaction;
                info.CommandText = $"PRAGMA table_info({TableName});";
                using var reader = info.ExecuteReader();
                while (reader.Read())
                {
                    if (string.Equals(reader.GetString(1), columnName, StringComparison.OrdinalIgnoreCase))
                        return;
                }
            }

            using var alter = connection.CreateCommand();
            alter.Transaction = transaction;
            alter.CommandText = $"ALTER TABLE {TableName} ADD COLUMN {columnName} {columnDefinition};";
            alter.ExecuteNonQuery();
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

            long maxPages = Math.Max(1L, _maxApplicationSpoolPhysicalBytes / pageSize);
            long journalSizeLimit = Math.Max(
                pageSize * 8L,
                Math.Min(_maxApplicationSpoolPhysicalBytes / 32L, 8L * 1024L * 1024L));

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

            using FileStream? recoveryLock = TryAcquireRecoveryLock(RecoveryLockTimeout);
            if (recoveryLock is null)
            {
                SelfLog.WriteLine(
                    "SerilogRelay could not acquire cross-process corruption-recovery coordination for spool '{0}'.",
                    _databasePath);
                return false;
            }

            if (IsCurrentSpoolHealthyCore())
                return true;

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

                Interlocked.Exchange(
                    ref _pendingCount,
                    GetClaimablePendingCountCore(DateTimeOffset.UtcNow));
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

        private FileStream? TryAcquireRecoveryLock(TimeSpan timeout)
        {
            if (_databasePath is null)
                return null;

            string lockPath = _databasePath + ".recovery.lock";
            DateTimeOffset deadline = DateTimeOffset.UtcNow + timeout;
            while (true)
            {
                try
                {
                    return new FileStream(
                        lockPath,
                        FileMode.OpenOrCreate,
                        FileAccess.ReadWrite,
                        FileShare.None);
                }
                catch (IOException)
                {
                    if (DateTimeOffset.UtcNow >= deadline)
                        return null;

                    Thread.Sleep(50);
                }
            }
        }

        private bool IsCurrentSpoolHealthyCore()
        {
            try
            {
                using var conn = new SqliteConnection(_connectionString);
                conn.Open();
                ConfigurePragmas(conn);
                using var command = conn.CreateCommand();
                command.CommandText = "PRAGMA quick_check(1);";
                return string.Equals(
                    Convert.ToString(command.ExecuteScalar(), CultureInfo.InvariantCulture),
                    "ok",
                    StringComparison.OrdinalIgnoreCase);
            }
            catch (SqliteException ex) when (IsCorruptionError(ex))
            {
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
