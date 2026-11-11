using RetroHub.Core.Catalog;
using RetroHub.Core.IO;
using RetroHub.Core.Logging;

namespace RetroHub.Core.Emulation.Portable;

public enum PortableApplyResult
{
    /// <summary>The recipe ran (files may or may not have changed — recipes are idempotent).</summary>
    Applied,
    /// <summary>The emulator exe is running; nothing was written (it would overwrite its config on exit).</summary>
    SkippedRunning,
    /// <summary>No exe to configure.</summary>
    NotInstalled,
    /// <summary>An error occurred and was logged; the game can still be started.</summary>
    Failed,
    /// <summary>No recipe with that id.</summary>
    NoRecipe,
    /// <summary>
    /// The exe lies outside the RetroHub folder (the player's own install, chosen on the Emulators page): RetroHub
    /// writes nothing into it – no portable markers, no config changes. It keeps its data where it always did.
    /// </summary>
    SkippedExternal,
}

/// <summary>What a recipe works with: absolute paths recomputed on every run, so a moved RetroHub folder repairs itself.</summary>
public sealed class PortableContext
{
    public EmulatorInstall Emulator { get; }
    public LaunchPlan Plan { get; }
    public AppPaths Paths { get; }
    public string Exe { get; }
    public string ExeDir { get; }
    /// <summary>The plan's system id when it is a safe folder name, else null (then the root folders are used).</summary>
    public string? SystemFolder { get; }
    public bool Embedded => Plan.Mode == LaunchMode.Embedded;
    public bool Fullscreen => Plan.Mode == LaunchMode.Fullscreen;

    /// <summary>Absolute path of the launched game file (the plan's game path resolved against the root), or null
    /// when there is none or it is not a usable file path (e.g. a URI).</summary>
    public string? GamePath
    {
        get
        {
            var raw = Plan.Game.Path;
            if (string.IsNullOrWhiteSpace(raw) || raw.Contains("://", StringComparison.Ordinal)) return null;
            try { return Paths.FromPortable(raw); }
            catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException) { return null; }
        }
    }

    internal PortableContext(EmulatorInstall emulator, LaunchPlan plan, AppPaths paths, string exe)
    {
        Emulator = emulator;
        Plan = plan;
        Paths = paths;
        Exe = Path.GetFullPath(exe);
        ExeDir = Path.GetDirectoryName(Exe)!;
        var sys = plan.System?.Id;
        SystemFolder = sys != null && AppPaths.IsSafeName(sys) ? sys : null;
    }

    private string PerSystem(string root) => Ensure(SystemFolder == null ? root : Path.Combine(root, SystemFolder));

    /// <summary>saves/&lt;system&gt; (created).</summary>
    public string SavesDir => PerSystem(Paths.Saves);
    /// <summary>savestates/&lt;system&gt; (created).</summary>
    public string SaveStatesDir => PerSystem(Paths.SaveStates);
    /// <summary>screenshots/&lt;system&gt; (created).</summary>
    public string ScreenshotsDir => PerSystem(Paths.Screenshots);
    /// <summary>games/&lt;system&gt; (created; used as the emulators' "open ROM" start folder).</summary>
    public string GamesDir => PerSystem(Paths.Games);

    /// <summary>Creates the folder (and parents) and returns its absolute path.</summary>
    public string Ensure(string dir)
    {
        var full = Path.GetFullPath(dir);
        Directory.CreateDirectory(full);
        return full;
    }

    /// <summary>Creates an empty marker file (portable.ini / portable.txt …) unless it exists already. True when created.</summary>
    public bool EnsureMarker(string fileName)
    {
        var path = Path.Combine(ExeDir, fileName);
        if (File.Exists(path)) return false;
        SafeFile.WriteAllText(path, "");
        Log.Info($"Portable: created {path}");
        return true;
    }
}

/// <summary>
/// Common frame of every recipe: no exe → nothing; exe running → skip (logged); any exception → logged,
/// never thrown to the caller (a broken emulator config must never stop a launch).
/// </summary>
public abstract class PortableSetupBase : IPortableSetup
{
    public abstract string Id { get; }

    /// <summary>German description of what the recipe does and where the emulator keeps its data (for the Emulators page).</summary>
    public abstract string Summary { get; }

    /// <summary>
    /// True when the recipe only writes RetroHub's own files (RetroArch's override in data/config) and may therefore
    /// also run for an emulator outside the RetroHub folder. All other recipes write next to the exe and skip such
    /// installs (<see cref="PortableApplyResult.SkippedExternal"/>).
    /// </summary>
    public virtual bool WritesOnlyRetroHubFiles => false;

    public void Apply(EmulatorInstall emulator, LaunchPlan plan, AppPaths paths) => Run(emulator, plan, paths);

    public PortableApplyResult Run(EmulatorInstall emulator, LaunchPlan plan, AppPaths paths)
    {
        try
        {
            var exe = emulator.ExePath;
            if (string.IsNullOrEmpty(exe) || !File.Exists(exe))
            {
                Log.Warn($"Portable setup '{Id}': emulator {emulator.Def.Id} is not installed, nothing to configure");
                return PortableApplyResult.NotInstalled;
            }
            if (!WritesOnlyRetroHubFiles && !AppPaths.IsInside(exe, paths.Root))
            {
                Log.Info($"Portable setup '{Id}' skipped: {exe} is outside the RetroHub folder (the player's own install is left untouched)");
                return PortableApplyResult.SkippedExternal;
            }
            if (EmulatorProcesses.IsRunning(exe))
            {
                Log.Info($"Portable setup '{Id}' skipped: {exe} is running (it rewrites its config on exit)");
                return PortableApplyResult.SkippedRunning;
            }
            Configure(new PortableContext(emulator, plan, paths, exe));
            return PortableApplyResult.Applied;
        }
        catch (Exception ex)
        {
            Log.Warn($"Portable setup '{Id}' for {emulator.Def.Id} failed", ex);
            return PortableApplyResult.Failed;
        }
    }

    protected abstract void Configure(PortableContext ctx);

    /// <summary>Logs which file was written (only when it changed).</summary>
    protected static void Saved(string id, string path, bool written)
    {
        if (written) Log.Info($"Portable setup '{id}': updated {path}");
        else Log.Debug($"Portable setup '{id}': {path} already up to date");
    }
}
