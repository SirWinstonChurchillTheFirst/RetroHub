using System.Runtime.InteropServices;
using Avalonia.Controls;
using Avalonia.Platform;
using RetroHub.Core.Emulation.Windows;
using RetroHub.Core.Logging;

namespace RetroHub.App.Controls;

/// <summary>
/// RetroHub's place for an embedded emulator window: a native STATIC child window
/// (<see cref="WindowEmbedder.CreateHostWindow"/>) inside the Avalonia window. The emulator's window is re-parented
/// into it by <see cref="GameScreen"/>. Avalonia positions and sizes this child window like any control (airspace:
/// it is drawn above all Avalonia content in its rectangle, so overlays mask it with <see cref="WindowEmbedder.SetMasked(nint, bool)"/>).
/// On platforms without native control hosting (headless tests) no window is created and <see cref="Hwnd"/> stays 0.
/// </summary>
public sealed class GameScreenHost : NativeControlHost
{
    private bool _own;

    /// <summary>The host window (0 while there is none).</summary>
    public nint Hwnd { get; private set; }

    /// <summary>Raised after the host window was created (UI thread).</summary>
    public event Action<nint>? Created;

    /// <summary>Right before the host window goes away – a game inside must leave first, or it would go down with it.</summary>
    public event Action<nint>? Destroying;

    protected override IPlatformHandle CreateNativeControlCore(IPlatformHandle parent)
    {
        if (OperatingSystem.IsWindows())
        {
            var hwnd = WindowEmbedder.CreateHostWindow(parent.Handle);
            if (hwnd != 0)
            {
                _own = true;
                Hwnd = hwnd;
                Log.Info($"Game screen host window {hwnd:X} created");
                Created?.Invoke(hwnd);
                return new PlatformHandle(hwnd, "HWND");
            }
        }
        var handle = base.CreateNativeControlCore(parent);
        Hwnd = handle.Handle;
        Created?.Invoke(Hwnd);
        return handle;
    }

    protected override void DestroyNativeControlCore(IPlatformHandle control)
    {
        try { Destroying?.Invoke(control.Handle); }
        catch (Exception ex) { Log.Warn("Game screen host: Destroying handler failed", ex); }
        Hwnd = 0;
        if (_own)
        {
            _own = false;
            // gives a game window still inside back to the desktop first
            WindowEmbedder.DestroyHostWindow(control.Handle);
        }
        else base.DestroyNativeControlCore(control);
    }
}

/// <summary>The few Win32 calls the game screen needs that the Core embedder does not offer (all own-process or non-blocking).</summary>
internal static partial class GameScreenNative
{
    [StructLayout(LayoutKind.Sequential)]
    public struct POINT { public int X, Y; }

    [StructLayout(LayoutKind.Sequential)]
    public struct RECT
    {
        public int Left, Top, Right, Bottom;
        public RECT(int left, int top, int right, int bottom) => (Left, Top, Right, Bottom) = (left, top, right, bottom);
    }

    public const int SW_RESTORE = 9;
    public const int SW_SHOWNORMAL = 1;

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetCursorPos(out POINT p);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool SetForegroundWindow(nint hwnd);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool ShowWindowAsync(nint hwnd, int cmd);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool IsIconic(nint hwnd);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool GetWindowRect(nint hwnd, out RECT r);

    [LibraryImport("user32.dll")]
    public static partial nint GetParent(nint hwnd);

    [LibraryImport("user32.dll")]
    public static partial nint GetForegroundWindow();

    [LibraryImport("user32.dll")]
    public static partial nint GetAncestor(nint hwnd, uint flags);

    public const uint GA_ROOT = 2;

    [StructLayout(LayoutKind.Sequential)]
    private struct GUITHREADINFO
    {
        public int cbSize, flags;
        public nint hwndActive, hwndFocus, hwndCapture, hwndMenuOwner, hwndMoveSize, hwndCaret;
        public RECT rcCaret;
    }

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetGUIThreadInfo(uint thread, ref GUITHREADINFO info);

    /// <summary>The window that has the keyboard focus on the foreground thread (0 when unknown). Read-only query.</summary>
    public static nint KeyboardFocus()
    {
        if (!OperatingSystem.IsWindows()) return 0;
        var info = new GUITHREADINFO { cbSize = Marshal.SizeOf<GUITHREADINFO>() };
        return GetGUIThreadInfo(0, ref info) ? info.hwndFocus : 0;
    }

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool IsWindowVisible(nint hwnd);

    [LibraryImport("user32.dll")]
    public static partial nint GetDC(nint hwnd);

    [LibraryImport("user32.dll")]
    public static partial int ReleaseDC(nint hwnd, nint dc);

    [LibraryImport("gdi32.dll")]
    public static partial uint GetPixel(nint dc, int x, int y);

    [LibraryImport("user32.dll")]
    public static partial nint SetFocus(nint hwnd);

    [LibraryImport("user32.dll")]
    private static partial uint GetWindowThreadProcessId(nint hwnd, out uint pid);

    /// <summary>Process id of a window's owner (0 when unknown). Read-only query.</summary>
    public static uint ProcessOf(nint hwnd) => hwnd != 0 && GetWindowThreadProcessId(hwnd, out var pid) != 0 ? pid : 0;

    [LibraryImport("gdi32.dll")]
    private static partial nint CreateRectRgn(int l, int t, int r, int b);

    [LibraryImport("gdi32.dll")]
    private static partial int CombineRgn(nint dst, nint a, nint b, int mode);

    [LibraryImport("user32.dll")]
    private static partial int SetWindowRgn(nint hwnd, nint region, [MarshalAs(UnmanagedType.Bool)] bool redraw);

    private const int RGN_DIFF = 4;

    /// <summary>
    /// Cuts <paramref name="holes"/> (screen pixels) out of the host window and its parents up to (not including) the
    /// top-level window, so Avalonia content there (toasts) is visible above the embedded game; no holes = no region.
    /// Own-process windows only (the game inside is clipped by its parent), never blocks.
    /// </summary>
    public static void SetHoles(nint host, IReadOnlyList<RECT> holes)
    {
        if (!OperatingSystem.IsWindows() || host == 0) return;
        var root = GetAncestor(host, GA_ROOT);
        for (var h = host; h != 0 && h != root; h = GetParent(h))
        {
            if (!GetWindowRect(h, out var r)) continue;
            nint region = 0;
            foreach (var hole in holes)
            {
                int l = Math.Max(hole.Left, r.Left), t = Math.Max(hole.Top, r.Top), rr = Math.Min(hole.Right, r.Right), b = Math.Min(hole.Bottom, r.Bottom);
                if (rr <= l || b <= t) continue; // this hole does not touch this window
                if (region == 0) region = CreateRectRgn(0, 0, r.Right - r.Left, r.Bottom - r.Top);
                var cut = CreateRectRgn(l - r.Left, t - r.Top, rr - r.Left, b - r.Top);
                CombineRgn(region, region, cut, RGN_DIFF);
                DeleteObject(cut);
            }
            // on success the system owns the region
            if (SetWindowRgn(h, region, true) == 0 && region != 0) DeleteObject(region);
        }
    }

    public static (int X, int Y)? Cursor()
    {
        if (!OperatingSystem.IsWindows()) return null;
        try { return GetCursorPos(out var p) ? (p.X, p.Y) : null; }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException) { return null; }
    }

    [LibraryImport("gdi32.dll")]
    private static partial nint CreateCompatibleDC(nint dc);

    [LibraryImport("gdi32.dll")]
    private static partial nint CreateCompatibleBitmap(nint dc, int w, int h);

    [LibraryImport("gdi32.dll")]
    private static partial nint SelectObject(nint dc, nint obj);

    [LibraryImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool DeleteObject(nint obj);

    [LibraryImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool DeleteDC(nint dc);

    [LibraryImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool BitBlt(nint dst, int x, int y, int w, int h, nint src, int sx, int sy, uint rop);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool PrintWindow(nint hwnd, nint dc, uint flags);

    private const uint SRCCOPY = 0x00CC0020;
    private const uint PW_RENDERFULLCONTENT = 2;

    /// <summary>
    /// Dev/test report only ("is a picture drawn inside the host?"): copies the top-level window <paramref name="root"/>
    /// into a memory bitmap – with PrintWindow (also when other windows cover it) or from the screen – and samples the
    /// rectangle <paramref name="area"/> (screen coordinates) on a grid: share of clearly non-black pixels and number
    /// of distinct colours. Only RetroHub's own window is read.
    /// </summary>
    public static (double NonBlack, int Colors) SampleWindow(nint root, RECT area, bool printWindow, int grid = 32)
    {
        if (!OperatingSystem.IsWindows() || root == 0 || area.Right <= area.Left || area.Bottom <= area.Top) return (0, 0);
        if (!GetWindowRect(root, out var rr)) return (0, 0);
        int w = rr.Right - rr.Left, h = rr.Bottom - rr.Top;
        var screen = GetDC(0);
        if (screen == 0) return (0, 0);
        var mem = CreateCompatibleDC(screen);
        var bmp = CreateCompatibleBitmap(screen, w, h);
        var old = SelectObject(mem, bmp);
        try
        {
            bool ok = printWindow ? PrintWindow(root, mem, PW_RENDERFULLCONTENT) : BitBlt(mem, 0, 0, w, h, screen, rr.Left, rr.Top, SRCCOPY);
            if (!ok) return (0, 0);
            var colors = new HashSet<uint>();
            int lit = 0, total = 0;
            for (int iy = 0; iy < grid; iy++)
            for (int ix = 0; ix < grid; ix++)
            {
                int x = area.Left + (int)((ix + 0.5) * (area.Right - area.Left) / grid) - rr.Left;
                int y = area.Top + (int)((iy + 0.5) * (area.Bottom - area.Top) / grid) - rr.Top;
                uint c = GetPixel(mem, x, y); // memory DC: fast (the screen DC would be ~7 ms per pixel under DWM)
                if (c == 0xFFFFFFFF) continue; // CLR_INVALID
                total++;
                colors.Add(c);
                int red = (int)(c & 0xFF), green = (int)((c >> 8) & 0xFF), blue = (int)((c >> 16) & 0xFF);
                if (red + green + blue > 60) lit++;
            }
            return (total == 0 ? 0 : (double)lit / total, colors.Count);
        }
        finally
        {
            SelectObject(mem, old);
            DeleteObject(bmp);
            DeleteDC(mem);
            ReleaseDC(0, screen);
        }
    }
}
