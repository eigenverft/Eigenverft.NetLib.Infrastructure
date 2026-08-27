using System.Net;

using Eigenverft.NetLib.Infrastructure.Networking;

namespace Eigenverft.NetLib.Infrastructure.Tests.Networking
{
    [TestClass]
    public sealed class IpAddressExtensionsTests
    {
        [TestMethod]
        public void IPv4AddressRemainsIPv4()
        {
            IPAddress address = IPAddress.Parse("192.168.1.25");

            IPAddress normalized = address.Normalize();

            Assert.AreEqual("192.168.1.25", normalized.ToString());
            Assert.AreEqual("192.168.1.25", address.ToCanonicalString());
        }

        [TestMethod]
        public void IPv4MappedIPv6NormalizesToIPv4()
        {
            IPAddress address = IPAddress.Parse("::ffff:192.168.1.25");

            IPAddress normalized = address.Normalize();

            Assert.AreEqual("192.168.1.25", normalized.ToString());
            Assert.AreEqual("192.168.1.25", address.ToCanonicalString());
        }

        [TestMethod]
        public void IPv6CanonicalStringDropsScopeIdentifier()
        {
            IPAddress address = IPAddress.Parse("fe80::1%7");

            IPAddress normalized = address.Normalize();

            Assert.AreEqual(0L, normalized.ScopeId);
            Assert.AreEqual("fe80::1", address.ToCanonicalString());
        }
    }
}
