using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
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
            while (!token.IsCancellationRequested && Volatile.Read(ref _disposeStarted) == 0)
            {
                try
                {
                    DateTimeOffset now = DateTimeOffset.UtcNow;
                    TimeSpan existingRetryDelay = _retryGate.GetDelay(now);
                    if (existingRetryDelay > TimeSpan.Zero)
                    {
                        await WaitForSenderDelayAsync(existingRetryDelay, token).ConfigureAwait(false);
                        continue;
                    }

                    long pending = RefreshClaimablePendingState(now);
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

                    if (Volatile.Read(ref _disposeStarted) != 0)
                        break;
                    if (deliveryOpportunityCompleted)
                        ApplyDeferredUnsentCleanup();

                    pending = Interlocked.Read(ref _pendingCount);
                    TimeSpan retryDelay = _retryGate.GetDelay(DateTimeOffset.UtcNow);
                    if (retryDelay > TimeSpan.Zero)
                    {
                        await WaitForSenderDelayAsync(retryDelay, token).ConfigureAwait(false);
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
                    await WaitForSenderDelayAsync(_baseInterval, token).ConfigureAwait(false);
                }
            }
        }

        private long RefreshClaimablePendingState(DateTimeOffset now)
        {
            long pending = ExecuteDatabaseWithRecovery(() => GetClaimablePendingCountCore(now));
            long previous = Interlocked.Exchange(ref _pendingCount, pending);

            lock (_signalLock)
            {
                if (pending == 0)
                {
                    _pendingSinceUtc = null;
                }
                else if (previous == 0 || !_pendingSinceUtc.HasValue)
                {
                    _pendingSinceUtc = now;
                }
            }

            return pending;
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
            while (!token.IsCancellationRequested && Volatile.Read(ref _disposeStarted) == 0)
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
                || !_applicationSpoolUnsentEventMaxAge.HasValue)
            {
                return;
            }

            ExecuteDatabaseWithRecovery(() =>
                CleanupApplicationSpoolRetentionCore(
                    _applicationSpoolSentEventRetention,
                    _applicationSpoolUnsentEventMaxAge));
            RefreshClaimablePendingState(DateTimeOffset.UtcNow);
        }

        /// <summary>
        /// Processes and sends claimable logs in batches, with optional bypass of the minimum threshold.
        /// </summary>
        /// <param name="token">Cancellation token.</param>
        /// <param name="shutdownDrain">If true, drains after the normal sender has yielded to shutdown.</param>
        /// <param name="ignoreMinBatch">If true, skips the minimum batch-size check.</param>
        /// <returns>True if logs were sent and the round ended without a failed HTTP attempt.</returns>
        private async Task<bool> ProcessPendingAsync(bool ignoreMinBatch, CancellationToken token, bool shutdownDrain = false)
        {
            long pending = RefreshClaimablePendingState(DateTimeOffset.UtcNow);
            if (!ignoreMinBatch && pending < _minBatchSize)
                return false;
            if (string.IsNullOrEmpty(_endpoint))
                return false;

            int sentCount = 0, batches = 0;
            while (pending > 0 && batches++ < 20 && !token.IsCancellationRequested)
            {
                if (!shutdownDrain && Volatile.Read(ref _disposeStarted) != 0)
                    break;

                ClaimedLogBatch claimed = await ClaimPendingAsync(
                    _maxBatchSize,
                    DateTimeOffset.UtcNow,
                    token).ConfigureAwait(false);
                List<LogEntry> entries = claimed.Entries;

                if (entries.Count == 0)
                {
                    pending = RefreshClaimablePendingState(DateTimeOffset.UtcNow);
                    break;
                }

                // Byte/capacity-limited batches are ready below the preferred event count.
                if (!ignoreMinBatch && !claimed.PayloadTargetReached && !claimed.ClaimCapacityLimited && entries.Count < _minBatchSize)
                {
                    await ReleaseClaimAsync(claimed, token).ConfigureAwait(false);
                    pending = RefreshClaimablePendingState(DateTimeOffset.UtcNow);
                    break;
                }

                if (!await SendBatchAsync(entries, token).ConfigureAwait(false))
                {
                    await ReleaseClaimAsync(claimed, token).ConfigureAwait(false);
                    RefreshClaimablePendingState(DateTimeOffset.UtcNow);
                    // Preserve failure pacing even if earlier batches in this round succeeded.
                    return false;
                }

                await AcknowledgeClaimAsync(claimed, token).ConfigureAwait(false);

                sentCount += entries.Count;
                // Finish the current request and acknowledgment, then hand over to RAM delivery.
                if (!shutdownDrain && Volatile.Read(ref _disposeStarted) != 0)
                    break;
                pending = RefreshClaimablePendingState(DateTimeOffset.UtcNow);
                if (!shutdownDrain)
                    await Task.Delay(100, token).ConfigureAwait(false);
            }

            return sentCount > 0;
        }

        private async Task WaitForShutdownRetryAsync(long roundStarted, CancellationToken token)
        {
            // Count time spent in the failed request toward the retry interval.
            long attemptStarted = Math.Max(roundStarted, Interlocked.Read(ref _lastHttpAttemptStartedTimestamp));
            TimeSpan delay = _shutdownRetryInterval - Stopwatch.GetElapsedTime(attemptStarted);
            if (delay > TimeSpan.Zero)
                await Task.Delay(delay, token).ConfigureAwait(false);
        }

        private static LogBatchPayload CreateBatchPayload(List<LogEntry> entries)
        {
            return new LogBatchPayload
            {
                ProtocolVersion = 1,
                BatchId = Guid.NewGuid().ToString(),
                Timestamp = DateTime.UtcNow.ToString("o"),
                Count = entries.Count,
                Logs = entries
            };
        }

        private static long GetEmptyBatchPayloadBytes()
            => JsonSerializer.SerializeToUtf8Bytes(
                CreateBatchPayload(new List<LogEntry>()),
                LogBatchJsonContext.Default.LogBatchPayload).Length;

        private static long GetBatchPayloadBytesWithNextEntry(long currentBytes, int currentCount, LogEntry entry)
        {
            int entryBytes = JsonSerializer.SerializeToUtf8Bytes(entry, LogBatchJsonContext.Default.LogEntry).Length;
            int countDigitsAdded = (currentCount + 1).ToString(CultureInfo.InvariantCulture).Length
                - currentCount.ToString(CultureInfo.InvariantCulture).Length;
            return currentBytes + entryBytes + countDigitsAdded + (currentCount == 0 ? 0 : 1);
        }

        // Send one batch of logs over HTTP using AOT-compatible source-gen context.
        private async Task<bool> SendBatchAsync(List<LogEntry> entries, CancellationToken token)
        {
            if (!_retryGate.TryAcquire(DateTimeOffset.UtcNow, ignoreBackoff: Volatile.Read(ref _disposeStarted) != 0))
                return false;

            LogBatchPayload payload = CreateBatchPayload(entries);
            var json = JsonSerializer.Serialize(payload, LogBatchJsonContext.Default.LogBatchPayload);
            using var content = new StringContent(json, Encoding.UTF8, "application/json");

            try
            {
                using var requestCancellation = CancellationTokenSource.CreateLinkedTokenSource(token);
                // Also shorten an HTTP attempt that was already running when shutdown began.
                using var shutdownRegistration = _shutdownSignal.Token.Register(() => requestCancellation.CancelAfter(_shutdownRequestTimeout));
                Interlocked.Exchange(ref _lastHttpAttemptStartedTimestamp, Stopwatch.GetTimestamp());
                using var resp = await _httpClient.PostAsync(_endpoint, content, requestCancellation.Token).ConfigureAwait(false);
                if (resp.IsSuccessStatusCode)
                {
                    _retryGate.RecordSuccess();
                    return true;
                }

                DateTimeOffset? retryAfter = null;
                if (resp.Headers.RetryAfter is not null)
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
