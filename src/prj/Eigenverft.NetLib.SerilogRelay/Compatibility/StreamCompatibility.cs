#if NETSTANDARD2_1
using System.IO;

namespace Eigenverft.NetLib.SerilogRelay
{
    internal static class StreamCompatibility
    {
        internal static void ReadExactly(this Stream stream, byte[] buffer)
        {
            int offset = 0;
            while (offset < buffer.Length)
            {
                int read = stream.Read(buffer, offset, buffer.Length - offset);
                if (read == 0)
                    throw new EndOfStreamException();
                offset += read;
            }
        }
    }
}
#endif
