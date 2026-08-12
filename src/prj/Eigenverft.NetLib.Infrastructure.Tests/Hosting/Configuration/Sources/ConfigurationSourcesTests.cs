using System;
using System.Collections.Generic;

using Eigenverft.NetLib.Infrastructure.Hosting.Configuration.Sources;

using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Configuration.CommandLine;
using Microsoft.Extensions.Configuration.EnvironmentVariables;

namespace Eigenverft.NetLib.Infrastructure.Tests.Hosting.Configuration.Sources;

[TestClass]
public sealed class ConfigurationSourcesTests
{
    [TestMethod]
    public void MinimalSourcesReplaceExistingSourcesWithEnvironmentVariables()
    {
        HostApplicationBuilder builder = CreateBuilder();
        builder.Configuration.AddInMemoryCollection(
            new Dictionary<string, string?> { ["Previous"] = "value" });

        HostApplicationBuilder result = builder.ResetToMinimalConfigurationSources();
        IList<IConfigurationSource> sources = GetSources(builder);

        Assert.AreSame(builder, result);
        Assert.AreEqual(1, sources.Count);
        Assert.IsInstanceOfType<EnvironmentVariablesConfigurationSource>(sources[0]);
    }

    [TestMethod]
    public void CommandLineArgumentsAreAddedAfterEnvironmentVariables()
    {
        HostApplicationBuilder builder = CreateBuilder();

        builder.ResetToMinimalConfigurationSources(includeCommandLineArguments: true);
        IList<IConfigurationSource> sources = GetSources(builder);

        Assert.AreEqual(2, sources.Count);
        Assert.IsInstanceOfType<EnvironmentVariablesConfigurationSource>(sources[0]);
        Assert.IsInstanceOfType<CommandLineConfigurationSource>(sources[1]);
    }

    [TestMethod]
    public void AllMinimalSourcesCanBeDisabled()
    {
        HostApplicationBuilder builder = CreateBuilder();

        builder.ResetToMinimalConfigurationSources(
            includeCommandLineArguments: false,
            includeEnvironmentVariables: false);

        Assert.AreEqual(0, GetSources(builder).Count);
    }

    [TestMethod]
    public void NullBuilderIsRejected()
    {
        Assert.ThrowsExactly<ArgumentNullException>(() =>
            HostApplicationBuilderConfigurationExtensions.ResetToMinimalConfigurationSources<HostApplicationBuilder>(null!));
    }

    private static HostApplicationBuilder CreateBuilder()
    {
        return Host.CreateApplicationBuilder(Array.Empty<string>());
    }

    private static IList<IConfigurationSource> GetSources(HostApplicationBuilder builder)
    {
        return ((IConfigurationBuilder)builder.Configuration).Sources;
    }
}
