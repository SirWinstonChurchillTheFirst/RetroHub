using System.Diagnostics;
using RetroHub.Core.Catalog;
using RetroHub.Core.Library;

namespace RetroHub.Core.Emulation;

/// <summary>An emulator definition plus where (and whether) it is installed.</summary>
public sealed record EmulatorInstall
{
    public required EmulatorDef Def { get; init; }
    /// <summary>Full path of the exe; null = not installed.</summary>
    public string? ExePath { get; init; }
    public string? Version { get; init; }
    /// <summary>True when the path was configured by the user (settings) rather than auto-detected.</summary>
    public bool UserConfigured { get; init; }
    /// <summary>RetroArch only: cores found in its cores/ folder (names without "_libretro.dll").</summary>
    public IReadOnlyList<string> InstalledCores { get; init; } = [];
    public bool IsInstalled => ExePath != null && File.Exists(ExePath);
}

/// <summary>Finds and describes installed emulators. Detection: emulators/&lt;id&gt;/** (depth 3), then user-configured paths.</summary>
public interface IEmulatorRegistry
{
    IReadOnlyList<EmulatorInstall> All { get; }
    EmulatorInstall? Get(string emulatorId);
    /// <summary>Re-scans the emulators/ folder (fast; called at startup and from the Emulators page).</summary>
    void Refresh();
    /// <summary>Sets a user path for an emulator (stored portably in settings) and refreshes it. Null clears it.</summary>
    void SetUserPath(string emulatorId, string? exePath);
    event Action? Changed;
}

public enum LaunchProblemKind { EmulatorMissing, CoreMissing, BiosMissing, FileMissing, NoEmulatorForSystem, AlreadyRunning, Other }

public sealed record LaunchProblem(LaunchProblemKind Kind, string Message, bool Blocking);

/// <summary>Everything needed to start one game; produced by <see cref="ILaunchPlanner"/>, consumed by the session service.</summary>
public sealed record LaunchPlan
{
    public required Game Game { get; init; }
    public required SystemDef System { get; init; }
    public EmulatorInstall? Emulator { get; init; }
    public string? Core { get; init; }
    public LaunchMode Mode { get; init; }
    public string? FileName { get; init; }
    /// <summary>One entry per argv element (use ProcessStartInfo.ArgumentList, never string concatenation).</summary>
    public IReadOnlyList<string> Arguments { get; init; } = [];
    public string? WorkingDirectory { get; init; }
    public IReadOnlyDictionary<string, string> Environment { get; init; } = new Dictionary<string, string>();
    /// <summary>
    /// Start via the Windows shell (ShellExecute) instead of CreateProcess: .lnk shortcuts and URLs (steam://…).
    /// Such sessions usually cannot be tracked (the shell hands off to another program) → no playtime, only a launch count.
    /// </summary>
    public bool UseShellExecute { get; init; }
    public EmbedProfile? Embed { get; init; }
    public double? AspectRatio { get; init; }
    public IReadOnlyList<LaunchProblem> Problems { get; init; } = [];
    public bool CanLaunch => FileName != null && Problems.All(p => !p.Blocking);
}

public interface ILaunchPlanner
{
    /// <summary>Builds the plan (does not start anything, no side effects except reading files).</summary>
    LaunchPlan Plan(Game game, string? emulatorOverride = null, LaunchMode? modeOverride = null);
    /// <summary>All emulator choices for a system ("mgba", "retroarch/mgba", …) with install state, in preference order.</summary>
    IReadOnlyList<(EmulatorRef Ref, EmulatorInstall? Install)> ChoicesFor(string systemId);
}

/// <summary>
/// A per-emulator "portable recipe": writes the emulator's own config so that saves/states/screenshots/BIOS
/// go to RetroHub's folders and it behaves well for the chosen launch mode. Idempotent; runs before each launch.
/// Must never write while that emulator is running (emulators rewrite their config on exit).
/// </summary>
public interface IPortableSetup
{
    /// <summary>Matches EmulatorDef.Portable.</summary>
    string Id { get; }
    void Apply(EmulatorInstall emulator, LaunchPlan plan, AppPaths paths);
}

public enum SessionState { Starting, Running, Exited, Failed }

/// <summary>A running game. Created by <see cref="ISessionService.Start"/>.</summary>
public interface IGameSession
{
    LaunchPlan Plan { get; }
    SessionState State { get; }
    /// <summary>The emulator's main process (null for shell-executed shortcuts that we cannot track).</summary>
    Process? Process { get; }
    DateTime StartedUtc { get; }
    /// <summary>Elapsed play time so far (or total, once exited).</summary>
    TimeSpan Elapsed { get; }
    int? ExitCode { get; }
    string? Error { get; }
    /// <summary>Raised once when every process of the session has exited (any thread).</summary>
    event Action<IGameSession>? Ended;
    /// <summary>Asks the emulator to close (WM_CLOSE to its windows). Returns false if it does not exit within the timeout.</summary>
    Task<bool> RequestCloseAsync(TimeSpan timeout);
    /// <summary>Kills the whole process tree (Job Object). Use only after RequestCloseAsync failed / user confirmed.</summary>
    void Kill();
    /// <summary>Top-level windows currently owned by the session's processes (for embedding / focusing).</summary>
    IReadOnlyList<nint> FindWindows();
}

/// <summary>
/// Starts games, tracks their processes (Job Object, so launcher stubs and child processes are followed and
/// nothing is orphaned), records play sessions in the repository when they end. One session at a time.
/// </summary>
public interface ISessionService
{
    IGameSession? Current { get; }
    /// <summary>Runs the portable recipe, starts the process. Throws <see cref="InvalidOperationException"/> if !plan.CanLaunch.</summary>
    IGameSession Start(LaunchPlan plan);
    event Action<IGameSession>? SessionStarted;
    event Action<IGameSession>? SessionEnded;
}
