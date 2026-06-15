using System.Runtime.InteropServices;

namespace ZenFakeCapture.Win32;

internal static class WindowHelper
{
    private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

    private static IntPtr _cachedHwnd = IntPtr.Zero;
    private static int _cachedPid;
    private static long _cacheTicks;

    [DllImport("user32.dll")]
    private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool IsIconic(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(IntPtr hWnd, out RECT rect);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetWindowDisplayAffinity(IntPtr hWnd, uint dwAffinity);

    internal const uint WdaNone = 0x00000000;
    internal const uint WdaExcludeFromCapture = 0x00000011;

    [StructLayout(LayoutKind.Sequential)]
    internal struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;

        public int Width => Right - Left;
        public int Height => Bottom - Top;
    }

    internal static IntPtr FindMainWindowForProcess(int processId, int cacheMs = 400)
    {
        var now = Environment.TickCount64;
        if (_cachedPid == processId &&
            _cachedHwnd != IntPtr.Zero &&
            now - _cacheTicks < cacheMs &&
            IsWindowVisible(_cachedHwnd))
        {
            return _cachedHwnd;
        }

        IntPtr best = IntPtr.Zero;
        var bestArea = 0L;

        EnumWindows(
            (hWnd, _) =>
            {
                GetWindowThreadProcessId(hWnd, out var pid);
                if (pid != (uint)processId || !IsWindowVisible(hWnd))
                {
                    return true;
                }

                if (!GetWindowRect(hWnd, out var rect))
                {
                    return true;
                }

                var area = (long)rect.Width * rect.Height;
                if (area > bestArea)
                {
                    bestArea = area;
                    best = hWnd;
                }

                return true;
            },
            IntPtr.Zero
        );

        _cachedPid = processId;
        _cachedHwnd = best;
        _cacheTicks = now;
        return best;
    }

    internal static bool TryGetWindowRect(IntPtr hWnd, out RECT rect)
    {
        return GetWindowRect(hWnd, out rect);
    }

    internal static bool IsWindowMinimized(IntPtr hWnd)
    {
        return hWnd != IntPtr.Zero && IsIconic(hWnd);
    }

    internal static bool IsProcessAlive(int processId)
    {
        try
        {
            using var proc = System.Diagnostics.Process.GetProcessById(processId);
            return !proc.HasExited;
        }
        catch
        {
            return false;
        }
    }

    internal static bool TryExcludeFromCapture(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero)
        {
            return false;
        }

        return SetWindowDisplayAffinity(hwnd, WdaExcludeFromCapture);
    }

    internal static void ClearExcludeFromCapture(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero)
        {
            return;
        }

        SetWindowDisplayAffinity(hwnd, WdaNone);
    }
}
