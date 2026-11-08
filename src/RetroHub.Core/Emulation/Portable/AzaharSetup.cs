using RetroHub.Core.Emulation.ConfigFiles;
using RetroHub.Core.Logging;

namespace RetroHub.Core.Emulation.Portable;

/// <summary>
/// Azahar (3DS; port of MonHub Azahar.cs Ensure/SetStorage/Set/Quote): a "user" folder next to azahar.exe makes it
/// portable; its config is user\config\qt-config.ini (Qt QSettings INI). Every value gets its "key\default=false"
/// twin — without it Azahar ignores the value.
/// <list type="bullet">
/// <item>[Data%20Storage] use_custom_storage=true, nand_directory / sdmc_directory → saves/&lt;system&gt;/nand/ and
/// /sdmc/ (both created first: Azahar ignores folders that do not exist and writes its default back).</item>
/// <item>[Miscellaneous] check_for_update_on_start=false (RetroHub manages the version).</item>
/// <item>[UI] confirmClose=false: RetroHub closes the emulator itself (WM_CLOSE); a confirmation dialog would block it.</item>
/// <item>[Paths] romsPath → games/&lt;system&gt;/ (as MonHub). screenshotPath → screenshots/&lt;system&gt;/ (key name as in
/// Citra's Qt frontend, not verified against an Azahar-written config).</item>
/// </list>
/// Savestates stay in Azahar's own user\states folder (not configurable). Windows' 260-character path limit:
/// 3DS games create deeply nested folders below sdmc (≈140 characters), a too deep RetroHub folder is logged.
/// </summary>
public sealed class AzaharSetup : PortableSetupBase
{
    public override string Id => "azahar";

    /// <summary>Room the 3DS games need below the SD card folder (see MonHub Azahar.cs SdCardDepth).</summary>
    private const int SdCardDepth = 140;
    private const int MaxPath = 259;

    public override string Summary =>
        "Azahar läuft portabel (Ordner „user“ neben der azahar.exe). NAND und SD-Karte (Spielstände) liegen in saves/<System>/nand " +
        "und saves/<System>/sdmc, Savestates bleiben in Azahars user\\states.";

    protected override void Configure(PortableContext ctx)
    {
        var user = ctx.Ensure(Path.Combine(ctx.ExeDir, "user")); // portable mode
        var file = Path.Combine(user, "config", "qt-config.ini");
        var ini = IniFile.Load(file);

        var nand = ctx.Ensure(Path.Combine(ctx.SavesDir, "nand"));
        var sdmc = ctx.Ensure(Path.Combine(ctx.SavesDir, "sdmc"));
        if (sdmc.Length + 1 + SdCardDepth > MaxPath)
            Log.Warn($"Azahar: the SD card folder is too deep for some 3DS games (Windows 260-character limit): {sdmc}");

        Set(ini, "Data%20Storage", "use_custom_storage", "true");
        Set(ini, "Data%20Storage", "nand_directory", Quote(nand));
        Set(ini, "Data%20Storage", "sdmc_directory", Quote(sdmc));
        Set(ini, "Miscellaneous", "check_for_update_on_start", "false");
        Set(ini, "UI", "confirmClose", "false");
        Set(ini, "Paths", "romsPath", Quote(ctx.GamesDir));
        Set(ini, "Paths", "screenshotPath", Quote(ctx.ScreenshotsDir));
        Saved(Id, file, ini.SaveIfChanged(file));
    }

    /// <summary>A setting plus its "\default=false" twin.</summary>
    internal static void Set(IniFile ini, string section, string key, string value)
    {
        ini.Set(section, key + "\\default", "false");
        ini.Set(section, key, value);
    }

    /// <summary>Qt settings folder value: forward slashes, one trailing slash, quoted (commas would split it).</summary>
    internal static string Quote(string folder) => "\"" + folder.Replace('\\', '/').TrimEnd('/') + "/\"";
}
