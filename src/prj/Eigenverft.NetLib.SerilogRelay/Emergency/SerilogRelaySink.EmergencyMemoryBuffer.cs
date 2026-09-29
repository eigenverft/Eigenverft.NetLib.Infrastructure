using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

using Serilog.Debugging;

namespace Eigenverft.NetLib.SerilogRelay
{
    public partial class SerilogRelaySink
    {
        private readonly object _emergencyBufferLock = new object();
        private EmergencyEntry? _emergencyInFlight;

        private void EnqueueEmergency(LogEntry entry, Exception? exception)
        {
            if (exception is not null)
                MarkSpoolUnavailable(exception);

            long payloadBytes = GetEmergencyPayloadBytes(entry);
            lock (_emergencyBufferLock)
            {
                long inFlightBytes = _emergencyInFlight?.PayloadBytes ?? 0L;
                if (payloadBytes > _maxEmergencyBufferedPayloadBytes - inFlightBytes)
                {
                    RecordEmergencyDrop();
                    return;
                }

                while (_emergencyBufferedCount >= _emergencyBufferCapacity
                    || payloadBytes > _maxEmergencyBufferedPayloadBytes - _emergencyBufferedPayloadBytes)
                {
                    if (!_emergencyChannel.Reader.TryRead(out EmergencyEntry? oldest))
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
                "SerilogRelay emergency buffer limit reached ({0} events / {1} payload bytes). Oldest queued events are evicted when possible; total dropped: {2}.",
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
                while (await _emergencyChannel.Reader.WaitToReadAsync(token).ConfigureAwait(false))
                {
                    while (true)
                    {
                        EmergencyEntry? bufferedEntry;
                        lock (_emergencyBufferLock)
                        {
                            if (!_emergencyChannel.Reader.TryRead(out bufferedEntry))
                                break;
                            _emergencyInFlight = bufferedEntry;
                        }

                        LogEntry entry = bufferedEntry.Entry;
                        bool completed = false;
                        while (!completed)
                        {
                            token.ThrowIfCancellationRequested();

                            // On shutdown prioritize sending volatile events before the process exits.
                            if (Volatile.Read(ref _disposeStarted) == 0 || string.IsNullOrEmpty(_endpoint))
                            {
                                try
                                {
                                    bool persisted = ExecuteDatabaseWithRecovery(() => TryPersistLogEntryCore(entry));
                                    if (persisted)
                                    {
                                        OnPersistedToSpool();
                                        CompleteEmergencyEntry(bufferedEntry);
                                        completed = true;
                                        continue;
                                    }

                                    MarkSpoolRecovered();
                                }
                                catch (Exception ex)
                                {
                                    MarkSpoolUnavailable(ex);
                                    try
                                    {
                                        if (ExecuteDatabaseWithRecovery(() => EventExistsCore(entry.EventId)))
                                        {
                                            OnPersistedToSpool();
                                            CompleteEmergencyEntry(bufferedEntry);
                                            completed = true;
                                            continue;
                                        }
                                    }
                                    catch (Exception verificationException)
                                    {
                                        MarkSpoolUnavailable(verificationException);
                                    }
                                }
                            }

                            if (!string.IsNullOrEmpty(_endpoint)
                                && await SendBatchAsync(
                                    new List<LogEntry> { entry },
                                    token).ConfigureAwait(false))
                            {
                                CompleteEmergencyEntry(bufferedEntry);
                                completed = true;
                                continue;
                            }

                            await Task.Delay(Volatile.Read(ref _disposeStarted) == 0 ? TimeSpan.FromMilliseconds(EmergencyRetryDelayMs) : _shutdownRetryInterval, token).ConfigureAwait(false);
                        }
                    }
                }
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
            }
        }

        private void CompleteEmergencyEntry(EmergencyEntry entry)
        {
            lock (_emergencyBufferLock)
            {
                if (ReferenceEquals(_emergencyInFlight, entry))
                    _emergencyInFlight = null;

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
