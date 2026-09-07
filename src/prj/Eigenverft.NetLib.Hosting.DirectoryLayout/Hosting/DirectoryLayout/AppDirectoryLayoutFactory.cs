using System;
using System.Collections.Generic;
using System.IO;

namespace Eigenverft.NetLib.Infrastructure.Hosting.DirectoryLayout
{
    /// <summary>
    /// Creates executable-rooted application directory layouts and ensures the configured directories exist.
    /// </summary>
    public static class AppDirectoryLayoutFactory
    {
        /// <summary>
        /// Creates the standard application directory layout with optional folder-name overrides.
        /// </summary>
        /// <param name="directoryOverrides">Optional folder-name overrides for standard directories.</param>
        /// <param name="rootPath">
        /// Optional root path. When omitted, <see cref="AppContext.BaseDirectory"/> is used.
        /// </param>
        /// <param name="verifyWritable">Whether each resolved directory is verified with a write probe.</param>
        /// <returns>The resolved directory layout.</returns>
        public static AppDirectoryLayout CreateDefault(
            IReadOnlyDictionary<DefaultDirectory, string>? directoryOverrides = null,
            string? rootPath = null,
            bool verifyWritable = true)
        {
            var folderMap = new Dictionary<string, string>(BuildDefaultMap(), StringComparer.OrdinalIgnoreCase);

            if (directoryOverrides is not null)
            {
                foreach (KeyValuePair<DefaultDirectory, string> entry in directoryOverrides)
                {
                    folderMap[entry.Key.GetKey()] = entry.Value;
                }
            }

            return Create(folderMap, rootPath, verifyWritable);
        }

        /// <summary>
        /// Creates an application directory layout from custom semantic keys and direct-child folder names.
        /// </summary>
        /// <param name="folderMap">Semantic keys and direct-child folder names.</param>
        /// <param name="rootPath">
        /// Optional root path. When omitted, <see cref="AppContext.BaseDirectory"/> is used.
        /// </param>
        /// <param name="verifyWritable">Whether each resolved directory is verified with a write probe.</param>
        /// <returns>The resolved directory layout.</returns>
        public static AppDirectoryLayout Create(
            IReadOnlyDictionary<string, string> folderMap,
            string? rootPath = null,
            bool verifyWritable = true)
        {
            if (folderMap is null)
            {
                throw new ArgumentNullException(nameof(folderMap));
            }

            string root = ResolveRootPath(rootPath);
            Dictionary<string, string> normalized = NormalizeAndValidateMap(folderMap);
            Dictionary<string, string> resolved = ResolveAndEnsureDirectories(root, normalized, verifyWritable);

            return new AppDirectoryLayout(root, resolved);
        }

        private static IReadOnlyDictionary<string, string> BuildDefaultMap()
        {
            var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            foreach (DefaultDirectory directory in Enum.GetValues(typeof(DefaultDirectory)))
            {
                result[directory.GetKey()] = directory.GetDefaultFolderName();
            }

            return result;
        }

        private static string ResolveRootPath(string? rootPath)
        {
            string root = string.IsNullOrWhiteSpace(rootPath)
                ? AppContext.BaseDirectory
                : rootPath;

            root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));

            if (string.IsNullOrWhiteSpace(root))
            {
                throw new IOException("Unable to determine the application directory root.");
            }

            return root;
        }

        private static Dictionary<string, string> NormalizeAndValidateMap(IReadOnlyDictionary<string, string> input)
        {
            if (input.Count == 0)
            {
                throw new ArgumentException("folderMap must not be empty.", nameof(input));
            }

            var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            foreach (KeyValuePair<string, string> entry in input)
            {
                string key = entry.Key;
                string folderName = entry.Value;

                if (string.IsNullOrWhiteSpace(key))
                {
                    throw new ArgumentException("folderMap contains an empty key.", nameof(input));
                }

                if (string.IsNullOrWhiteSpace(folderName))
                {
                    throw new ArgumentException($"folderMap['{key}'] is null/empty.", nameof(input));
                }

                folderName = folderName.Trim();

                if (folderName.IndexOfAny(new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar }) >= 0)
                {
                    throw new ArgumentException(
                        $"folderMap['{key}'] must be a single folder name (no path separators), but was '{folderName}'.",
                        nameof(input));
                }

                if (Path.IsPathRooted(folderName))
                {
                    throw new ArgumentException(
                        $"folderMap['{key}'] must not be rooted, but was '{folderName}'.",
                        nameof(input));
                }

                if (string.Equals(folderName, ".", StringComparison.Ordinal) ||
                    string.Equals(folderName, "..", StringComparison.Ordinal) ||
                    folderName.Contains("..", StringComparison.Ordinal))
                {
                    throw new ArgumentException(
                        $"folderMap['{key}'] must not contain traversal patterns, but was '{folderName}'.",
                        nameof(input));
                }

                result[key.Trim()] = folderName;
            }

            return result;
        }

        private static Dictionary<string, string> ResolveAndEnsureDirectories(
            string rootPath,
            IReadOnlyDictionary<string, string> normalized,
            bool verifyWritable)
        {
            var resolved = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            foreach (KeyValuePair<string, string> entry in normalized)
            {
                string fullPath = Path.GetFullPath(Path.Combine(rootPath, entry.Value));

                EnsureDirectoryExists(fullPath);

                if (verifyWritable)
                {
                    VerifyWritable(fullPath);
                }

                resolved[entry.Key] = fullPath;
            }

            return resolved;
        }

        private static void EnsureDirectoryExists(string fullPath)
        {
            try
            {
                Directory.CreateDirectory(fullPath);
            }
            catch (Exception ex)
            {
                throw new IOException($"Failed to create directory '{fullPath}'.", ex);
            }
        }

        private static void VerifyWritable(string fullPath)
        {
            string probePath = Path.Combine(fullPath, $".writeprobe_{Guid.NewGuid():N}.tmp");

            try
            {
                using (var stream = new FileStream(
                    probePath,
                    FileMode.CreateNew,
                    FileAccess.Write,
                    FileShare.None,
                    bufferSize: 1,
                    options: FileOptions.None))
                {
                    stream.WriteByte(0);
                    stream.Flush(true);
                }

                try
                {
                    File.Delete(probePath);
                }
                catch
                {
                    // Best-effort cleanup only.
                }
            }
            catch (Exception ex)
            {
                throw new IOException($"Directory '{fullPath}' is not writable (write probe failed).", ex);
            }
        }
    }
}
