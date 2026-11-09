using RetroHub.Core.Logging;

namespace RetroHub.Core.Emulation.Windows;

/// <summary>A rectangle in screen pixels (left, top, right, bottom).</summary>
public readonly record struct ScreenRect(int Left, int Top, int Right, int Bottom)
{
    public int Width => Right - Left;
    public int Height => Bottom - Top;
    public override string ToString() => $"{Left},{Top} {Width}x{Height}";
}

/// <summary>A monitor: its full area and its work area (without the taskbar).</summary>
public readonly record struct MonitorArea(nint Handle, ScreenRect Bounds, ScreenRect Work);

/// <summary>
/// Keeps an emulator's window on RetroHub's monitor. Emulators place their (fullscreen) window where they like –
/// Dolphin, for example, opens it on whatever monitor its window last was or the mouse is – so with two monitors the
/// game could start on the other screen while RetroHub hides on this one.
/// </summary>
public static class MonitorPlacement
{
    /// <summary>The monitor the window is (mostly) on, or null.</summary>
    public static MonitorArea? MonitorOf(nint hwnd)
    {
        if (!OperatingSystem.IsWindows() || hwnd == 0) return null;
        try
        {
            var mon = Native.MonitorFromWindow(hwnd, Native.MONITOR_DEFAULTTONEAREST);
            if (mon == 0) return null;
            var info = new Native.MONITORINFO { cbSize = (uint)System.Runtime.InteropServices.Marshal.SizeOf<Native.MONITORINFO>() };
            if (!Native.GetMonitorInfo(mon, ref info)) return null;
            return new MonitorArea(mon, From(info.rcMonitor), From(info.rcWork));
        }
        catch (Exception ex)
        {
            Log.Warn($"MonitorOf({hwnd:X}) failed", ex);
            return null;
        }
    }

    /// <summary>
    /// Where <paramref name="window"/> (now on <paramref name="current"/>) belongs on <paramref name="target"/>; null when
    /// it already is on the target monitor. A window covering its whole monitor (borderless fullscreen) gets the
    /// target's full area; any other window keeps its size (shrunk to fit) and is centred in the target's work area.
    /// </summary>
    public static ScreenRect? PlanMove(ScreenRect window, MonitorArea current, MonitorArea target)
    {
        if (current.Bounds == target.Bounds) return null;
        const int slack = 2; // fullscreen windows are sometimes a pixel off
        var fullscreen = window.Left <= current.Bounds.Left + slack && window.Top <= current.Bounds.Top + slack &&
                         window.Right >= current.Bounds.Right - slack && window.Bottom >= current.Bounds.Bottom - slack;
        if (fullscreen) return target.Bounds;
        var w = Math.Min(window.Width, target.Work.Width);
        var h = Math.Min(window.Height, target.Work.Height);
        var x = target.Work.Left + (target.Work.Width - w) / 2;
        var y = target.Work.Top + (target.Work.Height - h) / 2;
        return new ScreenRect(x, y, x + w, y + h);
    }

    /// <summary>
    /// Moves the window onto <paramref name="target"/> when it is on another monitor. Asynchronous (never blocks on a
    /// busy emulator), does not activate or re-order it. True when a move was requested. Never throws.
    /// </summary>
    public static bool MoveTo(nint window, MonitorArea target)
    {
        if (!OperatingSystem.IsWindows() || window == 0) return false;
        try
        {
            if (!Native.IsWindow(window) || Native.IsIconic(window)) return false;
            if (MonitorOf(window) is not { } current) return false;
            if (!Native.GetWindowRect(window, out var r)) return false;
            if (PlanMove(From(r), current, target) is not { } to) return false;
            Log.Info($"Emulator window {window:X} is on another monitor ({current.Bounds}) – moving it to RetroHub's monitor: {to}");
            return Native.SetWindowPos(window, 0, to.Left, to.Top, to.Width, to.Height,
                Native.SWP_NOZORDER | Native.SWP_NOACTIVATE | Native.SWP_ASYNCWINDOWPOS);
        }
        catch (Exception ex)
        {
            Log.Warn($"Moving window {window:X} to RetroHub's monitor failed", ex);
            return false;
        }
    }

    private static ScreenRect From(Native.RECT r) => new(r.Left, r.Top, r.Right, r.Bottom);
}
