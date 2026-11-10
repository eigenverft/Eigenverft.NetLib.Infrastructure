using System;
using System.Collections.Concurrent;
using System.IO;
using System.Threading.Tasks;

using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Eigenverft.NetLib.SqliteHotBackup.Tests;

internal sealed class BackupFixture : IDisposable
{
    internal string Root { get; } = Path.Combine(AppContext.BaseDirectory, "BackupTests", Guid.NewGuid().ToString("N"));
    internal string Output => Path.Combine(Root, "backups");
    internal EventLog Logs { get; } = new();

    internal BackupFixture() => Directory.CreateDirectory(Root);
    internal string FilePath(string name) => Path.GetFullPath(name, Root);

    internal async Task<string> DatabaseAsync(string name, bool wal = false, int payloadBytes = 0)
    {
        string path = FilePath(name);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await using SqliteConnection connection = Open(path);
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = (wal ? "PRAGMA journal_mode=WAL;" : "")
            + "CREATE TABLE entries(value TEXT, payload BLOB); INSERT INTO entries VALUES ('committed', zeroblob($bytes));";
        command.Parameters.AddWithValue("$bytes", payloadBytes);
        await command.ExecuteNonQueryAsync();
        return path;
    }

    internal static SqliteConnection Open(string path)
    {
        SqliteConnection connection = new(new SqliteConnectionStringBuilder { DataSource = path, Pooling = false }.ToString());
        connection.Open();
        return connection;
    }

    internal static async Task<long> CountAsync(string path)
    {
        await using SqliteConnection connection = new(new SqliteConnectionStringBuilder
        { DataSource = path, Pooling = false, Mode = SqliteOpenMode.ReadOnly }.ToString());
        await connection.OpenAsync();
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "SELECT count(*) FROM entries;";
        return (long)(await command.ExecuteScalarAsync())!;
    }

    internal ServiceProvider Provider(Action<SqliteHotBackupOptions>? configure = null, Action<IServiceCollection>? register = null)
    {
        ServiceCollection services = new();
        register?.Invoke(services);
        services.AddLogging(builder => builder.SetMinimumLevel(LogLevel.Trace).AddProvider(Logs));
        services.AddSqliteHotBackup(options =>
        {
            options.Discovery.DiscoverDbContexts = false;
            options.Backup.Directory = Output;
            options.Zip.Enabled = false;
            configure?.Invoke(options);
        });
        return services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });
    }

    public void Dispose() => Directory.Delete(Root, recursive: true);
}

internal sealed class EventLog : ILoggerProvider, ILogger
{
    internal ConcurrentQueue<(EventId Id, string Message, Exception? Error)> Entries { get; } = new();
    public ILogger CreateLogger(string categoryName) => this;
    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
    public bool IsEnabled(LogLevel logLevel) => true;
    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        => Entries.Enqueue((eventId, formatter(state, exception), exception));
    public void Dispose() { }
}

internal sealed class ManualTime : TimeProvider
{
    private long _ticks;
    public override long TimestampFrequency => TimeSpan.TicksPerSecond;
    public override long GetTimestamp() => _ticks;
    public override DateTimeOffset GetUtcNow() => DateTimeOffset.UnixEpoch + TimeSpan.FromTicks(_ticks);
    internal void Advance(TimeSpan elapsed) => _ticks += elapsed.Ticks;
}
