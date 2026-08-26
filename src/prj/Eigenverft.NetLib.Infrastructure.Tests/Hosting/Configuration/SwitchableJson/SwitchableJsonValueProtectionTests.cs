using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

using Eigenverft.NetLib.Infrastructure.Hosting.Configuration.SwitchableJson;
using Eigenverft.NetLib.Infrastructure.Hosting.Configuration.Values;
using Eigenverft.NetLib.Infrastructure.Transformations;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
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
        public void SwitchingToPlainTextCandidateProtectsBeforePreparedWatcherAndPublishesClearText()
        {
            using var directory = new TemporaryDirectory();
            directory.Write("A.json", "{ \"ApiKey\": \"stable-secret\" }");
            string candidatePath = directory.Write("B.json", "{ \"ApiKey\": \"candidate-secret\" }");
            HostApplicationBuilder builder = CreateBuilder(directory.Path);

            builder.AddSwitchableJsonFile(
                "settings",
                "A.json",
                new SwitchableJsonRegistrationOptions
                {
                    ReloadOnChange = true,
                    ReloadDelayMilliseconds = 25,
                    ValueProtection = JsonConfigurationValueProtection.ForKeys(
                        ConfigurationValueCodecs.Base64,
                        "ApiKey"),
                });
            using IHost host = builder.Build();
            ISwitchableJsonConfiguration runtime =
                host.Services.GetRequiredKeyedService<ISwitchableJsonConfiguration>("settings");

            StringAssert.Contains(File.ReadAllText(candidatePath), "candidate-secret");
            SwitchableJsonSwitchResult result = runtime.TrySwitch("B.json");

            Assert.AreEqual(SwitchableJsonSwitchStatus.Succeeded, result.Status);
            Assert.AreEqual("candidate-secret", builder.Configuration["ApiKey"]);
            string persisted = File.ReadAllText(candidatePath);
            Assert.IsTrue(persisted.Contains("enc:q7m2n4:", StringComparison.Ordinal));
            Assert.IsFalse(persisted.Contains("\"ApiKey\": \"candidate-secret\"", StringComparison.Ordinal));
        }

        [TestMethod]
        public async Task ActiveReloadReprotectsExternalPlainTextWithoutRejectingLastKnownGood()
        {
            using var directory = new TemporaryDirectory();
            string path = directory.Write("settings.json", "{ \"ApiKey\": \"initial-secret\" }");
            var preparation = new CountingPreparation();
            HostApplicationBuilder builder = CreateBuilder(directory.Path);
            builder.AddSwitchableJsonFile(
                "settings",
                "settings.json",
                new SwitchableJsonRegistrationOptions
                {
                    ReloadOnChange = true,
                    ReloadDelayMilliseconds = 0,
                    ValueProtection = JsonConfigurationValueProtection.ForKeys(
                        ConfigurationValueCodecs.Base64,
                        "ApiKey"),
                    CandidatePreparation = JsonConfigurationCandidatePreparations.From("Count", preparation),
                });
            using IHost host = builder.Build();
            ISwitchableJsonConfiguration runtime =
                host.Services.GetRequiredKeyedService<ISwitchableJsonConfiguration>("settings");
            var completion = new TaskCompletionSource<SwitchableJsonConfigurationEventArgs>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            int rejectedCount = 0;
            int reloadedCount = 0;
            int changedReloadCount = 0;
            runtime.LifecycleChanged += (_, args) =>
            {
                if (args.Kind == SwitchableJsonConfigurationEventKind.ActiveSourceReloadRejected)
                {
                    Interlocked.Increment(ref rejectedCount);
                }

                if (args.Kind == SwitchableJsonConfigurationEventKind.ActiveSourceReloaded)
                {
                    Interlocked.Increment(ref reloadedCount);
                    if (args.ConfigurationChanged)
                    {
                        Interlocked.Increment(ref changedReloadCount);
                        completion.TrySetResult(args);
                    }
                }
            };

            File.WriteAllText(path, "{ \"ApiKey\": \"runtime-secret\" }");
            SwitchableJsonConfigurationEventArgs reloaded =
                await completion.Task.WaitAsync(TimeSpan.FromSeconds(5));

            Assert.IsTrue(reloaded.ConfigurationChanged);
            Assert.AreEqual("runtime-secret", builder.Configuration["ApiKey"]);

            // File providers may emit several notifications for the protection write, especially with zero debounce. The contract
            // is intentionally weaker than an exact callback count: idempotent follow-up reloads must settle, must not reject LKG,
            // and must not publish another effective configuration change.
            await Task.Delay(300);
            int settledReloads = Volatile.Read(ref reloadedCount);
            int settledPreparations = preparation.InvocationCount;
            await Task.Delay(300);

            Assert.AreEqual(settledReloads, Volatile.Read(ref reloadedCount), "Self-triggered reload notifications did not settle.");
            Assert.AreEqual(settledPreparations, preparation.InvocationCount, "Self-triggered candidate preparation did not settle.");
            Assert.AreEqual(0, Volatile.Read(ref rejectedCount));
            Assert.AreEqual(1, Volatile.Read(ref changedReloadCount));
            Assert.IsGreaterThanOrEqualTo(1, settledReloads);
            Assert.AreEqual(1 + settledReloads, settledPreparations);
            Assert.AreEqual("runtime-secret", builder.Configuration["ApiKey"]);

            string persisted = File.ReadAllText(path);
            Assert.IsTrue(persisted.Contains("enc:q7m2n4:", StringComparison.Ordinal));
            Assert.IsFalse(persisted.Contains("\"ApiKey\": \"runtime-secret\"", StringComparison.Ordinal));
        }

        [TestMethod]
        public void FrameworkReloadReprotectsPlainTextEvenWithoutFileWatching()
        {
            using var directory = new TemporaryDirectory();
            string path = directory.Write("settings.json", "{ \"ApiKey\": \"initial-secret\" }");
            HostApplicationBuilder builder = CreateBuilder(directory.Path);
            builder.AddSwitchableJsonFile(
                "settings",
                "settings.json",
                Options(JsonConfigurationValueProtection.ForKeys(
                    ConfigurationValueCodecs.Base64,
                    "ApiKey")));

            File.WriteAllText(path, "{ \"ApiKey\": \"framework-reload-secret\" }");
            ((IConfigurationRoot)builder.Configuration).Reload();

            Assert.AreEqual("framework-reload-secret", builder.Configuration["ApiKey"]);
            string persisted = File.ReadAllText(path);
            Assert.IsTrue(persisted.Contains("enc:q7m2n4:", StringComparison.Ordinal));
            Assert.IsFalse(persisted.Contains("\"ApiKey\": \"framework-reload-secret\"", StringComparison.Ordinal));
        }

        [TestMethod]
        public void InvalidProtectedSwitchCandidateIsRejectedAndKeepsLastKnownGood()
        {
            using var directory = new TemporaryDirectory();
            directory.Write("A.json", "{ \"ApiKey\": \"stable-secret\" }");
            directory.Write("B.json", "{ invalid-json }");
            HostApplicationBuilder builder = CreateBuilder(directory.Path);
            builder.AddSwitchableJsonFile(
                "settings",
                "A.json",
                Options(JsonConfigurationValueProtection.ForKeys(
                    ConfigurationValueCodecs.Base64,
                    "ApiKey")));
            using IHost host = builder.Build();
            ISwitchableJsonConfiguration runtime =
                host.Services.GetRequiredKeyedService<ISwitchableJsonConfiguration>("settings");

            SwitchableJsonSwitchResult result = runtime.TrySwitch("B.json");

            Assert.AreEqual(SwitchableJsonSwitchStatus.Rejected, result.Status);
            Assert.AreEqual(SwitchableJsonFailureKind.InvalidJson, result.FailureKind);
            Assert.AreEqual("stable-secret", builder.Configuration["ApiKey"]);
            StringAssert.EndsWith(runtime.CurrentSourcePath, "A.json");
        }

        [TestMethod]
        public void NullRootProtectedSwitchCandidateIsRejectedAsInvalidJson()
        {
            using var directory = new TemporaryDirectory();
            directory.Write("A.json", "{ \"ApiKey\": \"stable-secret\" }");
            directory.Write("B.json", "null");
            HostApplicationBuilder builder = CreateBuilder(directory.Path);
            builder.AddSwitchableJsonFile(
                "settings",
                "A.json",
                Options(JsonConfigurationValueProtection.ForKeys(
                    ConfigurationValueCodecs.Base64,
                    "ApiKey")));
            using IHost host = builder.Build();
            ISwitchableJsonConfiguration runtime =
                host.Services.GetRequiredKeyedService<ISwitchableJsonConfiguration>("settings");

            SwitchableJsonSwitchResult result = runtime.TrySwitch("B.json");

            Assert.AreEqual(SwitchableJsonSwitchStatus.Rejected, result.Status);
            Assert.AreEqual(SwitchableJsonFailureKind.InvalidJson, result.FailureKind);
            Assert.AreEqual("stable-secret", builder.Configuration["ApiKey"]);
            StringAssert.EndsWith(runtime.CurrentSourcePath, "A.json");
        }

        [TestMethod]
        public void RuntimeProtectionCodecFailureUsesPreparationFailureAndKeepsLastKnownGood()
        {
            using var directory = new TemporaryDirectory();
            directory.Write("A.json", "{ \"ApiKey\": \"stable-secret\" }");
            string candidatePath = directory.Write("B.json", "{ \"ApiKey\": \"candidate-secret\" }");
            var transform = new ReversibleStringTransform(
                "SelectiveFailure",
                value => value == "candidate-secret"
                    ? throw new InvalidOperationException("candidate protection failed")
                    : $"protected:{value}",
                (string transformed, out string original) =>
                {
                    const string prefix = "protected:";
                    if (!transformed.StartsWith(prefix, StringComparison.Ordinal))
                    {
                        original = transformed;
                        return false;
                    }

                    original = transformed.Substring(prefix.Length);
                    return true;
                });
            var codec = new ConfigurationValueCodec(
                "SelectiveFailure",
                ConfigurationValueKind.Rot13,
                transform);
            HostApplicationBuilder builder = CreateBuilder(directory.Path);
            builder.AddSwitchableJsonFile(
                "settings",
                "A.json",
                Options(JsonConfigurationValueProtection.ForKeys(codec, "ApiKey")));
            using IHost host = builder.Build();
            ISwitchableJsonConfiguration runtime =
                host.Services.GetRequiredKeyedService<ISwitchableJsonConfiguration>("settings");

            SwitchableJsonSwitchResult result = runtime.TrySwitch("B.json");

            Assert.AreEqual(SwitchableJsonSwitchStatus.Rejected, result.Status);
            Assert.AreEqual(SwitchableJsonFailureKind.SourcePreparationFailed, result.FailureKind);
            Assert.IsInstanceOfType<JsonConfigurationSourcePreparationException>(result.Exception);
            Assert.AreEqual("stable-secret", builder.Configuration["ApiKey"]);
            StringAssert.EndsWith(runtime.CurrentSourcePath, "A.json");
            StringAssert.Contains(File.ReadAllText(candidatePath), "candidate-secret");
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

        [TestMethod]
        public void RecoveryReturnsOnlyProtectedRuntimeValues()
        {
            using var directory = new TemporaryDirectory();
            directory.Write(
                "settings.json",
                "{ \"PartnerApi\": { \"ApiKey\": \"secret\", \"Endpoint\": \"https://example.test\" } }");
            HostApplicationBuilder builder = CreateBuilder(directory.Path);

            builder.AddSwitchableJsonFile(
                "settings",
                "settings.json",
                Options(JsonConfigurationValueProtection.ForKeys(ConfigurationValueCodecs.Base64, "ApiKey")));

#pragma warning disable EVFRECOVERY001
            var recovered = ConfigurationValueRecovery.RecoverProtectedValues(builder.Configuration);
#pragma warning restore EVFRECOVERY001

            Assert.HasCount(1, recovered);
            Assert.AreEqual("secret", recovered["PartnerApi:ApiKey"]);
            Assert.IsFalse(recovered.ContainsKey("PartnerApi:Endpoint"));
        }

        [TestMethod]
        public void RecoveryUsesNormalProviderPrecedenceForDuplicateProtectedKeys()
        {
            using var directory = new TemporaryDirectory();
            directory.Write("first.json", "{ \"ApiKey\": \"first-secret\" }");
            directory.Write("second.json", "{ \"ApiKey\": \"second-secret\" }");
            HostApplicationBuilder builder = CreateBuilder(directory.Path);

            builder.AddSwitchableJsonFile(
                "first",
                "first.json",
                Options(JsonConfigurationValueProtection.ForKeys(ConfigurationValueCodecs.Base64, "ApiKey")));
            builder.AddSwitchableJsonFile(
                "second",
                "second.json",
                Options(JsonConfigurationValueProtection.ForKeys(ConfigurationValueCodecs.Base64, "ApiKey")));

#pragma warning disable EVFRECOVERY001
            var recovered = ConfigurationValueRecovery.RecoverProtectedValues(builder.Configuration);
#pragma warning restore EVFRECOVERY001

            Assert.HasCount(1, recovered);
            Assert.AreEqual("second-secret", recovered["ApiKey"]);
        }

        [TestMethod]
        public void RecoverySucceedsForCopiedValueWhenEquivalentProtectionContextIsAvailable()
        {
            using var directory = new TemporaryDirectory();
            string persistedValue = ConfigurationValueCodecs.AesPassword("shared-password").Encode("secret");
            directory.Write("settings.json", $"{{ \"ApiKey\": \"{persistedValue}\" }}");
            HostApplicationBuilder builder = CreateBuilder(directory.Path);

            builder.AddSwitchableJsonFile(
                "settings",
                "settings.json",
                Options(JsonConfigurationValueProtection.ForKeys(
                    ConfigurationValueCodecs.AesPassword("shared-password"),
                    "ApiKey")));

#pragma warning disable EVFRECOVERY001
            var recovered = ConfigurationValueRecovery.RecoverProtectedValues(builder.Configuration);
#pragma warning restore EVFRECOVERY001

            Assert.AreEqual("secret", recovered["ApiKey"]);
        }

        [TestMethod]
        public void RecoveryThrowsWhenProtectedValueCouldNotBeDecodedInCurrentRuntimeContext()
        {
            using var directory = new TemporaryDirectory();
            string persistedValue = ConfigurationValueCodecs.AesPassword("original-password").Encode("secret");
            directory.Write("settings.json", $"{{ \"ApiKey\": \"{persistedValue}\" }}");
            HostApplicationBuilder builder = CreateBuilder(directory.Path);

            builder.AddSwitchableJsonFile(
                "settings",
                "settings.json",
                Options(JsonConfigurationValueProtection.ForKeys(
                    ConfigurationValueCodecs.AesPassword("different-password"),
                    "ApiKey")));

#pragma warning disable EVFRECOVERY001
            InvalidOperationException exception = Assert.ThrowsExactly<InvalidOperationException>(() =>
                ConfigurationValueRecovery.RecoverProtectedValues(builder.Configuration));
#pragma warning restore EVFRECOVERY001

            StringAssert.Contains(exception.Message, "ApiKey");
            StringAssert.Contains(exception.Message, "settings");
            StringAssert.Contains(exception.Message, "AesPassword");
            StringAssert.Contains(exception.Message, "current runtime context");
            StringAssert.Contains(exception.Message, "original server");
        }

        [TestMethod]
        public void RecoveryReturnsEmptyWhenNoValueProtectionIsRegistered()
        {
            using var directory = new TemporaryDirectory();
            directory.Write("settings.json", "{ \"ApiKey\": \"plain\" }");
            HostApplicationBuilder builder = CreateBuilder(directory.Path);

            builder.AddSwitchableJsonFile("settings", "settings.json");

#pragma warning disable EVFRECOVERY001
            var recovered = ConfigurationValueRecovery.RecoverProtectedValues(builder.Configuration);
#pragma warning restore EVFRECOVERY001

            Assert.HasCount(0, recovered);
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
            private int _invocationCount;

            public int InvocationCount => Volatile.Read(ref _invocationCount);

            public void Prepare(JsonConfigurationSourcePreparationContext context)
            {
                Interlocked.Increment(ref _invocationCount);
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
