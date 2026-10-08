using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.Data.Sqlite;

using Serilog.Debugging;

namespace Eigenverft.NetLib.SerilogRelay
{
    public partial class SerilogRelaySink
    {
        /// <summary>
        /// Deletes sent entries whose age since spool insertion (CreatedAt) reaches the configured retention
        /// and optionally expires unsent entries. Acknowledgment does not restart the age.
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

        private bool TryPersistLogEntryWithRecovery(LogEntry entry)
        {
            // Capacity rejection is not write recovery. Observe acceptance under the database gate.
            return ExecuteDatabaseWithRecovery(() =>
            {
                bool persisted = TryPersistLogEntryWithReclamationCore(entry);
                if (persisted)
                    MarkSpoolRecovered();
                return persisted;
            }, updatesSpool: false);
        }

        // On SQLITE_FULL, sent and then oldest eligible unsent rows may be reclaimed.
        // False means the new event was rejected by capacity; other storage errors propagate.
        private bool TryPersistLogEntryWithReclamationCore(LogEntry entry)
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

        private void InsertLogEntryCore(SqliteConnection conn, LogEntry entry)
        {
            using var transaction = conn.BeginTransaction();
            InsertLogEntryCore(conn, entry, transaction);
            EnsureClaimHeadroomCore(conn, transaction);
            transaction.Commit();
        }

        private static void InsertLogEntryCore(SqliteConnection conn, LogEntry entry, SqliteTransaction transaction)
        {
            using var cmd = conn.CreateCommand();
            cmd.Transaction = transaction;
            cmd.CommandText = $@"
INSERT INTO {TableName}
  (EventId, ApplicationId, ApplicationVersion, MachineId, ProcessId, Timestamp, Level, RenderMessage, MessageTemplate, TraceId, SpanId, Exception, Properties, Sent)
VALUES
  ($eventId, $applicationId, $applicationVersion, $machineId, $processId, $ts, $lvl, $rendered, $tmpl, $tid, $sid, $ex, $props, 0);";

            cmd.Parameters.AddWithValue("$eventId", entry.EventId);
            cmd.Parameters.AddWithValue("$applicationId", entry.ApplicationId);
            cmd.Parameters.AddWithValue("$applicationVersion", (object?)entry.ApplicationVersion ?? DBNull.Value);
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

        private void EnsureClaimHeadroomCore(SqliteConnection conn, SqliteTransaction transaction)
        {
            using var command = conn.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = "SELECT page_size, page_count, freelist_count FROM pragma_page_size(), pragma_page_count(), pragma_freelist_count();";
            using var reader = command.ExecuteReader();
            reader.Read();
            long pageSize = reader.GetInt64(0);
            long maxPages = Math.Max(1L, _maxApplicationSpoolPhysicalBytes / pageSize);
            long usedPages = reader.GetInt64(1) - reader.GetInt64(2);
            // Keep room within the existing budget for growing claim rows and splitting indexes.
            // Small spools reserve at most a quarter; the claim fallback can use smaller batches.
            long reservePages = Math.Min(maxPages / 4L, 8L + ((_maxBatchSize * 512L + pageSize - 1L) / pageSize));
            if (usedPages + reservePages > maxPages)
                throw new SqliteException("Spool capacity is reserved for delivery claim metadata.", SQLitePCL.raw.SQLITE_FULL);
        }

        private bool TryReclaimAndPersistApplicationSpoolCore(LogEntry entry)
        {
            using var conn = new SqliteConnection(_connectionString);
            conn.Open();
            ConfigurePragmas(conn);

            // Status may reclaim sent rows, but must leave every unsent row in place.
            int maximumUnsentLimit = entry.IsRelayStatusEvent ? 0 : 512;
            for (int unsentLimit = 0; unsentLimit <= maximumUnsentLimit; unsentLimit += 64)
            {
                using var transaction = conn.BeginTransaction();
                DeleteOldestApplicationSpoolRowsCore(conn, transaction, sent: true, limit: int.MaxValue);
                int deletedUnsent = DeleteOldestApplicationSpoolRowsCore(conn, transaction, sent: false, limit: unsentLimit);

                try
                {
                    InsertLogEntryCore(conn, entry, transaction);
                    EnsureClaimHeadroomCore(conn, transaction);
                    transaction.Commit();
                }
                catch (SqliteException ex) when (IsFullError(ex))
                {
                    // The transaction restores reclaimed rows when the replacement still does not fit.
                    if (entry.IsRelayStatusEvent || deletedUnsent < unsentLimit)
                        return false;
                    continue;
                }

                if (deletedUnsent > 0)
                {
                    ReportApplicationSpoolEvictions(deletedUnsent);
                }

                return true;
            }

            return false;
        }

        private void ReportApplicationSpoolEvictions(int count)
        {
            Interlocked.Add(ref _applicationSpoolDroppedCount, count);
            if (Interlocked.Exchange(ref _spoolDropWakePending, 1) == 0)
                WakeStatusLoop();
            Volatile.Write(ref _pendingCountNeedsRefresh, 1);
            if (Interlocked.Exchange(ref _applicationSpoolOverflowReported, 1) == 0)
                SelfLog.WriteLine("SerilogRelay spool reached its {0}-byte budget; {1} oldest unsent events were evicted to keep disk usage bounded.", _maxApplicationSpoolPhysicalBytes, count);
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
            lock (_statusTransitionLock)
            {
                if (Interlocked.CompareExchange(ref _spoolUnavailable, 1, 0) != 0)
                    return;
                RecordStatusTransitionCore(StatusTransitionKind.SpoolUnavailable);
            }
            WakeStatusLoop();

            SelfLog.WriteLine(
                "SerilogRelay local spool is unavailable; using the bounded volatile emergency buffer. Error: {0}",
                exception.Message);
        }

        private void MarkSpoolRecovered()
        {
            lock (_statusTransitionLock)
            {
                if (_spoolDisabledForLifetime)
                    return;
                if (Interlocked.Exchange(ref _spoolUnavailable, 0) == 0)
                    return;
                RecordStatusTransitionCore(StatusTransitionKind.SpoolRecovered);
            }
            WakeStatusLoop();

            Interlocked.Exchange(ref _emergencyOverflowReported, 0);
            SelfLog.WriteLine("SerilogRelay local spool recovered; durable persistence resumed.");
        }

        private async Task<ClaimedLogBatch> ClaimPendingAsync(
            int limit,
            CancellationToken token)
        {
            string claimBatchId = Guid.NewGuid().ToString("N");

            return await ExecuteDatabaseWithRecoveryAsync(
                async () =>
                {
                    using var conn = new SqliteConnection(_connectionString);
                    await conn.OpenAsync(token).ConfigureAwait(false);
                    ConfigurePragmas(conn);

                    bool claimCapacityLimited = false;
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
                        claim.Parameters.AddWithValue("$claimUntil", 0L);
                        claim.Parameters.AddWithValue("$now", 0L);
                        claim.Parameters.AddWithValue("$limit", limit);
                        int claimLimit = limit;
                        int reclaimLimit = -1;
                        while (true)
                        {
                            token.ThrowIfCancellationRequested();
                            using var transaction = conn.BeginTransaction(deferred: false);
                            claim.Transaction = transaction;
                            claim.Parameters["$limit"].Value = claimLimit;
                            int deletedUnsent = 0;
                            if (reclaimLimit >= 0)
                            {
                                DeleteOldestApplicationSpoolRowsCore(conn, transaction, sent: true, limit: int.MaxValue);
                                deletedUnsent = DeleteOldestApplicationSpoolRowsCore(conn, transaction, sent: false, limit: reclaimLimit);
                            }

                            try
                            {
                                DateTimeOffset now = _timeProvider.GetUtcNow();
                                claim.Parameters["$now"].Value = now.ToUnixTimeMilliseconds();
                                claim.Parameters["$claimUntil"].Value = now.Add(ClaimLeaseDuration).ToUnixTimeMilliseconds();
                                int claimedCount = await claim.ExecuteNonQueryAsync(token).ConfigureAwait(false);
                                if (claimedCount == 0 && deletedUnsent > 0)
                                    throw new SqliteException("Capacity reclamation left no event to reserve.", SQLitePCL.raw.SQLITE_FULL);
                                transaction.Commit();
                            }
                            catch (SqliteException ex) when (IsFullError(ex))
                            {
                                // Old/full spools may lack the reserve. First shrink the claim;
                                // only reclaim capacity if even one event cannot be reserved.
                                if (claimLimit > 1)
                                {
                                    claimLimit = Math.Max(1, claimLimit / 2);
                                    continue;
                                }
                                if (reclaimLimit >= 512 || (reclaimLimit > 0 && deletedUnsent < reclaimLimit))
                                    throw;
                                reclaimLimit = reclaimLimit < 1 ? reclaimLimit + 1 : Math.Min(512, reclaimLimit * 2);
                                continue;
                            }

                            claimCapacityLimited = claimLimit < limit || reclaimLimit >= 0;
                            if (deletedUnsent > 0)
                                ReportApplicationSpoolEvictions(deletedUnsent);
                            break;
                        }
                    }

                    var entries = new List<LogEntry>();
                    using var select = conn.CreateCommand();
                    select.CommandText = $@"
SELECT Id, EventId, ApplicationId, MachineId, ProcessId, Timestamp, Level, RenderMessage, MessageTemplate, TraceId, SpanId, Exception, Properties, ApplicationVersion
  FROM {TableName}
 WHERE Sent = 0
   AND ClaimOwnerId = $owner
   AND ClaimBatchId = $claimBatchId
 ORDER BY Id ASC;";
                    select.Parameters.AddWithValue("$owner", _claimOwnerId);
                    select.Parameters.AddWithValue("$claimBatchId", claimBatchId);

                    long payloadBytes = GetEmptyBatchPayloadBytes();
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
                                ApplicationVersion = reader.IsDBNull(13) ? null : reader.GetString(13),
                            };
                            long nextPayloadBytes = GetBatchPayloadBytesWithNextEntry(payloadBytes, entries.Count, entry);

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

                    return new ClaimedLogBatch(claimBatchId, entries, payloadTargetReached, claimCapacityLimited);
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

        private async Task<DateTimeOffset?> RenewClaimLeaseAsync(ClaimedLogBatch batch, CancellationToken token)
        {
            return await ExecuteDatabaseWithRecoveryAsync<DateTimeOffset?>(
                async () =>
                {
                    using var conn = new SqliteConnection(_connectionString);
                    await conn.OpenAsync(token).ConfigureAwait(false);
                    ConfigurePragmas(conn);
                    // Acquire the SQLite writer before starting the lease clock. Recheck ownership
                    // after payload preparation: another process may already have taken over.
                    using var transaction = conn.BeginTransaction(deferred: false);
                    DateTimeOffset expiresAt = _timeProvider.GetUtcNow().Add(ClaimLeaseDuration);
                    using var renew = conn.CreateCommand();
                    renew.Transaction = transaction;
                    renew.CommandText = $@"
UPDATE {TableName}
   SET ClaimUntilUnixMs = $claimUntil
 WHERE Sent = 0 AND ClaimOwnerId = $owner AND ClaimBatchId = $batch;";
                    renew.Parameters.AddWithValue("$claimUntil", expiresAt.ToUnixTimeMilliseconds());
                    renew.Parameters.AddWithValue("$owner", _claimOwnerId);
                    renew.Parameters.AddWithValue("$batch", batch.ClaimBatchId);
                    if (await renew.ExecuteNonQueryAsync(token).ConfigureAwait(false) != batch.Entries.Count)
                        return null;
                    transaction.Commit();
                    return expiresAt;
                }, token).ConfigureAwait(false);
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

        // Ensure the logs table, origin metadata, and multi-process claim columns/indexes exist.
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
            EnsureColumnExistsCore(conn, migration, "ApplicationVersion", "TEXT");

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

        private bool EnsureSpoolInitializedCore()
        {
            if (_spoolInitialized)
                return false;

            EnsureTableCreatedCore();
            _spoolInitialized = true;
            return true;
        }

        private T ExecuteDatabaseWithRecovery<T>(Func<T> operation, bool updatesSpool = true)
        {
            _databaseGate.Wait();
            try
            {
                if (_spoolDisabledForLifetime)
                    throw new InvalidOperationException("This relay instance has permanently disabled its spool; emergency memory delivery remains available.");
                try
                {
                    bool initializedNow = EnsureSpoolInitializedCore();
                    T result = operation();
                    // A successful read alone does not prove that a previously failed
                    // write can resume. Schema initialization itself does prove write access.
                    if (updatesSpool || initializedNow)
                        MarkSpoolRecovered();
                    return result;
                }
                catch (SqliteException ex) when (IsCorruptionError(ex))
                {
                    TryRecoverCorruptedSpool(ex);
                    throw;
                }
                catch (Exception ex) when (ex is SqliteException or IOException or UnauthorizedAccessException)
                {
                    MarkSpoolUnavailable(ex);
                    throw;
                }
            }
            finally
            {
                _databaseGate.Release();
            }
        }

        private void ExecuteDatabaseWithRecovery(Action operation, bool updatesSpool = true)
            => ExecuteDatabaseWithRecovery(
                () =>
                {
                    operation();
                    return true;
                }, updatesSpool);

        private async Task<T> ExecuteDatabaseWithRecoveryAsync<T>(
            Func<Task<T>> operation,
            CancellationToken token)
        {
            await _databaseGate.WaitAsync(token).ConfigureAwait(false);
            T result;
            try
            {
                if (_spoolDisabledForLifetime)
                    throw new InvalidOperationException("This relay instance has permanently disabled its spool; emergency memory delivery remains available.");
                try
                {
                    EnsureSpoolInitializedCore();
                    result = await operation().ConfigureAwait(false);
                    MarkSpoolRecovered();
                }
                catch (SqliteException ex) when (IsCorruptionError(ex))
                {
                    TryRecoverCorruptedSpool(ex);
                    throw;
                }
                catch (Exception ex) when (ex is SqliteException or IOException or UnauthorizedAccessException)
                {
                    MarkSpoolUnavailable(ex);
                    throw;
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
