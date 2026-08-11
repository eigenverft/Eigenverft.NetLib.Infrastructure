using System.Collections.Generic;

namespace Eigenverft.NetLib.Infrastructure.Hosting.DirectoryLayout
{
    /// <summary>
    /// Provides access to a resolved application directory layout.
    /// </summary>
    public interface IAppDirectoryLayout
    {
        /// <summary>Gets the application root directory.</summary>
        string RootPath { get; }

        /// <summary>Gets resolved directory paths by semantic key.</summary>
        IReadOnlyDictionary<string, string> GetByKey { get; }

        /// <summary>Gets a directory path by custom semantic key.</summary>
        string this[string key] { get; }

        /// <summary>Gets a directory path by standard typed key.</summary>
        string this[DefaultDirectory directory] { get; }

        /// <summary>Gets a directory path by standard typed key.</summary>
        string Get(DefaultDirectory directory);

        /// <summary>Gets a directory path by custom semantic key.</summary>
        string Get(string key);

        /// <summary>Tries to get a directory path by standard typed key.</summary>
        bool TryGet(DefaultDirectory directory, out string directoryPath);

        /// <summary>Tries to get a directory path by custom semantic key.</summary>
        bool TryGet(string key, out string directoryPath);
    }
}
