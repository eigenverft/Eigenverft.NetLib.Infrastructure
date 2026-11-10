using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

using Eigenverft.NetLib.SqliteHotBackup.Internal;

using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Eigenverft.NetLib.SqliteHotBackup.Tests;

[TestClass]
public sealed class ConfigurationAndLimitIntegrationTests
{
    [TestMethod]
    public async Task NativeBusyLoopExhaustsLocalLimitAndLeavesNoPublishedOrPartialFile()
    {
        using BackupFixture fixture = new();
        string path = await fixture.DatabaseAsync("locked.db");
        SqliteHotBackupOptions options = new();
        options.Backup.Directory = fixture.Output;
        options.ExecutionLimit.NoProgressTimeout = TimeSpan.FromMilliseconds(200);
        TargetRegistration registration = new(new("locked", SqliteBackupSourceKind.File, "locked", path));
        using ServiceProvider provider = new ServiceCollection().BuildServiceProvider();
        SourceResolution resolution = await SourceResolver.ResolveAsync(registration, options, provider, TimeProvider.System, default);
        await using SqliteConnection locker = BackupFixture.Open(path);
        using SqliteCommand command = locker.CreateCommand();
        command.CommandText = "BEGIN EXCLUSIVE;";
        await command.ExecuteNonQueryAsync();
        SqliteBackupResult result = await SqliteCopy.ExecuteAsync(resolution.Source!, new[] { "locked" }, TimeSpan.Zero,
            options, TimeProvider.System, NullLogger.Instance, default);
        Assert.AreEqual(SqliteBackupStatus.Failed, result.Status);
        StringAssert.Contains(result.Reason!, "execution limit");
        Assert.IsFalse(Directory.Exists(fixture.Output) && Directory.GetFiles(fixture.Output).Length != 0);
        command.CommandText = "ROLLBACK;";
        await command.ExecuteNonQueryAsync();
        SqliteBackupResult later = await SqliteCopy.ExecuteAsync(resolution.Source!, new[] { "locked" }, TimeSpan.Zero,
            options, TimeProvider.System, NullLogger.Instance, default);
        Assert.AreEqual(SqliteBackupStatus.Succeeded, later.Status);
        Assert.AreEqual(1L, await BackupFixture.CountAsync(later.BackupFile!));
    }

    [TestMethod]
    public async Task ExhaustedPreparationBudgetCannotCreateOutput()
    {
        using BackupFixture fixture = new();
        string path = await fixture.DatabaseAsync("source.db");
        SqliteHotBackupOptions options = new();
        options.Backup.Directory = fixture.Output;
        ResolvedSource source = new(new(new("source", SqliteBackupSourceKind.File, "source", path)), path,
            new SqliteConnectionStringBuilder { DataSource = path, Pooling = false }.ToString(), options.ExecutionLimit, false, TimeSpan.Zero);
        SqliteBackupResult result = await SqliteCopy.ExecuteAsync(source, new[] { "source" }, TimeSpan.FromMinutes(5),
            options, new ManualTime(), NullLogger.Instance, default);
        Assert.AreEqual(SqliteBackupStatus.Failed, result.Status);
        Assert.IsFalse(Directory.Exists(fixture.Output));
    }

    [TestMethod]
    public async Task PerRunOptionsDoNotMutateRegisteredDefaults()
    {
        using BackupFixture fixture = new();
        string first = await fixture.DatabaseAsync("first.db");
        string second = await fixture.DatabaseAsync("second.db");
        await using ServiceProvider provider = fixture.Provider(options => options.Discovery.AddFile("first", first));
        ISqliteHotBackupOrchestrator backup = provider.GetRequiredService<ISqliteHotBackupOrchestrator>();
        SqliteBackupRunResult overridden = await backup.BackupAsync(new[] { "second" }, options =>
        {
            options.Discovery.AddFile("second", second);
            options.Backup.Directory = fixture.FilePath("per-run");
            options.Backup.FileNamePattern = "overridden";
            options.ExecutionLimit.MaxRewindWorkFactor = 7;
            options.ConfigureExecutionLimit("second", limit => limit.NoProgressTimeout = TimeSpan.FromMinutes(9));
        });
        Assert.AreEqual(fixture.FilePath("per-run/overridden.db"), overridden.Targets[0].BackupFile);
        Assert.HasCount(1, await backup.GetTargetsAsync());
        SqliteBackupRunResult normal = await backup.BackupAllAsync();
        Assert.AreEqual(fixture.Output, Path.GetDirectoryName(normal.Targets[0].BackupFile));
        Assert.AreEqual("first", normal.Targets[0].TargetIds[0]);
    }

    [TestMethod]
    public async Task NullOrInvalidOptionReplacementsAreConfigurationErrors()
    {
        using BackupFixture fixture = new();
        await using ServiceProvider provider = fixture.Provider();
        ISqliteHotBackupOrchestrator backup = provider.GetRequiredService<ISqliteHotBackupOrchestrator>();
        foreach (Action<SqliteHotBackupOptions> configure in new Action<SqliteHotBackupOptions>[]
        {
            options => options.Backup = null!, options => options.Discovery = null!, options => options.Zip = null!,
            options => options.Shutdown = null!, options => options.ExecutionLimit = null!
        })
        {
            await Assert.ThrowsExactlyAsync<ArgumentNullException>(() => backup.BackupAllAsync(configure));
        }
        await Assert.ThrowsExactlyAsync<ArgumentException>(() => backup.BackupAllAsync(options => options.Backup.Directory = " "));
        await Assert.ThrowsExactlyAsync<ArgumentException>(() => backup.BackupAllAsync(options => { options.Zip.Enabled = true; options.Zip.FileNamePattern = "../bad"; }));
        Assert.IsFalse((await backup.BackupAllAsync()).HasFailures);
    }

    [TestMethod]
    public void RegistrationRejectsInvalidIdsAndPathsAndIsIdempotent()
    {
        SqliteHotBackupOptions options = new();
        Assert.ThrowsExactly<ArgumentException>(() => options.Discovery.AddFile(" ", "file.db"));
        Assert.ThrowsExactly<ArgumentException>(() => options.Discovery.AddFile("source", " "));
        Assert.ThrowsExactly<ArgumentNullException>(() => SqliteHotBackupServiceCollectionExtensions.AddSqliteHotBackup(null!));
        options.Discovery.AddFile(" file ", "relative.db");
        Assert.AreEqual(Path.Combine(AppContext.BaseDirectory, "relative.db"), options.Discovery.Files[0].Path);
        ServiceCollection services = new();
        services.AddSqliteHotBackup();
        services.AddSqliteHotBackup();
        using ServiceProvider provider = services.BuildServiceProvider();
        Assert.AreSame(provider.GetRequiredService<ISqliteHotBackupOrchestrator>(), provider.GetRequiredService<ISqliteHotBackupOrchestrator>());
    }
}
