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
        public void MissingCollectionKeysKeepCodeDefaults()
        {
            IConfigurationRoot configuration = BuildJson("""
                {
                  "Options": {
                    "Other": "configured"
                  }
                }
                """);

            var options = new CollectionOptions();
            configuration.GetSection("Options").BindReplacingCollectionDefaults(options);

            CollectionAssert.AreEqual(new[] { "default-list" }, options.ListValues);
            Assert.AreEqual(1, options.DictionaryValues.Count);
            Assert.AreEqual("1", options.DictionaryValues["default"]);
        }

        [TestMethod]
        public void PresentCollectionsReplaceCodeDefaults()
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
            configuration.GetSection("Options").BindReplacingCollectionDefaults(options);

            CollectionAssert.AreEqual(new[] { "configured-list" }, options.ListValues);
            Assert.AreEqual(1, options.DictionaryValues.Count);
            Assert.AreEqual("2", options.DictionaryValues["configured"]);
            Assert.IsFalse(options.DictionaryValues.ContainsKey("default"));
        }

        [TestMethod]
        public void ExplicitlyEmptyCollectionsReplaceCodeDefaultsWithEmptyCollections()
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
            configuration.GetSection("Options").BindReplacingCollectionDefaults(options);

            Assert.HasCount(0, options.ListValues);
            Assert.HasCount(0, options.DictionaryValues);
        }

        [TestMethod]
        public void OptionsBuilderUsesSameReplacementSemantics()
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
                .BindReplacingCollectionDefaults("Options");

            using ServiceProvider provider = services.BuildServiceProvider();
            CollectionOptions options = provider.GetRequiredService<IOptions<CollectionOptions>>().Value;

            CollectionAssert.AreEqual(new[] { "configured-list" }, options.ListValues);
            Assert.HasCount(0, options.DictionaryValues);
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
