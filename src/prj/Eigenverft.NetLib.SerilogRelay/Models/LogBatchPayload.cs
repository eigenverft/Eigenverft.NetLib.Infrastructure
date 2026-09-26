using System.Collections.Generic;

namespace Eigenverft.NetLib.SerilogRelay
{
    /// <summary>
    /// Payload wrapper for JSON serialization of relay batches.
    /// </summary>
    internal sealed class LogBatchPayload
    {
        public int ProtocolVersion { get; set; }
        public List<LogEntry> Logs { get; set; } = new List<LogEntry>();
        public string BatchId { get; set; } = string.Empty;
        public string Timestamp { get; set; } = string.Empty;
        public int Count { get; set; }
    }
}
