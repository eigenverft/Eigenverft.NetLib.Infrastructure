using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Channels;
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
            var options = new SerilogRelayOptions();
            options.Delivery.MinimumBatchEvents = minimumBatchSize;
            options.Delivery.MaximumBatchEvents = maximumBatchSize;
            options.Delivery.PollInterval = baseInterval ?? TimeSpan.FromSeconds(5);
            options.Spool.SentRetention = sentRetention ?? TimeSpan.FromDays(1);
            options.Spool.UnsentMaxAge = unsentRetention;

            return SerilogRelay(
                loggerConfiguration,
                endpoint,
                options,
                spoolDirectory,
                spoolFileName,
                applicationId,
                dangerousAcceptAnyServerCertificate,
                restrictedToMinimumLevel);
        }

        /// <summary>
        /// Configures SerilogRelay with grouped reliability options while preserving the simple default overload.
        /// </summary>
        /// <param name="loggerConfiguration">The Serilog sink configuration.</param>
        /// <param name="endpoint">The optional HTTP endpoint that receives batched log events.</param>
        /// <param name="options">Relay behavior options. All nested option groups have complete defaults.</param>
        /// <param name="spoolDirectory">Optional spool directory.</param>
        /// <param name="spoolFileName">Optional spool filename.</param>
        /// <param name="applicationId">Optional logical application identity.</param>
        /// <param name="dangerousAcceptAnyServerCertificate">Whether relay HTTP requests should bypass server-certificate validation.</param>
        /// <param name="restrictedToMinimumLevel">The minimum Serilog event level accepted by the sink.</param>
        /// <returns>The original Serilog logger configuration.</returns>
        public static LoggerConfiguration SerilogRelay(
            this LoggerSinkConfiguration loggerConfiguration,
            string? endpoint,
            SerilogRelayOptions options,
            string? spoolDirectory = null,
            string spoolFileName = DefaultSpoolFileName,
            string? applicationId = null,
            bool dangerousAcceptAnyServerCertificate = false,
            LogEventLevel restrictedToMinimumLevel = LevelAlias.Minimum)
        {
            ArgumentNullException.ThrowIfNull(options);

            string spoolPath = ResolveSpoolPath(spoolDirectory, spoolFileName, applicationId);
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(spoolPath)!);
            }
            catch (Exception ex)
            {
                SelfLog.WriteLine(
                    "SerilogRelay could not create the local spool directory; startup will continue in emergency mode if the spool remains unavailable. Error: {0}",
                    ex.Message);
            }

            string connectionString = new SqliteConnectionStringBuilder
            {
                DataSource = spoolPath,
                Mode = SqliteOpenMode.ReadWriteCreate,
                Cache = SqliteCacheMode.Shared,
            }.ToString();

            var sink = new SerilogRelaySink(
                connectionString,
                endpoint,
                options,
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
        private const int EmergencyRetryDelayMs = 250;
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
        private readonly TimeSpan _maximumBatchWait;
        private readonly TimeSpan _sentRetention;
        private readonly TimeSpan? _unsentRetention;
        private readonly long _maxSpoolBytes;
        private readonly TimeSpan _minimumCatchUpInterval = TimeSpan.FromSeconds(1);
        private readonly RetryGate _retryGate;

        private readonly CancellationTokenSource _cts;
        private readonly Task _senderTask;
        private readonly Channel<EmergencyEntry> _emergencyChannel;
        private readonly Task _emergencyTask;
        private readonly HttpClient _httpClient;
        private readonly int _emergencyBufferCapacity;
        private readonly long _maxEmergencyBufferedPayloadBytes;

        private long _pendingCount;
        private long _emergencyBufferedCount;
        private long _emergencyBufferedPayloadBytes;
        private long _emergencyDroppedCount;
        private long _spoolDroppedCount;
        private int _emergencyOverflowReported;
        private int _spoolUnavailable;
        private int _pendingCountNeedsRefresh;
        private int _spoolOverflowReported;
        private int _startupBacklogPending;
        private int _startupUnsentCleanupPending;
        private readonly object _signalLock = new object();
        private bool _hasNewLogs;
        private DateTimeOffset? _pendingSinceUtc;

        private readonly object _disposeLock = new object();
        private Task? _disposeTask;
        private int _disposeStarted;

        /// <summary>
        /// Initializes a new instance using the compatibility constructor used by existing tests and callers.
        /// </summary>
        internal SerilogRelaySink(
            string connectionString,
            string? endpoint,
            int minBatchItems,
            int maxBatchItems,
            TimeSpan baseInterval,
            TimeSpan sentRetention,
            TimeSpan? unsentRetention,
            string? applicationId = null,
            bool dangerousAcceptAnyServerCertificate = false,
            TimeSpan? maximumBatchWait = null,
            RetryOptions? retryOptions = null,
            EmergencyOptions? emergencyOptions = null)
            : this(
                connectionString,
                endpoint,
                CreateCompatibilityOptions(
                    minBatchItems,
                    maxBatchItems,
                    baseInterval,
                    sentRetention,
                    unsentRetention,
                    maximumBatchWait,
                    retryOptions,
                    emergencyOptions),
                applicationId,
                dangerousAcceptAnyServerCertificate)
        {
        }

        internal SerilogRelaySink(
            string connectionString,
            string? endpoint,
            SerilogRelayOptions options,
            string? applicationId = null,
            bool dangerousAcceptAnyServerCertificate = false)
        {
            ArgumentNullException.ThrowIfNull(options);
            ValidateOptions(options);

            _connectionString = connectionString;
            _databasePath = ResolveDatabasePath(connectionString);
            _endpoint = endpoint;
            _applicationId = LoggerConfigurationSerilogRelayExtensions.ResolveApplicationId(applicationId);
            _machineId = ResolveMachineId();
            _processId = Environment.ProcessId;
            _minBatchSize = options.Delivery.MinimumBatchEvents;
            _maxBatchSize = options.Delivery.MaximumBatchEvents;
            _baseInterval = options.Delivery.PollInterval;
            _maximumBatchWait = options.Delivery.MaximumBatchWait;
            _sentRetention = options.Spool.SentRetention;
            _unsentRetention = options.Spool.UnsentMaxAge;
            _maxSpoolBytes = options.Spool.MaxBytes;
            _retryGate = new RetryGate(options.Retry);
            _emergencyBufferCapacity = options.Emergency.MaxBufferedEvents;
            _maxEmergencyBufferedPayloadBytes = options.Emergency.MaxBufferedPayloadBytes;

            _httpClient = new HttpClient(
                GetHttpClientHandler(dangerousAcceptAnyServerCertificate),
                disposeHandler: false)
            {
                Timeout = TimeSpan.FromSeconds(2),
            };
            _cts = new CancellationTokenSource();
            _emergencyChannel = Channel.CreateBounded<EmergencyEntry>(
                new BoundedChannelOptions(_emergencyBufferCapacity)
                {
                    SingleReader = true,
                    SingleWriter = false,
                    FullMode = BoundedChannelFullMode.Wait,
                    AllowSynchronousContinuations = false,
                });

            try
            {
                ExecuteDatabaseWithRecovery(() =>
                {
                    EnsureTableCreatedCore();
                    CleanupOldLogsCore(
                        _sentRetention,
                        string.IsNullOrEmpty(_endpoint) ? _unsentRetention : null);
                });

                _pendingCount = ExecuteDatabaseWithRecovery(GetPendingCountCore);
                if (_pendingCount > 0)
                {
                    _startupBacklogPending = string.IsNullOrEmpty(_endpoint) ? 0 : 1;
                    lock (_signalLock)
                    {
                        _pendingSinceUtc = DateTimeOffset.UtcNow - _maximumBatchWait;
                    }
                }

                _startupUnsentCleanupPending =
                    !string.IsNullOrEmpty(_endpoint) && _unsentRetention.HasValue ? 1 : 0;
            }
            catch (Exception ex)
            {
                _pendingCount = 0;
                MarkSpoolUnavailable(ex);
            }

            _emergencyTask = Task.Run(EmergencyLoopAsync, _cts.Token);

            _senderTask = !string.IsNullOrEmpty(_endpoint)
                ? Task.Run(SenderLoopAsync, _cts.Token)
                : Task.CompletedTask;
        }

        private static SerilogRelayOptions CreateCompatibilityOptions(
            int minBatchItems,
            int maxBatchItems,
            TimeSpan baseInterval,
            TimeSpan sentRetention,
            TimeSpan? unsentRetention,
            TimeSpan? maximumBatchWait,
            RetryOptions? retryOptions,
            EmergencyOptions? emergencyOptions)
        {
            var options = new SerilogRelayOptions();
            options.Delivery.MinimumBatchEvents = minBatchItems;
            options.Delivery.MaximumBatchEvents = maxBatchItems;
            options.Delivery.PollInterval = baseInterval;
            options.Delivery.MaximumBatchWait = maximumBatchWait ?? TimeSpan.FromSeconds(5);
            options.Spool.SentRetention = sentRetention;
            options.Spool.UnsentMaxAge = unsentRetention;

            if (retryOptions is not null)
            {
                options.Retry.InitialDelay = retryOptions.InitialDelay;
                options.Retry.Multiplier = retryOptions.Multiplier;
                options.Retry.MaximumDelay = retryOptions.MaximumDelay;
                options.Retry.JitterRatio = retryOptions.JitterRatio;
                options.Retry.RespectRetryAfter = retryOptions.RespectRetryAfter;
            }

            if (emergencyOptions is not null)
            {
                options.Emergency.MaxBufferedEvents = emergencyOptions.MaxBufferedEvents;
                options.Emergency.MaxBufferedPayloadBytes = emergencyOptions.MaxBufferedPayloadBytes;
            }

            return options;
        }

        private static void ValidateOptions(SerilogRelayOptions options)
        {
            if (options.Delivery.MinimumBatchEvents < 1)
                throw new ArgumentOutOfRangeException(nameof(options), "Minimum batch size must be at least 1.");
            if (options.Delivery.MaximumBatchEvents < options.Delivery.MinimumBatchEvents)
                throw new ArgumentException("Minimum batch size must be less than or equal to maximum batch size.", nameof(options));
            if (options.Delivery.PollInterval <= TimeSpan.Zero)
                throw new ArgumentOutOfRangeException(nameof(options), "Delivery poll interval must be greater than zero.");
            if (options.Delivery.MaximumBatchWait <= TimeSpan.Zero)
                throw new ArgumentOutOfRangeException(nameof(options), "Maximum batch wait must be greater than zero.");
            if (options.Spool.SentRetention < TimeSpan.Zero)
                throw new ArgumentOutOfRangeException(nameof(options), "Sent retention must not be negative.");
            if (options.Spool.UnsentMaxAge.HasValue && options.Spool.UnsentMaxAge.Value < TimeSpan.Zero)
                throw new ArgumentOutOfRangeException(nameof(options), "Unsent retention must not be negative.");
            if (options.Spool.MaxBytes < 4096)
                throw new ArgumentOutOfRangeException(nameof(options), "Spool byte budget must be at least one SQLite page (4096 bytes).");
            if (options.Emergency.MaxBufferedEvents < 1)
                throw new ArgumentOutOfRangeException(nameof(options), "Emergency event capacity must be at least 1.");
            if (options.Emergency.MaxBufferedPayloadBytes < 1)
                throw new ArgumentOutOfRangeException(nameof(options), "Emergency payload-byte capacity must be at least 1.");
        }

        internal static HttpClientHandler GetHttpClientHandler(bool dangerousAcceptAnyServerCertificate)
            => dangerousAcceptAnyServerCertificate ? _dangerousHttpHandler : _defaultHttpHandler;

        [ExcludeFromCodeCoverage]
        private static string? ResolveMachineId()
            => PhysicalMachineBinding.TryGetFingerprint(out string machineId) ? machineId : null;

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

        /// <summary>
        /// Persists a log event to the local SQLite spool before any network delivery attempt.
        /// </summary>
        /// <param name="logEvent">The Serilog event to persist.</param>
        public void Emit(LogEvent logEvent)
        {
            ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposeStarted) != 0, this);

            LogEntry entry;
            try
            {
                entry = CreateLogEntry(logEvent, Guid.NewGuid().ToString("D"));
            }
            catch (Exception ex)
            {
                SelfLog.WriteLine("SerilogRelay could not materialize a log event: {0}", ex.Message);
                return;
            }

            int attempts = 0;
            while (true)
            {
                try
                {
                    bool persisted = ExecuteDatabaseWithRecovery(() => PersistLogEntryCore(entry));
                    if (!persisted)
                    {
                        RecordSpoolRejected();
                        return;
                    }

                    OnPersistedToSpool();
                    return;
                }
                catch (SqliteException ex) when (IsBusyError(ex) && attempts++ < MaxBusyRetries)
                {
                    Thread.Sleep(BusyRetryDelayMs * attempts);
                }
                catch (Exception ex)
                {
                    EnqueueEmergency(entry, ex);
                    return;
                }
            }
        }

        private LogEntry CreateLogEntry(LogEvent logEvent, string eventId)
        {
            return new LogEntry
            {
                EventId = eventId,
                ApplicationId = _applicationId,
                MachineId = _machineId,
                ProcessId = _processId,
                Timestamp = logEvent.Timestamp.UtcDateTime.ToString("o", CultureInfo.InvariantCulture),
                Level = logEvent.Level.ToString(),
                RenderMessage = logEvent.RenderMessage(CultureInfo.InvariantCulture),
                MessageTemplate = logEvent.MessageTemplate.Text,
                TraceId = logEvent.TraceId?.ToHexString() ?? string.Empty,
                SpanId = logEvent.SpanId?.ToHexString() ?? string.Empty,
                Exception = logEvent.Exception?.ToString() ?? string.Empty,
                Properties = SerializeProperties(logEvent),
            };
        }

        private bool PersistLogEntryCore(LogEntry entry)
        {
            int reclaimAttempts = 0;
            while (true)
            {
                try
                {
                    PersistLogEntryOnceCore(entry);
                    return true;
                }
                catch (SqliteException ex) when (IsFullError(ex) && reclaimAttempts++ < 8)
                {
                    if (!TryReclaimSpoolSpaceCore())
                        return false;
                }
            }
        }

        private void PersistLogEntryOnceCore(LogEntry entry)
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

        private void OnPersistedToSpool()
        {
            long pending = Interlocked.Exchange(ref _pendingCountNeedsRefresh, 0) != 0
                ? ExecuteDatabaseWithRecovery(GetPendingCountCore)
                : Interlocked.Increment(ref _pendingCount);

            lock (_signalLock)
            {
                if (pending == 1)
                    _pendingSinceUtc = DateTimeOffset.UtcNow;

                _hasNewLogs = true;
            }

            MarkSpoolRecovered();
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

        private void EnqueueEmergency(LogEntry entry, Exception exception)
        {
            MarkSpoolUnavailable(exception);

            long payloadBytes = GetEmergencyPayloadBytes(entry);
            if (!TryReserveEmergencyPayloadBytes(payloadBytes))
            {
                RecordEmergencyDrop();
                return;
            }

            Interlocked.Increment(ref _emergencyBufferedCount);
            if (_emergencyChannel.Writer.TryWrite(new EmergencyEntry(entry, payloadBytes)))
                return;

            Interlocked.Decrement(ref _emergencyBufferedCount);
            Interlocked.Add(ref _emergencyBufferedPayloadBytes, -payloadBytes);
            RecordEmergencyDrop();
        }

        private bool TryReserveEmergencyPayloadBytes(long payloadBytes)
        {
            long updated = Interlocked.Add(ref _emergencyBufferedPayloadBytes, payloadBytes);
            if (updated <= _maxEmergencyBufferedPayloadBytes)
                return true;

            Interlocked.Add(ref _emergencyBufferedPayloadBytes, -payloadBytes);
            return false;
        }

        private void RecordEmergencyDrop()
        {
            long dropped = Interlocked.Increment(ref _emergencyDroppedCount);
            if (Interlocked.Exchange(ref _emergencyOverflowReported, 1) != 0)
                return;

            SelfLog.WriteLine(
                "SerilogRelay emergency buffer limit reached ({0} events / {1} payload bytes). Events are now being dropped; total dropped: {2}.",
                _emergencyBufferCapacity,
                _maxEmergencyBufferedPayloadBytes,
                dropped);
        }

        private static long GetEmergencyPayloadBytes(LogEntry entry)
        {
            return sizeof(long)
                + sizeof(int)
                + GetUtf8ByteCount(entry.EventId)
                + GetUtf8ByteCount(entry.ApplicationId)
                + GetUtf8ByteCount(entry.MachineId)
                + GetUtf8ByteCount(entry.Timestamp)
                + GetUtf8ByteCount(entry.Level)
                + GetUtf8ByteCount(entry.RenderMessage)
                + GetUtf8ByteCount(entry.MessageTemplate)
                + GetUtf8ByteCount(entry.TraceId)
                + GetUtf8ByteCount(entry.SpanId)
                + GetUtf8ByteCount(entry.Exception)
                + GetUtf8ByteCount(entry.Properties);
        }

        private static int GetUtf8ByteCount(string? value)
            => value is null ? 0 : Encoding.UTF8.GetByteCount(value);

        private async Task EmergencyLoopAsync()
        {
            CancellationToken token = _cts.Token;

            try
            {
                while (await _emergencyChannel.Reader.WaitToReadAsync(token).ConfigureAwait(false))
                {
                    while (_emergencyChannel.Reader.TryRead(out EmergencyEntry? bufferedEntry))
                    {
                        LogEntry entry = bufferedEntry.Entry;
                        bool completed = false;
                        while (!completed)
                        {
                            token.ThrowIfCancellationRequested();

                            try
                            {
                                bool persisted = ExecuteDatabaseWithRecovery(() => PersistLogEntryCore(entry));
                                if (!persisted)
                                {
                                    MarkSpoolRecovered();
                                    RecordSpoolRejected();
                                    CompleteEmergencyEntry(bufferedEntry);
                                    completed = true;
                                    continue;
                                }

                                OnPersistedToSpool();
                                CompleteEmergencyEntry(bufferedEntry);
                                completed = true;
                                continue;
                            }
                            catch (Exception ex)
                            {
                                MarkSpoolUnavailable(ex);

                                try
                                {
                                    if (ExecuteDatabaseWithRecovery(() => EventExistsCore(entry.EventId)))
                                    {
                                        OnPersistedToSpool();
                                        CompleteEmergencyEntry(bufferedEntry);
                                        completed = true;
                                        continue;
                                    }
                                }
                                catch (Exception verificationException)
                                {
                                    MarkSpoolUnavailable(verificationException);
                                }
                            }

                            if (!string.IsNullOrEmpty(_endpoint)
                                && await SendBatchAsync(
                                    new List<LogEntry> { entry },
                                    token).ConfigureAwait(false))
                            {
                                CompleteEmergencyEntry(bufferedEntry);
                                completed = true;
                                continue;
                            }

                            await Task.Delay(EmergencyRetryDelayMs, token).ConfigureAwait(false);
                        }
                    }
                }
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
            }
        }

        private void CompleteEmergencyEntry(EmergencyEntry entry)
        {
            Interlocked.Decrement(ref _emergencyBufferedCount);
            Interlocked.Add(ref _emergencyBufferedPayloadBytes, -entry.PayloadBytes);
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

        // Continuously send stored logs to the HTTP endpoint.
        private async Task SenderLoopAsync()
        {
            CancellationToken token = _cts.Token;
            while (!token.IsCancellationRequested)
            {
                try
                {
                    long pending = Interlocked.Read(ref _pendingCount);
                    bool startupDrain = pending > 0 && Volatile.Read(ref _startupBacklogPending) != 0;
                    bool partialBatchDue = pending > 0
                        && (startupDrain || IsMaximumBatchWaitElapsed(DateTimeOffset.UtcNow));
                    bool shouldAttempt = pending >= _minBatchSize || partialBatchDue;
                    bool deliveryOpportunityCompleted = pending == 0;
                    bool didWork = false;

                    if (shouldAttempt)
                    {
                        didWork = await ProcessPendingAsync(
                            ignoreMinBatch: partialBatchDue,
                            token).ConfigureAwait(false);
                        deliveryOpportunityCompleted = true;

                        if (startupDrain)
                            Volatile.Write(ref _startupBacklogPending, 0);
                    }

                    if (deliveryOpportunityCompleted)
                        ApplyDeferredUnsentCleanup();

                    pending = Interlocked.Read(ref _pendingCount);
                    TimeSpan retryDelay = _retryGate.GetDelay(DateTimeOffset.UtcNow);
                    if (retryDelay > TimeSpan.Zero)
                    {
                        await Task.Delay(retryDelay, token).ConfigureAwait(false);
                        continue;
                    }

                    TimeSpan delay = GetSenderDelay(pending, didWork, DateTimeOffset.UtcNow);
                    await WaitForSenderDelayAsync(delay, token).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (token.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex)
                {
                    SelfLog.WriteLine("Error in sender loop: {0}", ex.Message);
                    await Task.Delay(_baseInterval, token).ConfigureAwait(false);
                }
            }
        }

        private bool IsMaximumBatchWaitElapsed(DateTimeOffset now)
        {
            lock (_signalLock)
            {
                if (!_pendingSinceUtc.HasValue)
                {
                    _pendingSinceUtc = now;
                    return false;
                }

                return now - _pendingSinceUtc.Value >= _maximumBatchWait;
            }
        }

        private TimeSpan GetSenderDelay(long pending, bool didWork, DateTimeOffset now)
        {
            if (pending > 0 && pending < _minBatchSize)
            {
                lock (_signalLock)
                {
                    if (_pendingSinceUtc.HasValue)
                    {
                        TimeSpan remaining = _maximumBatchWait - (now - _pendingSinceUtc.Value);
                        if (remaining <= TimeSpan.Zero)
                            return TimeSpan.FromMilliseconds(1);
                        if (remaining < _baseInterval)
                            return remaining;
                    }
                }
            }

            if (didWork && pending > _maxBatchSize * 5)
                return _minimumCatchUpInterval;

            return _baseInterval;
        }

        private async Task WaitForSenderDelayAsync(TimeSpan delay, CancellationToken token)
        {
            Task deadline = Task.Delay(delay, token);
            while (!token.IsCancellationRequested)
            {
                lock (_signalLock)
                {
                    if (_hasNewLogs)
                    {
                        _hasNewLogs = false;
                        return;
                    }
                }

                if (await Task.WhenAny(deadline, Task.Delay(100, token)).ConfigureAwait(false) == deadline)
                    return;
            }
        }

        private void ApplyDeferredUnsentCleanup()
        {
            if (Interlocked.Exchange(ref _startupUnsentCleanupPending, 0) == 0
                || !_unsentRetention.HasValue)
            {
                return;
            }

            ExecuteDatabaseWithRecovery(() =>
                CleanupOldLogsCore(_sentRetention, _unsentRetention));
            Interlocked.Exchange(ref _pendingCount, ExecuteDatabaseWithRecovery(GetPendingCountCore));

            if (Interlocked.Read(ref _pendingCount) == 0)
            {
                lock (_signalLock)
                {
                    _pendingSinceUtc = null;
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
                var entries = await LoadUnsentAsync(_maxBatchSize, token).ConfigureAwait(false);
                if (entries.Count == 0)
                    break;

                if (!ignoreMinBatch && entries.Count < _minBatchSize)
                    break;

                if (!await SendBatchAsync(entries, token).ConfigureAwait(false))
                    break;

                await MarkAsSentAsync(entries, token).ConfigureAwait(false);

                long remaining = ExecuteDatabaseWithRecovery(GetPendingCountCore);
                Interlocked.Exchange(ref _pendingCount, remaining);
                if (remaining == 0)
                {
                    lock (_signalLock)
                    {
                        _pendingSinceUtc = null;
                    }
                }

                sentCount += entries.Count;
                await Task.Delay(100, token).ConfigureAwait(false);
            }

            return sentCount > 0;
        }

        // Send one batch of logs over HTTP using AOT-compatible source-gen context.
        private async Task<bool> SendBatchAsync(List<LogEntry> entries, CancellationToken token)
        {
            if (!_retryGate.TryAcquire(DateTimeOffset.UtcNow))
                return false;

            var payload = new LogBatchPayload
            {
                ProtocolVersion = 1,
                BatchId = Guid.NewGuid().ToString(),
                Timestamp = DateTime.UtcNow.ToString("o"),
                Count = entries.Count,
                Logs = entries
            };
            var json = JsonSerializer.Serialize(payload, LogBatchJsonContext.Default.LogBatchPayload);
            using var content = new StringContent(json, Encoding.UTF8, "application/json");

            try
            {
                using var resp = await _httpClient.PostAsync(_endpoint, content, token).ConfigureAwait(false);
                if (resp.IsSuccessStatusCode)
                {
                    _retryGate.RecordSuccess();
                    return true;
                }

                DateTimeOffset? retryAfter = null;
                if ((int)resp.StatusCode == 429 && resp.Headers.RetryAfter is not null)
                {
                    if (resp.Headers.RetryAfter.Delta.HasValue)
                        retryAfter = DateTimeOffset.UtcNow + resp.Headers.RetryAfter.Delta.Value;
                    else if (resp.Headers.RetryAfter.Date.HasValue)
                        retryAfter = resp.Headers.RetryAfter.Date.Value;
                }

                _retryGate.RecordFailure(DateTimeOffset.UtcNow, retryAfter);
                SelfLog.WriteLine("HTTP relay returned status code {0}.", (int)resp.StatusCode);
                return false;
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
                _retryGate.CancelAttempt();
                throw;
            }
            catch (Exception ex)
            {
                _retryGate.RecordFailure(DateTimeOffset.UtcNow, retryAfter: null);
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
            _emergencyChannel.Writer.TryComplete();

            try
            {
                Task emergencyDrainDeadline = Task.Delay(TimeSpan.FromSeconds(3));
                await Task.WhenAny(_emergencyTask, emergencyDrainDeadline).ConfigureAwait(false);

                _cts.Cancel();

                try
                {
                    await Task.WhenAll(_senderTask, _emergencyTask).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (_cts.IsCancellationRequested)
                {
                }

                long volatileRemaining = Interlocked.Read(ref _emergencyBufferedCount);
                long dropped = Interlocked.Read(ref _emergencyDroppedCount);
                if (volatileRemaining > 0 || dropped > 0)
                {
                    SelfLog.WriteLine(
                        "SerilogRelay shutdown with {0} volatile emergency events unresolved and {1} emergency events dropped because the bounded buffer was full.",
                        volatileRemaining,
                        dropped);
                }

                if (!string.IsNullOrEmpty(_endpoint))
                {
                    using var shutdownCts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
                    CancellationToken shutdownToken = shutdownCts.Token;
                    try
                    {
                        while (!shutdownToken.IsCancellationRequested)
                        {
                            long count = ExecuteDatabaseWithRecovery(GetPendingCountCore);
                            if (count == 0)
                                break;

                            bool didWork = await ProcessPendingAsync(
                                ignoreMinBatch: true,
                                shutdownToken).ConfigureAwait(false);
                            if (!didWork)
                                await Task.Delay(100, shutdownToken).ConfigureAwait(false);
                        }
                    }
                    catch (OperationCanceledException) when (shutdownToken.IsCancellationRequested)
                    {
                    }
                    catch (Exception ex)
                    {
                        SelfLog.WriteLine(
                            "SerilogRelay could not complete durable spool drain during shutdown: {0}",
                            ex.Message);
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

        private sealed class EmergencyEntry
        {
            internal EmergencyEntry(LogEntry entry, long payloadBytes)
            {
                Entry = entry;
                PayloadBytes = payloadBytes;
            }

            internal LogEntry Entry { get; }

            internal long PayloadBytes { get; }
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