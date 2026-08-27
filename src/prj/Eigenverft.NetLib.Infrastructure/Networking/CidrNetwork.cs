using System;
using System.Globalization;
using System.Net;
using System.Net.Sockets;

namespace Eigenverft.NetLib.Infrastructure.Networking
{
    /// <summary>
    /// Represents a normalized IPv4 or IPv6 CIDR network.
    /// </summary>
    public readonly struct CidrNetwork : IEquatable<CidrNetwork>
    {
        private CidrNetwork(IPAddress networkAddress, int prefixLength)
        {
            NetworkAddress = networkAddress;
            PrefixLength = prefixLength;
        }

        /// <summary>
        /// Gets the normalized network address with all host bits cleared.
        /// </summary>
        public IPAddress NetworkAddress { get; }

        /// <summary>
        /// Gets the CIDR prefix length.
        /// </summary>
        public int PrefixLength { get; }

        /// <summary>
        /// Gets the address family of this network.
        /// </summary>
        public AddressFamily AddressFamily => NetworkAddress.AddressFamily;

        /// <summary>
        /// Parses a CIDR network and normalizes host bits in the supplied base address.
        /// </summary>
        /// <remarks>
        /// Convenience input such as <c>192.168.1.123/24</c> is accepted and normalized
        /// to <c>192.168.1.0/24</c> rather than rejected for containing host bits.
        /// </remarks>
        /// <param name="value">CIDR text.</param>
        /// <returns>The normalized network.</returns>
        /// <exception cref="FormatException">The CIDR text is invalid.</exception>
        public static CidrNetwork Parse(string value)
        {
            if (!TryParse(value, out CidrNetwork network))
            {
                throw new FormatException($"'{value}' is not a valid IPv4 or IPv6 CIDR network.");
            }

            return network;
        }

        /// <summary>
        /// Attempts to parse a CIDR network and normalize host bits in the supplied base address.
        /// </summary>
        /// <param name="value">CIDR text.</param>
        /// <param name="network">Normalized network when parsing succeeds.</param>
        /// <returns><see langword="true"/> when parsing succeeds; otherwise <see langword="false"/>.</returns>
        public static bool TryParse(string? value, out CidrNetwork network)
        {
            network = default;

            if (string.IsNullOrWhiteSpace(value))
            {
                return false;
            }

            ReadOnlySpan<char> span = value.AsSpan().Trim();
            int slashIndex = span.IndexOf('/');
            if (slashIndex <= 0 || slashIndex != span.LastIndexOf('/'))
            {
                return false;
            }

            string addressText = span[..slashIndex].Trim().ToString();
            string prefixText = span[(slashIndex + 1)..].Trim().ToString();

            if (!IpAddressNormalizer.TryParse(addressText, out IPAddress? address) || address is null)
            {
                return false;
            }

            if (!int.TryParse(prefixText, NumberStyles.None, CultureInfo.InvariantCulture, out int prefixLength))
            {
                return false;
            }

            int bitLength = address.AddressFamily switch
            {
                AddressFamily.InterNetwork => 32,
                AddressFamily.InterNetworkV6 => 128,
                _ => 0,
            };

            if (bitLength == 0 || prefixLength < 0 || prefixLength > bitLength)
            {
                return false;
            }

            byte[] bytes = address.GetAddressBytes();
            ApplyMask(bytes, prefixLength);
            network = new CidrNetwork(new IPAddress(bytes), prefixLength);
            return true;
        }

        /// <summary>
        /// Determines whether an address belongs to this network.
        /// </summary>
        /// <param name="address">Address to test.</param>
        /// <returns><see langword="true"/> when the address belongs to the network; otherwise <see langword="false"/>.</returns>
        public bool Contains(IPAddress address)
        {
            ArgumentNullException.ThrowIfNull(address);

            IPAddress normalized = IpAddressNormalizer.Normalize(address);
            if (normalized.AddressFamily != AddressFamily)
            {
                return false;
            }

            byte[] networkBytes = NetworkAddress.GetAddressBytes();
            byte[] addressBytes = normalized.GetAddressBytes();

            int wholeBytes = PrefixLength / 8;
            int remainingBits = PrefixLength % 8;

            for (int i = 0; i < wholeBytes; i++)
            {
                if (networkBytes[i] != addressBytes[i])
                {
                    return false;
                }
            }

            if (remainingBits == 0)
            {
                return true;
            }

            byte mask = (byte)(0xFF << (8 - remainingBits));
            return (networkBytes[wholeBytes] & mask) == (addressBytes[wholeBytes] & mask);
        }

        /// <inheritdoc />
        public bool Equals(CidrNetwork other)
        {
            return PrefixLength == other.PrefixLength && Equals(NetworkAddress, other.NetworkAddress);
        }

        /// <inheritdoc />
        public override bool Equals(object? obj)
        {
            return obj is CidrNetwork other && Equals(other);
        }

        /// <inheritdoc />
        public override int GetHashCode()
        {
            return HashCode.Combine(NetworkAddress, PrefixLength);
        }

        /// <summary>
        /// Compares two CIDR networks for equality.
        /// </summary>
        public static bool operator ==(CidrNetwork left, CidrNetwork right) => left.Equals(right);

        /// <summary>
        /// Compares two CIDR networks for inequality.
        /// </summary>
        public static bool operator !=(CidrNetwork left, CidrNetwork right) => !left.Equals(right);

        /// <inheritdoc />
        public override string ToString()
        {
            return $"{IpAddressNormalizer.ToCanonicalString(NetworkAddress)}/{PrefixLength.ToString(CultureInfo.InvariantCulture)}";
        }

        private static void ApplyMask(byte[] bytes, int prefixLength)
        {
            int wholeBytes = prefixLength / 8;
            int remainingBits = prefixLength % 8;

            if (remainingBits != 0)
            {
                bytes[wholeBytes] &= (byte)(0xFF << (8 - remainingBits));
                wholeBytes++;
            }

            for (int i = wholeBytes; i < bytes.Length; i++)
            {
                bytes[i] = 0;
            }
        }
    }
}
