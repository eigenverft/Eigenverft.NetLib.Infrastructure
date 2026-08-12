using System;

using Microsoft.Extensions.Hosting;

namespace Eigenverft.NetLib.Infrastructure.Hosting.DirectoryLayout
{
    /// <summary>
    /// Creates Generic Host builders with the standard executable-rooted application directory layout.
    /// </summary>
    public static class HostApplicationBuilderFactory
    {
        /// <summary>
        /// Creates a Generic Host builder with the standard application directory layout.
        /// </summary>
        /// <param name="args">Optional command-line arguments to pass to the Generic Host.</param>
        /// <returns>A host builder with the standard directory layout registered.</returns>
        public static HostApplicationBuilder CreateWithDefaultDirectory(string[]? args = null)
        {
            HostApplicationBuilder builder = Host.CreateApplicationBuilder(args ?? Array.Empty<string>());
            builder.AddDefaultDirectoryLayout();
            return builder;
        }
    }
}
