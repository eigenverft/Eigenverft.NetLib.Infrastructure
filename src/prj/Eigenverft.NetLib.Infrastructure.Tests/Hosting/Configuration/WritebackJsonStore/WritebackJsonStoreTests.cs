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
        public void InitialSnapshotCurrentAndRuntimeCopyStartFromSameDocument()
        {
            using var directory = new TemporaryDirectory();
            string path = directory.Write("settings.json", "{ \"YarpRoute\": \"Variant1\" }");
            using var store = new WritebackJsonStore<RoutingSettings>(path, watchForExternalChanges: false);

            Assert.AreEqual("Variant1", store.InitialSnapshot.YarpRoute);
            Assert.AreEqual("Variant1", store.Current.YarpRoute);
            Assert.AreEqual("Variant1", store.RuntimeCopy.YarpRoute);
        }

        [TestMethod]
        public void MutateRuntimeCopyDoesNotChangePersistedBranchOrBackingFile()
        {
            using var directory = new TemporaryDirectory();
            string path = directory.Write("settings.json", "{ \"YarpRoute\": \"Variant1\" }");
            using var store = new WritebackJsonStore<RoutingSettings>(path, watchForExternalChanges: false);
            int notifications = 0;
            store.RuntimeCopyChanged += (_, _) => notifications++;

            store.MutateRuntimeCopy(settings => settings.YarpRoute = "Variant2");

            Assert.AreEqual("Variant2", store.RuntimeCopy.YarpRoute);
            Assert.AreEqual("Variant1", store.Current.YarpRoute);
            Assert.AreEqual("Variant1", store.InitialSnapshot.YarpRoute);
            Assert.AreEqual("Variant1", ReadRoute(path));
            Assert.AreEqual(1, notifications);
        }

        [TestMethod]
        public void MutateAndSaveChangesPersistedBranchButLeavesRuntimeCopyDetached()
        {
            using var directory = new TemporaryDirectory();
            string path = directory.Write("settings.json", "{ \"YarpRoute\": \"Variant1\" }");
            using var store = new WritebackJsonStore<RoutingSettings>(path, watchForExternalChanges: false);

            store.MutateRuntimeCopy(settings => settings.YarpRoute = "RuntimeOnly");
            store.MutateAndSave(settings => settings.YarpRoute = "Variant2");

            Assert.AreEqual("Variant2", store.Current.YarpRoute);
            Assert.AreEqual("Variant2", ReadRoute(path));
            Assert.AreEqual("RuntimeOnly", store.RuntimeCopy.YarpRoute);
            Assert.AreEqual("Variant1", store.InitialSnapshot.YarpRoute);
        }

        [TestMethod]
        public void ResetRuntimeCopyReturnsToInitialSnapshotWithoutWritingDisk()
        {
            using var directory = new TemporaryDirectory();
            string path = directory.Write("settings.json", "{ \"YarpRoute\": \"Variant1\" }");
            using var store = new WritebackJsonStore<RoutingSettings>(path, watchForExternalChanges: false);

            store.MutateRuntimeCopy(settings => settings.YarpRoute = "RuntimeOnly");
            store.MutateAndSave(settings => settings.YarpRoute = "Variant2");
            store.ResetRuntimeCopy();

            Assert.AreEqual("Variant1", store.RuntimeCopy.YarpRoute);
            Assert.AreEqual("Variant2", store.Current.YarpRoute);
            Assert.AreEqual("Variant2", ReadRoute(path));
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
