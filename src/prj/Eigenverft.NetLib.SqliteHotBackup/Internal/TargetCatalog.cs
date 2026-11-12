using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Eigenverft.NetLib.SqliteHotBackup.Internal
{
    internal sealed record TargetRegistration(SqliteBackupTarget Target, Type? ContextType = null, bool UsesFactory = false);
    internal sealed record TargetInventory(IReadOnlyList<TargetRegistration> Targets, IReadOnlyList<SqliteBackupDiscoveryFailure> Failures);

    internal static class TargetCatalog
    {
        internal static async Task<TargetInventory> BuildAsync(SqliteHotBackupOptions options, IServiceProvider services,
            ILogger logger, CancellationToken ct)
        {
            Dictionary<string, TargetRegistration> targets = new(StringComparer.OrdinalIgnoreCase);
            List<SqliteBackupDiscoveryFailure> failures = new();
            HashSet<Type> contexts = new();
            bool automatic = options.Discovery.Mode == SqliteBackupDiscoveryMode.Automatic;
            if (automatic && options.Discovery.DiscoverDbContexts)
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
                if (automatic && !contexts.Contains(mapped))
                {
                    throw new ArgumentException($"The ID mapping for '{mapped.FullName}' does not match a discovered context.");
                }
            }
            foreach ((string id, string path) in options.Discovery.Files)
            {
                Add(targets, new(new(id, SqliteBackupSourceKind.File, System.IO.Path.GetFileNameWithoutExtension(path), path)));
            }
            await AddSourceAsync(nameof(ISqliteBackupTargetRegistry),
                () => services.GetRequiredService<ISqliteBackupTargetRegistry>().GetSourcesAsync(ct), targets, failures, logger, ct).ConfigureAwait(false);
            if (automatic)
            {
                foreach (IBackupSourceRegistration source in services.GetServices<IBackupSourceRegistration>())
                {
                    await AddSourceAsync(source.Name, () => source.GetSourcesAsync(services, ct), targets, failures, logger, ct).ConfigureAwait(false);
                }
            }
            foreach (string id in options.OverrideIds)
            {
                if (!targets.ContainsKey(id) && failures.Count == 0)
                {
                    throw new ArgumentException($"ExecutionLimit override references unknown target '{id}'.");
                }
            }
            return new(Array.AsReadOnly(targets.Values.OrderBy(static item => item.Target.Id, StringComparer.OrdinalIgnoreCase).ToArray()),
                failures.AsReadOnly());
        }

        private static async Task AddSourceAsync(string name, Func<Task<IReadOnlyList<SqliteBackupFileSource>>> read,
            Dictionary<string, TargetRegistration> targets, List<SqliteBackupDiscoveryFailure> failures, ILogger logger, CancellationToken ct)
        {
            SqliteBackupFileSource[] snapshot;
            try
            {
                ct.ThrowIfCancellationRequested();
                IReadOnlyList<SqliteBackupFileSource> files = await read().ConfigureAwait(false);
                ct.ThrowIfCancellationRequested();
                ArgumentNullException.ThrowIfNull(files);
                HashSet<string> ids = new(StringComparer.OrdinalIgnoreCase);
                snapshot = files.Select(file =>
                {
                    ArgumentNullException.ThrowIfNull(file);
                    SqliteBackupFileSource normalized = SqliteBackupFileSource.Normalize(file.Id, file.FilePath);
                    if (!ids.Add(normalized.Id))
                    {
                        throw new ArgumentException($"Source '{name}' returned duplicate target ID '{normalized.Id}'.");
                    }
                    return normalized;
                }).ToArray();
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                ct.ThrowIfCancellationRequested();
                BackupLog.DiscoveryFailed(logger, name, exception);
                failures.Add(new(name, exception.GetBaseException().Message));
                return;
            }
            // Source-local failures reject the complete source snapshot. Cross-source ID collisions remain configuration errors.
            foreach (SqliteBackupFileSource file in snapshot)
            {
                Add(targets, new(new(file.Id, SqliteBackupSourceKind.File, System.IO.Path.GetFileNameWithoutExtension(file.FilePath), file.FilePath)));
            }
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
