using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Eigenverft.NetLib.SqliteHotBackup.Internal;

using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Eigenverft.NetLib.SqliteHotBackup.Tests;

[TestClass]
public sealed class PublicationTests
{
    [TestMethod]
    public async Task PublicationPreservesExistingFilesAndRetriesTransientSharingViolation()
    {
        using BackupFixture fixture = new();
        string desired = fixture.FilePath("backup.db");
        string staging = fixture.FilePath("staging.tmp");
        await File.WriteAllTextAsync(desired, "old");
        await File.WriteAllTextAsync(staging, "new");
        string published = await BackupFiles.PublishAsync(staging, desired, default);
        Assert.AreEqual("old", await File.ReadAllTextAsync(desired));
        Assert.AreEqual("new", await File.ReadAllTextAsync(published));
        Assert.AreEqual(fixture.FilePath("backup_2.db"), published);
        if (OperatingSystem.IsWindows())
        {
            await File.WriteAllTextAsync(staging, "after lock");
            FileStream locked = File.Open(staging, FileMode.Open, FileAccess.Read, FileShare.None);
            Task<string> pending = BackupFiles.PublishAsync(staging, fixture.FilePath("after.db"), default);
            Assert.IsFalse(pending.IsCompleted);
            await locked.DisposeAsync();
            Assert.AreEqual("after lock", await File.ReadAllTextAsync(await pending));
        }
    }

    [TestMethod]
    public async Task FailedPublicationLeavesOriginalAndOwnStagingForCleanup()
    {
        using BackupFixture fixture = new();
        string staging = fixture.FilePath("staging.tmp");
        await File.WriteAllTextAsync(staging, "data");
        string impossible = Path.Combine(fixture.FilePath("missing-directory"), "backup.db");
        await Assert.ThrowsAsync<IOException>(() => BackupFiles.PublishAsync(staging, impossible, default));
        Assert.IsTrue(File.Exists(staging));
        await Assert.ThrowsAsync<OperationCanceledException>(() => BackupFiles.PublishAsync(staging, impossible, new CancellationToken(true)));
        foreach (string suffix in new[] { "-wal", "-shm", "-journal" }) { await File.WriteAllTextAsync(staging + suffix, "sidecar"); }
        BackupFiles.DeleteTemporary(staging, NullLogger.Instance);
        BackupFiles.DeleteTemporary(null, NullLogger.Instance);
        Assert.IsEmpty(Directory.GetFiles(fixture.Root));
    }

    [TestMethod]
    public async Task ArchiveUsesUniqueEntryNamesAndKeepsRawWhenRequested()
    {
        using BackupFixture fixture = new();
        string first = await fixture.DatabaseAsync("a/same.db");
        string second = await fixture.DatabaseAsync("b/same.db");
        List<SqliteBackupResult> results = new() { Successful(first), Successful(second) };
        SqliteBackupArchiveResult? archive = await BackupArchive.CreateAsync(results, new()
        {
            DeleteRawBackupsAfterZip = false,
            PublishDirectory = fixture.Output,
            FileNamePattern = "archive"
        }, TimeProvider.System, NullLogger.Instance, default);
        Assert.AreEqual(SqliteBackupStatus.Succeeded, archive!.Status);
        using ZipArchive zip = ZipFile.OpenRead(archive.File!);
        CollectionAssert.AreEquivalent(new[] { "same.db", "same_2.db" }, zip.Entries.Select(entry => entry.Name).ToArray());
        Assert.IsTrue(results.All(result => result.RawBackupRetained));
        Assert.IsTrue(File.Exists(first));
        Assert.IsTrue(File.Exists(second));
    }

    [TestMethod]
    public async Task MissingArchivedInputFailsAndNeverDeletesOtherSuccessfulFiles()
    {
        using BackupFixture fixture = new();
        string first = await fixture.DatabaseAsync("first.db");
        List<SqliteBackupResult> results = new() { Successful(first), Successful(fixture.FilePath("missing.db")) };
        SqliteBackupArchiveResult? archive = await BackupArchive.CreateAsync(results, new(), TimeProvider.System, NullLogger.Instance, default);
        Assert.AreEqual(SqliteBackupStatus.Failed, archive!.Status);
        Assert.IsTrue(File.Exists(first));
        Assert.IsFalse(Directory.GetFiles(fixture.Root, "*.zip").Any());
        Assert.IsFalse(Directory.GetFiles(fixture.Root, "*.tmp").Any());
    }

    [TestMethod]
    public async Task ArchiveCancellationCleansStagingWithoutDeletingRawFiles()
    {
        using BackupFixture fixture = new();
        string first = await fixture.DatabaseAsync("first.db");
        await Assert.ThrowsAsync<OperationCanceledException>(() => BackupArchive.CreateAsync(new() { Successful(first) }, new(),
            TimeProvider.System, NullLogger.Instance, new CancellationToken(true)));
        Assert.IsTrue(File.Exists(first));
        Assert.IsEmpty(Directory.GetFiles(fixture.Root, "*.tmp"));
        Assert.IsNull(await BackupArchive.CreateAsync(new(), new(), TimeProvider.System, NullLogger.Instance, default));
    }

    [TestMethod]
    public void FilenameTokensAreDeterministicAndCannotEscapeOutputDirectory()
    {
        DateTimeOffset utc = new(2026, 11, 10, 15, 16, 17, TimeSpan.Zero);
        Assert.AreEqual("source_20261110_151617.db", BackupFiles.FileName("{NAME}_{utc:yyyyMMdd_HHmmss}", "source", utc, ".db"));
        Assert.AreEqual("source.DB", BackupFiles.FileName("{name}.DB", "source", utc, ".db"));
        Assert.AreEqual("source_2026_11.db", BackupFiles.FileName("{name}_{utc:yyyy}_{UTC:MM}.db", "source", utc, ".db"));
        foreach (string pattern in new[] { "{utc:yyyy", "{unknown}", "../out", "..\\out", ".", "..", "}", " " })
        {
            Assert.ThrowsExactly<ArgumentException>(() => BackupFiles.FileName(pattern, "source", utc, ".db"));
        }
        if (OperatingSystem.IsWindows())
        {
            Assert.AreEqual("a_b.db", BackupFiles.FileName("{name}", "a:b", utc, ".db"));
            Assert.ThrowsExactly<ArgumentException>(() => BackupFiles.FileName("bad:name", "source", utc, ".db"));
        }
    }

    [TestMethod]
    public async Task PublishedArchiveSurvivesRawCleanupFailureAndWarnsWithRetainedPath()
    {
        if (!OperatingSystem.IsWindows()) { return; }
        using BackupFixture fixture = new();
        string first = await fixture.DatabaseAsync("first.db");
        List<SqliteBackupResult> results = new() { Successful(first) };
        using FileStream locked = File.Open(first, FileMode.Open, FileAccess.Read, FileShare.Read);
        SqliteBackupArchiveResult? archive = await BackupArchive.CreateAsync(results, new(), TimeProvider.System, fixture.Logs, default);
        Assert.AreEqual(SqliteBackupStatus.Succeeded, archive!.Status);
        Assert.IsTrue(results[0].RawBackupRetained);
        Assert.IsTrue(File.Exists(archive.File));
        Assert.IsTrue(fixture.Logs.Entries.Any(entry => entry.Id.Id == 4));
        string staging = fixture.FilePath("staging.tmp");
        await File.WriteAllTextAsync(staging, "owned temporary");
        using (FileStream blocked = File.Open(staging, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            BackupFiles.DeleteTemporary(staging, fixture.Logs);
            Assert.IsTrue(fixture.Logs.Entries.Any(entry => entry.Id.Id == 1));
        }
        BackupFiles.DeleteTemporary(staging, fixture.Logs);
    }

    private static SqliteBackupResult Successful(string file) => new(new[] { "synthetic" }, SqliteBackupStatus.Succeeded,
        null, file, null, TimeSpan.Zero, RawBackupRetained: true);
}
