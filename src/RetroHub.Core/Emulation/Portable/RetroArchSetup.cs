using RetroHub.Core.Catalog;
using RetroHub.Core.Emulation.ConfigFiles;

namespace RetroHub.Core.Emulation.Portable;

/// <summary>
/// RetroArch: writes ONLY RetroHub's override file data/config/retroarch/retrohub.cfg (passed with
/// <c>--appendconfig {retroarchOverride}</c> in the emulator definition). The user's retroarch.cfg is never
/// read or written.
/// <list type="bullet">
/// <item>system/savefile/savestate/screenshot directories → bios/, saves/, savestates/, screenshots/.</item>
/// <item>The directories point straight at saves/&lt;system&gt;, savestates/&lt;system&gt;, screenshots/&lt;system&gt; (the override
/// is rewritten for every launch) and every sort_* option is off: games/gba/x.gba saves to saves/gba/x.srm, and a game
/// in an external folder like "I:/Zelda/SNES/A Link to the Past/x.sfc" to saves/snes/x.srm – not to a subfolder named
/// after its game folder (what RetroArch's sort_*_by_content does) or after the core.</item>
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
        var saves = ctx.SavesDir;
        var states = ctx.SaveStatesDir;
        var shots = ctx.ScreenshotsDir;

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
        cfg.Set("sort_savefiles_by_content_enable", "false");
        cfg.Set("sort_savestates_by_content_enable", "false");
        cfg.Set("sort_screenshots_by_content_enable", "false");
        cfg.Set("video_fullscreen", ctx.Plan.Mode == LaunchMode.Fullscreen ? "true" : "false");
        cfg.Set("video_windowed_fullscreen", "true");
        if (ctx.Embedded) cfg.Set("pause_nonactive", "false");
        else cfg.Remove("pause_nonactive");
        cfg.Set("quit_press_twice", "false");
        cfg.Set("config_save_on_exit", "false");
        Saved(Id, file, cfg.SaveIfChanged(file));
    }
}
