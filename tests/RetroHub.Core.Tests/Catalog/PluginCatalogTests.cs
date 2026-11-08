using RetroHub.Core.Catalog;
using Xunit;

namespace RetroHub.Core.Tests.Catalog;

using CatalogImpl = RetroHub.Core.Catalog.Catalog;

/// <summary>Plugin overrides, broken files and validation of untrusted definitions.</summary>
public sealed class PluginCatalogTests : IDisposable
{
    private readonly CatalogTestEnv _env = new();
    public void Dispose() => _env.Dispose();

    [Fact]
    public void PluginSystem_WithSameId_ReplacesBuiltIn()
    {
        _env.WritePluginSystems("my-gba.json", """
            // comments and trailing commas are allowed
            [
              {
                "id": "GBA",
                "name": "Mein GBA",
                "folders": ["gba", "agb"],
                "extensions": ["GBA", "*.Agb", ".zip",],
                "emulators": ["retroarch/mgba"],
                "sortOrder": 1,
              },
            ]
            """);
        var c = CatalogImpl.Load(_env.Paths);

        Assert.Empty(c.LoadWarnings);
        var gba = c.GetSystem("gba")!;
        Assert.Equal("gba", gba.Id);
        Assert.Equal("Mein GBA", gba.Name);
        Assert.Equal([".gba", ".agb", ".zip"], gba.Extensions);
        Assert.Equal(["retroarch/mgba"], gba.Emulators);
        Assert.Empty(gba.Bios); // full replacement, not a merge
        Assert.Same(gba, c.SystemForFolder("agb"));
        Assert.Same(gba, c.Systems[0]); // SortOrder 1
        Assert.Single(c.Systems, s => s.Id == "gba");
    }

    [Fact]
    public void PluginEmulator_WithSameId_ReplacesBuiltIn_AndNewEmulatorCanBeAdded()
    {
        _env.WritePluginEmulators("emus.json", """
            [
              { "id": "mgba", "name": "mGBA Nightly", "exe": ["mGBA-nightly.exe"], "systems": ["gb", "gbc", "gba"],
                "defaultMode": "windowed", "modes": ["Windowed"] },
              { "id": "skyemu", "name": "SkyEmu", "exe": ["SkyEmu.exe"], "systems": ["gba", "gb", "gbc", "nds"] }
            ]
            """);
        _env.WritePluginSystems("gba.json", """
            [{ "id": "gba", "name": "Game Boy Advance", "extensions": [".gba"], "emulators": ["skyemu", "mgba"] }]
            """);
        var c = CatalogImpl.Load(_env.Paths);

        Assert.Empty(c.LoadWarnings);
        var mgba = c.GetEmulator("mgba")!;
        Assert.Equal("mGBA Nightly", mgba.Name);
        Assert.Equal(LaunchMode.Windowed, mgba.DefaultMode);
        Assert.Null(mgba.Embed);
        Assert.Equal(["skyemu", "mgba"], c.GetSystem("gba")!.Emulators);
        Assert.Equal(["{rom}"], c.GetEmulator("skyemu")!.Args); // default
    }

    [Fact]
    public void BrokenPluginFile_GivesWarning_NotException()
    {
        _env.WritePluginSystems("broken.json", "[ { \"id\": \"x\", ");
        _env.WritePluginEmulators("notarray.json", "\"hello\"");
        var c = CatalogImpl.Load(_env.Paths);

        Assert.Contains(c.LoadWarnings, w => w.Contains("broken.json") && w.Contains("ungültiges JSON"));
        Assert.Contains(c.LoadWarnings, w => w.Contains("notarray.json"));
        Assert.NotNull(c.GetSystem("gba")); // built-ins still there
        Assert.Null(c.GetSystem("x"));
    }

    [Fact]
    public void BadEntry_IsSkipped_OtherEntriesOfTheFileLoad()
    {
        _env.WritePluginSystems("mixed.json", """
            [
              { "name": "ohne ID" },
              { "id": "../evil", "name": "Pfad" },
              { "id": "good", "name": "Gut", "extensions": [".good"], "category": "NoSuchCategory" },
              { "id": "fine", "name": "Fein", "extensions": [".fine"] }
            ]
            """);
        var c = CatalogImpl.Load(_env.Paths);

        Assert.NotNull(c.GetSystem("fine"));
        Assert.Null(c.GetSystem("good")); // bad enum value → entry skipped
        Assert.Null(c.GetSystem("../evil"));
        Assert.Contains(c.LoadWarnings, w => w.Contains("Eintrag 1"));
        Assert.Contains(c.LoadWarnings, w => w.Contains("../evil"));
        Assert.Contains(c.LoadWarnings, w => w.Contains("Eintrag 3"));
    }

    [Fact]
    public void UnknownEmulatorRef_GivesWarning_AndIsDropped()
    {
        _env.WritePluginSystems("sys.json", """
            [{ "id": "jaguar", "name": "Atari Jaguar", "extensions": [".j64"],
               "emulators": ["bigpemu", "retroarch/virtualjaguar", "mgba/whatever"] }]
            """);
        var c = CatalogImpl.Load(_env.Paths);

        var jag = c.GetSystem("jaguar")!;
        Assert.Contains(c.LoadWarnings, w => w.Contains("bigpemu"));
        Assert.Contains(c.LoadWarnings, w => w.Contains("virtualjaguar"));
        Assert.Contains(c.LoadWarnings, w => w.Contains("kein RetroArch-Emulator"));
        Assert.DoesNotContain("bigpemu", jag.Emulators);
        Assert.Contains("retroarch/virtualjaguar", jag.Emulators); // emulator exists, core just not mapped → kept, warned
        Assert.Contains("mgba", jag.Emulators); // core part removed
    }

    [Fact]
    public void UnknownProperty_InPlugin_GivesWarning_ButLoads()
    {
        _env.WritePluginSystems("typo.json", """
            [{ "$schema": "ignored", "id": "typo", "name": "Typo", "extentions": [".x"], "bios": [{ "file": "a.bin", "reqired": true }] }]
            """);
        var c = CatalogImpl.Load(_env.Paths);

        Assert.NotNull(c.GetSystem("typo"));
        var w = Assert.Single(c.LoadWarnings, w => w.Contains("typo.json"));
        Assert.Contains("extentions", w);
        Assert.Contains("bios[0].reqired", w);
        Assert.DoesNotContain("$schema", w);
    }

    [Fact]
    public void PluginFolderConflict_PluginWins_BuiltInLosesTheFolder()
    {
        _env.WritePluginSystems("ps.json", """
            [{ "id": "psxhacks", "name": "PS1 Hacks", "folders": ["psxhacks", "psx"], "extensions": [".cue"], "emulators": ["duckstation"] }]
            """);
        var c = CatalogImpl.Load(_env.Paths);

        Assert.Equal("psxhacks", c.SystemForFolder("psx")?.Id);
        Assert.DoesNotContain("psx", c.GetSystem("ps1")!.Folders);
        Assert.Contains(c.LoadWarnings, w => w.Contains("„psx“"));
        // duckstation does not list psxhacks in its systems → consistency warning
        Assert.Contains(c.LoadWarnings, w => w.Contains("duckstation") && w.Contains("psxhacks"));
    }

    [Fact]
    public void UnsafeValuesFromPlugins_AreDropped()
    {
        _env.WritePluginEmulators("evil.json", """
            [{ "id": "evil", "name": "Evil", "exe": ["..\\..\\cmd.exe", "C:\\Windows\\x.exe", "ok*.exe"],
               "systems": ["gba"], "downloadUrl": "file:///C:/Windows/System32/calc.exe", "website": "javascript:alert(1)",
               "args": ["{rom}", "{bogus}"] }]
            """);
        _env.WritePluginSystems("evil.json", """
            [{ "id": "evilsys", "name": "Evil", "folders": ["..", "a/b", "fine"], "extensions": ["../x", ".ok", ".a.b"],
               "bios": [{ "file": "../outside.bin" }, { "file": "C:/abs.bin" }, { "file": "sub/in.bin", "md5": ["XYZ", "A860E8C0B6D573D191E4EC7DB1B1E4F6"] }],
               "accent": "red", "aspectRatio": -1 }]
            """);
        var c = CatalogImpl.Load(_env.Paths);

        var evil = c.GetEmulator("evil")!;
        Assert.Equal(["ok*.exe"], evil.Exe);
        Assert.Null(evil.DownloadUrl);
        Assert.Null(evil.Website);
        Assert.Contains(c.LoadWarnings, w => w.Contains("{bogus}"));

        var sys = c.GetSystem("evilsys")!;
        Assert.Equal(["fine"], sys.Folders);
        Assert.Equal([".ok"], sys.Extensions);
        var bios = Assert.Single(sys.Bios);
        Assert.Equal("sub/in.bin", bios.File);
        Assert.Equal(["a860e8c0b6d573d191e4ec7db1b1e4f6"], bios.Md5);
        Assert.Null(sys.Accent);
        Assert.Null(sys.AspectRatio);
    }

    [Fact]
    public void DuplicateIdsAcrossPluginFiles_LaterWins_WithWarning()
    {
        _env.WritePluginSystems("a.json", """[{ "id": "dup", "name": "A", "extensions": [".dup"] }]""");
        _env.WritePluginSystems("b.json", """[{ "id": "dup", "name": "B", "extensions": [".dup"] }]""");
        var c = CatalogImpl.Load(_env.Paths);

        Assert.Equal("B", c.GetSystem("dup")!.Name);
        Assert.Contains(c.LoadWarnings, w => w.Contains("dup") && w.Contains("a.json"));
    }

    [Fact]
    public void MissingPluginFolder_IsFine()
    {
        Directory.Delete(_env.Paths.Plugins, recursive: true);
        var c = CatalogImpl.Load(_env.Paths);
        Assert.Empty(c.LoadWarnings);
    }

    [Fact]
    public void BrokenBuiltInFile_Throws()
    {
        File.WriteAllText(Path.Combine(_env.Paths.Resources, "systems", "zz-broken.json"), "[ { ");
        var ex = Assert.Throws<CatalogLoadException>(() => CatalogImpl.Load(_env.Paths));
        Assert.EndsWith("zz-broken.json", ex.Path);
    }

    [Fact]
    public void BuiltInEntryWithUnknownProperty_Throws()
    {
        File.WriteAllText(Path.Combine(_env.Paths.Resources, "systems", "zz-typo.json"),
            """[{ "id": "zz", "name": "ZZ", "extentions": [".zz"] }]""");
        Assert.Throws<CatalogLoadException>(() => CatalogImpl.Load(_env.Paths));
    }

    [Fact]
    public void DuplicateBuiltInId_Throws()
    {
        File.WriteAllText(Path.Combine(_env.Paths.Resources, "systems", "zz-dup.json"),
            """[{ "id": "gba", "name": "Noch ein GBA", "extensions": [".gba"] }]""");
        Assert.Throws<CatalogLoadException>(() => CatalogImpl.Load(_env.Paths));
    }

    [Fact]
    public void MissingBuiltInFolder_Throws()
    {
        using var empty = new CatalogTestEnv(copyBuiltIns: false);
        Assert.Throws<CatalogLoadException>(() => CatalogImpl.Load(empty.Paths));
    }
}
