using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using static AIMouse.Native.Win32;

namespace AIMouse.Native;

public readonly record struct MonitorData(RECT Bounds, RECT Work, double Scale);

public static class WindowHelper
{
    /// <summary>Set false (via --visible-to-capture) so test screenshots can see our windows.</summary>
    public static bool ExcludeFromCapture = true;

    public static IntPtr Handle(Window w) => new WindowInteropHelper(w).EnsureHandle();

    /// <summary>Non-activating tool window, excluded from screen capture; optionally click-through.</summary>
    public static void MakeToolWindow(Window w, bool clickThrough)
    {
        var h = Handle(w);
        long ex = GetWindowLongPtr(h, GWL_EXSTYLE).ToInt64();
        ex |= WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW;
        if (clickThrough) ex |= WS_EX_TRANSPARENT | WS_EX_LAYERED;
        SetWindowLongPtr(h, GWL_EXSTYLE, new IntPtr(ex));
        if (ExcludeFromCapture && !SetWindowDisplayAffinity(h, WDA_EXCLUDEFROMCAPTURE))
            Core.Log.Write($"SetWindowDisplayAffinity failed: {Marshal.GetLastWin32Error()} ({w.GetType().Name})");
    }

    public static void PlacePhysical(Window w, int x, int y, int width, int height)
    {
        if (!w.IsVisible) w.Show();
        SetWindowPos(Handle(w), HWND_TOPMOST, x, y, Math.Max(1, width), Math.Max(1, height), SWP_NOACTIVATE | SWP_SHOWWINDOW);
    }

    public static void MovePhysical(Window w, int x, int y)
    {
        if (!w.IsVisible) w.Show();
        SetWindowPos(Handle(w), HWND_TOPMOST, x, y, 0, 0, SWP_NOACTIVATE | SWP_SHOWWINDOW | SWP_NOSIZE);
    }

    public static RECT GetPhysicalRect(Window w)
    {
        GetWindowRect(Handle(w), out var r);
        return r;
    }

    public static MonitorData GetMonitor(POINT p)
    {
        var hmon = MonitorFromPoint(p, MONITOR_DEFAULTTONEAREST);
        var mi = new MONITORINFO { cbSize = Marshal.SizeOf<MONITORINFO>() };
        GetMonitorInfo(hmon, ref mi);
        double scale = 1.0;
        if (GetDpiForMonitor(hmon, 0, out uint dx, out _) == 0 && dx > 0) scale = dx / 96.0;
        return new MonitorData(mi.rcMonitor, mi.rcWork, scale);
    }

    public static POINT CursorPos()
    {
        GetCursorPos(out var p);
        return p;
    }
}
