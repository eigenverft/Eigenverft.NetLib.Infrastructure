using System;

using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Eigenverft.NetLib.SerilogRelay.Tests
{
    [TestClass]
    public sealed class SerilogRelayOptionsAndRetryGateTests
    {
        [TestMethod]
        public void OptionsExposeExpectedDefaultsAndRemainMutable()
        {
            var options = new SerilogRelayOptions();

            Assert.AreEqual(TimeSpan.FromDays(1), options.Spool.SentRetention);
            Assert.IsNull(options.Spool.UnsentMaxAge);
            Assert.AreEqual(64L * 1024L * 1024L, options.Spool.MaxBytes);
            Assert.AreEqual(20, options.Delivery.MinimumBatchEvents);
            Assert.AreEqual(100, options.Delivery.MaximumBatchEvents);
            Assert.AreEqual(TimeSpan.FromSeconds(5), options.Delivery.PollInterval);
            Assert.AreEqual(TimeSpan.FromSeconds(5), options.Delivery.MaximumBatchWait);
            Assert.AreEqual(TimeSpan.FromSeconds(5), options.Retry.InitialDelay);
            Assert.AreEqual(2d, options.Retry.Multiplier);
            Assert.AreEqual(TimeSpan.FromMinutes(5), options.Retry.MaximumDelay);
            Assert.AreEqual(0.2d, options.Retry.JitterRatio);
            Assert.IsTrue(options.Retry.RespectRetryAfter);
            Assert.AreEqual(16384, options.Emergency.MaxBufferedEvents);
            Assert.AreEqual(64L * 1024L * 1024L, options.Emergency.MaxBufferedPayloadBytes);

            options.Spool.SentRetention = TimeSpan.FromHours(12);
            options.Spool.UnsentMaxAge = TimeSpan.FromDays(30);
            options.Spool.MaxBytes = 8192;
            options.Delivery.MinimumBatchEvents = 3;
            options.Delivery.MaximumBatchEvents = 9;
            options.Delivery.PollInterval = TimeSpan.FromSeconds(2);
            options.Delivery.MaximumBatchWait = TimeSpan.FromSeconds(7);
            options.Retry.InitialDelay = TimeSpan.FromSeconds(1);
            options.Retry.Multiplier = 3d;
            options.Retry.MaximumDelay = TimeSpan.FromSeconds(30);
            options.Retry.JitterRatio = 0.1d;
            options.Retry.RespectRetryAfter = false;
            options.Emergency.MaxBufferedEvents = 123;
            options.Emergency.MaxBufferedPayloadBytes = 456;

            Assert.AreEqual(TimeSpan.FromHours(12), options.Spool.SentRetention);
            Assert.AreEqual(TimeSpan.FromDays(30), options.Spool.UnsentMaxAge);
            Assert.AreEqual(8192L, options.Spool.MaxBytes);
            Assert.AreEqual(3, options.Delivery.MinimumBatchEvents);
            Assert.AreEqual(9, options.Delivery.MaximumBatchEvents);
            Assert.AreEqual(TimeSpan.FromSeconds(2), options.Delivery.PollInterval);
            Assert.AreEqual(TimeSpan.FromSeconds(7), options.Delivery.MaximumBatchWait);
            Assert.AreEqual(TimeSpan.FromSeconds(1), options.Retry.InitialDelay);
            Assert.AreEqual(3d, options.Retry.Multiplier);
            Assert.AreEqual(TimeSpan.FromSeconds(30), options.Retry.MaximumDelay);
            Assert.AreEqual(0.1d, options.Retry.JitterRatio);
            Assert.IsFalse(options.Retry.RespectRetryAfter);
            Assert.AreEqual(123, options.Emergency.MaxBufferedEvents);
            Assert.AreEqual(456L, options.Emergency.MaxBufferedPayloadBytes);
        }

        [TestMethod]
        public void RetryGateRejectsInvalidConfiguration()
        {
            Assert.ThrowsExactly<ArgumentNullException>(() => new RetryGate(null!));

            Assert.ThrowsExactly<ArgumentOutOfRangeException>(() =>
                new RetryGate(new RetryOptions { InitialDelay = TimeSpan.Zero }));

            Assert.ThrowsExactly<ArgumentOutOfRangeException>(() =>
                new RetryGate(new RetryOptions { Multiplier = 0.5d }));

            Assert.ThrowsExactly<ArgumentOutOfRangeException>(() =>
                new RetryGate(new RetryOptions
                {
                    InitialDelay = TimeSpan.FromSeconds(10),
                    MaximumDelay = TimeSpan.FromSeconds(5),
                }));

            Assert.ThrowsExactly<ArgumentOutOfRangeException>(() =>
                new RetryGate(new RetryOptions { JitterRatio = -0.01d }));

            Assert.ThrowsExactly<ArgumentOutOfRangeException>(() =>
                new RetryGate(new RetryOptions { JitterRatio = 1.01d }));
        }

        [TestMethod]
        public void RetryGateControlsAttemptLifetimeBackoffJitterAndReset()
        {
            var options = new RetryOptions
            {
                InitialDelay = TimeSpan.FromSeconds(10),
                Multiplier = 2d,
                MaximumDelay = TimeSpan.FromSeconds(15),
                JitterRatio = 0.2d,
            };
            var gate = new RetryGate(options, nextDouble: () => 1d);
            DateTimeOffset now = new DateTimeOffset(2026, 9, 26, 12, 0, 0, TimeSpan.Zero);

            Assert.AreEqual(TimeSpan.Zero, gate.GetDelay(now));
            Assert.IsNull(gate.NextAttemptAt);
            Assert.AreEqual(0, gate.ConsecutiveFailures);

            Assert.IsTrue(gate.TryAcquire(now));
            Assert.IsFalse(gate.TryAcquire(now));
            gate.CancelAttempt();
            Assert.IsTrue(gate.TryAcquire(now));

            gate.RecordFailure(now, retryAfter: null);
            Assert.AreEqual(1, gate.ConsecutiveFailures);
            Assert.AreEqual(now + TimeSpan.FromSeconds(12), gate.NextAttemptAt);
            Assert.AreEqual(TimeSpan.FromSeconds(12), gate.GetDelay(now));
            Assert.IsFalse(gate.TryAcquire(now));
            Assert.AreEqual(TimeSpan.Zero, gate.GetDelay(now.AddMinutes(1)));

            Assert.IsTrue(gate.TryAcquire(now.AddMinutes(1)));
            gate.RecordFailure(now.AddMinutes(1), retryAfter: null);
            Assert.AreEqual(2, gate.ConsecutiveFailures);
            Assert.AreEqual(now.AddMinutes(1) + TimeSpan.FromSeconds(15), gate.NextAttemptAt);

            gate.RecordSuccess();
            Assert.AreEqual(0, gate.ConsecutiveFailures);
            Assert.IsNull(gate.NextAttemptAt);
            Assert.AreEqual(TimeSpan.Zero, gate.GetDelay(now));
        }

        [TestMethod]
        public void RetryGateHonorsRetryAfterOnlyWhenConfigured()
        {
            DateTimeOffset now = new DateTimeOffset(2026, 9, 26, 12, 0, 0, TimeSpan.Zero);
            DateTimeOffset serverNotBefore = now.AddMinutes(1);

            var respectingGate = new RetryGate(
                new RetryOptions
                {
                    InitialDelay = TimeSpan.FromSeconds(5),
                    MaximumDelay = TimeSpan.FromSeconds(5),
                    Multiplier = 1d,
                    JitterRatio = 0d,
                    RespectRetryAfter = true,
                },
                nextDouble: () => 0.5d);
            Assert.IsTrue(respectingGate.TryAcquire(now));
            respectingGate.RecordFailure(now, serverNotBefore);
            Assert.AreEqual(serverNotBefore, respectingGate.NextAttemptAt);

            var ignoringGate = new RetryGate(
                new RetryOptions
                {
                    InitialDelay = TimeSpan.FromSeconds(5),
                    MaximumDelay = TimeSpan.FromSeconds(5),
                    Multiplier = 1d,
                    JitterRatio = 0d,
                    RespectRetryAfter = false,
                },
                nextDouble: () => 0.5d);
            Assert.IsTrue(ignoringGate.TryAcquire(now));
            ignoringGate.RecordFailure(now, serverNotBefore);
            Assert.AreEqual(now.AddSeconds(5), ignoringGate.NextAttemptAt);
        }

        [TestMethod]
        public void SinkRejectsInvalidGroupedOptions()
        {
            AssertInvalid(options => options.Delivery.MinimumBatchEvents = 0, typeof(ArgumentOutOfRangeException));
            AssertInvalid(
                options =>
                {
                    options.Delivery.MinimumBatchEvents = 2;
                    options.Delivery.MaximumBatchEvents = 1;
                },
                typeof(ArgumentException));
            AssertInvalid(options => options.Delivery.PollInterval = TimeSpan.Zero, typeof(ArgumentOutOfRangeException));
            AssertInvalid(options => options.Delivery.MaximumBatchWait = TimeSpan.Zero, typeof(ArgumentOutOfRangeException));
            AssertInvalid(options => options.Spool.SentRetention = TimeSpan.FromSeconds(-1), typeof(ArgumentOutOfRangeException));
            AssertInvalid(options => options.Spool.UnsentMaxAge = TimeSpan.FromSeconds(-1), typeof(ArgumentOutOfRangeException));
            AssertInvalid(options => options.Spool.MaxBytes = 4095, typeof(ArgumentOutOfRangeException));
            AssertInvalid(options => options.Emergency.MaxBufferedEvents = 0, typeof(ArgumentOutOfRangeException));
            AssertInvalid(options => options.Emergency.MaxBufferedPayloadBytes = 0, typeof(ArgumentOutOfRangeException));
        }

        private static void AssertInvalid(Action<SerilogRelayOptions> configure, Type expectedExceptionType)
        {
            var options = new SerilogRelayOptions();
            configure(options);

            Exception? exception = null;
            try
            {
                using var sink = new SerilogRelaySink(
                    "Data Source=:memory:",
                    endpoint: null,
                    options);
            }
            catch (Exception ex)
            {
                exception = ex;
            }

            Assert.IsNotNull(exception);
            Assert.AreEqual(expectedExceptionType, exception.GetType());
        }
    }
}
