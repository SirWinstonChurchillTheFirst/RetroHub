using RetroHub.Core.Catalog;
using Xunit;

namespace RetroHub.Core.Tests.Catalog;

using CatalogImpl = RetroHub.Core.Catalog.Catalog;

/// <summary>Regression tests for defects found in review of the catalog module.</summary>
public sealed class ReviewRegressionTests : IDisposable
{
    private readonly CatalogTestEnv _env = new();
    public void Dispose() => _env.Dispose();

    [Fact]
    public void UndefinedEnumNumbers_InPlugin_AreRejected()
    {
        _env.WritePluginEmulators("nums.json", """
            [{ "id": "numemu", "name": "Num", "exe": ["n.exe"], "systems": ["gba"], "kind": 7 },
             { "id": "modeemu", "name": "Mode", "exe": ["m.exe"], "systems": ["gba"], "kind": 0, "defaultMode": 9, "modes": [9, "Windowed"] }]
            """);
        _env.WritePluginSystems("nums.json", """
            [{ "id": "numsys", "name": "Num", "extensions": [".num"], "category": 42 }]
            """);
        var c = CatalogImpl.Load(_env.Paths);

        foreach (var e in c.Emulators)
        {
            Assert.True(Enum.IsDefined(e.Kind), $"{e.Id}: kind {(int)e.Kind}");
            Assert.True(Enum.IsDefined(e.DefaultMode), $"{e.Id}: defaultMode {(int)e.DefaultMode}");
            Assert.All(e.Modes, m => Assert.True(Enum.IsDefined(m), $"{e.Id}: mode {(int)m}"));
        }
        foreach (var s in c.Systems) Assert.True(Enum.IsDefined(s.Category), $"{s.Id}: category {(int)s.Category}");

        Assert.Null(c.GetEmulator("numemu")); // unknown kind → entry skipped (it would be started the wrong way)
        var mode = c.GetEmulator("modeemu")!;
        Assert.Equal(EmulatorKind.Standalone, mode.Kind);
        Assert.Equal([LaunchMode.Windowed], mode.Modes);
        Assert.Equal(LaunchMode.Windowed, mode.DefaultMode);
        Assert.Equal(SystemCategory.Console, c.GetSystem("numsys")!.Category);
        Assert.Contains(c.LoadWarnings, w => w.Contains("numemu"));
        Assert.Contains(c.LoadWarnings, w => w.Contains("numsys") && w.Contains("42"));
    }

    [Fact]
    public void UndefinedEnums_FromDefinitions_AreCorrected()
    {
        var c = CatalogImpl.FromDefinitions(
            [new SystemDef { Id = "s", Name = "S", Extensions = [".s"], Category = (SystemCategory)99 }],
            [new EmulatorDef { Id = "e", Name = "E", Exe = ["e.exe"], Systems = ["s"], DefaultMode = (LaunchMode)5, Modes = [(LaunchMode)5] }]);
        Assert.Equal(SystemCategory.Console, c.GetSystem("s")!.Category);
        var e = c.GetEmulator("e")!;
        Assert.Equal([LaunchMode.Fullscreen], e.Modes);
        Assert.Equal(LaunchMode.Fullscreen, e.DefaultMode);
    }

    [Fact]
    public void IdsEndingWithDot_AreRejected_BecauseWindowsStripsTheDot()
    {
        // Windows resolves "saves\gba." to "saves\gba": such an id would share every folder of the built-in "gba"
        Assert.Equal(Path.GetFullPath(@"C:\x\saves\gba"), Path.GetFullPath(@"C:\x\saves\gba."));
        _env.WritePluginSystems("dot.json", """[{ "id": "gba.", "name": "GBA Punkt", "extensions": [".gbx"] }]""");
        _env.WritePluginEmulators("dot.json", """[{ "id": "mgba..", "name": "mGBA Punkt", "exe": ["x.exe"], "systems": ["gba"] }]""");
        var c = CatalogImpl.Load(_env.Paths);

        Assert.DoesNotContain(c.Systems, s => s.Id.EndsWith('.'));
        Assert.DoesNotContain(c.Emulators, e => e.Id.EndsWith('.'));
        Assert.Equal(2, c.LoadWarnings.Count(w => w.Contains("ungültiger ID")));
        Assert.NotNull(CatalogImpl.FromDefinitions([new SystemDef { Id = "a.b", Name = "ok" }], []).GetSystem("a.b"));
    }

    [Fact]
    public void FullscreenPlaceholder_InsideAToken_Warns()
    {
        _env.WritePluginEmulators("fs.json", """
            [{ "id": "fsemu", "name": "Fs", "exe": ["fs.exe"], "systems": ["gba"], "args": ["--mode={fullscreen}", "{rom}"],
               "fullscreenArgs": ["-f", "{fullscreen}"] }]
            """);
        var c = CatalogImpl.Load(_env.Paths);
        Assert.Contains(c.LoadWarnings, w => w.Contains("fsemu") && w.Contains("--mode={fullscreen}"));
        Assert.Contains(c.LoadWarnings, w => w.Contains("fsemu") && w.Contains("Vollbild-Argumente"));
    }

    [Fact]
    public void PluginFile_WithUtf8Bom_Loads()
    {
        // older Windows editors save "UTF-8 with BOM"
        File.WriteAllText(Path.Combine(_env.PluginSystems, "bom.json"),
            """[{ "id": "bomsys", "name": "Größe", "extensions": [".bom"] }]""", new System.Text.UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
        var c = CatalogImpl.Load(_env.Paths);
        Assert.Empty(c.LoadWarnings);
        Assert.Equal("Größe", c.GetSystem("bomsys")!.Name);
    }

    [Fact]
    public void NegativeBarHeights_AreClamped()
    {
        _env.WritePluginEmulators("bars.json", """
            [{ "id": "baremu", "name": "Bars", "exe": ["b.exe"], "systems": ["gba"], "modes": ["Embedded"], "defaultMode": "Embedded",
               "embed": { "barTop": -5, "barBottom": 100000 } }]
            """);
        var c = CatalogImpl.Load(_env.Paths);
        var embed = c.GetEmulator("baremu")!.Embed!;
        Assert.Equal(0, embed.BarTop);
        Assert.InRange(embed.BarBottom, 0, 1000);
        Assert.Equal(2, c.LoadWarnings.Count(w => w.Contains("baremu")));
    }

    [Fact]
    public void PublicLists_CannotBeMutatedByCasting()
    {
        var c = CatalogImpl.Load(_env.Paths);
        Assert.False(c.Systems is List<SystemDef>);
        Assert.False(c.Emulators is List<EmulatorDef>);
        Assert.False(c.LoadWarnings is List<string>);
    }

    [Fact]
    public void BuiltInAccentColours_AreVisibleOnTheDarkTheme()
    {
        // Nova base background is #0B0A1A; an accent must stand out from it (relative luminance check)
        var c = CatalogImpl.Load(_env.Paths);
        foreach (var s in c.Systems)
        {
            Assert.NotNull(s.Accent);
            var ratio = Contrast(Luminance(s.Accent!), Luminance("#0B0A1A"));
            Assert.True(ratio >= 2.0, $"{s.Id}: accent {s.Accent} contrast {ratio:0.00} against #0B0A1A");
        }
    }

    private static double Luminance(string hex)
    {
        static double Ch(int v) { var c = v / 255.0; return c <= 0.03928 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4); }
        var r = Convert.ToInt32(hex.Substring(1, 2), 16);
        var g = Convert.ToInt32(hex.Substring(3, 2), 16);
        var b = Convert.ToInt32(hex.Substring(5, 2), 16);
        return 0.2126 * Ch(r) + 0.7152 * Ch(g) + 0.0722 * Ch(b);
    }

    private static double Contrast(double a, double b) => (Math.Max(a, b) + 0.05) / (Math.Min(a, b) + 0.05);
}
