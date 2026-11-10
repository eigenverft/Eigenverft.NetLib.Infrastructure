using System;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

using SQLitePCL;

namespace Eigenverft.NetLib.SqliteHotBackup.Internal
{
    internal sealed record ResolvedSource(TargetRegistration Registration, string Path, string ConnectionString,
        ProgressStallLimitCalculation Limit, bool HasOverride, TimeSpan PreparationTime);

    internal sealed record SourceResolution(ResolvedSource? Source, string? IneligibleReason);

    internal static class SourceResolver
    {
        internal static async Task<SourceResolution> ResolveAsync(TargetRegistration registration,
            SqliteHotBackupOptions options, IServiceProvider services, TimeProvider time, CancellationToken ct)
        {
            long started = time.GetTimestamp();
            ProgressStallLimitCalculation limit = options.LimitFor(registration.Target.Id);
            DbContext? ownedContext = null;
            try
            {
                SqliteConnectionStringBuilder connection;
                if (registration.ContextType is Type contextType)
                {
                    DbContext context;
                    if (registration.UsesFactory)
                    {
                        Type factoryType = typeof(IDbContextFactory<>).MakeGenericType(contextType);
                        object factory = services.GetRequiredService(factoryType);
                        MethodInfo method = factoryType.GetMethod("CreateDbContext", Type.EmptyTypes)!;
                        ownedContext = (DbContext)method.Invoke(factory, null)!;
                        context = ownedContext;
                    }
                    else
                    {
                        context = (DbContext)services.GetRequiredService(contextType);
                    }
                    ct.ThrowIfCancellationRequested();
                    if (!context.Database.IsSqlite())
                    {
                        return new(null, "The automatically discovered context does not use SQLite.");
                    }
                    if (context.Database.GetDbConnection() is not SqliteConnection sqlite)
                    {
                        return new(null, "The context does not expose a Microsoft.Data.Sqlite connection.");
                    }
                    connection = new(sqlite.ConnectionString);
                    if (connection.Mode == SqliteOpenMode.Memory || string.IsNullOrWhiteSpace(connection.DataSource)
                        || connection.DataSource.Equals(":memory:", StringComparison.OrdinalIgnoreCase))
                    {
                        return new(null, "The discovered SQLite database is not file-backed.");
                    }
                }
                else
                {
                    string file = registration.Target.SourceFile!;
                    if (!File.Exists(file))
                    {
                        throw new FileNotFoundException("The explicitly registered database file does not exist.", file);
                    }
                    connection = new() { DataSource = file };
                }

                EnsureTime(started, time, limit);
                connection.Mode = SqliteOpenMode.ReadOnly;
                connection.Pooling = false;
                connection.DefaultTimeout = Math.Max(1, (int)Math.Ceiling(limit.NoProgressTimeout.TotalSeconds));
                // Inspect an independent read-only connection: discovery must never create a missing source
                // or change the lifetime/state of an application's EF connection.
                await using SqliteConnection source = new(connection.ToString());
                await source.OpenAsync(ct).ConfigureAwait(false);
                EnsureTime(started, time, limit);
                string? filename = raw.sqlite3_db_filename((sqlite3)source.Handle!, "main").utf8_to_string();
                if (string.IsNullOrWhiteSpace(filename))
                {
                    return new(null, "The discovered SQLite database is not file-backed.");
                }
                if (registration.ContextType is not null)
                {
                    await using SqliteCommand command = source.CreateCommand();
                    command.CommandText = "PRAGMA journal_mode;";
                    command.CommandTimeout = Math.Max(1, (int)Math.Ceiling((limit.NoProgressTimeout - time.GetElapsedTime(started)).TotalSeconds));
                    string? mode = Convert.ToString(await command.ExecuteScalarAsync(ct).ConfigureAwait(false), CultureInfo.InvariantCulture);
                    EnsureTime(started, time, limit);
                    if (!string.Equals(mode, "wal", StringComparison.OrdinalIgnoreCase))
                    {
                        return new(null, $"The discovered EF database uses journal_mode={mode}; WAL is required.");
                    }
                }
                string path = System.IO.Path.GetFullPath(filename);
                connection.DataSource = path;
                return new(new(registration, path, connection.ToString(), limit,
                    options.HasOverride(registration.Target.Id), time.GetElapsedTime(started)), null);
            }
            finally
            {
                if (ownedContext is not null)
                {
                    await ownedContext.DisposeAsync().ConfigureAwait(false);
                }
            }
        }

        private static void EnsureTime(long started, TimeProvider time, ProgressStallLimitCalculation limit)
        {
            if (time.GetElapsedTime(started) >= limit.NoProgressTimeout)
            {
                throw new TimeoutException("Source preparation exceeded its no-progress execution limit.");
            }
        }
    }
}
