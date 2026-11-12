using System;
using System.Threading;
using System.Threading.Tasks;

using Eigenverft.NetLib.SqliteHotBackup.Internal;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Eigenverft.NetLib.SqliteHotBackup
{
    /// <summary>Registers SQLite backup behavior without a scheduler or ASP.NET Core dependency.</summary>
    public static class SqliteHotBackupServiceCollectionExtensions
    {
        /// <summary>Adds the lazy target inventory, singleton backup orchestrator, and optional shutdown handling.</summary>
        public static IServiceCollection AddSqliteHotBackup(this IServiceCollection services, Action<SqliteHotBackupOptions>? configure = null)
        {
            ArgumentNullException.ThrowIfNull(services);
            services.AddOptions<SqliteHotBackupOptions>();
            if (configure is not null)
            {
                services.Configure(configure);
            }
            services.AddLogging();
            services.TryAddSingleton<TimeProvider>(TimeProvider.System);
            services.TryAddSingleton<ISqliteBackupTargetRegistry, SqliteBackupTargetRegistry>();
            services.TryAddSingleton<SqliteHotBackupOrchestrator>(static provider => new(provider,
                provider.GetRequiredService<IOptions<SqliteHotBackupOptions>>(), provider.GetRequiredService<TimeProvider>(),
                provider.GetRequiredService<ILogger<SqliteHotBackupOrchestrator>>()));
            services.TryAddSingleton<ISqliteHotBackupOrchestrator>(static provider => provider.GetRequiredService<SqliteHotBackupOrchestrator>());
            services.TryAddEnumerable(ServiceDescriptor.Singleton<IHostedService, ShutdownCheckpoint>());
            return services;
        }

        /// <summary>Registers an automatic file-source provider, scoped to each inventory/backup/checkpoint call.</summary>
        /// <remarks>Repeated registration of the same type is idempotent. An existing registration of TProvider retains its lifetime.</remarks>
        public static IServiceCollection AddSqliteBackupSourceProvider<TProvider>(this IServiceCollection services)
            where TProvider : class, ISqliteBackupSourceProvider
        {
            ArgumentNullException.ThrowIfNull(services);
            services.TryAddScoped<TProvider>();
            services.TryAddEnumerable(ServiceDescriptor.Singleton<IBackupSourceRegistration, BackupSourceRegistration<TProvider>>());
            return services;
        }
    }

    internal sealed class ShutdownCheckpoint : IHostedService
    {
        private readonly SqliteHotBackupOrchestrator _orchestrator;
        private readonly IOptions<SqliteHotBackupOptions> _options;
        private readonly ILogger<ShutdownCheckpoint> _logger;

        public ShutdownCheckpoint(SqliteHotBackupOrchestrator orchestrator, IOptions<SqliteHotBackupOptions> options,
            ILogger<ShutdownCheckpoint> logger)
        {
            _orchestrator = orchestrator;
            _options = options;
            _logger = logger;
        }

        public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public async Task StopAsync(CancellationToken cancellationToken)
        {
            if (!_options.Value.Shutdown.CheckpointWal || cancellationToken.IsCancellationRequested)
            {
                return;
            }
            try
            {
                await _orchestrator.CheckpointAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                BackupLog.ShutdownCanceled(_logger);
            }
            catch (Exception exception)
            {
                BackupLog.ShutdownFailed(_logger, exception);
            }
        }
    }
}
