using System.Collections;
using System.ComponentModel;
using System.Diagnostics;
using System.Text;
using Microsoft.Win32.SafeHandles;
using RetroHub.Core.Logging;

namespace RetroHub.Core.Emulation.Windows;

/// <summary>A process started by <see cref="ProcessLauncher"/>: its own handle (exit code, waits), the Process object, the job.</summary>
internal sealed class LaunchedProcess
{
    public required int ProcessId { get; init; }
    public required string ExePath { get; init; }
    public required SafeProcessHandle Handle { get; init; }
    public Process? Process { get; init; }
    /// <summary>Null when job assignment failed (then only the main process is tracked).</summary>
    public JobObject? Job { get; init; }
}

/// <summary>
/// Starts processes with raw CreateProcessW: suspended, put into a Job Object, then resumed – so not even the first
/// child process can escape tracking. Optionally minimized without activation (SW_SHOWMINNOACTIVE, see MonHub
/// GameWindow.StartMinimized) so an embedded emulator's window never flashes on the desktop.
/// </summary>
internal static class ProcessLauncher
{
    /// <exception cref="Win32Exception">CreateProcess failed (message is the system's, localized).</exception>
    /// <exception cref="ArgumentException">The command line or environment is invalid.</exception>
    public static unsafe LaunchedProcess Start(string exePath, IReadOnlyList<string> arguments, string? workingDirectory,
        IReadOnlyDictionary<string, string> environment, bool minimizedNoActivate, uint extraCreationFlags = 0)
    {
        var commandLine = CommandLine.Build(exePath, arguments);
        if (commandLine.Length >= CommandLine.MaxLength)
            throw new ArgumentException($"Die Befehlszeile ist zu lang ({commandLine.Length} Zeichen, höchstens {CommandLine.MaxLength - 1}).");

        string? envBlock = environment.Count > 0 ? BuildEnvironmentBlock(environment) : null;

        uint flags = Native.CREATE_SUSPENDED | extraCreationFlags;
        if (envBlock != null) flags |= Native.CREATE_UNICODE_ENVIRONMENT;

        // If RetroHub itself runs inside a job (started from a launcher/terminal that uses one), try to break away so the
        // game does not die with that job. Not allowed by every job – then start normally (still nested in our own job).
        bool tryBreakaway = Native.IsProcessInJob(Native.GetCurrentProcess(), 0, out bool inJob) && inJob;

        var si = new Native.STARTUPINFOW
        {
            cb = sizeof(Native.STARTUPINFOW),
            dwFlags = Native.STARTF_USESHOWWINDOW,
            wShowWindow = (ushort)(minimizedNoActivate ? Native.SW_SHOWMINNOACTIVE : Native.SW_SHOWNORMAL),
        };
        Native.PROCESS_INFORMATION pi;

        // CreateProcessW may modify the command line buffer: pass a private writable copy
        var cmdBuffer = new char[commandLine.Length + 1];
        commandLine.CopyTo(0, cmdBuffer, 0, commandLine.Length);

        bool ok;
        int error;
        fixed (char* cmd = cmdBuffer)
        fixed (char* env = envBlock)
        {
            ok = Native.CreateProcess(exePath, cmd, 0, 0, false, flags | (tryBreakaway ? Native.CREATE_BREAKAWAY_FROM_JOB : 0),
                env, workingDirectory, &si, &pi);
            error = ok ? 0 : System.Runtime.InteropServices.Marshal.GetLastPInvokeError();
            if (!ok && tryBreakaway && error == Native.ERROR_ACCESS_DENIED)
            {
                Log.Debug("CreateProcess: breakaway from the enclosing job not allowed, starting nested");
                ok = Native.CreateProcess(exePath, cmd, 0, 0, false, flags, env, workingDirectory, &si, &pi);
                error = ok ? 0 : System.Runtime.InteropServices.Marshal.GetLastPInvokeError();
            }
        }
        if (!ok) throw new Win32Exception(error);

        var handle = new SafeProcessHandle(pi.hProcess, ownsHandle: true);
        JobObject? job = null;
        Process? process = null;
        try
        {
            // the process exists (suspended) – Process.GetProcessById cannot miss it
            try
            {
                process = Process.GetProcessById(pi.dwProcessId);
            }
            catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
            {
                Log.Warn($"Process {pi.dwProcessId} not found right after CreateProcess", ex);
            }

            job = JobObject.TryCreate();
            if (job != null && !job.TryAssign(handle, out int assignError))
            {
                Log.Warn($"AssignProcessToJobObject failed ({new Win32Exception(assignError).Message}) – tracking only the main process {pi.dwProcessId}");
                job.Dispose();
                job = null;
            }

            if (Native.ResumeThread(pi.hThread) == uint.MaxValue)
                throw new Win32Exception(System.Runtime.InteropServices.Marshal.GetLastPInvokeError());
        }
        catch
        {
            // never leave a suspended zombie behind
            Native.TerminateProcess(handle, 1);
            handle.Dispose();
            job?.Dispose();
            process?.Dispose();
            throw;
        }
        finally
        {
            Native.CloseHandle(pi.hThread);
        }

        return new LaunchedProcess { ProcessId = pi.dwProcessId, ExePath = exePath, Handle = handle, Process = process, Job = job };
    }

    /// <summary>
    /// Unicode environment block: the current environment plus the overrides (an empty value removes a variable),
    /// "KEY=VALUE\0" entries sorted case-insensitively, terminated by an extra "\0".
    /// </summary>
    internal static string BuildEnvironmentBlock(IReadOnlyDictionary<string, string> overrides)
    {
        var vars = new SortedDictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (DictionaryEntry e in System.Environment.GetEnvironmentVariables())
        {
            if (e.Key is string k && k.Length > 0) vars[k] = e.Value as string ?? "";
        }
        foreach (var (key, value) in overrides)
        {
            if (string.IsNullOrEmpty(key) || key.Contains('=') || key.Contains('\0') || (value?.Contains('\0') ?? false))
            {
                Log.Warn($"Ignoring invalid environment variable override '{key}'");
                continue;
            }
            if (string.IsNullOrEmpty(value)) vars.Remove(key);
            else vars[key] = value;
        }
        var sb = new StringBuilder();
        foreach (var (k, v) in vars) sb.Append(k).Append('=').Append(v).Append('\0');
        if (vars.Count == 0) sb.Append('\0');
        sb.Append('\0');
        return sb.ToString();
    }

    /// <summary>
    /// Full path of an executable: rooted paths as they are, otherwise searched in the working directory, the system
    /// folders and PATH (".exe" appended when the name has no extension). Null when not found.
    /// </summary>
    public static string? ResolveExecutable(string fileName, string? workingDirectory)
    {
        if (string.IsNullOrWhiteSpace(fileName)) return null;
        fileName = fileName.Trim().Trim('"');
        try
        {
            if (Path.IsPathRooted(fileName))
            {
                var full = Path.GetFullPath(fileName);
                return File.Exists(full) ? full : null;
            }
            var names = Path.HasExtension(fileName) ? new[] { fileName } : new[] { fileName + ".exe", fileName };
            var dirs = new List<string>();
            if (!string.IsNullOrEmpty(workingDirectory)) dirs.Add(workingDirectory);
            if (fileName.IndexOfAny(['\\', '/']) < 0)
            {
                dirs.Add(System.Environment.SystemDirectory);
                dirs.Add(System.Environment.GetFolderPath(System.Environment.SpecialFolder.Windows));
                var path = System.Environment.GetEnvironmentVariable("PATH") ?? "";
                dirs.AddRange(path.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
            }
            else
            {
                dirs.Add(System.Environment.CurrentDirectory);
            }
            foreach (var d in dirs)
                foreach (var n in names)
                {
                    try
                    {
                        var candidate = Path.GetFullPath(Path.Combine(d.Trim('"'), n));
                        if (File.Exists(candidate)) return candidate;
                    }
                    catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
                    {
                    }
                }
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException or IOException)
        {
        }
        return null;
    }

    /// <summary>
    /// Whether a process with exactly this image path is running (MainModule path, falling back to
    /// QueryFullProcessImageName for processes whose modules cannot be read). Never throws.
    /// </summary>
    public static bool IsProgramRunning(string exePath, int? exceptProcessId = null)
    {
        string full;
        try
        {
            full = Path.GetFullPath(exePath);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return false;
        }
        Process[] candidates;
        try
        {
            candidates = Process.GetProcessesByName(Path.GetFileNameWithoutExtension(full));
        }
        catch (Exception ex)
        {
            Log.Warn("Process list unavailable", ex);
            return false;
        }
        bool found = false;
        foreach (var p in candidates)
        {
            try
            {
                if (found || p.Id == exceptProcessId || p.HasExited) continue;
                string? image = null;
                try
                {
                    image = p.MainModule?.FileName;
                }
                catch (Exception ex) when (ex is Win32Exception or InvalidOperationException or NotSupportedException)
                {
                    image = Native.ImagePathOf(p.Id);
                }
                if (image != null && string.Equals(Path.GetFullPath(image), full, StringComparison.OrdinalIgnoreCase)) found = true;
            }
            catch (Exception ex) when (ex is Win32Exception or InvalidOperationException or ArgumentException or NotSupportedException)
            {
                // ended in the meantime / not accessible: does not count
            }
            finally
            {
                p.Dispose();
            }
        }
        return found;
    }
}
