using System.Text.RegularExpressions;
using RetroHub.Core.Catalog;
using RetroHub.Core.Config;
using RetroHub.Core.Library;
using RetroHub.Core.Logging;

namespace RetroHub.Core.Emulation;

/// <summary>
/// Builds a <see cref="LaunchPlan"/> for a game. Pure planning: reads files and settings, starts nothing,
/// writes nothing. Never throws for bad data — every issue becomes a <see cref="LaunchProblem"/>.
/// <para>Emulator choice: explicit override &gt; <see cref="Game.EmulatorOverride"/> &gt; settings.SystemEmulator[system]
/// &gt; first installed entry of <see cref="ChoicesFor"/> (RetroArch entries only count when the core dll exists)
/// &gt; the first entry (then EmulatorMissing / CoreMissing). Override ids unknown to the catalog are skipped (logged).</para>
/// <para>Mode: override &gt; <see cref="Game.LaunchModeOverride"/> &gt; settings.EmulatorMode[emulator] &gt;
/// <see cref="EmulatorDef.DefaultMode"/>; must be one of <see cref="EmulatorDef.Modes"/>, else the first of them.</para>
/// </summary>
public sealed class LaunchPlanner : ILaunchPlanner
{
    private static readonly Regex UriScheme = new(@"^[a-zA-Z][a-zA-Z0-9+.\-]{1,30}://", RegexOptions.Compiled);
    private static readonly string[] ShortcutExtensions = [".lnk", ".url"];
    private static readonly string[] ExecutableExtensions = [".exe", ".bat", ".cmd", ".com"];

    private readonly ICatalog _catalog;
    private readonly IEmulatorRegistry _registry;
    private readonly AppPaths _paths;
    private readonly SettingsStore _settings;

    public LaunchPlanner(ICatalog catalog, IEmulatorRegistry registry, AppPaths paths, SettingsStore settings)
    {
        _catalog = catalog;
        _registry = registry;
        _paths = paths;
        _settings = settings;
    }

    public LaunchPlan Plan(Game game, string? emulatorOverride = null, LaunchMode? modeOverride = null)
    {
        try
        {
            return PlanCore(game, emulatorOverride, modeOverride);
        }
        catch (Exception ex)
        {
            Log.Error($"Planning '{game.Path}' failed", ex);
            SystemDef? system = null;
            try { system = _catalog.GetSystem(game.SystemId); } catch (Exception) { /* the catalog itself is broken */ }
            return new LaunchPlan
            {
                Game = game,
                System = system ?? UnknownSystem(game.SystemId),
                Problems = [new LaunchProblem(LaunchProblemKind.Other, $"Der Start konnte nicht vorbereitet werden: {ex.Message}", true)],
            };
        }
    }

    private static SystemDef UnknownSystem(string id) => new() { Id = id, Name = id };

    private LaunchPlan PlanCore(Game game, string? emulatorOverride, LaunchMode? modeOverride)
    {
        var problems = new List<LaunchProblem>();
        var system = _catalog.GetSystem(game.SystemId);
        if (system == null)
        {
            return new LaunchPlan
            {
                Game = game,
                System = UnknownSystem(game.SystemId),
                Problems = [new LaunchProblem(LaunchProblemKind.NoEmulatorForSystem,
                    $"Das System „{game.SystemId}“ ist unbekannt (keine Systemdefinition gefunden).", true)],
            };
        }

        // ---- the game file ----
        string rawPath = game.Path ?? "";
        bool isUri = UriScheme.IsMatch(rawPath) && !Path.IsPathRooted(rawPath);
        string? fullPath = null;
        if (!isUri)
        {
            try { fullPath = _paths.FromPortable(rawPath); }
            catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
            {
                problems.Add(new LaunchProblem(LaunchProblemKind.FileMissing, $"Ungültiger Dateipfad: {rawPath}", true));
            }
        }
        var ext = fullPath == null ? "" : Path.GetExtension(fullPath).ToLowerInvariant();
        bool isShortcut = isUri || game.Kind == GameKind.Shortcut || ShortcutExtensions.Contains(ext);
        bool isExecutable = !isShortcut && (game.Kind == GameKind.Executable || (system.Category == SystemCategory.Pc && ExecutableExtensions.Contains(ext)));

        var nativeDef = NativeDef(system);
        bool native = isShortcut || isExecutable || system.Category == SystemCategory.Pc || nativeDef is { Chosen: true };

        if (fullPath != null && !File.Exists(fullPath))
        {
            if (Directory.Exists(fullPath))
            {
                // folder games (e.g. PS3) may be directories when an emulator loads them; a PC game needs its exe
                if (native)
                    problems.Add(new LaunchProblem(LaunchProblemKind.FileMissing,
                        $"Für „{game.DisplayTitle}“ ist kein Startprogramm (.exe) hinterlegt: {fullPath}", true));
                else if (!system.FolderGames)
                    problems.Add(new LaunchProblem(LaunchProblemKind.FileMissing, $"Die Spieldatei wurde nicht gefunden: {fullPath}", true));
            }
            else
            {
                problems.Add(new LaunchProblem(LaunchProblemKind.FileMissing, $"Die Spieldatei wurde nicht gefunden: {fullPath}", true));
            }
        }

        // ---- native PC games / shortcuts ----
        if (native)
            return PlanNative(game, system, fullPath, rawPath, isUri, isShortcut, nativeDef?.Def, modeOverride, problems);

        // ---- emulators ----
        var choice = ChooseEmulator(game, system, emulatorOverride);
        if (choice == null)
        {
            problems.Add(new LaunchProblem(LaunchProblemKind.NoEmulatorForSystem,
                $"Für {system.Name} ist kein Emulator eingetragen.", true));
            return new LaunchPlan { Game = game, System = system, AspectRatio = system.AspectRatio, Problems = problems };
        }
        var (def, install, core) = choice.Value;
        var mode = ChooseMode(def, game, modeOverride);

        string? exe = install?.IsInstalled == true ? install.ExePath : null;
        if (exe == null)
            problems.Add(new LaunchProblem(LaunchProblemKind.EmulatorMissing, MissingEmulatorMessage(def), true));

        string? corePath = null;
        if (def.Kind == EmulatorKind.RetroArch)
        {
            if (core == null)
            {
                problems.Add(new LaunchProblem(LaunchProblemKind.CoreMissing,
                    $"Für {system.Name} ist in RetroArch kein Core eingetragen.", true));
            }
            else if (!AppPaths.IsSafeName(core + "_libretro.dll"))
            {
                problems.Add(new LaunchProblem(LaunchProblemKind.CoreMissing, $"Ungültiger Core-Name „{core}“.", true));
                core = null;
            }
            else if (exe != null)
            {
                var coresDir = Path.Combine(Path.GetDirectoryName(exe)!, "cores");
                corePath = Path.Combine(coresDir, core + "_libretro.dll");
                if (!File.Exists(corePath))
                    problems.Add(new LaunchProblem(LaunchProblemKind.CoreMissing,
                        $"Der RetroArch-Core „{core}_libretro.dll“ fehlt. Lade ihn in RetroArch herunter (Online-Updater → Core-Downloader) " +
                        $"oder lege ihn in den Ordner „cores“ von RetroArch: {coresDir}", true));
            }
        }

        // ---- BIOS ----
        var biosStatuses = BiosCheck.Check(system, _paths.Bios);
        problems.AddRange(BiosCheck.ProblemsFor(system, biosStatuses));
        if (def.Kind == EmulatorKind.RetroArch)
        {
            // RetroArch's system_directory is bios/ itself; it does not look into subfolders
            // (a BiosFile with a subpath such as "dc/dc_boot.bin" is expected in that subfolder)
            foreach (var s in biosStatuses.Where(s => s.FoundPath != null && s.State != BiosState.Missing))
            {
                var dir = Path.GetDirectoryName(s.FoundPath!)!.TrimEnd('\\', '/');
                var expected = Path.GetDirectoryName(Path.GetFullPath(Path.Combine(_paths.Bios, s.File.File)))!.TrimEnd('\\', '/');
                if (!string.Equals(dir, expected, StringComparison.OrdinalIgnoreCase))
                    problems.Add(new LaunchProblem(LaunchProblemKind.BiosMissing,
                        $"Die BIOS-Datei „{s.File.File}“ liegt in einem Unterordner ({s.FoundPath}). RetroArch sucht sie direkt im Ordner „bios/“.", false));
            }
        }

        if (exe != null && EmulatorProcesses.IsRunning(exe))
            problems.Add(new LaunchProblem(LaunchProblemKind.AlreadyRunning,
                $"{def.Name} läuft bereits. Zwei Instanzen können sich gegenseitig die Spielstände überschreiben.", false));

        // ---- arguments ----
        var ctx = ArgumentContext.For(_paths, system.Id, fullPath, exe, corePath, mode, def.FullscreenArgs);
        var args = new List<string>(ArgumentTemplate.Expand(def.Args, ctx));
        // settings dictionaries are null when settings.json says e.g. "emulatorExtraArgs": null (hand-edited)
        if (_settings.Current.EmulatorExtraArgs?.TryGetValue(def.Id, out var extra) == true && extra is { Length: > 0 })
            args.AddRange(ArgumentTemplate.Expand(extra, ctx));

        return new LaunchPlan
        {
            Game = game,
            System = system,
            Emulator = install ?? new EmulatorInstall { Def = def },
            Core = def.Kind == EmulatorKind.RetroArch ? core : null,
            Mode = mode,
            FileName = exe,
            Arguments = args,
            WorkingDirectory = exe == null ? null : Path.GetDirectoryName(exe),
            Embed = mode == LaunchMode.Embedded ? def.Embed : null,
            AspectRatio = system.AspectRatio,
            Problems = problems,
        };
    }

    private LaunchPlan PlanNative(Game game, SystemDef system, string? fullPath, string rawPath, bool isUri, bool isShortcut,
        EmulatorDef? nativeDef, LaunchMode? modeOverride, List<LaunchProblem> problems)
    {
        var def = nativeDef ?? new EmulatorDef
        {
            Id = "native",
            Name = "Windows",
            Kind = EmulatorKind.Native,
            Modes = [LaunchMode.Fullscreen, LaunchMode.Windowed],
            DefaultMode = LaunchMode.Fullscreen,
        };
        var mode = ChooseMode(def, game, modeOverride);
        if (mode == LaunchMode.Embedded)
        {
            // a native game is never re-parented unless its definition explicitly allows it
            if (def.Embed == null) mode = def.Modes.FirstOrDefault(m => m != LaunchMode.Embedded, LaunchMode.Fullscreen);
        }
        var args = string.IsNullOrWhiteSpace(game.ExtraArgs) ? [] : ArgumentTemplate.SplitCommandLine(game.ExtraArgs);

        string? fileName;
        string? workDir;
        if (isShortcut)
        {
            fileName = isUri ? rawPath : fullPath;
            workDir = isUri || fullPath == null ? null : Path.GetDirectoryName(fullPath);
            if (isUri) args = []; // a URL takes no argv
        }
        else
        {
            fileName = fullPath;
            workDir = fullPath == null ? null : Path.GetDirectoryName(fullPath);
        }
        if (!isShortcut && fullPath != null && File.Exists(fullPath) && !ExecutableExtensions.Contains(Path.GetExtension(fullPath).ToLowerInvariant()))
            problems.Add(new LaunchProblem(LaunchProblemKind.Other,
                $"„{Path.GetFileName(fullPath)}“ ist kein startbares Programm (.exe/.bat/.cmd) und keine Verknüpfung.", true));

        return new LaunchPlan
        {
            Game = game,
            System = system,
            Emulator = new EmulatorInstall { Def = def, ExePath = isShortcut ? null : fullPath },
            Mode = mode,
            FileName = fileName,
            Arguments = args,
            WorkingDirectory = workDir,
            UseShellExecute = isShortcut,
            Embed = mode == LaunchMode.Embedded ? def.Embed : null,
            AspectRatio = system.AspectRatio,
            Problems = problems,
        };
    }

    /// <summary>A Native-kind emulator listed for the system (Chosen = it is the system's first entry).</summary>
    private (EmulatorDef Def, bool Chosen)? NativeDef(SystemDef system)
    {
        for (int i = 0; i < system.Emulators.Length; i++)
        {
            var r = EmulatorRef.Parse(system.Emulators[i]);
            if (_catalog.GetEmulator(r.EmulatorId) is { Kind: EmulatorKind.Native } d) return (d, i == 0);
        }
        return null;
    }

    private string MissingEmulatorMessage(EmulatorDef def)
    {
        var exe = def.Exe.FirstOrDefault(p => !string.IsNullOrWhiteSpace(p) && p.IndexOfAny(['*', '?']) < 0);
        var where = $"emulators/{def.Id}/";
        var msg = $"{def.Name} ist nicht installiert. Entpacke den Emulator in den Ordner „{where}“" +
                  (exe != null ? $" (RetroHub sucht dort nach „{exe}“)" : "") +
                  " oder wähle die .exe auf der Emulator-Seite aus.";
        if (!string.IsNullOrWhiteSpace(def.DownloadUrl)) msg += $" Download: {def.DownloadUrl}";
        return msg;
    }

    private LaunchMode ChooseMode(EmulatorDef def, Game game, LaunchMode? modeOverride)
    {
        LaunchMode wanted;
        if (modeOverride is { } m) wanted = m;
        else if (game.LaunchModeOverride is { } g) wanted = g;
        else if (_settings.Current.EmulatorMode?.TryGetValue(def.Id, out var s) == true) wanted = s;
        else wanted = def.DefaultMode;
        var modes = def.Modes is { Length: > 0 } ? def.Modes : [LaunchMode.Fullscreen, LaunchMode.Windowed];
        if (modes.Contains(wanted)) return wanted;
        Log.Debug($"{def.Id}: mode {wanted} not supported, using {modes[0]}");
        return modes[0];
    }

    /// <summary>Picks (definition, install, core) following the precedence; null when the system has no choices at all.</summary>
    private (EmulatorDef Def, EmulatorInstall? Install, string? Core)? ChooseEmulator(Game game, SystemDef system, string? explicitOverride)
    {
        foreach (var (source, value) in new[]
                 {
                     ("override", explicitOverride),
                     ("game", game.EmulatorOverride),
                     ("settings", _settings.Current.SystemEmulator?.TryGetValue(system.Id, out var s) == true ? s : null),
                 })
        {
            if (string.IsNullOrWhiteSpace(value)) continue;
            var r = EmulatorRef.Parse(value);
            var def = _catalog.GetEmulator(r.EmulatorId);
            if (def == null)
            {
                Log.Warn($"Emulator '{value}' ({source}) is not in the catalog, ignoring it");
                continue;
            }
            var install = _registry.Get(def.Id);
            return (def, install, def.Kind == EmulatorKind.RetroArch ? r.Core ?? PickCore(def, install, system.Id) : null);
        }

        // entries whose id is unknown to the catalog (removed plugin) carry no install and are never chosen
        var choices = ChoicesFor(system.Id).Where(c => c.Install != null).ToList();
        if (choices.Count == 0) return null;
        foreach (var (r, install) in choices)
            if (IsUsable(r, install)) return (install!.Def, install, r.Core);
        var (first, firstInstall) = choices[0];
        return (firstInstall!.Def, firstInstall, first.Core);
    }

    private static bool IsUsable(EmulatorRef r, EmulatorInstall? install)
    {
        if (install is not { IsInstalled: true }) return false;
        if (install.Def.Kind != EmulatorKind.RetroArch) return true;
        return r.Core != null && CoreInstalled(install, r.Core);
    }

    private static bool CoreInstalled(EmulatorInstall install, string core)
    {
        if (install.ExePath == null || !AppPaths.IsSafeName(core + "_libretro.dll")) return false;
        return File.Exists(Path.Combine(Path.GetDirectoryName(install.ExePath)!, "cores", core + "_libretro.dll"));
    }

    /// <summary>For "retroarch" without a core: the first mapped core that is installed, else the first mapped one.</summary>
    private static string? PickCore(EmulatorDef def, EmulatorInstall? install, string systemId)
    {
        var cores = CoresFor(def, systemId);
        if (cores.Length == 0) return null;
        if (install is { IsInstalled: true })
            foreach (var c in cores)
                if (CoreInstalled(install, c)) return c;
        return cores[0];
    }

    private static string[] CoresFor(EmulatorDef def, string systemId)
    {
        foreach (var (k, v) in def.Cores)
            if (string.Equals(k, systemId, StringComparison.OrdinalIgnoreCase)) return v ?? [];
        return [];
    }

    /// <summary>
    /// The system's <see cref="SystemDef.Emulators"/> in order ("retroarch" without core → one entry per mapped
    /// core), followed by any other catalog emulator that lists the system in <see cref="EmulatorDef.Systems"/>
    /// (RetroArch: one entry per mapped core). Native-kind entries are left out (PC games need no emulator).
    /// </summary>
    public IReadOnlyList<(EmulatorRef Ref, EmulatorInstall? Install)> ChoicesFor(string systemId)
    {
        var result = new List<(EmulatorRef, EmulatorInstall?)>();
        try
        {
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var system = _catalog.GetSystem(systemId);

            void AddRef(EmulatorRef r)
            {
                var def = _catalog.GetEmulator(r.EmulatorId);
                if (def?.Kind == EmulatorKind.Native) return;
                if (def?.Kind == EmulatorKind.RetroArch && r.Core == null)
                {
                    foreach (var c in CoresFor(def, systemId)) AddRef(new EmulatorRef(def.Id, c));
                    return;
                }
                if (def != null && def.Kind != EmulatorKind.RetroArch) r = new EmulatorRef(def.Id, null);
                if (!seen.Add(r.ToString())) return;
                result.Add((r, def == null ? null : _registry.Get(def.Id) ?? new EmulatorInstall { Def = def }));
            }

            if (system != null)
                foreach (var e in system.Emulators)
                    if (!string.IsNullOrWhiteSpace(e)) AddRef(EmulatorRef.Parse(e));
            foreach (var def in _catalog.Emulators)
                if (def.Systems.Contains(systemId, StringComparer.OrdinalIgnoreCase))
                    AddRef(new EmulatorRef(def.Id, null));
        }
        catch (Exception ex)
        {
            Log.Error($"ChoicesFor({systemId}) failed", ex);
        }
        return result;
    }
}
