using System.ComponentModel;
using System.Diagnostics;
using Microsoft.Win32.SafeHandles;
using RetroHub.Core.Emulation.Windows;
using RetroHub.Core.Logging;

namespace RetroHub.Core.Emulation;

/// <summary>
/// A running (or finished) game started by <see cref="SessionService"/>. Tracks the emulator's whole process tree
/// through a Job Object (falls back to the main process when no job could be used), measures play time, and ends
/// exactly once when every process of the session has exited.
/// </summary>
public sealed class GameSession : IGameSession
{
    private const int StateStarting = 0, StateRunning = 1, StateExited = 2, StateFailed = 3;

    private readonly SafeProcessHandle? _handle;
    private readonly JobObject? _job;
    private readonly TimeSpan _elapsedBefore;
    private readonly Stopwatch _clock = new();
    private readonly AutoResetEvent _wake = new(false);
    private readonly TaskCompletionSource<bool> _endedTcs = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly object _eventGate = new();
    private Action<IGameSession>? _ended;
    private int _state;
    private int _finished;
    private volatile bool _killed;
    private volatile bool _stopTracking;
    private TimeSpan _total;
    private Thread? _tracker;

    // service hooks (called on the tracker thread)
    internal Action<GameSession>? OnHeartbeat;
    internal Action<GameSession>? OnFinishing;
    internal Action<GameSession>? OnEndedRaised;
    internal TimeSpan PollInterval = TimeSpan.FromMilliseconds(500);
    internal TimeSpan HeartbeatInterval = TimeSpan.FromSeconds(30);

    public LaunchPlan Plan { get; }
    public SessionState State => (SessionState)Volatile.Read(ref _state);
    public Process? Process { get; }
    /// <summary>Id of the emulator's main process (0 for detached/failed sessions).</summary>
    public int ProcessId { get; }
    /// <summary>Full path of the started executable.</summary>
    public string? ExePath { get; }
    /// <summary>Emulator id recorded with the play session (plan.Emulator.Def.Id, or the id from session.json after recovery).</summary>
    public string? EmulatorId { get; }
    public DateTime StartedUtc { get; }
    /// <summary>Wall-clock end of the session (null while running).</summary>
    public DateTime? EndedUtc { get; private set; }
    public TimeSpan Elapsed => State is SessionState.Exited or SessionState.Failed ? _total : _elapsedBefore + _clock.Elapsed;
    public int? ExitCode { get; private set; }
    public string? Error { get; private set; }
    /// <summary>True when the whole process tree is followed through a Job Object (false: only the main process).</summary>
    public bool TracksProcessTree => _job != null;
    /// <summary>Started through the Windows shell: not tracked, ended immediately (only a launch was recorded).</summary>
    public bool Detached { get; private init; }
    /// <summary>Re-attached after RetroHub was restarted while the game kept running (see SessionService.RecoverInterruptedSession).</summary>
    public bool Recovered { get; private init; }
    /// <summary>True when the session was ended through <see cref="Kill"/>.</summary>
    public bool WasKilled => _killed;

    /// <summary>
    /// Raised once when every process of the session has exited (on a background thread). A handler added after the
    /// session already ended is invoked immediately, so no subscriber can miss it. Handler exceptions are logged.
    /// </summary>
    public event Action<IGameSession>? Ended
    {
        add
        {
            if (value == null) return;
            bool already;
            lock (_eventGate)
            {
                already = _finished == 2;
                if (!already) _ended += value;
            }
            if (already) SafeInvoke(value, "Ended");
        }
        remove
        {
            lock (_eventGate) _ended -= value;
        }
    }

    internal GameSession(LaunchPlan plan, string? emulatorId, DateTime startedUtc, TimeSpan elapsedBefore, Process? process,
        SafeProcessHandle? handle, int processId, JobObject? job, string? exePath, bool recovered = false)
    {
        Plan = plan;
        EmulatorId = emulatorId;
        StartedUtc = startedUtc;
        _elapsedBefore = elapsedBefore < TimeSpan.Zero ? TimeSpan.Zero : elapsedBefore;
        Process = process;
        _handle = handle;
        ProcessId = processId;
        _job = job;
        ExePath = exePath;
        Recovered = recovered;
        _state = StateStarting;
    }

    /// <summary>A session that could not be started (no events are raised; Current stays empty).</summary>
    internal static GameSession CreateFailed(LaunchPlan plan, string error) =>
        new(plan, plan.Emulator?.Def.Id, DateTime.UtcNow, TimeSpan.Zero, null, null, 0, null, null)
        {
            _state = StateFailed,
            Error = error,
            _finished = 2,
        };

    /// <summary>A shell-executed session: already exited, play time 0.</summary>
    internal static GameSession CreateDetached(LaunchPlan plan, Process? process, DateTime startedUtc)
    {
        var s = new GameSession(plan, plan.Emulator?.Def.Id, startedUtc, TimeSpan.Zero, process, null, SafeId(process), null, plan.FileName)
        {
            Detached = true,
        };
        s._state = StateRunning;
        return s;
    }

    private static int SafeId(Process? p)
    {
        try { return p?.Id ?? 0; }
        catch (InvalidOperationException) { return 0; }
    }

    /// <summary>The process runs: play time starts counting (called right after the process was resumed / re-attached).</summary>
    internal void MarkRunning()
    {
        if (!_clock.IsRunning) _clock.Start();
        Volatile.Write(ref _state, StateRunning);
    }

    /// <summary>
    /// Starts the background tracker (500 ms poll + Process.Exited wake-up). Called after SessionStarted was raised,
    /// so Ended/SessionEnded can never come before it – a process that already exited is noticed on the first poll.
    /// </summary>
    internal void StartTracking()
    {
        MarkRunning();
        if (Process != null)
        {
            try
            {
                Process.EnableRaisingEvents = true;
                Process.Exited += (_, _) => Wake();
            }
            catch (Exception ex) when (ex is InvalidOperationException or Win32Exception or NotSupportedException)
            {
                // polling alone notices the end
            }
        }
        _tracker = new Thread(TrackLoop) { IsBackground = true, Name = $"RetroHub session {ProcessId}" };
        _tracker.Start();
    }

    /// <summary>Stops tracking without ending the session (service disposed; the game keeps running).</summary>
    internal void StopTracking()
    {
        _stopTracking = true;
        Wake();
    }

    private void Wake()
    {
        try { _wake.Set(); }
        catch (ObjectDisposedException) { }
    }

    private void TrackLoop()
    {
        long lastBeat = Stopwatch.GetTimestamp();
        while (!_stopTracking)
        {
            try
            {
                if (HasEnded())
                {
                    Finish();
                    return;
                }
                if (Stopwatch.GetElapsedTime(lastBeat) >= HeartbeatInterval)
                {
                    lastBeat = Stopwatch.GetTimestamp();
                    try { OnHeartbeat?.Invoke(this); }
                    catch (Exception ex) { Log.Warn("Session heartbeat failed", ex); }
                }
            }
            catch (Exception ex)
            {
                Log.Warn($"Session tracker error (pid {ProcessId})", ex);
            }
            try { _wake.WaitOne(PollInterval); }
            catch (ObjectDisposedException) { return; }
        }
    }

    private bool HasEnded()
    {
        if (_job != null && _job.ActiveProcesses() is int active) return active == 0;
        if (_handle != null && !_handle.IsInvalid && !_handle.IsClosed)
            return Native.WaitForSingleObject(_handle, 0) == Native.WAIT_OBJECT_0;
        if (Process != null)
        {
            try { return Process.HasExited; }
            catch (Exception ex) when (ex is InvalidOperationException or Win32Exception or NotSupportedException) { }
        }
        try
        {
            using var p = Process.GetProcessById(ProcessId);
            return p.HasExited;
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or Win32Exception)
        {
            return true;
        }
    }

    private void Finish()
    {
        if (Interlocked.CompareExchange(ref _finished, 1, 0) != 0) return;
        _clock.Stop();
        _total = _elapsedBefore + _clock.Elapsed;
        EndedUtc = StartedUtc + _total;
        ExitCode = ReadExitCode();
        Volatile.Write(ref _state, StateExited);
        Log.Info($"Session ended: game {Plan.Game.Id}, pid {ProcessId}, {_total.TotalSeconds:0.0} s, exit code {ExitCode?.ToString() ?? "?"}{(_killed ? " (killed)" : "")}");

        try { OnFinishing?.Invoke(this); }
        catch (Exception ex) { Log.Error("Session end bookkeeping failed", ex); }

        RaiseEnded();
        _endedTcs.TrySetResult(true);

        try { OnEndedRaised?.Invoke(this); }
        catch (Exception ex) { Log.Error("SessionEnded notification failed", ex); }

        _job?.Dispose();
        _handle?.Dispose();
    }

    /// <summary>Ends a detached session right away (called by the service after recording the launch).</summary>
    internal void FinishDetached()
    {
        _total = TimeSpan.Zero;
        EndedUtc = StartedUtc;
        Interlocked.Exchange(ref _finished, 1);
        Volatile.Write(ref _state, StateExited);
        RaiseEnded();
        _endedTcs.TrySetResult(true);
    }

    private void RaiseEnded()
    {
        Action<IGameSession>? handlers;
        lock (_eventGate)
        {
            handlers = _ended;
            _ended = null;
            _finished = 2;
        }
        if (handlers == null) return;
        foreach (var d in handlers.GetInvocationList()) SafeInvoke((Action<IGameSession>)d, "Ended");
    }

    private void SafeInvoke(Action<IGameSession> handler, string name)
    {
        try { handler(this); }
        catch (Exception ex) { Log.Error($"{name} handler threw", ex); }
    }

    private int? ReadExitCode()
    {
        try
        {
            if (_handle != null && !_handle.IsInvalid && !_handle.IsClosed && Native.GetExitCodeProcess(_handle, out uint code) && code != Native.STILL_ACTIVE)
                return unchecked((int)code);
            if (Process != null && Process.HasExited) return Process.ExitCode;
        }
        catch (Exception ex) when (ex is InvalidOperationException or Win32Exception or NotSupportedException or ObjectDisposedException)
        {
        }
        return null;
    }

    /// <summary>Ids of the session's live processes (job members, or just the main process).</summary>
    public IReadOnlyList<int> ProcessIds()
    {
        if (State is SessionState.Exited or SessionState.Failed) return [];
        if (_job?.ProcessIds() is { Count: > 0 } ids) return ids;
        return ProcessId != 0 ? [ProcessId] : [];
    }

    public IReadOnlyList<nint> FindWindows()
    {
        try
        {
            var ids = ProcessIds();
            return ids.Count == 0 ? [] : WindowFinder.FindMainWindows(new HashSet<int>(ids));
        }
        catch (Exception ex)
        {
            Log.Warn("FindWindows failed", ex);
            return [];
        }
    }

    /// <summary>
    /// Posts WM_CLOSE to every visible top-level window of the session's processes (like the emulator's own close
    /// button, so it can save on the way) and waits up to <paramref name="timeout"/> for all processes to exit.
    /// </summary>
    public async Task<bool> RequestCloseAsync(TimeSpan timeout)
    {
        if (State is SessionState.Exited or SessionState.Failed) return true;
        try
        {
            var ids = ProcessIds();
            int posted = 0;
            if (ids.Count > 0)
                foreach (var hwnd in WindowFinder.FindTopLevelWindows(new HashSet<int>(ids), visibleOnly: true))
                    if (Native.PostMessage(hwnd, Native.WM_CLOSE, 0, 0)) posted++;
            Log.Info($"RequestClose: WM_CLOSE posted to {posted} window(s) of pid(s) {string.Join(",", ids)}");
        }
        catch (Exception ex)
        {
            Log.Warn("RequestClose failed", ex);
        }
        if (timeout < TimeSpan.Zero && timeout != Timeout.InfiniteTimeSpan) timeout = TimeSpan.Zero;
        var finished = await Task.WhenAny(_endedTcs.Task, Task.Delay(timeout)).ConfigureAwait(false);
        return finished == _endedTcs.Task || State == SessionState.Exited;
    }

    /// <summary>Kills the whole process tree: TerminateJobObject, falling back to Process.Kill(entireProcessTree: true).</summary>
    public void Kill()
    {
        if (State is SessionState.Exited or SessionState.Failed || Detached) return;
        _killed = true;
        bool done = false;
        try
        {
            done = _job?.Terminate(1) ?? false;
        }
        catch (Exception ex)
        {
            Log.Warn("TerminateJobObject failed", ex);
        }
        if (!done)
        {
            try
            {
                if (Process != null) Process.Kill(entireProcessTree: true);
                else if (_handle != null && !_handle.IsClosed) Native.TerminateProcess(_handle, 1);
                done = true;
            }
            catch (Exception ex) when (ex is InvalidOperationException or Win32Exception or NotSupportedException or AggregateException or ObjectDisposedException)
            {
                Log.Warn($"Kill of pid {ProcessId} failed", ex);
            }
        }
        Log.Info($"Session killed (pid {ProcessId}, job {(_job != null ? "yes" : "no")}, ok {done})");
        Wake();
    }
}
