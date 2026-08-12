using System;
using System.Collections.Generic;

using Eigenverft.NetLib.Infrastructure.Transformations;

namespace Eigenverft.NetLib.Infrastructure.Hosting.Configuration.Values
{
    /// <summary>Identifies the persisted wrapper used for one configuration value transform.</summary>
    public enum ConfigurationValueKind
    {
        /// <summary>Base64 representation.</summary>
        Base64 = 0,
        /// <summary>Windows DPAPI LocalMachine with Base64 payload.</summary>
        DpapiMachine = 1,
        /// <summary>Windows DPAPI LocalMachine with Base64Url payload.</summary>
        DpapiMachineBase64Url = 2,
        /// <summary>Password-derived AES protection.</summary>
        AesPassword = 3,
        /// <summary>ROT13 transformation.</summary>
        Rot13 = 4,
        /// <summary>Caesar transformation.</summary>
        Caesar = 5,
        /// <summary>JSON-safe Base92 representation.</summary>
        Base92JsonSafe = 6,
        /// <summary>ASP.NET Data Protection payload supplied by an external adapter.</summary>
        DataProtection = 7,
    }

    /// <summary>
    /// Represents one self-describing persisted configuration-value codec built around a reversible string transform.
    /// </summary>
    public sealed class ConfigurationValueCodec
    {
        internal delegate bool TryDecodeDelegate(string encodedValue, out string clearText);

        private readonly Func<string, string> _encode;
        private readonly TryDecodeDelegate _tryDecode;

        /// <summary>Creates a codec that persists the supplied reversible transform with a stable value-kind wrapper.</summary>
        /// <param name="name">Descriptive codec name.</param>
        /// <param name="persistedKind">Persisted wrapper kind.</param>
        /// <param name="transform">Reversible value transform.</param>
        public ConfigurationValueCodec(
            string name,
            ConfigurationValueKind persistedKind,
            ReversibleStringTransform transform)
            : this(
                name,
                clearText => ConfigurationValueFormat.Wrap(persistedKind, transform.Apply(clearText)),
                (string encodedValue, out string clearText) =>
                {
                    clearText = encodedValue;
                    if (!ConfigurationValueFormat.TryUnwrap(
                            encodedValue,
                            out ConfigurationValueKind actualKind,
                            out string payload) ||
                        actualKind != persistedKind ||
                        !transform.TryReverse(payload, out string reversed))
                    {
                        return false;
                    }

                    clearText = reversed;
                    return true;
                })
        {
            ArgumentNullException.ThrowIfNull(transform);
        }

        internal ConfigurationValueCodec(
            string name,
            Func<string, string> encode,
            TryDecodeDelegate tryDecode)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(name);
            ArgumentNullException.ThrowIfNull(encode);
            ArgumentNullException.ThrowIfNull(tryDecode);

            Name = name;
            _encode = encode;
            _tryDecode = tryDecode;
        }

        /// <summary>Gets the descriptive codec name.</summary>
        public string Name { get; }

        /// <summary>Encodes one value using this codec.</summary>
        public string Encode(string? clearText)
        {
            return _encode(clearText ?? string.Empty);
        }

        /// <summary>Attempts to decode one value using the complete codec.</summary>
        public bool TryDecode(string? encodedValue, out string clearText)
        {
            string value = encodedValue ?? string.Empty;
            if (_tryDecode(value, out string decodedValue))
            {
                clearText = decodedValue;
                return true;
            }

            clearText = value;
            return false;
        }
    }

    internal static class ConfigurationValueFormat
    {
        private const string Prefix = "enc:";

        private static readonly IReadOnlyDictionary<ConfigurationValueKind, string> KindToToken =
            new Dictionary<ConfigurationValueKind, string>
            {
                { ConfigurationValueKind.Base64, "q7m2n4" },
                { ConfigurationValueKind.DpapiMachine, "x1p9d0" },
                { ConfigurationValueKind.DpapiMachineBase64Url, "k4v8s2" },
                { ConfigurationValueKind.AesPassword, "a3s6p1" },
                { ConfigurationValueKind.Rot13, "r1t3o7" },
                { ConfigurationValueKind.Caesar, "c4e5s2" },
                { ConfigurationValueKind.Base92JsonSafe, "b9j2s7" },
                { ConfigurationValueKind.DataProtection, "d7p4r8" },
            };

        private static readonly IReadOnlyDictionary<string, ConfigurationValueKind> TokenToKind =
            new Dictionary<string, ConfigurationValueKind>(StringComparer.OrdinalIgnoreCase)
            {
                { "q7m2n4", ConfigurationValueKind.Base64 },
                { "x1p9d0", ConfigurationValueKind.DpapiMachine },
                { "k4v8s2", ConfigurationValueKind.DpapiMachineBase64Url },
                { "a3s6p1", ConfigurationValueKind.AesPassword },
                { "r1t3o7", ConfigurationValueKind.Rot13 },
                { "c4e5s2", ConfigurationValueKind.Caesar },
                { "b9j2s7", ConfigurationValueKind.Base92JsonSafe },
                { "d7p4r8", ConfigurationValueKind.DataProtection },
            };

        public static string Wrap(ConfigurationValueKind kind, string? payload)
        {
            if (!KindToToken.TryGetValue(kind, out string? token))
            {
                throw new ArgumentOutOfRangeException(nameof(kind), kind, "The configuration value kind has no persisted token.");
            }

            return $"{Prefix}{token}:{payload ?? string.Empty}";
        }

        public static bool TryUnwrap(
            string? value,
            out ConfigurationValueKind kind,
            out string payload)
        {
            kind = default;
            payload = string.Empty;

            if (value is null || !value.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            string remainder = value.Substring(Prefix.Length);
            int delimiterIndex = remainder.IndexOf(':');
            if (delimiterIndex <= 0)
            {
                return false;
            }

            string token = remainder.Substring(0, delimiterIndex);
            payload = remainder.Substring(delimiterIndex + 1);
            if (TokenToKind.TryGetValue(token, out kind))
            {
                return true;
            }

            if (string.Equals(token, "DpapiMachineBase64", StringComparison.OrdinalIgnoreCase))
            {
                kind = ConfigurationValueKind.DpapiMachineBase64Url;
                return true;
            }

            kind = default;
            return false;
        }
    }
}
