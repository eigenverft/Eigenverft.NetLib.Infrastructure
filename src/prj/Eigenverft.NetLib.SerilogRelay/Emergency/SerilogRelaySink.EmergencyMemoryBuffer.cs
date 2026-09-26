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
        private void EnqueueEmergency(LogEntry entry, Exception exception)
        {
            MarkSpoolUnavailable(exception);

            long payloadBytes = GetEmergencyPayloadBytes(entry);
            if (!TryReserveEmergencyPayloadBytes(payloadBytes))
            {
                RecordEmergencyDrop();
                return;
            }

            Interlocked.Increment(ref _emergencyBufferedCount);
            if (_emergencyChannel.Writer.TryWrite(new EmergencyEntry(entry, payloadBytes)))
                return;

            Interlocked.Decrement(ref _emergencyBufferedCount);
            Interlocked.Add(ref _emergencyBufferedPayloadBytes, -payloadBytes);
            RecordEmergencyDrop();
        }

        private bool TryReserveEmergencyPayloadBytes(long payloadBytes)
        {
            long updated = Interlocked.Add(ref _emergencyBufferedPayloadBytes, payloadBytes);
            if (updated <= _maxEmergencyBufferedPayloadBytes)
                return true;

            Interlocked.Add(ref _emergencyBufferedPayloadBytes, -payloadBytes);
            return false;
        }

        private void RecordEmergencyDrop()
        {
            long dropped = Interlocked.Increment(ref _emergencyDroppedCount);
            if (Interlocked.Exchange(ref _emergencyOverflowReported, 1) != 0)
                return;

            SelfLog.WriteLine(
                "SerilogRelay emergency buffer limit reached ({0} events / {1} payload bytes). Events are now being dropped; total dropped: {2}.",
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
                    while (_emergencyChannel.Reader.TryRead(out EmergencyEntry? bufferedEntry))
                    {
                        LogEntry entry = bufferedEntry.Entry;
                        bool completed = false;
                        while (!completed)
                        {
                            token.ThrowIfCancellationRequested();

                            try
                            {
                                bool persisted = ExecuteDatabaseWithRecovery(() => PersistLogEntryCore(entry));
                                if (!persisted)
                                {
                                    MarkSpoolRecovered();
                                    RecordSpoolRejected();
                                    CompleteEmergencyEntry(bufferedEntry);
                                    completed = true;
                                    continue;
                                }

                                OnPersistedToSpool();
                                CompleteEmergencyEntry(bufferedEntry);
                                completed = true;
                                continue;
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

                            if (!string.IsNullOrEmpty(_endpoint)
                                && await SendBatchAsync(
                                    new List<LogEntry> { entry },
                                    token).ConfigureAwait(false))
                            {
                                CompleteEmergencyEntry(bufferedEntry);
                                completed = true;
                                continue;
                            }

                            await Task.Delay(EmergencyRetryDelayMs, token).ConfigureAwait(false);
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
            Interlocked.Decrement(ref _emergencyBufferedCount);
            Interlocked.Add(ref _emergencyBufferedPayloadBytes, -entry.PayloadBytes);
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
