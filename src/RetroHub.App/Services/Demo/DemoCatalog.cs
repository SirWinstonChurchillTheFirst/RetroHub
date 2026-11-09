using RetroHub.Core.Catalog;

namespace RetroHub.App.Services.Demo;

/// <summary>Design-time catalog: 13 well-known systems and 10 emulators (definitions only, nothing on disk).</summary>
public sealed class DemoCatalog : ICatalog
{
    public IReadOnlyList<SystemDef> Systems { get; }
    public IReadOnlyList<EmulatorDef> Emulators { get; }
    public IReadOnlyList<string> LoadWarnings { get; } = [];

    public DemoCatalog()
    {
        var order = 0;
        SystemDef S(string id, string name, string shortName, string maker, int year, SystemCategory cat, string accent,
            string[] ext, string[] emus, double aspect = 4.0 / 3, BiosFile[]? bios = null) => new()
        {
            Id = id, Name = name, ShortName = shortName, Manufacturer = maker, Year = year, Category = cat,
            Accent = accent, Folders = [id], Extensions = ext, Emulators = emus, AspectRatio = aspect,
            Bios = bios ?? [], SortOrder = order++,
        };

        Systems =
        [
            S("nes", "Nintendo Entertainment System", "NES", "Nintendo", 1983, SystemCategory.Console, "#E5484D", [".nes", ".zip"], ["retroarch/mesen", "retroarch/fceumm"]),
            S("snes", "Super Nintendo", "SNES", "Nintendo", 1990, SystemCategory.Console, "#8E6CEF", [".sfc", ".smc", ".zip"], ["retroarch/snes9x", "retroarch/bsnes"]),
            S("n64", "Nintendo 64", "N64", "Nintendo", 1996, SystemCategory.Console, "#30A46C", [".z64", ".n64", ".v64"], ["retroarch/mupen64plus_next"]),
            S("gc", "Nintendo GameCube", "GC", "Nintendo", 2001, SystemCategory.Console, "#6E56CF", [".iso", ".rvz", ".gcz"], ["dolphin"]),
            S("gb", "Game Boy", "GB", "Nintendo", 1989, SystemCategory.Handheld, "#99B33C", [".gb", ".zip"], ["mgba", "retroarch/gambatte"], 10.0 / 9),
            S("gbc", "Game Boy Color", "GBC", "Nintendo", 1998, SystemCategory.Handheld, "#F5A524", [".gbc", ".zip"], ["mgba", "retroarch/gambatte"], 10.0 / 9),
            S("gba", "Game Boy Advance", "GBA", "Nintendo", 2001, SystemCategory.Handheld, "#5B6CFF", [".gba", ".zip"], ["mgba", "retroarch/mgba"], 3.0 / 2),
            S("nds", "Nintendo DS", "NDS", "Nintendo", 2004, SystemCategory.Handheld, "#3DA9FC", [".nds"], ["melonds", "desmume"], 4.0 / 6),
            S("3ds", "Nintendo 3DS", "3DS", "Nintendo", 2011, SystemCategory.Handheld, "#E5466B", [".3ds", ".cci", ".cia"], ["azahar"], 5.0 / 6),
            S("psx", "PlayStation", "PS1", "Sony", 1994, SystemCategory.Console, "#4F8CFF", [".cue", ".chd", ".m3u"], ["duckstation", "retroarch/swanstation"],
                bios: [new BiosFile { File = "scph5501.bin", Required = true, Description = "PlayStation BIOS (USA)" }]),
            S("ps2", "PlayStation 2", "PS2", "Sony", 2000, SystemCategory.Console, "#3E63DD", [".iso", ".chd"], ["pcsx2"]),
            S("psp", "PlayStation Portable", "PSP", "Sony", 2004, SystemCategory.Handheld, "#A07CFE", [".iso", ".cso"], ["ppsspp"], 16.0 / 9),
            S("md", "Sega Mega Drive", "MD", "Sega", 1988, SystemCategory.Console, "#12A594", [".md", ".gen", ".bin"], ["retroarch/genesis_plus_gx"]),
        ];

        Emulators =
        [
            new EmulatorDef
            {
                Id = "retroarch", Name = "RetroArch", Kind = EmulatorKind.RetroArch, Website = "https://www.retroarch.com", License = "GPL-3.0",
                Exe = ["retroarch.exe"], Systems = ["nes", "snes", "n64", "gb", "gbc", "gba", "psx", "md"],
                Cores = new()
                {
                    ["nes"] = ["mesen", "fceumm"], ["snes"] = ["snes9x", "bsnes"], ["n64"] = ["mupen64plus_next"],
                    ["gb"] = ["gambatte"], ["gbc"] = ["gambatte"], ["gba"] = ["mgba"], ["psx"] = ["swanstation"], ["md"] = ["genesis_plus_gx"],
                },
                DefaultMode = LaunchMode.Fullscreen, Modes = [LaunchMode.Fullscreen, LaunchMode.Windowed],
                DownloadUrl = "https://www.retroarch.com/?page=platforms",
            },
            new EmulatorDef
            {
                Id = "mgba", Name = "mGBA", Website = "https://mgba.io", License = "MPL-2.0", Exe = ["mGBA.exe"], Systems = ["gb", "gbc", "gba"],
                Portable = "mgba", DefaultMode = LaunchMode.Embedded, Modes = [LaunchMode.Embedded, LaunchMode.Fullscreen, LaunchMode.Windowed],
                Embed = new EmbedProfile { KeepFrame = true }, DownloadUrl = "https://mgba.io/downloads.html",
            },
            new EmulatorDef
            {
                Id = "melonds", Name = "melonDS", Website = "https://melonds.kuribo64.net", License = "GPL-3.0", Exe = ["melonDS.exe"], Systems = ["nds"],
                Portable = "melonds", DefaultMode = LaunchMode.Embedded, Modes = [LaunchMode.Embedded, LaunchMode.Fullscreen, LaunchMode.Windowed],
                Embed = new EmbedProfile { KeepFrame = true }, DownloadUrl = "https://melonds.kuribo64.net/downloads.php",
            },
            new EmulatorDef
            {
                Id = "desmume", Name = "DeSmuME", Website = "https://desmume.org", License = "GPL-2.0", Exe = ["DeSmuME*.exe"], Systems = ["nds"],
                Portable = "desmume", DefaultMode = LaunchMode.Windowed, Modes = [LaunchMode.Embedded, LaunchMode.Windowed],
                DownloadUrl = "https://desmume.org/download/",
            },
            new EmulatorDef
            {
                Id = "azahar", Name = "Azahar", Website = "https://azahar-emu.org", License = "GPL-2.0", Exe = ["azahar.exe"], Systems = ["3ds"],
                Portable = "azahar", DefaultMode = LaunchMode.Embedded, Modes = [LaunchMode.Embedded, LaunchMode.Fullscreen, LaunchMode.Windowed],
                Embed = new EmbedProfile { KeepFrame = true, ReadyTitleContains = " | " }, DownloadUrl = "https://azahar-emu.org/pages/download/",
            },
            new EmulatorDef
            {
                Id = "dolphin", Name = "Dolphin", Website = "https://dolphin-emu.org", License = "GPL-2.0", Exe = ["Dolphin.exe"], Systems = ["gc"],
                DownloadUrl = "https://dolphin-emu.org/download/",
            },
            new EmulatorDef
            {
                Id = "duckstation", Name = "DuckStation", Website = "https://www.duckstation.org", License = "CC-BY-NC-ND-4.0",
                Exe = ["duckstation-qt-x64-ReleaseLTCG.exe"], Systems = ["psx"], DownloadUrl = "https://www.duckstation.org",
            },
            new EmulatorDef
            {
                Id = "pcsx2", Name = "PCSX2", Website = "https://pcsx2.net", License = "GPL-3.0", Exe = ["pcsx2-qt.exe"], Systems = ["ps2"],
                DownloadUrl = "https://pcsx2.net/downloads",
            },
            new EmulatorDef
            {
                Id = "ppsspp", Name = "PPSSPP", Website = "https://www.ppsspp.org", License = "GPL-2.0", Exe = ["PPSSPPWindows64.exe"], Systems = ["psp"],
                DownloadUrl = "https://www.ppsspp.org/download",
            },
        ];
    }

    public SystemDef? GetSystem(string id) => Systems.FirstOrDefault(s => string.Equals(s.Id, id, StringComparison.OrdinalIgnoreCase));
    public EmulatorDef? GetEmulator(string id) => Emulators.FirstOrDefault(e => string.Equals(e.Id, id, StringComparison.OrdinalIgnoreCase));

    public SystemDef? SystemForFolder(string folderName) =>
        Systems.FirstOrDefault(s => s.Folders.Any(f => string.Equals(f, folderName, StringComparison.OrdinalIgnoreCase)));

    public IReadOnlyList<SystemDef> SystemsForExtension(string extension) =>
        Systems.Where(s => s.Extensions.Contains(extension, StringComparer.OrdinalIgnoreCase)).ToList();
}
