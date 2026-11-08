using RetroHub.Core.Logging;

namespace RetroHub.Core.Emulation.Windows;

/// <summary>Brings a window to the front despite Windows' foreground-lock rules (gamepad input does not count as user input).</summary>
public static class Foreground
{
    /// <summary>
    /// Restores the window if minimized, then: AttachThreadInput to the current foreground window's thread +
    /// SetForegroundWindow; if Windows still refuses, a synthetic Alt tap (SendInput VK_MENU) and another try.
    /// Never throws.
    /// </summary>
    public static unsafe void BringToFront(nint hwnd)
    {
        if (!OperatingSystem.IsWindows() || hwnd == 0) return;
        try
        {
            if (!Native.IsWindow(hwnd)) return;
            if (Native.IsIconic(hwnd)) Native.ShowWindow(hwnd, Native.SW_RESTORE);
            if (Native.GetForegroundWindow() == hwnd) return;

            uint mine = Native.GetCurrentThreadId();
            var fg = Native.GetForegroundWindow();
            uint theirs = fg != 0 ? Native.GetWindowThreadProcessId(fg, out _) : 0;
            bool joined = theirs != 0 && theirs != mine && Native.AttachThreadInput(mine, theirs, true);
            try
            {
                Native.BringWindowToTop(hwnd);
                Native.SetForegroundWindow(hwnd);
            }
            finally
            {
                if (joined) Native.AttachThreadInput(mine, theirs, false);
            }
            if (Native.GetForegroundWindow() == hwnd) return;

            // the "last input" rule: an Alt tap makes this process the one that received the last input event
            var inputs = stackalloc Native.KEYBOARD_INPUT[2];
            inputs[0] = new Native.KEYBOARD_INPUT { type = Native.INPUT_KEYBOARD, wVk = Native.VK_MENU };
            inputs[1] = new Native.KEYBOARD_INPUT { type = Native.INPUT_KEYBOARD, wVk = Native.VK_MENU, dwFlags = Native.KEYEVENTF_KEYUP };
            Native.SendInput(2, inputs, sizeof(Native.KEYBOARD_INPUT));
            Native.SetForegroundWindow(hwnd);
            if (Native.GetForegroundWindow() != hwnd) Log.Info($"BringToFront({hwnd:X}): Windows refused the foreground change");
        }
        catch (Exception ex)
        {
            Log.Warn("BringToFront failed", ex);
        }
    }
}
