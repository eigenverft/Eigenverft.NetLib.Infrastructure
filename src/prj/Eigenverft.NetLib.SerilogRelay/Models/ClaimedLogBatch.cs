using System.Collections.Generic;

namespace Eigenverft.NetLib.SerilogRelay
{
    internal sealed class ClaimedLogBatch
    {
        internal ClaimedLogBatch(string claimBatchId, List<LogEntry> entries, bool payloadTargetReached = false, bool claimCapacityLimited = false)
        {
            ClaimBatchId = claimBatchId;
            Entries = entries;
            PayloadTargetReached = payloadTargetReached;
            ClaimCapacityLimited = claimCapacityLimited;
        }

        internal string ClaimBatchId { get; }

        internal List<LogEntry> Entries { get; }

        internal bool PayloadTargetReached { get; }

        internal bool ClaimCapacityLimited { get; }
    }
}
