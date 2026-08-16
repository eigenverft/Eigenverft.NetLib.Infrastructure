using System;

namespace Eigenverft.NetLib.Infrastructure.Hosting.Configuration.SwitchableJson
{
    /// <summary>
    /// Shared registration options for one or more switchable JSON configuration sources.
    /// </summary>
    public sealed class SwitchableJsonRegistrationOptions
    {
        /// <summary>Gets or initializes whether a missing active source is treated as empty by framework-driven loads.</summary>
        public bool Optional { get; init; }

        /// <summary>Gets or initializes whether each active JSON source is watched independently for physical changes.</summary>
        public bool ReloadOnChange { get; init; }

        /// <summary>Gets or initializes the debounce delay for active-source file notifications.</summary>
        public int ReloadDelayMilliseconds { get; init; } = 250;

        /// <summary>Gets or initializes the runtime failure policy used by each registered switchable JSON source.</summary>
        public SwitchableJsonRuntimeFailurePolicy RuntimeFailurePolicy { get; init; } =
            SwitchableJsonRuntimeFailurePolicy.KeepLastKnownGood;

        /// <summary>
        /// Gets or initializes the reusable candidate preparation applied after JSON parsing and before provider state is committed.
        /// </summary>
        /// <remarks>
        /// Use <see cref="JsonConfigurationCandidatePreparations.Compose"/> when several preparation steps should behave as one bundle.
        /// </remarks>
        public JsonConfigurationCandidatePreparation? CandidatePreparation { get; init; }

        /// <summary>Gets or initializes startup protection for selected values in existing JSON files.</summary>
        /// <remarks>
        /// Matching clear-text values are encoded once before provider registration. The corresponding decoder always runs
        /// before <see cref="CandidatePreparation"/> so application-owned preparation observes clear text.
        /// </remarks>
        public JsonConfigurationValueProtection? ValueProtection { get; init; }

        internal void Validate()
        {
            if (ReloadDelayMilliseconds < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(ReloadDelayMilliseconds));
            }

            if (!Enum.IsDefined(RuntimeFailurePolicy))
            {
                throw new ArgumentOutOfRangeException(nameof(RuntimeFailurePolicy));
            }
        }
    }
}
