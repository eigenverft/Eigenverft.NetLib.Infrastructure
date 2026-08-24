using System;
using System.Text.Json;

using Microsoft.Extensions.DependencyInjection;

namespace Eigenverft.NetLib.Infrastructure.Hosting.Configuration.WritebackJsonStore
{
    /// <summary>Extension methods for registering <see cref="WritebackJsonStore{T}"/> as a singleton service.</summary>
    public static class ServiceCollectionExtensions
    {
        /// <summary>
        /// Creates a <see cref="WritebackJsonStore{T}"/> for the specified JSON document and registers it as a singleton.
        /// </summary>
        /// <typeparam name="T">The typed settings or document model managed by the store.</typeparam>
        /// <param name="services">The service collection to add the singleton registration to.</param>
        /// <param name="filePath">Absolute or relative path of the backing JSON document.</param>
        /// <param name="watchForExternalChanges">
        /// When <see langword="true"/>, the store watches the backing file and reloads successful external changes into
        /// <see cref="WritebackJsonStore{T}.Current"/>.
        /// </param>
        /// <param name="serializerOptions">Optional serializer options used for load, clone, and save operations.</param>
        /// <returns>The same service collection so additional registrations can be chained.</returns>
        /// <exception cref="ArgumentNullException">
        /// Thrown when <paramref name="services"/> or <paramref name="filePath"/> is <see langword="null"/>.
        /// </exception>
        /// <remarks>
        /// <para>
        /// The registered store exposes two deliberately separate mutable branches. <see cref="WritebackJsonStore{T}.Current"/>
        /// is the file-backed state changed through <see cref="WritebackJsonStore{T}.MutateCurrentAndSave"/> or external
        /// reloads. <see cref="WritebackJsonStore{T}.RuntimeWorkingCopy"/> is a detached in-memory branch for runtime-only
        /// work that must not implicitly change the JSON document.
        /// </para>
        /// <para>
        /// A persisted mutation can be observed independently by normal JSON configuration or SwitchableJson when that
        /// configuration source has reload-on-change enabled. The writeback store itself does not trigger configuration
        /// reloads or participate in configuration-source switching.
        /// </para>
        /// </remarks>
        /// <example>
        /// <code>
        /// services.AddWritebackJsonStore&lt;RuntimeSettings&gt;("Settings/runtime-settings.json");
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
        /// <typeparam name="T">The typed settings or document model managed by the store.</typeparam>
        /// <param name="services">The service collection to add the singleton registration to.</param>
        /// <param name="instance">The existing store instance to register.</param>
        /// <returns>The same service collection so additional registrations can be chained.</returns>
        /// <exception cref="ArgumentNullException">
        /// Thrown when <paramref name="services"/> or <paramref name="instance"/> is <see langword="null"/>.
        /// </exception>
        /// <example>
        /// <code>
        /// var store = new WritebackJsonStore&lt;RuntimeSettings&gt;(path);
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
