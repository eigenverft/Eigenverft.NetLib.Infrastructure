using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.Data.Sqlite;

using Serilog;
using Serilog.Debugging;
using Serilog.Events;
using Serilog.Parsing;

namespace Eigenverft.NetLib.SerilogRelay
{
    public partial class SerilogRelaySink
    {
        private const string StatusMarkerProperty = "SerilogRelayStatus";
        private readonly SerilogRelayStatusEventMode _statusEventMode;
        private readonly LogEventLevel _statusMinimumLevel;
        private readonly TimeSpan? _statusSummaryInterval;
        private readonly Func<ILogger?>? _statusLoggerProvider;
        private int _lastSpoolUtilizationPercent = -1;
        private const int MaximumPendingStatusTransitions = 64;
        private readonly object _statusTransitionLock = new object();
        private readonly Queue<StatusTransition> _pendingStatusTransitions = new Queue<StatusTransition>();
        private readonly long[] _coalescedStatusTransitionCounts = new long[8];
        private readonly long[] _observedStatusTransitionCounts = new long[8];
        private DateTimeOffset? _coalescedStatusFirstUtc;
        private DateTimeOffset? _coalescedStatusLastUtc;
        private readonly SemaphoreSlim _statusSignal = new SemaphoreSlim(0, 1);
        private bool _emergencyPressureActive;
        private bool _spoolBudgetPressureActive;
        private int _spoolFailureEverObserved;
        private int _spoolDropWakePending;
        private int _emergencyDropWakePending;

        private enum StatusTransitionKind
        {
            SpoolUnavailable,
            SpoolRecovered,
            EndpointDeliveryFailed,
            EndpointDeliveryRecovered,
            EmergencyBufferNearLimit,
            EmergencyBufferPressureRelieved,
            SpoolNearLimit,
            SpoolPressureRelieved,
        }

        private readonly record struct StatusTransition(StatusTransitionKind Kind, DateTimeOffset OccurredAtUtc);

        private sealed class CoalescedStatusTransitions
        {
            internal CoalescedStatusTransitions(long[] counts, DateTimeOffset firstUtc, DateTimeOffset lastUtc)
            {
                Counts = counts;
                FirstUtc = firstUtc;
                LastUtc = lastUtc;
            }

            internal long[] Counts { get; }
            internal DateTimeOffset FirstUtc { get; }
            internal DateTimeOffset LastUtc { get; }
        }

        // Called under _statusTransitionLock by the producers of operational state changes.
        private void RecordStatusTransitionCore(StatusTransitionKind kind)
        {
            if (_statusEventMode == SerilogRelayStatusEventMode.Off)
                return;

            DateTimeOffset now = _timeProvider.GetUtcNow();
            _observedStatusTransitionCounts[(int)kind]++;
            if (kind == StatusTransitionKind.SpoolUnavailable)
                Volatile.Write(ref _spoolFailureEverObserved, 1);
            if (_coalescedStatusFirstUtc.HasValue || _pendingStatusTransitions.Count == MaximumPendingStatusTransitions)
            {
                _coalescedStatusFirstUtc ??= now;
                _coalescedStatusLastUtc = now;
                _coalescedStatusTransitionCounts[(int)kind]++;
            }
            else
            {
                _pendingStatusTransitions.Enqueue(new StatusTransition(kind, now));
            }
        }

        private void RecordStatusTransition(StatusTransitionKind kind)
        {
            lock (_statusTransitionLock)
                RecordStatusTransitionCore(kind);
            WakeStatusLoop();
        }

        private void WakeStatusLoop()
        {
            if (_statusEventMode == SerilogRelayStatusEventMode.Off)
                return;

            try
            {
                // Serialize producers; the status worker only consumes the signal.
                lock (_statusTransitionLock)
                {
                    if (_statusSignal.CurrentCount == 0)
                        _statusSignal.Release();
                }
            }
            catch (ObjectDisposedException) when (Volatile.Read(ref _disposeStarted) != 0)
            {
                // A concurrent emit can finish after bounded sink disposal.
            }
        }

        private void RecordEndpointDeliveryState(bool failed)
        {
            lock (_statusTransitionLock)
            {
                int value = failed ? 1 : 0;
                if (Volatile.Read(ref _endpointFailureActive) == value)
                    return;
                Volatile.Write(ref _endpointFailureActive, value);
                RecordStatusTransitionCore(failed
                    ? StatusTransitionKind.EndpointDeliveryFailed
                    : StatusTransitionKind.EndpointDeliveryRecovered);
            }
            WakeStatusLoop();
        }

        // Called while _emergencyBufferLock is held after a complete buffer mutation.
        private void ObserveEmergencyPressureUnderLock()
        {
            if (_statusEventMode == SerilogRelayStatusEventMode.Off)
                return;

            // Our own diagnostics occupy capacity but must not create more pressure diagnostics.
            bool nearLimit = _emergencyApplicationEventCount >= _emergencyBufferCapacity * 0.8
                || _emergencyApplicationPayloadBytes >= _maxEmergencyBufferedPayloadBytes * 0.8;
            if (!_emergencyPressureActive && nearLimit)
            {
                _emergencyPressureActive = true;
                RecordStatusTransition(StatusTransitionKind.EmergencyBufferNearLimit);
            }
            else if (_emergencyPressureActive
                && _emergencyApplicationEventCount < _emergencyBufferCapacity * 0.6
                && _emergencyApplicationPayloadBytes < _maxEmergencyBufferedPayloadBytes * 0.6)
            {
                _emergencyPressureActive = false;
                RecordStatusTransition(StatusTransitionKind.EmergencyBufferPressureRelieved);
            }
        }

        private static bool IsRelayStatusEvent(LogEvent logEvent)
            => logEvent.Properties.TryGetValue(StatusMarkerProperty, out LogEventPropertyValue? value)
                && value is ScalarValue { Value: true }
                && logEvent.Properties.TryGetValue("SourceContext", out LogEventPropertyValue? source)
                && source is ScalarValue { Value: "Eigenverft.NetLib.SerilogRelay" }
                && logEvent.Properties.ContainsKey("RelayStatusCode");

        private async Task StatusLoopAsync()
        {
            CancellationToken token = _cts.Token;
            bool startupPending = true;
            long previousSpoolDrops = 0;
            long previousEmergencyDrops = 0;
            DateTimeOffset nextSpoolCheck = DateTimeOffset.MinValue;
            DateTimeOffset nextSpoolDropReport = DateTimeOffset.MinValue;
            DateTimeOffset nextEmergencyDropReport = DateTimeOffset.MinValue;
            DateTimeOffset nextPublishRetry = DateTimeOffset.MinValue;
            DateTimeOffset nextSummary = _statusSummaryInterval.HasValue
                ? _timeProvider.GetUtcNow() + _statusSummaryInterval.Value
                : DateTimeOffset.MaxValue;
            CoalescedStatusTransitions? coalescedInFlight = null;

            try
            {
                while (!token.IsCancellationRequested && Volatile.Read(ref _disposeStarted) == 0)
                {
                    DateTimeOffset now = _timeProvider.GetUtcNow();
                    if (now >= nextSpoolCheck)
                    {
                        nextSpoolCheck = now + TimeSpan.FromSeconds(30);
                        int utilization = TryGetSpoolUtilizationPercent();
                        Volatile.Write(ref _lastSpoolUtilizationPercent, utilization);
                        if (utilization >= 80 && !_spoolBudgetPressureActive)
                        {
                            _spoolBudgetPressureActive = true;
                            RecordStatusTransition(StatusTransitionKind.SpoolNearLimit);
                        }
                        else if (utilization >= 0 && utilization < 60 && _spoolBudgetPressureActive)
                        {
                            _spoolBudgetPressureActive = false;
                            RecordStatusTransition(StatusTransitionKind.SpoolPressureRelieved);
                        }
                    }

                    bool canPublish = now >= nextPublishRetry;
                    if (canPublish && startupPending)
                    {
                        if (Volatile.Read(ref _spoolFailureEverObserved) != 0
                            || Volatile.Read(ref _spoolUnavailable) != 0)
                        {
                            startupPending = false;
                        }
                        else if (PublishStatus(LogEventLevel.Information, "Initialized",
                            "SerilogRelay initialized; local spool is available"))
                        {
                            startupPending = false;
                        }
                        else
                        {
                            nextPublishRetry = now + TimeSpan.FromSeconds(5);
                            canPublish = false;
                        }
                    }

                    if (canPublish && !DrainStatusTransitions(ref coalescedInFlight))
                    {
                        nextPublishRetry = now + TimeSpan.FromSeconds(5);
                        canPublish = false;
                    }

                    // Status events cannot reclaim unsent spool rows. Exclude dropped status
                    // events in RAM so a loss report does not describe its own rejection.
                    long spoolDrops = Interlocked.Read(ref _applicationSpoolDroppedCount);
                    if (spoolDrops > previousSpoolDrops
                        && now >= nextSpoolDropReport
                        && canPublish)
                    {
                        if (PublishStatus(LogEventLevel.Error, "SpoolEventsDropped",
                            "SerilogRelay spool capacity caused {DroppedSinceLastStatus} unsent events to be dropped",
                            spoolDrops - previousSpoolDrops))
                        {
                            previousSpoolDrops = spoolDrops;
                            nextSpoolDropReport = now + TimeSpan.FromMinutes(1);
                            Interlocked.Exchange(ref _spoolDropWakePending, 0);
                        }
                        else
                        {
                            nextPublishRetry = now + TimeSpan.FromSeconds(5);
                            canPublish = false;
                        }
                    }

                    long emergencyDrops = Interlocked.Read(ref _emergencyDroppedCount)
                        - Interlocked.Read(ref _statusEmergencyDroppedCount);
                    if (emergencyDrops > previousEmergencyDrops
                        && now >= nextEmergencyDropReport
                        && canPublish)
                    {
                        if (PublishStatus(LogEventLevel.Error, "EmergencyEventsDropped",
                            "SerilogRelay emergency buffer capacity caused {DroppedSinceLastStatus} events to be dropped",
                            emergencyDrops - previousEmergencyDrops))
                        {
                            previousEmergencyDrops = emergencyDrops;
                            nextEmergencyDropReport = now + TimeSpan.FromMinutes(1);
                            Interlocked.Exchange(ref _emergencyDropWakePending, 0);
                        }
                        else
                        {
                            nextPublishRetry = now + TimeSpan.FromSeconds(5);
                            canPublish = false;
                        }
                    }

                    if (_statusSummaryInterval.HasValue && now >= nextSummary)
                    {
                        if (canPublish)
                            PublishStatus(LogEventLevel.Debug, "StatusSummary",
                                "SerilogRelay periodic status summary");
                        nextSummary = now + _statusSummaryInterval.Value;
                    }

                    DateTimeOffset nextWake = nextSpoolCheck < nextSummary ? nextSpoolCheck : nextSummary;
                    if (nextPublishRetry > now && nextPublishRetry < nextWake)
                        nextWake = nextPublishRetry;
                    if (spoolDrops > previousSpoolDrops
                        && nextSpoolDropReport > now && nextSpoolDropReport < nextWake)
                        nextWake = nextSpoolDropReport;
                    if (emergencyDrops > previousEmergencyDrops
                        && nextEmergencyDropReport > now && nextEmergencyDropReport < nextWake)
                        nextWake = nextEmergencyDropReport;

                    TimeSpan wait = nextWake - _timeProvider.GetUtcNow();
                    await _statusSignal.WaitAsync(wait > TimeSpan.Zero ? wait : TimeSpan.Zero, token)
                        .ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
            }
            catch (Exception ex)
            {
                SelfLog.WriteLine("SerilogRelay status reporting stopped: {0}", ex.Message);
            }
        }

        private bool DrainStatusTransitions(ref CoalescedStatusTransitions? coalescedInFlight)
        {
            for (int published = 0; published < MaximumPendingStatusTransitions; published++)
            {
                if (coalescedInFlight is not null)
                {
                    if (!PublishCoalescedStatusTransitions(coalescedInFlight))
                        return false;
                    coalescedInFlight = null;
                    continue;
                }

                StatusTransition? next = null;
                lock (_statusTransitionLock)
                {
                    if (_pendingStatusTransitions.Count > 0)
                    {
                        next = _pendingStatusTransitions.Peek();
                    }
                    else if (_coalescedStatusFirstUtc.HasValue)
                    {
                        coalescedInFlight = new CoalescedStatusTransitions(
                            (long[])_coalescedStatusTransitionCounts.Clone(),
                            _coalescedStatusFirstUtc.Value,
                            _coalescedStatusLastUtc!.Value);
                        Array.Clear(_coalescedStatusTransitionCounts);
                        _coalescedStatusFirstUtc = null;
                        _coalescedStatusLastUtc = null;
                    }
                }

                if (next.HasValue)
                {
                    if (!PublishStatusTransition(next.Value))
                        return false;
                    lock (_statusTransitionLock)
                        _pendingStatusTransitions.Dequeue();
                    continue;
                }

                if (coalescedInFlight is null)
                    return true;
            }

            WakeStatusLoop();
            return true;
        }

        private bool PublishStatusTransition(StatusTransition transition)
        {
            string template = transition.Kind switch
            {
                StatusTransitionKind.SpoolUnavailable =>
                    "SerilogRelay spool unavailable; volatile emergency buffering is active",
                StatusTransitionKind.SpoolRecovered =>
                    "SerilogRelay spool recovered; durable persistence resumed",
                StatusTransitionKind.EndpointDeliveryFailed =>
                    "SerilogRelay HTTP delivery failed; delivery will retry",
                StatusTransitionKind.EndpointDeliveryRecovered =>
                    "SerilogRelay HTTP delivery succeeded after a failure",
                StatusTransitionKind.EmergencyBufferNearLimit =>
                    "SerilogRelay emergency buffer is nearing its configured capacity",
                StatusTransitionKind.EmergencyBufferPressureRelieved =>
                    "SerilogRelay emergency buffer is below its pressure threshold again",
                StatusTransitionKind.SpoolNearLimit =>
                    "SerilogRelay spool is nearing its configured SQLite page budget",
                StatusTransitionKind.SpoolPressureRelieved =>
                    "SerilogRelay spool is below its configured budget pressure threshold again",
                _ => throw new ArgumentOutOfRangeException(nameof(transition)),
            };
            return PublishStatus(LogEventLevel.Warning, transition.Kind.ToString(), template,
                occurredAtUtc: transition.OccurredAtUtc);
        }

        private bool PublishCoalescedStatusTransitions(CoalescedStatusTransitions transitions)
        {
            long total = 0;
            var details = new List<LogEventProperty>
            {
                new LogEventProperty("RelayCoalescedFirstUtc", new ScalarValue(transitions.FirstUtc)),
                new LogEventProperty("RelayCoalescedLastUtc", new ScalarValue(transitions.LastUtc)),
            };
            for (int index = 0; index < transitions.Counts.Length; index++)
            {
                long count = transitions.Counts[index];
                total += count;
                if (count > 0)
                    details.Add(new LogEventProperty(
                        "RelayCoalesced" + ((StatusTransitionKind)index),
                        new ScalarValue(count)));
            }
            details.Add(new LogEventProperty("RelayCoalescedTransitionCount", new ScalarValue(total)));
            return PublishStatus(LogEventLevel.Warning, "StatusTransitionsCoalesced",
                "SerilogRelay combined {RelayCoalescedTransitionCount} rapid operational state changes",
                additionalProperties: details);
        }

        private int TryGetSpoolUtilizationPercent()
        {
            if (_spoolDisabledForLifetime || Volatile.Read(ref _spoolUnavailable) != 0)
                return -1;

            try
            {
                return ExecuteDatabaseWithRecovery(() =>
                {
                    using var connection = new SqliteConnection(_connectionString);
                    connection.Open();
                    using var command = connection.CreateCommand();
                    command.CommandText = "SELECT page_size, page_count, freelist_count FROM pragma_page_size(), pragma_page_count(), pragma_freelist_count();";
                    using var reader = command.ExecuteReader();
                    reader.Read(); // These scalar PRAGMAs always return one row.
                    long usedBytes = (reader.GetInt64(1) - reader.GetInt64(2)) * reader.GetInt64(0);
                    return (int)Math.Min(100d, usedBytes * 100d / _maxApplicationSpoolPhysicalBytes);
                }, updatesSpool: false);
            }
            catch (Exception)
            {
                return -1;
            }
        }

        private (long Count, DateTimeOffset? OldestUtc)? TryGetPendingSummary()
        {
            if (_spoolDisabledForLifetime || Volatile.Read(ref _spoolUnavailable) != 0)
                return null;

            try
            {
                return ExecuteDatabaseWithRecovery<(long Count, DateTimeOffset? OldestUtc)?>(() =>
                {
                    using var connection = new SqliteConnection(_connectionString);
                    connection.Open();
                    using var command = connection.CreateCommand();
                    command.CommandText = $"SELECT COUNT(*), MIN(CreatedAt) FROM {TableName} WHERE Sent = 0;";
                    using var reader = command.ExecuteReader();
                    reader.Read(); // COUNT without GROUP BY always returns one row.
                    DateTimeOffset? oldest = null;
                    if (!reader.IsDBNull(1)
                        && DateTimeOffset.TryParse(
                            reader.GetString(1),
                            CultureInfo.InvariantCulture,
                            DateTimeStyles.AssumeUniversal,
                            out DateTimeOffset parsed))
                    {
                        oldest = parsed.ToUniversalTime();
                    }
                    return (reader.GetInt64(0), oldest);
                }, updatesSpool: false);
            }
            catch (Exception)
            {
                return null;
            }
        }

        private bool PublishStatus(
            LogEventLevel level,
            string code,
            string template,
            long? droppedSinceLastStatus = null,
            DateTimeOffset? occurredAtUtc = null,
            IEnumerable<LogEventProperty>? additionalProperties = null)
        {
            if (level < _statusMinimumLevel || _statusEventMode == SerilogRelayStatusEventMode.Off)
                return true;

            var properties = new List<LogEventProperty>
            {
                new LogEventProperty(StatusMarkerProperty, new ScalarValue(true)),
                new LogEventProperty("SourceContext", new ScalarValue("Eigenverft.NetLib.SerilogRelay")),
                new LogEventProperty("RelayStatusCode", new ScalarValue(code)),
                new LogEventProperty("RelayApplicationId", new ScalarValue(_applicationId)),
                new LogEventProperty("RelayClaimablePendingEvents", new ScalarValue(Interlocked.Read(ref _pendingCount))),
                new LogEventProperty("RelaySpoolAvailable", new ScalarValue(Volatile.Read(ref _spoolUnavailable) == 0)),
                new LogEventProperty("RelayEndpointConfigured", new ScalarValue(!string.IsNullOrEmpty(_endpoint))),
                new LogEventProperty("RelayEndpointFailureActive", new ScalarValue(Volatile.Read(ref _endpointFailureActive) != 0)),
                new LogEventProperty("RelayEmergencyBufferedEvents", new ScalarValue(Interlocked.Read(ref _emergencyBufferedCount))),
                new LogEventProperty("RelayEmergencyBufferedPayloadBytes", new ScalarValue(Interlocked.Read(ref _emergencyBufferedPayloadBytes))),
                new LogEventProperty("RelayEmergencyDroppedEvents", new ScalarValue(Interlocked.Read(ref _emergencyDroppedCount))),
                new LogEventProperty("RelaySpoolDroppedEvents", new ScalarValue(Interlocked.Read(ref _applicationSpoolDroppedCount))),
            };
            lock (_statusTransitionLock)
            {
                properties.Add(new LogEventProperty("RelaySpoolFailureCount",
                    new ScalarValue(_observedStatusTransitionCounts[(int)StatusTransitionKind.SpoolUnavailable])));
                properties.Add(new LogEventProperty("RelaySpoolRecoveryCount",
                    new ScalarValue(_observedStatusTransitionCounts[(int)StatusTransitionKind.SpoolRecovered])));
                properties.Add(new LogEventProperty("RelayEndpointFailureCount",
                    new ScalarValue(_observedStatusTransitionCounts[(int)StatusTransitionKind.EndpointDeliveryFailed])));
                properties.Add(new LogEventProperty("RelayEndpointRecoveryCount",
                    new ScalarValue(_observedStatusTransitionCounts[(int)StatusTransitionKind.EndpointDeliveryRecovered])));
                properties.Add(new LogEventProperty("RelayEmergencyPressureCount",
                    new ScalarValue(_observedStatusTransitionCounts[(int)StatusTransitionKind.EmergencyBufferNearLimit])));
                properties.Add(new LogEventProperty("RelayEmergencyPressureRecoveryCount",
                    new ScalarValue(_observedStatusTransitionCounts[(int)StatusTransitionKind.EmergencyBufferPressureRelieved])));
                properties.Add(new LogEventProperty("RelaySpoolBudgetPressureCount",
                    new ScalarValue(_observedStatusTransitionCounts[(int)StatusTransitionKind.SpoolNearLimit])));
                properties.Add(new LogEventProperty("RelaySpoolBudgetPressureRecoveryCount",
                    new ScalarValue(_observedStatusTransitionCounts[(int)StatusTransitionKind.SpoolPressureRelieved])));
            }
            (long Count, DateTimeOffset? OldestUtc)? pending = code == "StatusSummary"
                ? TryGetPendingSummary()
                : null;
            if (pending.HasValue)
            {
                properties.Add(new LogEventProperty("RelayPendingEvents", new ScalarValue(pending.Value.Count)));
                if (pending.Value.OldestUtc.HasValue)
                    properties.Add(new LogEventProperty("RelayOldestPendingUtc", new ScalarValue(pending.Value.OldestUtc.Value)));
            }
            int utilization = Volatile.Read(ref _lastSpoolUtilizationPercent);
            if (utilization >= 0)
                properties.Add(new LogEventProperty("RelaySpoolBudgetUsedPercent", new ScalarValue(utilization)));
            long lastSuccess = Interlocked.Read(ref _lastHttpSuccessUnixMs);
            if (lastSuccess > 0)
                properties.Add(new LogEventProperty("RelayLastSuccessfulSendUtc", new ScalarValue(DateTimeOffset.FromUnixTimeMilliseconds(lastSuccess))));
            if (droppedSinceLastStatus.HasValue)
                properties.Add(new LogEventProperty("DroppedSinceLastStatus", new ScalarValue(droppedSinceLastStatus.Value)));
            if (additionalProperties is not null)
                properties.AddRange(additionalProperties);

            var logEvent = new LogEvent(
                occurredAtUtc ?? _timeProvider.GetUtcNow(),
                level,
                exception: null,
                new MessageTemplateParser().Parse(template),
                properties);
            try
            {
                if (_statusEventMode == SerilogRelayStatusEventMode.RelayOnly)
                    return TryEmit(logEvent, reportRejectedEvent: false);

                ILogger? logger = _statusLoggerProvider?.Invoke();
                if (logger is null)
                    return false;
                logger.Write(logEvent);
                return true;
            }
            catch (Exception ex)
            {
                SelfLog.WriteLine("SerilogRelay could not publish operational status {0}: {1}", code, ex.Message);
                return false;
            }
        }

    }
}
