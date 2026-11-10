using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Eigenverft.NetLib.SqliteHotBackup.Tests;

[TestClass]
public sealed class BackupRunTests
{
    [TestMethod]
    public async Task SelectionBacksUpOnePhysicalFileAndRetainsSelectedAliasIds()
    {
        using BackupFixture fixture = new();
        string first = await fixture.DatabaseAsync("first.db");
        string second = await fixture.DatabaseAsync("second.db");
        await using ServiceProvider provider = fixture.Provider(options =>
        {
            options.Discovery.AddFile(" first ", first);
            options.Discovery.AddFile("alias", first);
            options.Discovery.AddFile("second", second);
        });
        ISqliteHotBackupOrchestrator backup = provider.GetRequiredService<ISqliteHotBackupOrchestrator>();
        SqliteBackupRunResult report = await backup.BackupAsync(new[] { " FIRST ", "alias", "first" });
        Assert.HasCount(1, report.Targets);
        CollectionAssert.AreEquivalent(new[] { "first", "alias" }, report.Targets[0].TargetIds.ToArray());
        Assert.IsFalse(report.HasFailures);
        Assert.IsNull(report.Archive);
        Assert.IsTrue(report.Targets[0].RawBackupRetained);
        Assert.AreEqual(first, report.Targets[0].SourceFile);
        Assert.IsTrue(report.Targets[0].Duration >= TimeSpan.Zero);
        Assert.AreEqual(0, report.Targets[0].RewoundPages);
        Assert.AreEqual(1L, await BackupFixture.CountAsync(report.Targets[0].BackupFile!));
        Assert.AreEqual(0, report.Targets[0].RemainingPages);
        Assert.IsTrue(report.Targets[0].TotalPages > 0);
        Assert.HasCount(1, Directory.GetFiles(fixture.Output));
    }

    [TestMethod]
    public async Task FailureStillArchivesHealthySourcesAndNextCallRetriesEverything()
    {
        using BackupFixture fixture = new();
        string healthy = await fixture.DatabaseAsync("healthy.db");
        string absent = fixture.FilePath("absent.db");
        await using ServiceProvider provider = fixture.Provider(options =>
        {
            options.Discovery.AddFile("healthy", healthy);
            options.Discovery.AddFile("absent", absent);
            options.Zip.Enabled = true;
        });
        ISqliteHotBackupOrchestrator backup = provider.GetRequiredService<ISqliteHotBackupOrchestrator>();
        SqliteBackupException error = await Assert.ThrowsExactlyAsync<SqliteBackupException>(() => backup.BackupAllAsync());
        Assert.IsTrue(error.Result.HasFailures);
        SqliteBackupResult failed = error.Result.Targets.Single(target => target.Status == SqliteBackupStatus.Failed);
        StringAssert.Contains(failed.Reason!, "does not exist");
        SqliteBackupResult succeeded = error.Result.Targets.Single(target => target.Status == SqliteBackupStatus.Succeeded);
        Assert.IsFalse(succeeded.RawBackupRetained);
        Assert.IsFalse(File.Exists(succeeded.BackupFile));
        Assert.AreEqual(SqliteBackupStatus.Succeeded, error.Result.Archive!.Status);
        using (ZipArchive archive = ZipFile.OpenRead(error.Result.Archive.File!))
        {
            Assert.HasCount(1, archive.Entries);
            string extracted = fixture.FilePath("extracted.db");
            archive.Entries[0].ExtractToFile(extracted);
            Assert.AreEqual(1L, await BackupFixture.CountAsync(extracted));
        }
        await fixture.DatabaseAsync("absent.db");
        SqliteBackupRunResult next = await backup.BackupAllAsync();
        Assert.HasCount(2, next.Targets);
        Assert.IsFalse(next.HasFailures);
        Assert.AreEqual(1L, await BackupFixture.CountAsync(healthy));
        Assert.AreEqual(1L, await BackupFixture.CountAsync(absent));
        Assert.IsFalse(Directory.EnumerateFiles(fixture.Output, "*.tmp").Any());
    }

    [TestMethod]
    public async Task ArchiveFailureKeepsRawBackupsAndDoesNotPoisonLaterCalls()
    {
        using BackupFixture fixture = new();
        string source = await fixture.DatabaseAsync("source.db");
        string obstacle = fixture.FilePath("file-not-directory");
        await File.WriteAllTextAsync(obstacle, "untouched");
        await using ServiceProvider provider = fixture.Provider(options =>
        {
            options.Discovery.AddFile("source", source);
            options.Zip.Enabled = true;
            options.Zip.PublishDirectory = obstacle;
        });
        ISqliteHotBackupOrchestrator backup = provider.GetRequiredService<ISqliteHotBackupOrchestrator>();
        SqliteBackupException error = await Assert.ThrowsExactlyAsync<SqliteBackupException>(() => backup.BackupAllAsync());
        Assert.AreEqual(SqliteBackupStatus.Failed, error.Result.Archive!.Status);
        Assert.IsTrue(error.Result.Targets[0].RawBackupRetained);
        Assert.AreEqual(1L, await BackupFixture.CountAsync(error.Result.Targets[0].BackupFile!));
        SqliteBackupRunResult success = await backup.BackupAllAsync(options => options.Zip.PublishDirectory = "archives");
        Assert.AreEqual(SqliteBackupStatus.Succeeded, success.Archive!.Status);
        Assert.AreEqual(Path.Combine(fixture.Output, "archives"), Path.GetDirectoryName(success.Archive.File));
        Assert.AreEqual("untouched", await File.ReadAllTextAsync(obstacle));
        await Assert.ThrowsExactlyAsync<SqliteBackupException>(() => backup.BackupAllAsync());
    }

    [TestMethod]
    public async Task InvalidSelectionFailsBeforeAnyBackupAndDoesNotHoldGate()
    {
        using BackupFixture fixture = new();
        string source = await fixture.DatabaseAsync("source.db");
        await using ServiceProvider provider = fixture.Provider(options => options.Discovery.AddFile("source", source));
        ISqliteHotBackupOrchestrator backup = provider.GetRequiredService<ISqliteHotBackupOrchestrator>();
        await Assert.ThrowsExactlyAsync<ArgumentException>(() => backup.BackupAsync(new[] { "source", "unknown" }));
        await Assert.ThrowsExactlyAsync<ArgumentException>(() => backup.BackupAsync(Array.Empty<string>()));
        await Assert.ThrowsExactlyAsync<ArgumentException>(() => backup.BackupAsync(" "));
        await Assert.ThrowsExactlyAsync<ArgumentNullException>(() => backup.BackupAsync((string)null!));
        await Assert.ThrowsExactlyAsync<ArgumentNullException>(() => backup.BackupAsync((string[])null!));
        await Assert.ThrowsExactlyAsync<ArgumentNullException>(() => backup.BackupAllAsync(null!));
        await Assert.ThrowsExactlyAsync<ArgumentNullException>(() => backup.BackupAsync(new[] { "source" }, null!));
        await Assert.ThrowsExactlyAsync<ArgumentNullException>(() => backup.BackupAsync(null!, _ => { }));
        Assert.IsFalse(Directory.Exists(fixture.Output));
        Assert.IsFalse((await backup.BackupAsync("SOURCE")).HasFailures);
    }

    [TestMethod]
    public async Task AliasLimitConflictsFailOnlyThatSourceAndUnselectedAliasesDoNotApply()
    {
        using BackupFixture fixture = new();
        string source = await fixture.DatabaseAsync("source.db");
        string other = await fixture.DatabaseAsync("other.db");
        await using ServiceProvider provider = fixture.Provider(options =>
        {
            options.Discovery.AddFile("a", source);
            options.Discovery.AddFile("b", source);
            options.Discovery.AddFile("c", source);
            options.Discovery.AddFile("other", other);
            options.ConfigureExecutionLimit("a", limit => limit.MaxRewindWorkFactor = 4);
            options.ConfigureExecutionLimit("b", limit => limit.MaxRewindWorkFactor = 3);
        });
        ISqliteHotBackupOrchestrator backup = provider.GetRequiredService<ISqliteHotBackupOrchestrator>();
        SqliteBackupException error = await Assert.ThrowsExactlyAsync<SqliteBackupException>(() => backup.BackupAllAsync());
        Assert.HasCount(2, error.Result.Targets);
        Assert.HasCount(3, error.Result.Targets.Single(target => target.Status == SqliteBackupStatus.Failed).TargetIds);
        Assert.AreEqual(1, error.Result.Targets.Count(target => target.Status == SqliteBackupStatus.Succeeded));
        Assert.IsFalse((await backup.BackupAsync(new[] { "a", "c" })).HasFailures);
        Assert.IsFalse((await backup.BackupAsync(new[] { "b", "c" })).HasFailures);
        Assert.IsFalse((await backup.BackupAsync(new[] { "a", "b" }, options =>
            options.ConfigureExecutionLimit("b", limit => limit.MaxRewindWorkFactor = 4))).HasFailures);
    }

    [TestMethod]
    public async Task WalBackupIncludesCommittedUncheckpointedWritesAndIsStandalone()
    {
        using BackupFixture fixture = new();
        string source = await fixture.DatabaseAsync("source.db", wal: true);
        await using SqliteConnection writer = BackupFixture.Open(source);
        using (SqliteCommand command = writer.CreateCommand())
        {
            command.CommandText = "PRAGMA wal_autocheckpoint=0; INSERT INTO entries(value) VALUES ('in WAL');";
            await command.ExecuteNonQueryAsync();
        }
        Assert.IsTrue(new FileInfo(source + "-wal").Length > 0);
        await using ServiceProvider provider = fixture.Provider(options => options.Discovery.AddFile("wal", source));
        SqliteBackupResult result = (await provider.GetRequiredService<ISqliteHotBackupOrchestrator>().BackupAllAsync()).Targets[0];
        Assert.IsFalse(File.Exists(result.BackupFile + "-wal"));
        string standalone = fixture.FilePath("standalone.db");
        File.Move(result.BackupFile!, standalone);
        Assert.AreEqual(2L, await BackupFixture.CountAsync(standalone));
        Assert.AreEqual(2L, await BackupFixture.CountAsync(source));
    }

    [TestMethod]
    public async Task ConcurrentCallsPublishDistinctFilesWithoutOverwriting()
    {
        using BackupFixture fixture = new();
        string source = await fixture.DatabaseAsync("source.db", payloadBytes: 300_000);
        await using ServiceProvider provider = fixture.Provider(options =>
        {
            options.Discovery.AddFile("source", source);
            options.Backup.FileNamePattern = "same.db";
        });
        ISqliteHotBackupOrchestrator backup = provider.GetRequiredService<ISqliteHotBackupOrchestrator>();
        SqliteBackupRunResult[] reports = await Task.WhenAll(backup.BackupAllAsync(), backup.BackupAllAsync(), backup.BackupAllAsync());
        string[] paths = reports.Select(report => report.Targets[0].BackupFile!).ToArray();
        Assert.AreEqual(3, paths.Distinct().Count());
        foreach (string path in paths) { Assert.AreEqual(1L, await BackupFixture.CountAsync(path)); }
    }

    [TestMethod]
    public async Task CancellationDuringSteppedCopyRemovesStagingAndReleasesGate()
    {
        using BackupFixture fixture = new();
        string source = await fixture.DatabaseAsync("large.db", payloadBytes: 8_000_000);
        await using ServiceProvider provider = fixture.Provider(options => options.Discovery.AddFile("large", source));
        ISqliteHotBackupOrchestrator backup = provider.GetRequiredService<ISqliteHotBackupOrchestrator>();
        using CancellationTokenSource cancellation = new();
        Task<SqliteBackupRunResult> running = backup.BackupAllAsync(cancellation.Token);
        Assert.IsFalse(running.IsCompleted, "A multi-step copy should reach its cancellable delay.");
        cancellation.Cancel();
        await Assert.ThrowsAsync<OperationCanceledException>(() => running);
        Assert.IsFalse(Directory.EnumerateFiles(fixture.Output).Any());
        await Assert.ThrowsAsync<OperationCanceledException>(() => backup.GetTargetsAsync(cancellation.Token));
        await Assert.ThrowsAsync<OperationCanceledException>(() => backup.BackupAllAsync(cancellation.Token));
        Assert.IsFalse((await backup.BackupAllAsync()).HasFailures);
    }

    [TestMethod]
    public async Task InvalidDatabaseAndInvalidOutputAreLocalFailures()
    {
        using BackupFixture fixture = new();
        string corrupt = fixture.FilePath("corrupt.db");
        await File.WriteAllTextAsync(corrupt, "This is not SQLite.");
        string healthy = await fixture.DatabaseAsync("healthy.db");
        await using ServiceProvider provider = fixture.Provider(options =>
        {
            options.Discovery.AddFile("corrupt", corrupt);
            options.Discovery.AddFile("healthy", healthy);
        });
        ISqliteHotBackupOrchestrator backup = provider.GetRequiredService<ISqliteHotBackupOrchestrator>();
        SqliteBackupException error = await Assert.ThrowsExactlyAsync<SqliteBackupException>(() => backup.BackupAllAsync());
        Assert.AreEqual(1, error.Result.Targets.Count(target => target.Status == SqliteBackupStatus.Succeeded));
        Assert.AreEqual(1, error.Result.Targets.Count(target => target.Status == SqliteBackupStatus.Failed));
        string obstacle = fixture.FilePath("obstacle");
        await File.WriteAllTextAsync(obstacle, "keep");
        SqliteBackupException outputError = await Assert.ThrowsExactlyAsync<SqliteBackupException>(() => backup.BackupAsync(new[] { "healthy" },
            options => options.Backup.Directory = obstacle));
        Assert.AreEqual(SqliteBackupStatus.Failed, outputError.Result.Targets[0].Status);
        Assert.AreEqual("keep", await File.ReadAllTextAsync(obstacle));
    }

    [TestMethod]
    public async Task EmptyInventoryIsAnExplicitSuccessfulNoOp()
    {
        using BackupFixture fixture = new();
        await using ServiceProvider provider = fixture.Provider();
        SqliteBackupRunResult result = await provider.GetRequiredService<ISqliteHotBackupOrchestrator>().BackupAllAsync();
        Assert.IsEmpty(result.Targets);
        Assert.IsNull(result.Archive);
        Assert.IsFalse(result.HasFailures);
        Assert.IsFalse(Directory.Exists(fixture.Output));
        Assert.ThrowsExactly<ArgumentNullException>(() => new SqliteBackupException(null!));
    }
}
