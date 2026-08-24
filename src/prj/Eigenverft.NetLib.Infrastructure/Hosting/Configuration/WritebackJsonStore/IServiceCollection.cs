using System;
using System.Text.Json;

using Microsoft.Extensions.DependencyInjection;

namespace Eigenverft.NetLib.Infrastructure.Hosting.Configuration.WritebackJsonStore
{
    /// <summary>Extension methods to register <see cref="WritebackJsonStore{T}"/> in the service collection.</summary>
    public static class ServiceCollectionExtensions
    {
        /// <summary>Creates a <see cref="WritebackJsonStore{T}"/> instance and registers it as a singleton.</summary>
        /// <remarks>
        /// This store supports read–modify–write (writeback) scenarios and can optionally watch the backing file for external changes.
        /// It also exposes a non-persisted <see cref="WritebackJsonStore{T}.WorkingCopy"/> for staging operations (for example decrypting values)
        /// without writing them back to disk.
        /// </remarks>
        /// <typeparam name="T">The settings/document type managed by the store.</typeparam>
        /// <param name="services">The service collection to add the registration to.</param>
        /// <param name="filePath">The absolute or relative path of the JSON settings file.</param>
        /// <param name="watchForExternalChanges">When true, a file watcher monitors the file and reloads it on external changes.</param>
        /// <param name="serializerOptions">Optional JSON serializer options. When null, sensible defaults are used (indented, trailing commas allowed).</param>
        /// <returns>The same service collection so that additional calls can be chained.</returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="services"/> or <paramref name="filePath"/> is null.</exception>
        /// <example>
        /// <code>
        /// services.AddWritebackJsonStore&lt;Settings&gt;("Settings/Eigenverft.App.ReverseProxy.settings.json");
        /// </code>
        /// </example>
        public static IServiceCollection AddWritebackJsonStore<T>(this IServiceCollection services, string filePath, bool watchForExternalChanges = true, JsonSerializerOptions? serializerOptions = null) where T : class, new()
        {
            ArgumentNullException.ThrowIfNull(services);
            ArgumentNullException.ThrowIfNull(filePath);

            var instance = new WritebackJsonStore<T>(filePath, watchForExternalChanges, serializerOptions);
            services.AddSingleton(instance);
            return services;
        }

        /// <summary>Registers an existing <see cref="WritebackJsonStore{T}"/> instance as a singleton.</summary>
        /// <typeparam name="T">The settings/document type managed by the store.</typeparam>
        /// <param name="services">The service collection to add the registration to.</param>
        /// <param name="instance">The existing store instance to register.</param>
        /// <returns>The same service collection so that additional calls can be chained.</returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="services"/> or <paramref name="instance"/> is null.</exception>
        /// <example>
        /// <code>
        /// var store = new WritebackJsonStore&lt;Settings&gt;(path);
        /// services.AddWritebackJsonStore(store);
        /// </code>
        /// </example>
        public static IServiceCollection AddWritebackJsonStore<T>(this IServiceCollection services, WritebackJsonStore<T> instance) where T : class, new()
        {
            ArgumentNullException.ThrowIfNull(services);
            ArgumentNullException.ThrowIfNull(instance);

            services.AddSingleton(instance);
            return services;
        }
    }
}
