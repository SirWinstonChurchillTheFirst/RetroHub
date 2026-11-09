using RetroHub.App.Localization;
using RetroHub.Core;
using RetroHub.Core.Catalog;
using RetroHub.Core.Emulation;
using RetroHub.Core.Library;

namespace RetroHub.App.Services.Demo;

/// <summary>Demo registry: some emulators "installed" (paths below emulators/, not on disk), some missing.</summary>
public sealed class DemoEmulatorRegistry : IEmulatorRegistry
{
    private readonly ICatalog _catalog;
    private readonly AppPaths _paths;
    private readonly Dictionary<string, string> _userPaths = new(StringComparer.OrdinalIgnoreCase);
    private IReadOnlyList<EmulatorInstall> _all = [];

    private static readonly Dictionary<string, (string Version, string[] Cores)> Installed = new(StringComparer.OrdinalIgnoreCase)
    {
        ["retroarch"] = ("1.21.0", ["mesen", "snes9x", "gambatte", "mgba", "genesis_plus_gx"]),
        ["mgba"] = ("0.10.5", []),
        ["melonds"] = ("1.0", []),
        ["azahar"] = ("2122.1", []),
        ["duckstation"] = ("0.1-9226", []),
        ["ppsspp"] = ("1.19.3", []),
    };

    public event Action? Changed;

    public DemoEmulatorRegistry(ICatalog catalog, AppPaths paths)
    {
        _catalog = catalog;
        _paths = paths;
        Refresh();
    }

    public IReadOnlyList<EmulatorInstall> All => _all;

    public EmulatorInstall? Get(string emulatorId) =>
        _all.FirstOrDefault(e => string.Equals(e.Def.Id, emulatorId, StringComparison.OrdinalIgnoreCase));

    public void Refresh()
    {
        _all = _catalog.Emulators.Select(def =>
        {
            if (_userPaths.TryGetValue(def.Id, out var user))
                return new EmulatorInstall { Def = def, ExePath = user, UserConfigured = true, Version = null };
            if (!Installed.TryGetValue(def.Id, out var info)) return new EmulatorInstall { Def = def };
            var exe = def.Exe.FirstOrDefault()?.Replace("*", "") ?? def.Id + ".exe";
            return new EmulatorInstall
            {
                Def = def,
                ExePath = Path.Combine(_paths.Emulators, def.Id, exe),
                Version = info.Version,
                InstalledCores = info.Cores,
            };
        }).ToList();
        try { Changed?.Invoke(); } catch { }
    }

    public void SetUserPath(string emulatorId, string? exePath)
    {
        if (exePath == null) _userPaths.Remove(emulatorId); else _userPaths[emulatorId] = exePath;
        Refresh();
    }
}

/// <summary>
/// Demo planner with the same rules a real planner applies: emulator installed? core present? BIOS? file present?
/// Problems are reported honestly (German, user-facing).
/// </summary>
public sealed class DemoLaunchPlanner(ICatalog catalog, DemoEmulatorRegistry registry, AppPaths paths) : ILaunchPlanner
{
    public IReadOnlyList<(EmulatorRef Ref, EmulatorInstall? Install)> ChoicesFor(string systemId)
    {
        var sys = catalog.GetSystem(systemId);
        if (sys == null) return [];
        return sys.Emulators.Select(EmulatorRef.Parse).Select(r => (r, registry.Get(r.EmulatorId))).ToList();
    }

    public LaunchPlan Plan(Game game, string? emulatorOverride = null, LaunchMode? modeOverride = null)
    {
        var sys = catalog.GetSystem(game.SystemId) ?? new SystemDef { Id = game.SystemId, Name = game.SystemId };
        var problems = new List<LaunchProblem>();
        var choices = ChoicesFor(sys.Id);
        var wanted = emulatorOverride ?? game.EmulatorOverride;

        (EmulatorRef Ref, EmulatorInstall? Install) choice;
        if (wanted != null)
        {
            var r = EmulatorRef.Parse(wanted);
            choice = (r, registry.Get(r.EmulatorId));
        }
        else
        {
            // first installed choice (with its core), else the first preference
            choice = choices.FirstOrDefault(c => c.Install?.ExePath != null &&
                         (c.Ref.Core == null || c.Install.InstalledCores.Contains(c.Ref.Core)));
            if (choice.Install == null && choices.Count > 0) choice = choices[0];
        }

        if (choices.Count == 0 && wanted == null)
            problems.Add(new LaunchProblem(LaunchProblemKind.NoEmulatorForSystem, Loc.T("problem.noEmulatorForSystem", sys.Name), true));
        else if (choice.Install?.ExePath == null)
        {
            var name = choice.Install?.Def.Name ?? catalog.GetEmulator(choice.Ref.EmulatorId)?.Name ?? choice.Ref.EmulatorId;
            problems.Add(new LaunchProblem(LaunchProblemKind.EmulatorMissing, Loc.T("problem.emulatorMissing", name, choice.Ref.EmulatorId), true));
        }
        else if (choice.Ref.Core != null && !choice.Install.InstalledCores.Contains(choice.Ref.Core))
            problems.Add(new LaunchProblem(LaunchProblemKind.CoreMissing, Loc.T("problem.coreMissing", choice.Ref.Core), true));

        foreach (var bios in sys.Bios)
            problems.Add(new LaunchProblem(LaunchProblemKind.BiosMissing,
                Loc.T(bios.Required ? "problem.biosMissingRequired" : "problem.biosMissing", bios.File), bios.Required));

        if (game.Status == GameStatus.Missing)
            problems.Add(new LaunchProblem(LaunchProblemKind.FileMissing, Loc.T("problem.fileMissing", game.Path), true));

        var def = choice.Install?.Def ?? catalog.GetEmulator(choice.Ref.EmulatorId);
        var mode = modeOverride ?? game.LaunchModeOverride ?? def?.DefaultMode ?? LaunchMode.Fullscreen;
        if (def != null && !def.Modes.Contains(mode)) mode = def.DefaultMode;

        var rom = paths.FromPortable(game.Path);
        return new LaunchPlan
        {
            Game = game,
            System = sys,
            Emulator = choice.Install,
            Core = choice.Ref.Core,
            Mode = mode,
            FileName = choice.Install?.ExePath,
            Arguments = choice.Ref.Core != null ? ["-L", $"cores/{choice.Ref.Core}_libretro.dll", rom] : [rom],
            WorkingDirectory = choice.Install?.ExePath is { } exe ? Path.GetDirectoryName(exe) : null,
            Embed = def?.Embed,
            AspectRatio = sys.AspectRatio,
            Problems = problems,
        };
    }
}
