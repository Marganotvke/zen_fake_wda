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
    private static extern bool IsWindowVisibleNative(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool IsIconic(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(IntPtr hWnd, out RECT rect);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetWindowDisplayAffinity(IntPtr hWnd, uint dwAffinity);

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern IntPtr GetAncestor(IntPtr hwnd, uint gaFlags);

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint dwFlags);

    internal const uint WdaNone = 0x00000000;
    internal const uint WdaExcludeFromCapture = 0x00000011;
    private const int SwHide = 0;
    private const int SwShowNoActivate = 8;
    private const uint GaRoot = 2;
    private const uint MonitorDefaultToNearest = 2;

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

                var root = GetAncestor(hWnd, GaRoot);
                if (root == IntPtr.Zero || root != hWnd)
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

    internal static void GetWindowOwnerPid(IntPtr hWnd, out uint processId)
    {
        GetWindowThreadProcessId(hWnd, out processId);
    }

    internal static bool IsWindowVisible(IntPtr hWnd)
    {
        return hWnd != IntPtr.Zero && IsWindowVisibleNative(hWnd);
    }

    internal static bool IsWindowMinimized(IntPtr hWnd)
    {
        return IsWindowVisible(hWnd) && IsIconic(hWnd);
    }

    internal static void HideWindow(IntPtr hWnd)
    {
        ShowWindow(hWnd, SwHide);
    }

    internal static void ShowWindowNoActivate(IntPtr hWnd)
    {
        ShowWindow(hWnd, SwShowNoActivate);
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

    internal static IntPtr GetForegroundRootWindow()
    {
        var foreground = GetForegroundWindow();
        if (foreground == IntPtr.Zero)
        {
            return IntPtr.Zero;
        }

        var root = GetAncestor(foreground, GaRoot);
        return root != IntPtr.Zero ? root : foreground;
    }

    internal static bool IsSameMonitor(IntPtr hwndA, IntPtr hwndB)
    {
        if (hwndA == IntPtr.Zero || hwndB == IntPtr.Zero)
        {
            return false;
        }

        var monitorA = MonitorFromWindow(hwndA, MonitorDefaultToNearest);
        var monitorB = MonitorFromWindow(hwndB, MonitorDefaultToNearest);
        return monitorA != IntPtr.Zero && monitorA == monitorB;
    }

    /// <summary>
    /// WDA targets: always Zen; plus foreground app when another process is focused on the same monitor.
    /// </summary>
    internal static IReadOnlyList<IntPtr> ResolveWdaExclusionTargets(IntPtr zenHwnd, int watchPid)
    {
        if (zenHwnd == IntPtr.Zero)
        {
            return Array.Empty<IntPtr>();
        }

        var targets = new List<IntPtr> { zenHwnd };
        var foreground = GetForegroundRootWindow();
        if (foreground == IntPtr.Zero ||
            !IsWindowVisible(foreground) ||
            IsWindowMinimized(foreground) ||
            !GetWindowRect(foreground, out var fgRect) ||
            fgRect.Width <= 0 ||
            fgRect.Height <= 0)
        {
            return targets;
        }

        GetWindowThreadProcessId(foreground, out var fgPid);
        if (fgPid == (uint)watchPid)
        {
            if (foreground != zenHwnd && IsSameMonitor(foreground, zenHwnd))
            {
                targets.Add(foreground);
            }

            return targets;
        }

        if (!IsSameMonitor(foreground, zenHwnd))
        {
            return targets;
        }

        targets.Add(foreground);
        return targets;
    }

    internal static bool TryExcludeFromCapture(IntPtr hwnd, bool required = true)
    {
        if (hwnd == IntPtr.Zero)
        {
            return false;
        }

        if (!SetWindowDisplayAffinity(hwnd, WdaExcludeFromCapture))
        {
            var error = Marshal.GetLastWin32Error();
            Console.Error.WriteLine(
                required
                    ? $"SetWindowDisplayAffinity failed for HWND 0x{hwnd:X} (error {error})"
                    : $"SetWindowDisplayAffinity optional exclude failed for HWND 0x{hwnd:X} (error {error})"
            );
            return false;
        }

        return true;
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
