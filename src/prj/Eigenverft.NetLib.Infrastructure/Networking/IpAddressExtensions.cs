using System;
using System.Net;
using System.Net.Sockets;

namespace Eigenverft.NetLib.Infrastructure.Networking
{
    /// <summary>
    /// Provides host-agnostic normalization helpers for <see cref="IPAddress"/>.
    /// </summary>
    public static class IpAddressExtensions
    {
        /// <summary>
        /// Normalizes an IP address for comparison and stable textual representation.
        /// </summary>
        /// <remarks>
        /// IPv4-mapped IPv6 addresses are converted to IPv4. IPv4 and native IPv6 addresses are returned unchanged,
        /// preserving potentially relevant native IPv6 information such as the scope identifier.
        /// </remarks>
        /// <param name="address">Address to normalize.</param>
        /// <returns>The normalized address.</returns>
        public static IPAddress Normalize(this IPAddress address)
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
                return address;
            }

            throw new NotSupportedException($"Address family '{address.AddressFamily}' is not supported.");
        }

        /// <summary>
        /// Returns a stable canonical textual representation of an IP address.
        /// </summary>
        /// <param name="address">Address to format.</param>
        /// <returns>The normalized address text.</returns>
        public static string ToCanonicalString(this IPAddress address)
        {
            return address.Normalize().ToString();
        }
    }
}
