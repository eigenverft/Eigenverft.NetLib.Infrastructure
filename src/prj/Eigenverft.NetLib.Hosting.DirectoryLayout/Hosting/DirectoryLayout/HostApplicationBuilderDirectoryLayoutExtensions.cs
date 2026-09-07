using System;
using System.Collections.Generic;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Eigenverft.NetLib.Infrastructure.Hosting.DirectoryLayout
{
    /// <summary>
    /// Generic Host extensions for executable-rooted application directory layouts.
    /// </summary>
    public static class HostApplicationBuilderDirectoryLayoutExtensions
    {
        /// <summary>
        /// Creates the standard executable-rooted directory layout and registers it with the host.
        /// </summary>
        /// <param name="builder">The host application builder.</param>
        /// <returns>The same builder for fluent registration.</returns>
        public static IHostApplicationBuilder AddDefaultDirectoryLayout(this IHostApplicationBuilder builder)
        {
            if (builder is null) throw new ArgumentNullException(nameof(builder));

            return SetDirectoryLayout(builder, AppDirectoryLayoutFactory.CreateDefault());
        }

        /// <summary>
        /// Creates the standard executable-rooted directory layout with typed folder-name overrides and registers it with the host.
        /// </summary>
        /// <param name="builder">The host application builder.</param>
        /// <param name="directoryOverrides">Folder-name overrides; unspecified standard directories retain their defaults.</param>
        /// <returns>The same builder for fluent registration.</returns>
        public static IHostApplicationBuilder AddDefaultDirectoryLayout(
            this IHostApplicationBuilder builder,
            IReadOnlyDictionary<DefaultDirectory, string> directoryOverrides)
        {
            if (builder is null) throw new ArgumentNullException(nameof(builder));
            if (directoryOverrides is null) throw new ArgumentNullException(nameof(directoryOverrides));

            return SetDirectoryLayout(builder, AppDirectoryLayoutFactory.CreateDefault(directoryOverrides));
        }

        /// <summary>
        /// Creates an executable-rooted directory layout from custom semantic keys and registers it with the host.
        /// </summary>
        /// <param name="builder">The host application builder.</param>
        /// <param name="folderMap">Semantic keys mapped to direct-child folder names.</param>
        /// <returns>The same builder for fluent registration.</returns>
        public static IHostApplicationBuilder AddDirectoryLayout(
            this IHostApplicationBuilder builder,
            IReadOnlyDictionary<string, string> folderMap)
        {
            if (builder is null) throw new ArgumentNullException(nameof(builder));
            if (folderMap is null) throw new ArgumentNullException(nameof(folderMap));

            return SetDirectoryLayout(builder, AppDirectoryLayoutFactory.Create(folderMap));
        }

        /// <summary>
        /// Gets the directory layout registered on the builder before the host is built.
        /// </summary>
        /// <param name="builder">The host application builder.</param>
        /// <returns>The registered directory layout.</returns>
        /// <exception cref="InvalidOperationException">No layout has been registered.</exception>
        public static IAppDirectoryLayout GetDirectoryLayout(this IHostApplicationBuilder builder)
        {
            if (builder is null) throw new ArgumentNullException(nameof(builder));

            for (int index = builder.Services.Count - 1; index >= 0; index--)
            {
                ServiceDescriptor descriptor = builder.Services[index];

                if (descriptor.ServiceType == typeof(IAppDirectoryLayout) &&
                    descriptor.ImplementationInstance is IAppDirectoryLayout layout)
                {
                    return layout;
                }
            }

            throw new InvalidOperationException(
                "No IAppDirectoryLayout is registered. Call AddDefaultDirectoryLayout(...) or AddDirectoryLayout(...) first.");
        }

        private static IHostApplicationBuilder SetDirectoryLayout(
            IHostApplicationBuilder builder,
            AppDirectoryLayout layout)
        {
            for (int index = builder.Services.Count - 1; index >= 0; index--)
            {
                if (builder.Services[index].ServiceType == typeof(AppDirectoryLayout) ||
                    builder.Services[index].ServiceType == typeof(IAppDirectoryLayout))
                {
                    builder.Services.RemoveAt(index);
                }
            }

            builder.Services.AddSingleton<IAppDirectoryLayout>(layout);
            builder.Services.AddSingleton(layout);
            return builder;
        }
    }
}
