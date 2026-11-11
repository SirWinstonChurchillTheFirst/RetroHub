using System.Text.Json.Serialization;

namespace RetroHub.Core.Catalog;

/*
 * Data-driven catalog. Systems and emulators are JSON definitions:
 *   app/resources/systems/*.json, app/resources/emulators/*.json   (built in, shipped)
 *   plugins/systems/*.json,        plugins/emulators/*.json         (user / plugin overrides, same id = replace)
 * Each file contains a JSON array of definitions. Adding a new system or emulator needs no code change,
 * unless it needs a special portable-config recipe (Emulation/Portable/*) or a header sniffer (Library/Sniffers/*).
 */

[JsonConverter(typeof(JsonStringEnumConverter<SystemCategory>))]
public enum SystemCategory { Console, Handheld, Arcade, Computer, Pc }

[JsonConverter(typeof(JsonStringEnumConverter<LaunchMode>))]
public enum LaunchMode
{
    /// <summary>Emulator window is re-parented into RetroHub's game screen (Win32 SetParent). Only for profiles verified to work.</summary>
    Embedded,
    /// <summary>Emulator starts in its own fullscreen; RetroHub shows a transition, hides, and comes back when it exits.</summary>
    Fullscreen,
    /// <summary>Emulator in a normal window, RetroHub stays open behind it.</summary>
    Windowed,
}

[JsonConverter(typeof(JsonStringEnumConverter<EmulatorKind>))]
public enum EmulatorKind
{
    Standalone,
    /// <summary>RetroArch: one exe, many libretro cores (cores/&lt;core&gt;_libretro.dll).</summary>
    RetroArch,
    /// <summary>Native PC game / executable; "emulator" is just the game exe itself.</summary>
    Native,
}

public sealed record BiosFile
{
    public required string File { get; init; }
    /// <summary>Lower-case hex MD5s of known-good dumps (any matches). Empty = only check presence.</summary>
    public string[] Md5 { get; init; } = [];
    public bool Required { get; init; }
    public string? Description { get; init; }
    /// <summary>
    /// Byte sizes a real dump has (any matches). Used by the firmware import to recognise a file by name when the
    /// catalog knows no MD5 (e.g. DS firmware, Sega CD BIOS). Empty = any non-empty file of that name.
    /// </summary>
    public long[] Sizes { get; init; } = [];
}

public sealed record SystemDef
{
    /// <summary>Stable id, also the default folder name below games/ (e.g. "gba").</summary>
    public required string Id { get; init; }
    public required string Name { get; init; }
    public string ShortName { get; init; } = "";
    public string Manufacturer { get; init; } = "";
    public int? Year { get; init; }
    public SystemCategory Category { get; init; } = SystemCategory.Console;
    /// <summary>Folder names (below games/ or any library folder) that mean this system. First = canonical.</summary>
    public string[] Folders { get; init; } = [];
    /// <summary>Lower-case extensions with dot (".gba"). Archives (".zip", ".7z") allowed when the emulator loads them.</summary>
    public string[] Extensions { get; init; } = [];
    /// <summary>Ids of header sniffers (Library/Sniffers) that can positively identify this system's files.</summary>
    public string[] Sniffers { get; init; } = [];
    /// <summary>Display aspect ratio of the picture (width/height), used for the embedded game screen.</summary>
    public double? AspectRatio { get; init; }
    /// <summary>libretro-thumbnails / libretro-database system name, e.g. "Nintendo - Game Boy Advance".</summary>
    public string? LibretroName { get; init; }
    /// <summary>
    /// GameTDB platform ("switch") for systems libretro-thumbnails has no set for: covers then come from
    /// art.gametdb.com instead of libretro (<see cref="Metadata.GameTdb"/>). Lower-case letters/digits only.
    /// </summary>
    public string? GameTdb { get; init; }
    /// <summary>Preferred emulators in order. "mgba" = standalone emulator id; "retroarch/mgba" = RetroArch with core mgba.</summary>
    public string[] Emulators { get; init; } = [];
    /// <summary>Accent colour (#RRGGBB) for tiles/backgrounds.</summary>
    public string? Accent { get; init; }
    public BiosFile[] Bios { get; init; } = [];
    /// <summary>Multi-file formats whose referenced files must not be listed as separate games (".cue" hides its ".bin").</summary>
    public string[] PlaylistExtensions { get; init; } = [];
    /// <summary>True for systems where a game is a folder (e.g. PC game folders, PS3); scanner then looks for an exe/marker.</summary>
    public bool FolderGames { get; init; }
    public int SortOrder { get; init; }
}

/// <summary>How RetroHub handles an emulator window when embedding (tuned per emulator; see MonHub GameWindow.cs).</summary>
public sealed record EmbedProfile
{
    /// <summary>Keep the window frame and push it outside the clipped host (Qt 6 apps break when the frame is stripped).</summary>
    public bool KeepFrame { get; init; }
    /// <summary>Do cross-process window calls on a worker thread (a busy emulator cannot freeze the UI).</summary>
    public bool OffUiThread { get; init; } = true;
    /// <summary>Nudge the window +1px and back after host moves (DirectDraw apps that miss WM_MOVE).</summary>
    public bool NudgeOnMove { get; init; }
    /// <summary>Hide the window behind a loading screen until it answers WM_NULL reliably.</summary>
    public bool MaskWhileLoading { get; init; } = true;
    /// <summary>Window title must contain this before the game counts as running (e.g. Azahar " | ").</summary>
    public string? ReadyTitleContains { get; init; }
    /// <summary>Known menu/status bar heights at 100 % scale (top, bottom), used when they cannot be hidden.</summary>
    public int BarTop { get; init; }
    public int BarBottom { get; init; }
}

public sealed record EmulatorDef
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public EmulatorKind Kind { get; init; } = EmulatorKind.Standalone;
    public string? Website { get; init; }
    public string? License { get; init; }
    /// <summary>Exe file names to look for below emulators/&lt;id&gt;/ (first match wins; wildcards * allowed).</summary>
    public string[] Exe { get; init; } = [];
    /// <summary>System ids this emulator can run (for RetroArch: union of its core mapping).</summary>
    public string[] Systems { get; init; } = [];
    /// <summary>
    /// Argument tokens. Placeholders: {rom} {romDir} {romName} {core} {emuDir} {root} {bios} {saves} {savestates}
    /// {screenshots} {system}. A token that consists only of "{fullscreen}" expands to <see cref="FullscreenArgs"/>
    /// in fullscreen mode and to nothing otherwise. Each token becomes exactly one argv entry (no shell quoting).
    /// </summary>
    public string[] Args { get; init; } = ["{rom}"];
    public string[] FullscreenArgs { get; init; } = [];
    /// <summary>RetroArch only: system id → core names in preference order (cores/&lt;name&gt;_libretro.dll).</summary>
    public Dictionary<string, string[]> Cores { get; init; } = new();
    /// <summary>Id of the portable-config recipe (Emulation/Portable) run before every launch; null = none.</summary>
    public string? Portable { get; init; }
    public LaunchMode DefaultMode { get; init; } = LaunchMode.Fullscreen;
    /// <summary>Modes this emulator supports. Embedded only when verified.</summary>
    public LaunchMode[] Modes { get; init; } = [LaunchMode.Fullscreen, LaunchMode.Windowed];
    public EmbedProfile? Embed { get; init; }
    /// <summary>Download page shown when the emulator is missing (RetroHub never downloads emulators silently).</summary>
    public string? DownloadUrl { get; init; }
    public string? Notes { get; init; }
}

/// <summary>Reference "emulator" or "emulator/core" as used in <see cref="SystemDef.Emulators"/>.</summary>
public readonly record struct EmulatorRef(string EmulatorId, string? Core)
{
    public static EmulatorRef Parse(string s)
    {
        var i = s.IndexOf('/');
        return i < 0 ? new EmulatorRef(s.Trim(), null) : new EmulatorRef(s[..i].Trim(), s[(i + 1)..].Trim());
    }
    public override string ToString() => Core == null ? EmulatorId : $"{EmulatorId}/{Core}";
}

/// <summary>Loaded catalog: built-in definitions merged with plugin overrides.</summary>
public interface ICatalog
{
    IReadOnlyList<SystemDef> Systems { get; }
    IReadOnlyList<EmulatorDef> Emulators { get; }
    SystemDef? GetSystem(string id);
    EmulatorDef? GetEmulator(string id);
    /// <summary>System whose <see cref="SystemDef.Folders"/> contains this folder name (case-insensitive).</summary>
    SystemDef? SystemForFolder(string folderName);
    /// <summary>All systems that list this extension (lower-case, with dot).</summary>
    IReadOnlyList<SystemDef> SystemsForExtension(string extension);
    /// <summary>Problems found while loading (bad JSON in a plugin etc.). Never throws for plugin errors.</summary>
    IReadOnlyList<string> LoadWarnings { get; }
}
