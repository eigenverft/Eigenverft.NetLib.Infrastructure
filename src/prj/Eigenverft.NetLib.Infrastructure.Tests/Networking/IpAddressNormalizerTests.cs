using System.Net;

using Eigenverft.NetLib.Infrastructure.Networking;

namespace Eigenverft.NetLib.Infrastructure.Tests.Networking
{
    [TestClass]
    public sealed class IpAddressNormalizerTests
    {
        [TestMethod]
        public void IPv4AddressRemainsIPv4()
        {
            IPAddress address = IPAddress.Parse("192.168.1.25");

            IPAddress normalized = IpAddressNormalizer.Normalize(address);

            Assert.AreEqual("192.168.1.25", normalized.ToString());
            Assert.AreEqual("192.168.1.25", IpAddressNormalizer.ToCanonicalString(address));
        }

        [TestMethod]
        public void IPv4MappedIPv6NormalizesToIPv4()
        {
            IPAddress address = IPAddress.Parse("::ffff:192.168.1.25");

            IPAddress normalized = IpAddressNormalizer.Normalize(address);

            Assert.AreEqual("192.168.1.25", normalized.ToString());
            Assert.AreEqual("192.168.1.25", IpAddressNormalizer.ToCanonicalString(address));
        }

        [TestMethod]
        public void IPv6CanonicalStringDropsScopeIdentifier()
        {
            IPAddress address = IPAddress.Parse("fe80::1%7");

            IPAddress normalized = IpAddressNormalizer.Normalize(address);

            Assert.AreEqual(0L, normalized.ScopeId);
            Assert.AreEqual("fe80::1", IpAddressNormalizer.ToCanonicalString(address));
        }

        [TestMethod]
        public void TryParseNormalizesMappedInput()
        {
            bool parsed = IpAddressNormalizer.TryParse("  ::ffff:10.20.30.40  ", out IPAddress? address);

            Assert.IsTrue(parsed);
            Assert.IsNotNull(address);
            Assert.AreEqual("10.20.30.40", address.ToString());
        }
    }
}
