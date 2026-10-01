using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.VisualStudio.TestTools.UnitTesting;
using Microsoft.Data.Sqlite;

namespace Eigenverft.NetLib.SerilogRelay.Tests
{
    public sealed partial class SerilogRelayReliabilityTests
    {
        [TestMethod]
        public async Task SenderWaitReleasesEveryTimerWhenWokenOrStopped()
        {
            string directory = CreateTemporaryDirectory();
            try
            {
                using var sink = new SerilogRelaySink($"Data Source={Path.Combine(directory, "wait.db")}", null, new SerilogRelayOptions());
                var clock = new ManualRelayTimeProvider();
                SetPrivateField(sink, "_timeProvider", clock);
                for (int i = 0; i < 100; i++)
                {
                    SetPrivateField(sink, "_hasNewLogs", true);
                    await InvokePrivateTaskMethod(sink, "WaitForSenderDelayAsync", TimeSpan.FromDays(100), CancellationToken.None);
                    Assert.AreEqual(0, clock.ActiveTimers, "Early wake leaked its long deadline.");
                }

                Task wait = InvokePrivateTaskMethod(sink, "WaitForSenderDelayAsync", TimeSpan.FromMilliseconds(10), CancellationToken.None);
                Assert.AreEqual(2, clock.ActiveTimers);
                clock.Advance(TimeSpan.FromMilliseconds(10));
                await wait.WaitAsync(TimeSpan.FromSeconds(5));
                Assert.AreEqual(0, clock.ActiveTimers, "Deadline completion leaked its polling timer.");

                using var cancellation = new CancellationTokenSource();
                wait = InvokePrivateTaskMethod(sink, "WaitForSenderDelayAsync", TimeSpan.FromDays(1), cancellation.Token);
                cancellation.Cancel();
                await wait.WaitAsync(TimeSpan.FromSeconds(5));
                Assert.AreEqual(0, clock.ActiveTimers);

                SetPrivateField(sink, "_disposeStarted", 1);
                await InvokePrivateTaskMethod(sink, "WaitForSenderDelayAsync", TimeSpan.FromDays(1), CancellationToken.None);
                Assert.AreEqual(0, clock.ActiveTimers);
                SetPrivateField(sink, "_disposeStarted", 0);
            }
            finally { DeleteTemporaryDirectory(directory); }
        }

        [TestMethod]
        [DataRow(1.1d, 60)]
        [DataRow(1.01d, 500)]
        [DataRow(double.MaxValue, 4)]
        public void RetryBackoffReachesConfiguredMaximumBeyondThirtyFailures(double multiplier, int failures)
        {
            var gate = new RetryGate(new EndpointRetryOptions
            {
                InitialDelay = TimeSpan.FromSeconds(5),
                MaximumDelay = TimeSpan.FromMinutes(5),
                Multiplier = multiplier,
                JitterRatio = 0,
            });
            DateTimeOffset now = DateTimeOffset.UtcNow;
            for (int i = 0; i < failures; i++)
                gate.RecordFailure(now, null);
            Assert.AreEqual(TimeSpan.FromMinutes(5), gate.GetDelay(now));
            typeof(RetryGate).GetField("_consecutiveFailures", BindingFlags.Instance | BindingFlags.NonPublic)!
                .SetValue(gate, int.MaxValue);
            gate.RecordFailure(now, null);
            Assert.AreEqual(int.MaxValue, gate.ConsecutiveFailures);
            Assert.AreEqual(TimeSpan.FromMinutes(5), gate.GetDelay(now));
        }

        [TestMethod]
        public async Task ClaimClockStartsAfterDatabaseGateAndRenewalPreventsTakeoverDuringSend()
        {
            string directory = CreateTemporaryDirectory();
            try
            {
                string connectionString = $"Data Source={Path.Combine(directory, "lease.db")}";
                await using var first = new SerilogRelaySink(connectionString, null, new SerilogRelayOptions());
                await using var second = new SerilogRelaySink(connectionString, null, new SerilogRelayOptions());
                var clock = new ManualRelayTimeProvider();
                SetPrivateField(first, "_timeProvider", clock);
                SetPrivateField(second, "_timeProvider", clock);
                first.Emit(CreateLogEvent("leased event"));
                SemaphoreSlim gate = GetPrivateField<SemaphoreSlim>(first, "_databaseGate");
                await gate.WaitAsync();
                Task<ClaimedLogBatch> pending = InvokePrivateTaskMethod<ClaimedLogBatch>(first, "ClaimPendingAsync", 1, CancellationToken.None);
                clock.Advance(TimeSpan.FromSeconds(40));
                gate.Release();
                ClaimedLogBatch claimed = await pending;
                Assert.AreEqual(clock.GetUtcNow().AddSeconds(30).ToUnixTimeMilliseconds(), ReadLease(connectionString));
                clock.Advance(TimeSpan.FromSeconds(40));
                using var client = new HttpClient(new RegressionHttpHandler(async (_, token) =>
                {
                    Assert.AreEqual(clock.GetUtcNow().AddSeconds(30).ToUnixTimeMilliseconds(), ReadLease(connectionString));
                    ClaimedLogBatch competing = await InvokePrivateTaskMethod<ClaimedLogBatch>(second, "ClaimPendingAsync", 1, token);
                    Assert.AreEqual(0, competing.Entries.Count);
                    return new HttpResponseMessage(HttpStatusCode.OK);
                }));
                SetPrivateField(first, "_httpClient", client);
                SetPrivateField(first, "_endpoint", "http://localhost/relay");
                Assert.IsTrue(await InvokePrivateTaskMethod<bool>(first, "SendBatchAsync", claimed.Entries, CancellationToken.None, claimed));
                await InvokePrivateTaskMethod(first, "AcknowledgeClaimAsync", claimed, CancellationToken.None);
                Assert.AreEqual(0L, GetUnsentCount(connectionString));
                SetPrivateField(first, "_endpoint", (string?)null);
            }
            finally { DeleteTemporaryDirectory(directory); }
        }

        [TestMethod]
        public async Task LostPartialClaimSkipsHttpAndRollsBackRenewal()
        {
            string directory = CreateTemporaryDirectory();
            try
            {
                string connectionString = $"Data Source={Path.Combine(directory, "takeover.db")}";
                await using var first = new SerilogRelaySink(connectionString, null, new SerilogRelayOptions());
                await using var second = new SerilogRelaySink(connectionString, null, new SerilogRelayOptions());
                var clock = new ManualRelayTimeProvider();
                SetPrivateField(first, "_timeProvider", clock);
                SetPrivateField(second, "_timeProvider", clock);
                first.Emit(CreateLogEvent("one")); first.Emit(CreateLogEvent("two"));
                ClaimedLogBatch claimed = await InvokePrivateTaskMethod<ClaimedLogBatch>(first, "ClaimPendingAsync", 2, CancellationToken.None);
                long originalLease = ReadLease(connectionString);
                clock.Advance(TimeSpan.FromSeconds(40));
                ClaimedLogBatch competing = await InvokePrivateTaskMethod<ClaimedLogBatch>(second, "ClaimPendingAsync", 1, CancellationToken.None);
                int requests = 0;
                using var client = new HttpClient(new RegressionHttpHandler((_, _) =>
                { requests++; return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)); }));
                SetPrivateField(first, "_httpClient", client);
                SetPrivateField(first, "_endpoint", "http://localhost/relay");
                Assert.IsFalse(await InvokePrivateTaskMethod<bool>(first, "SendBatchAsync", claimed.Entries, CancellationToken.None, claimed));
                Assert.AreEqual(0, requests);
                using var connection = new SqliteConnection(connectionString);
                connection.Open();
                using var command = connection.CreateCommand();
                command.CommandText = "SELECT ClaimUntilUnixMs FROM SerilogRelayEvents WHERE Id = 2";
                Assert.AreEqual(originalLease, (long)command.ExecuteScalar()!);
                Assert.IsTrue(GetPrivateField<RetryGate>(first, "_retryGate").TryAcquire(DateTimeOffset.UtcNow));
                GetPrivateField<RetryGate>(first, "_retryGate").CancelAttempt();
                Assert.AreEqual(2L, GetUnsentCount(connectionString));
                await InvokePrivateTaskMethod(first, "ReleaseClaimAsync", claimed, CancellationToken.None);
                await InvokePrivateTaskMethod(second, "ReleaseClaimAsync", competing, CancellationToken.None);
                SetPrivateField(first, "_endpoint", (string?)null);
            }
            finally { DeleteTemporaryDirectory(directory); }
        }

        [TestMethod]
        [DataRow(31d, false)]
        [DataRow(28.95d, true)]
        public async Task SlowLeaseCommitSkipsExpiredLeaseOrShortensRequest(double delaySeconds, bool expectRequest)
        {
            string directory = CreateTemporaryDirectory();
            try
            {
                string connectionString = $"Data Source={Path.Combine(directory, "slow.db")}";
                await using var sink = new SerilogRelaySink(connectionString, null, new SerilogRelayOptions());
                sink.Emit(CreateLogEvent("slow commit"));
                ClaimedLogBatch claimed = await InvokePrivateTaskMethod<ClaimedLogBatch>(sink, "ClaimPendingAsync", 1, CancellationToken.None);
                int requests = 0;
                using var client = new HttpClient(new RegressionHttpHandler(async (_, token) =>
                { requests++; await Task.Delay(Timeout.Infinite, token); return new HttpResponseMessage(HttpStatusCode.OK); }));
                SetPrivateField(sink, "_httpClient", client);
                SetPrivateField(sink, "_endpoint", "http://localhost/relay");
                SetPrivateField(sink, "_timeProvider", new DelayedLeaseClock(TimeSpan.FromSeconds(delaySeconds)));
                Assert.IsFalse(await InvokePrivateTaskMethod<bool>(sink, "SendBatchAsync", claimed.Entries, CancellationToken.None, claimed)
                    .WaitAsync(TimeSpan.FromSeconds(1)));
                Assert.AreEqual(expectRequest ? 1 : 0, requests);
                SetPrivateField(sink, "_endpoint", (string?)null);
            }
            finally { DeleteTemporaryDirectory(directory); }
        }

        [TestMethod]
        public async Task LeaseStorageFailureReleasesGateWithoutReportingEndpointFailure()
        {
            string directory = CreateTemporaryDirectory();
            try
            {
                await using var sink = new SerilogRelaySink($"Data Source={Path.Combine(directory, "error.db")}", null, new SerilogRelayOptions());
                sink.Emit(CreateLogEvent("storage failure"));
                ClaimedLogBatch claimed = await InvokePrivateTaskMethod<ClaimedLogBatch>(sink, "ClaimPendingAsync", 1, CancellationToken.None);
                SetPrivateField(sink, "_endpoint", "http://localhost/relay");
                SetPrivateField(sink, "_connectionString", $"Data Source={Path.Combine(directory, "missing", "bad.db")}");
                await Assert.ThrowsExactlyAsync<SqliteException>(() => InvokePrivateTaskMethod<bool>(sink, "SendBatchAsync", claimed.Entries, CancellationToken.None, claimed));
                Assert.AreEqual(0, GetPrivateField<int>(sink, "_endpointFailureActive"));
                RetryGate gate = GetPrivateField<RetryGate>(sink, "_retryGate");
                Assert.AreEqual(0, gate.ConsecutiveFailures);
                Assert.IsTrue(gate.TryAcquire(DateTimeOffset.UtcNow)); gate.CancelAttempt();
                SetPrivateField(sink, "_endpoint", (string?)null);
            }
            finally { DeleteTemporaryDirectory(directory); }
        }

        private static long ReadLease(string connectionString)
        {
            using var connection = new SqliteConnection(connectionString); connection.Open();
            using var command = connection.CreateCommand(); command.CommandText = "SELECT ClaimUntilUnixMs FROM SerilogRelayEvents ORDER BY Id LIMIT 1";
            return (long)command.ExecuteScalar()!;
        }

        private sealed class RegressionHttpHandler : HttpMessageHandler
        {
            private readonly Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> _send;
            internal RegressionHttpHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send) => _send = send;
            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => _send(request, cancellationToken);
        }

        private sealed class DelayedLeaseClock : TimeProvider
        {
            private readonly DateTimeOffset _start = DateTimeOffset.UtcNow;
            private readonly TimeSpan _delay;
            private int _reads;
            internal DelayedLeaseClock(TimeSpan delay) => _delay = delay;
            public override DateTimeOffset GetUtcNow() => Interlocked.Increment(ref _reads) == 1 ? _start : _start + _delay;
        }

        private sealed class ManualRelayTimeProvider : TimeProvider
        {
            private readonly object _sync = new object();
            private readonly List<ManualTimer> _timers = new List<ManualTimer>();
            private DateTimeOffset _now = DateTimeOffset.UtcNow;
            public int ActiveTimers { get { lock (_sync) return _timers.Count; } }
            public override DateTimeOffset GetUtcNow() { lock (_sync) return _now; }
            public override long GetTimestamp() => GetUtcNow().Ticks;
            public override long TimestampFrequency => TimeSpan.TicksPerSecond;
            public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
            {
                var timer = new ManualTimer(this, callback, state);
                timer.Change(dueTime, period);
                return timer;
            }
            public void Advance(TimeSpan elapsed)
            {
                List<ManualTimer> due;
                lock (_sync)
                {
                    _now += elapsed;
                    due = _timers.Where(timer => timer.Due <= _now).ToList();
                    foreach (ManualTimer timer in due)
                        timer.Due = timer.Period == Timeout.InfiniteTimeSpan ? DateTimeOffset.MaxValue : _now + timer.Period;
                }
                foreach (ManualTimer timer in due) timer.Fire();
            }
            private sealed class ManualTimer : ITimer
            {
                private readonly ManualRelayTimeProvider _clock;
                private readonly TimerCallback _callback;
                private readonly object? _state;
                private bool _disposed;
                internal DateTimeOffset Due;
                internal TimeSpan Period;
                internal ManualTimer(ManualRelayTimeProvider clock, TimerCallback callback, object? state)
                { _clock = clock; _callback = callback; _state = state; }
                public bool Change(TimeSpan dueTime, TimeSpan period)
                {
                    lock (_clock._sync)
                    {
                        if (_disposed) return false;
                        Due = dueTime == Timeout.InfiniteTimeSpan ? DateTimeOffset.MaxValue : _clock._now + dueTime;
                        Period = period;
                        if (!_clock._timers.Contains(this)) _clock._timers.Add(this);
                        return true;
                    }
                }
                internal void Fire() { if (!_disposed) _callback(_state); }
                public void Dispose()
                { lock (_clock._sync) { _disposed = true; _clock._timers.Remove(this); } }
                public ValueTask DisposeAsync() { Dispose(); return ValueTask.CompletedTask; }
            }
        }
    }
}
