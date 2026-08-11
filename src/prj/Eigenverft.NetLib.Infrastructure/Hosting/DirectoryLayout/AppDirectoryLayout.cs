using System;
using System.Collections.Generic;
using System.IO;

namespace Eigenverft.NetLib.Infrastructure.Hosting.DirectoryLayout
{
    /// <summary>
    /// Represents a resolved executable-root directory layout with named writable child directories.
    /// </summary>
    public sealed class AppDirectoryLayout : IAppDirectoryLayout
    {
        /// <summary>
        /// Initializes a new instance of <see cref="AppDirectoryLayout"/>.
        /// </summary>
        /// <param name="rootPath">The executable-root directory path.</param>
        /// <param name="directoriesByKey">Resolved directory paths by semantic key.</param>
        public AppDirectoryLayout(string rootPath, IReadOnlyDictionary<string, string> directoriesByKey)
        {
            if (string.IsNullOrWhiteSpace(rootPath))
            {
                throw new ArgumentException("Root path must not be null/empty.", nameof(rootPath));
            }

            RootPath = rootPath;
            GetByKey = directoriesByKey ?? throw new ArgumentNullException(nameof(directoriesByKey));
        }

        /// <summary>Gets the executable-root directory path.</summary>
        public string RootPath { get; }

        /// <summary>Gets resolved directory paths by semantic key.</summary>
        public IReadOnlyDictionary<string, string> GetByKey { get; }

        /// <summary>Gets a directory path by custom semantic key.</summary>
        /// <param name="key">The semantic key.</param>
        public string this[string key] => Get(key);

        /// <summary>Gets a directory path by standard typed key.</summary>
        /// <param name="directory">The standard directory key.</param>
        public string this[DefaultDirectory directory] => Get(directory);

        /// <summary>Gets a directory path by standard typed key.</summary>
        /// <param name="directory">The standard directory key.</param>
        /// <returns>The resolved directory path.</returns>
        public string Get(DefaultDirectory directory)
        {
            return Get(directory.GetKey());
        }

        /// <summary>Gets a directory path by custom semantic key.</summary>
        /// <param name="key">The semantic key.</param>
        /// <returns>The resolved directory path.</returns>
        /// <exception cref="ArgumentException">The key is null, empty, or whitespace.</exception>
        /// <exception cref="KeyNotFoundException">The key is not configured.</exception>
        public string Get(string key)
        {
            if (string.IsNullOrWhiteSpace(key))
            {
                throw new ArgumentException("Key must not be null/empty.", nameof(key));
            }

            if (!GetByKey.TryGetValue(key, out string? path))
            {
                throw new KeyNotFoundException(
                    $"Directory key '{key}' is not configured. Known keys: {string.Join(", ", GetByKey.Keys)}");
            }

            return path;
        }

        /// <summary>Tries to get a directory path by standard typed key.</summary>
        /// <param name="directory">The standard directory key.</param>
        /// <param name="directoryPath">The resolved directory path if found.</param>
        /// <returns><c>true</c> when the directory is present; otherwise <c>false</c>.</returns>
        public bool TryGet(DefaultDirectory directory, out string directoryPath)
        {
            return TryGet(directory.GetKey(), out directoryPath);
        }

        /// <summary>Tries to get a directory path by custom semantic key.</summary>
        /// <param name="key">The semantic key.</param>
        /// <param name="directoryPath">The resolved directory path if found.</param>
        /// <returns><c>true</c> when the key is present; otherwise <c>false</c>.</returns>
        public bool TryGet(string key, out string directoryPath)
        {
            directoryPath = string.Empty;

            if (string.IsNullOrWhiteSpace(key))
            {
                return false;
            }

            if (GetByKey.TryGetValue(key, out string? found) && !string.IsNullOrEmpty(found))
            {
                directoryPath = found;
                return true;
            }

            return false;
        }
    }
}
