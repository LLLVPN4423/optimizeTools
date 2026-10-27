using System.Runtime.InteropServices;

namespace OptiForge.SysMon;

internal static class NativeCpu
{
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetSystemTimes(out long idle, out long kernel, out long user);

    private static long _prevIdle, _prevKernel, _prevUser;
    private static bool _hasPrev;

    public static double NextValue()
    {
        if (!GetSystemTimes(out var idle, out var kernel, out var user))
            return 0;

        if (!_hasPrev)
        {
            _prevIdle = idle;
            _prevKernel = kernel;
            _prevUser = user;
            _hasPrev = true;
            return 0;
        }

        var idleDelta = idle - _prevIdle;
        var kernelDelta = kernel - _prevKernel;
        var userDelta = user - _prevUser;
        _prevIdle = idle;
        _prevKernel = kernel;
        _prevUser = user;

        var total = kernelDelta + userDelta;
        if (total <= 0)
            return 0;

        // kernel includes idle time on Windows
        var busy = total - idleDelta;
        if (busy < 0) busy = 0;
        return 100.0 * busy / total;
    }
}
