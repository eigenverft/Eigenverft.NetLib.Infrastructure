using System;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
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
        private const int MaximumApplicationIdLength = 256;
        private const string DefaultSpoolFileName = "SerilogRelay.db";

        /// <summary>
        /// Configures Serilog to try local SQLite persistence first, attempt bounded volatile buffering when storage fails or rejects an event, and optionally relay events to an HTTP endpoint.
        /// </summary>
        /// <remarks>
        /// Processes sharing one application spool may send each other's pending rows. Each new event records the originating application's version once, independently of the process that later sends it. The process that owns the current claim uses its own endpoint and bearer token. A 2xx response marks the still-owned claim delivered; non-2xx releases the claim before process-local retry backoff so another process/version may take over.
        /// </remarks>
        /// <param name="loggerConfiguration">The Serilog sink configuration.</param>
        /// <param name="endpoint">The optional HTTP endpoint used by this sink/process for batched delivery of any shared-spool rows it claims. Pending rows do not retain the endpoint of their creating process.</param>
        /// <param name="spoolDirectory">Optional spool directory. Relative paths are resolved below the application-specific default directory.</param>
        /// <param name="spoolFileName">Optional spool filename. Defaults to <c>SerilogRelay.db</c>.</param>
        /// <param name="applicationId">Optional application identity used by the default spool directory. Defaults to the entry-assembly name. The normalized identity must not exceed 256 characters.</param>
        /// <param name="dangerousAcceptAnyServerCertificate">When <see langword="true"/>, disables server-certificate validation for relay HTTP requests. Defaults to <see langword="false"/> and should only be enabled deliberately for trusted private/development infrastructure.</param>
        /// <param name="minimumBatchSize">The preferred minimum count of claimable spool events for normal background delivery; startup backlog, batch wait, and shutdown can bypass it.</param>
        /// <param name="maximumBatchSize">The maximum count in a claimed-spool HTTP batch. Direct emergency batches have a separate limit in the options overload.</param>
        /// <param name="baseInterval">The normal sender polling interval; new events may wake it earlier. Spool maintenance runs at the shorter of this interval and one minute.</param>
        /// <param name="sentRetention">Optional retention for successfully sent events. The default is zero, which deletes acknowledged events immediately.</param>
        /// <param name="unsentRetention">Optional maximum age for unsent events in the shared application spool. This applies spool-wide across processes using the same spool path.</param>
        /// <param name="bearerToken">Optional raw bearer token sent as <c>Authorization: Bearer &lt;token&gt;</c> on this sink/process's HTTP requests for claimed spool rows and direct emergency batches. The token is not persisted with spool rows. Null, empty, or whitespace disables the header.</param>
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
            LogEventLevel restrictedToMinimumLevel = LevelAlias.Minimum,
            string? bearerToken = null)
        {
            var options = new SerilogRelayOptions();
            options.Delivery.MinimumBatchEvents = minimumBatchSize;
            options.Delivery.MaximumBatchEvents = maximumBatchSize;
            options.Delivery.PollInterval = baseInterval ?? TimeSpan.FromSeconds(5);
            options.ApplicationSpool.SentEventRetention = sentRetention ?? TimeSpan.Zero;
            options.ApplicationSpool.UnsentEventMaxAge = unsentRetention;

            return SerilogRelay(
                loggerConfiguration,
                endpoint,
                options,
                spoolDirectory,
                spoolFileName,
                applicationId,
                dangerousAcceptAnyServerCertificate,
                restrictedToMinimumLevel,
                bearerToken);
        }

        /// <summary>
        /// Configures SerilogRelay with grouped reliability options while preserving the simple default overload.
        /// </summary>
        /// <remarks>
        /// Processes sharing one application spool may send each other's pending rows. Endpoint and bearer-token configuration belong to the sending process, not to the row that originally created the event.
        /// </remarks>
        /// <param name="loggerConfiguration">The Serilog sink configuration.</param>
        /// <param name="endpoint">The optional HTTP endpoint that receives batched log events.</param>
        /// <param name="options">Relay behavior options, including an optional caller-owned HTTP client. All nested option groups have complete defaults.</param>
        /// <param name="spoolDirectory">Optional spool directory.</param>
        /// <param name="spoolFileName">Optional spool filename.</param>
        /// <param name="applicationId">Optional logical application identity. The normalized identity must not exceed 256 characters.</param>
        /// <param name="dangerousAcceptAnyServerCertificate">Whether relay HTTP requests should bypass server-certificate validation.</param>
        /// <param name="bearerToken">Optional raw bearer token sent as <c>Authorization: Bearer &lt;token&gt;</c>. Null, empty, or whitespace disables the header.</param>
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
            LogEventLevel restrictedToMinimumLevel = LevelAlias.Minimum,
            string? bearerToken = null)
        {
            ArgumentNullException.ThrowIfNull(options);
            if (options.HttpClient is not null && dangerousAcceptAnyServerCertificate)
                throw new ArgumentException(
                    "Configure certificate validation on the supplied HttpClient instead.",
                    nameof(dangerousAcceptAnyServerCertificate));

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
                dangerousAcceptAnyServerCertificate,
                bearerToken);
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
                Path.IsPathRooted(spoolDirectory)
                    ? string.Empty
                    : Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData,
                        Environment.SpecialFolderOption.DoNotVerify));

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

            if (Path.IsPathRooted(spoolDirectory))
                return Path.Combine(Path.GetFullPath(spoolDirectory), spoolFileName);

            if (string.IsNullOrWhiteSpace(localApplicationData))
                throw new InvalidOperationException("The operating system did not provide a LocalApplicationData directory for the SerilogRelay spool.");

            string defaultDirectory = Path.Combine(
                localApplicationData,
                "Eigenverft",
                "SerilogRelay",
                ResolveApplicationSpoolDirectoryName(ResolveApplicationId(applicationId), OperatingSystem.IsWindows()));

            string resolvedDirectory = string.IsNullOrWhiteSpace(spoolDirectory)
                ? defaultDirectory
                : Path.GetFullPath(Path.Combine(defaultDirectory, spoolDirectory));

            return Path.Combine(resolvedDirectory, spoolFileName);
        }

        private static string ResolveApplicationSpoolDirectoryName(string applicationId, bool isWindows)
        {
            // The logical ID may contain 256 ASCII characters; a filesystem component may not.
            bool reservedWindowsName = isWindows
                && Regex.IsMatch(applicationId, @"^(CON|PRN|AUX|NUL|COM[1-9]|LPT[1-9])(?:\.|$)",
                    RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
            if (applicationId.Length <= 255 && !reservedWindowsName)
                return applicationId;

            // Normalized IDs cannot start with '_', so the hash namespace cannot collide
            // with an ordinary ID's directory. The event's ApplicationId stays unchanged.
            return "_" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(applicationId)))
                .ToLowerInvariant();
        }

        internal static string ResolveApplicationId(string? applicationId)
        {
            string candidate = string.IsNullOrWhiteSpace(applicationId)
                ? GetRuntimeApplicationId()
                : applicationId.Trim();

            string normalized = Regex.Replace(candidate, "[^A-Za-z0-9._-]+", "_").Trim('.', '_');
            if (normalized.Length > MaximumApplicationIdLength)
                throw new ArgumentException("ApplicationId must not exceed 256 characters after normalization.", nameof(applicationId));

            return string.IsNullOrWhiteSpace(normalized) ? "Application" : normalized;
        }

        [ExcludeFromCodeCoverage]
        private static string GetRuntimeApplicationId()
            => Assembly.GetEntryAssembly()?.GetName().Name
                ?? AppDomain.CurrentDomain.FriendlyName;
    }

    /// <summary>
    /// A Serilog relay sink that tries immediate SQLite persistence and sends pending events to an optional HTTP endpoint.
    /// Events that cannot be stored enter a bounded volatile emergency buffer when capacity allows.
    /// </summary>
    public partial class SerilogRelaySink : ILogEventSink, IAsyncDisposable, IDisposable
    {

        private static readonly HttpClientHandler _defaultHttpHandler = new HttpClientHandler
        {
            UseCookies = false,
        };

        private static readonly HttpClientHandler _dangerousHttpHandler = new HttpClientHandler
        {
            UseCookies = false,
            ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator,
        };

        private const int MaxBusyRetries = 5;
        private const int MaximumApplicationVersionLength = 256;
        private const int BusyRetryDelayMs = 100;
        private const int EmergencyRetryDelayMs = 250;
        private static readonly TimeSpan MaximumSenderDelay = TimeSpan.FromMilliseconds(uint.MaxValue - 1L);
        private static readonly TimeSpan ClaimLeaseDuration = TimeSpan.FromSeconds(30);
        private static readonly TimeSpan RecoveryLockTimeout = TimeSpan.FromSeconds(5);
        private const string TableName = "SerilogRelayEvents";

        private const string TableSchema = @"
CREATE TABLE IF NOT EXISTS {0} (
    Id              INTEGER PRIMARY KEY AUTOINCREMENT,
    EventId         TEXT    NOT NULL UNIQUE,
    ApplicationId   TEXT    NOT NULL,
    ApplicationVersion TEXT,
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
    Sent             INTEGER NOT NULL DEFAULT 0,
    ClaimOwnerId     TEXT,
    ClaimBatchId     TEXT,
    ClaimUntilUnixMs INTEGER,
    CreatedAt        TEXT    NOT NULL DEFAULT (datetime('now'))
);";

        private readonly string _connectionString;
        private readonly string? _baseDatabasePath;
        private readonly string? _databasePath;
        private volatile bool _spoolDisabledForLifetime;
        private bool _spoolInitialized; // Accessed only under _databaseGate.
        private readonly SemaphoreSlim _databaseGate = new SemaphoreSlim(1, 1);
        private readonly TimeProvider _timeProvider = TimeProvider.System;
        private readonly string? _endpoint;
        private readonly string _applicationId;
        private readonly string? _applicationVersion;
        private readonly string? _machineId;
        private readonly int _processId;
        private readonly string _claimOwnerId;
        private readonly int _minBatchSize;
        private readonly int _maxBatchSize;
        private readonly int _targetBatchPayloadBytes;
        private readonly int _emergencyMaxBatchSize;
        private readonly int _emergencyTargetBatchPayloadBytes;
        private readonly TimeSpan _baseInterval;
        private readonly TimeSpan _maximumBatchWait;
        private readonly TimeSpan _shutdownTimeout;
        private readonly TimeSpan _requestTimeout;
        private readonly TimeSpan _shutdownRequestTimeout;
        private readonly TimeSpan _shutdownRetryInterval;
        private long _lastHttpAttemptStartedTimestamp;
        private readonly TimeSpan _applicationSpoolSentEventRetention;
        private readonly TimeSpan? _applicationSpoolUnsentEventMaxAge;
        private readonly long _maxApplicationSpoolPhysicalBytes;
        private readonly TimeSpan _minimumCatchUpInterval = TimeSpan.FromSeconds(1);
        private readonly RetryGate _retryGate;

        private readonly CancellationTokenSource _cts;
        private readonly CancellationTokenSource _shutdownSignal;
        private readonly Task _senderTask;
        private readonly Task _statusTask;
        private readonly Channel<EmergencyEntry> _emergencyChannel;
        private readonly Task _emergencyTask;
        private readonly HttpClient _httpClient;
        private readonly bool _ownsHttpClient;
        private readonly AuthenticationHeaderValue? _authorizationHeader;
        private readonly int _emergencyBufferCapacity;
        private readonly long _maxEmergencyBufferedPayloadBytes;

        private long _pendingCount;
        private long _emergencyBufferedCount;
        private long _emergencyBufferedPayloadBytes;
        private long _emergencyDroppedCount;
        private long _applicationSpoolDroppedCount;
        private long _statusEmergencyDroppedCount;
        private int _emergencyOverflowReported;
        private int _spoolUnavailable;
        private int _pendingCountNeedsRefresh;
        private int _applicationSpoolOverflowReported;
        private int _startupBacklogPending;
        private int _startupUnsentCleanupPending;
        private int _endpointFailureActive;
        private long _lastHttpSuccessUnixMs;
        private readonly object _signalLock = new object();
        private bool _hasNewLogs;
        private DateTimeOffset? _pendingSinceUtc;

        private readonly object _disposeLock = new object();
        private Task? _disposeTask;
        private Task? _shutdownCleanupTask;
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
            EmergencyMemoryBufferOptions? emergencyOptions = null,
            string? bearerToken = null)
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
                dangerousAcceptAnyServerCertificate,
                bearerToken)
        {
        }

        internal SerilogRelaySink(
            string connectionString,
            string? endpoint,
            SerilogRelayOptions options,
            string? applicationId = null,
            bool dangerousAcceptAnyServerCertificate = false,
            string? bearerToken = null)
        {
            ArgumentNullException.ThrowIfNull(options);
            ValidateOptions(options);
            if (options.HttpClient is not null && dangerousAcceptAnyServerCertificate)
                throw new ArgumentException(
                    "Configure certificate validation on the supplied HttpClient instead.",
                    nameof(dangerousAcceptAnyServerCertificate));

            _connectionString = connectionString;
            _baseDatabasePath = ResolveDatabasePath(connectionString);
            _databasePath = _baseDatabasePath;
            _endpoint = endpoint;
            _applicationId = LoggerConfigurationSerilogRelayExtensions.ResolveApplicationId(applicationId);
            _applicationVersion = ResolveApplicationVersion(options.ApplicationVersion, Assembly.GetEntryAssembly());
            _machineId = ResolveMachineId();
            _processId = Environment.ProcessId;
            _claimOwnerId = FormattableString.Invariant($"{_processId}:{Guid.NewGuid():N}");
            _minBatchSize = options.Delivery.MinimumBatchEvents;
            _maxBatchSize = options.Delivery.MaximumBatchEvents;
            _targetBatchPayloadBytes = options.Delivery.TargetBatchPayloadBytes;
            _emergencyMaxBatchSize = options.Delivery.EmergencyMaximumBatchEvents;
            _emergencyTargetBatchPayloadBytes = options.Delivery.EmergencyTargetBatchPayloadBytes;
            _baseInterval = options.Delivery.PollInterval;
            _maximumBatchWait = options.Delivery.MaximumBatchWait;
            _shutdownTimeout = options.Delivery.ShutdownTimeout;
            _requestTimeout = options.Delivery.RequestTimeout;
            _shutdownRequestTimeout = options.Delivery.ShutdownRequestTimeout;
            _shutdownRetryInterval = options.Delivery.ShutdownRetryInterval;
            _applicationSpoolSentEventRetention = options.ApplicationSpool.SentEventRetention;
            _applicationSpoolUnsentEventMaxAge = options.ApplicationSpool.UnsentEventMaxAge;
            _maxApplicationSpoolPhysicalBytes = options.ApplicationSpool.MaxPhysicalBytes;
            _retryGate = new RetryGate(options.EndpointRetry);
            _emergencyBufferCapacity = options.EmergencyMemoryBuffer.MaxBufferedEvents;
            _maxEmergencyBufferedPayloadBytes = options.EmergencyMemoryBuffer.MaxBufferedPayloadBytes;
            _statusEventMode = options.StatusEvents.Mode;
            _statusMinimumLevel = options.StatusEvents.MinimumLevel;
            _statusSummaryInterval = options.StatusEvents.SummaryInterval;
            _statusLoggerProvider = options.StatusEvents.LoggerProvider;

            _ownsHttpClient = options.HttpClient is null;
            _httpClient = options.HttpClient ?? new HttpClient(
                GetHttpClientHandler(dangerousAcceptAnyServerCertificate),
                disposeHandler: false);
            if (!string.IsNullOrWhiteSpace(bearerToken))
                _authorizationHeader = new AuthenticationHeaderValue("Bearer", bearerToken);
            _cts = new CancellationTokenSource();
            _shutdownSignal = new CancellationTokenSource();
            _emergencyChannel = Channel.CreateBounded<EmergencyEntry>(
                new BoundedChannelOptions(_emergencyBufferCapacity)
                {
                    SingleReader = false,
                    SingleWriter = false,
                    FullMode = BoundedChannelFullMode.Wait,
                    AllowSynchronousContinuations = false,
                });

            _startupUnsentCleanupPending =
                !string.IsNullOrEmpty(_endpoint) && _applicationSpoolUnsentEventMaxAge.HasValue ? 1 : 0;

            try
            {
                if (_baseDatabasePath is not null)
                {
                    // Select once, before any SQLite access. Each instance keeps its generation.
                    try
                    {
                        _databasePath = SelectStartupSpoolGeneration();
                        var builder = new SqliteConnectionStringBuilder(connectionString)
                        {
                            DataSource = _databasePath,
                        };
                        _connectionString = builder.ToString();
                    }
                    catch (Exception)
                    {
                        // Without safe selection a retry could accidentally open generation zero.
                        _spoolDisabledForLifetime = true;
                        throw;
                    }
                }

                ExecuteDatabaseWithRecovery(() =>
                {
                    CleanupApplicationSpoolRetentionCore(
                        _applicationSpoolSentEventRetention,
                        string.IsNullOrEmpty(_endpoint) ? _applicationSpoolUnsentEventMaxAge : null);
                });

                _pendingCount = ExecuteDatabaseWithRecovery(
                    () => GetClaimablePendingCountCore(DateTimeOffset.UtcNow), updatesSpool: false);
                if (_pendingCount > 0)
                {
                    _startupBacklogPending = string.IsNullOrEmpty(_endpoint) ? 0 : 1;
                    lock (_signalLock)
                    {
                        _pendingSinceUtc = DateTimeOffset.UtcNow - _maximumBatchWait;
                    }
                }
            }
            catch (Exception ex)
            {
                _pendingCount = 0;
                // A delayed schema initialization must still give existing backlog its
                // first delivery opportunity before the deferred unsent-age cleanup.
                _startupBacklogPending = string.IsNullOrEmpty(_endpoint) ? 0 : 1;
                MarkSpoolUnavailable(ex);
            }

            _emergencyTask = Task.Run(EmergencyLoopAsync, _cts.Token);
            _applicationSpoolMaintenanceTask = Task.Run(
                ApplicationSpoolMaintenanceLoopAsync,
                _cts.Token);

            _senderTask = !string.IsNullOrEmpty(_endpoint)
                ? Task.Run(SenderLoopAsync, _cts.Token)
                : Task.CompletedTask;
            _statusTask = _statusEventMode == SerilogRelayStatusEventMode.Off
                ? Task.CompletedTask
                : Task.Run(StatusLoopAsync, _cts.Token);
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
            options.ApplicationSpool.SentEventRetention = sentRetention;
            options.ApplicationSpool.UnsentEventMaxAge = unsentRetention;

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
            if (options.Delivery.TargetBatchPayloadBytes < 1)
                throw new ArgumentOutOfRangeException(nameof(options), "Batch payload target must be at least 1 byte.");
            if (options.Delivery.EmergencyMaximumBatchEvents < 1)
                throw new ArgumentOutOfRangeException(nameof(options), "Emergency maximum batch size must be at least 1.");
            if (options.Delivery.EmergencyTargetBatchPayloadBytes < 1)
                throw new ArgumentOutOfRangeException(nameof(options), "Emergency batch payload target must be at least 1 byte.");
            if (options.Delivery.PollInterval < TimeSpan.FromMilliseconds(1)
                || options.Delivery.PollInterval > MaximumSenderDelay)
                throw new ArgumentOutOfRangeException(nameof(options), "Delivery poll interval must be at least one millisecond and fit a sender timer.");
            if (options.Delivery.MaximumBatchWait <= TimeSpan.Zero)
                throw new ArgumentOutOfRangeException(nameof(options), "Maximum batch wait must be greater than zero.");
            if (options.EndpointRetry.MaximumDelay > MaximumSenderDelay)
                throw new ArgumentOutOfRangeException(nameof(options), "Retry maximum delay must fit a sender timer.");
            if (options.Delivery.RequestTimeout <= TimeSpan.Zero
                || options.Delivery.RequestTimeout >= ClaimLeaseDuration)
                throw new ArgumentOutOfRangeException(nameof(options), "HTTP request timeout must be positive and shorter than the 30-second claim lease.");
            if (!Enum.IsDefined(options.StatusEvents.Mode))
                throw new ArgumentOutOfRangeException(nameof(options), "Unknown relay status event mode.");
            if (options.StatusEvents.Mode == SerilogRelayStatusEventMode.AllSinks
                && options.StatusEvents.LoggerProvider is null)
                throw new ArgumentException("AllSinks status events require a LoggerProvider that returns null until the application logger is ready.", nameof(options));
            if (!Enum.IsDefined(options.StatusEvents.MinimumLevel))
                throw new ArgumentOutOfRangeException(nameof(options), "Unknown relay status minimum level.");
            if (options.StatusEvents.SummaryInterval is TimeSpan summaryInterval
                && (summaryInterval < TimeSpan.FromMinutes(1) || summaryInterval.TotalMilliseconds > uint.MaxValue - 1L))
                throw new ArgumentOutOfRangeException(nameof(options), "Status summary interval must be at least one minute and fit a timer duration.");
            if (options.Delivery.ShutdownTimeout < TimeSpan.Zero
                || options.Delivery.ShutdownTimeout.TotalMilliseconds > uint.MaxValue - 1L)
                throw new ArgumentOutOfRangeException(nameof(options), "Shutdown timeout must be a finite, non-negative timer duration.");
            if (options.Delivery.ShutdownRequestTimeout <= TimeSpan.Zero
                || options.Delivery.ShutdownRequestTimeout.TotalMilliseconds > uint.MaxValue - 1L)
                throw new ArgumentOutOfRangeException(nameof(options), "Shutdown request timeout must be a finite, positive timer duration.");
            if (options.Delivery.ShutdownRetryInterval <= TimeSpan.Zero
                || options.Delivery.ShutdownRetryInterval.TotalMilliseconds > uint.MaxValue - 1L)
                throw new ArgumentOutOfRangeException(nameof(options), "Shutdown retry interval must be a finite, positive timer duration.");
            if (options.ApplicationSpool.SentEventRetention < TimeSpan.Zero)
                throw new ArgumentOutOfRangeException(nameof(options), "Sent retention must not be negative.");
            if (options.ApplicationSpool.UnsentEventMaxAge.HasValue && options.ApplicationSpool.UnsentEventMaxAge.Value < TimeSpan.Zero)
                throw new ArgumentOutOfRangeException(nameof(options), "Unsent retention must not be negative.");
            if (options.ApplicationSpool.MaxPhysicalBytes < 4096)
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

        internal static string? ResolveApplicationVersion(string? configuredVersion, Assembly? entryAssembly)
        {
            if (configuredVersion is not null)
            {
                string version = configuredVersion.Trim();
                if (version.Length == 0 || version.Length > MaximumApplicationVersionLength)
                    throw new ArgumentException("ApplicationVersion must contain 1 to 256 characters.", nameof(configuredVersion));
                return version;
            }

            if (entryAssembly is null)
                return null;

            string? informationalVersion = entryAssembly
                .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
                .InformationalVersion;
            string? versionFromAssembly = !string.IsNullOrWhiteSpace(informationalVersion)
                ? informationalVersion
                : entryAssembly.GetName().Version?.ToString();
            if (versionFromAssembly?.Length > MaximumApplicationVersionLength)
                throw new InvalidOperationException("Entry-assembly ApplicationVersion exceeds 256 characters; configure a shorter ApplicationVersion.");
            return versionFromAssembly;
        }


        /// <summary>
        /// Tries to persist a log event to the local SQLite spool first.
        /// If storage fails or rejects the event, queues it in the bounded volatile emergency buffer when capacity allows.
        /// </summary>
        /// <param name="logEvent">The Serilog event to relay.</param>
        public void Emit(LogEvent logEvent)
        {
            _ = TryEmit(logEvent, reportRejectedEvent: true);
        }

        // An internal status publication keeps its transition queued if neither the spool nor
        // emergency memory accepted it. Public Emit retains its existing best-effort contract.
        private bool TryEmit(LogEvent logEvent, bool reportRejectedEvent)
        {
            ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposeStarted) != 0, this);
            bool statusEvent = IsRelayStatusEvent(logEvent);

            LogEntry entry;
            try
            {
                entry = CreateLogEntry(logEvent, Guid.NewGuid().ToString("D"), statusEvent);
            }
            catch (Exception ex)
            {
                SelfLog.WriteLine("SerilogRelay could not materialize a log event: {0}", ex.Message);
                return false;
            }

            if (_spoolDisabledForLifetime)
                return EnqueueEmergency(entry, exception: null, reportRejectedEvent);

            int attempts = 0;
            while (true)
            {
                bool persisted = false;
                try
                {
                    persisted = TryPersistLogEntryWithRecovery(entry);
                    if (!persisted)
                        return EnqueueEmergency(entry, exception: null, reportRejectedEvent: reportRejectedEvent);

                    OnPersistedToSpool();
                    return true;
                }
                catch (SqliteException ex) when (!persisted && IsBusyError(ex) && attempts++ < MaxBusyRetries)
                {
                    Thread.Sleep(BusyRetryDelayMs * attempts);
                }
                catch (Exception ex)
                {
                    if (persisted)
                    {
                        // The row is already durable. A failure while refreshing sender state
                        // must not cause the same event to be buffered or published again.
                        Volatile.Write(ref _pendingCountNeedsRefresh, 1);
                        SelfLog.WriteLine("SerilogRelay stored an event but could not refresh sender state: {0}", ex.Message);
                        return true;
                    }
                    // Known storage faults were already recorded under _databaseGate.
                    // Re-reporting here could overwrite a newer successful write's recovery.
                    Exception? unobservedFailure = ex is SqliteException or IOException or UnauthorizedAccessException
                        ? null : ex;
                    return EnqueueEmergency(entry, unobservedFailure, reportRejectedEvent);
                }
            }
        }

        private LogEntry CreateLogEntry(LogEvent logEvent, string eventId, bool statusEvent)
        {
            return new LogEntry
            {
                IsRelayStatusEvent = statusEvent,
                EventId = eventId,
                ApplicationId = _applicationId,
                ApplicationVersion = _applicationVersion,
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
            if (_spoolDisabledForLifetime)
                return;
            long pending = Interlocked.Exchange(ref _pendingCountNeedsRefresh, 0) != 0
                ? RefreshClaimablePendingState(DateTimeOffset.UtcNow)
                : Interlocked.Increment(ref _pendingCount);

            lock (_signalLock)
            {
                if (_spoolDisabledForLifetime)
                {
                    Interlocked.Exchange(ref _pendingCount, 0);
                    return;
                }
                if (pending == 1)
                    _pendingSinceUtc = DateTimeOffset.UtcNow;

                _hasNewLogs = true;
            }
        }

        /// <summary>
        /// Synchronously attempts shutdown delivery within the configured time budget.
        /// </summary>
        public void Dispose()
        {
            GetOrCreateDisposeTask().GetAwaiter().GetResult();
            GC.SuppressFinalize(this);
        }

        /// <summary>
        /// Attempts pending emergency and spool delivery within the configured shutdown time budget.
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
                _shutdownSignal.Cancel();
                _disposeTask = Task.Run(DisposeCoreAsync);
                return _disposeTask;
            }
        }

        private async Task DisposeCoreAsync()
        {
            _emergencyChannel.Writer.TryComplete();
            using var deadline = new CancellationTokenSource(_shutdownTimeout);
            if (_shutdownTimeout == TimeSpan.Zero)
                deadline.Cancel();
            CancellationToken token = deadline.Token;

            // SQLite work can finish synchronously after cancellation. Keep its resources alive
            // until cleanup completes, while bounding how long the application waits for Dispose.
            _shutdownCleanupTask = Task.Run(() => DrainAndCleanupAsync(token));
            _ = _shutdownCleanupTask.ContinueWith(
                task => SelfLog.WriteLine("SerilogRelay shutdown cleanup failed: {0}", task.Exception!.GetBaseException().Message),
                CancellationToken.None,
                TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default);

            try
            {
                await _shutdownCleanupTask.WaitAsync(token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
                SelfLog.WriteLine("SerilogRelay shutdown time budget expired; remaining durable events stay in the spool.");
            }
        }

        private async Task DrainAndCleanupAsync(CancellationToken token)
        {
            try
            {
                _cts.CancelAfter(_shutdownTimeout);
                await _senderTask.WaitAsync(token).ConfigureAwait(false);
                await _emergencyTask.WaitAsync(token).ConfigureAwait(false);

                if (!string.IsNullOrEmpty(_endpoint) && !_spoolDisabledForLifetime)
                {
                    while (!token.IsCancellationRequested)
                    {
                        if (_spoolDisabledForLifetime
                            || ExecuteDatabaseWithRecovery(GetPendingCountCore, updatesSpool: false) == 0)
                            break;

                        long roundStarted = Stopwatch.GetTimestamp();
                        bool didWork = await ProcessPendingAsync(ignoreMinBatch: true, token, shutdownDrain: true).ConfigureAwait(false);
                        if (!didWork)
                            await WaitForShutdownRetryAsync(roundStarted, token).ConfigureAwait(false);
                    }
                }
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
            }
            catch (Exception ex)
            {
                SelfLog.WriteLine("SerilogRelay could not complete shutdown delivery: {0}", ex.Message);
            }
            finally
            {
                try
                {
                    _cts.Cancel();
                    try
                    {
                        await Task.WhenAll(_senderTask, _emergencyTask, _applicationSpoolMaintenanceTask, _statusTask).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException) when (_cts.IsCancellationRequested)
                    {
                    }

                    try
                    {
                        if (!_spoolDisabledForLifetime)
                            await ReleaseAllOwnedClaimsAsync(token).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException) when (token.IsCancellationRequested)
                    {
                        // Claims that could not be released become available after lease expiry.
                    }
                    catch (Exception ex)
                    {
                        SelfLog.WriteLine("SerilogRelay could not release owned delivery claims during shutdown: {0}", ex.Message);
                    }

                    long remaining = Interlocked.Read(ref _emergencyBufferedCount);
                    long dropped = Interlocked.Read(ref _emergencyDroppedCount);
                    if (remaining > 0 || dropped > 0)
                        SelfLog.WriteLine("SerilogRelay shutdown with {0} volatile emergency events unresolved and {1} emergency events dropped.", remaining, dropped);
                }
                finally
                {
                    if (_ownsHttpClient)
                        _httpClient.Dispose();
                    _cts.Dispose();
                    _shutdownSignal.Dispose();
                    _databaseGate.Dispose();
                    _statusSignal.Dispose();
                }
            }
        }
    }
}
