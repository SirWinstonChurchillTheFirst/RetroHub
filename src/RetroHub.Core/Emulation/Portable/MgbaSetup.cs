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
        Saved(Id, file, ini.SaveIfChanged(file));
    }
}
