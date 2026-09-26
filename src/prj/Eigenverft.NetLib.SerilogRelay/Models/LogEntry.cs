namespace Eigenverft.NetLib.SerilogRelay
{
    /// <summary>
    /// Represents one materialized relay log event.
    /// </summary>
    internal sealed class LogEntry
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
