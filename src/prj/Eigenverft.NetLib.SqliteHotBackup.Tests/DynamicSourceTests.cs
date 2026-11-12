using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Eigenverft.NetLib.SqliteHotBackup.Tests;

[TestClass]
public sealed class DynamicSourceTests
{
    [TestMethod]
    public async Task ManualUsesFixedAndRuntimeFilesWithoutConstructingAutomaticSources()
    {
        using BackupFixture fixture = new();
        string path = await fixture.DatabaseAsync("manual.db", wal: true);
        await using ServiceProvider provider = fixture.Provider(options =>
        {
            options.Discovery.Mode = SqliteBackupDiscoveryMode.Manual;
            options.Discovery.DiscoverDbContexts = true;
            options.Discovery.SetDbContextId<DiscoveryTests.DirectContext>("ignored-context");
            options.Discovery.AddFile("fixed", path);
            options.Shutdown.CheckpointWal = true;
        }, services =>
        {
            services.AddScoped<DiscoveryTests.DirectContext>(_ => throw new InvalidOperationException("EF must not be constructed."));
            services.AddSqliteBackupSourceProvider<ConstructorFailure>();
        });
        ISqliteBackupTargetRegistry registry = provider.GetRequiredService<ISqliteBackupTargetRegistry>();
        registry.AddFile(" runtime ", path);
        ISqliteHotBackupOrchestrator backup = provider.GetRequiredService<ISqliteHotBackupOrchestrator>();
        CollectionAssert.AreEquivalent(new[] { "fixed", "runtime" }, (await backup.GetTargetsAsync()).Select(item => item.Id).ToArray());
        SqliteBackupRunResult result = await backup.BackupAllAsync();
        Assert.HasCount(1, result.Targets);
        Assert.HasCount(2, result.Targets[0].TargetIds);
        Assert.AreEqual(1L, await BackupFixture.CountAsync(result.Targets[0].BackupFile!));
        await provider.GetServices<IHostedService>().Single().StopAsync(default);
        Assert.IsTrue(fixture.Logs.Entries.Any(entry => entry.Id.Id == 9));
        Assert.IsFalse(fixture.Logs.Entries.Any(entry => entry.Id.Id == 13));
    }

    [TestMethod]
    [DataRow(SqliteBackupDiscoveryMode.Automatic)]
    [DataRow(SqliteBackupDiscoveryMode.Manual)]
    public async Task RuntimeRegistrationsAreFreshRemovableAndProcessLocal(SqliteBackupDiscoveryMode mode)
    {
        using BackupFixture fixture = new();
        string first = await fixture.DatabaseAsync("first.db");
        string second = await fixture.DatabaseAsync("second.db");
        await using ServiceProvider provider = fixture.Provider(options => options.Discovery.Mode = mode);
        ISqliteBackupTargetRegistry registry = provider.GetRequiredService<ISqliteBackupTargetRegistry>();
        ISqliteHotBackupOrchestrator backup = provider.GetRequiredService<ISqliteHotBackupOrchestrator>();
        Assert.HasCount(0, await backup.GetTargetsAsync());
        registry.AddFile("first", first);
        IReadOnlyList<SqliteBackupFileSource> before = await registry.GetSourcesAsync();
        Assert.ThrowsExactly<ArgumentException>(() => registry.AddFile(" FIRST ", second));
        Assert.IsTrue(registry.Remove(" FIRST "));
        Assert.IsFalse(registry.Remove("not-registered"));
        registry.AddFile("second", second);
        Assert.AreEqual("first", before.Single().Id);
        Assert.AreEqual("second", (await backup.GetTargetsAsync()).Single().Id);
        Assert.AreEqual("second", (await backup.BackupAsync("second")).Targets.Single().TargetIds.Single());
        Assert.IsTrue(File.Exists(first));
        await using ServiceProvider restarted = fixture.Provider(options => options.Discovery.Mode = mode);
        Assert.HasCount(0, await restarted.GetRequiredService<ISqliteHotBackupOrchestrator>().GetTargetsAsync());
    }

    [TestMethod]
    public async Task RegistryValidatesIdsPathsAndCancellationWithoutOpeningFiles()
    {
        using BackupFixture fixture = new();
        await using ServiceProvider provider = fixture.Provider();
        ISqliteBackupTargetRegistry registry = provider.GetRequiredService<ISqliteBackupTargetRegistry>();
        Assert.ThrowsExactly<ArgumentException>(() => registry.AddFile(" ", "missing.db"));
        Assert.ThrowsExactly<ArgumentException>(() => registry.AddFile("id", " "));
        Assert.ThrowsExactly<ArgumentException>(() => registry.Remove(" "));
        registry.AddFile(" relative ", "not-created-" + Guid.NewGuid().ToString("N") + ".db");
        SqliteBackupFileSource file = (await registry.GetSourcesAsync()).Single();
        Assert.AreEqual("relative", file.Id);
        Assert.IsTrue(Path.IsPathFullyQualified(file.FilePath));
        Assert.IsFalse(File.Exists(file.FilePath));
        Assert.HasCount(1, await provider.GetRequiredService<ISqliteHotBackupOrchestrator>().GetTargetsAsync());
        using CancellationTokenSource cancelled = new();
        cancelled.Cancel();
        Assert.ThrowsExactly<OperationCanceledException>(() => registry.GetSourcesAsync(cancelled.Token));
        Assert.AreSame(registry, provider.GetRequiredService<ISqliteBackupTargetRegistry>());
        Assert.ThrowsExactly<ArgumentNullException>(() =>
            SqliteHotBackupServiceCollectionExtensions.AddSqliteBackupSourceProvider<MutableSource>(null!));
    }

    [TestMethod]
    public async Task AutomaticQueriesScopedProvidersFreshAndHonorsPerRunManualMode()
    {
        using BackupFixture fixture = new();
        string first = await fixture.DatabaseAsync("first.db");
        string second = await fixture.DatabaseAsync("second.db");
        SourceState state = new() { Files = new[] { new SqliteBackupFileSource(" first ", first) } };
        await using ServiceProvider provider = fixture.Provider(register: services =>
        {
            services.AddSingleton(state);
            services.AddScoped<ScopeMarker>();
            services.AddSqliteBackupSourceProvider<ScopedSource>();
            services.AddSqliteBackupSourceProvider<ScopedSource>();
        });
        ISqliteHotBackupOrchestrator backup = provider.GetRequiredService<ISqliteHotBackupOrchestrator>();
        Assert.AreEqual(SqliteBackupDiscoveryMode.Automatic, new SqliteHotBackupOptions().Discovery.Mode);
        Assert.AreEqual(0, state.Created);
        Assert.AreEqual("first", (await backup.GetTargetsAsync()).Single().Id);
        state.Files = new[] { new SqliteBackupFileSource("second", second) };
        Assert.AreEqual("second", (await backup.BackupAsync("second")).Targets.Single().TargetIds.Single());
        Assert.HasCount(0, (await backup.BackupAllAsync(options => options.Discovery.Mode = SqliteBackupDiscoveryMode.Manual)).Targets);
        Assert.AreEqual(2, state.Created);
        Assert.AreEqual("second", (await backup.GetTargetsAsync()).Single().Id);
        Assert.AreEqual(3, state.Created);
        Assert.AreEqual(3, state.Disposed);
        Assert.AreEqual(3, state.Markers.Distinct().Count());
        await Assert.ThrowsExactlyAsync<ArgumentOutOfRangeException>(() =>
            backup.BackupAllAsync(options => options.Discovery.Mode = (SqliteBackupDiscoveryMode)99));
    }

    [TestMethod]
    public async Task RegistrationChangesDuringAcquisitionApplyOnlyToNextRun()
    {
        using BackupFixture fixture = new();
        string first = await fixture.DatabaseAsync("first.db");
        string second = await fixture.DatabaseAsync("second.db");
        TaskCompletionSource entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        TaskCompletionSource release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        MutableSource source = new()
        {
            Read = async ct =>
            {
                entered.TrySetResult();
                await release.Task.WaitAsync(ct);
                return Array.Empty<SqliteBackupFileSource>();
            }
        };
        await using ServiceProvider provider = fixture.Provider(register: services =>
        {
            services.AddSingleton(source);
            services.AddSqliteBackupSourceProvider<MutableSource>();
        });
        ISqliteBackupTargetRegistry registry = provider.GetRequiredService<ISqliteBackupTargetRegistry>();
        ISqliteHotBackupOrchestrator backup = provider.GetRequiredService<ISqliteHotBackupOrchestrator>();
        registry.AddFile("first", first);
        Task<SqliteBackupRunResult> run = backup.BackupAllAsync();
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.IsTrue(registry.Remove("first"));
            registry.AddFile("second", second);
        }
        finally { release.TrySetResult(); }
        Assert.AreEqual("first", (await run).Targets.Single().TargetIds.Single());
        Assert.AreEqual("second", (await backup.BackupAllAsync()).Targets.Single().TargetIds.Single());
    }

    [TestMethod]
    [DataRow("query")]
    [DataRow("constructor")]
    [DataRow("null-list")]
    [DataRow("null-item")]
    [DataRow("invalid-id")]
    [DataRow("invalid-path")]
    [DataRow("duplicate")]
    [DataRow("other-cancellation")]
    [DataRow("null-task")]
    public async Task FailedProviderDoesNotSuppressHealthyBackupAndArchive(string kind)
    {
        using BackupFixture fixture = new();
        string healthy = await fixture.DatabaseAsync("healthy.db");
        MutableSource source = new()
        {
            Read = _ => kind switch
            {
                "null-list" => Task.FromResult<IReadOnlyList<SqliteBackupFileSource>>(null!),
                "null-item" => Task.FromResult<IReadOnlyList<SqliteBackupFileSource>>(new SqliteBackupFileSource[] { null! }),
                "invalid-id" => Files(new SqliteBackupFileSource(" ", healthy)),
                "invalid-path" => Files(new SqliteBackupFileSource("bad", " ")),
                "duplicate" => Files(new("provider", healthy), new(" PROVIDER ", healthy)),
                "other-cancellation" => throw new OperationCanceledException("Provider's own cancellation."),
                "null-task" => null!,
                _ => throw new IOException("Catalog unavailable.")
            }
        };
        await using ServiceProvider provider = fixture.Provider(options =>
        {
            options.Discovery.AddFile("healthy", healthy);
            options.Zip.Enabled = true;
            options.Shutdown.CheckpointWal = true;
        }, services =>
        {
            services.AddSingleton(source);
            if (kind == "constructor") { services.AddSqliteBackupSourceProvider<ConstructorFailure>(); }
            else { services.AddSqliteBackupSourceProvider<MutableSource>(); }
        });
        ISqliteHotBackupOrchestrator backup = provider.GetRequiredService<ISqliteHotBackupOrchestrator>();
        SqliteBackupDiscoveryException inventory = await Assert.ThrowsExactlyAsync<SqliteBackupDiscoveryException>(() => backup.GetTargetsAsync());
        Assert.AreEqual("healthy", inventory.Targets.Single().Id);
        Assert.HasCount(1, inventory.Failures);
        SqliteBackupException failed = await Assert.ThrowsExactlyAsync<SqliteBackupException>(() => backup.BackupAllAsync());
        Assert.IsTrue(failed.Result.HasFailures);
        Assert.HasCount(1, failed.Result.DiscoveryFailures);
        Assert.AreEqual(SqliteBackupStatus.Succeeded, failed.Result.Targets.Single().Status);
        Assert.AreEqual(SqliteBackupStatus.Succeeded, failed.Result.Archive!.Status);
        Assert.IsTrue(File.Exists(failed.Result.Archive.File));
        Assert.IsTrue(fixture.Logs.Entries.Any(entry => entry.Id.Id == 13));
        await provider.GetServices<IHostedService>().Single().StopAsync(default);
        Assert.IsTrue(fixture.Logs.Entries.Any(entry => entry.Id.Id == 9));
    }

    [TestMethod]
    public async Task IncompleteDiscoveryRetainsKnownSelectionAndReportsUnavailableIds()
    {
        using BackupFixture fixture = new();
        string source = await fixture.DatabaseAsync("source.db");
        await using ServiceProvider provider = fixture.Provider(options =>
        {
            options.Discovery.AddFile("known", source);
            options.ConfigureExecutionLimit("temporarily-unavailable", limit => limit.MaxRewindWorkFactor = 3);
        }, services => services.AddSqliteBackupSourceProvider<ConstructorFailure>());
        SqliteBackupException failure = await Assert.ThrowsExactlyAsync<SqliteBackupException>(() =>
            provider.GetRequiredService<ISqliteHotBackupOrchestrator>().BackupAsync(new[] { "known", "temporarily-unavailable" }));
        Assert.AreEqual(SqliteBackupStatus.Succeeded, failure.Result.Targets.Single(item => item.TargetIds.Contains("known")).Status);
        Assert.AreEqual(SqliteBackupStatus.Failed, failure.Result.Targets.Single(item => item.TargetIds.Contains("temporarily-unavailable")).Status);
        Assert.HasCount(1, failure.Result.DiscoveryFailures);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task CrossSourceIdCollisionsFailBeforeCreatingOutput(bool dynamicProvider)
    {
        using BackupFixture fixture = new();
        string path = await fixture.DatabaseAsync("source.db");
        await using ServiceProvider provider = fixture.Provider(options => options.Discovery.AddFile("same", path), services =>
        {
            if (dynamicProvider)
            {
                services.AddSingleton(new MutableSource { Read = _ => Files(new SqliteBackupFileSource(" SAME ", path)) });
                services.AddSqliteBackupSourceProvider<MutableSource>();
            }
        });
        if (!dynamicProvider) { provider.GetRequiredService<ISqliteBackupTargetRegistry>().AddFile(" SAME ", path); }
        await Assert.ThrowsExactlyAsync<ArgumentException>(() => provider.GetRequiredService<ISqliteHotBackupOrchestrator>().BackupAllAsync());
        Assert.IsFalse(Directory.Exists(fixture.Output));
    }

    [TestMethod]
    public async Task SourceCancellationStopsRunAndReleasesGateForNextAttempt()
    {
        using BackupFixture fixture = new();
        string path = await fixture.DatabaseAsync("source.db");
        using CancellationTokenSource cancellation = new();
        MutableSource source = new()
        {
            Read = ct =>
            {
                cancellation.Cancel();
                ct.ThrowIfCancellationRequested();
                return Files();
            }
        };
        await using ServiceProvider provider = fixture.Provider(options => options.Discovery.AddFile("known", path), services =>
        {
            services.AddSingleton(source);
            services.AddSqliteBackupSourceProvider<MutableSource>();
        });
        ISqliteHotBackupOrchestrator backup = provider.GetRequiredService<ISqliteHotBackupOrchestrator>();
        await Assert.ThrowsAsync<OperationCanceledException>(() => backup.BackupAllAsync(cancellation.Token));
        Assert.IsFalse(Directory.Exists(fixture.Output));
        source.Read = _ => Files();
        Assert.IsFalse((await backup.BackupAllAsync()).HasFailures);
    }

    [TestMethod]
    public async Task ReturnedMutableCollectionIsDetachedBeforeNextProviderRuns()
    {
        using BackupFixture fixture = new();
        string path = await fixture.DatabaseAsync("source.db");
        List<SqliteBackupFileSource> files = new() { new("stable", path) };
        await using ServiceProvider provider = fixture.Provider(register: services =>
        {
            services.AddSingleton(new MutableSource { Read = _ => Task.FromResult<IReadOnlyList<SqliteBackupFileSource>>(files) });
            services.AddSingleton(new MutatingSource(files));
            services.AddSqliteBackupSourceProvider<MutableSource>();
            services.AddSqliteBackupSourceProvider<MutatingSource>();
        });
        SqliteBackupRunResult result = await provider.GetRequiredService<ISqliteHotBackupOrchestrator>().BackupAllAsync();
        Assert.HasCount(0, files);
        Assert.AreEqual("stable", result.Targets.Single().TargetIds.Single());
    }

    [TestMethod]
    public void DiscoveryExceptionRejectsNullReports()
    {
        Assert.ThrowsExactly<ArgumentNullException>(() => new SqliteBackupDiscoveryException(null!, Array.Empty<SqliteBackupDiscoveryFailure>()));
        Assert.ThrowsExactly<ArgumentNullException>(() => new SqliteBackupDiscoveryException(Array.Empty<SqliteBackupTarget>(), null!));
    }

    private static Task<IReadOnlyList<SqliteBackupFileSource>> Files(params SqliteBackupFileSource[] files)
        => Task.FromResult<IReadOnlyList<SqliteBackupFileSource>>(files);

    public sealed class MutableSource : ISqliteBackupSourceProvider
    {
        internal Func<CancellationToken, Task<IReadOnlyList<SqliteBackupFileSource>>> Read { get; set; } = _ => Files();
        public Task<IReadOnlyList<SqliteBackupFileSource>> GetSourcesAsync(CancellationToken cancellationToken = default) => Read(cancellationToken);
    }

    public sealed class ConstructorFailure : ISqliteBackupSourceProvider
    {
        public ConstructorFailure() => throw new IOException("Provider could not be constructed.");
        public Task<IReadOnlyList<SqliteBackupFileSource>> GetSourcesAsync(CancellationToken cancellationToken = default)
            => throw new InvalidOperationException("Constructor must have failed first.");
    }

    public sealed class MutatingSource(List<SqliteBackupFileSource> files) : ISqliteBackupSourceProvider
    {
        public Task<IReadOnlyList<SqliteBackupFileSource>> GetSourcesAsync(CancellationToken cancellationToken = default)
        {
            files.Clear();
            return Files();
        }
    }

    public sealed class SourceState
    {
        internal IReadOnlyList<SqliteBackupFileSource> Files { get; set; } = Array.Empty<SqliteBackupFileSource>();
        internal int Created { get; set; }
        internal int Disposed { get; set; }
        internal List<Guid> Markers { get; } = new();
    }

    public sealed class ScopeMarker
    {
        internal Guid Id { get; } = Guid.NewGuid();
    }

    public sealed class ScopedSource : ISqliteBackupSourceProvider, IAsyncDisposable
    {
        private readonly SourceState _state;
        private readonly ScopeMarker _marker;
        public ScopedSource(SourceState state, ScopeMarker marker)
        {
            _state = state;
            _marker = marker;
            state.Created++;
            state.Markers.Add(marker.Id);
        }
        public Task<IReadOnlyList<SqliteBackupFileSource>> GetSourcesAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(_state.Files);
        public ValueTask DisposeAsync()
        {
            _state.Disposed++;
            GC.KeepAlive(_marker);
            return ValueTask.CompletedTask;
        }
    }
}
