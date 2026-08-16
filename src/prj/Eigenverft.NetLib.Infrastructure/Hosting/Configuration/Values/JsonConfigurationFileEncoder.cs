using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace Eigenverft.NetLib.Infrastructure.Hosting.Configuration.Values
{
    /// <summary>Encodes selected string values directly in JSON configuration files.</summary>
    public static class JsonConfigurationFileEncoder
    {
        /// <summary>Encodes values whose complete configuration paths match any supplied glob pattern.</summary>
        /// <param name="jsonFilePath">The JSON file that may be changed.</param>
        /// <param name="keyPathPatterns">Case-insensitive glob patterns for complete colon-separated configuration paths.</param>
        /// <param name="codec">The reversible codec applied to matching clear-text values.</param>
        /// <param name="nullAsEmpty">Whether matching JSON <see langword="null"/> values are encoded as empty strings.</param>
        /// <returns>The number of values changed.</returns>
        /// <remarks>
        /// The file is rewritten only when at least one value changes. Rewriting uses formatted JSON and therefore removes
        /// comments, trailing commas and the original whitespace. Recognized encoded wrappers are left untouched, including
        /// wrappers created by another codec; codec migration requires an explicit decode-and-rewrite operation.
        /// </remarks>
        public static int EncodeMatchingValuesInPlace(
            string jsonFilePath,
            IEnumerable<string> keyPathPatterns,
            ConfigurationValueCodec codec,
            bool nullAsEmpty = true)
        {
            ArgumentNullException.ThrowIfNull(codec);
            return EncodeMatchingValuesInPlace(
                jsonFilePath,
                new ConfigurationKeyPathGlobMatcher(keyPathPatterns, matchLastSegmentOnly: false),
                codec,
                nullAsEmpty);
        }

        /// <summary>Encodes values whose complete configuration paths match one glob pattern.</summary>
        public static int EncodeMatchingValuesInPlace(
            string jsonFilePath,
            string keyPathPattern,
            ConfigurationValueCodec codec,
            bool nullAsEmpty = true)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(keyPathPattern);
            return EncodeMatchingValuesInPlace(jsonFilePath, new[] { keyPathPattern }, codec, nullAsEmpty);
        }

        internal static int EncodeMatchingValuesInPlace(
            string jsonFilePath,
            ConfigurationKeyPathGlobMatcher matcher,
            ConfigurationValueCodec codec,
            bool nullAsEmpty = true)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(jsonFilePath);
            ArgumentNullException.ThrowIfNull(matcher);
            ArgumentNullException.ThrowIfNull(codec);

            if (!File.Exists(jsonFilePath))
            {
                throw new FileNotFoundException("JSON configuration file not found.", jsonFilePath);
            }

            JsonNode? root = JsonNode.Parse(
                File.ReadAllText(jsonFilePath),
                nodeOptions: null,
                documentOptions: new JsonDocumentOptions
                {
                    AllowTrailingCommas = true,
                    CommentHandling = JsonCommentHandling.Skip,
                });

            if (root is null)
            {
                throw new InvalidDataException("Parsed JSON root was null.");
            }

            int updated = 0;
            WalkAndEncode(root, string.Empty, matcher, codec, nullAsEmpty, ref updated);
            if (updated == 0)
            {
                return 0;
            }

            string formattedJson = root.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
            WriteAtomically(jsonFilePath, formattedJson);
            return updated;
        }

        private static void WalkAndEncode(
            JsonNode node,
            string currentPath,
            ConfigurationKeyPathGlobMatcher matcher,
            ConfigurationValueCodec codec,
            bool nullAsEmpty,
            ref int updated)
        {
            if (node is JsonObject jsonObject)
            {
                foreach (string propertyName in jsonObject.Select(property => property.Key).ToList())
                {
                    JsonNode? propertyValue = jsonObject[propertyName];
                    string propertyPath = CombinePath(currentPath, propertyName);
                    if (TryEncodeValue(propertyValue, propertyPath, matcher, codec, nullAsEmpty, out string? encoded))
                    {
                        jsonObject[propertyName] = encoded;
                        updated++;
                    }

                    if (propertyValue is not null && propertyValue is not JsonValue)
                    {
                        WalkAndEncode(propertyValue, propertyPath, matcher, codec, nullAsEmpty, ref updated);
                    }
                }

                return;
            }

            if (node is not JsonArray jsonArray)
            {
                return;
            }

            for (int index = 0; index < jsonArray.Count; index++)
            {
                JsonNode? item = jsonArray[index];
                string itemPath = CombinePath(currentPath, index.ToString());
                if (TryEncodeValue(item, itemPath, matcher, codec, nullAsEmpty, out string? encoded))
                {
                    jsonArray[index] = encoded;
                    updated++;
                }

                if (item is not null && item is not JsonValue)
                {
                    WalkAndEncode(item, itemPath, matcher, codec, nullAsEmpty, ref updated);
                }
            }
        }

        private static bool TryEncodeValue(
            JsonNode? value,
            string path,
            ConfigurationKeyPathGlobMatcher matcher,
            ConfigurationValueCodec codec,
            bool nullAsEmpty,
            out string? encoded)
        {
            encoded = null;
            if (!matcher.IsMatch(path))
            {
                return false;
            }

            if (value is null)
            {
                if (nullAsEmpty)
                {
                    encoded = codec.Encode(string.Empty);
                    return true;
                }

                return false;
            }

            if (value is not JsonValue || !TryGetString(value, out string? clearText))
            {
                return false;
            }

            string text = clearText ?? string.Empty;
            if (ConfigurationValueFormat.HasRecognizedWrapper(text))
            {
                return false;
            }

            encoded = codec.Encode(text);
            return true;
        }

        private static string CombinePath(string prefix, string segment)
        {
            return string.IsNullOrEmpty(prefix) ? segment : $"{prefix}:{segment}";
        }

        private static bool TryGetString(JsonNode valueNode, out string? value)
        {
            try
            {
                value = valueNode.GetValue<string?>();
                return true;
            }
            catch (InvalidOperationException)
            {
                value = null;
                return false;
            }
        }

        private static void WriteAtomically(string path, string content)
        {
            string directoryPath = Path.GetDirectoryName(path) ?? ".";
            string temporaryPath = Path.Combine(directoryPath, $"{Path.GetFileName(path)}.{Guid.NewGuid():N}.tmp");

            try
            {
                File.WriteAllText(temporaryPath, content);
                try
                {
                    File.Replace(temporaryPath, path, destinationBackupFileName: null);
                }
                catch (PlatformNotSupportedException)
                {
                    File.Move(temporaryPath, path, overwrite: true);
                }
                catch (IOException)
                {
                    File.Move(temporaryPath, path, overwrite: true);
                }
            }
            finally
            {
                if (File.Exists(temporaryPath))
                {
                    File.Delete(temporaryPath);
                }
            }
        }
    }

    internal sealed class ConfigurationKeyPathGlobMatcher
    {
        private readonly bool _matchLastSegmentOnly;
        private readonly Regex[] _patterns;

        public ConfigurationKeyPathGlobMatcher(
            IEnumerable<string> globPatterns,
            bool matchLastSegmentOnly)
        {
            ArgumentNullException.ThrowIfNull(globPatterns);
            string[] patterns = globPatterns.ToArray();
            if (patterns.Length == 0 || patterns.Any(string.IsNullOrWhiteSpace))
            {
                throw new ArgumentException(
                    "At least one non-empty key-path pattern is required.",
                    nameof(globPatterns));
            }

            _matchLastSegmentOnly = matchLastSegmentOnly;
            _patterns = patterns.Select(BuildRegex).ToArray();
        }

        public bool IsMatch(string keyPath)
        {
            string candidate = _matchLastSegmentOnly
                ? keyPath.Substring(keyPath.LastIndexOf(':') + 1)
                : keyPath;
            return _patterns.Any(pattern => pattern.IsMatch(candidate));
        }

        private static Regex BuildRegex(string globPattern)
        {
            string expression = "^" +
                Regex.Escape(globPattern)
                    .Replace(@"\*", ".*")
                    .Replace(@"\?", ".") +
                "$";

            return new Regex(
                expression,
                RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);
        }
    }
}
