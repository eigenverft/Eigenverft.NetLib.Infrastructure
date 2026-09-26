using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.Data.Sqlite;

using Serilog;
using Serilog.Configuration;
using Serilog.Core;
using Serilog.Debugging;
using Serilog.Events;

namespace Eigenverft.NetLib.SerilogRelay
{
    /// <summary>
    /// Provides extension methods to configure the durable Serilog relay.
    /// </summary>
    public static class LoggerConfigurationSerilogRelayExtensions
    {
        private const string DefaultSpoolFileName = "SerilogRelay.db";

        /// <summary>
        /// Configures Serilog to persist events to a durable local spool and optionally relay pending events to an HTTP endpoint.
        /// </summary>
        /// <param name="loggerConfiguration">The Serilog sink configuration.</param>
        /// <param name="endpoint">The optional HTTP endpoint that receives batched log events.</param>
        /// <param name="spoolDirectory">Optional spool directory. Relative paths are resolved below the application-specific default directory.</param>
        /// <param name="spoolFileName">Optional spool filename. Defaults to <c>SerilogRelay.db</c>.</param>
        /// <param name="applicationId">Optional application identity used by the default spool directory. Defaults to the entry-assembly name.</param>
        /// <param name="dangerousAcceptAnyServerCertificate">When <see langword="true"/>, disables server-certificate validation for relay HTTP requests. Defaults to <see langword="false"/> and should only be enabled deliberately for trusted private/development infrastructure.</param>
        /// <param name="minimumBatchSize">The minimum pending-event count required before normal background delivery starts.</param>
        /// <param name="maximumBatchSize">The maximum number of events included in one HTTP batch.</param>
        /// <param name="baseInterval">The normal delay between background delivery attempts.</param>
        /// <param name="sentRetention">How long successfully sent events are retained locally.</param>
        /// <param name="unsentRetention">How long unsent events are retained locally.</param>
        /// <param name="restrictedToMinimumLevel">The minimum Serilog event level accepted by the sink.</param>
        /// <returns>The original Serilog logger configuration.</returns>
        public static LoggerConfiguration SerilogRelay(
            this LoggerSinkConfiguration loggerConfiguration,
            string? endpoint = null,
            string? spoolDirectory = null,
            string spoolFileName = DefaultSpoolFileName,
            string? applicationId = null,
            bool dangerousAcceptAnyServerCertificate = false,
            int minimumBatchSize = 20,
            int maximumBatchSize = 100,
            TimeSpan? baseInterval = null,
            TimeSpan? sentRetention = null,
            TimeSpan? unsentRetention = null,
            LogEventLevel restrictedToMinimumLevel = LevelAlias.Minimum)
        {
            string spoolPath = ResolveSpoolPath(spoolDirectory, spoolFileName, applicationId);
            Directory.CreateDirectory(Path.GetDirectoryName(spoolPath)!);

            string connectionString = new SqliteConnectionStringBuilder
            {
                DataSource = spoolPath,
                Mode = SqliteOpenMode.ReadWriteCreate,
                Cache = SqliteCacheMode.Shared,
            }.ToString();

            var sink = new SerilogRelaySink(
                connectionString,
                endpoint,
                minimumBatchSize,
                maximumBatchSize,
                baseInterval ?? TimeSpan.FromSeconds(5),
                sentRetention ?? TimeSpan.FromDays(1),
                unsentRetention ?? TimeSpan.FromDays(3),
                applicationId,
                dangerousAcceptAnyServerCertificate);
            return loggerConfiguration.Sink(sink, restrictedToMinimumLevel);
        }

        internal static string ResolveSpoolPath(
            string? spoolDirectory,
            string spoolFileName,
            string? applicationId)
            => ResolveSpoolPath(
                spoolDirectory,
                spoolFileName,
                applicationId,
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData));

        internal static string ResolveSpoolPath(
            string? spoolDirectory,
            string spoolFileName,
            string? applicationId,
            string localApplicationData)
        {
            if (string.IsNullOrWhiteSpace(spoolFileName)
                || Path.IsPathRooted(spoolFileName)
                || !string.Equals(Path.GetFileName(spoolFileName), spoolFileName, StringComparison.Ordinal)
                || spoolFileName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
            {
                throw new ArgumentException("Spool filename must be a valid filename without a directory component.", nameof(spoolFileName));
            }

            if (string.IsNullOrWhiteSpace(localApplicationData))
                throw new InvalidOperationException("The operating system did not provide a LocalApplicationData directory for the SerilogRelay spool.");

            string defaultDirectory = Path.Combine(
                localApplicationData,
                "Eigenverft",
                "SerilogRelay",
                ResolveApplicationId(applicationId));

            string resolvedDirectory = string.IsNullOrWhiteSpace(spoolDirectory)
                ? defaultDirectory
                : Path.IsPathRooted(spoolDirectory)
                    ? Path.GetFullPath(spoolDirectory)
                    : Path.GetFullPath(Path.Combine(defaultDirectory, spoolDirectory));

            return Path.Combine(resolvedDirectory, spoolFileName);
        }

        internal static string ResolveApplicationId(string? applicationId)
        {
            string candidate = string.IsNullOrWhiteSpace(applicationId)
                ? GetRuntimeApplicationId()
                : applicationId.Trim();

            string normalized = Regex.Replace(candidate, "[^A-Za-z0-9._-]+", "_").Trim('.', '_');
            return string.IsNullOrWhiteSpace(normalized) ? "Application" : normalized;
        }

        [ExcludeFromCodeCoverage]
        private static string GetRuntimeApplicationId()
            => Assembly.GetEntryAssembly()?.GetName().Name
                ?? AppDomain.CurrentDomain.FriendlyName;
    }

    /// <summary>
    /// A durable Serilog relay sink that persists each event immediately to SQLite
    /// and ships stored events reliably to an HTTP endpoint.
    /// </summary>
    public class SerilogRelaySink : ILogEventSink, IAsyncDisposable, IDisposable
    {

        private static readonly HttpClientHandler _defaultHttpHandler = new HttpClientHandler();

        private static readonly HttpClientHandler _dangerousHttpHandler = new HttpClientHandler
        {
            ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator,
        };

        private const int MaxBusyRetries = 5;
        private const int BusyRetryDelayMs = 100;
        private const string TableName = "SerilogRelayEvents";
        private const string CorruptionDirectoryName = "corrupted";
        private const string CorruptionEventType = "spool_corrupted";

        private const string TableSchema = @"
CREATE TABLE IF NOT EXISTS {0} (
    Id              INTEGER PRIMARY KEY AUTOINCREMENT,
    EventId         TEXT    NOT NULL UNIQUE,
    ApplicationId   TEXT    NOT NULL,
    MachineId       TEXT,
    ProcessId       INTEGER NOT NULL,
    Timestamp       TEXT    NOT NULL,
    Level           TEXT    NOT NULL,
    RenderMessage   TEXT    NOT NULL,
    MessageTemplate TEXT    NOT NULL,
    TraceId         TEXT,
    SpanId          TEXT,
    Exception       TEXT,
    Properties      TEXT,
    Sent            INTEGER NOT NULL DEFAULT 0,
    CreatedAt       TEXT    NOT NULL DEFAULT (datetime('now'))
);";

        private readonly string _connectionString;
        private readonly string? _databasePath;
        private readonly SemaphoreSlim _databaseGate = new SemaphoreSlim(1, 1);
        private readonly string? _endpoint;
        private readonly string _applicationId;
        private readonly string? _machineId;
        private readonly int _processId;
        private readonly int _minBatchSize;
        private readonly int _maxBatchSize;
        private readonly TimeSpan _baseInterval;
        private readonly TimeSpan _minInterval = TimeSpan.FromSeconds(1);
        private readonly TimeSpan _maxInterval = TimeSpan.FromMinutes(5);
        private TimeSpan _currentInterval;

        private readonly CancellationTokenSource _cts;
        private readonly Task _senderTask;
        private readonly HttpClient _httpClient;

        private long _pendingCount;
        private readonly object _signalLock = new object();
        private bool _hasNewLogs;

        private readonly object _disposeLock = new object();
        private Task? _disposeTask;
        private int _disposeStarted;

        /// <summary>
        /// Initializes a new instance of <see cref="SerilogRelaySink"/>.
        /// </summary>
        /// <param name="connectionString">The SQLite connection string.</param>
        /// <param name="endpoint">The HTTP endpoint to send batched logs.</param>
        /// <param name="minBatchItems">Minimum number of items before sending a batch.</param>
        /// <param name="maxBatchItems">Maximum number of items per batch.</param>
        /// <param name="baseInterval">Base delay interval between send attempts.</param>
        /// <param name="sentRetention">How long successfully sent entries remain in the local spool.</param>
        /// <param name="unsentRetention">How long unsent entries remain in the local spool.</param>
        /// <param name="applicationId">Optional logical application identity stored with newly persisted events.</param>
        /// <param name="dangerousAcceptAnyServerCertificate">Whether relay HTTP requests should bypass all server-certificate validation.</param>
        /// <remarks>
        /// Ensures <paramref name="minBatchItems"/> is at least 1 and not greater than <paramref name="maxBatchItems"/>.
        /// </remarks>
        internal SerilogRelaySink(
            string connectionString,
            string? endpoint,
            int minBatchItems,
            int maxBatchItems,
            TimeSpan baseInterval,
            TimeSpan sentRetention,
            TimeSpan unsentRetention,
            string? applicationId = null,
            bool dangerousAcceptAnyServerCertificate = false
            )
        {
            if (minBatchItems < 1)
                throw new ArgumentOutOfRangeException(nameof(minBatchItems), "Minimum batch size must be at least 1.");
            if (minBatchItems > maxBatchItems)
                throw new ArgumentException("Minimum batch size must be less than or equal to maximum batch size.", nameof(minBatchItems));

            _connectionString = connectionString;
            _databasePath = ResolveDatabasePath(connectionString);
            _endpoint = endpoint;
            _applicationId = LoggerConfigurationSerilogRelayExtensions.ResolveApplicationId(applicationId);
            _machineId = ResolveMachineId();
            _processId = Environment.ProcessId;
            _minBatchSize = minBatchItems;
            _maxBatchSize = maxBatchItems;
            _baseInterval = baseInterval;
            _currentInterval = baseInterval;

            ExecuteDatabaseWithRecovery(() =>
            {
                EnsureTableCreatedCore();
                CleanupOldLogsCore(sentRetention, unsentRetention);
            });

            _pendingCount = ExecuteDatabaseWithRecovery(GetPendingCountCore);


            _httpClient = new HttpClient(
                GetHttpClientHandler(dangerousAcceptAnyServerCertificate),
                disposeHandler: false)
            {
                Timeout = TimeSpan.FromSeconds(2),
            };
            _cts = new CancellationTokenSource();

            // only start sender if endpoint provided
            _senderTask = !string.IsNullOrEmpty(_endpoint)
                ? Task.Run(SenderLoopAsync, _cts.Token)
                : Task.CompletedTask;
        }

        internal static HttpClientHandler GetHttpClientHandler(bool dangerousAcceptAnyServerCertificate)
            => dangerousAcceptAnyServerCertificate ? _dangerousHttpHandler : _defaultHttpHandler;

        [ExcludeFromCodeCoverage]
        private static string? ResolveMachineId()
            => PhysicalMachineBinding.TryGetFingerprint(out string machineId) ? machineId : null;

        /// <summary>
        /// Deletes log entries older than the specified retention periods.
        /// </summary>
        /// <param name="sentRetention">How long to keep logs that have been sent (e.g. TimeSpan.FromDays(1)).</param>
        /// <param name="unsentRetention">How long to keep logs not yet sent (e.g. TimeSpan.FromDays(7)).</param>
        private void CleanupOldLogsCore(TimeSpan sentRetention, TimeSpan unsentRetention)
        {
            using var conn = new SqliteConnection(_connectionString);
            conn.Open();
            ConfigurePragmas(conn);

            using var cmd = conn.CreateCommand();
            cmd.CommandText = $@"
DELETE FROM {TableName}
 WHERE Sent = 1
   AND datetime(CreatedAt) <= datetime('now', $sentOffset);
DELETE FROM {TableName}
 WHERE Sent = 0
   AND datetime(CreatedAt) <= datetime('now', $unsentOffset);";

            cmd.Parameters.AddWithValue("$sentOffset", BuildSqliteOffset(sentRetention));
            cmd.Parameters.AddWithValue("$unsentOffset", BuildSqliteOffset(unsentRetention));
            cmd.ExecuteNonQuery();
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

        /// <summary>
        /// Persists a log event to the local SQLite spool before any network delivery attempt.
        /// </summary>
        /// <param name="logEvent">The Serilog event to persist.</param>
        public void Emit(LogEvent logEvent)
        {
            ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposeStarted) != 0, this);

            string eventId = Guid.NewGuid().ToString("D");
            int attempts = 0;
            while (true)
            {
                try
                {
                    ExecuteDatabaseWithRecovery(() => PersistLogEventCore(logEvent, eventId));

                    Interlocked.Increment(ref _pendingCount);
                    lock (_signalLock) { _hasNewLogs = true; }
                    break;
                }
                catch (SqliteException ex) when (IsBusyError(ex) && attempts++ < MaxBusyRetries)
                {
                    Thread.Sleep(BusyRetryDelayMs * attempts);
                }
                catch (Exception ex)
                {
                    SelfLog.WriteLine("Failed to write log to SQLite: {0}", ex.Message);
                    break;
                }
            }
        }

        private void PersistLogEventCore(LogEvent logEvent, string eventId)
        {
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
  ($eventId, $applicationId, $machineId, $processId, $ts, $lvl, $rendered, $tmpl, $tid, $sid, $ex, $props, 0);";

            cmd.Parameters.AddWithValue("$eventId", eventId);
            cmd.Parameters.AddWithValue("$applicationId", _applicationId);
            cmd.Parameters.AddWithValue("$machineId", (object?)_machineId ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$processId", _processId);
            cmd.Parameters.AddWithValue("$ts", logEvent.Timestamp.UtcDateTime.ToString("o", CultureInfo.InvariantCulture));
            cmd.Parameters.AddWithValue("$lvl", logEvent.Level.ToString());
            cmd.Parameters.AddWithValue("$rendered", logEvent.RenderMessage(CultureInfo.InvariantCulture));
            cmd.Parameters.AddWithValue("$tmpl", logEvent.MessageTemplate.Text);
            cmd.Parameters.AddWithValue("$tid", logEvent.TraceId?.ToHexString() ?? string.Empty);
            cmd.Parameters.AddWithValue("$sid", logEvent.SpanId?.ToHexString() ?? string.Empty);
            cmd.Parameters.AddWithValue("$ex", logEvent.Exception?.ToString() ?? string.Empty);
            cmd.Parameters.AddWithValue("$props", SerializeProperties(logEvent));

            cmd.ExecuteNonQuery();
            tx.Commit();
        }

        // Continuously send stored logs to the HTTP endpoint
        private async Task SenderLoopAsync()
        {
            var token = _cts.Token;
            while (!token.IsCancellationRequested)
            {
                try
                {
                    long pending = Interlocked.Read(ref _pendingCount);
                    bool didWork = false;

                    // only send when we've hit the minimum threshold
                    if (pending >= _minBatchSize)
                        didWork = await ProcessPendingAsync(ignoreMinBatch: false, token);

                    // backoff or reset interval
                    if (!didWork)
                        _currentInterval = TimeSpan.FromMilliseconds(
                            Math.Min(_currentInterval.TotalMilliseconds * 2, _maxInterval.TotalMilliseconds));
                    else if (pending > _maxBatchSize * 5)
                        _currentInterval = _minInterval;
                    else
                        _currentInterval = _baseInterval;

                    // wait or break early on new logs
                    var delay = Task.Delay(_currentInterval, token);
                    while (!token.IsCancellationRequested)
                    {
                        lock (_signalLock)
                        {
                            if (_hasNewLogs) { _hasNewLogs = false; break; }
                        }
                        if (await Task.WhenAny(delay, Task.Delay(100, token)) == delay)
                            break;
                    }
                }
                catch (OperationCanceledException) { break; }
                catch (Exception ex)
                {
                    SelfLog.WriteLine("Error in sender loop: {0}", ex.Message);
                    await Task.Delay(_baseInterval, token);
                }
            }
        }

        /// <summary>
        /// Processes and sends pending logs in batches, with optional bypass of the minimum threshold.
        /// </summary>
        /// <param name="token">Cancellation token.</param>
        /// <param name="ignoreMinBatch">If true, skips the minimum batch-size check.</param>
        /// <returns>True if any logs were successfully sent.</returns>
        private async Task<bool> ProcessPendingAsync(bool ignoreMinBatch, CancellationToken token)
        {
            long pending = Interlocked.Read(ref _pendingCount);
            if (!ignoreMinBatch && pending < _minBatchSize)
                return false;
            if (string.IsNullOrEmpty(_endpoint))
                return false;

            int sentCount = 0, batches = 0;
            while (Interlocked.Read(ref _pendingCount) > 0 && batches++ < 20 && !token.IsCancellationRequested)
            {
                var entries = await LoadUnsentAsync(_maxBatchSize, token);
                if (entries.Count == 0) break;

                // only dispatch batches meeting the minimum size unless forced
                if (!ignoreMinBatch && entries.Count < _minBatchSize)
                    break;

                if (!await SendBatchAsync(entries, token)) break;
                await MarkAsSentAsync(entries, token);

                Interlocked.Exchange(ref _pendingCount, ExecuteDatabaseWithRecovery(GetPendingCountCore));
                sentCount += entries.Count;
                await Task.Delay(100, token);
            }
            return sentCount > 0;
        }

        // Send one batch of logs over HTTP using AOT-compatible source-gen context
        private async Task<bool> SendBatchAsync(List<LogEntry> entries, CancellationToken token)
        {
            var payload = new LogBatchPayload
            {
                ProtocolVersion = 1,
                BatchId = Guid.NewGuid().ToString(),
                Timestamp = DateTime.UtcNow.ToString("o"),
                Count = entries.Count,
                Logs = entries
            };
            var json = JsonSerializer.Serialize(payload, LogBatchJsonContext.Default.LogBatchPayload);
            using var content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");
            try
            {
                using var resp = await _httpClient.PostAsync(_endpoint, content, token);
                if (resp.IsSuccessStatusCode) return true;
                if ((int)resp.StatusCode == 429 && resp.Headers.RetryAfter?.Delta != null)
                    _currentInterval = resp.Headers.RetryAfter.Delta.Value;
                SelfLog.WriteLine("HTTP relay returned status code {0}.", (int)resp.StatusCode);
                return false;
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                SelfLog.WriteLine("HTTP relay request failed: {0}", ex.Message);
                return false;
            }
        }

        // Load unsent log entries from SQLite
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

        // Apply durable settings to SQLite connection
        private static void ConfigurePragmas(SqliteConnection conn)
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
PRAGMA journal_mode = WAL;
PRAGMA synchronous = FULL;
PRAGMA busy_timeout = 5000;";
            cmd.ExecuteNonQuery();
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

        // Detect SQLite busy or locked errors
        private static bool IsBusyError(SqliteException ex)
            => ex.SqliteErrorCode == SQLitePCL.raw.SQLITE_BUSY
               || ex.SqliteErrorCode == SQLitePCL.raw.SQLITE_LOCKED;

        /// <summary>
        /// Synchronously disposes the sink, ensuring pending logs are flushed.
        /// </summary>
        public void Dispose()
        {
            GetOrCreateDisposeTask().GetAwaiter().GetResult();
            GC.SuppressFinalize(this);
        }

        /// <summary>
        /// Flushes pending logs on shutdown, ensuring all writes and sends complete.
        /// </summary>
        public async ValueTask DisposeAsync()
        {
            await GetOrCreateDisposeTask().ConfigureAwait(false);
            GC.SuppressFinalize(this);
        }

        private Task GetOrCreateDisposeTask()
        {
            lock (_disposeLock)
            {
                if (_disposeTask is not null)
                    return _disposeTask;

                Volatile.Write(ref _disposeStarted, 1);
                _disposeTask = Task.Run(DisposeCoreAsync);
                return _disposeTask;
            }
        }

        private async Task DisposeCoreAsync()
        {
            _cts.Cancel();
            try
            {
                try
                {
                    await _senderTask.ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (_cts.IsCancellationRequested)
                {
                }

                if (!string.IsNullOrEmpty(_endpoint))
                {
                    var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(3);
                    while (DateTime.UtcNow < deadline)
                    {
                        long count = ExecuteDatabaseWithRecovery(GetPendingCountCore);
                        if (count == 0)
                            break;

                        bool didWork = await ProcessPendingAsync(ignoreMinBatch: true, CancellationToken.None).ConfigureAwait(false);
                        if (!didWork)
                            await Task.Delay(100).ConfigureAwait(false);
                    }
                }
            }
            finally
            {
                _httpClient.Dispose();
                _cts.Dispose();
                _databaseGate.Dispose();
            }
        }

        private static string SerializeProperties(LogEvent logEvent)
        {
            var properties = new Dictionary<string, string>(logEvent.Properties.Count);
            foreach (var kvp in logEvent.Properties)
            {
                properties.Add(kvp.Key, kvp.Value.ToString()!);
            }

            return JsonSerializer.Serialize(
                properties,
                typeof(Dictionary<string, string>),
                LogBatchJsonContext.Default);
        }
    }

    // JSON Source Generator Context for AOT/Trimming compatibility
    [JsonSerializable(typeof(LogBatchPayload))]
    [JsonSerializable(typeof(List<LogEntry>))]
    [JsonSerializable(typeof(LogEntry))]
    [JsonSerializable(typeof(Dictionary<string, string>))]
    [JsonSourceGenerationOptions(
        PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
        WriteIndented = false,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
    internal partial class LogBatchJsonContext : JsonSerializerContext
    {
    }

    /// <summary>
    /// Payload wrapper for JSON serialization of log batches.
    /// </summary>
    internal class LogBatchPayload
    {
        public int ProtocolVersion { get; set; }
        public List<LogEntry> Logs { get; set; } = new List<LogEntry>();
        public string BatchId { get; set; } = string.Empty;
        public string Timestamp { get; set; } = string.Empty;
        public int Count { get; set; }
    }

    /// <summary>
    /// Represents a log entry loaded from SQLite.
    /// </summary>
    internal class LogEntry
    {
        public long Id { get; set; }
        public string EventId { get; set; } = string.Empty;
        public string ApplicationId { get; set; } = string.Empty;
        public string? MachineId { get; set; }
        public int ProcessId { get; set; }
        public string Timestamp { get; set; } = string.Empty;
        public string Level { get; set; } = string.Empty;
        public string RenderMessage { get; set; } = string.Empty;
        public string MessageTemplate { get; set; } = string.Empty;
        public string? TraceId { get; set; }
        public string? SpanId { get; set; }
        public string? Exception { get; set; }
        public string? Properties { get; set; }
    }
}