using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace Eigenverft.NetLib.Infrastructure.Hosting.Configuration.Values
{
    /// <summary>Encodes selected string values directly in JSON configuration files.</summary>
    public static class JsonConfigurationFileEncoder
    {
        private const int MaxConcurrentRewriteAttempts = 3;
        private static readonly Encoding PersistedEncoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);

        /// <summary>Encodes values whose complete configuration paths match any supplied glob pattern.</summary>
        /// <param name="jsonFilePath">The JSON file that may be changed.</param>
        /// <param name="keyPathPatterns">Case-insensitive glob patterns for complete colon-separated configuration paths.</param>
        /// <param name="codec">The reversible codec applied to matching clear-text values.</param>
        /// <param name="nullAsEmpty">Whether matching JSON <see langword="null"/> values are encoded as empty strings.</param>
        /// <returns>The number of values changed.</returns>
        /// <remarks>
        /// The file is rewritten only when at least one value changes. The encoder holds exclusive access to the source for the
        /// read/transform/write cycle so a successful rewrite cannot overwrite a newer normal file write that occurred after its
        /// snapshot was read. Rewriting uses formatted JSON and therefore removes comments, trailing commas and the original
        /// whitespace. Recognized encoded wrappers are left untouched, including wrappers created by another codec; codec migration
        /// requires an explicit decode-and-rewrite operation.
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

            for (int attempt = 1; attempt <= MaxConcurrentRewriteAttempts; attempt++)
            {
                EncodeAttemptResult result = EncodeUnderExclusiveAccess(
                    jsonFilePath,
                    matcher,
                    codec,
                    nullAsEmpty);
                if (result.Updated == 0)
                {
                    return 0;
                }

                // FileShare.None closes the ordinary read/modify/write race on platforms that enforce sharing semantics.
                // Re-check the path after releasing the handle as well: platforms that allow an atomic rename over an open file
                // can otherwise leave our protected write on an unlinked file while a newer replacement remains at the path.
                if (File.Exists(jsonFilePath) &&
                    File.ReadAllBytes(jsonFilePath).SequenceEqual(result.PersistedBytes!))
                {
                    return result.Updated;
                }

                if (attempt == MaxConcurrentRewriteAttempts)
                {
                    throw new IOException(
                        $"JSON configuration file '{jsonFilePath}' changed repeatedly while value protection was being persisted.");
                }
            }

            throw new InvalidOperationException("The JSON protection rewrite loop completed unexpectedly.");
        }

        private static EncodeAttemptResult EncodeUnderExclusiveAccess(
            string jsonFilePath,
            ConfigurationKeyPathGlobMatcher matcher,
            ConfigurationValueCodec codec,
            bool nullAsEmpty)
        {
            FileStream stream;
            try
            {
                stream = new FileStream(
                    jsonFilePath,
                    FileMode.Open,
                    FileAccess.ReadWrite,
                    FileShare.None);
            }
            catch (UnauthorizedAccessException)
            {
                // Preserve the historical no-op behavior for already protected read-only files. Only clear text requires write access.
                using FileStream readOnlyStream = new(
                    jsonFilePath,
                    FileMode.Open,
                    FileAccess.Read,
                    FileShare.None);
                EncodeAttemptResult readOnlyResult = PrepareEncoding(
                    readOnlyStream,
                    matcher,
                    codec,
                    nullAsEmpty);
                if (readOnlyResult.Updated == 0)
                {
                    return readOnlyResult;
                }

                throw;
            }

            using (stream)
            {
                EncodeAttemptResult result = PrepareEncoding(stream, matcher, codec, nullAsEmpty);
                if (result.Updated != 0)
                {
                    RewriteLockedStream(stream, result.OriginalBytes, result.PersistedBytes!);
                }

                return result;
            }
        }

        private static EncodeAttemptResult PrepareEncoding(
            FileStream stream,
            ConfigurationKeyPathGlobMatcher matcher,
            ConfigurationValueCodec codec,
            bool nullAsEmpty)
        {
            byte[] originalBytes = ReadAllBytes(stream);
            string originalText = ReadText(originalBytes);
            JsonNode? root = JsonNode.Parse(
                originalText,
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
                return new EncodeAttemptResult(updated, originalBytes, persistedBytes: null);
            }

            string formattedJson = root.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
            return new EncodeAttemptResult(updated, originalBytes, PersistedEncoding.GetBytes(formattedJson));
        }

        private static byte[] ReadAllBytes(FileStream stream)
        {
            stream.Position = 0;
            using var memory = new MemoryStream();
            stream.CopyTo(memory);
            return memory.ToArray();
        }

        private static string ReadText(byte[] bytes)
        {
            using var memory = new MemoryStream(bytes, writable: false);
            using var reader = new StreamReader(
                memory,
                Encoding.UTF8,
                detectEncodingFromByteOrderMarks: true);
            return reader.ReadToEnd();
        }

        private static void RewriteLockedStream(FileStream stream, byte[] originalBytes, byte[] persistedBytes)
        {
            try
            {
                WriteLockedStream(stream, persistedBytes);
            }
            catch (Exception writeException)
            {
                try
                {
                    WriteLockedStream(stream, originalBytes);
                }
                catch (Exception restoreException)
                {
                    throw new IOException(
                        "Failed to persist protected JSON and failed to restore the original file content.",
                        new AggregateException(writeException, restoreException));
                }

                throw;
            }
        }

        private static void WriteLockedStream(FileStream stream, byte[] content)
        {
            stream.Position = 0;
            stream.SetLength(0);
            stream.Write(content, 0, content.Length);
            stream.Flush(flushToDisk: true);
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

        private readonly struct EncodeAttemptResult
        {
            public EncodeAttemptResult(int updated, byte[] originalBytes, byte[]? persistedBytes)
            {
                Updated = updated;
                OriginalBytes = originalBytes;
                PersistedBytes = persistedBytes;
            }

            public int Updated { get; }

            public byte[] OriginalBytes { get; }

            public byte[]? PersistedBytes { get; }
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
