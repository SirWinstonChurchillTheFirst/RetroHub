using RetroHub.Core.Catalog;
using RetroHub.Core.Emulation;

using Xunit;

namespace RetroHub.Core.Tests.Emulation.Config;

/// <summary>Version fallback for exes without a version resource (RetroArch).</summary>
public sealed class EmulatorVersionTests : IDisposable
{
    private const string RealRetroArch = @"D:\RetroHub\emulators\retroarch\RetroArch-Win64\retroarch.exe";
    private readonly TempRoot _t = new();

    public void Dispose() => _t.Dispose();

    [Theory]
    [InlineData("RetroArch - Frontend for libretro\nVersion: 1.22.2 (Git 69a4f0e) Nov 20 2025\nCompiler: MinGW (10.2.0) 64-bit\n", "1.22.2")]
    [InlineData("RetroArch: Frontend for libretro -- v1.9.0 -- 3e8b2a1 --\nCompiler: MSVC", "1.9.0")]
    [InlineData("Version: 1.17\n", "1.17")]
    [InlineData("version: v1.18.0", "1.18.0")]
    [InlineData("Compiler: MinGW (10.2.0) 64-bit", null)]
    [InlineData("", null)]
    [InlineData(null, null)]
    public void Parses_version_output(string? text, string? expected) =>
        Assert.Equal(expected, EmulatorVersionProbe.ParseVersionOutput(text));

    [Fact]
    public void Finds_the_most_frequent_version_string_in_a_binary()
    {
        var text = "\0\0RetroArch-WIMP/1.22.2\0Version: 1.22.2\0libpng version 1.6.37\0Version: 1.22.2 (Git %s)\0Version: 2.0\0";
        Assert.Equal("1.22.2", EmulatorVersionProbe.FromBinaryText(text));
        Assert.Null(EmulatorVersionProbe.FromBinaryText("nothing here"));
    }

    [Fact]
    public void Fake_or_tiny_exes_are_never_executed_or_cached()
    {
        var fake = _t.Touch("emulators/retroarch/retroarch.exe", "not a program");
        var cacheFile = Path.Combine(_t.Root, "data", "cache", "emulator-versions.json");
        var cache = new EmulatorVersionCache(cacheFile);
        var calls = 0;

        Assert.False(EmulatorVersionProbe.LooksLikeExe(fake));
        Assert.Null(cache.GetOrProbe(fake, _ => { calls++; return "9.9"; }));
        cache.Save();

        Assert.Equal(0, calls);
        Assert.False(File.Exists(cacheFile));
    }

    [Fact]
    public void Probe_result_is_cached_by_path_size_and_mtime()
    {
        var exe = _t.Touch("emulators/retroarch/retroarch.exe", FakePe(8192));
        var cacheFile = Path.Combine(_t.Root, "data", "cache", "emulator-versions.json");
        var calls = 0;
        string? Probe(string _) { calls++; return "1.22.2"; }

        var first = new EmulatorVersionCache(cacheFile);
        Assert.Equal("1.22.2", first.GetOrProbe(exe, Probe));
        first.Save();
        Assert.True(File.Exists(cacheFile));

        // a new instance (next start) reads the file: no second probe
        var second = new EmulatorVersionCache(cacheFile);
        Assert.Equal("1.22.2", second.GetOrProbe(exe, Probe));
        Assert.Equal(1, calls);

        // an updated exe (new mtime) is probed again
        File.SetLastWriteTimeUtc(exe, DateTime.UtcNow.AddMinutes(5));
        Assert.Equal("1.22.2", second.GetOrProbe(exe, Probe));
        Assert.Equal(2, calls);

        // "no version" is cached as well
        var other = _t.Touch("emulators/other/other.exe", FakePe(8192));
        Assert.Null(second.GetOrProbe(other, _ => { calls++; return null; }));
        Assert.Null(second.GetOrProbe(other, _ => { calls++; return null; }));
        Assert.Equal(3, calls);
    }

    [Fact]
    public void Registry_uses_the_cached_probe_for_retroarch_only()
    {
        var catalog = new FakeCatalog()
            .Add(new EmulatorDef { Id = "retroarch", Name = "RetroArch", Kind = EmulatorKind.RetroArch, Exe = ["retroarch.exe"] })
            .Add(new EmulatorDef { Id = "other", Name = "Other", Exe = ["other.exe"] });
        var ra = _t.Touch("emulators/retroarch/RetroArch-Win64/retroarch.exe", FakePe(8192));
        _t.Touch("emulators/other/other.exe", FakePe(8192));
        var cacheFile = Path.Combine(_t.Root, "data", "cache", "emulator-versions.json");
        // pre-seeded cache entry for the fake RetroArch (a fake PE cannot actually be executed)
        var info = new FileInfo(ra);
        File.WriteAllText(Path.Combine(Directory.CreateDirectory(Path.GetDirectoryName(cacheFile)!).FullName, "emulator-versions.json"),
            $$"""{ "{{Path.GetFullPath(ra).ToLowerInvariant().Replace("\\", "\\\\")}}": { "size": {{info.Length}}, "mtime": {{info.LastWriteTimeUtc.Ticks}}, "version": "1.22.2" } }""");

        var reg = new EmulatorRegistry(catalog, _t.Paths, _t.Settings, cacheFile);

        Assert.Equal("1.22.2", reg.Get("retroarch")!.Version);
        Assert.Null(reg.Get("other")!.Version); // standalone emulators are never executed for a version
    }

    [Theory]
    [InlineData("\0Dolphin Version: \0Dolphin 2609b\0HEAD\0Dolphin/2609b\0dolphin-emu.org\0", "2609b")]
    [InlineData("\0Dolphin 5.0-21460\0Dolphin/5.0-21460\0", "5.0-21460")]
    [InlineData("\0Dolphin 2412\0", "2412")]
    [InlineData("\0Dolphin is too old for traversal server\0Dolphin Emulator\0Dolphin 2003-2024 team members\0", null)]
    [InlineData("", null)]
    public void Reads_the_dolphin_version_from_its_binary(string text, string? expected) =>
        Assert.Equal(expected, EmulatorVersionProbe.DolphinFromBinaryText(text));

    [Fact]
    public void Registry_reads_dolphins_version_from_the_binary_without_running_it()
    {
        var catalog = new FakeCatalog()
            .Add(new EmulatorDef { Id = "dolphin", Name = "Dolphin", Exe = ["Dolphin.exe"] });
        var bytes = FakePe(16384);
        var marker = System.Text.Encoding.ASCII.GetBytes("\0Dolphin 2609b\0Dolphin/2609b\0");
        marker.CopyTo(bytes, 5000);
        var exe = _t.Touch("emulators/dolphin/Dolphin-x64/Dolphin.exe", bytes);
        var cacheFile = Path.Combine(_t.Root, "data", "cache", "emulator-versions.json");

        var reg = new EmulatorRegistry(catalog, _t.Paths, _t.Settings, cacheFile);

        Assert.Equal("2609b", reg.Get("dolphin")!.Version);
        Assert.True(File.Exists(cacheFile)); // cached: the 20 MB binary is not read on every start
        Assert.Contains("2609b", File.ReadAllText(cacheFile));
        Assert.True(EmulatorVersionProbe.IsDolphin(new EmulatorDef { Id = "x", Name = "x", Exe = [] }, exe));
    }

    [Fact]
    public void Real_dolphin_reports_its_version()
    {
        const string realDolphin = @"D:\RetroHub\emulators\dolphin\Dolphin-x64\Dolphin.exe";
        Assert.SkipUnless(File.Exists(realDolphin), "Dolphin test install not present");
        var v = EmulatorVersionProbe.DolphinFromBinary(realDolphin);
        TestContext.Current.TestOutputHelper?.WriteLine($"Dolphin binary scan: {v}");
        Assert.NotNull(v);
        Assert.Matches(@"^\d{4}[a-z]?(-\d+)?$|^\d+\.\d+", v);
    }

    [Fact]
    public void Real_retroarch_reports_its_version()
    {
        Assert.SkipUnless(File.Exists(RealRetroArch), "RetroArch test install not present");
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var fromProcess = EmulatorVersionProbe.FromProcess(RealRetroArch, "--version");
        var processMs = sw.ElapsedMilliseconds;
        sw.Restart();
        var fromBinary = EmulatorVersionProbe.FromBinary(RealRetroArch);
        TestContext.Current.TestOutputHelper?.WriteLine($"--version: {fromProcess} ({processMs} ms); binary scan: {fromBinary} ({sw.ElapsedMilliseconds} ms)");

        Assert.Equal("1.22.2", fromProcess);
        Assert.Equal("1.22.2", fromBinary);
        // (no "RetroArch is not running any more" check: other test runs may use the same install concurrently;
        // FromProcess only returns after the process exited or was killed)
    }

    /// <summary>Bytes that pass the "MZ + at least 4 KB" check without being a runnable program.</summary>
    private static byte[] FakePe(int size)
    {
        var b = new byte[size];
        b[0] = (byte)'M';
        b[1] = (byte)'Z';
        return b;
    }
}
