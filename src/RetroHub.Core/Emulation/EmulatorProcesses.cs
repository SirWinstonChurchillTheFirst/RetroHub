using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using RetroHub.Core.Logging;

namespace RetroHub.Core.Emulation;

/// <summary>
/// "Is this exact emulator exe running?" — matched by full image path, so only RetroHub's own portable copy
/// counts (an emulator of the same name started from elsewhere does not block anything).
/// The process list is taken at most once per second and shared by all callers (one launch asks several times).
/// Thread-safe, never throws.
/// </summary>
public static class EmulatorProcesses
{
    private static readonly TimeSpan CacheTime = TimeSpan.FromSeconds(1);
    private static readonly object Gate = new();
    private static Snapshot? _snapshot;

    /// <summary>Test hook: replaces the real check for the current async flow only (parallel tests stay isolated).</summary>
    internal static readonly AsyncLocal<Func<string, bool>?> Override = new();

    private sealed record Snapshot(DateTime TakenUtc, HashSet<string> Paths, HashSet<string> UnknownNames);

    /// <summary>
    /// True when a running process has exactly this image path (case-insensitive, normalised).
    /// A process whose path cannot be read (access denied) but whose name equals the exe name counts as running
    /// — better to skip a config write than to have it overwritten by the emulator on exit.
    /// </summary>
    public static bool IsRunning(string? exePath)
    {
        if (string.IsNullOrWhiteSpace(exePath)) return false;
        if (Override.Value is { } fake) return fake(exePath);
        string full;
        try { full = Path.GetFullPath(exePath); }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException) { return false; }
        var snap = Take();
        if (snap.Paths.Contains(full)) return true;
        return snap.UnknownNames.Contains(Path.GetFileNameWithoutExtension(full));
    }

    /// <summary>Drops the cached process list (e.g. right after an emulator exited).</summary>
    public static void Invalidate()
    {
        lock (Gate) _snapshot = null;
    }

    private static Snapshot Take()
    {
        lock (Gate)
        {
            if (_snapshot != null && DateTime.UtcNow - _snapshot.TakenUtc < CacheTime) return _snapshot;
            var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var unknown = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            Process[] all;
            try { all = Process.GetProcesses(); }
            catch (Exception ex)
            {
                Log.Warn("Process list unavailable", ex);
                all = [];
            }
            foreach (var p in all)
            {
                try
                {
                    var path = ImagePath(p, out bool exited);
                    if (path != null) paths.Add(path);
                    else if (!exited)
                    {
                        string? name = null;
                        try { name = p.ProcessName; } catch (InvalidOperationException) { /* exited meanwhile */ }
                        if (!string.IsNullOrEmpty(name) && p.Id > 4) unknown.Add(name);
                    }
                }
                catch
                {
                    // a process that vanished meanwhile: ignore
                }
                finally { p.Dispose(); }
            }
            _snapshot = new Snapshot(DateTime.UtcNow, paths, unknown);
            return _snapshot;
        }
    }

    /// <summary>Full image path of a live process, or null when it cannot be read or the process already exited.</summary>
    private static string? ImagePath(Process p, out bool exited)
    {
        exited = false;
        if (OperatingSystem.IsWindows())
        {
            var h = OpenProcess(ProcessQueryLimitedInformation, false, (uint)p.Id);
            if (h != 0)
            {
                try
                {
                    if (GetExitCodeProcess(h, out var code) && code != StillActive)
                    {
                        exited = true; // ended, but a handle is still open somewhere (e.g. the session's Process object)
                        return null;
                    }
                    var sb = new StringBuilder(1024);
                    int size = sb.Capacity;
                    if (QueryFullProcessImageName(h, 0, sb, ref size)) return Normalize(sb.ToString(0, size));
                }
                finally { CloseHandle(h); }
            }
            // No MainModule fallback on Windows: it needs more rights than PROCESS_QUERY_LIMITED_INFORMATION, and
            // for protected/system processes (Registry, Memory Compression, …) .NET's module enumeration retries
            // with sleeps — measured ~800 ms per snapshot. The caller treats such processes as "path unknown".
            return null;
        }
        try
        {
            if (p.HasExited) { exited = true; return null; }
            var file = p.MainModule?.FileName;
            return file == null ? null : Normalize(file);
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException or NotSupportedException)
        {
            return null;
        }
    }

    private static string Normalize(string path)
    {
        if (path.StartsWith(@"\\?\", StringComparison.Ordinal)) path = path[4..];
        try { return Path.GetFullPath(path); } catch { return path; }
    }

    private const uint ProcessQueryLimitedInformation = 0x1000;
    private const uint StillActive = 259;

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern nint OpenProcess(uint access, bool inherit, uint pid);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(nint handle);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetExitCodeProcess(nint process, out uint exitCode);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode, EntryPoint = "QueryFullProcessImageNameW")]
    private static extern bool QueryFullProcessImageName(nint process, uint flags, StringBuilder exeName, ref int size);
}
