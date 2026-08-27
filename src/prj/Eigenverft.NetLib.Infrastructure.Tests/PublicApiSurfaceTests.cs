using System;
using System.Reflection;

using Eigenverft.NetLib.Infrastructure.Hosting;

using Eigenverft.NetLib.Infrastructure.Hosting.Configuration.ConfigurationSets;
using Eigenverft.NetLib.Infrastructure.Hosting.Configuration.Diagnostics;
using Eigenverft.NetLib.Infrastructure.Hosting.Configuration.Sources;
using Eigenverft.NetLib.Infrastructure.Hosting.Configuration.SwitchableJson;
using Eigenverft.NetLib.Infrastructure.Hosting.Configuration.Values;
using Eigenverft.NetLib.Infrastructure.Hosting.Configuration.CollectionOverrides;
using Eigenverft.NetLib.Infrastructure.Hosting.DirectoryLayout;
using Eigenverft.NetLib.Infrastructure.Hosting.Logging.BootstrapLogger;
using Eigenverft.NetLib.Infrastructure.Networking;
using Eigenverft.NetLib.Infrastructure.Security.Certificates;
using Eigenverft.NetLib.Infrastructure.Transformations;

namespace Eigenverft.NetLib.Infrastructure.Tests
{
    [TestClass]
    public sealed class PublicApiSurfaceTests
    {
        [TestMethod]
        public void DeveloperFacingEntryPointsArePublic()
        {
            Type[] publicTypes =
            {
                typeof(HostApplicationBuilderFactory),
                typeof(HostApplicationBuilderDirectoryLayoutExtensions),
                typeof(StaticHostEnvironment),
                typeof(SelfSignedCertificateFactory),
                typeof(ManagedCertificateFile),
                typeof(ReversibleStringTransform),
                typeof(ReversibleStringTransforms),
                typeof(BootstrapLogger),
                typeof(IpAddressExtensions),
                typeof(CidrNetwork),
                typeof(CidrMatchingExtensions),
                typeof(ConfigurationCollectionOverrideBindingExtensions),
                typeof(ConfigurationSetRegistration),
                typeof(ConfigurationSetDefinition),
                typeof(IConfigurationSetCoordinator),
                typeof(IConfigurationSetManager),
                typeof(IConfigurationSetEventHub),
                typeof(IConfigurationSetDesiredStateStore),
                typeof(ISwitchableJsonConfiguration),
                typeof(SwitchableJsonRegistrationOptions),
                typeof(IJsonConfigurationSourcePreparation),
                typeof(JsonConfigurationCandidatePreparation),
                typeof(JsonConfigurationCandidatePreparations),
                typeof(JsonConfigurationValueProtection),
                typeof(ConfigurationValueCodec),
                typeof(ConfigurationValueCodecs),
                typeof(JsonConfigurationFileEncoder),
                typeof(ConfigurationPrecedenceDiagnosticsExtensions),
                typeof(HostApplicationBuilderConfigurationExtensions),
            };

            foreach (Type type in publicTypes)
            {
                Assert.IsTrue(type.IsPublic, $"Developer-facing type '{type.FullName}' must remain public.");
            }
        }

        [TestMethod]
        public void RuntimeImplementationTypesRemainInternal()
        {
            Assembly assembly = typeof(ConfigurationValueCodec).Assembly;
            string[] internalTypeNames =
            {
                "Eigenverft.NetLib.Infrastructure.Hosting.Configuration.ConfigurationSets.ConfigurationSetCoordinator",
                "Eigenverft.NetLib.Infrastructure.Hosting.Configuration.ConfigurationSets.ConfigurationSetManager",
                "Eigenverft.NetLib.Infrastructure.Hosting.Configuration.ConfigurationSets.ConfigurationSetEventHub",
                "Eigenverft.NetLib.Infrastructure.Hosting.Configuration.SwitchableJson.SwitchableJsonConfigurationRuntime",
                "Eigenverft.NetLib.Infrastructure.Hosting.Configuration.SwitchableJson.SwitchableJsonConfigurationProvider",
                "Eigenverft.NetLib.Infrastructure.Hosting.Configuration.SwitchableJson.SwitchableJsonConfigurationSource",
                "Eigenverft.NetLib.Infrastructure.Hosting.Configuration.SwitchableJson.JsonConfigurationSourcePreparationPipeline",
                "Eigenverft.NetLib.Infrastructure.Hosting.Configuration.Values.ConfigurationValueFormat",
            };

            foreach (string typeName in internalTypeNames)
            {
                Type? type = assembly.GetType(typeName, throwOnError: false);
                Assert.IsNotNull(type, $"Expected implementation type '{typeName}' was not found.");
                Assert.IsFalse(type.IsPublic, $"Implementation type '{typeName}' must not become public API.");
            }
        }

        [TestMethod]
        public void LegacyTransformHelpersAreNotPublicApi()
        {
            MethodInfo[] publicMethods = typeof(ReversibleStringTransforms)
                .GetMethods(BindingFlags.Public | BindingFlags.Static);

            Assert.IsFalse(Array.Exists(publicMethods, method => method.Name == "NormalizeReadablePassword"));
            Assert.IsFalse(Array.Exists(publicMethods, method => method.Name == "TryReverseCaesarPayload"));

            Assert.IsTrue(Array.Exists(publicMethods, method => method.Name == nameof(ReversibleStringTransforms.AesPassword)));
            Assert.IsTrue(Array.Exists(publicMethods, method => method.Name == nameof(ReversibleStringTransforms.Caesar)));
        }
    }
}
