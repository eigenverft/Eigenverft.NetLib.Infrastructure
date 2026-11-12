using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Eigenverft.NetLib.SqliteHotBackup.Internal
{
    internal sealed class SqliteHotBackupOrchestrator : ISqliteHotBackupOrchestrator, IDisposable
    {
        private readonly IServiceProvider _services;
        private readonly IOptions<SqliteHotBackupOptions> _options;
        private readonly TimeProvider _time;
        private readonly ILogger<SqliteHotBackupOrchestrator> _logger;
        private readonly SemaphoreSlim _gate = new(1, 1);

        internal SqliteHotBackupOrchestrator(IServiceProvider services, IOptions<SqliteHotBackupOptions> options,
            TimeProvider time, ILogger<SqliteHotBackupOrchestrator> logger)
        {
            _services = services;
            _options = options;
            _time = time;
            _logger = logger;
        }

        public async Task<IReadOnlyList<SqliteBackupTarget>> GetTargetsAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            SqliteHotBackupOptions options = Snapshot(null);
            await using AsyncServiceScope scope = _services.CreateAsyncScope();
            TargetInventory inventory = await TargetCatalog.BuildAsync(options, scope.ServiceProvider, _logger, cancellationToken).ConfigureAwait(false);
            IReadOnlyList<SqliteBackupTarget> targets = Array.AsReadOnly(inventory.Targets
                .Select(static item => item.Target).ToArray());
            if (inventory.Failures.Count != 0)
            {
                throw new SqliteBackupDiscoveryException(targets, inventory.Failures);
            }
            return targets;
        }

        public Task<SqliteBackupRunResult> BackupAllAsync(CancellationToken cancellationToken = default)
            => RunAsync(null, null, cancellationToken);

        public Task<SqliteBackupRunResult> BackupAllAsync(Action<SqliteHotBackupOptions> overrideOptions, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(overrideOptions);
            return RunAsync(null, overrideOptions, cancellationToken);
        }

        public Task<SqliteBackupRunResult> BackupAsync(string targetId, CancellationToken cancellationToken = default)
            => RunAsync(new[] { TargetId.Normalize(targetId) }, null, cancellationToken);

        public Task<SqliteBackupRunResult> BackupAsync(IEnumerable<string> targetIds, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(targetIds);
            return RunAsync(targetIds, null, cancellationToken);
        }

        public Task<SqliteBackupRunResult> BackupAsync(IEnumerable<string> targetIds, Action<SqliteHotBackupOptions> overrideOptions,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(targetIds);
            ArgumentNullException.ThrowIfNull(overrideOptions);
            return RunAsync(targetIds, overrideOptions, cancellationToken);
        }

        private SqliteHotBackupOptions Snapshot(Action<SqliteHotBackupOptions>? configure)
        {
            SqliteHotBackupOptions options = _options.Value.Copy();
            configure?.Invoke(options);
            // A second copy validates nullable replacements made by a per-run callback.
            options = options.Copy();
            options.Validate();
            return options;
        }

        private async Task<SqliteBackupRunResult> RunAsync(IEnumerable<string>? requestedIds,
            Action<SqliteHotBackupOptions>? configure, CancellationToken ct)
        {
            await _gate.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                SqliteHotBackupOptions options = Snapshot(configure);
                HashSet<string>? selection = null;
                if (requestedIds is not null)
                {
                    selection = new(requestedIds.Select(TargetId.Normalize), StringComparer.OrdinalIgnoreCase);
                    if (selection.Count == 0)
                    {
                        throw new ArgumentException("Select at least one target, or call BackupAllAsync explicitly.", nameof(requestedIds));
                    }
                }

                List<SqliteBackupResult> results = new();
                List<ResolvedSource> sources = new();
                await using AsyncServiceScope scope = _services.CreateAsyncScope();
                TargetInventory inventory = await TargetCatalog.BuildAsync(options, scope.ServiceProvider, _logger, ct).ConfigureAwait(false);
                IReadOnlyList<TargetRegistration> catalog = inventory.Targets;
                if (selection is not null)
                {
                    string[] unknown = selection.Except(catalog.Select(static item => item.Target.Id), StringComparer.OrdinalIgnoreCase).ToArray();
                    if (unknown.Length != 0 && inventory.Failures.Count == 0)
                    {
                        throw new ArgumentException("Unknown backup target IDs: " + string.Join(", ", unknown), nameof(requestedIds));
                    }
                    // An unavailable provider cannot establish that a requested ID is unknown. Keep the healthy selection usable.
                    foreach (string id in unknown)
                    {
                        results.Add(new(Array.AsReadOnly(new[] { id }), SqliteBackupStatus.Failed, null, null,
                            "Target unavailable because source discovery is incomplete.", TimeSpan.Zero));
                    }
                }
                foreach (TargetRegistration registration in catalog.Where(item => selection is null || selection.Contains(item.Target.Id)))
                {
                    ct.ThrowIfCancellationRequested();
                    long started = _time.GetTimestamp();
                    try
                    {
                        SourceResolution resolution = await SourceResolver.ResolveAsync(registration, options, scope.ServiceProvider, _time, ct).ConfigureAwait(false);
                        if (resolution.Source is ResolvedSource source)
                        {
                            sources.Add(source);
                        }
                        else
                        {
                            results.Add(new(Array.AsReadOnly(new[] { registration.Target.Id }),
                                selection is null ? SqliteBackupStatus.Skipped : SqliteBackupStatus.Failed,
                                registration.Target.SourceFile, null, resolution.IneligibleReason, _time.GetElapsedTime(started)));
                        }
                    }
                    catch (OperationCanceledException) when (ct.IsCancellationRequested)
                    {
                        throw;
                    }
                    catch (Exception exception)
                    {
                        ct.ThrowIfCancellationRequested();
                        BackupLog.ResolutionFailed(_logger, registration.Target.Id, exception);
                        results.Add(new(Array.AsReadOnly(new[] { registration.Target.Id }), SqliteBackupStatus.Failed,
                            registration.Target.SourceFile, null, exception.GetBaseException().Message, _time.GetElapsedTime(started)));
                    }
                }

                StringComparer paths = OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
                foreach (IGrouping<string, ResolvedSource> group in sources.GroupBy(static source => source.Path, paths))
                {
                    ct.ThrowIfCancellationRequested();
                    ResolvedSource source = group.First();
                    ResolvedSource[] explicitLimits = group.Where(static item => item.HasOverride).ToArray();
                    IReadOnlyList<string> ids = Array.AsReadOnly(group.Select(static item => item.Registration.Target.Id).ToArray());
                    TimeSpan preparation = TimeSpan.FromTicks(group.Sum(static item => item.PreparationTime.Ticks));
                    if (explicitLimits.Length != 0)
                    {
                        source = explicitLimits[0];
                        if (explicitLimits.Any(item => item.Limit.NoProgressTimeout != source.Limit.NoProgressTimeout
                            || item.Limit.MaxRewindWorkFactor != source.Limit.MaxRewindWorkFactor))
                        {
                            results.Add(new(ids, SqliteBackupStatus.Failed, group.Key, null,
                                "Selected aliases configure conflicting explicit execution limits for this source.", preparation));
                            continue;
                        }
                    }
                    results.Add(await SqliteCopy.ExecuteAsync(source, ids, preparation, options, _time, _logger, ct).ConfigureAwait(false));
                }

                ct.ThrowIfCancellationRequested();
                SqliteBackupArchiveResult? archive = await BackupArchive.CreateAsync(results, options.Zip, _time, _logger, ct).ConfigureAwait(false);
                ct.ThrowIfCancellationRequested();
                SqliteBackupRunResult report = new(results.AsReadOnly(), archive) { DiscoveryFailures = inventory.Failures };
                bool hasFailures = report.HasFailures;
                BackupLog.RunCompleted(_logger, results.Count, hasFailures);
                if (hasFailures)
                {
                    throw new SqliteBackupException(report);
                }
                return report;
            }
            finally
            {
                _gate.Release();
            }
        }

        internal async Task CheckpointAsync(CancellationToken ct)
        {
            await _gate.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                SqliteHotBackupOptions options = Snapshot(null);
                await using AsyncServiceScope scope = _services.CreateAsyncScope();
                TargetInventory inventory = await TargetCatalog.BuildAsync(options, scope.ServiceProvider, _logger, ct).ConfigureAwait(false);
                HashSet<string> seen = new(OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
                foreach (TargetRegistration registration in inventory.Targets)
                {
                    ct.ThrowIfCancellationRequested();
                    try
                    {
                        SourceResolution resolved = await SourceResolver.ResolveAsync(registration, options, scope.ServiceProvider, _time, ct).ConfigureAwait(false);
                        if (resolved.Source is not ResolvedSource source || !seen.Add(source.Path))
                        {
                            continue;
                        }
                        SqliteConnectionStringBuilder settings = new(source.ConnectionString) { Mode = SqliteOpenMode.ReadWrite };
                        await using SqliteConnection connection = new(settings.ToString());
                        await connection.OpenAsync(ct).ConfigureAwait(false);
                        await using SqliteCommand command = connection.CreateCommand();
                        command.CommandText = "PRAGMA wal_checkpoint(TRUNCATE);";
                        await using SqliteDataReader reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
                        if (await reader.ReadAsync(ct).ConfigureAwait(false))
                        {
                            BackupLog.CheckpointCompleted(_logger,
                                source.Path, reader.GetInt32(0), reader.GetInt32(1), reader.GetInt32(2));
                        }
                    }
                    catch (OperationCanceledException) when (ct.IsCancellationRequested)
                    {
                        throw;
                    }
                    catch (Exception exception)
                    {
                        BackupLog.CheckpointFailed(_logger, registration.Target.Id, exception);
                    }
                }
            }
            finally
            {
                _gate.Release();
            }
        }

        public void Dispose() => _gate.Dispose();
    }
}
