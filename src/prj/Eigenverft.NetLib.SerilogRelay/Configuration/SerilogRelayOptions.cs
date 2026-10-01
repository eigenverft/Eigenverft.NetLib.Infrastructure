using System;

namespace Eigenverft.NetLib.SerilogRelay
{
    /// <summary>
    /// Configures SerilogRelay application-spool, delivery, endpoint retry, and emergency-memory behavior.
    /// </summary>
    public sealed class SerilogRelayOptions
    {
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
        /// Gets or sets the exponential backoff multiplier.
        /// </summary>
        public double Multiplier { get; set; } = 2d;

        /// <summary>
        /// Gets or sets the maximum retry delay.
        /// </summary>
        public TimeSpan MaximumDelay { get; set; } = TimeSpan.FromMinutes(5);

        /// <summary>
        /// Gets or sets the random jitter ratio applied to exponential backoff delays.
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
