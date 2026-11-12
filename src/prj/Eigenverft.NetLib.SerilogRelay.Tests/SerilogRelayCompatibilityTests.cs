using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Versioning;
using System.Text;
using System.Threading;

using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Eigenverft.NetLib.SerilogRelay.Tests
{
    [TestClass]
    public sealed class SerilogRelayCompatibilityTests
    {
        [TestMethod]
        public void LibraryTargetsExpectedFramework()
        {
            string expected = typeof(SerilogRelayCompatibilityTests).Assembly
                .GetCustomAttributes<AssemblyMetadataAttribute>()
                .Single(attribute => attribute.Key == "TestLibraryTargetFramework").Value!;
            string actual = typeof(SerilogRelaySink).Assembly.GetCustomAttribute<TargetFrameworkAttribute>()!.FrameworkName;
            string framework = expected == "netstandard2.1" ? ".NETStandard,Version=v2.1"
                : ".NETCoreApp,Version=v" + expected.Substring(3);
            Assert.AreEqual(framework, actual);
        }

        [TestMethod]
        public void RuntimeHelpersPreserveProcessIdentityHashesAndElapsedTime()
        {
            Assert.AreEqual(Environment.ProcessId, RelayRuntime.ProcessId);
            Assert.AreEqual("BA7816BF8F01CFEA414140DE5DAE2223B00361A396177A9CB410FF61F20015AD",
                RelayRuntime.Sha256Hex(Encoding.UTF8.GetBytes("abc")));
            Assert.IsTrue(RelayRuntime.ElapsedSince(Stopwatch.GetTimestamp()) >= TimeSpan.Zero);
            double random = RelayRuntime.NextRandomDouble();
            Assert.IsTrue(random >= 0 && random < 1);
        }

        [TestMethod]
        public void MissingSinkOptionsAreRejectedBeforeStartingAnyWorkers()
        {
            var error = Assert.ThrowsExactly<ArgumentNullException>(() =>
                new SerilogRelaySink("Data Source=:memory:", null, (SerilogRelayOptions)null!));
            Assert.AreEqual("options", error.ParamName);
        }

#if TEST_NETSTANDARD_RELAY
        [TestMethod]
        public void StandardReadExactlyHandlesPartialEmptyAndTruncatedReads()
        {
            using var input = new PartialReadStream([1, 2, 3]);
            byte[] bytes = new byte[3];
            StreamCompatibility.ReadExactly(input, bytes);
            CollectionAssert.AreEqual(new byte[] { 1, 2, 3 }, bytes);
            StreamCompatibility.ReadExactly(input, []);
            Assert.ThrowsExactly<EndOfStreamException>(() => StreamCompatibility.ReadExactly(input, new byte[1]));
        }

        [TestMethod]
        public void StandardTimeoutCallbackCancelsAndToleratesConcurrentScopeDisposal()
        {
            MethodInfo callback = typeof(SerilogRelaySink).GetMethod("CancelRequestTimeout", BindingFlags.Static | BindingFlags.NonPublic)!;
            var source = new CancellationTokenSource();
            callback.Invoke(null, [source]);
            Assert.IsTrue(source.IsCancellationRequested);
            source.Dispose();
            callback.Invoke(null, [source]);
        }

        private sealed class PartialReadStream(byte[] bytes) : MemoryStream(bytes)
        {
            public override int Read(byte[] buffer, int offset, int count) => base.Read(buffer, offset, Math.Min(count, 1));
        }
#endif
    }
}
