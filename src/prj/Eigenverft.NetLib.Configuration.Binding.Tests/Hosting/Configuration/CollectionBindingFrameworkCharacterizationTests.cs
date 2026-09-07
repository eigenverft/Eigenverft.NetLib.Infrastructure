using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

using Eigenverft.NetLib.Infrastructure.Hosting.Configuration.CollectionOverrides;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Eigenverft.NetLib.Infrastructure.Tests.Hosting.Configuration
{
    [TestClass]
    public sealed class CollectionBindingFrameworkCharacterizationTests
    {
        [TestMethod]
        public void NativeBinderMergesInitializedCollectionDefaults()
        {
            IConfigurationRoot configuration = BuildJson("""
                {
                  "Options": {
                    "ListValues": ["configured-list"],
                    "DictionaryValues": { "configured": "2" }
                  }
                }
                """);

            var options = new CollectionOptions();
            configuration.GetSection("Options").Bind(options);

            CollectionAssert.AreEqual(new[] { "default-list", "configured-list" }, options.ListValues);
            Assert.AreEqual(2, options.DictionaryValues.Count);
            Assert.AreEqual("1", options.DictionaryValues["default"]);
            Assert.AreEqual("2", options.DictionaryValues["configured"]);
        }

        [TestMethod]
        public void NativeBinderKeepsInitializedDefaultsForExplicitlyEmptyCollections()
        {
            IConfigurationRoot configuration = BuildJson("""
                {
                  "Options": {
                    "ListValues": [],
                    "DictionaryValues": {}
                  }
                }
                """);

            var options = new CollectionOptions();
            configuration.GetSection("Options").Bind(options);

            CollectionAssert.AreEqual(new[] { "default-list" }, options.ListValues);
            Assert.AreEqual(1, options.DictionaryValues.Count);
            Assert.AreEqual("1", options.DictionaryValues["default"]);
        }

        [TestMethod]
        public void MissingCollectionKeysKeepCodeDefaultsForEitherEmptyBehavior()
        {
            IConfigurationRoot configuration = BuildJson("""
                {
                  "Options": {
                    "Other": "configured"
                  }
                }
                """);

            foreach (EmptyCollectionBehavior behavior in Enum.GetValues<EmptyCollectionBehavior>())
            {
                var options = new CollectionOptions();
                configuration.GetSection("Options").BindReplacingCollectionDefaults(options, behavior);

                CollectionAssert.AreEqual(new[] { "default-list" }, options.ListValues);
                Assert.AreEqual(1, options.DictionaryValues.Count);
                Assert.AreEqual("1", options.DictionaryValues["default"]);
            }
        }

        [TestMethod]
        public void PresentCollectionsReplaceCodeDefaultsForEitherEmptyBehavior()
        {
            IConfigurationRoot configuration = BuildJson("""
                {
                  "Options": {
                    "ListValues": ["configured-list"],
                    "DictionaryValues": { "configured": "2" }
                  }
                }
                """);

            foreach (EmptyCollectionBehavior behavior in Enum.GetValues<EmptyCollectionBehavior>())
            {
                var options = new CollectionOptions();
                configuration.GetSection("Options").BindReplacingCollectionDefaults(options, behavior);

                CollectionAssert.AreEqual(new[] { "configured-list" }, options.ListValues);
                Assert.AreEqual(1, options.DictionaryValues.Count);
                Assert.AreEqual("2", options.DictionaryValues["configured"]);
                Assert.IsFalse(options.DictionaryValues.ContainsKey("default"));
            }
        }

        [TestMethod]
        public void ExplicitlyEmptyCollectionsCanUseEmptyCollection()
        {
            IConfigurationRoot configuration = BuildJson("""
                {
                  "Options": {
                    "ListValues": [],
                    "DictionaryValues": {}
                  }
                }
                """);

            var options = new CollectionOptions();
            configuration.GetSection("Options").BindReplacingCollectionDefaults(
                options,
                EmptyCollectionBehavior.UseEmptyCollection);

            Assert.HasCount(0, options.ListValues);
            Assert.HasCount(0, options.DictionaryValues);
        }

        [TestMethod]
        public void ExplicitlyEmptyCollectionsCanUseCodeDefaults()
        {
            IConfigurationRoot configuration = BuildJson("""
                {
                  "Options": {
                    "ListValues": [],
                    "DictionaryValues": {}
                  }
                }
                """);

            var options = new CollectionOptions();
            configuration.GetSection("Options").BindReplacingCollectionDefaults(
                options,
                EmptyCollectionBehavior.UseCodeDefaults);

            CollectionAssert.AreEqual(new[] { "default-list" }, options.ListValues);
            Assert.AreEqual(1, options.DictionaryValues.Count);
            Assert.AreEqual("1", options.DictionaryValues["default"]);
        }

        [TestMethod]
        public void OptionsBuilderUsesExplicitEmptyCollectionBehavior()
        {
            IConfigurationRoot configuration = BuildJson("""
                {
                  "Options": {
                    "ListValues": ["configured-list"],
                    "DictionaryValues": {}
                  }
                }
                """);

            var services = new ServiceCollection();
            services.AddSingleton<IConfiguration>(configuration);
            services
                .AddOptions<CollectionOptions>()
                .BindReplacingCollectionDefaults(
                    "Options",
                    EmptyCollectionBehavior.UseEmptyCollection);

            using ServiceProvider provider = services.BuildServiceProvider();
            CollectionOptions options = provider.GetRequiredService<IOptions<CollectionOptions>>().Value;

            CollectionAssert.AreEqual(new[] { "configured-list" }, options.ListValues);
            Assert.HasCount(0, options.DictionaryValues);
        }

        [TestMethod]
        public void OptionsBuilderCanKeepCodeDefaultsForExplicitlyEmptyCollections()
        {
            IConfigurationRoot configuration = BuildJson("""
                {
                  "Options": {
                    "ListValues": [],
                    "DictionaryValues": {}
                  }
                }
                """);

            var services = new ServiceCollection();
            services.AddSingleton<IConfiguration>(configuration);
            services
                .AddOptions<CollectionOptions>()
                .BindReplacingCollectionDefaults(
                    "Options",
                    EmptyCollectionBehavior.UseCodeDefaults);

            using ServiceProvider provider = services.BuildServiceProvider();
            CollectionOptions options = provider.GetRequiredService<IOptions<CollectionOptions>>().Value;

            CollectionAssert.AreEqual(new[] { "default-list" }, options.ListValues);
            Assert.AreEqual("1", options.DictionaryValues["default"]);
        }

        [TestMethod]
        public void OptionsMonitorReloadReappliesUseEmptyCollectionBehavior()
        {
            AssertOptionsMonitorReloadBehavior(
                EmptyCollectionBehavior.UseEmptyCollection,
                expectDefaultsAfterEmptyReload: false);
        }

        [TestMethod]
        public void OptionsMonitorReloadReappliesUseCodeDefaultsBehavior()
        {
            AssertOptionsMonitorReloadBehavior(
                EmptyCollectionBehavior.UseCodeDefaults,
                expectDefaultsAfterEmptyReload: true);
        }

        [TestMethod]
        public void InvalidEmptyCollectionBehaviorIsRejected()
        {
            IConfigurationRoot configuration = BuildJson("""
                {
                  "Options": {
                    "ListValues": ["configured-list"]
                  }
                }
                """);

            var options = new CollectionOptions();
            Assert.ThrowsExactly<ArgumentOutOfRangeException>(() =>
                configuration.GetSection("Options").BindReplacingCollectionDefaults(
                    options,
                    (EmptyCollectionBehavior)999));
        }

        private static void AssertOptionsMonitorReloadBehavior(
            EmptyCollectionBehavior behavior,
            bool expectDefaultsAfterEmptyReload)
        {
            string directory = Path.Combine(Path.GetTempPath(), "Eigenverft.NetLib.Tests", Guid.NewGuid().ToString("N"));
            string fileName = "appsettings.json";
            string filePath = Path.Combine(directory, fileName);
            Directory.CreateDirectory(directory);

            try
            {
                File.WriteAllText(filePath, """
                    {
                      "Options": {
                        "ListValues": ["configured-list"],
                        "DictionaryValues": { "configured": "2" }
                      }
                    }
                    """);

                IConfigurationRoot configuration = new ConfigurationBuilder()
                    .SetBasePath(directory)
                    .AddJsonFile(fileName, optional: false, reloadOnChange: false)
                    .Build();

                var services = new ServiceCollection();
                services.AddSingleton<IConfiguration>(configuration);
                services
                    .AddOptions<CollectionOptions>()
                    .BindReplacingCollectionDefaults("Options", behavior);

                using ServiceProvider provider = services.BuildServiceProvider();
                IOptionsMonitor<CollectionOptions> monitor = provider.GetRequiredService<IOptionsMonitor<CollectionOptions>>();

                CollectionOptions initial = monitor.CurrentValue;
                CollectionAssert.AreEqual(new[] { "configured-list" }, initial.ListValues);
                Assert.AreEqual("2", initial.DictionaryValues["configured"]);

                File.WriteAllText(filePath, """
                    {
                      "Options": {
                        "ListValues": [],
                        "DictionaryValues": {}
                      }
                    }
                    """);
                configuration.Reload();

                CollectionOptions afterEmptyReload = monitor.CurrentValue;
                if (expectDefaultsAfterEmptyReload)
                {
                    CollectionAssert.AreEqual(new[] { "default-list" }, afterEmptyReload.ListValues);
                    Assert.AreEqual(1, afterEmptyReload.DictionaryValues.Count);
                    Assert.AreEqual("1", afterEmptyReload.DictionaryValues["default"]);
                }
                else
                {
                    Assert.HasCount(0, afterEmptyReload.ListValues);
                    Assert.HasCount(0, afterEmptyReload.DictionaryValues);
                }

                File.WriteAllText(filePath, """
                    {
                      "Options": {
                        "Other": "configured-again"
                      }
                    }
                    """);
                configuration.Reload();

                CollectionOptions afterMissingReload = monitor.CurrentValue;
                CollectionAssert.AreEqual(new[] { "default-list" }, afterMissingReload.ListValues);
                Assert.AreEqual(1, afterMissingReload.DictionaryValues.Count);
                Assert.AreEqual("1", afterMissingReload.DictionaryValues["default"]);
            }
            finally
            {
                Directory.Delete(directory, recursive: true);
            }
        }

        private static IConfigurationRoot BuildJson(string json)
        {
            var stream = new MemoryStream(Encoding.UTF8.GetBytes(json));
            return new ConfigurationBuilder()
                .AddJsonStream(stream)
                .Build();
        }

        private sealed class CollectionOptions
        {
            public string Other { get; set; } = "default";

            public List<string> ListValues { get; set; } = new() { "default-list" };

            public Dictionary<string, string> DictionaryValues { get; set; } = new()
            {
                ["default"] = "1",
            };
        }
    }
}
