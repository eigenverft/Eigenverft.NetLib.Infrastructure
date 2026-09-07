using System;

using Eigenverft.NetLib.Infrastructure.Transformations;

namespace Eigenverft.NetLib.Infrastructure.Hosting.Configuration.Values
{
    /// <summary>Creates common persisted configuration-value codecs from reusable reversible string transforms.</summary>
    public static class ConfigurationValueCodecs
    {
        /// <summary>Gets the Base64 representation codec.</summary>
        public static ConfigurationValueCodec Base64 { get; } = new(
            "Base64",
            ConfigurationValueKind.Base64,
            ReversibleStringTransforms.Base64);

        /// <summary>Gets the JSON-safe Base92 representation codec.</summary>
        public static ConfigurationValueCodec Base92JsonSafe { get; } = new(
            "Base92JsonSafe",
            ConfigurationValueKind.Base92JsonSafe,
            ReversibleStringTransforms.Base92JsonSafe);

        /// <summary>Gets the ROT13 codec.</summary>
        public static ConfigurationValueCodec Rot13 { get; } = new(
            "Rot13",
            ConfigurationValueKind.Rot13,
            ReversibleStringTransforms.Rot13);

        /// <summary>Creates a Caesar codec.</summary>
        public static ConfigurationValueCodec Caesar(int shift)
        {
            int normalizedShift = shift % 26;
            if (normalizedShift < 0)
            {
                normalizedShift += 26;
            }

            return new ConfigurationValueCodec(
                $"Caesar({normalizedShift})",
                ConfigurationValueKind.Caesar,
                ReversibleStringTransforms.Caesar(normalizedShift));
        }

        /// <summary>Gets the Windows DPAPI LocalMachine codec with Base64 payload.</summary>
        public static ConfigurationValueCodec DpapiMachine { get; } = new(
            "DpapiMachine",
            ConfigurationValueKind.DpapiMachine,
            ReversibleStringTransforms.DpapiMachine);

        /// <summary>Gets the Windows DPAPI LocalMachine codec with Base64Url payload.</summary>
        public static ConfigurationValueCodec DpapiMachineBase64Url { get; } = new(
            "DpapiMachineBase64Url",
            ConfigurationValueKind.DpapiMachineBase64Url,
            ReversibleStringTransforms.DpapiMachineBase64Url);

        /// <summary>Creates a password-derived AES codec.</summary>
        public static ConfigurationValueCodec AesPassword(string password)
        {
            return new ConfigurationValueCodec(
                "AesPassword",
                ConfigurationValueKind.AesPassword,
                ReversibleStringTransforms.AesPassword(password));
        }

        /// <summary>Creates a password-derived AES codec from visible ASCII password bytes.</summary>
        public static ConfigurationValueCodec AesPassword(byte[] passwordAsciiBytes)
        {
            return new ConfigurationValueCodec(
                "AesPassword",
                ConfigurationValueKind.AesPassword,
                ReversibleStringTransforms.AesPassword(passwordAsciiBytes));
        }

        /// <summary>Creates a codec bound to the current physical-machine fingerprint.</summary>
        public static ConfigurationValueCodec PhysicalMachineBoundAes()
        {
            return new ConfigurationValueCodec(
                "PhysicalMachineBoundAes",
                ConfigurationValueKind.AesPassword,
                ReversibleStringTransforms.PhysicalMachineBoundAes());
        }

        /// <summary>Composes codecs in encoding order and decodes them in reverse order.</summary>
        public static ConfigurationValueCodec Compose(params ConfigurationValueCodec[] codecs)
        {
            ArgumentNullException.ThrowIfNull(codecs);
            if (codecs.Length == 0)
            {
                throw new ArgumentException("At least one codec is required.", nameof(codecs));
            }

            var pipeline = new ConfigurationValueCodec[codecs.Length];
            var names = new string[codecs.Length];
            for (int index = 0; index < codecs.Length; index++)
            {
                pipeline[index] = codecs[index] ??
                    throw new ArgumentException($"Codec at index {index} is null.", nameof(codecs));
                names[index] = pipeline[index].Name;
            }

            return new ConfigurationValueCodec(
                string.Join(" -> ", names),
                clearText => EncodePipeline(clearText, pipeline),
                (string encodedValue, out string clearText) =>
                    TryDecodePipeline(encodedValue, pipeline, out clearText));
        }

        private static string EncodePipeline(string clearText, ConfigurationValueCodec[] codecs)
        {
            string current = clearText;
            foreach (ConfigurationValueCodec codec in codecs)
            {
                current = codec.Encode(current);
            }

            return current;
        }

        private static bool TryDecodePipeline(
            string encodedValue,
            ConfigurationValueCodec[] codecs,
            out string clearText)
        {
            string current = encodedValue;
            for (int index = codecs.Length - 1; index >= 0; index--)
            {
                if (!codecs[index].TryDecode(current, out string next))
                {
                    clearText = encodedValue;
                    return false;
                }

                current = next;
            }

            clearText = current;
            return true;
        }
    }
}
