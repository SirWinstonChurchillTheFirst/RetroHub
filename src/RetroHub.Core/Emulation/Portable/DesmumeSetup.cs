using System.Text;
using RetroHub.Core.Emulation.ConfigFiles;
using RetroHub.Core.Logging;

namespace RetroHub.Core.Emulation.Portable;

/// <summary>
/// DeSmuME (Windows build, port of MonHub HubSetup.EnsureDeSmuME/SetDeSmuMERatio): desmume.ini next to the exe,
/// [PathSettings] Roms / Battery / States / Screenshots → games|saves|savestates|screenshots/&lt;system&gt;.
/// Embedded: [Video] Window Force Ratio=0 — with the ratio lock DeSmuME moves its own window and pushes it out
/// of RetroHub's game screen. In other modes the user's value stays.
/// DeSmuME reads its ini as ANSI: a pure-ASCII or new file is written as Latin-1 so a path with umlauts stays
/// readable for it (characters outside Latin-1 cannot be represented — logged).
/// </summary>
public sealed class DesmumeSetup : PortableSetupBase
{
    public override string Id => "desmume";

    public override string Summary =>
        "DeSmuME: Einstellungen in desmume.ini neben der DeSmuME-exe. Spielstände: saves/<System>, Savestates: savestates/<System>, " +
        "Screenshots: screenshots/<System>.";

    protected override void Configure(PortableContext ctx)
    {
        var file = Path.Combine(ctx.ExeDir, "desmume.ini");
        var ini = IniFile.Load(file, Encoding.Latin1);
        const string s = "PathSettings";
        ini.Set(s, "Roms", ctx.GamesDir);
        ini.Set(s, "Battery", ctx.SavesDir);
        ini.Set(s, "States", ctx.SaveStatesDir);
        ini.Set(s, "Screenshots", ctx.ScreenshotsDir);
        if (ctx.Embedded) ini.Set("Video", "Window Force Ratio", "0");
        if (ini.Encoding.CodePage == Encoding.Latin1.CodePage && ctx.Paths.Root.Any(c => c > 0xFF))
            Log.Warn($"DeSmuME reads desmume.ini as ANSI; the RetroHub path contains characters it cannot represent: {ctx.Paths.Root}");
        Saved(Id, file, ini.SaveIfChanged(file));
    }
}
