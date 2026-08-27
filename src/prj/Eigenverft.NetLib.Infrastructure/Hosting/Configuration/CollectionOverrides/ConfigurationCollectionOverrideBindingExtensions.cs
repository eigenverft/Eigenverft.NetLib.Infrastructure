using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Reflection;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Eigenverft.NetLib.Infrastructure.Hosting.Configuration.CollectionOverrides
{
    /// <summary>
    /// Adds configuration binding semantics in which an explicitly configured collection replaces code defaults.
    /// </summary>
    /// <remarks>
    /// The built-in configuration binder mutates initialized lists and dictionaries, which normally appends or merges
    /// configured values with code defaults. These helpers clear only collection properties whose configuration key is
    /// explicitly present, then delegate the actual binding and options reload behavior back to the framework.
    /// </remarks>
    public static class ConfigurationCollectionOverrideBindingExtensions
    {
        /// <summary>
        /// Binds configuration while replacing defaults for explicitly configured mutable collection properties.
        /// </summary>
        /// <remarks>
        /// Missing collection keys leave initialized defaults untouched. Present collection keys are cleared before the
        /// native binder runs, including present-but-empty keys surfaced by the active configuration provider.
        /// </remarks>
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

            PrepareConfiguredCollections(configuration, instance);

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
        /// Registers options binding in which explicitly configured collections replace initialized code defaults.
        /// </summary>
        /// <remarks>
        /// This is a thin preparation layer over the native <c>BindConfiguration</c> options integration. Change-token
        /// handling, named options behavior, binder options, and the final object binding remain framework-owned.
        /// </remarks>
        /// <typeparam name="TOptions">Options type.</typeparam>
        /// <param name="optionsBuilder">Options builder.</param>
        /// <param name="configSectionPath">Configuration section path.</param>
        /// <param name="configureBinder">Optional native binder configuration.</param>
        /// <returns>The same options builder for chaining.</returns>
        [RequiresDynamicCode("Configuration binding and collection preparation may require runtime code generation.")]
        [RequiresUnreferencedCode("Configuration binding and reflection over collection properties require preserved members.")]
        public static OptionsBuilder<TOptions> BindConfigurationReplacingCollectionDefaults<TOptions>(
            this OptionsBuilder<TOptions> optionsBuilder,
            string configSectionPath,
            Action<BinderOptions>? configureBinder = null)
            where TOptions : class
        {
            ArgumentNullException.ThrowIfNull(optionsBuilder);
            ArgumentNullException.ThrowIfNull(configSectionPath);

            optionsBuilder.Configure<IConfiguration>((options, configuration) =>
            {
                PrepareConfiguredCollections(configuration.GetSection(configSectionPath), options);
            });

            return optionsBuilder.BindConfiguration(configSectionPath, configureBinder);
        }

        private static void PrepareConfiguredCollections(IConfiguration configuration, object instance)
        {
            Dictionary<string, IConfigurationSection> configuredChildren = configuration
                .GetChildren()
                .ToDictionary(static section => section.Key, StringComparer.OrdinalIgnoreCase);

            if (configuredChildren.Count == 0)
            {
                return;
            }

            PropertyInfo[] properties = instance.GetType().GetProperties(BindingFlags.Instance | BindingFlags.Public);
            for (int i = 0; i < properties.Length; i++)
            {
                PropertyInfo property = properties[i];
                if (!property.CanRead || property.GetIndexParameters().Length != 0)
                {
                    continue;
                }

                string key = property.GetCustomAttribute<ConfigurationKeyNameAttribute>()?.Name ?? property.Name;
                if (!configuredChildren.TryGetValue(key, out IConfigurationSection? childSection))
                {
                    continue;
                }

                object? currentValue = property.GetValue(instance);
                CollectionPreparationResult result = PrepareCollectionProperty(property, instance, currentValue);
                if (result == CollectionPreparationResult.Prepared)
                {
                    continue;
                }

                if (result == CollectionPreparationResult.Unsupported)
                {
                    throw new InvalidOperationException(
                        $"Configured collection property '{instance.GetType().FullName}.{property.Name}' cannot be reset before binding.");
                }

                if (currentValue is not null && childSection.GetChildren().Any())
                {
                    PrepareConfiguredCollections(childSection, currentValue);
                }
            }
        }

        private static CollectionPreparationResult PrepareCollectionProperty(
            PropertyInfo property,
            object instance,
            object? currentValue)
        {
            Type propertyType = property.PropertyType;
            if (propertyType == typeof(string))
            {
                return CollectionPreparationResult.NotCollection;
            }

            if (propertyType.IsArray)
            {
                if (!property.CanWrite)
                {
                    return CollectionPreparationResult.Unsupported;
                }

                Type elementType = propertyType.GetElementType()!;
                property.SetValue(instance, Array.CreateInstance(elementType, 0));
                return CollectionPreparationResult.Prepared;
            }

            Type? collectionInterface = FindGenericCollectionInterface(propertyType);
            bool isCollectionType = typeof(IList).IsAssignableFrom(propertyType)
                || typeof(IDictionary).IsAssignableFrom(propertyType)
                || collectionInterface is not null;

            if (!isCollectionType)
            {
                return CollectionPreparationResult.NotCollection;
            }

            if (currentValue is null)
            {
                return CollectionPreparationResult.Prepared;
            }

            if (currentValue is IDictionary dictionary)
            {
                if (dictionary.IsReadOnly || dictionary.IsFixedSize)
                {
                    return CollectionPreparationResult.Unsupported;
                }

                dictionary.Clear();
                return CollectionPreparationResult.Prepared;
            }

            if (currentValue is IList list)
            {
                if (list.IsReadOnly || list.IsFixedSize)
                {
                    return CollectionPreparationResult.Unsupported;
                }

                list.Clear();
                return CollectionPreparationResult.Prepared;
            }

            Type? runtimeCollectionInterface = FindGenericCollectionInterface(currentValue.GetType()) ?? collectionInterface;
            if (runtimeCollectionInterface is null)
            {
                return CollectionPreparationResult.Unsupported;
            }

            PropertyInfo? isReadOnlyProperty = runtimeCollectionInterface.GetProperty(nameof(ICollection<int>.IsReadOnly));
            if (isReadOnlyProperty?.GetValue(currentValue) is true)
            {
                return CollectionPreparationResult.Unsupported;
            }

            MethodInfo? clearMethod = runtimeCollectionInterface.GetMethod(nameof(ICollection<int>.Clear));
            if (clearMethod is null)
            {
                return CollectionPreparationResult.Unsupported;
            }

            clearMethod.Invoke(currentValue, null);
            return CollectionPreparationResult.Prepared;
        }

        private static Type? FindGenericCollectionInterface(Type type)
        {
            if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(ICollection<>))
            {
                return type;
            }

            return type
                .GetInterfaces()
                .FirstOrDefault(static candidate =>
                    candidate.IsGenericType && candidate.GetGenericTypeDefinition() == typeof(ICollection<>));
        }

        private enum CollectionPreparationResult
        {
            NotCollection,
            Prepared,
            Unsupported,
        }
    }
}
