using System;
using System.Collections;
using System.Collections.Concurrent;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.Data.Sqlite;
using Microsoft.VisualStudio.TestTools.UnitTesting;

using Serilog.Core;
using Serilog.Debugging;
using Serilog.Events;

namespace Eigenverft.NetLib.SerilogRelay.Tests
{
    public sealed partial class SerilogRelayReliabilityTests
    {
        [TestMethod]
        public void RetryAndStatusValidationRejectNonfiniteAndUnsupportedDurations()
        {
            foreach (double value in new[] { double.NaN, double.PositiveInfinity, double.NegativeInfinity })
            {
                Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => new RetryGate(new EndpointRetryOptions { Multiplier = value }));
                Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => new RetryGate(new EndpointRetryOptions { JitterRatio = value }));
            }
            var retryOptions = new SerilogRelayOptions(); retryOptions.EndpointRetry.MaximumDelay = TimeSpan.FromDays(100);
            Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => new SerilogRelaySink("Data Source=:memory:", null, retryOptions));
            var statusOptions = new SerilogRelayOptions(); statusOptions.StatusEvents.SummaryInterval = TimeSpan.FromDays(100);
            Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => new SerilogRelaySink("Data Source=:memory:", null, statusOptions));
        }

        [TestMethod]
        public void StatusMarkerRequiresAllThreePropertiesAndCorrectScalarTypes()
        {
            MethodInfo check = typeof(SerilogRelaySink).GetMethod("IsRelayStatusEvent", BindingFlags.Static | BindingFlags.NonPublic)!;
            LogEvent logEvent = CreateLogEvent("test");
            Assert.IsFalse((bool)check.Invoke(null, new object[] { logEvent })!);
            logEvent.AddOrUpdateProperty(new LogEventProperty("SerilogRelayStatus", new ScalarValue(false)));
            Assert.IsFalse((bool)check.Invoke(null, new object[] { logEvent })!);
            logEvent.AddOrUpdateProperty(new LogEventProperty("SerilogRelayStatus", new SequenceValue(Array.Empty<LogEventPropertyValue>())));
            Assert.IsFalse((bool)check.Invoke(null, new object[] { logEvent })!);
            logEvent.AddOrUpdateProperty(new LogEventProperty("SerilogRelayStatus", new ScalarValue(true)));
            Assert.IsFalse((bool)check.Invoke(null, new object[] { logEvent })!);
            logEvent.AddOrUpdateProperty(new LogEventProperty("SourceContext", new ScalarValue("application")));
            Assert.IsFalse((bool)check.Invoke(null, new object[] { logEvent })!);
            logEvent.AddOrUpdateProperty(new LogEventProperty("SourceContext", new SequenceValue(Array.Empty<LogEventPropertyValue>())));
            Assert.IsFalse((bool)check.Invoke(null, new object[] { logEvent })!);
            logEvent.AddOrUpdateProperty(new LogEventProperty("SourceContext", new ScalarValue("Eigenverft.NetLib.SerilogRelay")));
            Assert.IsFalse((bool)check.Invoke(null, new object[] { logEvent })!);
            logEvent.AddOrUpdateProperty(new LogEventProperty("RelayStatusCode", new ScalarValue("Test")));
            Assert.IsTrue((bool)check.Invoke(null, new object[] { logEvent })!);
        }

        [TestMethod]
        public async Task CorruptionFlagSuppressesPendingReadsAndPersistenceNotifications()
        {
            string directory = CreateTemporaryDirectory();
            try
            {
                await using var sink = new SerilogRelaySink($"Data Source={Path.Combine(directory, "state.db")}", null, new SerilogRelayOptions());
                await StopRelayWorkers(sink);
                sink.Emit(CreateLogEvent("retained"));
                SetPrivateField(sink, "_spoolDisabledForLifetime", true);
                Assert.AreEqual(0L, InvokePrivateMethod<long>(sink, "RefreshClaimablePendingState", DateTimeOffset.UtcNow));
                InvokePrivateMethod<object?>(sink, "ApplyDeferredUnsentCleanup");
                InvokePrivateMethod<object?>(sink, "OnPersistedToSpool");
                InvokePrivateMethod<object?>(sink, "MarkSpoolRecovered");
                SetPrivateField(sink, "_spoolDisabledForLifetime", false);
                SetPrivateField(sink, "_pendingCount", 0L);
                RunWithSignalLock(sink, () => InvokePrivateMethod<long>(sink, "RefreshClaimablePendingState", DateTimeOffset.UtcNow),
                    () => SetPrivateField(sink, "_spoolDisabledForLifetime", true));
                SetPrivateField(sink, "_spoolDisabledForLifetime", false);
                RunWithSignalLock(sink, () => InvokePrivateMethod<object?>(sink, "OnPersistedToSpool"),
                    () => SetPrivateField(sink, "_spoolDisabledForLifetime", true));
                Assert.AreEqual(0L, GetPrivateField<long>(sink, "_pendingCount"));
            }
            finally { DeleteTemporaryDirectory(directory); }
        }

        [TestMethod]
        public async Task EventCommittedBeforePendingRefreshFailureIsNeverBufferedAgain()
        {
            string directory = CreateTemporaryDirectory();
            try
            {
                string connectionString = $"Data Source={Path.Combine(directory, "commit.db")}";
                await using var sink = new SerilogRelaySink(connectionString, null, new SerilogRelayOptions());
                await StopRelayWorkers(sink);
                object transitionLock = GetPrivateField<object>(sink, "_statusTransitionLock");
                Task emit;
                lock (transitionLock)
                {
                    emit = Task.Run(() => sink.Emit(CreateLogEvent("already durable")));
                    Assert.IsTrue(SpinWait.SpinUntil(() => GetUnsentCount(connectionString) == 1, TimeSpan.FromSeconds(5)));
                    SetPrivateField(sink, "_pendingCountNeedsRefresh", 1);
                    SetPrivateField(sink, "_connectionString", $"Data Source={Path.Combine(directory, "missing", "bad.db")}");
                }
                await emit;
                Assert.AreEqual(1L, GetUnsentCount(connectionString));
                Assert.AreEqual(0L, GetPrivateField<long>(sink, "_emergencyBufferedCount"));
                Assert.AreEqual(1, GetPrivateField<int>(sink, "_pendingCountNeedsRefresh"));
                SetPrivateField(sink, "_connectionString", connectionString);
                sink.Emit(CreateLogEvent("after recovery"));
                Assert.AreEqual(2L, GetPrivateField<long>(sink, "_pendingCount"));
            }
            finally { DeleteTemporaryDirectory(directory); }
        }

        [TestMethod]
        public async Task StatusWakeHandlesConcurrentProducersAndLateDisposal()
        {
            string directory = CreateTemporaryDirectory();
            try
            {
                await using var sink = new SerilogRelaySink($"Data Source={Path.Combine(directory, "wake.db")}", null, new SerilogRelayOptions());
                await StopRelayWorkers(sink);
                SetPrivateField(sink, "_statusEventMode", SerilogRelayStatusEventMode.AllSinks);
                Assert.IsFalse(PublishTestStatus(sink, "NoProvider"));
                SemaphoreSlim signal = GetPrivateField<SemaphoreSlim>(sink, "_statusSignal");
                Parallel.For(0, 2000, _ => { signal.Wait(0); WakeTestStatusWorker(sink); });
                SetPrivateField(sink, "_disposeStarted", 1); signal.Wait(0); signal.Dispose();
                WakeTestStatusWorker(sink);
                SetPrivateField(sink, "_disposeStarted", 0);
                await InvokePrivateTaskMethod(sink, "StatusLoopAsync");
                Type transitionType = typeof(SerilogRelaySink).GetNestedType("StatusTransition", BindingFlags.NonPublic)!;
                Type kindType = typeof(SerilogRelaySink).GetNestedType("StatusTransitionKind", BindingFlags.NonPublic)!;
                object invalid = Activator.CreateInstance(transitionType, new object[] { Enum.ToObject(kindType, 99), DateTimeOffset.UtcNow })!;
                TargetInvocationException error = Assert.ThrowsExactly<TargetInvocationException>(() => InvokePrivateMethod<bool>(sink, "PublishStatusTransition", invalid));
                Assert.IsInstanceOfType<ArgumentOutOfRangeException>(error.InnerException);
            }
            finally { DeleteTemporaryDirectory(directory); }
        }

        [TestMethod]
        [DataRow("io")]
        [DataRow("access")]
        [DataRow("unknown")]
        public async Task EmitKeepsObservedStorageFailureSeparateFromDiagnosticFailure(string failureKind)
        {
            string directory = CreateTemporaryDirectory();
            try
            {
                await using var sink = new SerilogRelaySink($"Data Source={Path.Combine(directory, "observed.db")}", null, new SerilogRelayOptions());
                await StopRelayWorkers(sink);
                SetPrivateField(sink, "_connectionString", $"Data Source={Path.Combine(directory, "absent", "bad.db")}");
                int diagnostics = 0;
                SelfLog.Enable(_ =>
                {
                    if (Interlocked.Increment(ref diagnostics) != 1) return;
                    throw failureKind switch
                    {
                        "io" => new IOException("diagnostic IO failure"),
                        "access" => new UnauthorizedAccessException("diagnostic access failure"),
                        _ => new InvalidOperationException("unknown diagnostic failure"),
                    };
                });
                sink.Emit(CreateLogEvent("volatile event"));
                Assert.AreEqual(1L, GetPrivateField<long>(sink, "_emergencyBufferedCount"));
                Assert.AreEqual(1, GetPrivateField<int>(sink, "_spoolUnavailable"));
            }
            finally { SelfLog.Disable(); DeleteTemporaryDirectory(directory); }
        }

        [TestMethod]
        public async Task CorruptionDuringEmergencyPersistenceSkipsEveryFurtherSpoolRead()
        {
            string directory = CreateTemporaryDirectory();
            try
            {
                string path = Path.Combine(directory, "corrupt.db");
                await using var sink = new SerilogRelaySink($"Data Source={path}", null, new SerilogRelayOptions());
                await StopRelayWorkers(sink);
                LogEntry entry = InvokePrivateMethod<LogEntry>(sink, "CreateLogEntry", CreateLogEvent("emergency"), Guid.NewGuid().ToString(), false);
                Assert.IsTrue(InvokePrivateMethod<bool>(sink, "EnqueueEmergency", entry, null, true));
                object batch = InvokePrivateMethod<object>(sink, "TakeEmergencyBatch");
                SqliteConnection.ClearAllPools(); File.Delete(path + "-wal"); File.Delete(path + "-shm");
                File.WriteAllText(path, "corrupt SQLite file");
                Assert.IsFalse(await InvokePrivateTaskMethod<bool>(sink, "TryProcessEmergencyBatchAsync", batch, CancellationToken.None));
                Assert.IsTrue(GetPrivateField<bool>(sink, "_spoolDisabledForLifetime"));
                Assert.AreEqual(1, ((IList)batch).Count);
                Assert.IsTrue(File.Exists(Path.Combine(directory, "corrupt.g0001.db")));
                InvokePrivateMethod<object?>(sink, "ReturnEmergencyEntriesToRetry", batch);
            }
            finally { DeleteTemporaryDirectory(directory); }
        }

        [TestMethod]
        [DataRow(false)]
        [DataRow(true)]
        public async Task EmergencyBatchHandsRemainingEventsToHttpWhenSpoolAccessStopsBetweenEntries(bool shutdown)
        {
            string directory = CreateTemporaryDirectory();
            SerilogRelaySink? sink = null;
            try
            {
                string connectionString = $"Data Source={Path.Combine(directory, "handover.db")}";
                string? sentBody = null;
                using var client = new HttpClient(new RegressionHttpHandler(async (request, token) =>
                {
                    sentBody = await request.Content!.ReadAsStringAsync(token);
                    return new HttpResponseMessage(HttpStatusCode.NoContent);
                }));
                sink = new SerilogRelaySink(connectionString, null, new SerilogRelayOptions { HttpClient = client });
                await StopRelayWorkers(sink);
                SetPrivateField(sink, "_senderTask", Task.CompletedTask);
                LogEntry first = InvokePrivateMethod<LogEntry>(sink, "CreateLogEntry", CreateLogEvent("persisted first"), Guid.NewGuid().ToString(), false);
                LogEntry second = InvokePrivateMethod<LogEntry>(sink, "CreateLogEntry", CreateLogEvent("sent second"), Guid.NewGuid().ToString(), false);
                Assert.IsTrue(InvokePrivateMethod<bool>(sink, "EnqueueEmergency", first, new IOException("temporary write failure"), true));
                Assert.IsTrue(InvokePrivateMethod<bool>(sink, "EnqueueEmergency", second, null, true));
                SetPrivateField(sink, "_endpoint", "http://localhost/relay");
                object batch = InvokePrivateMethod<object>(sink, "TakeEmergencyBatch");
                Assert.AreEqual(2, ((IList)batch).Count);
                bool handoverObserved = false;
                // Recovery is reported after the first write commits, before the next entry.
                SelfLog.Enable(message =>
                {
                    if (!message.Contains("local spool recovered", StringComparison.Ordinal)) return;
                    handoverObserved = true;
                    if (shutdown) SetPrivateField(sink, "_disposeStarted", 1);
                    else SetPrivateField(sink, "_spoolDisabledForLifetime", true);
                });

                Assert.IsTrue(await InvokePrivateTaskMethod<bool>(sink, "TryProcessEmergencyBatchAsync", batch, CancellationToken.None));
                Assert.IsTrue(handoverObserved);
                Assert.AreEqual(1, ((IList)batch).Count);
                Assert.AreEqual(1L, GetUnsentCount(connectionString));
                Assert.IsNotNull(sentBody);
                StringAssert.Contains(sentBody, "sent second");
                Assert.IsFalse(sentBody.Contains("persisted first", StringComparison.Ordinal));
                InvokePrivateMethod<object?>(sink, "CompleteEmergencyEntry", ((IList)batch)[0], true);
                Assert.AreEqual(0L, GetPrivateField<long>(sink, "_emergencyBufferedCount"));
                Assert.AreEqual(0L, GetPrivateField<long>(sink, "_emergencyDroppedCount"));
            }
            finally
            {
                SelfLog.Disable();
                if (sink is not null)
                {
                    SetPrivateField(sink, "_endpoint", (string?)null);
                    await DisposeAndWaitForCleanupAsync(sink);
                }
                DeleteTemporaryDirectory(directory);
            }
        }

        [TestMethod]
        public async Task ShutdownStopsFurtherSpoolBatchesIfCorruptionIsObservedAfterAcknowledgment()
        {
            string directory = CreateTemporaryDirectory();
            SerilogRelaySink? sink = null;
            try
            {
                string connectionString = $"Data Source={Path.Combine(directory, "shutdown-corruption.db")}";
                int requests = 0;
                using var client = new HttpClient(new RegressionHttpHandler((_, _) =>
                {
                    requests++;
                    InvokePrivateMethod<object?>(sink!, "MarkSpoolUnavailable", new IOException("concurrent storage failure"));
                    return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NoContent));
                }));
                var options = new SerilogRelayOptions { HttpClient = client };
                options.Delivery.MinimumBatchEvents = 1;
                options.Delivery.MaximumBatchEvents = 1;
                sink = new SerilogRelaySink(connectionString, null, options);
                await StopRelayWorkers(sink);
                SetPrivateField(sink, "_senderTask", Task.CompletedTask);
                SetPrivateField(sink, "_emergencyTask", Task.CompletedTask);
                sink.Emit(CreateLogEvent("first shutdown batch"));
                sink.Emit(CreateLogEvent("preserved second batch"));
                SetPrivateField(sink, "_endpoint", "http://localhost/relay");
                SelfLog.Enable(message =>
                {
                    if (message.Contains("local spool recovered", StringComparison.Ordinal))
                        SetPrivateField(sink, "_spoolDisabledForLifetime", true);
                });

                await DisposeAndWaitForCleanupAsync(sink);
                Assert.IsTrue(GetPrivateField<bool>(sink, "_spoolDisabledForLifetime"));
                Assert.AreEqual(1, requests);
                Assert.AreEqual(1L, GetUnsentCount(connectionString));
                Assert.IsNull(GetClaimOwnerId(connectionString));
                Assert.ThrowsExactly<ObjectDisposedException>(() => _ = GetPrivateField<CancellationTokenSource>(sink, "_cts").Token);
            }
            finally
            {
                SelfLog.Disable();
                if (sink is not null) await DisposeAndWaitForCleanupAsync(sink);
                DeleteTemporaryDirectory(directory);
            }
        }

        [TestMethod]
        public async Task PendingDeliveryStopsWhenCorruptionFlagAppearsDuringClaim()
        {
            string directory = CreateTemporaryDirectory();
            try
            {
                await using var sink = new SerilogRelaySink($"Data Source={Path.Combine(directory, "midclaim.db")}", null, new SerilogRelayOptions());
                await StopRelayWorkers(sink); sink.Emit(CreateLogEvent("pending"));
                SetPrivateField(sink, "_endpoint", "http://localhost/relay");
                SetPrivateField(sink, "_timeProvider", new CorruptingClaimClock(() => SetPrivateField(sink, "_spoolDisabledForLifetime", true)));
                Assert.IsFalse(await InvokePrivateTaskMethod<bool>(sink, "ProcessPendingAsync", true, CancellationToken.None, false));
                Assert.AreEqual(0L, GetPrivateField<long>(sink, "_lastHttpAttemptStartedTimestamp"));
            }
            finally { DeleteTemporaryDirectory(directory); }
        }

        [TestMethod]
        public async Task StatusCannotReclaimUnsentRowsFromFullSpool()
        {
            string directory = CreateTemporaryDirectory();
            try
            {
                string connectionString = $"Data Source={Path.Combine(directory, "full.db")}";
                var options = new SerilogRelayOptions(); options.ApplicationSpool.MaxPhysicalBytes = 64 * 1024;
                await using var sink = new SerilogRelaySink(connectionString, null, options);
                await StopRelayWorkers(sink);
                using (var connection = new SqliteConnection(connectionString))
                { connection.Open(); FillLegacySpool(connection, 64 * 1024, 500); }
                long before = GetUnsentCount(connectionString);
                LogEntry status = InvokePrivateMethod<LogEntry>(sink, "CreateLogEntry", CreateLogEvent(new string('s', 2000)), Guid.NewGuid().ToString(), true);
                Assert.IsFalse(InvokePrivateMethod<bool>(sink, "TryReclaimAndPersistApplicationSpoolCore", status));
                Assert.AreEqual(before, GetUnsentCount(connectionString));
                Assert.AreEqual(0L, GetPrivateField<long>(sink, "_applicationSpoolDroppedCount"));
            }
            finally { DeleteTemporaryDirectory(directory); }
        }

        [TestMethod]
        public async Task MissingBasePathMakesCorruptionRecoveryConservative()
        {
            string directory = CreateTemporaryDirectory();
            try
            {
                await using var sink = new SerilogRelaySink($"Data Source={Path.Combine(directory, "base.db")}", null, new SerilogRelayOptions());
                await StopRelayWorkers(sink); SetPrivateField(sink, "_baseDatabasePath", (string?)null);
                Assert.IsFalse(InvokePrivateMethod<bool>(sink, "TryRecoverCorruptedSpool", new SqliteException("corrupt", SQLitePCL.raw.SQLITE_CORRUPT)));
            }
            finally { DeleteTemporaryDirectory(directory); }
        }

        [TestMethod]
        public async Task ShutdownPreservesDiagnosticFailureAfterSuccessfulResourceCleanup()
        {
            string directory = CreateTemporaryDirectory();
            try
            {
                string connectionString = $"Data Source={Path.Combine(directory, "shutdown.db")}";
                var sink = new SerilogRelaySink(connectionString, null, new SerilogRelayOptions());
                await StopRelayWorkers(sink);
                SetPrivateField(sink, "_senderTask", Task.CompletedTask);
                SetPrivateField(sink, "_emergencyTask", Task.CompletedTask);
                SetPrivateField(sink, "_endpoint", "http://localhost/relay");
                using (var connection = new SqliteConnection(connectionString))
                { connection.Open(); using var command = connection.CreateCommand(); command.CommandText = "DROP TABLE SerilogRelayEvents"; command.ExecuteNonQuery(); }
                int diagnostics = 0;
                SelfLog.Enable(_ =>
                {
                    if (Interlocked.Increment(ref diagnostics) == 2)
                        throw new IOException("shutdown diagnostic output unavailable");
                });
                await Assert.ThrowsExactlyAsync<IOException>(async () => await sink.DisposeAsync());
                Assert.ThrowsExactly<ObjectDisposedException>(() => _ = GetPrivateField<CancellationTokenSource>(sink, "_cts").Token);
            }
            finally { SelfLog.Disable(); DeleteTemporaryDirectory(directory); }
        }

        private sealed class CorruptingClaimClock : TimeProvider
        {
            private readonly Action _onRead;
            internal CorruptingClaimClock(Action onRead) => _onRead = onRead;
            public override DateTimeOffset GetUtcNow() { _onRead(); return DateTimeOffset.UtcNow; }
        }

        private static void RunWithSignalLock(SerilogRelaySink sink, Action action, Action change)
        {
            Exception? failure = null;
            using var started = new ManualResetEventSlim();
            var thread = new Thread(() => { started.Set(); try { action(); } catch (Exception ex) { failure = ex; } });
            lock (GetPrivateField<object>(sink, "_signalLock"))
            {
                thread.Start(); started.Wait();
                Assert.IsTrue(SpinWait.SpinUntil(() => (thread.ThreadState & ThreadState.WaitSleepJoin) != 0, TimeSpan.FromSeconds(5)));
                change();
            }
            Assert.IsTrue(thread.Join(TimeSpan.FromSeconds(5)));
            Assert.IsNull(failure);
        }
    }
}
