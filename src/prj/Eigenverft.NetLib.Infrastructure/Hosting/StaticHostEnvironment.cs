using System;
using System.Linq;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

namespace Eigenverft.NetLib.Infrastructure.Hosting
{
    /// <summary>
    /// Exposes the process-level Generic Host environment before a host builder exists.
    /// </summary>
    /// <remarks>
    /// Resolution follows the Generic Host defaults: <c>DOTNET_</c>-prefixed environment variables,
    /// then process command-line arguments, with <see cref="Environments.Production"/> as the default.
    /// The resolved value is captured once when this type is first initialized.
    /// </remarks>
    public static class StaticHostEnvironment
    {
        private static readonly string ResolvedEnvironmentName = StaticHostEnvironmentResolver.Resolve(
            System.Environment.GetCommandLineArgs().Skip(1).ToArray());

        /// <summary>
        /// Gets the resolved Generic Host environment name.
        /// </summary>
        public static string EnvironmentName => ResolvedEnvironmentName;

        /// <summary>
        /// Gets whether the resolved environment is <see cref="Environments.Development"/>.
        /// </summary>
        public static bool IsDevelopment => IsEnvironment(Environments.Development);

        /// <summary>
        /// Gets whether the resolved environment is <see cref="Environments.Production"/>.
        /// </summary>
        public static bool IsProduction => IsEnvironment(Environments.Production);

        /// <summary>
        /// Gets whether the resolved environment is <see cref="Environments.Staging"/>.
        /// </summary>
        public static bool IsStaging => IsEnvironment(Environments.Staging);

        /// <summary>
        /// Determines whether the resolved environment matches <paramref name="environmentName"/>.
        /// </summary>
        public static bool IsEnvironment(string environmentName)
        {
            return string.Equals(EnvironmentName, environmentName, StringComparison.OrdinalIgnoreCase);
        }
    }

    internal static class StaticHostEnvironmentResolver
    {
        internal static string Resolve(string[]? args)
        {
            var configuration = new ConfigurationManager();
            configuration.AddEnvironmentVariables(prefix: "DOTNET_");

            if (args is { Length: > 0 })
            {
                configuration.AddCommandLine(args);
            }

            return configuration[HostDefaults.EnvironmentKey] ?? Environments.Production;
        }
    }
}
