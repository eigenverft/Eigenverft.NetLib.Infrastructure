using System;
using System.Diagnostics;
using System.Security.Cryptography;

namespace Eigenverft.NetLib.SerilogRelay
{
    internal static class RelayRuntime
    {
        internal static int ProcessId
        {
            get
            {
#if NETSTANDARD2_1
                using Process process = Process.GetCurrentProcess();
                return process.Id;
#else
                return Environment.ProcessId;
#endif
            }
        }

        internal static string Sha256Hex(byte[] value)
        {
#if NETSTANDARD2_1
            using SHA256 algorithm = SHA256.Create();
            return BitConverter.ToString(algorithm.ComputeHash(value)).Replace("-", string.Empty);
#else
            return Convert.ToHexString(SHA256.HashData(value));
#endif
        }

        internal static TimeSpan ElapsedSince(long timestamp)
        {
#if NETSTANDARD2_1
            return TimeSpan.FromTicks((long)((Stopwatch.GetTimestamp() - timestamp)
                * ((double)TimeSpan.TicksPerSecond / Stopwatch.Frequency)));
#else
            return Stopwatch.GetElapsedTime(timestamp);
#endif
        }

        internal static double NextRandomDouble()
        {
#if NETSTANDARD2_1
            return RandomNumberGenerator.GetInt32(int.MaxValue) / (double)int.MaxValue;
#else
            return Random.Shared.NextDouble();
#endif
        }
    }
}
