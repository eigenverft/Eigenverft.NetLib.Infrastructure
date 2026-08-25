using System;

using Eigenverft.NetLib.Infrastructure.Hosting;

using Microsoft.Extensions.Hosting;

namespace Eigenverft.NetLib.Infrastructure.Tests.Hosting
{
    [TestClass]
    [DoNotParallelize]
    public sealed class StaticHostEnvironmentTests
    {
        private string? _originalDotnetEnvironment;

        [TestInitialize]
        public void SaveEnvironment()
        {
            _originalDotnetEnvironment = Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT");
        }

        [TestCleanup]
        public void RestoreEnvironment()
        {
            Environment.SetEnvironmentVariable("DOTNET_ENVIRONMENT", _originalDotnetEnvironment);
        }

        [TestMethod]
        public void Resolve_DefaultsToProduction_WhenEnvironmentIsUnset()
        {
            Environment.SetEnvironmentVariable("DOTNET_ENVIRONMENT", null);

            string environmentName = StaticHostEnvironmentResolver.Resolve(Array.Empty<string>());

            Assert.AreEqual(Environments.Production, environmentName);
        }

        [TestMethod]
        public void Resolve_UsesDotnetEnvironment_AndAllowsArbitraryNames()
        {
            Environment.SetEnvironmentVariable("DOTNET_ENVIRONMENT", "QA");

            string environmentName = StaticHostEnvironmentResolver.Resolve(Array.Empty<string>());

            Assert.AreEqual("QA", environmentName);
        }

        [TestMethod]
        public void Resolve_CommandLineOverridesDotnetEnvironment()
        {
            Environment.SetEnvironmentVariable("DOTNET_ENVIRONMENT", Environments.Development);

            string environmentName = StaticHostEnvironmentResolver.Resolve(["--environment", "Blue"]);

            Assert.AreEqual("Blue", environmentName);
        }

        [TestMethod]
        public void PublicEnvironmentPredicates_AreCaseInsensitiveAndConsistent()
        {
            Assert.IsTrue(StaticHostEnvironment.IsEnvironment(StaticHostEnvironment.EnvironmentName.ToUpperInvariant()));
            Assert.AreEqual(StaticHostEnvironment.IsEnvironment(Environments.Development), StaticHostEnvironment.IsDevelopment);
            Assert.AreEqual(StaticHostEnvironment.IsEnvironment(Environments.Production), StaticHostEnvironment.IsProduction);
            Assert.AreEqual(StaticHostEnvironment.IsEnvironment(Environments.Staging), StaticHostEnvironment.IsStaging);
        }
    }
}
