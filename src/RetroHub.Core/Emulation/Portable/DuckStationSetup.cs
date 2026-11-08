using RetroHub.Core.Emulation.ConfigFiles;
using RetroHub.Core.Logging;

namespace RetroHub.Core.Emulation.Portable;

/// <summary>
/// DuckStation: portable.txt next to the exe makes it keep settings.ini (and its data folders) there.
/// settings.ini is only edited once it exists: DuckStation resets every setting when [Main] SettingsVersion is
/// missing, so a file created by RetroHub would be thrown away on the first start anyway. After DuckStation ran
/// once: [BIOS] SearchDirectory → bios/, [MemoryCards] Directory → saves/&lt;system&gt;, [Folders] SaveStates /
/// Screenshots → savestates|screenshots/&lt;system&gt; (key names from DuckStation's EmuFolders; not verified
/// against a DuckStation install on this machine).
/// </summary>
public sealed class DuckStationSetup : PortableSetupBase
{
    public override string Id => "duckstation";

    public override string Summary =>
        "DuckStation läuft portabel (portable.txt neben der exe). Nach dem ersten Start von DuckStation sucht es BIOS-Dateien in bios/, " +
        "Memory Cards liegen dann in saves/<System>, Savestates in savestates/<System>, Screenshots in screenshots/<System>.";

    protected override void Configure(PortableContext ctx)
    {
        ctx.EnsureMarker("portable.txt");
        var file = Path.Combine(ctx.ExeDir, "settings.ini");
        if (!File.Exists(file))
        {
            Log.Info("DuckStation: settings.ini does not exist yet – folders are set after DuckStation has been started once");
            return;
        }
        var ini = IniFile.Load(file);
        if (ini.Get("Main", "SettingsVersion") == null)
        {
            Log.Info("DuckStation: settings.ini has no SettingsVersion (DuckStation will reset it) – left unchanged");
            return;
        }
        ini.Set("BIOS", "SearchDirectory", ctx.Ensure(ctx.Paths.Bios));
        ini.Set("MemoryCards", "Directory", ctx.SavesDir);
        ini.Set("Folders", "SaveStates", ctx.SaveStatesDir);
        ini.Set("Folders", "Screenshots", ctx.ScreenshotsDir);
        Saved(Id, file, ini.SaveIfChanged(file));
    }
}
