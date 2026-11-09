using System.Runtime.InteropServices;

namespace RetroHub.Core.Emulation.Windows;

/// <summary>Finds top-level windows of a set of processes. Only reads system-kept window state (never blocks on a hung program).</summary>
public static class WindowFinder
{
    /// <summary>
    /// Main-window candidates of these processes, in Z-order (topmost first): visible, unowned (GW_OWNER == 0, so no
    /// dialogs such as "update available"), not tool windows, and larger than 100x80 – or minimized (a minimized window
    /// is only a small bar far off screen, but it counts). Same rule as MonHub's GameWindow.Find.
    /// </summary>
    public static IReadOnlyList<nint> FindMainWindows(IReadOnlyCollection<int> processIds)
    {
        if (!OperatingSystem.IsWindows() || processIds.Count == 0) return [];
        var set = processIds as HashSet<int> ?? new HashSet<int>(processIds);
        var result = new List<nint>();
        foreach (var h in TopLevelWindows())
        {
            if (!set.Contains(Native.ProcessIdOf(h))) continue;
            if (!Native.IsWindowVisible(h)) continue;
            if (Native.GetWindow(h, Native.GW_OWNER) != 0) continue;
            if ((Native.GetExStyle(h) & Native.WS_EX_TOOLWINDOW) != 0) continue;
            if (Native.IsIconic(h) || Native.GetWindowRect(h, out var r) && r.Width > 100 && r.Height > 80)
                result.Add(h);
        }
        return result;
    }

    /// <summary>All top-level windows of these processes (optionally only visible ones), in Z-order.</summary>
    public static IReadOnlyList<nint> FindTopLevelWindows(IReadOnlyCollection<int> processIds, bool visibleOnly)
    {
        if (!OperatingSystem.IsWindows() || processIds.Count == 0) return [];
        var set = processIds as HashSet<int> ?? new HashSet<int>(processIds);
        var result = new List<nint>();
        foreach (var h in TopLevelWindows())
            if (set.Contains(Native.ProcessIdOf(h)) && (!visibleOnly || Native.IsWindowVisible(h)))
                result.Add(h);
        return result;
    }

    /// <summary>Every top-level window on the desktop (EnumWindows), in Z-order.</summary>
    public static unsafe List<nint> TopLevelWindows()
    {
        var list = new List<nint>(256);
        var handle = GCHandle.Alloc(list);
        try
        {
            Native.EnumWindows(&Collect, GCHandle.ToIntPtr(handle));
        }
        finally
        {
            handle.Free();
        }
        return list;
    }

    [UnmanagedCallersOnly]
    private static int Collect(nint hwnd, nint state)
    {
        // must never throw across the native boundary
        try
        {
            if (GCHandle.FromIntPtr(state).Target is List<nint> list) list.Add(hwnd);
            return 1;
        }
        catch
        {
            return 0;
        }
    }
}
