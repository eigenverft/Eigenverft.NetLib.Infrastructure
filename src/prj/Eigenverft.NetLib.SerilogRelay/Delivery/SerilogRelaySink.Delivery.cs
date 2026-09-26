using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

using Serilog.Debugging;

namespace Eigenverft.NetLib.SerilogRelay
{
    public partial class SerilogRelaySink
    {
        // Continuously send stored logs to the HTTP endpoint.
        private async Task SenderLoopAsync()
        {
            CancellationToken token = _cts.Token;
            while (!token.IsCancellationRequested)
            {
                try
                {
                    long pending = Interlocked.Read(ref _pendingCount);
                    bool startupDrain = pending > 0 && Volatile.Read(ref _startupBacklogPending) != 0;
                    bool partialBatchDue = pending > 0
                        && (startupDrain || IsMaximumBatchWaitElapsed(DateTimeOffset.UtcNow));
                    bool shouldAttempt = pending >= _minBatchSize || partialBatchDue;
                    bool deliveryOpportunityCompleted = pending == 0;
                    bool didWork = false;

                    if (shouldAttempt)
                    {
                        didWork = await ProcessPendingAsync(
                            ignoreMinBatch: partialBatchDue,
                            token).ConfigureAwait(false);
                        deliveryOpportunityCompleted = true;

                        if (startupDrain)
                            Volatile.Write(ref _startupBacklogPending, 0);
                    }

                    if (deliveryOpportunityCompleted)
                        ApplyDeferredUnsentCleanup();

                    pending = Interlocked.Read(ref _pendingCount);
                    TimeSpan retryDelay = _retryGate.GetDelay(DateTimeOffset.UtcNow);
                    if (retryDelay > TimeSpan.Zero)
                    {
                        await Task.Delay(retryDelay, token).ConfigureAwait(false);
                        continue;
                    }

                    TimeSpan delay = GetSenderDelay(pending, didWork, DateTimeOffset.UtcNow);
                    await WaitForSenderDelayAsync(delay, token).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (token.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex)
                {
                    SelfLog.WriteLine("Error in sender loop: {0}", ex.Message);
                    await Task.Delay(_baseInterval, token).ConfigureAwait(false);
                }
            }
        }

        private bool IsMaximumBatchWaitElapsed(DateTimeOffset now)
        {
            lock (_signalLock)
            {
                if (!_pendingSinceUtc.HasValue)
                {
                    _pendingSinceUtc = now;
                    return false;
                }

                return now - _pendingSinceUtc.Value >= _maximumBatchWait;
            }
        }

        private TimeSpan GetSenderDelay(long pending, bool didWork, DateTimeOffset now)
        {
            if (pending > 0 && pending < _minBatchSize)
            {
                lock (_signalLock)
                {
                    if (_pendingSinceUtc.HasValue)
                    {
                        TimeSpan remaining = _maximumBatchWait - (now - _pendingSinceUtc.Value);
                        if (remaining <= TimeSpan.Zero)
                            return TimeSpan.FromMilliseconds(1);
                        if (remaining < _baseInterval)
                            return remaining;
                    }
                }
            }

            if (didWork && pending > _maxBatchSize * 5)
                return _minimumCatchUpInterval;

            return _baseInterval;
        }

        private async Task WaitForSenderDelayAsync(TimeSpan delay, CancellationToken token)
        {
            Task deadline = Task.Delay(delay, token);
            while (!token.IsCancellationRequested)
            {
                lock (_signalLock)
                {
                    if (_hasNewLogs)
                    {
                        _hasNewLogs = false;
                        return;
                    }
                }

                if (await Task.WhenAny(deadline, Task.Delay(100, token)).ConfigureAwait(false) == deadline)
                    return;
            }
        }

        private void ApplyDeferredUnsentCleanup()
        {
            if (Interlocked.Exchange(ref _startupUnsentCleanupPending, 0) == 0
                || !_unsentRetention.HasValue)
            {
                return;
            }

            ExecuteDatabaseWithRecovery(() =>
                CleanupOldLogsCore(_sentRetention, _unsentRetention));
            Interlocked.Exchange(ref _pendingCount, ExecuteDatabaseWithRecovery(GetPendingCountCore));

            if (Interlocked.Read(ref _pendingCount) == 0)
            {
                lock (_signalLock)
                {
                    _pendingSinceUtc = null;
                }
            }
        }

        /// <summary>
        /// Processes and sends pending logs in batches, with optional bypass of the minimum threshold.
        /// </summary>
        /// <param name="token">Cancellation token.</param>
        /// <param name="ignoreMinBatch">If true, skips the minimum batch-size check.</param>
        /// <returns>True if any logs were successfully sent.</returns>
        private async Task<bool> ProcessPendingAsync(bool ignoreMinBatch, CancellationToken token)
        {
            long pending = Interlocked.Read(ref _pendingCount);
            if (!ignoreMinBatch && pending < _minBatchSize)
                return false;
            if (string.IsNullOrEmpty(_endpoint))
                return false;

            int sentCount = 0, batches = 0;
            while (Interlocked.Read(ref _pendingCount) > 0 && batches++ < 20 && !token.IsCancellationRequested)
            {
                var entries = await LoadUnsentAsync(_maxBatchSize, token).ConfigureAwait(false);
                if (entries.Count == 0)
                    break;

                if (!ignoreMinBatch && entries.Count < _minBatchSize)
                    break;

                if (!await SendBatchAsync(entries, token).ConfigureAwait(false))
                    break;

                await MarkAsSentAsync(entries, token).ConfigureAwait(false);

                long remaining = ExecuteDatabaseWithRecovery(GetPendingCountCore);
                Interlocked.Exchange(ref _pendingCount, remaining);
                if (remaining == 0)
                {
                    lock (_signalLock)
                    {
                        _pendingSinceUtc = null;
                    }
                }

                sentCount += entries.Count;
                await Task.Delay(100, token).ConfigureAwait(false);
            }

            return sentCount > 0;
        }

        // Send one batch of logs over HTTP using AOT-compatible source-gen context.
        private async Task<bool> SendBatchAsync(List<LogEntry> entries, CancellationToken token)
        {
            if (!_retryGate.TryAcquire(DateTimeOffset.UtcNow))
                return false;

            var payload = new LogBatchPayload
            {
                ProtocolVersion = 1,
                BatchId = Guid.NewGuid().ToString(),
                Timestamp = DateTime.UtcNow.ToString("o"),
                Count = entries.Count,
                Logs = entries
            };
            var json = JsonSerializer.Serialize(payload, LogBatchJsonContext.Default.LogBatchPayload);
            using var content = new StringContent(json, Encoding.UTF8, "application/json");

            try
            {
                using var resp = await _httpClient.PostAsync(_endpoint, content, token).ConfigureAwait(false);
                if (resp.IsSuccessStatusCode)
                {
                    _retryGate.RecordSuccess();
                    return true;
                }

                DateTimeOffset? retryAfter = null;
                if ((int)resp.StatusCode == 429 && resp.Headers.RetryAfter is not null)
                {
                    if (resp.Headers.RetryAfter.Delta.HasValue)
                        retryAfter = DateTimeOffset.UtcNow + resp.Headers.RetryAfter.Delta.Value;
                    else if (resp.Headers.RetryAfter.Date.HasValue)
                        retryAfter = resp.Headers.RetryAfter.Date.Value;
                }

                _retryGate.RecordFailure(DateTimeOffset.UtcNow, retryAfter);
                SelfLog.WriteLine("HTTP relay returned status code {0}.", (int)resp.StatusCode);
                return false;
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
                _retryGate.CancelAttempt();
                throw;
            }
            catch (Exception ex)
            {
                _retryGate.RecordFailure(DateTimeOffset.UtcNow, retryAfter: null);
                SelfLog.WriteLine("HTTP relay request failed: {0}", ex.Message);
                return false;
            }
        }
    }
}
