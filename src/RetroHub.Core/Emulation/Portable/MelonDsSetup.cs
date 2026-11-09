using RetroHub.Core.Catalog;
using RetroHub.Core.Emulation.ConfigFiles;
using RetroHub.Core.Logging;

namespace RetroHub.Core.Emulation.Portable;

/// <summary>
/// melonDS 1.x (port of MonHub HubSetup.EnsureMelonDS): melonDS.toml next to the exe (the Windows build keeps
/// its config there). Sets top-level LastROMFolder (games/&lt;system&gt;) and PauseLostFocus=false,
/// [Instance0] SaveFilePath / SavestatePath. When bios7.bin, bios9.bin and firmware.bin are all found below
/// bios/ (any subfolder, e.g. bios/nds/), [DS] BIOS7Path/BIOS9Path/FirmwarePath point at them and
/// [Emu] ExternalBIOSEnable=true; otherwise the BIOS settings are left as the user has them.
/// Paths are written with forward slashes, quoted (as melonDS writes them).
/// </summary>
public sealed class MelonDsSetup : PortableSetupBase
{
    public override string Id => "melonds";

    public override string Summary =>
        "melonDS: Einstellungen in melonDS.toml neben der melonDS.exe. Spielstände: saves/<System>, Savestates: savestates/<System>. " +
        "Liegen bios7.bin, bios9.bin und firmware.bin in bios/, nutzt melonDS sie (sonst seine eingebaute Firmware).";

    private static readonly SystemDef DsBios = new()
    {
        Id = "nds",
        Name = "Nintendo DS",
        Bios =
        [
            new BiosFile { File = "bios7.bin" },
            new BiosFile { File = "bios9.bin" },
            new BiosFile { File = "firmware.bin" },
        ],
    };

    protected override void Configure(PortableContext ctx)
    {
        var file = Path.Combine(ctx.ExeDir, "melonDS.toml");
        var toml = TomlFile.Load(file);
        toml.Set(null, "LastROMFolder", TomlFile.QuotePath(ctx.GamesDir));
        toml.SetBool(null, "PauseLostFocus", false);
        toml.Set("Instance0", "SaveFilePath", TomlFile.QuotePath(ctx.SavesDir));
        toml.Set("Instance0", "SavestatePath", TomlFile.QuotePath(ctx.SaveStatesDir));

        var bios = BiosCheck.Check(DsBios, ctx.Paths.Bios);
        if (bios.Count == 3 && bios.All(b => b.FoundPath != null))
        {
            toml.Set("DS", "BIOS7Path", TomlFile.QuotePath(bios[0].FoundPath!));
            toml.Set("DS", "BIOS9Path", TomlFile.QuotePath(bios[1].FoundPath!));
            toml.Set("DS", "FirmwarePath", TomlFile.QuotePath(bios[2].FoundPath!));
            toml.SetBool("Emu", "ExternalBIOSEnable", true);
        }
        else if (bios.Any(b => b.FoundPath != null))
        {
            Log.Info("melonDS: only some DS BIOS files found in bios/ (needs bios7.bin, bios9.bin, firmware.bin) – BIOS settings left unchanged");
        }
        Saved(Id, file, toml.SaveIfChanged(file));
    }
}
