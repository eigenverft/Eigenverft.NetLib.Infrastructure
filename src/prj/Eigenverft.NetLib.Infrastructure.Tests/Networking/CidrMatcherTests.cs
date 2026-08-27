using System.Net;

using Eigenverft.NetLib.Infrastructure.Networking;

namespace Eigenverft.NetLib.Infrastructure.Tests.Networking
{
    [TestClass]
    [DoNotParallelize]
    public sealed class CidrMatcherTests
    {
        [TestInitialize]
        public void ResetCache()
        {
            CidrMatcher.ResetCacheForTests();
        }

        [TestMethod]
        public void MatchAllWildcardShortCircuits()
        {
            bool match = CidrMatcher.IsMatch(
                IPAddress.Parse("203.0.113.10"),
                new string?[] { "", "  *  ", "invalid" });

            Assert.IsTrue(match);
            CidrMatcherCacheStatistics statistics = CidrMatcher.GetCacheStatistics();
            Assert.AreEqual(0L, statistics.ParsedNetworkCacheMisses);
            Assert.AreEqual(0L, statistics.ListMatchCacheMisses);
        }

        [TestMethod]
        public void InvalidEntriesAreIgnoredAndCached()
        {
            IPAddress address = IPAddress.Parse("192.168.1.42");

            Assert.IsFalse(CidrMatcher.IsMatch(address, "not-a-cidr"));
            Assert.IsFalse(CidrMatcher.IsMatch(address, "not-a-cidr"));

            CidrMatcherCacheStatistics statistics = CidrMatcher.GetCacheStatistics();
            Assert.AreEqual(1L, statistics.ParsedNetworkCacheMisses);
            Assert.AreEqual(1L, statistics.ParsedNetworkCacheHits);
        }

        [TestMethod]
        public void ParsedNetworkCacheIsReusedForRepeatedSingleMatch()
        {
            IPAddress address = IPAddress.Parse("192.168.1.42");

            Assert.IsTrue(CidrMatcher.IsMatch(address, "192.168.1.123/24"));
            Assert.IsTrue(CidrMatcher.IsMatch(address, "192.168.1.123/24"));

            CidrMatcherCacheStatistics statistics = CidrMatcher.GetCacheStatistics();
            Assert.AreEqual(1L, statistics.ParsedNetworkCacheMisses);
            Assert.AreEqual(1L, statistics.ParsedNetworkCacheHits);
        }

        [TestMethod]
        public void RepeatedListMatchUsesOrderIndependentCacheKey()
        {
            IPAddress address = IPAddress.Parse("10.20.30.40");

            Assert.IsTrue(CidrMatcher.IsMatch(address, new string?[] { "192.168.0.0/16", "10.0.0.0/8" }));
            Assert.IsTrue(CidrMatcher.IsMatch(address, new string?[] { "10.0.0.0/8", "192.168.0.0/16" }));

            CidrMatcherCacheStatistics statistics = CidrMatcher.GetCacheStatistics();
            Assert.AreEqual(1L, statistics.ListMatchCacheMisses);
            Assert.AreEqual(1L, statistics.ListMatchCacheHits);
        }

        [TestMethod]
        public void IPv4MappedAddressSharesIPv4MatchingSemantics()
        {
            bool match = CidrMatcher.IsMatch(
                IPAddress.Parse("::ffff:192.168.1.42"),
                new string?[] { "192.168.1.123/24" });

            Assert.IsTrue(match);
        }

        [TestMethod]
        public void IPv6ListMatchingWorks()
        {
            bool match = CidrMatcher.IsMatch(
                IPAddress.Parse("2001:db8:abcd:12::42"),
                new string?[] { "2001:db8:abcd:12::1234/64" });

            Assert.IsTrue(match);
        }
    }
}
