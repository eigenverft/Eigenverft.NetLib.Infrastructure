using System;
using System.IO;

using Eigenverft.NetLib.Infrastructure.Hosting.Configuration.Values;

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
