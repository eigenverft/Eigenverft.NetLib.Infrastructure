using System.Net;

using Eigenverft.NetLib.Infrastructure.Networking;

namespace Eigenverft.NetLib.Infrastructure.Tests.Networking
{
    [TestClass]
    [DoNotParallelize]
    public sealed class CidrMatchingExtensionsTests
    {
        [TestInitialize]
        public void ResetCache()
        {
            CidrMatchingExtensions.ResetCacheForTests();
        }

        [TestMethod]
        public void MatchAllWildcardShortCircuits()
        {
            bool match = IPAddress.Parse("203.0.113.10")
                .Matches(new string?[] { "", "  *  ", "invalid" });

            Assert.IsTrue(match);
            CidrMatchingCacheStatistics statistics = CidrMatchingExtensions.GetCacheStatistics();
            Assert.AreEqual(0L, statistics.ParsedNetworkCacheMisses);
            Assert.AreEqual(0L, statistics.ListMatchCacheMisses);
        }

        [TestMethod]
        public void InvalidEntriesAreIgnoredAndCached()
        {
            IPAddress address = IPAddress.Parse("192.168.1.42");

            Assert.IsFalse(address.Matches("not-a-cidr"));
            Assert.IsFalse(address.Matches("not-a-cidr"));

            CidrMatchingCacheStatistics statistics = CidrMatchingExtensions.GetCacheStatistics();
            Assert.AreEqual(1L, statistics.ParsedNetworkCacheMisses);
            Assert.AreEqual(1L, statistics.ParsedNetworkCacheHits);
        }

        [TestMethod]
        public void ParsedNetworkCacheIsReusedForRepeatedSingleMatch()
        {
            IPAddress address = IPAddress.Parse("192.168.1.42");

            Assert.IsTrue(address.Matches("192.168.1.123/24"));
            Assert.IsTrue(address.Matches("192.168.1.123/24"));

            CidrMatchingCacheStatistics statistics = CidrMatchingExtensions.GetCacheStatistics();
            Assert.AreEqual(1L, statistics.ParsedNetworkCacheMisses);
            Assert.AreEqual(1L, statistics.ParsedNetworkCacheHits);
        }

        [TestMethod]
        public void RepeatedListMatchUsesOrderIndependentCacheKey()
        {
            IPAddress address = IPAddress.Parse("10.20.30.40");

            Assert.IsTrue(address.Matches(new string?[] { "192.168.0.0/16", "10.0.0.0/8" }));
            Assert.IsTrue(address.Matches(new string?[] { "10.0.0.0/8", "192.168.0.0/16" }));

            CidrMatchingCacheStatistics statistics = CidrMatchingExtensions.GetCacheStatistics();
            Assert.AreEqual(1L, statistics.ListMatchCacheMisses);
            Assert.AreEqual(1L, statistics.ListMatchCacheHits);
        }

        [TestMethod]
        public void IPv4MappedAddressSharesIPv4MatchingSemantics()
        {
            bool match = IPAddress.Parse("::ffff:192.168.1.42")
                .Matches(new string?[] { "192.168.1.123/24" });

            Assert.IsTrue(match);
        }

        [TestMethod]
        public void IPv6ListMatchingWorks()
        {
            bool match = IPAddress.Parse("2001:db8:abcd:12::42")
                .Matches(new string?[] { "2001:db8:abcd:12::1234/64" });

            Assert.IsTrue(match);
        }
    }
}
