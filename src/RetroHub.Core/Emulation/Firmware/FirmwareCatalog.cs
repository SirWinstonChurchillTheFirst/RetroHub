using RetroHub.Core.Catalog;

namespace RetroHub.Core.Emulation.Firmware;

/// <summary>How much a console depends on the player's own BIOS/firmware/keys.</summary>
public enum FirmwareNeed
{
    /// <summary>Nothing to import (Dolphin, Azahar with decrypted games).</summary>
    None,
    /// <summary>The emulator runs without; the files add something (boot logo, system menu, accuracy).</summary>
    Optional,
    /// <summary>Only some games or formats need it (Wii U disc images, Sega CD / PC Engine CD games).</summary>
    Sometimes,
    /// <summary>No game starts without it (Switch keys).</summary>
    Required,
}

/// <summary>What kind of console entry it is: decides which files the importer looks for and where they go.</summary>
public enum FirmwareConsoleKind
{
    /// <summary>The system's "bios" entries of the catalog JSON → bios/&lt;file&gt;.</summary>
    CatalogBios,
    /// <summary>Eden: prod.keys, title.keys and the firmware (.nca files).</summary>
    Switch,
    /// <summary>Cemu: keys.txt / .key disc keys.</summary>
    WiiU,
    /// <summary>LRPS2 / PCSX2: a PS2 BIOS dump of any name → bios/pcsx2/bios/.</summary>
    Ps2,
    /// <summary>A data folder a RetroArch core needs in bios/ (PPSSPP assets, blueMSX Machines/Databases).</summary>
    Assets,
    /// <summary>Nothing needed (shown as such instead of an import).</summary>
    NoneNeeded,
}

/// <summary>
/// A data folder below bios/: recognised by an anchor file ("ppge_atlas.zim" in PPSSPP/, "msxromdb.xml" in
/// Databases/); <see cref="AnchorDepth"/> = how many folders above the anchor the copied folder starts.
/// </summary>
public sealed record AssetFolder(string Target, string Anchor, int AnchorDepth, string StatusFile);

/// <summary>One row of "Firmware &amp; Keys": a console (or a family such as GameCube &amp; Wii) and its emulators.</summary>
public sealed record FirmwareConsole
{
    /// <summary>Stable id (the first system id, "gc-wii" for GameCube &amp; Wii).</summary>
    public required string Id { get; init; }
    public required string[] SystemIds { get; init; }
    public required string Name { get; init; }
    /// <summary>The emulators the files are for, as a short text ("Eden", "melonDS, DeSmuME").</summary>
    public string Emulators { get; init; } = "";
    public FirmwareConsoleKind Kind { get; init; }
    public FirmwareNeed Need { get; init; }
    /// <summary>The catalog's BIOS entries (only for <see cref="FirmwareConsoleKind.CatalogBios"/>).</summary>
    public BiosFile[] Bios { get; init; } = [];
    /// <summary>The system definition (only for <see cref="FirmwareConsoleKind.CatalogBios"/>).</summary>
    public SystemDef? System { get; init; }
    /// <summary>A whole folder can be imported (the Switch firmware is a folder of .nca files).</summary>
    public bool AcceptsFolders { get; init; }
    /// <summary>The data folders (only for <see cref="FirmwareConsoleKind.Assets"/>).</summary>
    public AssetFolder[] Assets { get; init; } = [];
    public int SortOrder { get; init; }
}

/// <summary>
/// The consoles shown under "Firmware &amp; Keys": every catalog system with "bios" entries (data-driven, plugin systems
/// included) plus the built-in special cases that the catalog JSON cannot describe – Switch (Eden keys + firmware),
/// Wii U (Cemu keys), PS2 (BIOS of any name) – and the systems that need nothing (GameCube &amp; Wii, 3DS), which are
/// listed so the player sees that they need nothing. Only systems the catalog knows appear.
/// </summary>
public static class FirmwareCatalog
{
    /// <summary>Systems for which the importer knows more than the catalog's "bios" entries.</summary>
    private static readonly string[] Special = ["switch", "wiiu", "ps2", "gamecube", "wii", "3ds", "psp", "msx"];

    /// <summary>PPSSPP core: system/PPSSPP (the "assets" folder of PPSSPP, renamed); checked file ppge_atlas.zim.</summary>
    public static readonly AssetFolder PpssppAssets = new("PPSSPP", "ppge_atlas.zim", 0, "PPSSPP/ppge_atlas.zim");
    /// <summary>blueMSX core: system/Machines (checked: Machines/Shared Roms/MSX.rom) and system/Databases (msxromdb.xml).</summary>
    public static readonly AssetFolder MsxMachines = new("Machines", "MSX.rom", 1, "Machines/Shared Roms/MSX.rom");
    public static readonly AssetFolder MsxDatabases = new("Databases", "msxromdb.xml", 0, "Databases/msxromdb.xml");

    public static IReadOnlyList<FirmwareConsole> Build(IEnumerable<SystemDef> systems)
    {
        var all = systems.ToList();
        var byId = all.ToDictionary(s => s.Id, StringComparer.OrdinalIgnoreCase);
        var list = new List<FirmwareConsole>();

        if (byId.TryGetValue("switch", out var sw))
            list.Add(new FirmwareConsole
            {
                Id = "switch", SystemIds = ["switch"], Name = sw.Name, Emulators = "Eden", Kind = FirmwareConsoleKind.Switch,
                Need = FirmwareNeed.Required, AcceptsFolders = true, SortOrder = sw.SortOrder,
            });
        if (byId.TryGetValue("wiiu", out var wiiu))
            list.Add(new FirmwareConsole
            {
                Id = "wiiu", SystemIds = ["wiiu"], Name = wiiu.Name, Emulators = "Cemu", Kind = FirmwareConsoleKind.WiiU,
                Need = FirmwareNeed.Sometimes, SortOrder = wiiu.SortOrder,
            });
        if (byId.TryGetValue("3ds", out var n3ds))
            list.Add(new FirmwareConsole
            {
                Id = "3ds", SystemIds = ["3ds"], Name = n3ds.Name, Emulators = "Azahar", Kind = FirmwareConsoleKind.NoneNeeded,
                Need = FirmwareNeed.None, SortOrder = n3ds.SortOrder,
            });
        var gcWii = new[] { "gamecube", "wii" }.Where(byId.ContainsKey).Select(id => byId[id]).ToList();
        if (gcWii.Count > 0)
            list.Add(new FirmwareConsole
            {
                Id = gcWii.Count == 2 ? "gc-wii" : gcWii[0].Id, SystemIds = gcWii.Select(s => s.Id).ToArray(),
                Name = string.Join(" & ", gcWii.Select(s => s.Name)), Emulators = "Dolphin",
                Kind = FirmwareConsoleKind.NoneNeeded, Need = FirmwareNeed.None, SortOrder = gcWii.Min(s => s.SortOrder),
            });
        if (byId.TryGetValue("ps2", out var ps2))
            list.Add(new FirmwareConsole
            {
                Id = "ps2", SystemIds = ["ps2"], Name = ps2.Name, Emulators = EmulatorNames(ps2), Kind = FirmwareConsoleKind.Ps2,
                Need = FirmwareNeed.Required, SortOrder = ps2.SortOrder,
            });

        if (byId.TryGetValue("psp", out var psp))
            list.Add(new FirmwareConsole
            {
                Id = "psp", SystemIds = ["psp"], Name = psp.Name, Emulators = "RetroArch", Kind = FirmwareConsoleKind.Assets,
                Need = FirmwareNeed.Required, AcceptsFolders = true, Assets = [PpssppAssets], SortOrder = psp.SortOrder,
            });
        if (byId.TryGetValue("msx", out var msx))
            list.Add(new FirmwareConsole
            {
                Id = "msx", SystemIds = ["msx"], Name = msx.Name, Emulators = "RetroArch", Kind = FirmwareConsoleKind.Assets,
                Need = FirmwareNeed.Required, AcceptsFolders = true, Assets = [MsxMachines, MsxDatabases], SortOrder = msx.SortOrder,
            });

        foreach (var s in all)
        {
            if (s.Bios.Length == 0 || Special.Contains(s.Id, StringComparer.OrdinalIgnoreCase)) continue;
            list.Add(new FirmwareConsole
            {
                Id = s.Id, SystemIds = [s.Id], Name = s.Name, Emulators = EmulatorNames(s), Kind = FirmwareConsoleKind.CatalogBios,
                Need = s.Bios.Any(b => b.Required) ? FirmwareNeed.Required : NeedFor(s.Id), Bios = s.Bios, System = s,
                SortOrder = s.SortOrder,
            });
        }
        return list.OrderBy(c => c.SortOrder).ThenBy(c => c.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
    }

    /// <summary>Optional BIOS for most systems; CD add-ons and the FDS only need it for their games.</summary>
    private static FirmwareNeed NeedFor(string systemId) => systemId switch
    {
        "segacd" or "pcengine" or "nes" or "arcade" => FirmwareNeed.Sometimes,
        "ps1" or "saturn" => FirmwareNeed.Sometimes,
        _ => FirmwareNeed.Optional,
    };

    /// <summary>"melonDS, DeSmuME, RetroArch": the stand-alone programs, every RetroArch core as one "RetroArch" (first three).</summary>
    private static string EmulatorNames(SystemDef s)
    {
        var names = new List<string>();
        foreach (var e in s.Emulators)
        {
            var name = e.Contains('/') ? "RetroArch" : Pretty(e);
            if (!names.Contains(name)) names.Add(name);
            if (names.Count == 3) break;
        }
        return string.Join(", ", names);
    }

    private static string Pretty(string id) => id switch
    {
        "melonds" => "melonDS", "desmume" => "DeSmuME", "mgba" => "mGBA", "duckstation" => "DuckStation", "pcsx2" => "PCSX2",
        "flycast" => "Flycast", "redream" => "Redream", "mame" => "MAME", "fbneo" => "FBNeo", "melondsds" => "melonDS DS",
        "pcsx_rearmed" => "PCSX ReARMed", "swanstation" => "SwanStation", "mednafen_psx_hw" => "Beetle PSX",
        "mednafen_saturn" => "Beetle Saturn", "kronos" => "Kronos", "genesis_plus_gx" => "Genesis Plus GX",
        "picodrive" => "PicoDrive", "mednafen_pce_fast" => "Beetle PCE Fast", "mednafen_pce" => "Beetle PCE",
        "mednafen_supergrafx" => "Beetle SuperGrafx", "handy" => "Handy", "mednafen_lynx" => "Beetle Lynx",
        "prosystem" => "ProSystem", "fceumm" => "FCEUmm", "nestopia" => "Nestopia", "mesen" => "Mesen",
        "gpsp" => "gpSP", "gambatte" => "Gambatte", "sameboy" => "SameBoy",
        _ => id.Length > 0 ? char.ToUpperInvariant(id[0]) + id[1..] : id,
    };
}
