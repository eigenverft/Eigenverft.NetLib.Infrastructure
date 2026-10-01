using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

using Serilog.Debugging;

namespace Eigenverft.NetLib.SerilogRelay
{
    public partial class SerilogRelaySink
    {
        private readonly object _emergencyBufferLock = new object();
        private readonly List<EmergencyEntry> _emergencyInFlight = new List<EmergencyEntry>();
        private readonly LinkedList<EmergencyEntry> _emergencyRetryEntries = new LinkedList<EmergencyEntry>();
        private long _emergencyInFlightPayloadBytes;

        private void EnqueueEmergency(LogEntry entry, Exception? exception)
        {
            if (exception is not null)
                MarkSpoolUnavailable(exception);

            long payloadBytes = GetEmergencyPayloadBytes(entry);
            lock (_emergencyBufferLock)
            {
                if (payloadBytes > _maxEmergencyBufferedPayloadBytes - _emergencyInFlightPayloadBytes)
                {
                    RecordEmergencyDrop();
                    return;
                }

                while (_emergencyBufferedCount >= _emergencyBufferCapacity
                    || payloadBytes > _maxEmergencyBufferedPayloadBytes - _emergencyBufferedPayloadBytes)
                {
                    EmergencyEntry? oldest;
                    if (_emergencyRetryEntries.First is LinkedListNode<EmergencyEntry> retryNode)
                    {
                        oldest = retryNode.Value;
                        _emergencyRetryEntries.RemoveFirst();
                    }
                    else if (!_emergencyChannel.Reader.TryRead(out oldest))
                    {
                        RecordEmergencyDrop();
                        return;
                    }

                    CompleteEmergencyEntry(oldest);
                    RecordEmergencyDrop();
                }

                var bufferedEntry = new EmergencyEntry(entry, payloadBytes);
                Interlocked.Increment(ref _emergencyBufferedCount);
                Interlocked.Add(ref _emergencyBufferedPayloadBytes, payloadBytes);
                if (_emergencyChannel.Writer.TryWrite(bufferedEntry))
                    return;

                CompleteEmergencyEntry(bufferedEntry);
                RecordEmergencyDrop();
            }
        }

        private void RecordEmergencyDrop()
        {
            long dropped = Interlocked.Increment(ref _emergencyDroppedCount);
            if (Interlocked.Exchange(ref _emergencyOverflowReported, 1) != 0)
                return;

            SelfLog.WriteLine(
                "SerilogRelay emergency buffer limit reached ({0} events / {1} payload bytes). Oldest waiting events are evicted when possible; total dropped: {2}.",
                _emergencyBufferCapacity,
                _maxEmergencyBufferedPayloadBytes,
                dropped);
        }

        private static long GetEmergencyPayloadBytes(LogEntry entry)
        {
            return sizeof(long)
                + sizeof(int)
                + GetUtf8ByteCount(entry.EventId)
                + GetUtf8ByteCount(entry.ApplicationId)
                + GetUtf8ByteCount(entry.MachineId)
                + GetUtf8ByteCount(entry.Timestamp)
                + GetUtf8ByteCount(entry.Level)
                + GetUtf8ByteCount(entry.RenderMessage)
                + GetUtf8ByteCount(entry.MessageTemplate)
                + GetUtf8ByteCount(entry.TraceId)
                + GetUtf8ByteCount(entry.SpanId)
                + GetUtf8ByteCount(entry.Exception)
                + GetUtf8ByteCount(entry.Properties);
        }

        private static int GetUtf8ByteCount(string? value)
            => value is null ? 0 : Encoding.UTF8.GetByteCount(value);

        private async Task EmergencyLoopAsync()
        {
            CancellationToken token = _cts.Token;

            try
            {
                while (true)
                {
                    List<EmergencyEntry> batch = TakeEmergencyBatch();
                    if (batch.Count == 0)
                    {
                        if (!await _emergencyChannel.Reader.WaitToReadAsync(token).ConfigureAwait(false))
                            break;
                        continue;
                    }

                    bool completed = false;
                    long roundStarted = Stopwatch.GetTimestamp();
                    try
                    {
                        completed = await TryProcessEmergencyBatchAsync(batch, token).ConfigureAwait(false);
                    }
                    finally
                    {
                        if (completed)
                        {
                            foreach (EmergencyEntry entry in batch)
                                CompleteEmergencyEntry(entry);
                        }
                        else
                            ReturnEmergencyEntriesToRetry(batch);
                    }

                    if (!completed)
                    {
                        if (Volatile.Read(ref _disposeStarted) == 0)
                            await Task.Delay(EmergencyRetryDelayMs, token).ConfigureAwait(false);
                        else
                            await WaitForShutdownRetryAsync(roundStarted, token).ConfigureAwait(false);
                    }
                }
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
            }
        }

        private List<EmergencyEntry> TakeEmergencyBatch()
        {
            var batch = new List<EmergencyEntry>();
            long payloadBytes = GetEmptyBatchPayloadBytes();
            int maximumEvents = string.IsNullOrEmpty(_endpoint) ? 1 : _emergencyMaxBatchSize;

            while (batch.Count < maximumEvents)
            {
                EmergencyEntry? next = TakeEmergencyEntry();
                if (next is null)
                    break;

                long nextPayloadBytes = GetBatchPayloadBytesWithNextEntry(payloadBytes, batch.Count, next.Entry);
                if (batch.Count > 0 && nextPayloadBytes > _emergencyTargetBatchPayloadBytes)
                {
                    ReturnEmergencyEntriesToRetry(new List<EmergencyEntry> { next });
                    break;
                }

                batch.Add(next);
                payloadBytes = nextPayloadBytes;
                if (payloadBytes >= _emergencyTargetBatchPayloadBytes)
                    break;
            }

            return batch;
        }

        private EmergencyEntry? TakeEmergencyEntry()
        {
            lock (_emergencyBufferLock)
            {
                EmergencyEntry? entry;
                if (_emergencyRetryEntries.First is LinkedListNode<EmergencyEntry> retryNode)
                {
                    entry = retryNode.Value;
                    _emergencyRetryEntries.RemoveFirst();
                }
                else if (!_emergencyChannel.Reader.TryRead(out entry))
                {
                    return null;
                }

                _emergencyInFlight.Add(entry);
                _emergencyInFlightPayloadBytes += entry.PayloadBytes;
                return entry;
            }
        }

        private void ReturnEmergencyEntriesToRetry(List<EmergencyEntry> batch)
        {
            lock (_emergencyBufferLock)
            {
                for (int index = batch.Count - 1; index >= 0; index--)
                {
                    EmergencyEntry entry = batch[index];
                    if (!_emergencyInFlight.Remove(entry))
                        continue;

                    _emergencyInFlightPayloadBytes -= entry.PayloadBytes;
                    _emergencyRetryEntries.AddFirst(entry);
                }
            }
        }

        private async Task<bool> TryProcessEmergencyBatchAsync(List<EmergencyEntry> batch, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();

            // On shutdown prioritize sending volatile events before the process exits.
            if (Volatile.Read(ref _disposeStarted) == 0 || string.IsNullOrEmpty(_endpoint))
            {
                for (int index = 0; index < batch.Count;)
                {
                    token.ThrowIfCancellationRequested();
                    if (Volatile.Read(ref _disposeStarted) != 0 && !string.IsNullOrEmpty(_endpoint))
                        break;

                    LogEntry entry = batch[index].Entry;
                    bool persisted = false;
                    bool storageFailed = false;
                    try
                    {
                        persisted = ExecuteDatabaseWithRecovery(() => TryPersistLogEntryWithReclamationCore(entry));
                        if (persisted)
                            OnPersistedToSpool();
                        else
                            MarkSpoolRecovered();
                    }
                    catch (Exception ex)
                    {
                        MarkSpoolUnavailable(ex);
                        try
                        {
                            persisted = ExecuteDatabaseWithRecovery(() => EventExistsCore(entry.EventId));
                            if (persisted)
                                OnPersistedToSpool();
                        }
                        catch (Exception verificationException)
                        {
                            MarkSpoolUnavailable(verificationException);
                        }
                        storageFailed = !persisted;
                    }

                    if (persisted)
                    {
                        CompleteEmergencyEntry(batch[index]);
                        batch.RemoveAt(index);
                    }
                    else
                        index++;

                    // Do not repeat a failing storage operation for every volatile event in
                    // this batch when direct HTTP rescue is available.
                    if (storageFailed && !string.IsNullOrEmpty(_endpoint))
                        break;
                }
            }

            if (batch.Count == 0)
                return true;
            if (string.IsNullOrEmpty(_endpoint))
                return false;
            if (Volatile.Read(ref _disposeStarted) != 0)
                await _senderTask.WaitAsync(token).ConfigureAwait(false);

            var entries = new List<LogEntry>(batch.Count);
            foreach (EmergencyEntry bufferedEntry in batch)
                entries.Add(bufferedEntry.Entry);
            return await SendBatchAsync(entries, token).ConfigureAwait(false);
        }

        private void CompleteEmergencyEntry(EmergencyEntry entry)
        {
            lock (_emergencyBufferLock)
            {
                if (_emergencyInFlight.Remove(entry))
                    _emergencyInFlightPayloadBytes -= entry.PayloadBytes;

                Interlocked.Decrement(ref _emergencyBufferedCount);
                Interlocked.Add(ref _emergencyBufferedPayloadBytes, -entry.PayloadBytes);
            }
        }

        private sealed class EmergencyEntry
        {
            internal EmergencyEntry(LogEntry entry, long payloadBytes)
            {
                Entry = entry;
                PayloadBytes = payloadBytes;
            }

            internal LogEntry Entry { get; }

            internal long PayloadBytes { get; }
        }
    }
}
