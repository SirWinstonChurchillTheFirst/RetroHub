using System.ComponentModel;
using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Win32.SafeHandles;
using RetroHub.Core.Catalog;
using RetroHub.Core.Emulation.Windows;
using RetroHub.Core.IO;
using RetroHub.Core.Library;
using RetroHub.Core.Logging;

namespace RetroHub.Core.Emulation;

/// <summary>
/// Starts games and follows them until every process has exited; records play sessions in the repository.
/// One session at a time.
/// <list type="bullet">
/// <item>Normal start: CreateProcessW (suspended) → Job Object WITHOUT kill-on-close (the game survives a RetroHub
/// crash) → resume. Embedded launches start minimized without activation so the window never flashes.</item>
/// <item>Shell start (<see cref="LaunchPlan.UseShellExecute"/>: .lnk / steam://): cannot be tracked – the shell hands
/// off to another program. The session is "detached": a launch is recorded (PlayCount+1, LastPlayed, 0 seconds) and
/// the session ends immediately (State = Exited, events raised right away).</item>
/// <item>Crash recovery: data/cache/session.json is written at start, refreshed every 30 s and deleted at the end.
/// <see cref="RecoverInterruptedSession"/> re-attaches to a still-running game or records the interrupted session.</item>
/// </list>
/// A failed process start does not throw: <see cref="Start"/> returns a session with State = Failed and a German
/// <see cref="IGameSession.Error"/>; no events are raised and <see cref="Current"/> stays empty.
/// </summary>
public sealed class SessionService : ISessionService, IDisposable
{
    public const string SessionFileName = "session.json";

    private static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly IGameRepository _repo;
    private readonly AppPaths _paths;
    private readonly IReadOnlyList<IPortableSetup> _setups;
    private readonly object _gate = new();
    private readonly object _fileGate = new();
    private volatile GameSession? _current;
    private bool _disposed;

    // tuning knobs for tests
    internal TimeSpan HeartbeatInterval { get; set; } = TimeSpan.FromSeconds(30);
    internal TimeSpan PollInterval { get; set; } = TimeSpan.FromMilliseconds(500);
    /// <summary>Extra CreateProcess flags (tests use CREATE_NO_WINDOW for console programs).</summary>
    internal uint ExtraCreationFlags { get; set; }
    internal ProcessWindowStyle ShellWindowStyle { get; set; } = ProcessWindowStyle.Normal;

    public SessionService(IGameRepository repo, AppPaths paths, IEnumerable<IPortableSetup> setups)
    {
        _repo = repo ?? throw new ArgumentNullException(nameof(repo));
        _paths = paths ?? throw new ArgumentNullException(nameof(paths));
        _setups = setups?.ToList() ?? [];
    }

    public IGameSession? Current => _current;
    /// <summary>The running session with its concrete type (window lookup, process ids, …).</summary>
    public GameSession? CurrentSession => _current;
    /// <summary>data/cache/session.json.</summary>
    public string SessionFile => Path.Combine(_paths.Cache, SessionFileName);

    public event Action<IGameSession>? SessionStarted;
    public event Action<IGameSession>? SessionEnded;

    /// <summary>
    /// Runs the portable recipe and starts the game.
    /// </summary>
    /// <exception cref="InvalidOperationException">The plan cannot launch, a session is already running, or the
    /// same emulator executable is already running (German message for the UI).</exception>
    public IGameSession Start(LaunchPlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!plan.CanLaunch)
        {
            var reasons = plan.Problems.Where(p => p.Blocking).Select(p => p.Message).ToList();
            throw new InvalidOperationException(reasons.Count > 0
                ? "Das Spiel kann nicht gestartet werden: " + string.Join(" ", reasons)
                : "Das Spiel kann nicht gestartet werden: Es ist keine Startdatei festgelegt.");
        }

        GameSession session;
        lock (_gate)
        {
            if (_current is { State: SessionState.Starting or SessionState.Running })
                throw new InvalidOperationException("Es läuft bereits ein Spiel. Beende es zuerst, bevor du ein anderes startest.");

            // two copies of one emulator would overwrite each other's saves and config – and the portable recipe
            // must never write while the emulator runs (it rewrites its config on exit)
            var emuExe = plan.Emulator?.ExePath;
            if (!string.IsNullOrEmpty(emuExe) && ProcessLauncher.IsProgramRunning(emuExe))
            {
                var name = plan.Emulator?.Def.Name ?? Path.GetFileNameWithoutExtension(emuExe);
                throw new InvalidOperationException(
                    $"{name} läuft bereits. Bitte schließe das geöffnete {name}-Fenster zuerst – zwei gleichzeitig laufende Kopien könnten sich gegenseitig die Spielstände überschreiben.");
            }

            RunPortableSetup(plan);

            session = plan.UseShellExecute ? StartDetached(plan) : StartTracked(plan);
        }

        // events outside the lock (handlers may call back into the service)
        if (session.Detached)
        {
            // shown as started and ended right away; Current is never set
            Raise(SessionStarted, session, nameof(SessionStarted));
            session.FinishDetached();
            Raise(SessionEnded, session, nameof(SessionEnded));
        }
        else if (session.State == SessionState.Running)
        {
            Activate(session);
        }
        return session;
    }

    private void RunPortableSetup(LaunchPlan plan)
    {
        var id = plan.Emulator?.Def.Portable;
        if (string.IsNullOrEmpty(id) || plan.Emulator == null) return;
        var setup = _setups.FirstOrDefault(s => string.Equals(s.Id, id, StringComparison.OrdinalIgnoreCase));
        if (setup == null)
        {
            Log.Warn($"Portable recipe '{id}' for {plan.Emulator.Def.Id} not registered");
            return;
        }
        try
        {
            setup.Apply(plan.Emulator, plan, _paths);
        }
        catch (Exception ex)
        {
            Log.Error($"Portable recipe '{id}' failed (starting anyway)", ex);
        }
    }

    private GameSession StartDetached(LaunchPlan plan)
    {
        var now = DateTime.UtcNow;
        Process? process;
        try
        {
            var psi = new ProcessStartInfo(plan.FileName!)
            {
                UseShellExecute = true,
                Arguments = CommandLine.JoinArguments(plan.Arguments),
                WindowStyle = ShellWindowStyle,
            };
            if (!string.IsNullOrEmpty(plan.WorkingDirectory) && Directory.Exists(plan.WorkingDirectory))
                psi.WorkingDirectory = plan.WorkingDirectory;
            process = Process.Start(psi);
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException or FileNotFoundException or PlatformNotSupportedException)
        {
            Log.Error($"Shell start of '{plan.FileName}' failed", ex);
            return GameSession.CreateFailed(plan, $"„{plan.Game.DisplayTitle}“ konnte nicht gestartet werden: {ex.Message}");
        }

        var session = GameSession.CreateDetached(plan, process, now);
        Log.Info($"Detached (shell) start of game {plan.Game.Id}: {plan.FileName}");
        Record(new PlaySession
        {
            GameId = plan.Game.Id,
            EmulatorId = session.EmulatorId,
            StartUtc = now,
            EndUtc = now,
            Seconds = 0,
            ExitCode = null,
        });
        return session;
    }

    private GameSession StartTracked(LaunchPlan plan)
    {
        var workingDir = plan.WorkingDirectory;
        if (!string.IsNullOrEmpty(workingDir) && !Directory.Exists(workingDir))
        {
            Log.Warn($"Working directory '{workingDir}' does not exist – using the program's folder");
            workingDir = null;
        }
        var exe = ProcessLauncher.ResolveExecutable(plan.FileName!, workingDir);
        if (exe == null)
            return GameSession.CreateFailed(plan, $"Die Programmdatei wurde nicht gefunden: {plan.FileName}");
        workingDir ??= Path.GetDirectoryName(exe);

        LaunchedProcess launched;
        try
        {
            launched = ProcessLauncher.Start(exe, plan.Arguments, workingDir, plan.Environment,
                minimizedNoActivate: plan.Mode == LaunchMode.Embedded, ExtraCreationFlags);
        }
        catch (Exception ex) when (ex is Win32Exception or ArgumentException or IOException)
        {
            Log.Error($"Start of '{exe}' failed", ex);
            return GameSession.CreateFailed(plan, $"„{Path.GetFileName(exe)}“ konnte nicht gestartet werden: {ex.Message}");
        }

        var startUtc = DateTime.UtcNow;
        var session = new GameSession(plan, plan.Emulator?.Def.Id, startUtc, TimeSpan.Zero, launched.Process, launched.Handle,
            launched.ProcessId, launched.Job, exe);
        Log.Info($"Session started: game {plan.Game.Id} '{plan.Game.DisplayTitle}', pid {launched.ProcessId}, job {(launched.Job != null ? "yes" : "no")}, mode {plan.Mode}, exe {exe}");
        Register(session);
        return session;
    }

    /// <summary>Makes the session current (under the lock): clock running, crash-recovery file written.</summary>
    private void Register(GameSession session)
    {
        session.PollInterval = PollInterval;
        session.HeartbeatInterval = HeartbeatInterval;
        session.OnHeartbeat = s => WriteSessionFile(s, DateTime.UtcNow);
        session.OnFinishing = OnFinishing;
        session.OnEndedRaised = s => Raise(SessionEnded, s, nameof(SessionEnded));
        session.MarkRunning();
        _current = session;
        WriteSessionFile(session, DateTime.UtcNow);
    }

    /// <summary>Outside the lock: SessionStarted first, then the tracker (so SessionEnded always comes after it).</summary>
    private void Activate(GameSession session)
    {
        Raise(SessionStarted, session, nameof(SessionStarted));
        session.StartTracking();
    }

    private void OnFinishing(GameSession s)
    {
        long seconds = (long)Math.Round(s.Elapsed.TotalSeconds, MidpointRounding.AwayFromZero);
        if (seconds >= 1)
        {
            Record(new PlaySession
            {
                GameId = s.Plan.Game.Id,
                EmulatorId = s.EmulatorId,
                StartUtc = s.StartedUtc,
                EndUtc = s.EndedUtc ?? DateTime.UtcNow,
                Seconds = seconds,
                // a killed emulator has no meaningful exit code
                ExitCode = s.WasKilled ? null : s.ExitCode,
            });
        }
        DeleteSessionFile();
        lock (_gate)
        {
            if (ReferenceEquals(_current, s)) _current = null;
        }
    }

    private void Record(PlaySession session)
    {
        try
        {
            _repo.AddSession(session);
        }
        catch (Exception ex)
        {
            Log.Error($"Recording play session for game {session.GameId} failed", ex);
        }
    }

    private static void Raise(Action<IGameSession>? handlers, IGameSession session, string name)
    {
        if (handlers == null) return;
        foreach (var d in handlers.GetInvocationList())
        {
            try { ((Action<IGameSession>)d)(session); }
            catch (Exception ex) { Log.Error($"{name} handler threw", ex); }
        }
    }

    // ---- crash recovery ---------------------------------------------------------------------------------------

    internal sealed record SessionFileData
    {
        public long GameId { get; init; }
        public string? EmulatorId { get; init; }
        public int Pid { get; init; }
        public string ExePath { get; init; } = "";
        public DateTime StartUtc { get; init; }
        public DateTime HeartbeatUtc { get; init; }
        public LaunchMode? Mode { get; init; }
    }

    private void WriteSessionFile(GameSession s, DateTime heartbeatUtc)
    {
        var data = new SessionFileData
        {
            GameId = s.Plan.Game.Id,
            EmulatorId = s.EmulatorId,
            Pid = s.ProcessId,
            ExePath = s.ExePath ?? "",
            StartUtc = s.StartedUtc,
            HeartbeatUtc = heartbeatUtc,
            Mode = s.Plan.Mode,
        };
        try
        {
            lock (_fileGate)
            {
                if (s.State is SessionState.Exited or SessionState.Failed) return; // never resurrect a finished session's file
                SafeFile.WriteAllText(SessionFile, JsonSerializer.Serialize(data, Json));
            }
        }
        catch (Exception ex)
        {
            Log.Warn("Writing session.json failed", ex);
        }
    }

    private void DeleteSessionFile()
    {
        try
        {
            lock (_fileGate)
            {
                if (File.Exists(SessionFile)) File.Delete(SessionFile);
            }
        }
        catch (Exception ex)
        {
            Log.Warn("Deleting session.json failed", ex);
        }
    }

    /// <summary>
    /// Call once at startup. If data/cache/session.json exists (RetroHub ended while a game was running):
    /// if that process (same pid AND same exe path, started at the recorded time) is still running, it is re-attached
    /// (Current is set, SessionStarted raised, play time continues from the original start); otherwise the session is
    /// recorded with EndUtc = the last heartbeat. The file is then removed (re-attached sessions write a new one).
    /// Never throws.
    /// </summary>
    public void RecoverInterruptedSession()
    {
        try
        {
            if (!File.Exists(SessionFile)) return;
            lock (_gate)
            {
                if (_current is { State: SessionState.Starting or SessionState.Running })
                {
                    Log.Warn("RecoverInterruptedSession skipped: a session is already running");
                    return;
                }
            }

            SessionFileData? data = null;
            try
            {
                data = JsonSerializer.Deserialize<SessionFileData>(File.ReadAllText(SessionFile), Json);
            }
            catch (Exception ex) when (ex is JsonException or IOException or NotSupportedException or UnauthorizedAccessException)
            {
                Log.Warn("session.json unreadable – discarded", ex);
            }
            DeleteSessionFile();
            if (data == null) return;

            var startUtc = AsUtc(data.StartUtc);
            var heartbeatUtc = AsUtc(data.HeartbeatUtc);

            if (TryReattach(data, startUtc) is { } reattached)
            {
                Activate(reattached);
                return;
            }

            long seconds = (long)Math.Round((heartbeatUtc - startUtc).TotalSeconds, MidpointRounding.AwayFromZero);
            Log.Info($"Interrupted session of game {data.GameId} recovered: {seconds} s (until last heartbeat {heartbeatUtc:O})");
            if (seconds >= 1)
            {
                Record(new PlaySession
                {
                    GameId = data.GameId,
                    EmulatorId = data.EmulatorId,
                    StartUtc = startUtc,
                    EndUtc = heartbeatUtc,
                    Seconds = seconds,
                    ExitCode = null,
                });
            }
        }
        catch (Exception ex)
        {
            Log.Error("RecoverInterruptedSession failed", ex);
        }
    }

    private static DateTime AsUtc(DateTime t) => t.Kind switch
    {
        DateTimeKind.Utc => t,
        DateTimeKind.Local => t.ToUniversalTime(),
        _ => DateTime.SpecifyKind(t, DateTimeKind.Utc),
    };

    /// <summary>The re-attached (registered, not yet activated) session, or null when the game is not running any more.</summary>
    private GameSession? TryReattach(SessionFileData data, DateTime startUtc)
    {
        if (data.Pid <= 0 || string.IsNullOrWhiteSpace(data.ExePath)) return null;

        SafeProcessHandle handle = Native.OpenProcess(
            Native.PROCESS_QUERY_LIMITED_INFORMATION | Native.SYNCHRONIZE | Native.PROCESS_TERMINATE | Native.PROCESS_SET_QUOTA, false, data.Pid);
        if (handle.IsInvalid)
        {
            handle.Dispose();
            handle = Native.OpenProcess(Native.PROCESS_QUERY_LIMITED_INFORMATION | Native.SYNCHRONIZE, false, data.Pid);
        }
        if (handle.IsInvalid)
        {
            handle.Dispose();
            return null; // gone
        }

        bool keep = false;
        Process? process = null;
        JobObject? job = null;
        try
        {
            if (Native.WaitForSingleObject(handle, 0) == Native.WAIT_OBJECT_0) return null; // exited (zombie handle)
            var image = Native.ImagePathOf(handle);
            if (image == null || !SamePath(image, data.ExePath))
            {
                Log.Info($"session.json pid {data.Pid} now belongs to '{image ?? "?"}' – not the game");
                return null;
            }
            try
            {
                process = Process.GetProcessById(data.Pid);
                // pid reuse guard: the process must have been created around the recorded start
                var created = process.StartTime.ToUniversalTime();
                if (Math.Abs((created - startUtc).TotalSeconds) > 120)
                {
                    Log.Info($"session.json pid {data.Pid} was created at {created:O}, session started {startUtc:O} – a different process");
                    return null;
                }
            }
            catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or Win32Exception or NotSupportedException)
            {
                // start time unreadable: the exe path match has to do
            }

            // a new job follows every process the game starts from now on (earlier children cannot be adopted)
            job = JobObject.TryCreate();
            if (job != null && !job.TryAssign(handle, out int err))
            {
                Log.Info($"Re-attached process {data.Pid} could not join a job ({new Win32Exception(err).Message}) – tracking the main process");
                job.Dispose();
                job = null;
            }

            var game = SafeGet(data.GameId) ?? new Game { Id = data.GameId, SystemId = "", Path = "", Title = "" };
            var plan = new LaunchPlan
            {
                Game = game,
                System = new SystemDef { Id = game.SystemId, Name = game.SystemId },
                FileName = data.ExePath,
                Mode = data.Mode ?? LaunchMode.Windowed,
            };
            var session = new GameSession(plan, data.EmulatorId, startUtc, DateTime.UtcNow - startUtc, process, handle, data.Pid, job,
                data.ExePath, recovered: true);
            lock (_gate)
            {
                if (_current is { State: SessionState.Starting or SessionState.Running }) return null;
                keep = true;
                Log.Info($"Re-attached to running game {data.GameId} (pid {data.Pid}, started {startUtc:O})");
                Register(session);
            }
            return session;
        }
        finally
        {
            if (!keep)
            {
                job?.Dispose();
                process?.Dispose();
                handle.Dispose();
            }
        }
    }

    private Game? SafeGet(long id)
    {
        try { return _repo.Get(id); }
        catch (Exception ex) { Log.Warn($"Game {id} could not be loaded", ex); return null; }
    }

    private static bool SamePath(string a, string b)
    {
        try { return string.Equals(Path.GetFullPath(a), Path.GetFullPath(b), StringComparison.OrdinalIgnoreCase); }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException) { return false; }
    }

    /// <summary>Whether a program with exactly this path is running (by MainModule path). Never throws.</summary>
    public static bool IsProgramRunning(string exePath) => ProcessLauncher.IsProgramRunning(exePath);

    /// <summary>
    /// Stops tracking. A running game is NOT killed and session.json is kept, so the next start re-attaches or
    /// records it (<see cref="RecoverInterruptedSession"/>).
    /// </summary>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        GameSession? s;
        lock (_gate) s = _current;
        s?.StopTracking();
    }
}
