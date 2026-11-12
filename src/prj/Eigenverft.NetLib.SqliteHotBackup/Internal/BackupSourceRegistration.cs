using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.Extensions.DependencyInjection;

namespace Eigenverft.NetLib.SqliteHotBackup.Internal
{
    internal interface IBackupSourceRegistration
    {
        string Name { get; }
        Task<IReadOnlyList<SqliteBackupFileSource>> GetSourcesAsync(IServiceProvider services, CancellationToken cancellationToken);
    }

    // Resolving a provider inside its own attempt also isolates constructor failures from other sources.
    internal sealed class BackupSourceRegistration<TProvider> : IBackupSourceRegistration
        where TProvider : class, ISqliteBackupSourceProvider
    {
        public string Name => typeof(TProvider).FullName ?? typeof(TProvider).Name;
        public Task<IReadOnlyList<SqliteBackupFileSource>> GetSourcesAsync(IServiceProvider services, CancellationToken cancellationToken)
            => services.GetRequiredService<TProvider>().GetSourcesAsync(cancellationToken);
    }
}
