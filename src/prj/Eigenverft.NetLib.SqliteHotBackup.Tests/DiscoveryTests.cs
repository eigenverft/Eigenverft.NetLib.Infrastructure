using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Eigenverft.NetLib.SqliteHotBackup.Tests;

[TestClass]
public sealed class DiscoveryTests
{
    [TestMethod]
    public async Task InventoryFindsDirectAndFactoryRegistrationsWithoutConstructingContexts()
    {
        using BackupFixture fixture = new();
        string source = await fixture.DatabaseAsync("ef.db", wal: true);
        int constructions = 0;
        TrackingFactory factory = new(source);
        await using ServiceProvider provider = fixture.Provider(options =>
        {
            options.Discovery.DiscoverDbContexts = true;
            options.Discovery.SetDbContextId<DirectContext>(" identity ");
            options.Discovery.AddFile("file", source);
        }, services =>
        {
            services.AddScoped(_ => { constructions++; return new DirectContext(new DbContextOptionsBuilder<DirectContext>().UseSqlite("Data Source=" + source).Options); });
            services.AddSingleton<IDbContextFactory<FactoryContext>>(factory);
        });
        ISqliteHotBackupOrchestrator backup = provider.GetRequiredService<ISqliteHotBackupOrchestrator>();
        var inventory = await backup.GetTargetsAsync();
        Assert.AreEqual(0, constructions);
        Assert.AreEqual(0, factory.Created);
        CollectionAssert.AreEquivalent(new[] { "identity", "file", "ef:" + typeof(FactoryContext).FullName }, inventory.Select(target => target.Id).ToArray());
        Assert.IsNull(inventory.Single(target => target.Id == "identity").SourceFile);
        SqliteBackupRunResult run = await backup.BackupAllAsync();
        Assert.HasCount(1, run.Targets);
        Assert.HasCount(3, run.Targets[0].TargetIds);
        Assert.AreEqual(1, constructions);
        Assert.AreEqual(1, factory.Created);
        Assert.IsTrue(factory.Last!.Disposed);
        Assert.AreEqual(1L, await BackupFixture.CountAsync(run.Targets[0].BackupFile!));
    }

    [TestMethod]
    public async Task DirectRegistrationWinsWhenFactoryIsAlsoPresent()
    {
        using BackupFixture fixture = new();
        string source = await fixture.DatabaseAsync("ef.db", wal: true);
        TrackingFactory factory = new(source);
        FactoryContext? context = null;
        await using ServiceProvider provider = fixture.Provider(options => options.Discovery.DiscoverDbContexts = true, services =>
        {
            services.AddScoped(_ => context = new FactoryContext(new DbContextOptionsBuilder<FactoryContext>().UseSqlite("Data Source=" + source).Options));
            services.AddSingleton<IDbContextFactory<FactoryContext>>(factory);
        });
        await provider.GetRequiredService<ISqliteHotBackupOrchestrator>().BackupAllAsync();
        Assert.AreEqual(0, factory.Created);
        Assert.IsTrue(context!.Disposed);
    }

    [TestMethod]
    [DataRow("non-sqlite")]
    [DataRow("memory")]
    [DataRow("mode-memory")]
    [DataRow("uri-memory")]
    [DataRow("empty-source")]
    [DataRow("non-wal")]
    public async Task IneligibleAutomaticCandidateIsSkippedInAllRunButFailsDeliberateSelection(string kind)
    {
        using BackupFixture fixture = new();
        string source = await fixture.DatabaseAsync("source.db");
        DbContextOptionsBuilder<DirectContext> builder = new();
        if (kind == "non-sqlite") { builder.UseInMemoryDatabase(Guid.NewGuid().ToString()); }
        else { builder.UseSqlite(kind switch
        {
            "memory" => "Data Source=:memory:", "mode-memory" => "Data Source=named;Mode=Memory",
            "uri-memory" => "Data Source=file::memory:?cache=shared", "empty-source" => "Data Source=",
            _ => "Data Source=" + source
        }); }
        await using ServiceProvider provider = fixture.Provider(options =>
        {
            options.Discovery.DiscoverDbContexts = true;
            options.Discovery.SetDbContextId<DirectContext>("context");
        }, services => services.AddScoped(_ => new DirectContext(builder.Options)));
        ISqliteHotBackupOrchestrator backup = provider.GetRequiredService<ISqliteHotBackupOrchestrator>();
        SqliteBackupRunResult result = await backup.BackupAllAsync();
        Assert.AreEqual(SqliteBackupStatus.Skipped, result.Targets[0].Status);
        Assert.IsNotNull(result.Targets[0].Reason);
        SqliteBackupException error = await Assert.ThrowsExactlyAsync<SqliteBackupException>(() => backup.BackupAsync("context"));
        Assert.AreEqual(SqliteBackupStatus.Failed, error.Result.Targets[0].Status);
        Assert.IsFalse(Directory.Exists(fixture.Output));
    }

    [TestMethod]
    public async Task MissingEfDatabaseIsAnErrorAndDiscoveryNeverCreatesIt()
    {
        using BackupFixture fixture = new();
        string absent = fixture.FilePath("absent.db");
        await using ServiceProvider provider = fixture.Provider(options => options.Discovery.DiscoverDbContexts = true,
            services => services.AddDbContext<DirectContext>(options => options.UseSqlite("Data Source=" + absent)));
        ISqliteHotBackupOrchestrator backup = provider.GetRequiredService<ISqliteHotBackupOrchestrator>();
        Assert.HasCount(1, await backup.GetTargetsAsync());
        Assert.IsFalse(File.Exists(absent));
        await Assert.ThrowsExactlyAsync<SqliteBackupException>(() => backup.BackupAllAsync());
        Assert.IsFalse(File.Exists(absent));
    }

    [TestMethod]
    [DataRow("duplicate")]
    [DataRow("unknown-mapping")]
    [DataRow("unknown-override")]
    [DataRow("context-collision")]
    public async Task InvalidCatalogIsRejectedBeforeOutput(string kind)
    {
        using BackupFixture fixture = new();
        await using ServiceProvider provider = fixture.Provider(options =>
        {
            options.Discovery.AddFile(" file ", fixture.FilePath("source.db"));
            switch (kind)
            {
                case "duplicate": options.Discovery.AddFile("FILE", fixture.FilePath("other.db")); break;
                case "unknown-mapping": options.Discovery.SetDbContextId<DirectContext>("missing"); break;
                case "unknown-override": options.ConfigureExecutionLimit("missing", limit => limit.MaxRewindWorkFactor = 4); break;
                case "context-collision":
                    options.Discovery.DiscoverDbContexts = true;
                    options.Discovery.SetDbContextId<DirectContext>("file");
                    break;
            }
        }, services => services.AddScoped(_ => new DirectContext(new DbContextOptionsBuilder<DirectContext>().Options)));
        ISqliteHotBackupOrchestrator backup = provider.GetRequiredService<ISqliteHotBackupOrchestrator>();
        await Assert.ThrowsExactlyAsync<ArgumentException>(() => backup.GetTargetsAsync());
        await Assert.ThrowsExactlyAsync<ArgumentException>(() => backup.BackupAllAsync());
        Assert.IsFalse(Directory.Exists(fixture.Output));
    }

    [TestMethod]
    public async Task SourcePreparationLimitIsLocalAndFactoryOwnedContextIsDisposed()
    {
        using BackupFixture fixture = new();
        string source = await fixture.DatabaseAsync("ef.db", wal: true);
        ManualTime time = new();
        TrackingFactory factory = new(source) { BeforeCreate = () => time.Advance(TimeSpan.FromMinutes(5)) };
        await using ServiceProvider provider = fixture.Provider(options =>
        {
            options.Discovery.DiscoverDbContexts = true;
            options.Discovery.AddFile("healthy", source);
        }, services =>
        {
            services.AddSingleton<TimeProvider>(time);
            services.AddSingleton<IDbContextFactory<FactoryContext>>(factory);
        });
        SqliteBackupException error = await Assert.ThrowsExactlyAsync<SqliteBackupException>(() => provider.GetRequiredService<ISqliteHotBackupOrchestrator>().BackupAllAsync());
        Assert.IsTrue(factory.Last!.Disposed);
        Assert.AreEqual(1, error.Result.Targets.Count(target => target.Status == SqliteBackupStatus.Succeeded));
        StringAssert.Contains(error.Result.Targets.Single(target => target.Status == SqliteBackupStatus.Failed).Reason!, "preparation");
    }

    [TestMethod]
    public async Task ShutdownCheckpointIsOptInAndDoesNotCreateMissingFiles()
    {
        using BackupFixture fixture = new();
        string source = await fixture.DatabaseAsync("wal.db", wal: true);
        await using SqliteConnection writer = BackupFixture.Open(source);
        using SqliteCommand command = writer.CreateCommand();
        command.CommandText = "PRAGMA wal_autocheckpoint=0; INSERT INTO entries(value) VALUES ('pending');";
        await command.ExecuteNonQueryAsync();
        long length = new FileInfo(source + "-wal").Length;
        Assert.IsGreaterThan(0, length);
        await using (ServiceProvider disabled = fixture.Provider(options => options.Discovery.AddFile("wal", source)))
        {
            IHostedService hosted = disabled.GetServices<IHostedService>().Single();
            await hosted.StartAsync(default);
            await hosted.StopAsync(default);
            Assert.AreEqual(length, new FileInfo(source + "-wal").Length);
        }
        string absent = fixture.FilePath("absent.db");
        await using ServiceProvider enabled = fixture.Provider(options =>
        {
            options.Shutdown.CheckpointWal = true;
            options.Discovery.AddFile("wal", source);
            options.Discovery.AddFile("alias", source);
            options.Discovery.AddFile("absent", absent);
        });
        IHostedService checkpoint = enabled.GetServices<IHostedService>().Single();
        await checkpoint.StopAsync(new CancellationToken(canceled: true));
        Assert.AreEqual(length, new FileInfo(source + "-wal").Length);
        await checkpoint.StopAsync(default);
        Assert.AreEqual(0L, new FileInfo(source + "-wal").Length);
        Assert.IsFalse(File.Exists(absent));
    }

    [TestMethod]
    public async Task CancellationDuringFactoryResolutionPropagatesAndNextCallStillWorks()
    {
        using BackupFixture fixture = new();
        string path = await fixture.DatabaseAsync("source.db", wal: true);
        using CancellationTokenSource cancellation = new();
        TrackingFactory factory = new(path) { BeforeCreate = cancellation.Cancel };
        await using ServiceProvider provider = fixture.Provider(options => options.Discovery.DiscoverDbContexts = true,
            services => services.AddSingleton<IDbContextFactory<FactoryContext>>(factory));
        ISqliteHotBackupOrchestrator backup = provider.GetRequiredService<ISqliteHotBackupOrchestrator>();
        await Assert.ThrowsAsync<OperationCanceledException>(() => backup.BackupAllAsync(cancellation.Token));
        Assert.IsTrue(factory.Last!.Disposed);
        Assert.IsFalse((await backup.BackupAllAsync()).HasFailures);
    }

    [TestMethod]
    public async Task ShutdownLogsCancellationAndInvalidConfigurationWithoutThrowing()
    {
        using BackupFixture fixture = new();
        string path = await fixture.DatabaseAsync("source.db", wal: true);
        using CancellationTokenSource cancellation = new();
        TrackingFactory factory = new(path) { BeforeCreate = cancellation.Cancel };
        await using (ServiceProvider provider = fixture.Provider(options =>
        {
            options.Discovery.DiscoverDbContexts = true;
            options.Shutdown.CheckpointWal = true;
        }, services => services.AddSingleton<IDbContextFactory<FactoryContext>>(factory)))
        {
            await provider.GetServices<IHostedService>().Single().StopAsync(cancellation.Token);
        }
        Assert.IsTrue(fixture.Logs.Entries.Any(entry => entry.Id.Id == 11));
        await using ServiceProvider invalid = fixture.Provider(options =>
        {
            options.Shutdown.CheckpointWal = true;
            options.Discovery.AddFile("id", path);
            options.Discovery.AddFile("ID", path);
        });
        await invalid.GetServices<IHostedService>().Single().StopAsync(default);
        Assert.IsTrue(fixture.Logs.Entries.Any(entry => entry.Id.Id == 12));
    }

    public sealed class DirectContext(DbContextOptions<DirectContext> options) : DbContext(options);
    public sealed class FactoryContext(DbContextOptions<FactoryContext> options) : DbContext(options)
    {
        internal bool Disposed { get; private set; }
        public override async ValueTask DisposeAsync() { Disposed = true; await base.DisposeAsync(); }
    }
    private sealed class TrackingFactory(string path) : IDbContextFactory<FactoryContext>
    {
        internal int Created { get; private set; }
        internal FactoryContext? Last { get; private set; }
        internal Action? BeforeCreate { get; init; }
        public FactoryContext CreateDbContext()
        {
            BeforeCreate?.Invoke();
            Created++;
            return Last = new(new DbContextOptionsBuilder<FactoryContext>().UseSqlite("Data Source=" + path).Options);
        }
    }
}
