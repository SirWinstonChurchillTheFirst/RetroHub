using System.Buffers.Binary;
using Microsoft.Data.Sqlite;
using RetroHub.Core.Data;
using RetroHub.Core.Library;
using RetroHub.Core.Metadata;
using RetroHub.Core.Tests.Data;
using Xunit;

namespace RetroHub.Core.Tests.Metadata;

/// <summary>
/// Cover pixel sizes (DESIGN 11.4, M3): read from the image header without decoding, stored with the cover
/// (schema 2: games.cover_w / cover_h), backfilled for covers stored earlier, cleared with the cover.
/// </summary>
public sealed class CoverSizeTests : IDisposable
{
    private readonly ArtworkTestEnv _env = new(online: false);

    public void Dispose() => _env.Dispose();

    private CancellationToken Ct => TestContext.Current.CancellationToken;

    // ───────────── header formats ─────────────

    public static byte[] PngHeader(int w, int h)
    {
        var b = new byte[33];
        new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0, 0, 0, 13, (byte)'I', (byte)'H', (byte)'D', (byte)'R' }.CopyTo(b, 0);
        BinaryPrimitives.WriteInt32BigEndian(b.AsSpan(16), w);
        BinaryPrimitives.WriteInt32BigEndian(b.AsSpan(20), h);
        b[24] = 8; b[25] = 6;
        return b;
    }

    /// <summary>SOI, APP0, a large APP1 (EXIF-like), DQT, DHT, then the frame header <paramref name="sof"/>.</summary>
    public static byte[] JpegHeader(int w, int h, byte sof = 0xC0, int app1 = 3000)
    {
        var ms = new MemoryStream();
        void Seg(byte marker, int length, byte fill = 0)
        {
            ms.WriteByte(0xFF); ms.WriteByte(marker);
            ms.WriteByte((byte)(length >> 8)); ms.WriteByte((byte)length);
            for (var i = 0; i < length - 2; i++) ms.WriteByte(fill);
        }
        ms.WriteByte(0xFF); ms.WriteByte(0xD8);
        Seg(0xE0, 16, 0x4A);
        Seg(0xE1, app1, 0xC0); // its payload is full of 0xC0 bytes: must be skipped, never read as a frame marker
        Seg(0xDB, 67, 1);
        Seg(0xC4, 31, 2);     // DHT (0xC4) is not a frame header
        ms.WriteByte(0xFF); ms.WriteByte(0xFF); // fill byte before a marker is allowed
        ms.WriteByte(0xFF); ms.WriteByte(sof);
        ms.WriteByte(0); ms.WriteByte(17); ms.WriteByte(8);
        ms.WriteByte((byte)(h >> 8)); ms.WriteByte((byte)h);
        ms.WriteByte((byte)(w >> 8)); ms.WriteByte((byte)w);
        for (var i = 0; i < 10; i++) ms.WriteByte(0);
        ms.WriteByte(0xFF); ms.WriteByte(0xD9);
        return ms.ToArray();
    }

    private static (int, int)? Read(byte[] bytes) => ImageHeader.TryRead(new MemoryStream(bytes));

    [Fact]
    public void Png_size_comes_from_the_IHDR_chunk() => Assert.Equal((512, 716), Read(PngHeader(512, 716)));

    [Theory]
    [InlineData(0xC0)] // baseline
    [InlineData(0xC2)] // progressive
    public void Jpeg_size_comes_from_the_first_frame_header_after_large_segments(int sof) =>
        Assert.Equal((640, 897), Read(JpegHeader(640, 897, (byte)sof)));

    [Fact]
    public void Jpeg_without_a_frame_header_is_unknown()
    {
        var bytes = JpegHeader(10, 10)[..40];
        Assert.Null(Read(bytes));
    }

    [Fact]
    public void WebP_lossy_lossless_and_extended_sizes()
    {
        var lossy = new byte[30];
        "RIFF"u8.CopyTo(lossy); "WEBP"u8.CopyTo(lossy.AsSpan(8)); "VP8 "u8.CopyTo(lossy.AsSpan(12));
        lossy[23] = 0x9D; lossy[24] = 0x01; lossy[25] = 0x2A;
        BinaryPrimitives.WriteUInt16LittleEndian(lossy.AsSpan(26), 300);
        BinaryPrimitives.WriteUInt16LittleEndian(lossy.AsSpan(28), 420);
        Assert.Equal((300, 420), Read(lossy));

        var lossless = new byte[30];
        "RIFF"u8.CopyTo(lossless); "WEBP"u8.CopyTo(lossless.AsSpan(8)); "VP8L"u8.CopyTo(lossless.AsSpan(12));
        lossless[20] = 0x2F;
        BinaryPrimitives.WriteUInt32LittleEndian(lossless.AsSpan(21), (uint)((300 - 1) | (420 - 1) << 14));
        Assert.Equal((300, 420), Read(lossless));

        var extended = new byte[30];
        "RIFF"u8.CopyTo(extended); "WEBP"u8.CopyTo(extended.AsSpan(8)); "VP8X"u8.CopyTo(extended.AsSpan(12));
        extended[24] = (300 - 1) & 0xFF; extended[25] = (300 - 1) >> 8;
        extended[27] = (420 - 1) & 0xFF; extended[28] = (420 - 1) >> 8;
        Assert.Equal((300, 420), Read(extended));
    }

    [Fact]
    public void Gif_and_bmp_sizes()
    {
        var gif = new byte[16];
        "GIF89a"u8.CopyTo(gif);
        BinaryPrimitives.WriteUInt16LittleEndian(gif.AsSpan(6), 160);
        BinaryPrimitives.WriteUInt16LittleEndian(gif.AsSpan(8), 144);
        Assert.Equal((160, 144), Read(gif));

        var bmp = new byte[30];
        bmp[0] = (byte)'B'; bmp[1] = (byte)'M';
        BinaryPrimitives.WriteInt32LittleEndian(bmp.AsSpan(18), 256);
        BinaryPrimitives.WriteInt32LittleEndian(bmp.AsSpan(22), -224); // top-down bitmap
        Assert.Equal((256, 224), Read(bmp));
    }

    [Fact]
    public void Garbage_truncated_and_missing_files_are_unknown()
    {
        Assert.Null(Read([1, 2, 3]));
        Assert.Null(Read(ArtworkTestEnv.Png(7))); // a PNG signature without IHDR
        Assert.Null(Read(PngHeader(0, 50)));
        Assert.Null(ImageHeader.TryRead(Path.Combine(_env.Root, "nope.png")));
    }

    // ───────────── schema ─────────────

    [Fact]
    public void Schema_1_database_migrates_and_keeps_its_games_without_a_size()
    {
        using var db = new TempDatabase();
        Directory.CreateDirectory(Path.GetDirectoryName(db.File)!);
        using (var c = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = db.File, Pooling = false }.ToString()))
        {
            c.Open();
            using var tx = c.BeginTransaction();
            Migrations.All[0].Apply(c, tx);
            using var cmd = c.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = """
                INSERT INTO games (system_id, path, path_key, title, file_time_utc, added_utc, cover_path)
                VALUES ('snes', 'games\snes\a.sfc', 'GAMES\SNES\A.SFC', 'Alpha', '2024-01-02T03:04:05.0000000Z', '2024-01-02T03:04:05.0000000Z', 'artwork/covers/snes/a.png');
                PRAGMA user_version = 1;
                """;
            cmd.ExecuteNonQuery();
            tx.Commit();
        }

        using var repo = db.Open();
        Assert.Equal(2, Migrations.Latest);
        Assert.Equal(Migrations.Latest, repo.SchemaVersion);
        var g = Assert.Single(repo.Query(new GameQuery()));
        Assert.Equal("Alpha", g.Title);
        Assert.Equal("artwork/covers/snes/a.png", g.CoverPath);
        Assert.Null(g.CoverWidth);
        Assert.Null(g.CoverHeight);

        g.CoverWidth = 512;
        g.CoverHeight = 512;
        repo.Upsert(g);
        var back = repo.Get(g.Id)!;
        Assert.Equal((512, 512), (back.CoverWidth, back.CoverHeight));
    }

    [Fact]
    public void Rescan_keeps_the_size_of_the_kept_cover_and_drops_it_with_a_new_one()
    {
        var g = _env.AddGame("games/gba/Alpha.gba", "gba", x => { x.CoverPath = "artwork/covers/gba/Alpha.png"; x.CoverWidth = 500; x.CoverHeight = 500; });
        // a scan brings a fresh object for the same path (Id 0, no cover): the cover and its size stay
        _env.Repo.Upsert(new Game { SystemId = "gba", Path = g.Path, Title = "Alpha", FileTimeUtc = DateTime.UtcNow });
        Assert.Equal((500, 500), (_env.Reload(g).CoverWidth, _env.Reload(g).CoverHeight));
        // a fresh object WITH another cover and no size: the old size does not stick to the new file
        _env.Repo.Upsert(new Game { SystemId = "gba", Path = g.Path, Title = "Alpha", FileTimeUtc = DateTime.UtcNow, CoverPath = "artwork/covers/gba/Other.png" });
        Assert.Null(_env.Reload(g).CoverWidth);
    }

    // ───────────── artwork pipeline ─────────────

    [Fact]
    public void User_cover_stores_its_pixel_size_and_removing_it_clears_the_size()
    {
        var g = _env.AddGame("games/gba/Alpha.gba");
        var picked = _env.Touch("picked/alpha.png", PngHeader(480, 320));
        var svc = _env.Service();

        svc.SetUserCover(g.Id, picked);
        var stored = _env.Reload(g);
        Assert.Equal((480, 320), (stored.CoverWidth, stored.CoverHeight));

        svc.ClearCover(g.Id);
        var cleared = _env.Reload(g);
        Assert.Equal("", cleared.CoverPath);
        Assert.Null(cleared.CoverWidth);
        Assert.Null(cleared.CoverHeight);
    }

    [Fact]
    public async Task Local_cover_found_by_the_resolver_stores_its_size()
    {
        var g = _env.AddGame("games/gba/Alpha.gba");
        _env.Touch("artwork/covers/gba/Alpha.jpg", JpegHeader(600, 600));

        await _env.Service().ResolveAsync(null, null, Ct);

        var r = _env.Reload(g);
        Assert.Equal("artwork/covers/gba/Alpha.jpg", r.CoverPath);
        Assert.Equal((600, 600), (r.CoverWidth, r.CoverHeight));
    }

    [Fact]
    public async Task Covers_stored_without_a_size_are_backfilled_from_their_headers()
    {
        _env.Touch("artwork/covers/gba/Alpha.png", PngHeader(512, 512));
        _env.Touch("artwork/covers/gba/Beta.png", ArtworkTestEnv.Png(3)); // unreadable header: stays unknown
        var a = _env.AddGame("games/gba/Alpha.gba", "gba", x => x.CoverPath = "artwork/covers/gba/Alpha.png");
        var b = _env.AddGame("games/gba/Beta.gba", "gba", x => x.CoverPath = "artwork/covers/gba/Beta.png");
        var svc = _env.Service();

        Assert.Equal(1, svc.FillCoverSizes(ct: Ct));
        Assert.Equal((512, 512), (_env.Reload(a).CoverWidth, _env.Reload(a).CoverHeight));
        Assert.Null(_env.Reload(b).CoverWidth);
        Assert.Equal(0, svc.FillCoverSizes(ct: Ct)); // nothing left to do

        // the resolver run does the same for covers it does not touch otherwise
        _env.Repo.Upsert(Clear(_env.Reload(a)));
        await svc.ResolveAsync(null, null, Ct);
        Assert.Equal(512, _env.Reload(a).CoverWidth);

        static Game Clear(Game g) { g.CoverWidth = g.CoverHeight = null; return g; }
    }
}
