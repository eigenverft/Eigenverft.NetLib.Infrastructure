using System;
using System.Threading;
using System.Threading.Tasks;

using Serilog.Debugging;

namespace Eigenverft.NetLib.SerilogRelay
{
    public partial class SerilogRelaySink
    {
        private static readonly TimeSpan MaximumApplicationSpoolMaintenanceInterval =
            TimeSpan.FromMinutes(1);

        private readonly Task _applicationSpoolMaintenanceTask;

        private async Task ApplicationSpoolMaintenanceLoopAsync()
        {
            CancellationToken token = _cts.Token;
            TimeSpan interval = TimeSpan.FromTicks(
                Math.Min(
                    _baseInterval.Ticks,
                    MaximumApplicationSpoolMaintenanceInterval.Ticks));

            try
            {
                while (!token.IsCancellationRequested)
                {
                    await Task.Delay(interval, token).ConfigureAwait(false);
                    if (_spoolDisabledForLifetime)
                        return;

                    try
                    {
                        TimeSpan? unsentRetention =
                            Volatile.Read(ref _startupUnsentCleanupPending) == 0
                                ? _applicationSpoolUnsentEventMaxAge
                                : null;

                        ExecuteDatabaseWithRecovery(() =>
                            CleanupApplicationSpoolRetentionCore(
                                _applicationSpoolSentEventRetention,
                                unsentRetention));

                        RefreshClaimablePendingState(DateTimeOffset.UtcNow);
                    }
                    catch (Exception ex)
                    {
                        SelfLog.WriteLine(
                            "SerilogRelay application-spool maintenance failed: {0}",
                            ex.Message);
                    }
                }
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
            }
        }
    }
}
