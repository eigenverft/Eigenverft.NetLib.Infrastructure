using System;
using System.Net;
using System.Net.Sockets;

namespace Eigenverft.NetLib.Infrastructure.Networking
{
    /// <summary>
    /// Provides a small, host-agnostic contract for normalizing IP addresses.
    /// </summary>
    public static class IpAddressNormalizer
    {
        /// <summary>
        /// Normalizes an IP address for comparison and stable textual representation.
        /// </summary>
        /// <remarks>
        /// IPv4-mapped IPv6 addresses are converted to IPv4. Native IPv6 addresses have
        /// their scope identifier removed because a scope is interface-local metadata and
        /// must not affect the canonical address identity used by reusable network logic.
        /// </remarks>
        /// <param name="address">Address to normalize.</param>
        /// <returns>The normalized address.</returns>
        public static IPAddress Normalize(IPAddress address)
        {
            ArgumentNullException.ThrowIfNull(address);

            if (address.AddressFamily == AddressFamily.InterNetworkV6 && address.IsIPv4MappedToIPv6)
            {
                return address.MapToIPv4();
            }

            if (address.AddressFamily == AddressFamily.InterNetwork)
            {
                return address;
            }

            if (address.AddressFamily == AddressFamily.InterNetworkV6)
            {
                return address.ScopeId == 0
                    ? address
                    : new IPAddress(address.GetAddressBytes());
            }

            throw new NotSupportedException($"Address family '{address.AddressFamily}' is not supported.");
        }

        /// <summary>
        /// Returns a stable canonical textual representation of an IP address.
        /// </summary>
        /// <param name="address">Address to format.</param>
        /// <returns>The normalized address text.</returns>
        public static string ToCanonicalString(IPAddress address)
        {
            return Normalize(address).ToString();
        }

        /// <summary>
        /// Parses and normalizes an IP address.
        /// </summary>
        /// <param name="value">Text to parse.</param>
        /// <param name="address">Normalized address when parsing succeeds.</param>
        /// <returns><see langword="true"/> when parsing succeeds; otherwise <see langword="false"/>.</returns>
        public static bool TryParse(string? value, out IPAddress? address)
        {
            address = null;

            if (string.IsNullOrWhiteSpace(value) || !IPAddress.TryParse(value.Trim(), out IPAddress? parsed))
            {
                return false;
            }

            address = Normalize(parsed);
            return true;
        }
    }
}
