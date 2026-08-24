using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;

using Eigenverft.NetLib.Infrastructure.Hosting.Configuration.SwitchableJson;

using Microsoft.Extensions.Configuration;

namespace Eigenverft.NetLib.Infrastructure.Hosting.Configuration.Values
{
    /// <summary>
    /// Provides an explicit local recovery view over clear-text runtime values that were selected by NetLib JSON value protection.
    /// </summary>
    /// <remarks>
    /// Recovery does not decode backing files, bypass a protection boundary, or introduce a second cryptographic path. It reads
    /// values that a NetLib switchable JSON provider has already decoded and published into its active runtime configuration
    /// snapshot, then limits the result to keys selected by that provider's registered
    /// <see cref="JsonConfigurationValueProtection"/> policy.
    /// </remarks>
    public static class ConfigurationValueRecovery
    {
        /// <summary>
        /// Returns the currently published clear-text values for keys selected by registered NetLib JSON value-protection rules.
        /// </summary>
        /// <param name="configuration">
        /// The active configuration root or <see cref="ConfigurationManager"/> whose provider chain contains NetLib switchable
        /// JSON sources configured with <see cref="JsonConfigurationValueProtection"/>.
        /// </param>
        /// <returns>
        /// A read-only dictionary keyed by full colon-separated configuration path. Only values from NetLib switchable JSON
        /// providers with registered value protection are included. When more than one such provider contains the same key,
        /// later provider precedence wins, matching normal <see cref="IConfiguration"/> resolution.
        /// </returns>
        /// <remarks>
        /// This helper is intentionally intended for short-lived local recovery or debugging, for example when a developer needs
        /// to inspect a protected value that is already available to the running process but is no longer known in clear text.
        /// The returned values may contain secrets. Do not log, persist, transmit, or otherwise retain the returned clear text
        /// unless that is explicitly intended. The experimental diagnostic is deliberately strong so temporary recovery calls
        /// remain visible and are removed when the recovery session is finished.
        /// </remarks>
        /// <exception cref="ArgumentNullException"><paramref name="configuration"/> is <see langword="null"/>.</exception>
        /// <exception cref="ArgumentException">
        /// <paramref name="configuration"/> is not an <see cref="IConfigurationRoot"/> and therefore does not expose its provider
        /// chain for recovery inspection.
        /// </exception>
        [Experimental("EVFRECOVERY001")]
        public static IReadOnlyDictionary<string, string?> RecoverProtectedValues(IConfiguration configuration)
        {
            ArgumentNullException.ThrowIfNull(configuration);

            if (configuration is not IConfigurationRoot root)
            {
                throw new ArgumentException(
                    "Configuration value recovery requires an IConfigurationRoot or ConfigurationManager so the active provider chain can be inspected.",
                    nameof(configuration));
            }

            var recovered = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
            foreach (IConfigurationProvider provider in root.Providers)
            {
                if (provider is SwitchableJsonConfigurationProvider switchableProvider)
                {
                    switchableProvider.AppendRecoverableValues(recovered);
                }
            }

            return recovered;
        }
    }
}
