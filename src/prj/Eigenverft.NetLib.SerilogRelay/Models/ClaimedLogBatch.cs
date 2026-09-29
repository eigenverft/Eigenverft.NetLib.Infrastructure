using System.Collections.Generic;

namespace Eigenverft.NetLib.SerilogRelay
{
    internal sealed class ClaimedLogBatch
    {
        internal ClaimedLogBatch(string claimBatchId, List<LogEntry> entries, bool payloadTargetReached = false)
        {
            ClaimBatchId = claimBatchId;
            Entries = entries;
            PayloadTargetReached = payloadTargetReached;
        }

        internal string ClaimBatchId { get; }

        internal List<LogEntry> Entries { get; }

        internal bool PayloadTargetReached { get; }
    }
}
