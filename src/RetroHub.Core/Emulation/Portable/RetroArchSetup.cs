using RetroHub.Core.Catalog;
using RetroHub.Core.Emulation.ConfigFiles;

namespace RetroHub.Core.Emulation.Portable;

/// <summary>
/// RetroArch: writes ONLY RetroHub's override file data/config/retroarch/retrohub.cfg (passed with
/// <c>--appendconfig {retroarchOverride}</c> in the emulator definition). The user's retroarch.cfg is never
/// read or written.
/// <list type="bullet">
/// <item>system/savefile/savestate/screenshot directories → bios/, saves/, savestates/, screenshots/.</item>
/// <item>sort_*_by_content_enable = true: RetroArch puts files into a subfolder named after the content's folder,
/// so games/gba/x.gba saves to saves/gba/x.srm. (For external library folders the subfolder is that folder's name.)
/// sort_savefiles_enable / sort_savestates_enable (by core name) = false, so it is not saves/gba/mGBA/.</item>
/// <item>*_in_content_dir = false, otherwise RetroArch would ignore the directories above.</item>
/// <item>video_fullscreen per launch mode, video_windowed_fullscreen = true (borderless, fast alt-tab).</item>
/// <item>pause_nonactive = false only when embedded (the RetroHub window has the focus then); otherwise the
/// user's own setting applies (the key is removed from the override).</item>
/// <item>quit_press_twice = false: RetroHub's exit/close must not need a second press.</item>
/// <item>config_save_on_exit = false — deliberate: with --appendconfig RetroArch merges the override into its
/// settings and, with save-on-exit, would write RetroHub's absolute paths and the per-launch fullscreen flag
/// into the user's retroarch.cfg on exit. That is exactly what must not happen. Trade-off: changes made in the
/// RetroArch menu during a RetroHub session are not saved automatically; core options, remaps and per-core
/// overrides are separate files and are still saved by RetroArch as usual.</item>
/// </list>
/// </summary>
public sealed class RetroArchSetup : PortableSetupBase
{
    public override string Id => "retroarch";

    public override string Summary =>
        "RetroHub schreibt nur seine eigene Zusatzkonfiguration (data/config/retroarch/retrohub.cfg), nie deine retroarch.cfg. " +
        "Spielstände landen in saves/<System>, Savestates in savestates/<System>, Screenshots in screenshots/<System>, " +
        "BIOS-Dateien werden in bios/ gesucht. Menü-Änderungen während einer RetroHub-Sitzung speichert RetroArch nicht automatisch.";

    protected override void Configure(PortableContext ctx)
    {
        var p = ctx.Paths;
        var bios = ctx.Ensure(p.Bios);
        var saves = ctx.Ensure(p.Saves);
        var states = ctx.Ensure(p.SaveStates);
        var shots = ctx.Ensure(p.Screenshots);
        // RetroArch creates the per-content subfolder itself; creating the system's one up front does no harm
        _ = ctx.SavesDir;
        _ = ctx.SaveStatesDir;
        _ = ctx.ScreenshotsDir;

        var file = ArgumentContext.RetroArchOverridePath(p);
        var cfg = RetroArchConfig.Load(file);
        cfg.EnsureComment("RetroHub override, loaded with --appendconfig. Rewritten before every launch.");
        cfg.Set("system_directory", bios);
        cfg.Set("savefile_directory", saves);
        cfg.Set("savestate_directory", states);
        cfg.Set("screenshot_directory", shots);
        cfg.Set("savefiles_in_content_dir", "false");
        cfg.Set("savestates_in_content_dir", "false");
        cfg.Set("screenshots_in_content_dir", "false");
        cfg.Set("systemfiles_in_content_dir", "false");
        cfg.Set("sort_savefiles_enable", "false");
        cfg.Set("sort_savestates_enable", "false");
        cfg.Set("sort_savefiles_by_content_enable", "true");
        cfg.Set("sort_savestates_by_content_enable", "true");
        cfg.Set("sort_screenshots_by_content_enable", "true");
        cfg.Set("video_fullscreen", ctx.Plan.Mode == LaunchMode.Fullscreen ? "true" : "false");
        cfg.Set("video_windowed_fullscreen", "true");
        if (ctx.Embedded) cfg.Set("pause_nonactive", "false");
        else cfg.Remove("pause_nonactive");
        cfg.Set("quit_press_twice", "false");
        cfg.Set("config_save_on_exit", "false");
        Saved(Id, file, cfg.SaveIfChanged(file));
    }
}
