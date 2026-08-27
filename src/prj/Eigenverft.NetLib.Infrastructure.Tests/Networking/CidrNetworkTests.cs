using System.Net;

using Eigenverft.NetLib.Infrastructure.Networking;

namespace Eigenverft.NetLib.Infrastructure.Tests.Networking
{
    [TestClass]
    public sealed class CidrNetworkTests
    {
        [TestMethod]
        public void IPv4HostBitsAreNormalizedToNetworkAddress()
        {
            CidrNetwork network = CidrNetwork.Parse("192.168.1.123/24");

            Assert.AreEqual("192.168.1.0/24", network.ToString());
            Assert.IsTrue(network.Contains(IPAddress.Parse("192.168.1.1")));
            Assert.IsTrue(network.Contains(IPAddress.Parse("192.168.1.255")));
            Assert.IsFalse(network.Contains(IPAddress.Parse("192.168.2.1")));
        }

        [TestMethod]
        public void IPv4MappedIPv6BaseAddressUsesIPv4Semantics()
        {
            CidrNetwork network = CidrNetwork.Parse("::ffff:192.168.10.123/24");

            Assert.AreEqual("192.168.10.0/24", network.ToString());
            Assert.IsTrue(network.Contains(IPAddress.Parse("::ffff:192.168.10.8")));
            Assert.IsFalse(network.Contains(IPAddress.Parse("192.168.11.8")));
        }

        [TestMethod]
        public void IPv6HostBitsAreNormalizedToNetworkAddress()
        {
            CidrNetwork network = CidrNetwork.Parse("2001:db8:abcd:12::1234/64");

            Assert.AreEqual("2001:db8:abcd:12::/64", network.ToString());
            Assert.IsTrue(network.Contains(IPAddress.Parse("2001:db8:abcd:12::ffff")));
            Assert.IsFalse(network.Contains(IPAddress.Parse("2001:db8:abcd:13::1")));
        }

        [TestMethod]
        public void AddressFamilyMismatchDoesNotMatch()
        {
            CidrNetwork network = CidrNetwork.Parse("10.0.0.0/8");

            Assert.IsFalse(network.Contains(IPAddress.Parse("2001:db8::1")));
        }

        [TestMethod]
        [DataRow("")]
        [DataRow("192.168.1.1")]
        [DataRow("192.168.1.1/33")]
        [DataRow("2001:db8::1/129")]
        [DataRow("::ffff:192.168.1.1/120")]
        public void InvalidCidrDoesNotParse(string value)
        {
            Assert.IsFalse(CidrNetwork.TryParse(value, out _));
        }
    }
}
