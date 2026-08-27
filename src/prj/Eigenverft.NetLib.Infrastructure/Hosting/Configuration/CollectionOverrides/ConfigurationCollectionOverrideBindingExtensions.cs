using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Eigenverft.NetLib.Infrastructure.Hosting.Configuration.CollectionOverrides
{
    /// <summary>
    /// Adds configuration binding semantics in which explicitly configured lists and dictionaries replace code defaults.
    /// </summary>
    /// <remarks>
    /// The built-in configuration binder mutates initialized lists and dictionaries. These helpers clear only existing
    /// mutable list and dictionary values whose configuration key is present, then delegate binding and options behavior
    /// back to the framework. Consumer options can keep normal <see cref="List{T}"/> and
    /// <see cref="Dictionary{TKey, TValue}"/> properties.
    /// </remarks>
    public static class ConfigurationCollectionOverrideBindingExtensions
    {
        /// <summary>
        /// Binds configuration while replacing initialized list and dictionary defaults for explicitly configured keys.
        /// </summary>
        /// <param name="configuration">Configuration section to bind.</param>
        /// <param name="instance">Existing options or settings instance.</param>
        /// <param name="configureBinder">Optional native binder configuration.</param>
        [RequiresDynamicCode("Configuration binding and collection preparation may require runtime code generation.")]
        [RequiresUnreferencedCode("Configuration binding and reflection over collection properties require preserved members.")]
        public static void BindReplacingCollectionDefaults(
            this IConfiguration configuration,
            object instance,
            Action<BinderOptions>? configureBinder = null)
        {
            ArgumentNullException.ThrowIfNull(configuration);
            ArgumentNullException.ThrowIfNull(instance);

            ClearConfiguredCollectionDefaults(configuration, instance);

            if (configureBinder is null)
            {
                configuration.Bind(instance);
            }
            else
            {
                configuration.Bind(instance, configureBinder);
            }
        }

        /// <summary>
        /// Registers options binding in which explicitly configured lists and dictionaries replace initialized code defaults.
        /// </summary>
        /// <remarks>
        /// Change tokens, named options, binder options, and final object binding remain framework-owned.
        /// </remarks>
        /// <typeparam name="TOptions">Options type.</typeparam>
        /// <param name="optionsBuilder">Options builder.</param>
        /// <param name="configSectionPath">Configuration section path.</param>
        /// <param name="configureBinder">Optional native binder configuration.</param>
        /// <returns>The same options builder for chaining.</returns>
        [RequiresDynamicCode("Configuration binding and collection preparation may require runtime code generation.")]
        [RequiresUnreferencedCode("Configuration binding and reflection over collection properties require preserved members.")]
        public static OptionsBuilder<TOptions> BindReplacingCollectionDefaults<TOptions>(
            this OptionsBuilder<TOptions> optionsBuilder,
            string configSectionPath,
            Action<BinderOptions>? configureBinder = null)
            where TOptions : class
        {
            ArgumentNullException.ThrowIfNull(optionsBuilder);
            ArgumentNullException.ThrowIfNull(configSectionPath);

            optionsBuilder.Configure<IConfiguration>((options, configuration) =>
            {
                ClearConfiguredCollectionDefaults(configuration.GetSection(configSectionPath), options);
            });

            return optionsBuilder.BindConfiguration(configSectionPath, configureBinder);
        }

        private static void ClearConfiguredCollectionDefaults(IConfiguration configuration, object instance)
        {
            var configuredKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (IConfigurationSection child in configuration.GetChildren())
            {
                configuredKeys.Add(child.Key);
            }

            if (configuredKeys.Count == 0)
            {
                return;
            }

            foreach (PropertyInfo property in instance.GetType().GetProperties(BindingFlags.Instance | BindingFlags.Public))
            {
                if (!property.CanRead || property.GetIndexParameters().Length != 0)
                {
                    continue;
                }

                string key = property.GetCustomAttribute<ConfigurationKeyNameAttribute>()?.Name ?? property.Name;
                if (!configuredKeys.Contains(key))
                {
                    continue;
                }

                object? currentValue = property.GetValue(instance);
                if (currentValue is IDictionary dictionary && !dictionary.IsReadOnly && !dictionary.IsFixedSize)
                {
                    dictionary.Clear();
                }
                else if (currentValue is IList list && !list.IsReadOnly && !list.IsFixedSize)
                {
                    list.Clear();
                }
            }
        }
    }
}
