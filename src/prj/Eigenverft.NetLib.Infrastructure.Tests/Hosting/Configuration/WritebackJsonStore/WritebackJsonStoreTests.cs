using System;
using System.IO;
using System.Text.Json;

using Eigenverft.NetLib.Infrastructure.Hosting.Configuration.WritebackJsonStore;

namespace Eigenverft.NetLib.Infrastructure.Tests.Hosting.Configuration.WritebackJsonStore
{
    [TestClass]
    public sealed class WritebackJsonStoreTests
    {
        [TestMethod]
        public void InitialSnapshotCurrentAndRuntimeWorkingCopyStartFromSameDocument()
        {
            using var directory = new TemporaryDirectory();
            string path = directory.Write("settings.json", "{ \"YarpRoute\": \"Variant1\" }");
            using var store = new WritebackJsonStore<RoutingSettings>(path, watchForExternalChanges: false);

            Assert.AreEqual("Variant1", store.InitialSnapshot.YarpRoute);
            Assert.AreEqual("Variant1", store.Current.YarpRoute);
            Assert.AreEqual("Variant1", store.RuntimeWorkingCopy.YarpRoute);
        }

        [TestMethod]
        public void MutateRuntimeWorkingCopyDoesNotChangeCurrentOrBackingFile()
        {
            using var directory = new TemporaryDirectory();
            string path = directory.Write("settings.json", "{ \"YarpRoute\": \"Variant1\" }");
            using var store = new WritebackJsonStore<RoutingSettings>(path, watchForExternalChanges: false);
            int runtimeNotifications = 0;
            int currentNotifications = 0;
            store.RuntimeWorkingCopyChanged += (_, _) => runtimeNotifications++;
            store.CurrentChanged += (_, _) => currentNotifications++;

            store.MutateRuntimeWorkingCopy(settings => settings.YarpRoute = "RuntimeOnly");

            Assert.AreEqual("RuntimeOnly", store.RuntimeWorkingCopy.YarpRoute);
            Assert.AreEqual("Variant1", store.Current.YarpRoute);
            Assert.AreEqual("Variant1", store.InitialSnapshot.YarpRoute);
            Assert.AreEqual("Variant1", ReadRoute(path));
            Assert.AreEqual(1, runtimeNotifications);
            Assert.AreEqual(0, currentNotifications);
        }

        [TestMethod]
        public void MutateCurrentAndSaveChangesCurrentAndDiskButLeavesRuntimeWorkingCopyDetached()
        {
            using var directory = new TemporaryDirectory();
            string path = directory.Write("settings.json", "{ \"YarpRoute\": \"Variant1\" }");
            using var store = new WritebackJsonStore<RoutingSettings>(path, watchForExternalChanges: false);
            int currentNotifications = 0;
            int runtimeNotifications = 0;
            store.CurrentChanged += (_, _) => currentNotifications++;
            store.RuntimeWorkingCopyChanged += (_, _) => runtimeNotifications++;

            store.MutateRuntimeWorkingCopy(settings => settings.YarpRoute = "RuntimeOnly");
            runtimeNotifications = 0;

            store.MutateCurrentAndSave(settings => settings.YarpRoute = "Variant2");

            Assert.AreEqual("Variant2", store.Current.YarpRoute);
            Assert.AreEqual("Variant2", ReadRoute(path));
            Assert.AreEqual("RuntimeOnly", store.RuntimeWorkingCopy.YarpRoute);
            Assert.AreEqual("Variant1", store.InitialSnapshot.YarpRoute);
            Assert.AreEqual(1, currentNotifications);
            Assert.AreEqual(0, runtimeNotifications);
        }

        [TestMethod]
        public void RestoreRuntimeWorkingCopyFromCurrentDiscardsRuntimeChangesWithoutWritingDisk()
        {
            using var directory = new TemporaryDirectory();
            string path = directory.Write("settings.json", "{ \"YarpRoute\": \"Variant1\" }");
            using var store = new WritebackJsonStore<RoutingSettings>(path, watchForExternalChanges: false);

            store.MutateCurrentAndSave(settings => settings.YarpRoute = "Variant2");
            store.MutateRuntimeWorkingCopy(settings => settings.YarpRoute = "RuntimeOnly");
            int notifications = 0;
            store.RuntimeWorkingCopyChanged += (_, _) => notifications++;

            store.RestoreRuntimeWorkingCopyFromCurrent();

            Assert.AreEqual("Variant2", store.RuntimeWorkingCopy.YarpRoute);
            Assert.AreEqual("Variant2", store.Current.YarpRoute);
            Assert.AreEqual("Variant2", ReadRoute(path));
            Assert.AreEqual("Variant1", store.InitialSnapshot.YarpRoute);
            Assert.AreEqual(1, notifications);
        }

        [TestMethod]
        public void RestoreRuntimeWorkingCopyFromInitialSnapshotLeavesCurrentAndDiskUntouched()
        {
            using var directory = new TemporaryDirectory();
            string path = directory.Write("settings.json", "{ \"YarpRoute\": \"Variant1\" }");
            using var store = new WritebackJsonStore<RoutingSettings>(path, watchForExternalChanges: false);

            store.MutateCurrentAndSave(settings => settings.YarpRoute = "Variant2");
            store.MutateRuntimeWorkingCopy(settings => settings.YarpRoute = "RuntimeOnly");

            store.RestoreRuntimeWorkingCopyFromInitialSnapshot();

            Assert.AreEqual("Variant1", store.RuntimeWorkingCopy.YarpRoute);
            Assert.AreEqual("Variant2", store.Current.YarpRoute);
            Assert.AreEqual("Variant2", ReadRoute(path));
            Assert.AreEqual("Variant1", store.InitialSnapshot.YarpRoute);
        }

        [TestMethod]
        public void RestoreCurrentFromInitialSnapshotAndSaveLeavesRuntimeWorkingCopyUntouched()
        {
            using var directory = new TemporaryDirectory();
            string path = directory.Write("settings.json", "{ \"YarpRoute\": \"Variant1\" }");
            using var store = new WritebackJsonStore<RoutingSettings>(path, watchForExternalChanges: false);

            store.MutateCurrentAndSave(settings => settings.YarpRoute = "Variant2");
            store.MutateRuntimeWorkingCopy(settings => settings.YarpRoute = "RuntimeOnly");

            store.RestoreCurrentFromInitialSnapshotAndSave();

            Assert.AreEqual("Variant1", store.Current.YarpRoute);
            Assert.AreEqual("Variant1", ReadRoute(path));
            Assert.AreEqual("RuntimeOnly", store.RuntimeWorkingCopy.YarpRoute);
            Assert.AreEqual("Variant1", store.InitialSnapshot.YarpRoute);
        }

        [TestMethod]
        public void RestoreAllFromInitialSnapshotAndSaveRollsBackCurrentDiskAndRuntimeWorkingCopy()
        {
            using var directory = new TemporaryDirectory();
            string path = directory.Write("settings.json", "{ \"YarpRoute\": \"Variant1\" }");
            using var store = new WritebackJsonStore<RoutingSettings>(path, watchForExternalChanges: false);

            store.MutateCurrentAndSave(settings => settings.YarpRoute = "Variant2");
            store.MutateRuntimeWorkingCopy(settings => settings.YarpRoute = "RuntimeOnly");
            int currentNotifications = 0;
            int runtimeNotifications = 0;
            store.CurrentChanged += (_, _) => currentNotifications++;
            store.RuntimeWorkingCopyChanged += (_, _) => runtimeNotifications++;

            store.RestoreAllFromInitialSnapshotAndSave();

            Assert.AreEqual("Variant1", store.Current.YarpRoute);
            Assert.AreEqual("Variant1", store.RuntimeWorkingCopy.YarpRoute);
            Assert.AreEqual("Variant1", store.InitialSnapshot.YarpRoute);
            Assert.AreEqual("Variant1", ReadRoute(path));
            Assert.AreEqual(1, currentNotifications);
            Assert.AreEqual(1, runtimeNotifications);
        }

        [TestMethod]
        public void ReloadCurrentFromFileDoesNotImplicitlyReplaceRuntimeWorkingCopy()
        {
            using var directory = new TemporaryDirectory();
            string path = directory.Write("settings.json", "{ \"YarpRoute\": \"Variant1\" }");
            using var store = new WritebackJsonStore<RoutingSettings>(path, watchForExternalChanges: false);

            store.MutateRuntimeWorkingCopy(settings => settings.YarpRoute = "RuntimeOnly");
            File.WriteAllText(path, "{ \"YarpRoute\": \"Variant3\" }");

            bool reloaded = store.ReloadCurrentFromFile();

            Assert.IsTrue(reloaded);
            Assert.AreEqual("Variant3", store.Current.YarpRoute);
            Assert.AreEqual("RuntimeOnly", store.RuntimeWorkingCopy.YarpRoute);
            Assert.AreEqual("Variant1", store.InitialSnapshot.YarpRoute);
        }

        [TestMethod]
        public void GetCurrentSnapshotReturnsIndependentCopy()
        {
            using var directory = new TemporaryDirectory();
            string path = directory.Write("settings.json", "{ \"YarpRoute\": \"Variant1\" }");
            using var store = new WritebackJsonStore<RoutingSettings>(path, watchForExternalChanges: false);

            RoutingSettings snapshot = store.GetCurrentSnapshot();
            snapshot.YarpRoute = "Detached";

            Assert.AreEqual("Variant1", store.Current.YarpRoute);
            Assert.AreEqual("Variant1", ReadRoute(path));
        }

        private static string? ReadRoute(string path)
        {
            using JsonDocument document = JsonDocument.Parse(File.ReadAllText(path));
            return document.RootElement.GetProperty("YarpRoute").GetString();
        }

        public sealed class RoutingSettings
        {
            public string YarpRoute { get; set; } = string.Empty;
        }

        private sealed class TemporaryDirectory : IDisposable
        {
            public TemporaryDirectory()
            {
                Path = System.IO.Path.Combine(
                    System.IO.Path.GetTempPath(),
                    "Eigenverft.NetLib.Infrastructure.Tests",
                    Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(Path);
            }

            public string Path { get; }

            public string Write(string relativePath, string content)
            {
                string path = System.IO.Path.Combine(Path, relativePath);
                File.WriteAllText(path, content);
                return path;
            }

            public void Dispose()
            {
                if (Directory.Exists(Path))
                {
                    Directory.Delete(Path, recursive: true);
                }
            }
        }
    }
}
