using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using RetroHub.App.Services;
using RetroHub.App.ViewModels;
using RetroHub.Core;
using RetroHub.Core.Config;
using RetroHub.Core.Data;
using RetroHub.Core.Emulation;
using RetroHub.Core.Library;
using RetroHub.Core.Metadata;
using Xunit;

namespace RetroHub.App.Tests;

/// <summary>Review regressions of the artwork module (App side: thumbnail disk cache).</summary>
public class ArtworkUiTests
{
    private static string NewDir()
    {
        var dir = Path.Combine(Path.GetTempPath(), "retrohub-ui-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }

    /// <summary>A large RGBA PNG (random noise so it does not compress below the disk-cache threshold);
    /// the top-left quarter is fully transparent when <paramref name="transparentCorner"/> is set.</summary>
    private static string WriteNoisePng(string dir, string name, int w, int h, bool transparentCorner)
    {
        using var wb = new WriteableBitmap(new PixelSize(w, h), new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Unpremul);
        using (var fb = wb.Lock())
        {
            var rnd = new Random(42);
            var row = new byte[fb.RowBytes];
            for (var y = 0; y < h; y++)
            {
                rnd.NextBytes(row);
                for (var x = 0; x < w; x++)
                    row[x * 4 + 3] = transparentCorner && x < w / 2 && y < h / 2 ? (byte)0 : (byte)255;
                Marshal.Copy(row, 0, fb.Address + y * fb.RowBytes, fb.RowBytes);
            }
        }
        var file = Path.Combine(dir, name);
        using (var fs = File.Create(file)) wb.Save(fs, new PngBitmapEncoderOptions());
        return file;
    }

    private static byte[] Pixel(Bitmap bmp, int x, int y)
    {
        var buf = new byte[4];
        var handle = GCHandle.Alloc(buf, GCHandleType.Pinned);
        try { bmp.CopyPixels(new PixelRect(x, y, 1, 1), handle.AddrOfPinnedObject(), 4, 4); }
        finally { handle.Free(); }
        return buf;
    }

    [Fact]
    public Task Disk_thumbnail_keeps_transparency_and_is_used_on_the_next_load() => UiTest.Run(() =>
    {
        var dir = NewDir();
        var thumbs = Path.Combine(dir, "thumbs");
        var png = WriteNoisePng(dir, "cover.png", 600, 840, transparentCorner: true);
        Assert.True(new FileInfo(png).Length > 256 * 1024, "test image must exceed the disk-cache threshold");

        var first = new AsyncImageLoader(16) { DiskCacheFolder = thumbs };
        var a = first.LoadAsync(png, 256, CancellationToken.None).GetAwaiter().GetResult();
        Assert.NotNull(a);
        var thumb = Assert.Single(Directory.GetFiles(thumbs));
        Assert.True(new FileInfo(thumb).Length < new FileInfo(png).Length);

        // a new loader (next start) decodes the small file: the transparent corner must stay transparent
        var second = new AsyncImageLoader(16) { DiskCacheFolder = thumbs };
        var b = second.LoadAsync(png, 256, CancellationToken.None).GetAwaiter().GetResult();
        Assert.NotNull(b);
        Assert.Equal(256, b!.PixelSize.Width);
        Assert.Equal(0, Pixel(b, 10, 10)[3]);
        Assert.Equal(255, Pixel(b, 250, 350)[3]);
        try { Directory.Delete(dir, true); } catch (IOException) { }
    });

    [Fact]
    public Task Opaque_covers_get_a_small_jpeg_thumbnail_that_the_next_start_uses() => UiTest.Run(() =>
    {
        var dir = NewDir();
        var thumbs = Path.Combine(dir, "thumbs");
        var png = WriteNoisePng(dir, "cover.png", 600, 840, transparentCorner: false);

        var loader = new AsyncImageLoader(16) { DiskCacheFolder = thumbs };
        Assert.NotNull(loader.LoadAsync(png, 256, CancellationToken.None).GetAwaiter().GetResult());

        var thumb = Assert.Single(Directory.GetFiles(thumbs));
        Assert.Equal(".jpg", Path.GetExtension(thumb));
        var head = File.ReadAllBytes(thumb).AsSpan(0, 3).ToArray();
        Assert.Equal(new byte[] { 0xFF, 0xD8, 0xFF }, head);
        Assert.True(new FileInfo(thumb).Length < new FileInfo(png).Length / 4);

        // proof that the next start decodes the cached copy, not the original: replace the copy by a solid green image
        using (var green = new RenderTargetBitmap(new PixelSize(256, 358)))
        {
            using (var dc = green.CreateDrawingContext())
                dc.FillRectangle(Avalonia.Media.Brushes.Lime, new Rect(0, 0, 256, 358));
            using var fs = File.Create(thumb);
            green.Save(fs, new PngBitmapEncoderOptions());
        }
        var next = new AsyncImageLoader(16) { DiskCacheFolder = thumbs };
        var b = next.LoadAsync(png, 256, CancellationToken.None).GetAwaiter().GetResult();
        Assert.NotNull(b);
        var px = Pixel(b!, 100, 100); // BGRA
        Assert.True(px[1] > 200 && px[0] < 50 && px[2] < 50, $"expected the green cached copy, got {string.Join(",", px)}");
        try { Directory.Delete(dir, true); } catch (IOException) { }
    });

    /// <summary>Routes requests to a callback (GitHub index + thumbnails) without network access.</summary>
    private sealed class FakeHttp(Func<HttpRequestMessage, HttpResponseMessage> handler) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) =>
            Task.Run(() => handler(request), ct);
    }

    private static readonly byte[] TinyPng = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0, 0, 0, 13, 1, 1, 1];

    [Fact]
    public Task Scan_finishing_during_a_cover_run_gets_its_own_quiet_pass() => UiTest.Run(() =>
    {
        var root = NewDir();
        var paths = new AppPaths(root, AppContext.BaseDirectory);
        var settings = new SettingsStore(Path.Combine(root, "settings.json"));
        settings.Current.OnlineCovers = true;
        settings.Current.Motion = MotionLevel.Off;
        App.InitializeUi(settings.Current, null);
        var catalog = RetroHub.Core.Catalog.Catalog.Load(paths);
        var repo = new SqliteGameRepository(paths.DatabaseFile);
        using var release = new ManualResetEventSlim(false);
        using var indexRequested = new ManualResetEventSlim(false);
        const string tree = """{"tree":[{"path":"Named_Boxarts/Advance Wars (USA).png","type":"blob"},{"path":"Named_Boxarts/Golden Sun (USA, Europe).png","type":"blob"}],"truncated":false}""";
        var http = new HttpClient(new FakeHttp(req =>
        {
            if (req.RequestUri!.Host == "api.github.com")
            {
                indexRequested.Set();
                release.Wait(TimeSpan.FromSeconds(20)); // the first pass hangs on the index until the test lets it go
                return new HttpResponseMessage(System.Net.HttpStatusCode.OK) { Content = new StringContent(tree) };
            }
            return new HttpResponseMessage(System.Net.HttpStatusCode.OK) { Content = new ByteArrayContent(TinyPng) };
        }));
        var registry = new EmulatorRegistry(catalog, paths, settings, Path.Combine(root, "emulator-versions.json"));
        var services = new AppServices
        {
            Paths = paths, Settings = settings, Ui = new UiPreferencesStore(null), Catalog = catalog, Games = repo,
            Scanner = new LibraryScanner(catalog, repo, paths), Emulators = registry,
            Launcher = new LaunchPlanner(catalog, registry, paths, settings),
            Sessions = new SessionService(repo, paths, []),
            Artwork = new ArtworkService(paths, repo, catalog, settings, http),
        };
        Game Add(string file)
        {
            var g = new Game { SystemId = "gba", Path = "games/gba/" + file, Title = Path.GetFileNameWithoutExtension(file), AddedUtc = DateTime.UtcNow, FileTimeUtc = DateTime.UtcNow };
            g.Id = repo.Upsert(g);
            return g;
        }
        try
        {
            var vm = new ImportsViewModel(new UiContext(services));
            var first = Add("Advance Wars (USA).gba");
            var running = vm.RunCoverSearchAsync(quiet: false);
            Assert.True(vm.IsFindingCovers);
            // the first pass has read the library (only the first game) and waits for the GitHub index
            Assert.True(indexRequested.Wait(TimeSpan.FromSeconds(10)), "first pass did not reach the index request");

            // a scan adds a game and asks for its quiet cover pass while the first pass is still going
            var second = Add("Golden Sun (USA, Europe).gba");
            var quiet = vm.RunCoverSearchAsync(quiet: true);
            Assert.True(quiet.IsCompleted); // nothing started now …
            release.Set();

            // … but once the first pass is done, the new game gets its cover too
            Assert.True(TestSupport.Pump(() => running.IsCompleted && !vm.IsFindingCovers, 15_000), "cover passes did not finish");
            Assert.Equal("artwork/covers/gba/Advance Wars (USA).png", repo.Get(first.Id)!.CoverPath);
            Assert.Equal("artwork/covers/gba/Golden Sun (USA, Europe).png", repo.Get(second.Id)!.CoverPath);
        }
        finally
        {
            services.Dispose();
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            try { Directory.Delete(root, true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
    });
}
