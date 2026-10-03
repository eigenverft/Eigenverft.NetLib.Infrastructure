using System;
using System.Reflection;
using System.Reflection.Emit;

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

            Assert.AreEqual(TimeSpan.Zero, options.ApplicationSpool.SentEventRetention);
            Assert.IsNull(options.ApplicationSpool.UnsentEventMaxAge);
            Assert.AreEqual(64L * 1024L * 1024L, options.ApplicationSpool.MaxPhysicalBytes);
            Assert.IsNull(options.HttpClient);
            Assert.AreEqual(TimeSpan.FromSeconds(2), options.Delivery.RequestTimeout);
            Assert.AreEqual(20, options.Delivery.MinimumBatchEvents);
            Assert.AreEqual(100, options.Delivery.MaximumBatchEvents);
            Assert.AreEqual(4 * 1024 * 1024, options.Delivery.TargetBatchPayloadBytes);
            Assert.AreEqual(256, options.Delivery.EmergencyMaximumBatchEvents);
            Assert.AreEqual(4 * 1024 * 1024, options.Delivery.EmergencyTargetBatchPayloadBytes);
            Assert.AreEqual(TimeSpan.FromSeconds(5), options.Delivery.PollInterval);
            Assert.AreEqual(TimeSpan.FromSeconds(5), options.Delivery.MaximumBatchWait);
            Assert.AreEqual(TimeSpan.FromSeconds(3), options.Delivery.ShutdownTimeout);
            Assert.AreEqual(TimeSpan.FromSeconds(1), options.Delivery.ShutdownRequestTimeout);
            Assert.AreEqual(TimeSpan.FromSeconds(1), options.Delivery.ShutdownRetryInterval);
            Assert.AreEqual(TimeSpan.FromSeconds(5), options.EndpointRetry.InitialDelay);
            Assert.AreEqual(2d, options.EndpointRetry.Multiplier);
            Assert.AreEqual(TimeSpan.FromMinutes(5), options.EndpointRetry.MaximumDelay);
            Assert.AreEqual(0.2d, options.EndpointRetry.JitterRatio);
            Assert.IsTrue(options.EndpointRetry.RespectRetryAfter);
            Assert.AreEqual(16384, options.EmergencyMemoryBuffer.MaxBufferedEvents);
            Assert.AreEqual(64L * 1024L * 1024L, options.EmergencyMemoryBuffer.MaxBufferedPayloadBytes);

            options.ApplicationSpool.SentEventRetention = TimeSpan.FromHours(12);
            options.ApplicationSpool.UnsentEventMaxAge = TimeSpan.FromDays(30);
            options.ApplicationSpool.MaxPhysicalBytes = 8192;
            options.Delivery.RequestTimeout = TimeSpan.FromSeconds(10);
            options.Delivery.MinimumBatchEvents = 3;
            options.Delivery.MaximumBatchEvents = 9;
            options.Delivery.TargetBatchPayloadBytes = 1024;
            options.Delivery.EmergencyMaximumBatchEvents = 17;
            options.Delivery.EmergencyTargetBatchPayloadBytes = 2048;
            options.Delivery.PollInterval = TimeSpan.FromSeconds(2);
            options.Delivery.MaximumBatchWait = TimeSpan.FromSeconds(7);
            options.Delivery.ShutdownTimeout = TimeSpan.FromMilliseconds(500);
            options.Delivery.ShutdownRequestTimeout = TimeSpan.FromMilliseconds(200);
            options.Delivery.ShutdownRetryInterval = TimeSpan.FromMilliseconds(100);
            options.EndpointRetry.InitialDelay = TimeSpan.FromSeconds(1);
            options.EndpointRetry.Multiplier = 3d;
            options.EndpointRetry.MaximumDelay = TimeSpan.FromSeconds(30);
            options.EndpointRetry.JitterRatio = 0.1d;
            options.EndpointRetry.RespectRetryAfter = false;
            options.EmergencyMemoryBuffer.MaxBufferedEvents = 123;
            options.EmergencyMemoryBuffer.MaxBufferedPayloadBytes = 456;

            Assert.AreEqual(TimeSpan.FromHours(12), options.ApplicationSpool.SentEventRetention);
            Assert.AreEqual(TimeSpan.FromDays(30), options.ApplicationSpool.UnsentEventMaxAge);
            Assert.AreEqual(8192L, options.ApplicationSpool.MaxPhysicalBytes);
            Assert.AreEqual(TimeSpan.FromSeconds(10), options.Delivery.RequestTimeout);
            Assert.AreEqual(3, options.Delivery.MinimumBatchEvents);
            Assert.AreEqual(9, options.Delivery.MaximumBatchEvents);
            Assert.AreEqual(1024, options.Delivery.TargetBatchPayloadBytes);
            Assert.AreEqual(17, options.Delivery.EmergencyMaximumBatchEvents);
            Assert.AreEqual(2048, options.Delivery.EmergencyTargetBatchPayloadBytes);
            Assert.AreEqual(TimeSpan.FromSeconds(2), options.Delivery.PollInterval);
            Assert.AreEqual(TimeSpan.FromSeconds(7), options.Delivery.MaximumBatchWait);
            Assert.AreEqual(TimeSpan.FromMilliseconds(500), options.Delivery.ShutdownTimeout);
            Assert.AreEqual(TimeSpan.FromMilliseconds(200), options.Delivery.ShutdownRequestTimeout);
            Assert.AreEqual(TimeSpan.FromMilliseconds(100), options.Delivery.ShutdownRetryInterval);
            Assert.AreEqual(TimeSpan.FromSeconds(1), options.EndpointRetry.InitialDelay);
            Assert.AreEqual(3d, options.EndpointRetry.Multiplier);
            Assert.AreEqual(TimeSpan.FromSeconds(30), options.EndpointRetry.MaximumDelay);
            Assert.AreEqual(0.1d, options.EndpointRetry.JitterRatio);
            Assert.IsFalse(options.EndpointRetry.RespectRetryAfter);
            Assert.AreEqual(123, options.EmergencyMemoryBuffer.MaxBufferedEvents);
            Assert.AreEqual(456L, options.EmergencyMemoryBuffer.MaxBufferedPayloadBytes);
        }

        [TestMethod]
        public void ApplicationVersionResolutionHandlesOverridesAndAssemblyMetadata()
        {
            Assembly withInformationalVersion = CreateAssembly(
                new Version(1, 2, 3, 4),
                "1.2.3+commit");
            Assert.AreEqual("1.2.3+commit", SerilogRelaySink.ResolveApplicationVersion(null, withInformationalVersion));
            Assert.AreEqual("manual", SerilogRelaySink.ResolveApplicationVersion(" manual ", withInformationalVersion));
            Assert.AreEqual(new string('v', 255), SerilogRelaySink.ResolveApplicationVersion(new string('v', 255), null));
            Assert.ThrowsExactly<ArgumentException>(() => SerilogRelaySink.ResolveApplicationVersion("  ", null));
            Assert.AreEqual(new string('v', 255), SerilogRelaySink.ResolveApplicationVersion(new string('v', 256), null));

            Assembly withoutInformationalVersion = CreateAssembly(new Version(4, 5, 6, 7), null);
            Assert.AreEqual("4.5.6.7", SerilogRelaySink.ResolveApplicationVersion(null, withoutInformationalVersion));
            Assembly withBlankInformationalVersion = CreateAssembly(new Version(4, 5), " ");
            Assert.AreEqual("4.5.0.0", SerilogRelaySink.ResolveApplicationVersion(null, withBlankInformationalVersion));
            Assert.IsNull(SerilogRelaySink.ResolveApplicationVersion(null, null));
            Assert.AreEqual("0.0.0.0", SerilogRelaySink.ResolveApplicationVersion(null, CreateAssembly(null, null)));
            Assert.IsNull(SerilogRelaySink.ResolveApplicationVersion(null, new VersionlessAssembly()));
            Assert.AreEqual(new string('v', 255), SerilogRelaySink.ResolveApplicationVersion(
                null,
                CreateAssembly(new Version(1, 0), new string('v', 257))));
            Assert.AreEqual("trimmed", SerilogRelaySink.ResolveApplicationVersion(
                null, CreateAssembly(new Version(1, 0), new string(' ', 300) + "trimmed ")));
        }

        [TestMethod]
        [DataRow(253, false)]
        [DataRow(254, false)]
        [DataRow(255, false)]
        [DataRow(253, true)]
        [DataRow(254, true)]
        [DataRow(255, true)]
        public void ApplicationVersionTruncationKeepsSurrogatePairsIntact(int prefixLength, bool fromAssembly)
        {
            string prefix = new string('v', prefixLength);
            string input = prefix + "\U0001F600suffix";
            string expected = prefixLength == 253 ? prefix + "\U0001F600" : prefix;
            string? actual = fromAssembly
                ? SerilogRelaySink.ResolveApplicationVersion(null, CreateAssembly(new Version(1, 0), input))
                : SerilogRelaySink.ResolveApplicationVersion(input, null);

            Assert.AreEqual(expected, actual);
            Assert.IsTrue(actual!.Length <= 255);
        }

        [TestMethod]
        public void RetryGateRejectsInvalidConfiguration()
        {
            Assert.ThrowsExactly<ArgumentNullException>(() => new RetryGate(null!));

            Assert.ThrowsExactly<ArgumentOutOfRangeException>(() =>
                new RetryGate(new EndpointRetryOptions { InitialDelay = TimeSpan.Zero }));

            Assert.ThrowsExactly<ArgumentOutOfRangeException>(() =>
                new RetryGate(new EndpointRetryOptions { Multiplier = 0.5d }));

            Assert.ThrowsExactly<ArgumentOutOfRangeException>(() =>
                new RetryGate(new EndpointRetryOptions
                {
                    InitialDelay = TimeSpan.FromSeconds(10),
                    MaximumDelay = TimeSpan.FromSeconds(5),
                }));

            Assert.ThrowsExactly<ArgumentOutOfRangeException>(() =>
                new RetryGate(new EndpointRetryOptions { JitterRatio = -0.01d }));

            Assert.ThrowsExactly<ArgumentOutOfRangeException>(() =>
                new RetryGate(new EndpointRetryOptions { JitterRatio = 1.01d }));
        }

        [TestMethod]
        public void RetryGateControlsAttemptLifetimeBackoffJitterAndReset()
        {
            var options = new EndpointRetryOptions
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
        public void RetryGateCapsRetryAfterAndIgnoresItWhenDisabled()
        {
            DateTimeOffset now = new DateTimeOffset(2026, 9, 26, 12, 0, 0, TimeSpan.Zero);
            DateTimeOffset serverNotBefore = now.AddYears(1);

            var respectingGate = new RetryGate(
                new EndpointRetryOptions
                {
                    InitialDelay = TimeSpan.FromSeconds(5),
                    MaximumDelay = TimeSpan.FromSeconds(15),
                    Multiplier = 1d,
                    JitterRatio = 0d,
                    RespectRetryAfter = true,
                },
                nextDouble: () => 0.5d);
            Assert.IsTrue(respectingGate.TryAcquire(now));
            respectingGate.RecordFailure(now, serverNotBefore);
            Assert.AreEqual(now.AddSeconds(15), respectingGate.NextAttemptAt);

            var ignoringGate = new RetryGate(
                new EndpointRetryOptions
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
            AssertInvalid(options => options.Delivery.TargetBatchPayloadBytes = 0, typeof(ArgumentOutOfRangeException));
            AssertInvalid(options => options.Delivery.EmergencyMaximumBatchEvents = 0, typeof(ArgumentOutOfRangeException));
            AssertInvalid(options => options.Delivery.EmergencyTargetBatchPayloadBytes = 0, typeof(ArgumentOutOfRangeException));
            AssertInvalid(options => options.Delivery.PollInterval = TimeSpan.Zero, typeof(ArgumentOutOfRangeException));
            AssertInvalid(options => options.Delivery.MaximumBatchWait = TimeSpan.Zero, typeof(ArgumentOutOfRangeException));
            AssertInvalid(options => options.Delivery.RequestTimeout = TimeSpan.Zero, typeof(ArgumentOutOfRangeException));
            AssertInvalid(options => options.Delivery.RequestTimeout = TimeSpan.FromSeconds(30), typeof(ArgumentOutOfRangeException));
            AssertInvalid(options => options.Delivery.ShutdownTimeout = TimeSpan.FromSeconds(-1), typeof(ArgumentOutOfRangeException));
            AssertInvalid(options => options.Delivery.ShutdownTimeout = TimeSpan.FromDays(50), typeof(ArgumentOutOfRangeException));
            AssertInvalid(options => options.Delivery.ShutdownRequestTimeout = TimeSpan.Zero, typeof(ArgumentOutOfRangeException));
            AssertInvalid(options => options.Delivery.ShutdownRequestTimeout = TimeSpan.FromDays(50), typeof(ArgumentOutOfRangeException));
            AssertInvalid(options => options.Delivery.ShutdownRetryInterval = TimeSpan.Zero, typeof(ArgumentOutOfRangeException));
            AssertInvalid(options => options.Delivery.ShutdownRetryInterval = TimeSpan.FromDays(50), typeof(ArgumentOutOfRangeException));
            AssertInvalid(options => options.ApplicationSpool.SentEventRetention = TimeSpan.FromSeconds(-1), typeof(ArgumentOutOfRangeException));
            AssertInvalid(options => options.ApplicationSpool.UnsentEventMaxAge = TimeSpan.FromSeconds(-1), typeof(ArgumentOutOfRangeException));
            AssertInvalid(options => options.ApplicationSpool.MaxPhysicalBytes = 4095, typeof(ArgumentOutOfRangeException));
            AssertInvalid(options => options.EmergencyMemoryBuffer.MaxBufferedEvents = 0, typeof(ArgumentOutOfRangeException));
            AssertInvalid(options => options.EmergencyMemoryBuffer.MaxBufferedPayloadBytes = 0, typeof(ArgumentOutOfRangeException));
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

        private static Assembly CreateAssembly(Version? version, string? informationalVersion)
        {
            var name = new AssemblyName($"SerilogRelayVersionTest{Guid.NewGuid():N}")
            {
                Version = version,
            };
            AssemblyBuilder assembly = AssemblyBuilder.DefineDynamicAssembly(name, AssemblyBuilderAccess.Run);
            if (informationalVersion is not null)
            {
                ConstructorInfo constructor = typeof(AssemblyInformationalVersionAttribute)
                    .GetConstructor(new[] { typeof(string) })!;
                assembly.SetCustomAttribute(new CustomAttributeBuilder(
                    constructor,
                    new object[] { informationalVersion }));
            }
            return assembly;
        }

        private sealed class VersionlessAssembly : Assembly
        {
            public override AssemblyName GetName()
                => new AssemblyName("Versionless");

            public override AssemblyName GetName(bool copiedName)
                => GetName();

            public override object[] GetCustomAttributes(Type attributeType, bool inherit)
                => Array.Empty<Attribute>();
        }
    }
}
