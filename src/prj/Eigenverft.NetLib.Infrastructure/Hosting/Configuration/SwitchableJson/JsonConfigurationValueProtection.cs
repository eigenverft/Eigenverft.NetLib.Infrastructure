using System;

using Eigenverft.NetLib.Infrastructure.Hosting.Configuration.Values;

namespace Eigenverft.NetLib.Infrastructure.Hosting.Configuration.SwitchableJson
{
    /// <summary>
    /// Selects existing JSON values that are encoded once during switchable-source registration and decoded during every load.
    /// </summary>
    public sealed class JsonConfigurationValueProtection
    {
        private readonly ConfigurationKeyPathGlobMatcher _matcher;

        private JsonConfigurationValueProtection(
            ConfigurationValueCodec codec,
            ConfigurationKeyPathGlobMatcher matcher)
        {
            Codec = codec;
            _matcher = matcher;
            Decoder = JsonConfigurationCandidatePreparations.Decode(codec);
        }

        internal ConfigurationValueCodec Codec { get; }

        internal JsonConfigurationCandidatePreparation Decoder { get; }

        internal bool IsMatch(string keyPath)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(keyPath);
            return _matcher.IsMatch(keyPath);
        }

        /// <summary>Protects values by matching only their final JSON key name, regardless of nesting.</summary>
        public static JsonConfigurationValueProtection ForKeys(
            ConfigurationValueCodec codec,
            params string[] patterns)
        {
            ArgumentNullException.ThrowIfNull(codec);
            return new JsonConfigurationValueProtection(
                codec,
                new ConfigurationKeyPathGlobMatcher(patterns, matchLastSegmentOnly: true));
        }

        /// <summary>Protects values by matching their complete colon-separated configuration paths.</summary>
        public static JsonConfigurationValueProtection ForPaths(
            ConfigurationValueCodec codec,
            params string[] patterns)
        {
            ArgumentNullException.ThrowIfNull(codec);
            return new JsonConfigurationValueProtection(
                codec,
                new ConfigurationKeyPathGlobMatcher(patterns, matchLastSegmentOnly: false));
        }

        internal void ProtectExistingFile(string path)
        {
            if (System.IO.File.Exists(path))
            {
                _ = JsonConfigurationFileEncoder.EncodeMatchingValuesInPlace(path, _matcher, Codec);
            }
        }
    }
}
