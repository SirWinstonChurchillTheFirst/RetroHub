using RetroHub.Core.Emulation.ConfigFiles;

namespace RetroHub.Core.Emulation.Portable;

/// <summary>
/// mGBA (port of MonHub HubSetup.EnsureMGBA): an empty portable.ini next to the exe makes mGBA keep config.ini
/// there; config.ini [ports.qt] savegamePath / savestatePath / screenshotPath → saves|savestates|screenshots/&lt;system&gt;,
/// lastDirectory → games/&lt;system&gt; (the "open ROM" dialog). Embedded: pauseOnFocusLost=0, because the
/// RetroHub window holds the focus while the game runs inside it (otherwise the user's value stays).
/// </summary>
public sealed class MgbaSetup : PortableSetupBase
{
    public override string Id => "mgba";

    public override string Summary =>
        "mGBA läuft portabel (portable.ini neben der mGBA.exe, Einstellungen in config.ini daneben). " +
        "Spielstände: saves/<System>, Savestates: savestates/<System>, Screenshots: screenshots/<System>.";

    private static readonly Catalog.SystemDef GbaBios = new()
    {
        Id = "gba",
        Name = "Game Boy Advance",
        Bios = [new Catalog.BiosFile { File = "gba_bios.bin", Md5 = ["a860e8c0b6d573d191e4ec7db1b1e4f6"] }],
    };

    protected override void Configure(PortableContext ctx)
    {
        ctx.EnsureMarker("portable.ini");
        var file = Path.Combine(ctx.ExeDir, "config.ini");
        var ini = IniFile.Load(file);
        const string s = "ports.qt";
        ini.Set(s, "savegamePath", ctx.SavesDir);
        ini.Set(s, "savestatePath", ctx.SaveStatesDir);
        ini.Set(s, "screenshotPath", ctx.ScreenshotsDir);
        ini.Set(s, "lastDirectory", ctx.GamesDir);
        if (ctx.Embedded) ini.Set(s, "pauseOnFocusLost", "0");
        // mGBA 0.10.5 only checks for updates when updateAutoCheck is set (GBAApp.cpp); written as 0 so it stays off
        ini.Set(s, "updateAutoCheck", "0");
        // the player's own GBA BIOS (bios/gba_bios.bin, MD5-checked): boot logo; otherwise mGBA's built-in one stays
        var bios = BiosCheck.Check(GbaBios, ctx.Paths.Bios).FirstOrDefault();
        if (bios is { State: BiosState.Ok, FoundPath: { } path })
        {
            ini.Set(s, "gba.bios", path);
            ini.Set(s, "useBios", "1");
        }
        Saved(Id, file, ini.SaveIfChanged(file));
    }
}
