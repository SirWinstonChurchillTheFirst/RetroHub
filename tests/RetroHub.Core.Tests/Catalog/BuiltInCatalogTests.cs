using System.Text.RegularExpressions;
using RetroHub.Core.Catalog;
using Xunit;

namespace RetroHub.Core.Tests.Catalog;

// inside RetroHub.Core.Tests.Catalog the simple name "Catalog" is this namespace → alias the class
using CatalogImpl = RetroHub.Core.Catalog.Catalog;

/// <summary>Checks the shipped definitions in src/RetroHub.Core/Resources (loaded through the real loader).</summary>
public sealed class BuiltInCatalogTests : IDisposable
{
    private readonly CatalogTestEnv _env = new();
    private readonly CatalogImpl _catalog;

    public BuiltInCatalogTests() { _catalog = CatalogImpl.Load(_env.Paths); }
    public void Dispose() => _env.Dispose();

    private static readonly string[] RequiredSystems =
    [
        "nes", "snes", "n64", "gamecube", "wii", "gb", "gbc", "gba", "nds", "3ds", "ps1", "ps2", "psp", "megadrive",
        "dreamcast", "saturn", "atari2600", "arcade", "pc",
        "mastersystem", "gamegear", "segacd", "sega32x", "pcengine", "atari7800", "lynx", "ngp", "wonderswan", "virtualboy", "msx",
        "wiiu", "switch",
    ];

    private static readonly string[] RequiredEmulators =
    [
        "retroarch", "mgba", "melonds", "desmume", "azahar", "dolphin", "pcsx2", "duckstation", "ppsspp", "mame",
        "flycast", "redream", "native", "cemu", "eden", "ryujinx",
    ];

    private static readonly HashSet<string> KnownSniffers =
    [
        "nes", "snes", "n64", "gb", "gbc", "gba", "nds", "3ds", "megadrive", "sms", "iso9660-ps1", "iso9660-ps2",
        "iso9660-psp", "nintendo-disc-gc", "nintendo-disc-wii",
    ];

    [Fact]
    public void BuiltIns_LoadWithoutWarnings()
    {
        Assert.True(_catalog.LoadWarnings.Count == 0, string.Join(Environment.NewLine, _catalog.LoadWarnings));
    }

    [Fact]
    public void BuiltIns_ContainAllRequestedSystemsAndEmulators()
    {
        foreach (var id in RequiredSystems) Assert.True(_catalog.GetSystem(id) != null, $"system {id} missing");
        foreach (var id in RequiredEmulators) Assert.True(_catalog.GetEmulator(id) != null, $"emulator {id} missing");
        Assert.Equal(RequiredSystems.Length, _catalog.Systems.Count);
    }

    [Theory]
    // real-world folder names (e.g. a user's collection on another drive) → system via the normalised name map
    [InlineData("Game Boy Advance", "gba")]
    [InlineData("Game Boy Color", "gbc")]
    [InlineData("Game Boy", "gb")]
    [InlineData("GameCube", "gamecube")]
    [InlineData("Nintendo GameCube", "gamecube")]
    [InlineData("NES", "nes")]
    [InlineData("SNES", "snes")]
    [InlineData("Nintendo 3DS", "3ds")]
    [InlineData("Nintendo 64", "n64")]
    [InlineData("Nintendo DS", "nds")]
    [InlineData("Nintendo Switch", "switch")]
    [InlineData("Wii U", "wiiu")]
    [InlineData("Wii", "wii")]
    [InlineData("game-boy_advance", "gba")]
    public void SystemForFolder_MatchesDisplayNames(string folder, string expected)
    {
        Assert.Equal(expected, _catalog.SystemForFolder(folder)?.Id);
    }

    [Theory]
    [InlineData("The Legend of Zelda")]
    [InlineData("Hacks & Übersetzungen")]
    [InlineData("Nintendo")]
    public void SystemForFolder_DoesNotGuessForUnrelatedOrAmbiguousNames(string folder)
    {
        Assert.Null(_catalog.SystemForFolder(folder));
    }

    [Fact]
    public void EverySystemEmulatorRef_ResolvesToEmulator_AndRetroArchCoreIsMapped()
    {
        foreach (var system in _catalog.Systems)
        {
            Assert.NotEmpty(system.Emulators);
            foreach (var text in system.Emulators)
            {
                var r = EmulatorRef.Parse(text);
                var emu = _catalog.GetEmulator(r.EmulatorId);
                Assert.True(emu != null, $"{system.Id}: unknown emulator {text}");
                Assert.Contains(system.Id, emu!.Systems);
                if (r.Core != null)
                {
                    Assert.Equal(EmulatorKind.RetroArch, emu.Kind);
                    Assert.True(emu.Cores.TryGetValue(system.Id, out var cores) && cores.Contains(r.Core),
                        $"{system.Id}: core {r.Core} not in {emu.Id} cores");
                }
            }
        }
    }

    [Fact]
    public void EveryEmulatorSystem_Exists_AndEveryEmulatorIsUsed()
    {
        foreach (var emu in _catalog.Emulators)
        {
            Assert.NotEmpty(emu.Systems);
            foreach (var s in emu.Systems) Assert.True(_catalog.GetSystem(s) != null, $"{emu.Id}: unknown system {s}");
            foreach (var s in emu.Cores.Keys) Assert.True(_catalog.GetSystem(s) != null, $"{emu.Id}: core map for unknown system {s}");
            Assert.Contains(_catalog.Systems, sys => sys.Emulators.Any(r => EmulatorRef.Parse(r).EmulatorId == emu.Id));
        }
    }

    [Fact]
    public void FolderNames_AreUniqueAcrossSystems_AndCanonicalFolderIsId()
    {
        var all = _catalog.Systems.SelectMany(s => s.Folders).ToList();
        Assert.Equal(all.Count, all.Distinct(StringComparer.OrdinalIgnoreCase).Count());
        foreach (var s in _catalog.Systems)
        {
            Assert.Equal(s.Id, s.Folders[0]);
            Assert.Same(s, _catalog.SystemForFolder(s.Id));
        }
    }

    [Fact]
    public void Extensions_AreNormalised()
    {
        var rx = new Regex(@"^\.[a-z0-9]+$");
        foreach (var s in _catalog.Systems)
        {
            Assert.NotEmpty(s.Extensions);
            foreach (var e in s.Extensions.Concat(s.PlaylistExtensions)) Assert.Matches(rx, e);
            Assert.Equal(s.Extensions.Length, s.Extensions.Distinct().Count());
            foreach (var p in s.PlaylistExtensions) Assert.Contains(p, s.Extensions);
        }
    }

    [Fact]
    public void SystemsForExtension_Iso_ReturnsSeveralSystems_InAnyNotation()
    {
        var iso = _catalog.SystemsForExtension(".iso").Select(s => s.Id).ToList();
        Assert.True(iso.Count > 1);
        foreach (var id in new[] { "ps2", "psp", "gamecube", "wii" }) Assert.Contains(id, iso);
        Assert.Equal(iso, _catalog.SystemsForExtension("ISO").Select(s => s.Id));
        Assert.Equal(iso, _catalog.SystemsForExtension("*.Iso").Select(s => s.Id));
        Assert.Single(_catalog.SystemsForExtension(".gba"));
        Assert.Empty(_catalog.SystemsForExtension(".doesnotexist"));
        Assert.Empty(_catalog.SystemsForExtension(""));
    }

    [Fact]
    public void Lookups_AreCaseInsensitive()
    {
        Assert.Equal("gba", _catalog.GetSystem("GBA")?.Id);
        Assert.Equal("ps1", _catalog.SystemForFolder("PSX")?.Id);
        Assert.Equal("ps1", _catalog.SystemForFolder("PlayStation")?.Id);
        Assert.Equal("gamecube", _catalog.SystemForFolder("gc")?.Id);
        Assert.Equal("megadrive", _catalog.SystemForFolder("Genesis")?.Id);
        Assert.Equal("arcade", _catalog.SystemForFolder("fbneo")?.Id);
        Assert.Equal("pc", _catalog.SystemForFolder("Windows")?.Id);
        Assert.Equal("retroarch", _catalog.GetEmulator("RetroArch")?.Id);
        Assert.Null(_catalog.GetSystem("nope"));
        Assert.Null(_catalog.SystemForFolder(""));
    }

    [Fact]
    public void Arcade_prefers_the_bundled_MAME_core_FBNeo_only_optional()
    {
        // the installer bundles mame_libretro (FBNeo's licence forbids asking for donations, so it is not shipped)
        var arcade = _catalog.GetSystem("arcade")!;
        Assert.Equal(["retroarch/mame", "retroarch/fbneo", "mame"], arcade.Emulators);
        Assert.Equal(["mame", "fbneo"], _catalog.GetEmulator("retroarch")!.Cores["arcade"]);
        // the MAME core looks for BIOS sets in <system dir>/mame/bios
        Assert.Equal("mame/bios/neogeo.zip", Assert.Single(arcade.Bios).File);
    }

    [Fact]
    public void Systems_AreSortedBySortOrderThenName()
    {
        var sorted = _catalog.Systems.OrderBy(s => s.SortOrder).ThenBy(s => s.Name, StringComparer.InvariantCultureIgnoreCase).ToList();
        Assert.Equal(sorted.Select(s => s.Id), _catalog.Systems.Select(s => s.Id));
        Assert.Equal("nes", _catalog.Systems[0].Id);
        Assert.Equal("pc", _catalog.Systems[^1].Id);
    }

    [Fact]
    public void Emulators_HaveConsistentModes_AndArguments()
    {
        foreach (var e in _catalog.Emulators)
        {
            Assert.Contains(e.DefaultMode, e.Modes);
            if (e.Modes.Contains(LaunchMode.Embedded)) Assert.NotNull(e.Embed);
            Assert.Contains(e.Args, a => a.Contains("{rom}") || a.Contains("{romName}"));
            if (e.Kind != EmulatorKind.Native) Assert.NotEmpty(e.Exe);
            // a fullscreen-capable emulator needs either a flag or the documented fallback in its notes
            if (e.Args.Contains("{fullscreen}")) Assert.NotEmpty(e.FullscreenArgs);
            Assert.True(e.DownloadUrl == null || e.DownloadUrl.StartsWith("https://"), e.Id);
        }
        var ra = _catalog.GetEmulator("retroarch")!;
        Assert.Equal(["-L", "{core}", "--appendconfig", "{retroarchOverride}", "{fullscreen}", "{rom}"], ra.Args);
        Assert.Equal(LaunchMode.Fullscreen, ra.DefaultMode);
        Assert.Equal(LaunchMode.Embedded, _catalog.GetEmulator("mgba")!.DefaultMode);
        Assert.Equal(" | ", _catalog.GetEmulator("azahar")!.Embed!.ReadyTitleContains);
        Assert.DoesNotContain(LaunchMode.Embedded, _catalog.GetEmulator("dolphin")!.Modes);
        var desmume = _catalog.GetEmulator("desmume")!.Embed!;
        Assert.False(desmume.KeepFrame);
        Assert.False(desmume.OffUiThread);
        Assert.True(desmume.NudgeOnMove);
    }

    [Fact]
    public void Sniffers_UseKnownIds()
    {
        foreach (var s in _catalog.Systems)
            foreach (var id in s.Sniffers)
                Assert.True(KnownSniffers.Contains(id), $"{s.Id}: unknown sniffer {id}");
    }

    [Fact]
    public void Bios_Md5AreLowerHex_AndFilesAreRelative()
    {
        var rx = new Regex("^[0-9a-f]{32}$");
        foreach (var s in _catalog.Systems)
            foreach (var b in s.Bios)
            {
                Assert.False(Path.IsPathRooted(b.File));
                Assert.DoesNotContain("..", b.File);
                foreach (var m in b.Md5) Assert.Matches(rx, m);
            }
        Assert.Contains(_catalog.GetSystem("dreamcast")!.Bios, b => b.File == "dc/dc_boot.bin");
    }

    [Fact]
    public void LibretroNames_AreSetForAllConsoleSystems()
    {
        // every console has an online cover source: libretro-thumbnails, or GameTDB where libretro has no set (Switch)
        foreach (var s in _catalog.Systems.Where(s => s.Id != "pc"))
            Assert.True(!string.IsNullOrWhiteSpace(s.LibretroName) ^ !string.IsNullOrWhiteSpace(s.GameTdb), s.Id);
        Assert.Equal("switch", _catalog.GetSystem("switch")!.GameTdb);
        Assert.Null(_catalog.GetSystem("switch")!.LibretroName); // libretro-thumbnails has no Nintendo Switch repository
        Assert.Equal("Nintendo - Game Boy Advance", _catalog.GetSystem("gba")!.LibretroName);
        Assert.Equal("Sony - PlayStation", _catalog.GetSystem("ps1")!.LibretroName);
        Assert.Equal("Sega - Mega Drive - Genesis", _catalog.GetSystem("megadrive")!.LibretroName);
    }

    [Fact]
    public void Helpers_EmulatorsForSystem_RefsAndPlaylist()
    {
        var nds = _catalog.EmulatorsForSystem("NDS").Select(e => e.Id).ToList();
        Assert.Contains("melonds", nds);
        Assert.Contains("desmume", nds);
        Assert.Contains("retroarch", nds);
        Assert.Equal(new EmulatorRef("melonds", null), _catalog.EmulatorRefsFor("nds")[0]);
        // Switch: Eden first (portable, verified flags -f / -g), Ryujinx stays as the alternative
        Assert.Equal([new EmulatorRef("eden", null), new EmulatorRef("ryujinx", null)], _catalog.EmulatorRefsFor("switch"));
        var eden = _catalog.GetEmulator("eden")!;
        Assert.Equal(["{fullscreen}", "-g", "{rom}"], eden.Args);
        Assert.Equal(["-f"], eden.FullscreenArgs);
        Assert.Equal(["eden.exe"], eden.Exe);
        Assert.Equal("eden", eden.Portable);
        Assert.Equal(LaunchMode.Fullscreen, eden.DefaultMode);
        Assert.DoesNotContain(LaunchMode.Embedded, eden.Modes);
        Assert.True(_catalog.IsPlaylistExtension("ps1", ".CUE"));
        Assert.False(_catalog.IsPlaylistExtension("ps1", ".chd"));
    }

    [Fact]
    public void BuildOutput_ContainsTheBuiltInDefinitions()
    {
        // RetroHub.Core copies Resources/** to <output>/resources/** – the referencing test project gets them too
        var output = Path.Combine(AppContext.BaseDirectory, "resources");
        var fromOutput = CatalogImpl.LoadFrom(output, null);
        Assert.Equal(_catalog.Systems.Count, fromOutput.Systems.Count);
        Assert.Equal(_catalog.Emulators.Count, fromOutput.Emulators.Count);
    }
}
