using System;
using System.IO;

using Eigenverft.NetLib.Infrastructure.Hosting.Configuration.ConfigurationSets;
using Eigenverft.NetLib.Infrastructure.Hosting.Configuration.SwitchableJson;
using Eigenverft.NetLib.Infrastructure.Hosting.Configuration.Values;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Eigenverft.NetLib.Infrastructure.Tests.Hosting.Configuration.ConfigurationSets
{
    [TestClass]
    public sealed class ConfigurationSetValueProtectionTests
    {
        [TestMethod]
        public void ProtectionCoversSelectedFilesAcrossAllProfilesAndEverySwitchDecodes()
        {
            using var directory = new TemporaryDirectory();
            foreach (string profile in new[] { "Normal", "Degraded", "Incident" })
            {
                directory.Write(
                    Path.Combine("Operations", profile, "Features.json"),
                    $$"""{ "Profile": "{{profile}}", "PartnerApi": { "ApiKey": "{{profile}}-secret" } }""");
                directory.Write(
                    Path.Combine("Operations", profile, "Resilience.json"),
                    $$"""{ "ResilienceToken": "{{profile}}-plain-token" }""");
                directory.Write(
                    Path.Combine("Operations", profile, "Diagnostics.json"),
                    $$"""{ "DiagnosticsPassword": "{{profile}}-plain-password" }""");
            }

            HostApplicationBuilder builder = CreateBuilder(directory.Path);
            ConfigurationSetRegistration operationalProfile = builder.AddConfigurationSet(
                "OperationalProfile",
                "Normal",
                "Degraded",
                "Incident");
            operationalProfile
                .AddSwitchableJson(
                    "Operations",
                    new SwitchableJsonRegistrationOptions
                    {
                        ValueProtection = JsonConfigurationValueProtection.ForKeys(
                            ConfigurationValueCodecs.Base64,
                            "*ApiKey*",
                            "*Token*",
                            "*Password*"),
                    },
                    "Features.json")
                .AddSwitchableJson(
                    "Operations",
                    "Resilience.json",
                    "Diagnostics.json");

            foreach (string profile in new[] { "Normal", "Degraded", "Incident" })
            {
                string features = File.ReadAllText(directory.GetPath("Operations", profile, "Features.json"));
                string resilience = File.ReadAllText(directory.GetPath("Operations", profile, "Resilience.json"));
                string diagnostics = File.ReadAllText(directory.GetPath("Operations", profile, "Diagnostics.json"));
                Assert.IsTrue(features.Contains("enc:q7m2n4:", StringComparison.Ordinal));
                Assert.IsTrue(resilience.Contains($"{profile}-plain-token", StringComparison.Ordinal));
                Assert.IsTrue(diagnostics.Contains($"{profile}-plain-password", StringComparison.Ordinal));
            }

            using IHost host = builder.Build();
            IConfigurationSetCoordinator coordinator =
                host.Services.GetRequiredKeyedService<IConfigurationSetCoordinator>("OperationalProfile");
            AssertProfile(builder, "Normal");

            Assert.AreEqual(ConfigurationSetSwitchStatus.Succeeded, coordinator.TrySwitch("Degraded").Status);
            AssertProfile(builder, "Degraded");
            Assert.AreEqual(ConfigurationSetSwitchStatus.Succeeded, coordinator.TrySwitch("Incident").Status);
            AssertProfile(builder, "Incident");
        }

        [TestMethod]
        public void SwitchReprotectsInactiveProfileChangedToPlainTextAfterStartup()
        {
            using var directory = new TemporaryDirectory();
            directory.Write(
                Path.Combine("Operations", "Normal", "Features.json"),
                "{ \"Profile\": \"Normal\", \"ApiKey\": \"normal-secret\" }");
            string incidentPath = directory.Write(
                Path.Combine("Operations", "Incident", "Features.json"),
                "{ \"Profile\": \"Incident\", \"ApiKey\": \"incident-startup-secret\" }");
            HostApplicationBuilder builder = CreateBuilder(directory.Path);

            builder
                .AddConfigurationSet("OperationalProfile", "Normal", "Incident")
                .AddSwitchableJson(
                    "Operations",
                    new SwitchableJsonRegistrationOptions
                    {
                        ReloadOnChange = true,
                        ReloadDelayMilliseconds = 25,
                        ValueProtection = JsonConfigurationValueProtection.ForKeys(
                            ConfigurationValueCodecs.Base64,
                            "ApiKey"),
                    },
                    "Features.json");
            using IHost host = builder.Build();
            IConfigurationSetCoordinator coordinator =
                host.Services.GetRequiredKeyedService<IConfigurationSetCoordinator>("OperationalProfile");

            // Configuration-set registration protected both profiles at startup. Simulate a later edit while Incident is
            // inactive; there is intentionally no watcher on inactive profile files.
            File.WriteAllText(
                incidentPath,
                "{ \"Profile\": \"Incident\", \"ApiKey\": \"incident-runtime-secret\" }");
            StringAssert.Contains(File.ReadAllText(incidentPath), "incident-runtime-secret");

            ConfigurationSetSwitchResult result = coordinator.TrySwitch("Incident");

            Assert.AreEqual(ConfigurationSetSwitchStatus.Succeeded, result.Status);
            Assert.AreEqual("Incident", coordinator.ActiveValue);
            Assert.AreEqual("incident-runtime-secret", builder.Configuration["ApiKey"]);
            string persisted = File.ReadAllText(incidentPath);
            Assert.IsTrue(persisted.Contains("enc:q7m2n4:", StringComparison.Ordinal));
            Assert.IsFalse(persisted.Contains("\"ApiKey\": \"incident-runtime-secret\"", StringComparison.Ordinal));
        }

        [TestMethod]
        public void MissingInactiveFileRemainsMissingAndUsesExistingSwitchFailureSemantics()
        {
            using var directory = new TemporaryDirectory();
            directory.Write(
                Path.Combine("Operations", "Normal", "Features.json"),
                "{ \"ApiKey\": \"normal-secret\" }");
            HostApplicationBuilder builder = CreateBuilder(directory.Path);

            builder
                .AddConfigurationSet("OperationalProfile", "Normal", "Incident")
                .AddSwitchableJson(
                    "Operations",
                    new SwitchableJsonRegistrationOptions
                    {
                        ValueProtection = JsonConfigurationValueProtection.ForKeys(
                            ConfigurationValueCodecs.Base64,
                            "ApiKey"),
                    },
                    "Features.json");
            string missingPath = directory.GetPath("Operations", "Incident", "Features.json");

            using IHost host = builder.Build();
            IConfigurationSetCoordinator coordinator =
                host.Services.GetRequiredKeyedService<IConfigurationSetCoordinator>("OperationalProfile");
            ConfigurationSetSwitchResult result = coordinator.TrySwitch("Incident");

            Assert.IsFalse(File.Exists(missingPath));
            Assert.AreEqual(ConfigurationSetSwitchStatus.Rejected, result.Status);
            Assert.AreEqual("Normal", coordinator.ActiveValue);
            Assert.AreEqual("normal-secret", builder.Configuration["ApiKey"]);
        }

        private static void AssertProfile(HostApplicationBuilder builder, string profile)
        {
            Assert.AreEqual(profile, builder.Configuration["Profile"]);
            Assert.AreEqual($"{profile}-secret", builder.Configuration["PartnerApi:ApiKey"]);
            Assert.AreEqual($"{profile}-plain-token", builder.Configuration["ResilienceToken"]);
            Assert.AreEqual($"{profile}-plain-password", builder.Configuration["DiagnosticsPassword"]);
        }

        private static HostApplicationBuilder CreateBuilder(string contentRootPath)
        {
            return new HostApplicationBuilder(new HostApplicationBuilderSettings
            {
                ContentRootPath = contentRootPath,
                DisableDefaults = true,
            });
        }

        private sealed class TemporaryDirectory : IDisposable
        {
            public TemporaryDirectory()
            {
                Path = System.IO.Path.Combine(
                    System.IO.Path.GetTempPath(),
                    $"Eigenverft.NetLib.Infrastructure.SetProtection.Tests.{Guid.NewGuid():N}");
                Directory.CreateDirectory(Path);
            }

            public string Path { get; }

            public string GetPath(params string[] segments)
            {
                string path = Path;
                foreach (string segment in segments)
                {
                    path = System.IO.Path.Combine(path, segment);
                }

                return path;
            }

            public string Write(string fileName, string content)
            {
                string path = System.IO.Path.Combine(Path, fileName);
                string? parent = System.IO.Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(parent))
                {
                    Directory.CreateDirectory(parent);
                }

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
