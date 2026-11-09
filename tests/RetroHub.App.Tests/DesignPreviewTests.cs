using System.Net;
using Avalonia.Headless;
using RetroHub.App.Services;
using RetroHub.App.ViewModels;
using RetroHub.App.Views;
using RetroHub.Core;
using RetroHub.Core.Config;
using RetroHub.Core.Library;
using RetroHub.Core.Metadata;
using Xunit;

namespace RetroHub.App.Tests;

/// <summary>
/// Design preview harness (DESIGN 11.5): renders the main pages headless (real Skia, no desktop) at several window
/// sizes into <c>%RETROHUB_DESIGN_DIR%\shots\&lt;milestone&gt;\</c>, for three libraries:
/// <list type="bullet">
/// <item><c>preview</c>: a COPY of a real library database (read-only copy of <c>RETROHUB_DESIGN_DB_SOURCE</c>) in a
/// scratch root with covers fetched from thumbnails.libretro.com through the app's own artwork pipeline
/// (<see cref="ArtworkService.SetUserCover"/>) – only when <c>RETROHUB_DESIGN_FETCH_COVERS=1</c>;</item>
/// <item><c>nocovers</c>: the same library with every cover cleared (graceful degradation: spine placeholders);</item>
/// <item><c>demo</c>: the built-in demo services.</item>
/// </list>
/// Never part of the normal runs: trait Design + Interactive (the standard filter skips Interactive) and it skips itself
/// unless <c>RETROHUB_DESIGN_DIR</c> is set. Run:
/// <c>$env:RETROHUB_DESIGN_DIR="…\scratchpad\design"; dotnet test --project tests\RetroHub.App.Tests -- --filter-trait "Category=Design"</c>
/// </summary>
[Trait("Category", "Design")]
[Trait("Category", "Interactive")]
public class DesignPreviewTests
{
    private static string? DesignDir => Environment.GetEnvironmentVariable("RETROHUB_DESIGN_DIR");
    private static string Milestone => Environment.GetEnvironmentVariable("RETROHUB_DESIGN_MILESTONE") is { Length: > 0 } m ? m : "m1";
    private static string DbSource => Environment.GetEnvironmentVariable("RETROHUB_DESIGN_DB_SOURCE") is { Length: > 0 } s ? s : @"D:\RetroHub\data\database";

    private static readonly (int W, int H)[] AllSizes = [(1920, 1080), (2560, 1440), (1600, 900), (1100, 700)];
    private static readonly (int W, int H)[] ShortSizes = [(1920, 1080), (1100, 700)];

    [Fact]
    public Task Preview_library_with_covers() => Run("preview", PreviewRoot(), AllSizes);

    [Fact]
    public Task Preview_library_without_covers() => Run("nocovers", NoCoversRoot(), ShortSizes);

    [Fact]
    public Task Demo_library() => Run("demo", null, ShortSizes);

    // ===================== scratch roots =====================

    private static string PreviewRoot()
    {
        var root = Path.Combine(DesignDir!, "preview-root");
        CopyDatabase(DbSource, Path.Combine(root, "data", "database"));
        PrepareSettings(root);
        if (Environment.GetEnvironmentVariable("RETROHUB_DESIGN_FETCH_COVERS") == "1") FetchCovers(root);
        return root;
    }

    private static string NoCoversRoot()
    {
        var preview = Path.Combine(DesignDir!, "preview-root");
        var root = Path.Combine(DesignDir!, "preview-root-nocovers");
        // a fresh copy every time (from the preview copy when it exists, never from the user's file twice)
        var db = Path.Combine(root, "data", "database");
        if (Directory.Exists(db)) Directory.Delete(db, true);
        CopyDatabase(Directory.Exists(Path.Combine(preview, "data", "database")) ? Path.Combine(preview, "data", "database") : DbSource, db);
        PrepareSettings(root);
        using var services = CreateReal(root);
        foreach (var g in services.Games.Query(new GameQuery { IncludeHidden = true }))
            services.Artwork!.ClearCover(g.Id);
        return root;
    }

    /// <summary>Copies the library database READ-ONLY from the source (shared read, the owner may have it open).</summary>
    private static void CopyDatabase(string sourceDir, string targetDir)
    {
        Directory.CreateDirectory(targetDir);
        if (File.Exists(Path.Combine(targetDir, "retrohub.db"))) return;
        foreach (var file in Directory.GetFiles(sourceDir, "retrohub.db*"))
        {
            using var src = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var dst = File.Create(Path.Combine(targetDir, Path.GetFileName(file)));
            src.CopyTo(dst);
        }
    }

    private static void PrepareSettings(string root)
    {
        var settings = new SettingsStore(new AppPaths(root, AppContext.BaseDirectory).SettingsFile);
        settings.Current.Language = "de";
        settings.Current.ScanOnStartup = false;
        settings.Current.WatchFolders = false;
        settings.Current.OnlineCovers = false; // the app itself never goes online here; FetchCovers is the only network use
        settings.Current.Motion = MotionLevel.Full;
        settings.Save();
    }

    private static AppServices CreateReal(string root)
    {
        AppServices.WithoutGamepad = true;
        var paths = new AppPaths(root, AppContext.BaseDirectory);
        var settings = new SettingsStore(paths.SettingsFile);
        settings.Load();
        var services = AppServices.Create(paths, settings, new UiPreferencesStore(null));
        Assert.False(services.IsDemo, "the real services could not be created for the preview root");
        return services;
    }

    /// <summary>
    /// Covers for the copied library from thumbnails.libretro.com (the ONLY host contacted), stored through the app's
    /// own pipeline (ArtworkService.SetUserCover → artwork/covers/&lt;system&gt;/ + database), inside the scratch root.
    /// </summary>
    private static void FetchCovers(string root)
    {
        using var services = CreateReal(root);
        var download = Path.Combine(DesignDir!, "covers-dl");
        Directory.CreateDirectory(download);
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
        http.DefaultRequestHeaders.UserAgent.ParseAdd("RetroHub-design-preview/0.1");
        foreach (var game in services.Games.Query(new GameQuery { IncludeHidden = true }))
        {
            if (!string.IsNullOrEmpty(game.CoverPath)) continue;
            var sys = services.Catalog.GetSystem(game.SystemId);
            if (sys?.LibretroName is not { } libretro) continue;
            foreach (var name in Candidates(game))
            {
                var url = LibretroThumbnails.BoxartUrl(libretro, name);
                if (url.Host != "thumbnails.libretro.com") continue;
                try
                {
                    using var response = http.GetAsync(url).GetAwaiter().GetResult();
                    if (response.StatusCode != HttpStatusCode.OK) continue;
                    var bytes = response.Content.ReadAsByteArrayAsync().GetAwaiter().GetResult();
                    if (bytes.Length < 8 || bytes[0] != 0x89 || bytes[1] != (byte)'P') continue;
                    var file = Path.Combine(download, $"{game.Id}.png");
                    File.WriteAllBytes(file, bytes);
                    services.Artwork!.SetUserCover(game.Id, file);
                    break;
                }
                catch (HttpRequestException) { }
                catch (TaskCanceledException) { }
            }
        }
    }

    /// <summary>No-Intro style names to try: the app's own candidates, then "Title, The" and common region tags.</summary>
    private static IEnumerable<string> Candidates(Game game)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var c in LibretroThumbnails.DirectCandidates(game))
            if (seen.Add(c)) yield return c;
        var title = game.Title;
        var titles = new List<string> { title };
        if (title.StartsWith("The ", StringComparison.OrdinalIgnoreCase))
        {
            var rest = title[4..];
            var dash = rest.IndexOf(" - ", StringComparison.Ordinal);
            titles.Add(dash > 0 ? $"{rest[..dash]}, The{rest[dash..]}" : $"{rest}, The");
        }
        foreach (var t in titles)
            foreach (var region in new[] { "(USA, Europe)", "(Europe)", "(USA)", "(World)", "(Europe) (En,Fr,De,Es,It)", "(USA) (En,Fr,Es)", "(Germany)" })
            {
                var name = LibretroThumbnails.Sanitize($"{t} {region}");
                if (seen.Add(name)) yield return name;
            }
    }

    // ===================== rendering =====================

    private static Task Run(string variant, string? root, (int W, int H)[] sizes) => UiTest.Run(() =>
    {
        Assert.SkipWhen(string.IsNullOrEmpty(DesignDir), "RETROHUB_DESIGN_DIR is not set (design previews only on request)");
        var outDir = Path.Combine(DesignDir!, "shots", Milestone);
        Directory.CreateDirectory(outDir);
        if (root == null) root = variant == "preview" ? PreviewRoot() : variant == "nocovers" ? NoCoversRoot() : null;
        foreach (var (w, h) in sizes)
        {
            var ctx = root == null
                ? TestSupport.CreateContext(s => s.Motion = MotionLevel.Full)
                : new UiContext(CreateReal(root));
            if (root != null) App.InitializeUi(ctx.Services.Settings.Current, null);
            var vm = new MainViewModel(ctx);
            var window = new MainWindow(vm) { Width = w, Height = h };
            window.Show();
            try
            {
                TestSupport.Pump(() => vm.CurrentPage is HomeViewModel { IsLoaded: true }, 8000);
                var prefix = Path.Combine(outDir, $"{variant}-{w}x{h}");
                Save(window, prefix + "-1-start.png", 1200);

                vm.Navigator.Navigate(PageKey.Library);
                TestSupport.Pump(() => vm.CurrentPage is LibraryViewModel { IsLoaded: true }, 8000);
                Save(window, prefix + "-2-bibliothek.png", 1200);

                var pick = PickGame(ctx);
                if (pick != null)
                {
                    vm.Navigator.Navigate(PageKey.GameDetail, pick);
                    TestSupport.Pump(() => vm.CurrentPage is GameDetailViewModel { Game: not null }, 8000);
                    Save(window, prefix + "-3-detail.png", 1200);
                }

                vm.Navigator.Navigate(PageKey.Systems);
                TestSupport.Pump(() => vm.CurrentPage is SystemsViewModel { Systems.Count: > 0 }, 8000);
                Save(window, prefix + "-4-konsolen.png", 600);

                if (pick != null)
                {
                    vm.Navigator.Navigate(PageKey.System, pick.SystemId);
                    TestSupport.Pump(() => vm.CurrentPage is SystemViewModel { Games.Count: > 0 }, 8000);
                    Save(window, prefix + "-5-systemseite.png", 1200);
                }

                vm.Navigator.Navigate(PageKey.Emulators);
                TestSupport.Pump(() => vm.CurrentPage is EmulatorsViewModel, 8000);
                Save(window, prefix + "-6-emulatoren.png", 600);

                vm.Navigator.Navigate(PageKey.Settings);
                Save(window, prefix + "-7-einstellungen.png", 600);
            }
            finally
            {
                window.Close();
                if (root != null) ctx.Services.Dispose();
            }
        }
    });

    /// <summary>The detail/system subject: the last played game, else one with a cover, else the first.</summary>
    private static GameItemViewModel? PickGame(UiContext ctx)
    {
        var all = ctx.Services.Games.Query(new GameQuery { Sort = GameSort.LastPlayed });
        var game = all.FirstOrDefault(g => g.LastPlayedUtc != null && !string.IsNullOrEmpty(g.CoverPath))
                   ?? all.FirstOrDefault(g => !string.IsNullOrEmpty(g.CoverPath))
                   ?? all.FirstOrDefault();
        return game == null ? null : ctx.Items.Get(game);
    }

    private static void Save(MainWindow window, string file, int settleMs)
    {
        TestSupport.Settle(settleMs);
        var frame = window.CaptureRenderedFrame();
        Assert.NotNull(frame);
        frame!.Save(file, new Avalonia.Media.Imaging.PngBitmapEncoderOptions());
    }
}
