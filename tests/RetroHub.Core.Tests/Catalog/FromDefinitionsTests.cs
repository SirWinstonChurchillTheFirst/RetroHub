using RetroHub.Core.Catalog;
using Xunit;

namespace RetroHub.Core.Tests.Catalog;

using CatalogImpl = RetroHub.Core.Catalog.Catalog;

public sealed class FromDefinitionsTests
{
    private static SystemDef Sys(string id, params string[] ext) => new() { Id = id, Name = id.ToUpperInvariant(), Extensions = ext };

    [Fact]
    public void Normalises_IdsExtensionsAndFolders()
    {
        var c = CatalogImpl.FromDefinitions(
            [new SystemDef { Id = " GBA ", Name = "Game Boy Advance", Extensions = ["GBA", ".Zip", "gba", " *.7Z "], Emulators = ["MGBA"] }],
            [new EmulatorDef { Id = "mGBA", Name = "mGBA", Exe = ["mGBA.exe"], Systems = ["gba"] }]);

        Assert.Empty(c.LoadWarnings);
        var gba = Assert.Single(c.Systems);
        Assert.Equal("gba", gba.Id);
        Assert.Equal([".gba", ".zip", ".7z"], gba.Extensions);
        Assert.Equal(["gba"], gba.Folders); // default folder = id
        Assert.Equal(["mgba"], gba.Emulators);
        Assert.Same(gba, c.SystemsForExtension(".7z")[0]);
    }

    [Fact]
    public void RetroArch_SystemsAreUnionOfCoreMapping_UnknownSystemsWarn()
    {
        var c = CatalogImpl.FromDefinitions(
            [Sys("nes", ".nes"), Sys("snes", ".sfc")],
            [new EmulatorDef
            {
                Id = "retroarch", Name = "RetroArch", Kind = EmulatorKind.RetroArch, Exe = ["retroarch.exe"],
                Systems = ["nes"],
                Cores = new() { ["SNES"] = ["Snes9x"], ["nes"] = ["fceumm"], ["jaguar"] = ["virtualjaguar"] },
            }]);

        var ra = c.GetEmulator("retroarch")!;
        Assert.Equal(["nes", "snes"], ra.Systems.Order());
        Assert.Equal(["snes9x"], ra.Cores["snes"]);
        Assert.False(ra.Cores.ContainsKey("jaguar"));
        Assert.Contains(c.LoadWarnings, w => w.Contains("jaguar"));
    }

    [Fact]
    public void DuplicateIds_LaterWins_WithWarning()
    {
        var c = CatalogImpl.FromDefinitions([Sys("x", ".a"), Sys("X", ".b")], []);
        Assert.Equal([".b"], Assert.Single(c.Systems).Extensions);
        Assert.Single(c.LoadWarnings);
    }

    [Fact]
    public void DefaultModeNotInModes_IsCorrected_EmbeddedGetsProfile()
    {
        var c = CatalogImpl.FromDefinitions([Sys("gba", ".gba")],
            [new EmulatorDef { Id = "e", Name = "E", Exe = ["e.exe"], Systems = ["gba"], DefaultMode = LaunchMode.Fullscreen, Modes = [LaunchMode.Embedded] }]);
        var e = c.GetEmulator("e")!;
        Assert.Equal(LaunchMode.Embedded, e.DefaultMode);
        Assert.NotNull(e.Embed);
        Assert.Equal(2, c.LoadWarnings.Count);
    }

    [Fact]
    public void NullCollections_FromJsonNulls_DoNotCrash()
    {
        // System.Text.Json writes null into non-nullable arrays for "extensions": null – the builder must cope
        var c = CatalogImpl.FromDefinitions(
            [new SystemDef { Id = "n", Name = "N", Extensions = null!, Folders = null!, Emulators = null!, Bios = null!, Sniffers = null!, PlaylistExtensions = null! }],
            [new EmulatorDef { Id = "e", Name = "E", Exe = null!, Systems = null!, Args = null!, FullscreenArgs = null!, Cores = null!, Modes = null! }]);
        Assert.Equal(["n"], c.GetSystem("n")!.Folders);
        Assert.Equal(["{rom}"], c.GetEmulator("e")!.Args);
    }

    [Fact]
    public void FolderConflictBetweenDefinitions_FirstKeepsIt()
    {
        var c = CatalogImpl.FromDefinitions(
            [new SystemDef { Id = "a", Name = "A", Folders = ["a", "shared"] }, new SystemDef { Id = "b", Name = "B", Folders = ["b", "Shared"] }], []);
        Assert.Equal("a", c.SystemForFolder("SHARED")!.Id);
        Assert.Equal(["b"], c.GetSystem("b")!.Folders);
        Assert.Single(c.LoadWarnings);
    }
}
