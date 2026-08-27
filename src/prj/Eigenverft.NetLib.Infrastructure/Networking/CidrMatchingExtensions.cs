using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading;

using Microsoft.Extensions.Caching.Memory;

namespace Eigenverft.NetLib.Infrastructure.Networking
{
    /// <summary>
    /// Provides CIDR matching extensions for <see cref="IPAddress"/> while caching parsed networks and repeated list evaluations.
    /// </summary>
    public static class CidrMatchingExtensions
    {
        private const int CacheSizeLimit = 10_000;
        private static readonly TimeSpan CacheLifetime = TimeSpan.FromMinutes(5);
        private static readonly object CacheSync = new();

        private static MemoryCache _cache = CreateCache();
        private static long _parsedNetworkCacheHits;
        private static long _parsedNetworkCacheMisses;
        private static long _listMatchCacheHits;
        private static long _listMatchCacheMisses;

        /// <summary>
        /// Determines whether an address belongs to one CIDR network.
        /// </summary>
        /// <remarks>
        /// A single <c>*</c> value matches every address. Invalid or empty CIDR text does not match.
        /// Parsed CIDR results, including invalid results, are cached for repeated calls.
        /// </remarks>
        /// <param name="address">Address to test.</param>
        /// <param name="cidr">CIDR text or <c>*</c>.</param>
        /// <returns><see langword="true"/> when the address matches; otherwise <see langword="false"/>.</returns>
        public static bool Matches(this IPAddress address, string? cidr)
        {
            ArgumentNullException.ThrowIfNull(address);

            string normalizedText = (cidr ?? string.Empty).Trim();
            if (normalizedText.Length == 0)
            {
                return false;
            }

            if (string.Equals(normalizedText, "*", StringComparison.Ordinal))
            {
                return true;
            }

            return TryGetCachedNetwork(normalizedText, out CidrNetwork network)
                && network.Contains(address);
        }

        /// <summary>
        /// Determines whether an address belongs to any CIDR network in a collection.
        /// </summary>
        /// <remarks>
        /// Empty entries are ignored and <c>*</c> short-circuits as match-all. The repeated
        /// address/list result is cached using an order-independent list key, preserving the
        /// historical convenience for recurring filter evaluations.
        /// </remarks>
        /// <param name="address">Address to test.</param>
        /// <param name="cidrs">CIDR texts to test.</param>
        /// <returns><see langword="true"/> when any entry matches; otherwise <see langword="false"/>.</returns>
        public static bool Matches(this IPAddress address, IEnumerable<string?>? cidrs)
        {
            ArgumentNullException.ThrowIfNull(address);

            if (cidrs is null)
            {
                return false;
            }

            string[] entries = cidrs
                .Select(static value => (value ?? string.Empty).Trim())
                .Where(static value => value.Length != 0)
                .ToArray();

            if (entries.Length == 0)
            {
                return false;
            }

            if (entries.Any(static value => string.Equals(value, "*", StringComparison.Ordinal)))
            {
                return true;
            }

            IPAddress normalizedAddress = address.Normalize();
            string listKey = BuildListCacheKey(normalizedAddress, entries);
            MemoryCache cache = Volatile.Read(ref _cache);

            if (cache.TryGetValue(listKey, out bool cached))
            {
                Interlocked.Increment(ref _listMatchCacheHits);
                return cached;
            }

            Interlocked.Increment(ref _listMatchCacheMisses);

            bool result = false;
            for (int i = 0; i < entries.Length; i++)
            {
                if (TryGetCachedNetwork(entries[i], out CidrNetwork network)
                    && network.Contains(normalizedAddress))
                {
                    result = true;
                    break;
                }
            }

            cache.Set(
                listKey,
                result,
                new MemoryCacheEntryOptions
                {
                    AbsoluteExpirationRelativeToNow = CacheLifetime,
                    Size = 1,
                });

            return result;
        }

        internal static CidrMatchingCacheStatistics GetCacheStatistics()
        {
            return new CidrMatchingCacheStatistics(
                Interlocked.Read(ref _parsedNetworkCacheHits),
                Interlocked.Read(ref _parsedNetworkCacheMisses),
                Interlocked.Read(ref _listMatchCacheHits),
                Interlocked.Read(ref _listMatchCacheMisses));
        }

        internal static void ResetCacheForTests()
        {
            lock (CacheSync)
            {
                MemoryCache replacement = CreateCache();
                MemoryCache old = Interlocked.Exchange(ref _cache, replacement);
                old.Dispose();

                Interlocked.Exchange(ref _parsedNetworkCacheHits, 0);
                Interlocked.Exchange(ref _parsedNetworkCacheMisses, 0);
                Interlocked.Exchange(ref _listMatchCacheHits, 0);
                Interlocked.Exchange(ref _listMatchCacheMisses, 0);
            }
        }

        private static bool TryGetCachedNetwork(string cidr, out CidrNetwork network)
        {
            string key = $"Cidr:Range:{cidr}";
            MemoryCache cache = Volatile.Read(ref _cache);

            if (cache.TryGetValue(key, out ParsedNetworkCacheEntry cached))
            {
                Interlocked.Increment(ref _parsedNetworkCacheHits);
                network = cached.Network;
                return cached.IsValid;
            }

            Interlocked.Increment(ref _parsedNetworkCacheMisses);

            bool isValid = CidrNetwork.TryParse(cidr, out network);
            cache.Set(
                key,
                new ParsedNetworkCacheEntry(isValid, network),
                new MemoryCacheEntryOptions
                {
                    AbsoluteExpirationRelativeToNow = CacheLifetime,
                    Size = 1,
                });

            return isValid;
        }

        private static string BuildListCacheKey(IPAddress address, string[] entries)
        {
            string[] sorted = entries.OrderBy(static value => value, StringComparer.Ordinal).ToArray();
            var builder = new StringBuilder("Cidr:IsInList:");
            builder.Append(address.ToCanonicalString());
            builder.Append('|');

            for (int i = 0; i < sorted.Length; i++)
            {
                builder.Append(sorted[i].Length);
                builder.Append(':');
                builder.Append(sorted[i]);
                builder.Append(';');
            }

            return builder.ToString();
        }

        private static MemoryCache CreateCache()
        {
            return new MemoryCache(new MemoryCacheOptions { SizeLimit = CacheSizeLimit });
        }

        private readonly record struct ParsedNetworkCacheEntry(bool IsValid, CidrNetwork Network);
    }

    internal readonly record struct CidrMatchingCacheStatistics(
        long ParsedNetworkCacheHits,
        long ParsedNetworkCacheMisses,
        long ListMatchCacheHits,
        long ListMatchCacheMisses);
}
