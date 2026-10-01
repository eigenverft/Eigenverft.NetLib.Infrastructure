using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.Data.Sqlite;
using Microsoft.VisualStudio.TestTools.UnitTesting;

using Serilog;
using Serilog.Core;
using Serilog.Events;

namespace Eigenverft.NetLib.SerilogRelay.Tests
{
    public sealed partial class SerilogRelayReliabilityTests
    {
        [TestMethod]
        public void StatusOptionsValidateModesLevelsAndSummaryIntervals()
        {
            foreach (Action<SerilogRelayOptions> invalid in new Action<SerilogRelayOptions>[]
            {
                options => options.StatusEvents.Mode = (SerilogRelayStatusEventMode)99,
                options => options.StatusEvents.Mode = SerilogRelayStatusEventMode.AllSinks,
                options => options.StatusEvents.MinimumLevel = (LogEventLevel)99,
                options => options.StatusEvents.SummaryInterval = TimeSpan.FromSeconds(59),
            })
            {
                var options = new SerilogRelayOptions(); invalid(options);
                Assert.Throws<ArgumentException>(() => new SerilogRelaySink("Data Source=:memory:", null, options));
            }
        }

        [TestMethod]
        public async Task StatusRelayOnlyPersistsMarkersAndHonorsMinimumLevel()
        {
            string directory = CreateTemporaryDirectory();
            try
            {
                string connectionString = $"Data Source={Path.Combine(directory, "status.db")}";
                var options = new SerilogRelayOptions();
                options.StatusEvents.Mode = SerilogRelayStatusEventMode.RelayOnly;
                options.StatusEvents.MinimumLevel = LogEventLevel.Information;
                await using var sink = new SerilogRelaySink(connectionString, null, options);
                await WaitUntilAsync(() => GetUnsentCount(connectionString) == 1, TimeSpan.FromSeconds(5));
                Assert.IsTrue(PublishTestStatus(sink, "Filtered", LogEventLevel.Debug));
                Assert.AreEqual(1L, GetUnsentCount(connectionString));
                Assert.IsTrue(PublishTestStatus(sink, "TestWarning"));
                using var connection = new SqliteConnection(connectionString); connection.Open();
                using var command = connection.CreateCommand();
                command.CommandText = "SELECT Properties FROM SerilogRelayEvents WHERE Level = 'Warning'";
                string properties = (string)command.ExecuteScalar()!;
                StringAssert.Contains(properties, "TestWarning");
                StringAssert.Contains(properties, "SerilogRelayStatus");
                SetPrivateField(sink, "_statusEventMode", SerilogRelayStatusEventMode.Off);
                Assert.IsTrue(PublishTestStatus(sink, "Off"));
                Assert.AreEqual(2L, GetUnsentCount(connectionString));
            }
            finally { DeleteTemporaryDirectory(directory); }
        }

        [TestMethod]
        public async Task StatusTransitionsKeepOrderAndCoalesceWithoutLosingCountsWhenPublicationFails()
        {
            string directory = CreateTemporaryDirectory();
            try
            {
                await using var sink = new SerilogRelaySink($"Data Source={Path.Combine(directory, "transitions.db")}", null, new SerilogRelayOptions());
                var capture = new StatusCaptureSink();
                using Logger logger = new LoggerConfiguration().MinimumLevel.Verbose().WriteTo.Sink(capture).CreateLogger();
                bool available = false;
                SetPrivateField(sink, "_statusEventMode", SerilogRelayStatusEventMode.AllSinks);
                SetPrivateField(sink, "_statusLoggerProvider", (Func<ILogger?>)(() => available ? logger : null));
                for (int i = 0; i < 70; i++)
                    InvokePrivateMethod<object?>(sink, "RecordEndpointDeliveryState", i % 2 == 0);
                object?[] drainArgs = { null };
                MethodInfo drain = typeof(SerilogRelaySink).GetMethod("DrainStatusTransitions", BindingFlags.NonPublic | BindingFlags.Instance)!;
                Assert.IsFalse((bool)drain.Invoke(sink, drainArgs)!);
                Assert.AreEqual(0, capture.Events.Count);
                available = true;
                Assert.IsTrue((bool)drain.Invoke(sink, drainArgs)!);
                Assert.AreEqual(64, capture.Events.Count);
                Assert.AreEqual("EndpointDeliveryFailed", StatusCode(capture.Events.First()));
                available = false;
                Assert.IsFalse((bool)drain.Invoke(sink, drainArgs)!);
                Assert.IsNotNull(drainArgs[0], "Coalesced transitions must survive rejected publication.");
                available = true;
                Assert.IsTrue((bool)drain.Invoke(sink, drainArgs)!);
                LogEvent combined = capture.Events.Last();
                Assert.AreEqual("StatusTransitionsCoalesced", StatusCode(combined));
                Assert.AreEqual(6L, Scalar<long>(combined, "RelayCoalescedTransitionCount"));
                Assert.AreEqual(3L, Scalar<long>(combined, "RelayCoalescedEndpointDeliveryFailed"));
                Assert.AreEqual(35L, Scalar<long>(combined, "RelayEndpointFailureCount"));
                Assert.AreEqual(35L, Scalar<long>(combined, "RelayEndpointRecoveryCount"));
                Assert.IsNull(drainArgs[0]);

                InvokePrivateMethod<object?>(sink, "MarkSpoolUnavailable", new IOException("offline"));
                InvokePrivateMethod<object?>(sink, "MarkSpoolRecovered");
                SetPrivateField(sink, "_emergencyApplicationEventCount", 14000L);
                InvokePrivateMethod<object?>(sink, "ObserveEmergencyPressureUnderLock");
                InvokePrivateMethod<object?>(sink, "ObserveEmergencyPressureUnderLock");
                SetPrivateField(sink, "_emergencyApplicationEventCount", 0L);
                SetPrivateField(sink, "_emergencyApplicationPayloadBytes", 64L * 1024 * 1024);
                InvokePrivateMethod<object?>(sink, "ObserveEmergencyPressureUnderLock");
                SetPrivateField(sink, "_emergencyApplicationPayloadBytes", 0L);
                InvokePrivateMethod<object?>(sink, "ObserveEmergencyPressureUnderLock");
                RecordTestTransition(sink, "SpoolNearLimit"); RecordTestTransition(sink, "SpoolPressureRelieved");
                Assert.IsTrue((bool)drain.Invoke(sink, drainArgs)!);
                CollectionAssert.AreEqual(new[] { "SpoolUnavailable", "SpoolRecovered", "EmergencyBufferNearLimit", "EmergencyBufferPressureRelieved", "SpoolNearLimit", "SpoolPressureRelieved" },
                    capture.Events.Skip(65).Select(StatusCode).ToArray());
                SetPrivateField(sink, "_statusLoggerProvider", (Func<ILogger?>)(() => throw new IOException("logger unavailable")));
                Assert.IsFalse(PublishTestStatus(sink, "ThrowingProvider"));
            }
            finally { DeleteTemporaryDirectory(directory); }
        }

        [TestMethod]
        public async Task StatusWorkerRetriesStartupAndTransitionsAggregatesDropsAndPublishesSummaries()
        {
            string directory = CreateTemporaryDirectory();
            try
            {
                string connectionString = $"Data Source={Path.Combine(directory, "worker.db")}";
                await using var sink = new SerilogRelaySink(connectionString, null, new SerilogRelayOptions());
                var clock = new ManualRelayTimeProvider();
                var capture = new StatusCaptureSink();
                using Logger logger = new LoggerConfiguration().MinimumLevel.Verbose().WriteTo.Sink(capture).CreateLogger();
                bool available = false;
                ConfigureTestStatusWorker(sink, clock, () => available ? logger : null, TimeSpan.FromMinutes(1));
                sink.Emit(CreateLogEvent("pending application event"));
                StartTestStatusWorker(sink);
                Assert.AreEqual(0, capture.Events.Count);
                RecordTestTransition(sink, "EndpointDeliveryFailed");
                available = true;
                clock.Advance(TimeSpan.FromSeconds(5)); WakeTestStatusWorker(sink);
                await WaitUntilAsync(() => capture.Events.Count == 2, TimeSpan.FromSeconds(5));
                Assert.AreEqual("Initialized", StatusCode(capture.Events.First()));

                SetPrivateField(sink, "_applicationSpoolDroppedCount", 2L);
                SetPrivateField(sink, "_emergencyDroppedCount", 4L);
                SetPrivateField(sink, "_statusEmergencyDroppedCount", 1L);
                WakeTestStatusWorker(sink);
                await WaitUntilAsync(() => capture.Events.Count == 4, TimeSpan.FromSeconds(5));
                Assert.AreEqual(2L, Scalar<long>(capture.Events.ElementAt(2), "DroppedSinceLastStatus"));
                Assert.AreEqual(3L, Scalar<long>(capture.Events.ElementAt(3), "DroppedSinceLastStatus"));
                SetPrivateField(sink, "_applicationSpoolDroppedCount", 5L);
                SetPrivateField(sink, "_emergencyDroppedCount", 6L);
                WakeTestStatusWorker(sink);
                await Task.Delay(30);
                Assert.AreEqual(4, capture.Events.Count, "Loss reports must be rate limited.");
                SetPrivateField(sink, "_lastHttpSuccessUnixMs", clock.GetUtcNow().ToUnixTimeMilliseconds());
                clock.Advance(TimeSpan.FromMinutes(1)); WakeTestStatusWorker(sink);
                await WaitUntilAsync(() => capture.Events.Count == 7, TimeSpan.FromSeconds(5));
                LogEvent summary = capture.Events.Last();
                Assert.AreEqual("StatusSummary", StatusCode(summary));
                Assert.AreEqual(1L, Scalar<long>(summary, "RelayPendingEvents"));
                Assert.IsTrue(summary.Properties.ContainsKey("RelayOldestPendingUtc"));
                Assert.IsTrue(summary.Properties.ContainsKey("RelayLastSuccessfulSendUtc"));
                Assert.IsTrue(summary.Properties.ContainsKey("RelaySpoolBudgetUsedPercent"));
            }
            finally { DeleteTemporaryDirectory(directory); }
        }

        [TestMethod]
        public async Task StatusSummaryHandlesEmptyInvalidUnavailableAndFailedSpools()
        {
            string directory = CreateTemporaryDirectory();
            try
            {
                string connectionString = $"Data Source={Path.Combine(directory, "summary.db")}";
                await using var sink = new SerilogRelaySink(connectionString, null, new SerilogRelayOptions());
                var capture = new StatusCaptureSink();
                using Logger logger = new LoggerConfiguration().MinimumLevel.Verbose().WriteTo.Sink(capture).CreateLogger();
                ConfigureTestStatusWorker(sink, new ManualRelayTimeProvider(), () => logger, null);
                Assert.IsTrue(PublishTestStatus(sink, "StatusSummary", LogEventLevel.Debug));
                Assert.AreEqual(0L, Scalar<long>(capture.Events.Last(), "RelayPendingEvents"));
                Assert.IsFalse(capture.Events.Last().Properties.ContainsKey("RelayOldestPendingUtc"));
                sink.Emit(CreateLogEvent("invalid timestamp"));
                using (var connection = new SqliteConnection(connectionString))
                {
                    connection.Open(); using var command = connection.CreateCommand();
                    command.CommandText = "UPDATE SerilogRelayEvents SET CreatedAt = 'invalid'"; command.ExecuteNonQuery();
                }
                Assert.IsTrue(PublishTestStatus(sink, "StatusSummary", LogEventLevel.Debug));
                Assert.IsFalse(capture.Events.Last().Properties.ContainsKey("RelayOldestPendingUtc"));
                SetPrivateField(sink, "_spoolUnavailable", 1);
                Assert.IsTrue(PublishTestStatus(sink, "StatusSummary", LogEventLevel.Debug));
                Assert.IsFalse(capture.Events.Last().Properties.ContainsKey("RelayPendingEvents"));
                Assert.AreEqual(-1, InvokePrivateMethod<int>(sink, "TryGetSpoolUtilizationPercent"));
                SetPrivateField(sink, "_spoolUnavailable", 0);
                SetPrivateField(sink, "_spoolDisabledForLifetime", true);
                Assert.IsTrue(PublishTestStatus(sink, "StatusSummary", LogEventLevel.Debug));
                Assert.AreEqual(-1, InvokePrivateMethod<int>(sink, "TryGetSpoolUtilizationPercent"));
                SetPrivateField(sink, "_spoolDisabledForLifetime", false);
                SetPrivateField(sink, "_connectionString", $"Data Source={Path.Combine(directory, "absent", "bad.db")}");
                Assert.IsTrue(PublishTestStatus(sink, "StatusSummary", LogEventLevel.Debug));
                SetPrivateField(sink, "_spoolUnavailable", 0);
                Assert.AreEqual(-1, InvokePrivateMethod<int>(sink, "TryGetSpoolUtilizationPercent"));
            }
            finally { DeleteTemporaryDirectory(directory); }
        }

        [TestMethod]
        public async Task StatusAdmissionDoesNotEvictApplicationEventsOrTriggerItsOwnBufferPressure()
        {
            string directory = CreateTemporaryDirectory();
            try
            {
                var options = new SerilogRelayOptions(); options.EmergencyMemoryBuffer.MaxBufferedEvents = 10;
                await using var sink = new SerilogRelaySink($"Data Source={Path.Combine(directory, "admission.db")}", null, options);
                await StopRelayWorkers(sink);
                SetPrivateField(sink, "_spoolDisabledForLifetime", true);
                SetPrivateField(sink, "_statusEventMode", SerilogRelayStatusEventMode.RelayOnly);
                for (int i = 0; i < 8; i++) Assert.IsTrue(PublishTestStatus(sink, "BufferedStatus"));
                Assert.IsFalse(GetPrivateField<bool>(sink, "_emergencyPressureActive"));
                Assert.AreEqual(0L, GetPrivateField<long>(sink, "_emergencyApplicationEventCount"));
                sink.Emit(CreateLogEvent("application one")); sink.Emit(CreateLogEvent("application two"));
                Assert.IsFalse(PublishTestStatus(sink, "RejectedStatus"));
                Assert.AreEqual(10L, GetPrivateField<long>(sink, "_emergencyBufferedCount"));
                Assert.AreEqual(0L, GetPrivateField<long>(sink, "_emergencyDroppedCount"));
                sink.Emit(CreateLogEvent("application three"));
                Assert.AreEqual(1L, GetPrivateField<long>(sink, "_statusEmergencyDroppedCount"));
                Assert.AreEqual(3L, GetPrivateField<long>(sink, "_emergencyApplicationEventCount"));
                for (int i = 0; i < 7; i++) sink.Emit(CreateLogEvent("more application logs"));
                Assert.IsTrue(GetPrivateField<bool>(sink, "_emergencyPressureActive"));
                Assert.IsFalse(PublishTestStatus(sink, "StillRejected"));
            }
            finally { DeleteTemporaryDirectory(directory); }
        }

        [TestMethod]
        public async Task StatusWorkerSuppressesMisleadingStartupAndRetriesRejectedTransition()
        {
            string directory = CreateTemporaryDirectory();
            try
            {
                await using var sink = new SerilogRelaySink($"Data Source={Path.Combine(directory, "recovery.db")}", null, new SerilogRelayOptions());
                var clock = new ManualRelayTimeProvider(); var capture = new StatusCaptureSink();
                using Logger logger = new LoggerConfiguration().MinimumLevel.Verbose().WriteTo.Sink(capture).CreateLogger();
                bool available = false;
                ConfigureTestStatusWorker(sink, clock, () => available ? logger : null, null);
                InvokePrivateMethod<object?>(sink, "MarkSpoolUnavailable", new IOException("failed before startup publication"));
                InvokePrivateMethod<object?>(sink, "MarkSpoolRecovered");
                StartTestStatusWorker(sink);
                Assert.AreEqual(0, capture.Events.Count);
                available = true; clock.Advance(TimeSpan.FromSeconds(5)); WakeTestStatusWorker(sink);
                await WaitUntilAsync(() => capture.Events.Count == 2, TimeSpan.FromSeconds(5));
                CollectionAssert.AreEqual(new[] { "SpoolUnavailable", "SpoolRecovered" }, capture.Events.Select(StatusCode).ToArray());
                Assert.AreEqual(1L, Scalar<long>(capture.Events.Last(), "RelaySpoolFailureCount"));
            }
            finally { DeleteTemporaryDirectory(directory); }
        }

        [TestMethod]
        public async Task StatusWorkerRetriesBothLossReportsAndObservesSpoolPressureHysteresis()
        {
            string directory = CreateTemporaryDirectory();
            try
            {
                await using var sink = new SerilogRelaySink($"Data Source={Path.Combine(directory, "pressure.db")}", null, new SerilogRelayOptions());
                var clock = new ManualRelayTimeProvider(); var capture = new StatusCaptureSink();
                using Logger logger = new LoggerConfiguration().MinimumLevel.Verbose().WriteTo.Sink(capture).CreateLogger();
                bool available = true;
                ConfigureTestStatusWorker(sink, clock, () => available ? logger : null, null);
                SetPrivateField(sink, "_maxApplicationSpoolPhysicalBytes", 4096L);
                StartTestStatusWorker(sink);
                await WaitUntilAsync(() => capture.Events.Count == 2, TimeSpan.FromSeconds(5));
                Assert.AreEqual("SpoolNearLimit", StatusCode(capture.Events.Last()));
                available = false; SetPrivateField(sink, "_applicationSpoolDroppedCount", 1L); WakeTestStatusWorker(sink);
                await Task.Delay(30);
                available = true; clock.Advance(TimeSpan.FromSeconds(5)); WakeTestStatusWorker(sink);
                await WaitUntilAsync(() => capture.Events.Count == 3, TimeSpan.FromSeconds(5));
                Assert.AreEqual("SpoolEventsDropped", StatusCode(capture.Events.Last()));
                available = false; SetPrivateField(sink, "_emergencyDroppedCount", 1L); WakeTestStatusWorker(sink);
                await Task.Delay(30);
                available = true; clock.Advance(TimeSpan.FromSeconds(5)); WakeTestStatusWorker(sink);
                await WaitUntilAsync(() => capture.Events.Count == 4, TimeSpan.FromSeconds(5));
                Assert.AreEqual("EmergencyEventsDropped", StatusCode(capture.Events.Last()));
                SetPrivateField(sink, "_maxApplicationSpoolPhysicalBytes", 64L * 1024 * 1024);
                clock.Advance(TimeSpan.FromSeconds(30)); WakeTestStatusWorker(sink);
                await WaitUntilAsync(() => capture.Events.Count == 5, TimeSpan.FromSeconds(5));
                Assert.AreEqual("SpoolPressureRelieved", StatusCode(capture.Events.Last()));
            }
            finally { DeleteTemporaryDirectory(directory); }
        }

        [TestMethod]
        public async Task StatusAllSinksCanUseApplicationLoggerIncludingRelayWithoutRecursion()
        {
            string directory = CreateTemporaryDirectory();
            try
            {
                string connectionString = $"Data Source={Path.Combine(directory, "all.db")}";
                await using var sink = new SerilogRelaySink(connectionString, null, new SerilogRelayOptions());
                var capture = new StatusCaptureSink();
                using Logger logger = new LoggerConfiguration().MinimumLevel.Verbose().WriteTo.Sink(capture).WriteTo.Sink(sink).CreateLogger();
                ConfigureTestStatusWorker(sink, new ManualRelayTimeProvider(), () => logger, null);
                InvokePrivateMethod<object?>(sink, "RecordEndpointDeliveryState", true);
                StartTestStatusWorker(sink);
                await WaitUntilAsync(() => capture.Events.Count == 2, TimeSpan.FromSeconds(5));
                Assert.AreEqual(2L, GetUnsentCount(connectionString));
                Assert.AreEqual(1L, Scalar<long>(capture.Events.Last(), "RelayEndpointFailureCount"));
            }
            finally { DeleteTemporaryDirectory(directory); }
        }

        private static async Task StopRelayWorkers(SerilogRelaySink sink)
        {
            GetPrivateField<CancellationTokenSource>(sink, "_cts").Cancel();
            try
            {
                await Task.WhenAll(GetPrivateField<Task>(sink, "_senderTask"), GetPrivateField<Task>(sink, "_emergencyTask"), GetPrivateField<Task>(sink, "_applicationSpoolMaintenanceTask"));
            }
            catch (OperationCanceledException) { }
        }

        [TestMethod]
        public async Task StatusWorkerKeepsLossReportDeadlinesAndSummarySeparate()
        {
            string directory = CreateTemporaryDirectory();
            try
            {
                await using var sink = new SerilogRelaySink($"Data Source={Path.Combine(directory, "deadlines.db")}", null, new SerilogRelayOptions());
                var clock = new ManualRelayTimeProvider(); var capture = new StatusCaptureSink();
                using Logger logger = new LoggerConfiguration().MinimumLevel.Verbose().WriteTo.Sink(capture).CreateLogger();
                ConfigureTestStatusWorker(sink, clock, () => logger, TimeSpan.FromMinutes(1));
                StartTestStatusWorker(sink);
                clock.Advance(TimeSpan.FromSeconds(30)); WakeTestStatusWorker(sink);
                await Task.Delay(30); // At t=30, the next spool check and summary share t=60.
                clock.Advance(TimeSpan.FromSeconds(5));
                SetPrivateField(sink, "_applicationSpoolDroppedCount", 1L); WakeTestStatusWorker(sink);
                await WaitUntilAsync(() => capture.Events.Any(e => StatusCode(e) == "SpoolEventsDropped"), TimeSpan.FromSeconds(5));
                clock.Advance(TimeSpan.FromSeconds(5));
                SetPrivateField(sink, "_emergencyDroppedCount", 1L); WakeTestStatusWorker(sink);
                await WaitUntilAsync(() => capture.Events.Any(e => StatusCode(e) == "EmergencyEventsDropped"), TimeSpan.FromSeconds(5));
                SetPrivateField(sink, "_applicationSpoolDroppedCount", 2L);
                SetPrivateField(sink, "_emergencyDroppedCount", 2L);
                clock.Advance(TimeSpan.FromSeconds(50)); WakeTestStatusWorker(sink);
                await WaitUntilAsync(() => capture.Events.Any(e => StatusCode(e) == "StatusSummary"), TimeSpan.FromSeconds(5));
                Assert.AreEqual(1, capture.Events.Count(e => StatusCode(e) == "SpoolEventsDropped"));
                clock.Advance(TimeSpan.FromSeconds(6)); WakeTestStatusWorker(sink);
                await WaitUntilAsync(() => capture.Events.Count(e => StatusCode(e) == "SpoolEventsDropped") == 2, TimeSpan.FromSeconds(5));
                Assert.AreEqual(1, capture.Events.Count(e => StatusCode(e) == "EmergencyEventsDropped"));
                clock.Advance(TimeSpan.FromSeconds(5)); WakeTestStatusWorker(sink);
                await WaitUntilAsync(() => capture.Events.Count(e => StatusCode(e) == "EmergencyEventsDropped") == 2, TimeSpan.FromSeconds(5));
            }
            finally { DeleteTemporaryDirectory(directory); }
        }

        [TestMethod]
        [DataRow(true)]
        [DataRow(false)]
        public async Task SpoolPressureRemainsActiveWhenUsageIsUnknownOrInsideTheHysteresisBand(bool unavailable)
        {
            string directory = CreateTemporaryDirectory();
            try
            {
                string connectionString = $"Data Source={Path.Combine(directory, "pressure.db")}";
                await using var sink = new SerilogRelaySink(connectionString, null, new SerilogRelayOptions());
                using (var connection = new SqliteConnection(connectionString))
                {
                    connection.Open();
                    using var command = connection.CreateCommand();
                    command.CommandText = "SELECT (page_count - freelist_count) * page_size FROM pragma_page_size(), pragma_page_count(), pragma_freelist_count();";
                    long usedBytes = (long)command.ExecuteScalar()!;
                    SetPrivateField(sink, "_maxApplicationSpoolPhysicalBytes", usedBytes * 100 / 70);
                }
                SetPrivateField(sink, "_spoolBudgetPressureActive", true);
                SetPrivateField(sink, "_spoolUnavailable", unavailable ? 1 : 0);
                SetPrivateField(sink, "_lastSpoolUtilizationPercent", -2);
                var capture = new StatusCaptureSink();
                using Logger logger = new LoggerConfiguration().MinimumLevel.Verbose().WriteTo.Sink(capture).CreateLogger();
                ConfigureTestStatusWorker(sink, new ManualRelayTimeProvider(), () => logger, null);
                StartTestStatusWorker(sink);

                await WaitUntilAsync(() => GetPrivateField<int>(sink, "_lastSpoolUtilizationPercent") != -2, TimeSpan.FromSeconds(5));
                Assert.AreEqual(unavailable ? -1 : 70, GetPrivateField<int>(sink, "_lastSpoolUtilizationPercent"));
                Assert.IsTrue(GetPrivateField<bool>(sink, "_spoolBudgetPressureActive"));
                Assert.IsFalse(capture.Events.Any(e => StatusCode(e) == "SpoolPressureRelieved"));
                await DisposeAndWaitForCleanupAsync(sink);
            }
            finally { DeleteTemporaryDirectory(directory); }
        }

        [TestMethod]
        public async Task StatusWorkerHandlesElapsedWakeDeadlineFailureAndStopBeforeStart()
        {
            string directory = CreateTemporaryDirectory();
            try
            {
                await using var sink = new SerilogRelaySink($"Data Source={Path.Combine(directory, "elapsed.db")}", null, new SerilogRelayOptions());
                var clock = new ManualRelayTimeProvider(); var capture = new StatusCaptureSink();
                using Logger logger = new LoggerConfiguration().MinimumLevel.Verbose().WriteTo.Sink(capture).CreateLogger();
                bool first = true;
                ConfigureTestStatusWorker(sink, clock, () =>
                {
                    if (first) { first = false; clock.Advance(TimeSpan.FromSeconds(31)); }
                    return logger;
                }, null);
                StartTestStatusWorker(sink);
                await Task.Delay(30);
                Assert.AreEqual(1, capture.Events.Count);
                SetPrivateField(sink, "_disposeStarted", 1); WakeTestStatusWorker(sink);
                await GetPrivateField<Task>(sink, "_statusTask").WaitAsync(TimeSpan.FromSeconds(5));
                await InvokePrivateTaskMethod(sink, "StatusLoopAsync");
                SetPrivateField(sink, "_disposeStarted", 0);
                GetPrivateField<SemaphoreSlim>(sink, "_statusSignal").Dispose();
                await InvokePrivateTaskMethod(sink, "StatusLoopAsync"); // Unexpected reporting failures remain on SelfLog.
            }
            finally { DeleteTemporaryDirectory(directory); }
        }

        [TestMethod]
        public async Task StatusRejectionFromPublicLoggerIsCountedButDoesNotEvictOrReportItself()
        {
            string directory = CreateTemporaryDirectory();
            try
            {
                var options = new SerilogRelayOptions(); options.EmergencyMemoryBuffer.MaxBufferedEvents = 1;
                await using var sink = new SerilogRelaySink($"Data Source={Path.Combine(directory, "public.db")}", null, options);
                await StopRelayWorkers(sink);
                SetPrivateField(sink, "_spoolDisabledForLifetime", true);
                SetPrivateField(sink, "_statusEventMode", SerilogRelayStatusEventMode.RelayOnly);
                sink.Emit(CreateLogEvent("application"));
                LogEvent status = CreateLogEvent("status");
                status.AddOrUpdateProperty(new LogEventProperty("SerilogRelayStatus", new ScalarValue(true)));
                status.AddOrUpdateProperty(new LogEventProperty("SourceContext", new ScalarValue("Eigenverft.NetLib.SerilogRelay")));
                status.AddOrUpdateProperty(new LogEventProperty("RelayStatusCode", new ScalarValue("Test")));
                sink.Emit(status);
                Assert.AreEqual(1L, GetPrivateField<long>(sink, "_emergencyBufferedCount"));
                Assert.AreEqual(1L, GetPrivateField<long>(sink, "_statusEmergencyDroppedCount"));
                Assert.AreEqual(1L, GetPrivateField<long>(sink, "_emergencyApplicationEventCount"));
            }
            finally { DeleteTemporaryDirectory(directory); }
        }

        private static bool PublishTestStatus(SerilogRelaySink sink, string code, LogEventLevel level = LogEventLevel.Warning)
            => InvokePrivateMethod<bool>(sink, "PublishStatus", level, code, "Relay test status", null, null, null);

        private static string StatusCode(LogEvent logEvent) => Scalar<string>(logEvent, "RelayStatusCode");
        private static T Scalar<T>(LogEvent logEvent, string property) => (T)((ScalarValue)logEvent.Properties[property]).Value!;
        private static void RecordTestTransition(SerilogRelaySink sink, string kind)
        {
            Type type = typeof(SerilogRelaySink).GetNestedType("StatusTransitionKind", BindingFlags.NonPublic)!;
            InvokePrivateMethod<object?>(sink, "RecordStatusTransition", Enum.Parse(type, kind));
        }
        private static void ConfigureTestStatusWorker(SerilogRelaySink sink, TimeProvider clock, Func<ILogger?> logger, TimeSpan? summary)
        {
            SetPrivateField(sink, "_timeProvider", clock);
            SetPrivateField(sink, "_statusEventMode", SerilogRelayStatusEventMode.AllSinks);
            SetPrivateField(sink, "_statusMinimumLevel", LogEventLevel.Verbose);
            SetPrivateField(sink, "_statusLoggerProvider", logger);
            SetPrivateField(sink, "_statusSummaryInterval", summary);
        }
        private static void StartTestStatusWorker(SerilogRelaySink sink)
            => SetPrivateField(sink, "_statusTask", InvokePrivateTaskMethod(sink, "StatusLoopAsync"));
        private static void WakeTestStatusWorker(SerilogRelaySink sink) => InvokePrivateMethod<object?>(sink, "WakeStatusLoop");
        private sealed class StatusCaptureSink : ILogEventSink
        {
            internal ConcurrentQueue<LogEvent> Events { get; } = new ConcurrentQueue<LogEvent>();
            public void Emit(LogEvent logEvent) => Events.Enqueue(logEvent);
        }
    }
}
