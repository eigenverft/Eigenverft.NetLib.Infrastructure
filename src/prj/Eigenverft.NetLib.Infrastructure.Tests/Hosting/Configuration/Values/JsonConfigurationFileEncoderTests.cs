using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

using Eigenverft.NetLib.Infrastructure.Hosting.Configuration.Values;
using Eigenverft.NetLib.Infrastructure.Transformations;

namespace Eigenverft.NetLib.Infrastructure.Tests.Hosting.Configuration.Values
{
    [TestClass]
    public sealed class JsonConfigurationFileEncoderTests
    {
        [TestMethod]
        public void CompletePathsMatchObjectsArraysWildcardsAndNulls()
        {
            using var directory = new TemporaryDirectory();
            string path = directory.Write("settings.json", """
                {
                  "Certificates": [
                    { "Password": "first", "Token": null, "Port": 443 },
                    { "Password": "second", "Token": "plain" }
                  ],
                  "Other": { "Password": "untouched" }
                }
                """);

            int updated = JsonConfigurationFileEncoder.EncodeMatchingValuesInPlace(
                path,
                new[] { "certificates:?:password", "Certificates:*:Token" },
                ConfigurationValueCodecs.Base64);

            Assert.AreEqual(4, updated);
            string persisted = File.ReadAllText(path);
            Assert.IsTrue(persisted.Contains("untouched", StringComparison.Ordinal));
            Assert.IsTrue(persisted.Contains("\"Port\": 443", StringComparison.Ordinal));
            Assert.IsFalse(persisted.Contains("\"Password\": \"first\"", StringComparison.Ordinal));
            Assert.IsFalse(persisted.Contains("\"Token\": null", StringComparison.Ordinal));
        }

        [TestMethod]
        public void NullValuesCanRemainNull()
        {
            using var directory = new TemporaryDirectory();
            string path = directory.Write("settings.json", "{ \"Token\": null }");

            int updated = JsonConfigurationFileEncoder.EncodeMatchingValuesInPlace(
                path,
                "Token",
                ConfigurationValueCodecs.Base64,
                nullAsEmpty: false);

            Assert.AreEqual(0, updated);
            Assert.AreEqual("{ \"Token\": null }", File.ReadAllText(path));
        }

        [TestMethod]
        public void RecognizedWrappersAreUntouchedRegardlessOfCodec()
        {
            using var directory = new TemporaryDirectory();
            string recognized = ConfigurationValueCodecs.Rot13.Encode("preserve");
            string path = directory.Write(
                "settings.json",
                $$"""{ "Known": "{{recognized}}", "Unknown": "enc:999:anything" }""");

            int updated = JsonConfigurationFileEncoder.EncodeMatchingValuesInPlace(
                path,
                new[] { "Known", "Unknown" },
                ConfigurationValueCodecs.Base64);

            Assert.AreEqual(1, updated);
            string persisted = File.ReadAllText(path);
            Assert.IsTrue(persisted.Contains(recognized, StringComparison.Ordinal));
            Assert.IsFalse(persisted.Contains("enc:999:anything", StringComparison.Ordinal));
        }

        [TestMethod]
        public void RepeatedEncodingPreservesContentAndWriteTime()
        {
            using var directory = new TemporaryDirectory();
            string path = directory.Write("settings.json", "{ \"ApiKey\": \"secret\" }");
            _ = JsonConfigurationFileEncoder.EncodeMatchingValuesInPlace(
                path,
                "ApiKey",
                ConfigurationValueCodecs.Base64);
            string firstContent = File.ReadAllText(path);
            DateTime firstWriteTime = File.GetLastWriteTimeUtc(path);

            int updated = JsonConfigurationFileEncoder.EncodeMatchingValuesInPlace(
                path,
                "ApiKey",
                ConfigurationValueCodecs.AesPassword("test-only-password"));

            Assert.AreEqual(0, updated);
            Assert.AreEqual(firstContent, File.ReadAllText(path));
            Assert.AreEqual(firstWriteTime, File.GetLastWriteTimeUtc(path));
        }

        [TestMethod]
        public void ComposedCodecRoundTripsPersistedValueAndLeavesNoTemporaryFile()
        {
            using var directory = new TemporaryDirectory();
            string path = directory.Write("settings.json", "{ // comment\n \"ApiToken\": \"secret\",\n }");
            ConfigurationValueCodec codec = ConfigurationValueCodecs.Compose(
                ConfigurationValueCodecs.AesPassword("test-only-password"),
                ConfigurationValueCodecs.Base64);

            int updated = JsonConfigurationFileEncoder.EncodeMatchingValuesInPlace(
                path,
                "ApiToken",
                codec);

            Assert.AreEqual(1, updated);
            Assert.AreEqual(0, Directory.GetFiles(directory.Path, "*.tmp").Length);
            string persisted = File.ReadAllText(path);
            Assert.IsFalse(persisted.Contains("comment", StringComparison.Ordinal));
            using System.Text.Json.JsonDocument document = System.Text.Json.JsonDocument.Parse(persisted);
            string encoded = document.RootElement.GetProperty("ApiToken").GetString()!;
            Assert.IsTrue(codec.TryDecode(encoded, out string clearText));
            Assert.AreEqual("secret", clearText);
        }

        [TestMethod]
        public void AlreadyProtectedReadOnlyFileRemainsAValidNoOp()
        {
            if (!OperatingSystem.IsWindows())
            {
                return;
            }

            using var directory = new TemporaryDirectory();
            string path = directory.Write("settings.json", "{ \"ApiKey\": \"secret\" }");
            _ = JsonConfigurationFileEncoder.EncodeMatchingValuesInPlace(
                path,
                "ApiKey",
                ConfigurationValueCodecs.Base64);
            string protectedContent = File.ReadAllText(path);
            File.SetAttributes(path, File.GetAttributes(path) | FileAttributes.ReadOnly);

            try
            {
                int updated = JsonConfigurationFileEncoder.EncodeMatchingValuesInPlace(
                    path,
                    "ApiKey",
                    ConfigurationValueCodecs.Base64);

                Assert.AreEqual(0, updated);
                Assert.AreEqual(protectedContent, File.ReadAllText(path));
            }
            finally
            {
                File.SetAttributes(path, File.GetAttributes(path) & ~FileAttributes.ReadOnly);
            }
        }

        [TestMethod]
        public void ClearTextReadOnlyFileStillRequiresWriteAccess()
        {
            if (!OperatingSystem.IsWindows())
            {
                return;
            }

            using var directory = new TemporaryDirectory();
            string path = directory.Write("settings.json", "{ \"ApiKey\": \"secret\" }");
            string original = File.ReadAllText(path);
            File.SetAttributes(path, File.GetAttributes(path) | FileAttributes.ReadOnly);

            try
            {
                _ = Assert.ThrowsExactly<UnauthorizedAccessException>(() =>
                    JsonConfigurationFileEncoder.EncodeMatchingValuesInPlace(
                        path,
                        "ApiKey",
                        ConfigurationValueCodecs.Base64));
                Assert.AreEqual(original, File.ReadAllText(path));
            }
            finally
            {
                File.SetAttributes(path, File.GetAttributes(path) & ~FileAttributes.ReadOnly);
            }
        }

        [TestMethod]
        public async Task ConcurrentExternalWriterWinsWithoutBeingOverwrittenByOlderProtectionSnapshot()
        {
            using var directory = new TemporaryDirectory();
            string path = directory.Write(
                "settings.json",
                "{ \"Revision\": \"A\", \"ApiKey\": \"secret-a\" }");
            using var entered = new ManualResetEventSlim();
            using var release = new ManualResetEventSlim();
            ConfigurationValueCodec codec = CreateBlockingCodec(entered, release);

            Task<int> protection = Task.Run(() =>
                JsonConfigurationFileEncoder.EncodeMatchingValuesInPlace(path, "ApiKey", codec));
            Assert.IsTrue(entered.Wait(TimeSpan.FromSeconds(5)), "Protection did not reach the blocked transform.");

            Task writer = Task.Run(() =>
            {
                while (true)
                {
                    try
                    {
                        File.WriteAllText(path, "{ \"Revision\": \"B\", \"ApiKey\": \"secret-b\" }");
                        return;
                    }
                    catch (IOException)
                    {
                        Thread.Sleep(5);
                    }
                }
            });

            release.Set();
            _ = await protection.WaitAsync(TimeSpan.FromSeconds(5));
            await writer.WaitAsync(TimeSpan.FromSeconds(5));

            string persisted = File.ReadAllText(path);
            using System.Text.Json.JsonDocument document = System.Text.Json.JsonDocument.Parse(persisted);
            Assert.AreEqual("B", document.RootElement.GetProperty("Revision").GetString());
            string persistedApiKey = document.RootElement.GetProperty("ApiKey").GetString()!;
            string effectiveApiKey = codec.TryDecode(persistedApiKey, out string decodedApiKey)
                ? decodedApiKey
                : persistedApiKey;
            Assert.AreEqual("secret-b", effectiveApiKey);
            Assert.IsFalse(persisted.Contains("secret-a", StringComparison.Ordinal));
        }

        [TestMethod]
        public async Task ConcurrentDeleteIsNeverResurrectedFromProtectionSnapshot()
        {
            using var directory = new TemporaryDirectory();
            string path = directory.Write(
                "settings.json",
                "{ \"Revision\": \"A\", \"ApiKey\": \"secret-a\" }");
            using var entered = new ManualResetEventSlim();
            using var release = new ManualResetEventSlim();
            ConfigurationValueCodec codec = CreateBlockingCodec(entered, release);

            Task<int> protection = Task.Run(() =>
                JsonConfigurationFileEncoder.EncodeMatchingValuesInPlace(path, "ApiKey", codec));
            Assert.IsTrue(entered.Wait(TimeSpan.FromSeconds(5)), "Protection did not reach the blocked transform.");

            Task delete = Task.Run(() =>
            {
                while (File.Exists(path))
                {
                    try
                    {
                        File.Delete(path);
                    }
                    catch (IOException)
                    {
                        Thread.Sleep(5);
                    }
                    catch (UnauthorizedAccessException)
                    {
                        Thread.Sleep(5);
                    }
                }
            });

            release.Set();
            try
            {
                _ = await protection.WaitAsync(TimeSpan.FromSeconds(5));
            }
            catch (FileNotFoundException)
            {
                // The delete won immediately after the exclusive protection handle was released.
            }
            catch (IOException)
            {
                // Post-write verification can observe the concurrently completed delete and reject the protection attempt.
            }

            await delete.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.IsFalse(File.Exists(path));
        }

        private static ConfigurationValueCodec CreateBlockingCodec(
            ManualResetEventSlim entered,
            ManualResetEventSlim release)
        {
            int invocation = 0;
            var transform = new ReversibleStringTransform(
                "BlockingBase64",
                value =>
                {
                    if (Interlocked.Increment(ref invocation) == 1)
                    {
                        entered.Set();
                        if (!release.Wait(TimeSpan.FromSeconds(5)))
                        {
                            throw new TimeoutException("Blocking codec was not released.");
                        }
                    }

                    return Convert.ToBase64String(Encoding.UTF8.GetBytes(value));
                },
                (string transformed, out string original) =>
                {
                    try
                    {
                        original = Encoding.UTF8.GetString(Convert.FromBase64String(transformed));
                        return true;
                    }
                    catch (FormatException)
                    {
                        original = transformed;
                        return false;
                    }
                });

            return new ConfigurationValueCodec("BlockingBase64", ConfigurationValueKind.Base64, transform);
        }

        private sealed class TemporaryDirectory : IDisposable
        {
            public TemporaryDirectory()
            {
                Path = System.IO.Path.Combine(
                    System.IO.Path.GetTempPath(),
                    $"Eigenverft.NetLib.Infrastructure.Encoder.Tests.{Guid.NewGuid():N}");
                Directory.CreateDirectory(Path);
            }

            public string Path { get; }

            public string Write(string fileName, string content)
            {
                string path = System.IO.Path.Combine(Path, fileName);
                File.WriteAllText(path, content);
                return path;
            }

            public void Dispose()
            {
                Directory.Delete(Path, recursive: true);
            }
        }
    }
}
