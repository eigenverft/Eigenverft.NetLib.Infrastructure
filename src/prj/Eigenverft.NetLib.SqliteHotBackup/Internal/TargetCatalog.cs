using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Eigenverft.NetLib.SqliteHotBackup.Internal
{
    internal sealed record TargetRegistration(SqliteBackupTarget Target, Type? ContextType = null, bool UsesFactory = false);

    internal static class TargetCatalog
    {
        internal static IReadOnlyList<TargetRegistration> Build(SqliteHotBackupOptions options, IServiceProvider services)
        {
            Dictionary<string, TargetRegistration> targets = new(StringComparer.OrdinalIgnoreCase);
            HashSet<Type> contexts = new();
            if (options.Discovery.DiscoverDbContexts)
            {
                IServiceProviderIsService registered = services.GetRequiredService<IServiceProviderIsService>();
                foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies().Where(static assembly => !assembly.IsDynamic))
                {
                    foreach (Type type in Types(assembly))
                    {
                        if (type.IsAbstract || type.ContainsGenericParameters || !typeof(DbContext).IsAssignableFrom(type))
                        {
                            continue;
                        }
                        bool direct = registered.IsService(type);
                        bool factory = registered.IsService(typeof(IDbContextFactory<>).MakeGenericType(type));
                        if (!direct && !factory)
                        {
                            continue;
                        }
                        contexts.Add(type);
                        string id = options.Discovery.ContextIds.GetValueOrDefault(type) ?? "ef:" + type.FullName;
                        Add(targets, new(new(id, SqliteBackupSourceKind.DbContext, type.Name, null), type, !direct));
                    }
                }
            }
            foreach (Type mapped in options.Discovery.ContextIds.Keys)
            {
                if (!contexts.Contains(mapped))
                {
                    throw new ArgumentException($"The ID mapping for '{mapped.FullName}' does not match a discovered context.");
                }
            }
            foreach ((string id, string path) in options.Discovery.Files)
            {
                Add(targets, new(new(id, SqliteBackupSourceKind.File, System.IO.Path.GetFileNameWithoutExtension(path), path)));
            }
            foreach (string id in options.OverrideIds)
            {
                if (!targets.ContainsKey(id))
                {
                    throw new ArgumentException($"ExecutionLimit override references unknown target '{id}'.");
                }
            }
            return targets.Values.OrderBy(static item => item.Target.Id, StringComparer.OrdinalIgnoreCase).ToArray();
        }

        private static void Add(Dictionary<string, TargetRegistration> targets, TargetRegistration target)
        {
            if (!targets.TryAdd(target.Target.Id, target))
            {
                throw new ArgumentException($"Backup target ID '{target.Target.Id}' is configured more than once.");
            }
        }

        private static IEnumerable<Type> Types(Assembly assembly)
        {
            try
            {
                return assembly.GetTypes();
            }
            catch (ReflectionTypeLoadException exception)
            {
                return exception.Types.OfType<Type>();
            }
        }
    }
}
