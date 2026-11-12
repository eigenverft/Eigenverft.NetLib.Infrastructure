using System;
using System.Threading;
using System.Threading.Tasks;

namespace Eigenverft.NetLib.SerilogRelay
{
    public partial class SerilogRelaySink
    {
        private Task DelayAsync(TimeSpan delay, CancellationToken token)
        {
#if NETSTANDARD2_1
            return _timeProvider.Delay(delay, token);
#else
            return Task.Delay(delay, _timeProvider, token);
#endif
        }

        private static Task WaitForCompletionAsync(Task task, CancellationToken token)
        {
#if NETSTANDARD2_1
            return task.WaitAsync(Timeout.InfiniteTimeSpan, TimeProvider.System, token);
#else
            return task.WaitAsync(token);
#endif
        }

#if NETSTANDARD2_1
        private static void CancelRequestTimeout(object? state)
        {
            try
            {
                ((CancellationTokenSource)state!).Cancel();
            }
            catch (ObjectDisposedException)
            {
                // A timer callback can already be running when its request scope disposes it.
            }
        }
#endif
    }
}
