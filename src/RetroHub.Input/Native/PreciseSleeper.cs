using System.Runtime.InteropServices;

namespace RetroHub.Input.Native;

/// <summary>
/// Sleeps with sub-millisecond precision on Windows 10 1803+ using a high-resolution waitable timer, without
/// changing the global timer resolution (timeBeginPeriod). Falls back to <see cref="Thread.Sleep(int)"/>
/// (~15.6 ms granularity) when unavailable. One instance per thread; not thread-safe.
/// </summary>
internal sealed partial class PreciseSleeper : IDisposable
{
    private const uint CREATE_WAITABLE_TIMER_HIGH_RESOLUTION = 0x00000002;
    private const uint TIMER_ALL_ACCESS = 0x001F0003;
    private const uint INFINITE = 0xFFFFFFFF;

    private nint _timer;

    public bool IsHighResolution => _timer != 0;

    public PreciseSleeper()
    {
        if (!OperatingSystem.IsWindows()) return;
        try { _timer = CreateWaitableTimerExW(0, null, CREATE_WAITABLE_TIMER_HIGH_RESOLUTION, TIMER_ALL_ACCESS); }
        catch { _timer = 0; }
    }

    public void Sleep(TimeSpan duration)
    {
        if (duration <= TimeSpan.Zero) return;
        if (_timer != 0)
        {
            long due = -duration.Ticks; // negative = relative, in 100 ns units (= TimeSpan ticks)
            if (SetWaitableTimer(_timer, ref due, 0, 0, 0, false))
            {
                WaitForSingleObject(_timer, INFINITE);
                return;
            }
        }
        Thread.Sleep(Math.Max(1, (int)Math.Round(duration.TotalMilliseconds)));
    }

    public void Dispose()
    {
        if (_timer != 0) { CloseHandle(_timer); _timer = 0; }
    }

    [LibraryImport("kernel32.dll", StringMarshalling = StringMarshalling.Utf16, SetLastError = true)]
    private static partial nint CreateWaitableTimerExW(nint attributes, string? name, uint flags, uint access);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetWaitableTimer(nint timer, ref long dueTime, int period, nint completion, nint arg,
        [MarshalAs(UnmanagedType.Bool)] bool resume);

    [LibraryImport("kernel32.dll")]
    private static partial uint WaitForSingleObject(nint handle, uint milliseconds);

    [LibraryImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool CloseHandle(nint handle);
}
