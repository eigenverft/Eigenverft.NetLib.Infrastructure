using System;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;

using Microsoft.Data.Sqlite;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Eigenverft.NetLib.SerilogRelay.Tests
{
    public sealed partial class SerilogRelayReliabilityTests
    {
        [TestMethod]
        public void ApplicationDirectoryAndGenerationNamesRespectBothPlatformRules()
        {
            MethodInfo directoryName = typeof(LoggerConfigurationSerilogRelayExtensions).GetMethod("ResolveApplicationSpoolDirectoryName", BindingFlags.Static | BindingFlags.NonPublic)!;
            Assert.AreEqual("CON", directoryName.Invoke(null, new object[] { "CON", false }));
            string reserved = (string)directoryName.Invoke(null, new object[] { "CON", true })!;
            Assert.AreEqual(65, reserved.Length); Assert.IsTrue(reserved.StartsWith('_'));
            string longId = LoggerConfigurationSerilogRelayExtensions.ResolveApplicationId(new string('a', 256));
            Assert.AreEqual(255, longId.Length);
            Assert.AreEqual(longId, directoryName.Invoke(null, new object[] { longId, false }));
            Assert.AreEqual(longId, directoryName.Invoke(null, new object[] { longId, true }));
            Assert.AreEqual("app", directoryName.Invoke(null, new object[] { "app", true }));

            string directory = CreateTemporaryDirectory();
            try
            {
                using var sink = new SerilogRelaySink($"Data Source={Path.Combine(directory, "relay.db")}", null, new SerilogRelayOptions());
                Assert.AreEqual(Path.Combine(directory, "relay.db"), InvokePrivateMethod<string>(sink, "GetSpoolGenerationPath", 0));
                Assert.AreEqual(1, InvokePrivateMethod<int>(sink, "GetSpoolGenerationNumberCore", "RELAY.g0001.DB", true));
                Assert.AreEqual(-1, InvokePrivateMethod<int>(sink, "GetSpoolGenerationNumberCore", "RELAY.g0001.DB", false));
                Assert.AreEqual(1, InvokePrivateMethod<int>(sink, "GetSpoolGenerationNumberCore", "relay.g0001.db", false));
                foreach (string invalid in new[] { "relay.gxxxx.db", "relay.g0000.db", "relay.g00001.db", "relay.g2147483648.db" })
                    Assert.AreEqual(-1, InvokePrivateMethod<int>(sink, "GetSpoolGenerationNumber", invalid));
            }
            finally { DeleteTemporaryDirectory(directory); }
        }

        [TestMethod]
        public async Task GenerationCleanupRequiresMatchingMarkerAndChangedBootIdentity()
        {
            string directory = CreateTemporaryDirectory();
            try
            {
                string databasePath = Path.Combine(directory, "relay.db");
                await using var sink = new SerilogRelaySink($"Data Source={databasePath}", null, new SerilogRelayOptions());
                await StopRelayWorkers(sink); SqliteConnection.ClearAllPools();
                string latest = Path.Combine(directory, "relay.g0001.db"); File.WriteAllBytes(latest, Array.Empty<byte>());
                Guid boot = Guid.NewGuid();
                SetPrivateField(sink, "_bootIdentityProvider", (Func<Guid?>)(() => boot));
                Assert.AreEqual(latest, InvokePrivateMethod<string>(sink, "SelectStartupSpoolGeneration"));
                Assert.IsTrue(File.Exists(databasePath), "A stale marker must preserve earlier generations.");
                Assert.AreEqual(latest, InvokePrivateMethod<string>(sink, "SelectStartupSpoolGeneration"));
                Assert.IsTrue(File.Exists(databasePath), "Same-boot old generations can still be in use.");
                File.WriteAllText(databasePath + "-wal", "wal"); File.WriteAllText(databasePath + "-shm", "shm");
                File.WriteAllText(Path.Combine(directory, "unrelated.db-shm"), "keep");
                boot = Guid.NewGuid();
                Assert.AreEqual(latest, InvokePrivateMethod<string>(sink, "SelectStartupSpoolGeneration"));
                Assert.IsFalse(File.Exists(databasePath));
                Assert.IsFalse(File.Exists(databasePath + "-wal")); Assert.IsFalse(File.Exists(databasePath + "-shm"));
                Assert.IsTrue(File.Exists(latest)); Assert.IsTrue(File.Exists(Path.Combine(directory, "unrelated.db-shm")));
                SetPrivateField(sink, "_bootIdentityProvider", (Func<Guid?>)(() => null));
                Assert.AreEqual(latest, InvokePrivateMethod<string>(sink, "SelectStartupSpoolGeneration"));
                Assert.IsTrue(InvokePrivateMethod<bool>(sink, "TryRecoverCorruptedSpool", new SqliteException("corrupt", SQLitePCL.raw.SQLITE_CORRUPT)));
                Assert.IsFalse(InvokePrivateMethod<bool>(sink, "TryRecoverCorruptedSpool", new SqliteException("again", SQLitePCL.raw.SQLITE_CORRUPT)));
            }
            finally { DeleteTemporaryDirectory(directory); }
        }

        [TestMethod]
        public async Task CorruptionReservesANewGenerationWithoutBootIdentityAndPreservesTheOldFile()
        {
            string directory = CreateTemporaryDirectory();
            try
            {
                string path = Path.Combine(directory, "relay.db");
                string connectionString = $"Data Source={path}";
                await using var sink = new SerilogRelaySink(connectionString, null, new SerilogRelayOptions());
                await StopRelayWorkers(sink);
                sink.Emit(CreateLogEvent("old generation"));
                SetPrivateField(sink, "_bootIdentityProvider", (Func<Guid?>)(() => null));

                Assert.IsTrue(InvokePrivateMethod<bool>(sink, "TryRecoverCorruptedSpool",
                    new SqliteException("corrupt", SQLitePCL.raw.SQLITE_CORRUPT)));
                string successor = Path.Combine(directory, "relay.g0001.db");
                Assert.IsTrue(File.Exists(successor));
                Assert.AreEqual(0L, new FileInfo(successor).Length);
                Assert.IsTrue(GetPrivateField<bool>(sink, "_spoolDisabledForLifetime"));

                await using var restarted = new SerilogRelaySink(connectionString, null, new SerilogRelayOptions());
                await StopRelayWorkers(restarted);
                Assert.AreEqual(successor, GetPrivateField<string>(restarted, "_databasePath"));
                restarted.Emit(CreateLogEvent("new generation"));
                Assert.AreEqual(1L, GetUnsentCount(GetPrivateField<string>(restarted, "_connectionString")));
                Assert.AreEqual(1L, GetUnsentCount(connectionString),
                    "Without a marker for the new generation, startup cannot prove that old processes are gone.");
            }
            finally { DeleteTemporaryDirectory(directory); }
        }

        [TestMethod]
        public void GenerationMarkerRejectsMalformedAndInterruptedRecords()
        {
            string directory = CreateTemporaryDirectory();
            try
            {
                string path = Path.Combine(directory, "marker");
                MethodInfo read = typeof(SerilogRelaySink).GetMethod("ReadGenerationMarker", BindingFlags.NonPublic | BindingFlags.Static)!;
                MethodInfo write = typeof(SerilogRelaySink).GetMethod("WriteGenerationMarker", BindingFlags.NonPublic | BindingFlags.Static)!;
                Guid boot = Guid.NewGuid();
                using var stream = new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.ReadWrite);
                write.Invoke(null, new object[] { stream, 1, boot });
                Assert.AreEqual((1, boot), ((int, Guid))read.Invoke(null, new object[] { stream })!);
                foreach (string text in new[]
                {
                    new string('a', 129), "", "wrong\n1\n" + boot + "\n", "SerilogRelay.Generation.v1\n1\n" + boot + "\nextra",
                    "SerilogRelay.Generation.v1\ninvalid\n" + boot + "\n", "SerilogRelay.Generation.v1\n-1\n" + boot + "\n",
                    "SerilogRelay.Generation.v1\n1\ninvalid\n", "SerilogRelay.Generation.v1\n1\n" + Guid.Empty + "\n",
                })
                {
                    byte[] bytes = Encoding.UTF8.GetBytes(text); stream.SetLength(0); stream.Position = 0; stream.Write(bytes); stream.Flush();
                    Assert.IsNull(read.Invoke(null, new object[] { stream }), text);
                }
                using var fault = new FaultingMarkerStream(Path.Combine(directory, "fault"));
                Assert.IsNull(read.Invoke(null, new object[] { fault }));
                write.Invoke(null, new object[] { fault, 1, boot });
            }
            finally { DeleteTemporaryDirectory(directory); }
        }

        [TestMethod]
        public async Task GenerationCoordinationAndCleanupFailuresPreserveFiles()
        {
            string directory = CreateTemporaryDirectory();
            try
            {
                string path = Path.Combine(directory, "relay.db");
                await using var sink = new SerilogRelaySink($"Data Source={path}", null, new SerilogRelayOptions());
                await StopRelayWorkers(sink); SqliteConnection.ClearAllPools();
                using (var coordination = new FileStream(path + ".recovery.lock", FileMode.Open, FileAccess.ReadWrite, FileShare.None))
                {
                    TargetInvocationException exception = Assert.ThrowsExactly<TargetInvocationException>(() => InvokePrivateMethod<string>(sink, "SelectStartupSpoolGeneration"));
                    Assert.IsInstanceOfType<IOException>(exception.InnerException);
                }
                using (var locked = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
                    InvokePrivateMethod<object?>(sink, "CleanupObsoleteSpoolGenerations", 1);
                Assert.IsTrue(File.Exists(path));
                File.SetAttributes(path, FileAttributes.ReadOnly);
                try { InvokePrivateMethod<object?>(sink, "CleanupObsoleteSpoolGenerations", 1); }
                finally { if (File.Exists(path)) File.SetAttributes(path, FileAttributes.Normal); }
                File.WriteAllText(path + "-wal", "orphan wal"); File.WriteAllText(path + "-shm", "orphan shm");
                File.Delete(path);
                InvokePrivateMethod<object?>(sink, "CleanupObsoleteSpoolGenerations", 1);
                Assert.IsFalse(File.Exists(path + "-wal")); Assert.IsFalse(File.Exists(path + "-shm"));
                SetPrivateField(sink, "_baseDatabasePath", Path.Combine(directory, "absent", "relay.db"));
                InvokePrivateMethod<object?>(sink, "CleanupObsoleteSpoolGenerations", 1);
                SetPrivateField(sink, "_spoolDisabledForLifetime", true);
            }
            finally { DeleteTemporaryDirectory(directory); }
        }

        private sealed class FaultingMarkerStream : FileStream
        {
            internal FaultingMarkerStream(string path) : base(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.ReadWrite) { }
            public override long Length => throw new IOException("marker cannot be read");
            public override void SetLength(long value) => throw new IOException("marker cannot be written");
        }
    }
}
