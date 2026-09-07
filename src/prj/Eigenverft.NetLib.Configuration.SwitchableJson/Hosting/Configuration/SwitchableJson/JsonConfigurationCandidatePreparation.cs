using System;
using System.Collections.Generic;
using System.Linq;

using Eigenverft.NetLib.Infrastructure.Hosting.Configuration.Values;

namespace Eigenverft.NetLib.Infrastructure.Hosting.Configuration.SwitchableJson
{
    /// <summary>
    /// Represents one reusable candidate-preparation bundle that can be assigned to switchable JSON registrations.
    /// </summary>
    public sealed class JsonConfigurationCandidatePreparation : IJsonConfigurationSourcePreparation
    {
        private readonly IJsonConfigurationSourcePreparation _inner;

        internal JsonConfigurationCandidatePreparation(
            string name,
            IJsonConfigurationSourcePreparation inner)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(name);
            ArgumentNullException.ThrowIfNull(inner);

            Name = name;
            _inner = inner;
        }

        /// <summary>Gets the descriptive name of this reusable candidate-preparation bundle.</summary>
        public string Name { get; }

        /// <inheritdoc />
        public void Prepare(JsonConfigurationSourcePreparationContext context)
        {
            _inner.Prepare(context);
        }

        /// <inheritdoc />
        public override string ToString()
        {
            return Name;
        }
    }

    /// <summary>Creates reusable candidate-preparation bundles for switchable JSON sources.</summary>
    public static class JsonConfigurationCandidatePreparations
    {
        /// <summary>Decodes values wrapped with the Base64 configuration-value codec.</summary>
        public static JsonConfigurationCandidatePreparation Base64 { get; } =
            Decode(ConfigurationValueCodecs.Base64);

        /// <summary>Decodes values wrapped with the JSON-safe Base92 configuration-value codec.</summary>
        public static JsonConfigurationCandidatePreparation Base92JsonSafe { get; } =
            Decode(ConfigurationValueCodecs.Base92JsonSafe);

        /// <summary>Decodes values wrapped with the ROT13 configuration-value codec.</summary>
        public static JsonConfigurationCandidatePreparation Rot13 { get; } =
            Decode(ConfigurationValueCodecs.Rot13);

        /// <summary>Creates a candidate preparation for a Caesar configuration-value codec.</summary>
        public static JsonConfigurationCandidatePreparation Caesar(int shift)
        {
            return Decode(ConfigurationValueCodecs.Caesar(shift));
        }

        /// <summary>Decodes Windows DPAPI LocalMachine values with Base64 payload.</summary>
        public static JsonConfigurationCandidatePreparation DpapiMachine { get; } =
            Decode(ConfigurationValueCodecs.DpapiMachine);

        /// <summary>Decodes Windows DPAPI LocalMachine values with Base64Url payload.</summary>
        public static JsonConfigurationCandidatePreparation DpapiMachineBase64Url { get; } =
            Decode(ConfigurationValueCodecs.DpapiMachineBase64Url);

        /// <summary>Creates a candidate preparation for password-derived AES values.</summary>
        public static JsonConfigurationCandidatePreparation AesPassword(string password)
        {
            return Decode(ConfigurationValueCodecs.AesPassword(password));
        }

        /// <summary>Creates a candidate preparation for password-derived AES values from visible ASCII bytes.</summary>
        public static JsonConfigurationCandidatePreparation AesPassword(byte[] passwordAsciiBytes)
        {
            return Decode(ConfigurationValueCodecs.AesPassword(passwordAsciiBytes));
        }

        /// <summary>Creates a candidate preparation for physical-machine-bound AES values.</summary>
        public static JsonConfigurationCandidatePreparation PhysicalMachineBoundAes()
        {
            return Decode(ConfigurationValueCodecs.PhysicalMachineBoundAes());
        }

        /// <summary>Adapts one persisted configuration-value codec to isolated candidate preparation.</summary>
        public static JsonConfigurationCandidatePreparation Decode(ConfigurationValueCodec codec)
        {
            ArgumentNullException.ThrowIfNull(codec);
            return new JsonConfigurationCandidatePreparation(
                $"Decode({codec.Name})",
                new CodecPreparation(codec));
        }

        /// <summary>Wraps one custom low-level preparation in the reusable application-facing candidate-preparation type.</summary>
        public static JsonConfigurationCandidatePreparation From(
            string name,
            IJsonConfigurationSourcePreparation preparation)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(name);
            ArgumentNullException.ThrowIfNull(preparation);

            return preparation is JsonConfigurationCandidatePreparation candidate
                ? candidate
                : new JsonConfigurationCandidatePreparation(name, preparation);
        }

        /// <summary>Composes candidate preparations into one reusable bundle. Steps execute in declaration order.</summary>
        public static JsonConfigurationCandidatePreparation Compose(
            params IJsonConfigurationSourcePreparation[] preparations)
        {
            ArgumentNullException.ThrowIfNull(preparations);
            if (preparations.Length == 0)
            {
                throw new ArgumentException(
                    "At least one candidate preparation is required.",
                    nameof(preparations));
            }

            var steps = new IJsonConfigurationSourcePreparation[preparations.Length];
            for (int index = 0; index < preparations.Length; index++)
            {
                steps[index] = preparations[index] ??
                    throw new ArgumentException(
                        $"Candidate preparation at index {index} is null.",
                        nameof(preparations));
            }

            string name = string.Join(
                " -> ",
                steps.Select(step => step is JsonConfigurationCandidatePreparation candidate
                    ? candidate.Name
                    : step.GetType().Name));

            return new JsonConfigurationCandidatePreparation(
                name,
                new CompositePreparation(steps));
        }

        private sealed class CodecPreparation : IJsonConfigurationSourcePreparation
        {
            private readonly ConfigurationValueCodec _codec;

            public CodecPreparation(ConfigurationValueCodec codec)
            {
                _codec = codec;
            }

            public void Prepare(JsonConfigurationSourcePreparationContext context)
            {
                ArgumentNullException.ThrowIfNull(context);
                foreach (string key in context.Values.Keys.ToArray())
                {
                    string? value = context.Values[key];
                    if (value is not null && _codec.TryDecode(value, out string clearText))
                    {
                        context.Values[key] = clearText;
                    }
                }
            }
        }

        private sealed class CompositePreparation : IJsonConfigurationSourcePreparation
        {
            private readonly IReadOnlyList<IJsonConfigurationSourcePreparation> _steps;

            public CompositePreparation(IReadOnlyList<IJsonConfigurationSourcePreparation> steps)
            {
                _steps = steps;
            }

            public void Prepare(JsonConfigurationSourcePreparationContext context)
            {
                ArgumentNullException.ThrowIfNull(context);
                JsonConfigurationSourcePreparationPipeline.Apply(
                    context.SourcePath,
                    context.Values,
                    _steps);
            }
        }
    }
}
