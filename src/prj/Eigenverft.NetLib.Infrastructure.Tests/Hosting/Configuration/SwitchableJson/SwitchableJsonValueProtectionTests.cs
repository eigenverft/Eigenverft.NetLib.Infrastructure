using System;
using System.IO;
using System.Threading;

using Eigenverft.NetLib.Infrastructure.Hosting.Configuration.SwitchableJson;
using Eigenverft.NetLib.Infrastructure.Hosting.Configuration.Values;

using Microsoft.Extensions.Hosting;

namespace Eigenverft.NetLib.Infrastructure.Tests.Hosting.Configuration.SwitchableJson
{
    [TestClass]
    public sealed class SwitchableJsonValueProtectionTests
    {
        [TestMethod]
        public void InitialFileIsProtectedBeforeLoadAndPublishedAsClearText()
        {
            using var directory = new TemporaryDirectory();
            string path = directory.Write(
                "settings.json",
                "{ \"PartnerApi\": { \"ApiKey\": \"secret\", \"Endpoint\": \"https://example.test\" } }");
            HostApplicationBuilder builder = CreateBuilder(directory.Path);

            builder.AddSwitchableJsonFile(
                "settings",
                "settings.json",
                Options(JsonConfigurationValueProtection.ForKeys(ConfigurationValueCodecs.Base64, "*ApiKey*")));

            Assert.AreEqual("secret", builder.Configuration["PartnerApi:ApiKey"]);
            Assert.AreEqual("https://example.test", builder.Configuration["PartnerApi:Endpoint"]);
            string persisted = File.ReadAllText(path);
            Assert.IsFalse(persisted.Contains("\"ApiKey\": \"secret\"", StringComparison.Ordinal));
            Assert.IsTrue(persisted.Contains("enc:q7m2n4:", StringComparison.Ordinal));
        }

        [TestMethod]
        public void PathProtectionCanTargetOneNestedValue()
        {
            using var directory = new TemporaryDirectory();
            string path = directory.Write(
                "settings.json",
                "{ \"PartnerApi\": { \"Production\": { \"ApiKey\": \"protect\" }, \"Development\": { \"ApiKey\": \"plain\" } } }");
            HostApplicationBuilder builder = CreateBuilder(directory.Path);

            builder.AddSwitchableJsonFile(
                "settings",
                "settings.json",
                Options(JsonConfigurationValueProtection.ForPaths(
                    ConfigurationValueCodecs.Base64,
                    "PartnerApi:Production:ApiKey")));

            Assert.AreEqual("protect", builder.Configuration["PartnerApi:Production:ApiKey"]);
            Assert.AreEqual("plain", builder.Configuration["PartnerApi:Development:ApiKey"]);
            string persisted = File.ReadAllText(path);
            Assert.IsTrue(persisted.Contains("\"ApiKey\": \"plain\"", StringComparison.Ordinal));
        }

        [TestMethod]
        public void AutomaticDecoderRunsBeforeApplicationPreparation()
        {
            using var directory = new TemporaryDirectory();
            directory.Write("settings.json", "{ \"ApiToken\": \"secret\" }");
            HostApplicationBuilder builder = CreateBuilder(directory.Path);
            var preparation = new CopyValuePreparation();

            builder.AddSwitchableJsonFile(
                "settings",
                "settings.json",
                new SwitchableJsonRegistrationOptions
                {
                    ValueProtection = JsonConfigurationValueProtection.ForKeys(
                        ConfigurationValueCodecs.Base64,
                        "*Token"),
                    CandidatePreparation = JsonConfigurationCandidatePreparations.From("Copy", preparation),
                });

            Assert.AreEqual("secret", preparation.ObservedValue);
            Assert.AreEqual("secret", builder.Configuration["ObservedAfterProtection"]);
        }

        [TestMethod]
        public void StartupEncodingDoesNotTriggerOwnReloadWatcher()
        {
            using var directory = new TemporaryDirectory();
            directory.Write("settings.json", "{ \"ApiKey\": \"secret\" }");
            HostApplicationBuilder builder = CreateBuilder(directory.Path);
            var preparation = new CountingPreparation();

            builder.AddSwitchableJsonFile(
                "settings",
                "settings.json",
                new SwitchableJsonRegistrationOptions
                {
                    ReloadOnChange = true,
                    ReloadDelayMilliseconds = 25,
                    ValueProtection = JsonConfigurationValueProtection.ForKeys(
                        ConfigurationValueCodecs.Base64,
                        "ApiKey"),
                    CandidatePreparation = JsonConfigurationCandidatePreparations.From("Count", preparation),
                });
            using IHost host = builder.Build();

            Thread.Sleep(250);

            Assert.AreEqual(1, preparation.InvocationCount);
            Assert.AreEqual("secret", builder.Configuration["ApiKey"]);
        }

        [TestMethod]
        public void RepeatedStartupDoesNotRewriteProtectedFile()
        {
            using var directory = new TemporaryDirectory();
            string path = directory.Write("settings.json", "{ \"ApiKey\": \"secret\" }");
            SwitchableJsonRegistrationOptions options = Options(
                JsonConfigurationValueProtection.ForKeys(ConfigurationValueCodecs.Base64, "ApiKey"));
            HostApplicationBuilder firstBuilder = CreateBuilder(directory.Path);
            firstBuilder.AddSwitchableJsonFile("first", "settings.json", options);
            DateTime firstWriteTime = File.GetLastWriteTimeUtc(path);
            string firstContent = File.ReadAllText(path);

            HostApplicationBuilder secondBuilder = CreateBuilder(directory.Path);
            secondBuilder.AddSwitchableJsonFile("second", "settings.json", options);

            Assert.AreEqual(firstWriteTime, File.GetLastWriteTimeUtc(path));
            Assert.AreEqual(firstContent, File.ReadAllText(path));
            Assert.AreEqual("secret", secondBuilder.Configuration["ApiKey"]);
        }

        [TestMethod]
        public void MissingOptionalFileIsNotCreatedByProtection()
        {
            using var directory = new TemporaryDirectory();
            HostApplicationBuilder builder = CreateBuilder(directory.Path);

            builder.AddSwitchableJsonFile(
                "settings",
                "missing.json",
                new SwitchableJsonRegistrationOptions
                {
                    Optional = true,
                    ValueProtection = JsonConfigurationValueProtection.ForKeys(
                        ConfigurationValueCodecs.Base64,
                        "ApiKey"),
                });

            Assert.IsFalse(File.Exists(System.IO.Path.Combine(directory.Path, "missing.json")));
        }

        [TestMethod]
        public void ProtectionRejectsEmptyPatternCollections()
        {
            _ = Assert.ThrowsExactly<ArgumentException>(() =>
                JsonConfigurationValueProtection.ForKeys(ConfigurationValueCodecs.Base64));
            _ = Assert.ThrowsExactly<ArgumentException>(() =>
                JsonConfigurationValueProtection.ForPaths(ConfigurationValueCodecs.Base64, " "));
        }

        private static SwitchableJsonRegistrationOptions Options(JsonConfigurationValueProtection protection)
        {
            return new SwitchableJsonRegistrationOptions { ValueProtection = protection };
        }

        private static HostApplicationBuilder CreateBuilder(string contentRootPath)
        {
            return new HostApplicationBuilder(new HostApplicationBuilderSettings
            {
                ContentRootPath = contentRootPath,
                DisableDefaults = true,
            });
        }

        private sealed class CopyValuePreparation : IJsonConfigurationSourcePreparation
        {
            public string? ObservedValue { get; private set; }

            public void Prepare(JsonConfigurationSourcePreparationContext context)
            {
                ObservedValue = context.Values["ApiToken"];
                context.Values["ObservedAfterProtection"] = ObservedValue;
            }
        }

        private sealed class CountingPreparation : IJsonConfigurationSourcePreparation
        {
            public int InvocationCount { get; private set; }

            public void Prepare(JsonConfigurationSourcePreparationContext context)
            {
                InvocationCount++;
            }
        }

        private sealed class TemporaryDirectory : IDisposable
        {
            public TemporaryDirectory()
            {
                Path = System.IO.Path.Combine(
                    System.IO.Path.GetTempPath(),
                    $"Eigenverft.NetLib.Infrastructure.Protection.Tests.{Guid.NewGuid():N}");
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
