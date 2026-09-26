using System;

namespace Eigenverft.NetLib.SerilogRelay
{
    /// <summary>
    /// Configures SerilogRelay application-spool, delivery, endpoint retry, and emergency-memory behavior.
    /// </summary>
    public sealed class SerilogRelayOptions
    {
        /// <summary>
        /// Gets options that apply to the shared durable spool selected by ApplicationId/spool path.
        /// Multiple processes of the same application may use this spool.
        /// </summary>
        public ApplicationSpoolOptions ApplicationSpool { get; } = new ApplicationSpoolOptions();

        /// <summary>
        /// Gets normal background-delivery options for this sink/process.
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
    /// Configures behavior of the durable application spool shared by processes that resolve to the same spool path.
    /// </summary>
    public sealed class ApplicationSpoolOptions
    {
        /// <summary>
        /// Gets or sets how long successfully delivered events remain in the shared application spool.
        /// Cleanup is spool-wide, not limited to events created by the current process.
        /// </summary>
        public TimeSpan SentEventRetention { get; set; } = TimeSpan.FromDays(1);

        /// <summary>
        /// Gets or sets the maximum physical size of the shared application spool in bytes.
        /// This is currently a spool-wide physical ceiling shared by all processes using the same spool path.
        /// </summary>
        public long MaxPhysicalBytes { get; set; } = 64L * 1024L * 1024L;

        /// <summary>
        /// Gets or sets the maximum age of unsent events in the shared application spool.
        /// A null value preserves unsent events regardless of age. Cleanup is spool-wide.
        /// </summary>
        public TimeSpan? UnsentEventMaxAge { get; set; }
    }

    /// <summary>
    /// Configures normal background delivery for this sink/process.
    /// </summary>
    public sealed class DeliveryOptions
    {
        /// <summary>
        /// Gets or sets the preferred minimum event count for normal batches.
        /// </summary>
        public int MinimumBatchEvents { get; set; } = 20;

        /// <summary>
        /// Gets or sets the maximum event count in one HTTP batch.
        /// </summary>
        public int MaximumBatchEvents { get; set; } = 100;

        /// <summary>
        /// Gets or sets the normal sender polling interval.
        /// </summary>
        public TimeSpan PollInterval { get; set; } = TimeSpan.FromSeconds(5);

        /// <summary>
        /// Gets or sets the maximum time a partial batch may wait before delivery is attempted.
        /// </summary>
        public TimeSpan MaximumBatchWait { get; set; } = TimeSpan.FromSeconds(5);
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
        /// Gets or sets the maximum number of events buffered in memory.
        /// </summary>
        public int MaxBufferedEvents { get; set; } = 16384;

        /// <summary>
        /// Gets or sets the maximum serialized event payload bytes buffered in memory.
        /// </summary>
        public long MaxBufferedPayloadBytes { get; set; } = 64L * 1024L * 1024L;
    }
}
