using System;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Net.Http;
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
            options.LocalStorage.SentRetention = sentRetention ?? TimeSpan.FromDays(1);
            options.LocalStorage.UnsentMaxAge = unsentRetention;

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
    public partial class SerilogRelaySink : ILogEventSink, IAsyncDisposable, IDisposable
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
            EndpointRetryOptions? retryOptions = null,
            EmergencyMemoryBufferOptions? emergencyOptions = null)
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
            _sentRetention = options.LocalStorage.SentRetention;
            _unsentRetention = options.LocalStorage.UnsentMaxAge;
            _maxSpoolBytes = options.LocalStorage.MaxBytes;
            _retryGate = new RetryGate(options.EndpointRetry);
            _emergencyBufferCapacity = options.EmergencyMemoryBuffer.MaxBufferedEvents;
            _maxEmergencyBufferedPayloadBytes = options.EmergencyMemoryBuffer.MaxBufferedPayloadBytes;

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
            EndpointRetryOptions? retryOptions,
            EmergencyMemoryBufferOptions? emergencyOptions)
        {
            var options = new SerilogRelayOptions();
            options.Delivery.MinimumBatchEvents = minBatchItems;
            options.Delivery.MaximumBatchEvents = maxBatchItems;
            options.Delivery.PollInterval = baseInterval;
            options.Delivery.MaximumBatchWait = maximumBatchWait ?? TimeSpan.FromSeconds(5);
            options.LocalStorage.SentRetention = sentRetention;
            options.LocalStorage.UnsentMaxAge = unsentRetention;

            if (retryOptions is not null)
            {
                options.EndpointRetry.InitialDelay = retryOptions.InitialDelay;
                options.EndpointRetry.Multiplier = retryOptions.Multiplier;
                options.EndpointRetry.MaximumDelay = retryOptions.MaximumDelay;
                options.EndpointRetry.JitterRatio = retryOptions.JitterRatio;
                options.EndpointRetry.RespectRetryAfter = retryOptions.RespectRetryAfter;
            }

            if (emergencyOptions is not null)
            {
                options.EmergencyMemoryBuffer.MaxBufferedEvents = emergencyOptions.MaxBufferedEvents;
                options.EmergencyMemoryBuffer.MaxBufferedPayloadBytes = emergencyOptions.MaxBufferedPayloadBytes;
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
            if (options.LocalStorage.SentRetention < TimeSpan.Zero)
                throw new ArgumentOutOfRangeException(nameof(options), "Sent retention must not be negative.");
            if (options.LocalStorage.UnsentMaxAge.HasValue && options.LocalStorage.UnsentMaxAge.Value < TimeSpan.Zero)
                throw new ArgumentOutOfRangeException(nameof(options), "Unsent retention must not be negative.");
            if (options.LocalStorage.MaxBytes < 4096)
                throw new ArgumentOutOfRangeException(nameof(options), "Spool byte budget must be at least one SQLite page (4096 bytes).");
            if (options.EmergencyMemoryBuffer.MaxBufferedEvents < 1)
                throw new ArgumentOutOfRangeException(nameof(options), "Emergency event capacity must be at least 1.");
            if (options.EmergencyMemoryBuffer.MaxBufferedPayloadBytes < 1)
                throw new ArgumentOutOfRangeException(nameof(options), "Emergency payload-byte capacity must be at least 1.");
        }

        internal static HttpClientHandler GetHttpClientHandler(bool dangerousAcceptAnyServerCertificate)
            => dangerousAcceptAnyServerCertificate ? _dangerousHttpHandler : _defaultHttpHandler;

        [ExcludeFromCodeCoverage]
        private static string? ResolveMachineId()
            => PhysicalMachineBinding.TryGetFingerprint(out string machineId) ? machineId : null;


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





        // Load unsent log entries from SQLite

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


    }

}