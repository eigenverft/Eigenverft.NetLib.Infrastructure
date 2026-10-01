using System;
using System.Net.Http;
using Serilog;
using Serilog.Events;

namespace Eigenverft.NetLib.SerilogRelay
{
    /// <summary>
    /// Configures SerilogRelay event origin, HTTP transport, application-spool, delivery, endpoint retry, and emergency-memory behavior.
    /// </summary>
    public sealed class SerilogRelayOptions
    {
        /// <summary>
        /// Gets or sets the version of the application that creates new events. Null resolves the
        /// entry assembly's informational version once when the sink is created, falling back to
        /// its assembly version. The resolved value must not exceed 256 characters; it is never
        /// truncated. Existing spool rows keep their original version.
        /// </summary>
        public string? ApplicationVersion { get; set; }

        /// <summary>
        /// Gets or sets an optional HTTP client for this sink's relay requests. The caller owns
        /// the client. Prefer an application-lifetime client because bounded sink disposal can
        /// return while canceled background cleanup finishes. The sink does not change the
        /// client's default headers or timeout and does not dispose it. Leave null to use the
        /// built-in client. Configure proxy, client certificates, or custom authentication on
        /// the supplied client and its handlers.
        /// </summary>
        public HttpClient? HttpClient { get; set; }

        /// <summary>
        /// Gets options whose values this sink/process applies to the shared durable spool selected by ApplicationId/spool path.
        /// Multiple processes may use the same spool; their ApplicationSpool values are not negotiated or merged.
        /// </summary>
        public ApplicationSpoolOptions ApplicationSpool { get; } = new ApplicationSpoolOptions();

        /// <summary>
        /// Gets background, direct emergency, and shutdown delivery options for this sink/process.
        /// </summary>
        public DeliveryOptions Delivery { get; } = new DeliveryOptions();

        /// <summary>
        /// Gets remote endpoint retry options for this sink/process.
        /// </summary>
        public EndpointRetryOptions EndpointRetry { get; } = new EndpointRetryOptions();

        /// <summary>
        /// Gets volatile emergency memory-buffer options for this sink/process.
        /// </summary>
        public EmergencyMemoryBufferOptions EmergencyMemoryBuffer { get; } = new EmergencyMemoryBufferOptions();

        /// <summary>
        /// Gets optional operational status events emitted by this sink/process.
        /// </summary>
        public SerilogRelayStatusEventOptions StatusEvents { get; } = new SerilogRelayStatusEventOptions();
    }

    /// <summary>
    /// Selects where operational events produced by SerilogRelay are written.
    /// </summary>
    public enum SerilogRelayStatusEventMode
    {
        /// <summary>No operational events are emitted.</summary>
        Off,
        /// <summary>Operational events are stored and delivered only by this relay sink.</summary>
        RelayOnly,
        /// <summary>Operational events pass through the configured application logger, including this relay sink.</summary>
        AllSinks,
    }

    /// <summary>
    /// Configures low-volume operational events about the relay itself.
    /// </summary>
    public sealed class SerilogRelayStatusEventOptions
    {
        /// <summary>
        /// Gets or sets the destination. The default is Off; Serilog SelfLog remains available independently.
        /// </summary>
        public SerilogRelayStatusEventMode Mode { get; set; } = SerilogRelayStatusEventMode.Off;

        /// <summary>
        /// Gets or sets the minimum level of relay status events. Defaults to Warning.
        /// In AllSinks mode, the application's own Serilog filters also apply.
        /// </summary>
        public LogEventLevel MinimumLevel { get; set; } = LogEventLevel.Warning;

        /// <summary>
        /// Gets or sets an optional interval for Debug-level status summaries. Null disables summaries.
        /// The interval must be at least one minute to keep diagnostic volume bounded.
        /// State changes and loss reports are independent of this interval.
        /// </summary>
        public TimeSpan? SummaryInterval { get; set; }

        /// <summary>
        /// Gets or sets the accessor for the completed application logger in AllSinks mode.
        /// Required for AllSinks. Return null until CreateLogger completes so startup status
        /// events are retained for a later publication attempt. The accessor may return a local
        /// or global application logger.
        /// </summary>
        public Func<ILogger?>? LoggerProvider { get; set; }
    }

    /// <summary>
    /// Configures the spool-wide policies this sink/process applies to the durable application spool shared by processes that resolve to the same spool path.
    /// </summary>
    public sealed class ApplicationSpoolOptions
    {
        /// <summary>
        /// Gets or sets optional retention for successfully delivered events. Zero deletes acknowledged events immediately.
        /// This process applies the value during spool-wide maintenance; cleanup is not limited to rows created by this process.
        /// </summary>
        public TimeSpan SentEventRetention { get; set; } = TimeSpan.Zero;

        /// <summary>
        /// Gets or sets the SQLite page budget, in bytes, for the shared application spool.
        /// This process applies the value to the shared database, not to individual processes' rows. An existing larger database is not shrunk, and SQLite auxiliary files are outside this page budget. Other processes do not negotiate this value.
        /// </summary>
        public long MaxPhysicalBytes { get; set; } = 64L * 1024L * 1024L;

        /// <summary>
        /// Gets or sets the maximum age at which unclaimed unsent events become eligible for spool-wide cleanup.
        /// A null value disables age-based unsent cleanup. Maintenance is periodic, may affect rows created by any process, and defers rows while they have an active delivery claim. Capacity reclamation remains independent of this value.
        /// </summary>
        public TimeSpan? UnsentEventMaxAge { get; set; }
    }

    /// <summary>
    /// Configures background, direct emergency, and shutdown delivery for this sink/process.
    /// </summary>
    public sealed class DeliveryOptions
    {
        /// <summary>
        /// Gets or sets the maximum time for one HTTP request. Defaults to two seconds and must
        /// be shorter than the 30-second claim lease. The client's own timeout, shutdown request
        /// timeout, remaining shutdown budget, or remaining renewed spool lease may end a request earlier.
        /// </summary>
        public TimeSpan RequestTimeout { get; set; } = TimeSpan.FromSeconds(2);

        /// <summary>
        /// Gets or sets the preferred minimum count of claimable spool events before normal background delivery.
        /// Startup backlog, elapsed MaximumBatchWait, byte- or capacity-limited batches, and shutdown may send fewer events. Direct emergency delivery has no minimum.
        /// </summary>
        public int MinimumBatchEvents { get; set; } = 20;

        /// <summary>
        /// Gets or sets the maximum number of claimed spool events in one HTTP batch, including during shutdown.
        /// Direct emergency batches use EmergencyMaximumBatchEvents instead.
        /// </summary>
        public int MaximumBatchEvents { get; set; } = 100;

        /// <summary>
        /// Gets or sets the target UTF-8 JSON size of a spool HTTP batch, including batch metadata.
        /// This also applies during shutdown; direct emergency batches use EmergencyTargetBatchPayloadBytes. An event larger than the target is sent alone, without truncation or size-based rejection.
        /// </summary>
        public int TargetBatchPayloadBytes { get; set; } = 4 * 1024 * 1024;

        /// <summary>
        /// Gets or sets the maximum number of volatile emergency events in one direct HTTP batch, including during shutdown.
        /// Emergency delivery has no minimum batch count and does not wait to fill a batch.
        /// </summary>
        public int EmergencyMaximumBatchEvents { get; set; } = 256;

        /// <summary>
        /// Gets or sets the target UTF-8 JSON size of a direct emergency HTTP batch.
        /// An individual event larger than this target is sent alone without truncation.
        /// </summary>
        public int EmergencyTargetBatchPayloadBytes { get; set; } = 4 * 1024 * 1024;

        /// <summary>
        /// Gets or sets the normal sender polling interval; new events may wake it earlier.
        /// Must be at least one millisecond and no longer than the maximum supported timer delay
        /// (about 49.7 days).
        /// Spool maintenance runs at the shorter of this interval and one minute.
        /// </summary>
        public TimeSpan PollInterval { get; set; } = TimeSpan.FromSeconds(5);

        /// <summary>
        /// Gets or sets how long the normal sender waits before attempting a spool batch below MinimumBatchEvents.
        /// Startup backlog and shutdown bypass this wait; direct emergency delivery does not use it.
        /// </summary>
        public TimeSpan MaximumBatchWait { get; set; } = TimeSpan.FromSeconds(5);

        /// <summary>
        /// Gets or sets how long Dispose and DisposeAsync wait for shutdown delivery and cleanup.
        /// They return earlier when work completes. On timeout, delivery is canceled; cleanup that cannot stop immediately may finish after the call returns. Zero skips shutdown delivery.
        /// </summary>
        public TimeSpan ShutdownTimeout { get; set; } = TimeSpan.FromSeconds(3);

        /// <summary>
        /// Gets or sets the additional HTTP request time limit during shutdown.
        /// The normal HTTP timeout and remaining shutdown budget may end a request earlier.
        /// </summary>
        public TimeSpan ShutdownRequestTimeout { get; set; } = TimeSpan.FromSeconds(1);

        /// <summary>
        /// Gets or sets the minimum interval before retrying failed shutdown delivery.
        /// Time spent in the failed request counts toward this interval; successful batches have no delay.
        /// </summary>
        public TimeSpan ShutdownRetryInterval { get; set; } = TimeSpan.FromSeconds(1);
    }

    /// <summary>
    /// Configures retry timing for the remote relay endpoint for this sink/process.
    /// </summary>
    public sealed class EndpointRetryOptions
    {
        /// <summary>
        /// Gets or sets the first delay after an endpoint failure.
        /// </summary>
        public TimeSpan InitialDelay { get; set; } = TimeSpan.FromSeconds(5);

        /// <summary>
        /// Gets or sets the exponential backoff multiplier. Must be finite and at least 1.
        /// </summary>
        public double Multiplier { get; set; } = 2d;

        /// <summary>
        /// Gets or sets the maximum retry delay. It must fit the sender timer (about 49.7 days).
        /// A later HTTP Retry-After time is rechecked in bounded waits.
        /// </summary>
        public TimeSpan MaximumDelay { get; set; } = TimeSpan.FromMinutes(5);

        /// <summary>
        /// Gets or sets the random jitter ratio applied to exponential backoff delays. Must be finite and between 0 and 1.
        /// </summary>
        public double JitterRatio { get; set; } = 0.2d;

        /// <summary>
        /// Gets or sets whether valid HTTP Retry-After values are honored.
        /// </summary>
        public bool RespectRetryAfter { get; set; } = true;
    }

    /// <summary>
    /// Configures the bounded volatile emergency memory buffer for this sink/process.
    /// </summary>
    public sealed class EmergencyMemoryBufferOptions
    {
        /// <summary>
        /// Gets or sets the maximum number of queued and in-flight emergency events. The oldest queued events are evicted when necessary.
        /// </summary>
        public int MaxBufferedEvents { get; set; } = 16384;

        /// <summary>
        /// Gets or sets the maximum estimated UTF-8 event-field bytes counted for queued and in-flight emergency events.
        /// The estimate includes fixed field overhead; it is neither the serialized JSON size nor the process's memory usage.
        /// </summary>
        public long MaxBufferedPayloadBytes { get; set; } = 64L * 1024L * 1024L;
    }
}
