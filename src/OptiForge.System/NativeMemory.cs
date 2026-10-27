using System.Runtime.InteropServices;

namespace OptiForge.SysMon;

internal static class NativeMemory
{
    [StructLayout(LayoutKind.Sequential)]
    private struct MemoryStatusEx
    {
        public uint dwLength;
        public uint dwMemoryLoad;
        public ulong ullTotalPhys;
        public ulong ullAvailPhys;
        public ulong ullTotalPageFile;
        public ulong ullAvailPageFile;
        public ulong ullTotalVirtual;
        public ulong ullAvailVirtual;
        public ulong ullAvailExtendedVirtual;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GlobalMemoryStatusEx(ref MemoryStatusEx lpBuffer);

    public static bool TryGet(out double usedPercent, out double usedGb, out double totalGb)
    {
        usedPercent = usedGb = totalGb = 0;
        try
        {
            var status = new MemoryStatusEx
            {
                dwLength = (uint)Marshal.SizeOf<MemoryStatusEx>()
            };

            if (!GlobalMemoryStatusEx(ref status) || status.ullTotalPhys == 0)
                return false;

            totalGb = status.ullTotalPhys / (1024.0 * 1024 * 1024);
            var availGb = status.ullAvailPhys / (1024.0 * 1024 * 1024);
            usedGb = Math.Max(0, totalGb - availGb);
            usedPercent = status.dwMemoryLoad;
            return true;
        }
        catch
        {
            return false;
        }
    }
}
