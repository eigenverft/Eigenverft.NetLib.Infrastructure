using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
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
    /// Provides extension methods to configure the high-reliability SQLite sink.
    /// </summary>
    public static class LoggerConfigurationSQLiteSinkHttp
    {
        /// <summary>
        /// Configures Serilog to persist events to SQLite and optionally relay pending events to an HTTP endpoint.
        /// </summary>
        /// <param name="loggerConfiguration">The Serilog sink configuration.</param>
        /// <param name="connectionString">The SQLite connection string used for the durable local spool.</param>
        /// <param name="tableName">The SQLite table name used to store log events.</param>
        /// <param name="endpoint">The optional HTTP endpoint that receives batched log events.</param>
        /// <param name="minimumBatchSize">The minimum pending-event count required before normal background delivery starts.</param>
        /// <param name="maximumBatchSize">The maximum number of events included in one HTTP batch.</param>
        /// <param name="baseInterval">The normal delay between background delivery attempts.</param>
        /// <param name="sentRetention">How long successfully sent events are retained locally.</param>
        /// <param name="unsentRetention">How long unsent events are retained locally.</param>
        /// <param name="restrictedToMinimumLevel">The minimum Serilog event level accepted by the sink.</param>
        /// <returns>The original Serilog logger configuration.</returns>
        public static LoggerConfiguration SQLiteSinkHttp(
            this LoggerSinkConfiguration loggerConfiguration,
            string connectionString,
            string tableName,
            string? endpoint = null,
            int minimumBatchSize = 20,
            int maximumBatchSize = 100,
            TimeSpan? baseInterval = null,
            TimeSpan? sentRetention = null,
            TimeSpan? unsentRetention = null,
            LogEventLevel restrictedToMinimumLevel = LevelAlias.Minimum)
        {
            var sink = new SQLiteSinkHttp(
                connectionString,
                tableName,
                endpoint,
                minimumBatchSize,
                maximumBatchSize,
                baseInterval ?? TimeSpan.FromSeconds(5),
                sentRetention ?? TimeSpan.FromDays(1),
                unsentRetention ?? TimeSpan.FromDays(3));
            return loggerConfiguration.Sink(sink, restrictedToMinimumLevel);
        }
    }

    /// <summary>
    /// A high-reliability Serilog sink that persists each event immediately to SQLite
    /// and ships stored events reliably to an HTTP endpoint.
    /// </summary>
    public class SQLiteSinkHttp : ILogEventSink, IAsyncDisposable, IDisposable
    {

        private static readonly HttpClientHandler _handler = new HttpClientHandler
        {
            // .NET Core / .NET 5+ shortcut
            ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator

            // Or, for full control:
            // ServerCertificateCustomValidationCallback = (request, cert, chain, errors) => true
        };

        private const int MaxBusyRetries = 5;
        private const int BusyRetryDelayMs = 100;

        private const string TableSchema = @"
CREATE TABLE IF NOT EXISTS {0} (
    Id              INTEGER PRIMARY KEY AUTOINCREMENT,
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
        private readonly string _tableName;
        private readonly string? _endpoint;
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

        /// <summary>
        /// Initializes a new instance of <see cref="SQLiteSinkHttp"/>.
        /// </summary>
        /// <param name="connectionString">The SQLite connection string.</param>
        /// <param name="tableName">The table name for storing logs.</param>
        /// <param name="endpoint">The HTTP endpoint to send batched logs.</param>
        /// <param name="minBatchItems">Minimum number of items before sending a batch.</param>
        /// <param name="maxBatchItems">Maximum number of items per batch.</param>
        /// <param name="baseInterval">Base delay interval between send attempts.</param>
        /// <param name="sentRetention">How long successfully sent entries remain in the local spool.</param>
        /// <param name="unsentRetention">How long unsent entries remain in the local spool.</param>
        /// <remarks>
        /// Ensures <paramref name="minBatchItems"/> is at least 1 and not greater than <paramref name="maxBatchItems"/>.
        /// </remarks>
        public SQLiteSinkHttp(
            string connectionString,
            string tableName,
            string? endpoint,
            int minBatchItems,
            int maxBatchItems,
            TimeSpan baseInterval,
            TimeSpan sentRetention,
            TimeSpan unsentRetention
            )
        {
            SelfLog.Enable(Console.Error);

            if (string.IsNullOrEmpty(tableName) || !Regex.IsMatch(tableName, "^[A-Za-z0-9_]+$"))
                throw new ArgumentException("Table name must be alphanumeric or underscore.", nameof(tableName));

            if (minBatchItems < 1)
                throw new ArgumentOutOfRangeException(nameof(minBatchItems), "Minimum batch size must be at least 1.");
            if (minBatchItems > maxBatchItems)
                throw new ArgumentException("Minimum batch size must be less than or equal to maximum batch size.", nameof(minBatchItems));

            _connectionString = connectionString;
            _tableName = tableName;
            _endpoint = endpoint;
            _minBatchSize = minBatchItems;
            _maxBatchSize = maxBatchItems;
            _baseInterval = baseInterval;
            _currentInterval = baseInterval;

            EnsureTableCreated();

            CleanupOldLogs(sentRetention, unsentRetention);

            _pendingCount = GetPendingCount();


            _httpClient = new HttpClient(_handler, disposeHandler: false) { Timeout = TimeSpan.FromSeconds(2) };
            _cts = new CancellationTokenSource();

            // only start sender if endpoint provided
            _senderTask = !string.IsNullOrEmpty(_endpoint)
                ? Task.Run(SenderLoopAsync, _cts.Token)
                : Task.CompletedTask;
        }

        /// <summary>
        /// Deletes log entries older than the specified retention periods.
        /// </summary>
        /// <param name="sentRetention">How long to keep logs that have been sent (e.g. TimeSpan.FromDays(1)).</param>
        /// <param name="unsentRetention">How long to keep logs not yet sent (e.g. TimeSpan.FromDays(7)).</param>
        private void CleanupOldLogs(TimeSpan sentRetention, TimeSpan unsentRetention)
        {
            using var conn = new SqliteConnection(_connectionString);
            conn.Open();
            ConfigurePragmas(conn);

            using var cmd = conn.CreateCommand();
            cmd.CommandText = $@"
DELETE FROM {_tableName}
 WHERE Sent = 1
   AND datetime(CreatedAt) <= datetime('now', $sentOffset);
DELETE FROM {_tableName}
 WHERE Sent = 0
   AND datetime(CreatedAt) <= datetime('now', $unsentOffset);";

            cmd.Parameters.AddWithValue("$sentOffset", BuildSqliteOffset(sentRetention));
            cmd.Parameters.AddWithValue("$unsentOffset", BuildSqliteOffset(unsentRetention));
            cmd.ExecuteNonQuery();
        }

        /// <summary>
        /// Converts a TimeSpan into a comma-separated sequence of SQLite relative modifiers,
        /// e.g. "-1 days, -2 hours, -30 minutes".
        /// </summary>
        /// <param name="span">The timespan to convert.</param>
        /// <returns>A string you can feed to datetime('now', &lt;this&gt;).</returns>
        private static string BuildSqliteOffset(TimeSpan span)
        {
            var parts = new List<string>();
            if (span.Days != 0) parts.Add($"{-span.Days} days");
            if (span.Hours != 0) parts.Add($"{-span.Hours} hours");
            if (span.Minutes != 0) parts.Add($"{-span.Minutes} minutes");
            if (span.Seconds != 0) parts.Add($"{-span.Seconds} seconds");
            return string.Join(", ", parts);
        }

        /// <summary>
        /// Persists a log event to the local SQLite spool before any network delivery attempt.
        /// </summary>
        /// <param name="logEvent">The Serilog event to persist.</param>
        public void Emit(LogEvent logEvent)
        {
            int attempts = 0;
            while (true)
            {
                try
                {
                    using var conn = new SqliteConnection(_connectionString);
                    conn.Open();
                    ConfigurePragmas(conn);
                    using var tx = conn.BeginTransaction();
                    using var cmd = conn.CreateCommand();
                    cmd.Transaction = tx;
                    cmd.CommandText = $@"
INSERT INTO {_tableName}
  (Timestamp, Level, RenderMessage, MessageTemplate, TraceId, SpanId, Exception, Properties, Sent)
VALUES
  ($ts, $lvl, $rendered, $tmpl, $tid, $sid, $ex, $props, 0);";

                    cmd.Parameters.AddWithValue("$ts", logEvent.Timestamp.UtcDateTime.ToString("o"));
                    cmd.Parameters.AddWithValue("$lvl", logEvent.Level.ToString());
                    cmd.Parameters.AddWithValue("$rendered", logEvent.RenderMessage());
                    cmd.Parameters.AddWithValue("$tmpl", logEvent.MessageTemplate.Text);
                    cmd.Parameters.AddWithValue("$tid", logEvent.TraceId?.ToHexString() ?? string.Empty);
                    cmd.Parameters.AddWithValue("$sid", logEvent.SpanId?.ToHexString() ?? string.Empty);
                    cmd.Parameters.AddWithValue("$ex", logEvent.Exception?.ToString() ?? string.Empty);
                    var propsJson = SerializeProperties(logEvent);
                    cmd.Parameters.AddWithValue("$props", propsJson);

                    cmd.ExecuteNonQuery();
                    tx.Commit();

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
                        didWork = await ProcessPendingAsync(token, ignoreMinBatch: false);

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
        private async Task<bool> ProcessPendingAsync(CancellationToken token, bool ignoreMinBatch)
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

                Interlocked.Add(ref _pendingCount, -entries.Count);
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
                BatchId = Guid.NewGuid().ToString(),
                Timestamp = DateTime.UtcNow.ToString("o"),
                Count = entries.Count,
                Logs = entries
            };
            var json = JsonSerializer.Serialize(payload, LogBatchJsonContext.Default.LogBatchPayload);
            using var content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");
            try
            {
                var resp = await _httpClient.PostAsync(_endpoint, content, token);
                if (resp.IsSuccessStatusCode) return true;
                if ((int)resp.StatusCode == 429 && resp.Headers.RetryAfter?.Delta != null)
                    _currentInterval = resp.Headers.RetryAfter.Delta.Value;
                //SelfLog.WriteLine("HTTP error {0}", resp.StatusCode);
                return false;
            }
            catch (Exception)
            {
                return false;
            }
        }

        // Load unsent log entries from SQLite
        private async Task<List<LogEntry>> LoadUnsentAsync(int limit, CancellationToken token)
        {
            var list = new List<LogEntry>();
            using var conn = new SqliteConnection(_connectionString);
            await conn.OpenAsync(token);
            ConfigurePragmas(conn);
            using var cmd = conn.CreateCommand();
            cmd.CommandText = $@"
SELECT Id, Timestamp, Level, RenderMessage, MessageTemplate, TraceId, SpanId, Exception, Properties
FROM {_tableName}
WHERE Sent = 0 ORDER BY Id ASC LIMIT {limit}";
            using var reader = await cmd.ExecuteReaderAsync(token);
            while (await reader.ReadAsync(token))
            {
                list.Add(new LogEntry
                {
                    Id = reader.GetInt64(0),
                    Timestamp = reader.GetString(1),
                    Level = reader.GetString(2),
                    RenderMessage = reader.GetString(3),
                    MessageTemplate = reader.GetString(4),
                    TraceId = reader.IsDBNull(5) ? null : reader.GetString(5),
                    SpanId = reader.IsDBNull(6) ? null : reader.GetString(6),
                    Exception = reader.IsDBNull(7) ? null : reader.GetString(7),
                    Properties = reader.IsDBNull(8) ? null : reader.GetString(8)
                });
            }
            return list;
        }

        // Mark specific log entries as sent in SQLite
        private async Task MarkAsSentAsync(List<LogEntry> entries, CancellationToken token)
        {
            using var conn = new SqliteConnection(_connectionString);
            await conn.OpenAsync(token);
            ConfigurePragmas(conn);
            using var tx = conn.BeginTransaction();
            using var cmd = conn.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = $"UPDATE {_tableName} SET Sent = 1 WHERE Id IN ({string.Join(",", entries.ConvertAll(e => e.Id))})";
            await cmd.ExecuteNonQueryAsync(token);
            tx.Commit();
        }

        // Retrieve the total number of unsent logs
        private long GetPendingCount()
        {
            using var conn = new SqliteConnection(_connectionString);
            conn.Open();
            ConfigurePragmas(conn);
            using var cmd = conn.CreateCommand();
            cmd.CommandText = $"SELECT COUNT(*) FROM {_tableName} WHERE Sent = 0";
            return Convert.ToInt64(cmd.ExecuteScalar());
        }

        // Ensure the logs table exists in SQLite
        private void EnsureTableCreated()
        {
            using var conn = new SqliteConnection(_connectionString);
            conn.Open();
            ConfigurePragmas(conn);
            using var cmd = conn.CreateCommand();
            cmd.CommandText = string.Format(TableSchema, _tableName);
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

        // Detect SQLite busy or locked errors
        private static bool IsBusyError(SqliteException ex)
            => ex.SqliteErrorCode == SQLitePCL.raw.SQLITE_BUSY
               || ex.SqliteErrorCode == SQLitePCL.raw.SQLITE_LOCKED;

        /// <summary>
        /// Synchronously disposes the sink, ensuring pending logs are flushed.
        /// </summary>
        public void Dispose()
        {
            // Ensure async disposal runs to completion
            DisposeAsync().GetAwaiter().GetResult();
        }

        /// <summary>
        /// Flushes pending logs on shutdown, ensuring all writes and sends complete.
        /// </summary>
        // Flush all logs and stop processing on shutdown
        public async ValueTask DisposeAsync()
        {
            _cts.Cancel();
            try { await _senderTask; } catch { /* ignore */ }

            if (!string.IsNullOrEmpty(_endpoint))
            {
                var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(3);
                while (DateTime.UtcNow < deadline)
                {
                    var count = GetPendingCount();
                    if (count == 0) break;

                    // Flush regardless of minimum threshold
                    var didWork = await ProcessPendingAsync(CancellationToken.None, ignoreMinBatch: true);
                    if (!didWork)
                        await Task.Delay(100);
                }
            }

            _httpClient.Dispose();
            _cts.Dispose();
        }

        private static string SerializeProperties(LogEvent logEvent)
        {
            var sb = new StringBuilder();
            sb.Append('{');
            bool first = true;
            foreach (var kvp in logEvent.Properties)
            {
                if (!first) sb.Append(',');
                first = false;
                sb.Append('\"').Append(EscapeJson(kvp.Key)).Append("\":\"")
                  .Append(EscapeJson(kvp.Value.ToString()!)).Append('\"');
            }
            sb.Append('}');
            return sb.ToString();
        }

        private static string EscapeJson(string s) => s
            .Replace("\\", "\\\\")
            .Replace("\"", "\\\"")
            .Replace("\b", "\\b")
            .Replace("\f", "\\f")
            .Replace("\n", "\\n")
            .Replace("\r", "\\r")
            .Replace("\t", "\\t");
    }

    // JSON Source Generator Context for AOT/Trimming compatibility
    [JsonSerializable(typeof(LogBatchPayload))]
    [JsonSerializable(typeof(List<LogEntry>))]
    [JsonSerializable(typeof(LogEntry))]
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