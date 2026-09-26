using System.Collections.Generic;

namespace Eigenverft.NetLib.SerilogRelay
{
    internal sealed class ClaimedLogBatch
    {
        internal ClaimedLogBatch(string claimBatchId, List<LogEntry> entries)
        {
            ClaimBatchId = claimBatchId;
            Entries = entries;
        }

        internal string ClaimBatchId { get; }

        internal List<LogEntry> Entries { get; }
    }
}
