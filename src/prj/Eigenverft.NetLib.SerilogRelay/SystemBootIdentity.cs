using System;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace Eigenverft.NetLib.SerilogRelay
{
    /// <summary>
    /// Reads a platform boot-session identifier without depending on wall-clock or file timestamps.
    /// Unsupported or unavailable platform information prevents cleanup, not logging.
    /// </summary>
    [ExcludeFromCodeCoverage]
    internal static class SystemBootIdentity
    {
        internal static Guid? Get() => TryGet(out Guid bootId) ? bootId : null;

        internal static bool TryGet(out Guid bootId)
        {
            bootId = Guid.Empty;
            try
            {
                if (OperatingSystem.IsWindows())
                {
                    // SystemBootEnvironmentInformation. The native query is optional:
                    // if its layout or availability changes, retain obsolete generations.
                    if (NtQuerySystemInformation(90, out WindowsBootEnvironment information,
                        Marshal.SizeOf<WindowsBootEnvironment>(), out int length) != 0
                        || length != Marshal.SizeOf<WindowsBootEnvironment>())
                        return false;
                    bootId = information.BootIdentifier;
                }
                else if (OperatingSystem.IsLinux())
                {
                    if (!Guid.TryParseExact(File.ReadAllText("/proc/sys/kernel/random/boot_id").Trim(), "D", out bootId))
                        return false;
                }
                else if (OperatingSystem.IsMacOS())
                {
                    var buffer = new byte[37];
                    nuint length = (nuint)buffer.Length;
                    if (sysctlbyname("kern.bootsessionuuid", buffer, ref length, IntPtr.Zero, 0) != 0
                        || length > (nuint)buffer.Length
                        || !Guid.TryParseExact(Encoding.UTF8.GetString(buffer, 0, (int)length).TrimEnd('\0'), "D", out bootId))
                        return false;
                }

                return bootId != Guid.Empty;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException
                or DllNotFoundException or EntryPointNotFoundException)
            {
                bootId = Guid.Empty;
                return false;
            }
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct WindowsBootEnvironment
        {
            internal Guid BootIdentifier;
            internal uint FirmwareType;
            internal ulong BootFlags;
        }

        [DllImport("ntdll.dll", ExactSpelling = true)]
        private static extern int NtQuerySystemInformation(int informationClass,
            out WindowsBootEnvironment information, int informationLength, out int returnLength);

        [DllImport("/usr/lib/libSystem.B.dylib", ExactSpelling = true)]
        [SuppressMessage("Interoperability", "CA2101:Specify marshaling for P/Invoke string arguments",
            Justification = "sysctlbyname requires a UTF-8 name, explicitly marshalled as LPUTF8Str.")]
        private static extern int sysctlbyname(
            [MarshalAs(UnmanagedType.LPUTF8Str)] string name,
            [Out] byte[] value, ref nuint length, IntPtr newValue, nuint newLength);
    }
}
