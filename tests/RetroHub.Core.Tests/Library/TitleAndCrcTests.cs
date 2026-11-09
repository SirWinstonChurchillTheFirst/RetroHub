using System.Diagnostics;
using System.Text;
using RetroHub.Core.Library;
using Xunit;

namespace RetroHub.Core.Tests.Library;

public sealed class TitleParserTests
{
    [Theory]
    [InlineData("Super Mario World (USA).sfc", "Super Mario World", "Super Mario World", "USA")]
    [InlineData("Legend of Zelda, The - A Link to the Past (USA) (Rev 1) [!].sfc", "The Legend of Zelda - A Link to the Past", "Legend of Zelda - A Link to the Past", "USA")]
    [InlineData("Legend of Zelda, The (Europe).nes", "The Legend of Zelda", "Legend of Zelda", "Europe")]
    [InlineData("Pokemon - Emerald Version (USA, Europe).gba", "Pokemon - Emerald Version", "Pokemon - Emerald Version", "USA, Europe")]
    [InlineData("Sonic_the_Hedgehog_(W)_[!].md", "Sonic the Hedgehog", "Sonic the Hedgehog", "World")]
    [InlineData("Street Fighter II (U) [h1].smc", "Street Fighter II", "Street Fighter II", "USA")]
    [InlineData("Mario Kart 64 (Germany) (Beta).z64", "Mario Kart 64", "Mario Kart 64", "Germany")]
    [InlineData("Die Siedler (Germany).zip", "Die Siedler", "Siedler", "Germany")]
    [InlineData("A Boy and His Blob (USA).nes", "A Boy and His Blob", "Boy and His Blob", "USA")]
    [InlineData("Final Fantasy VII (USA) (Disc 1).cue", "Final Fantasy VII", "Final Fantasy VII", "USA")]
    [InlineData("Tetris.gb", "Tetris", "Tetris", null)]
    [InlineData("240p Test Suite (Homebrew).nes", "240p Test Suite", "240p Test Suite", null)]
    [InlineData("Castlevania (Proto) [b].nes", "Castlevania", "Castlevania", null)]
    [InlineData("(USA).gba", "(USA)", "(USA)", "USA")]
    // language tags of personal collections tell versions apart instead of vanishing
    [InlineData("The Legend of Zelda - A Link to the Past (Deutsch).sfc", "The Legend of Zelda - A Link to the Past", "Legend of Zelda - A Link to the Past", "Deutsch")]
    [InlineData("The Legend of Zelda - A Link to the Past (Englisch, NTSC).sfc", "The Legend of Zelda - A Link to the Past", "Legend of Zelda - A Link to the Past", "Englisch, NTSC")]
    [InlineData("Zelda no Densetsu - Kamigami no Triforce (Japanisch).sfc", "Zelda no Densetsu - Kamigami no Triforce", "Zelda no Densetsu - Kamigami no Triforce", "Japanisch")]
    [InlineData("Metroid (German).nes", "Metroid", "Metroid", "German")]
    [InlineData("BS The Legend of Zelda - Kapitel 1 (Englisch).sfc", "BS The Legend of Zelda - Kapitel 1", "BS The Legend of Zelda - Kapitel 1", "Englisch")]
    [InlineData("Hyrule Warriors (Definitive Edition).nsp", "Hyrule Warriors", "Hyrule Warriors", null)]
    public void Parses_no_intro_and_goodtools_names(string file, string title, string sort, string? region)
    {
        var p = TitleParser.Parse(file);
        Assert.Equal(title, p.Title);
        Assert.Equal(sort, p.SortTitle);
        Assert.Equal(region, p.Region);
    }

    [Fact]
    public void Folder_names_keep_dots()
    {
        var p = TitleParser.Parse("Sam & Max Ep.1", isFileName: false);
        Assert.Equal("Sam & Max Ep.1", p.Title);
    }

    [Theory]
    [InlineData("0001", true)]
    [InlineData("1234 5678", true)]
    [InlineData("SLUS_203.12", true)]
    [InlineData("NTR-IRBO", true)]
    [InlineData("rom", true)]
    [InlineData("Disc 1", true)]
    [InlineData("DEADBEEF01", true)]
    [InlineData("Tetris", false)]
    [InlineData("Super Mario World", false)]
    [InlineData("1942 Joint Strike", false)]
    public void Detects_meaningless_names(string name, bool meaningless) => Assert.Equal(meaningless, TitleParser.IsMeaningless(name));
}

public sealed class Crc32Tests
{
    [Fact]
    public void Known_check_values()
    {
        Assert.Equal(0xCBF43926u, Crc32.Compute("123456789"u8));
        Assert.Equal(0u, Crc32.Compute([]));
        Assert.Equal(0x414FA339u, Crc32.Compute(Encoding.ASCII.GetBytes("The quick brown fox jumps over the lazy dog")));
        Assert.Equal("CBF43926", Crc32.ToHex(0xCBF43926u));
    }

    [Fact]
    public void Streaming_equals_one_shot_and_bitwise_reference()
    {
        var data = RomImages.Random(3 * 1024 * 1024 + 13, seed: 77);
        var oneShot = Crc32.Compute(data);
        using var ms = new MemoryStream(data);
        Assert.Equal(oneShot, Crc32.Compute(ms, TestContext.Current.CancellationToken));

        var state = 0xFFFFFFFFu;
        foreach (var chunk in data.Chunk(1000)) state = Crc32.Append(state, chunk);
        Assert.Equal(oneShot, ~state);

        // Bit-at-a-time reference on a slice.
        var slice = data.AsSpan(0, 10_001);
        var crc = 0xFFFFFFFFu;
        foreach (var b in slice)
        {
            crc ^= b;
            for (var k = 0; k < 8; k++) crc = (crc & 1) != 0 ? (crc >> 1) ^ 0xEDB88320u : crc >> 1;
        }
        Assert.Equal(~crc, Crc32.Compute(slice));
    }

    [Fact]
    public void Throughput_is_sufficient()
    {
        var data = RomImages.Random(64 * 1024 * 1024, seed: 1);
        Crc32.Compute(data.AsSpan(0, 1024 * 1024)); // warm-up
        var sw = Stopwatch.StartNew();
        Crc32.Compute(data);
        sw.Stop();
        var mbPerSec = 64 / sw.Elapsed.TotalSeconds;
        TestContext.Current.TestOutputHelper?.WriteLine($"CRC32 throughput: {mbPerSec:0} MB/s");
        Assert.True(mbPerSec > 150, $"CRC32 throughput only {mbPerSec:0} MB/s");
    }
}

public sealed class PlaylistParserTests : IDisposable
{
    private readonly TempRoot _tmp = new();
    public void Dispose() => _tmp.Dispose();

    [Fact]
    public void Cue_file_lines_quoted_and_unquoted()
    {
        _tmp.Write("d/a (Track 1).bin", new byte[10]);
        _tmp.Write("d/b.bin", new byte[10]);
        var cue = _tmp.Write("d/game.cue",
            "FILE \"a (Track 1).bin\" BINARY\r\n  TRACK 01 MODE2/2352\r\n    INDEX 01 00:00:00\r\nFILE b.bin BINARY\r\n  TRACK 02 AUDIO\r\n");
        var refs = PlaylistParser.References(cue);
        Assert.Equal([_tmp.Full("d/a (Track 1).bin"), _tmp.Full("d/b.bin")], refs.Select(r => r.FullPath));
        Assert.False(PlaylistParser.Expand(cue).AnyRequiredMissing);
    }

    [Fact]
    public void Cue_with_stale_absolute_path_falls_back_to_bare_name()
    {
        _tmp.Write("d/x.bin", new byte[10]);
        var cue = _tmp.Write("d/x.cue", "FILE \"C:\\old rips\\x.bin\" BINARY\n");
        Assert.Equal(_tmp.Full("d/x.bin"), PlaylistParser.References(cue).Single().FullPath);
    }

    [Fact]
    public void M3u_to_cue_to_bin_is_expanded_and_missing_detected()
    {
        _tmp.Write("d/g1.bin", new byte[10]);
        _tmp.Write("d/g1.cue", "FILE \"g1.bin\" BINARY\n");
        _tmp.Write("d/g2.cue", "FILE \"g2.bin\" BINARY\n"); // g2.bin missing
        var m3u = _tmp.Write("d/g.m3u", "#EXTM3U\n# comment\ng1.cue\ng2.cue\n");
        var (files, missing) = PlaylistParser.Expand(m3u);
        Assert.Contains(_tmp.Full("d/g1.cue"), files);
        Assert.Contains(_tmp.Full("d/g1.bin"), files);
        Assert.Contains(_tmp.Full("d/g2.bin"), files);
        Assert.True(missing);
    }

    [Fact]
    public void Gdi_tracks()
    {
        var gdi = _tmp.Write("dc/game.gdi", "3\n1 0 4 2352 track01.bin 0\n2 600 0 2352 \"track 02.raw\" 0\n3 45000 4 2352 track03.bin 0\n");
        var refs = PlaylistParser.References(gdi);
        Assert.Equal(["track01.bin", "track 02.raw", "track03.bin"], refs.Select(r => Path.GetFileName(r.FullPath)));
    }

    [Fact]
    public void Self_referencing_m3u_does_not_loop()
    {
        var m3u = _tmp.Write("d/loop.m3u", "loop.m3u\nother.m3u\n");
        _tmp.Write("d/other.m3u", "loop.m3u\n");
        var (files, _) = PlaylistParser.Expand(m3u);
        Assert.Single(files); // other.m3u (loop.m3u is the root)
    }
}
