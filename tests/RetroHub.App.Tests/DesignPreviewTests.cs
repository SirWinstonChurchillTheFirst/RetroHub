using System.Net;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.VisualTree;
using GameTile = RetroHub.App.Controls.GameTile;
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

    /// <summary>The 1000-game demo (DESIGN 9.5): no covers at all – every box a generated spine placeholder.</summary>
    [Fact]
    public Task Demo_library_1000() => Run("demo1000", null, [(1920, 1080), (2560, 1440)]);

    /// <summary>An empty library (fresh scratch root, real services): the switched-off TV and the empty shelf.</summary>
    [Fact]
    public Task Empty_library() => Run("empty", EmptyRoot(), ShortSizes);

    // ===================== filters (RETROHUB_DESIGN_ONLY, ";"-separated tokens) =====================
    // sizes "1920x1080", variants "preview|nocovers|demo|empty", pages "start|states|bibliothek|detail|konsolen|
    // systemseite|emulatoren|einstellungen", "specimen". Each kind of token only filters when at least one is given.

    private static string[] OnlyTokens => (Environment.GetEnvironmentVariable("RETROHUB_DESIGN_ONLY") ?? "")
        .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    private static readonly string[] PageTokens = ["start", "states", "bibliothek", "detail", "konsolen", "systemseite", "emulatoren", "einstellungen"];
    private static readonly string[] VariantTokens = ["preview", "nocovers", "demo", "demo1000", "empty", "perf"];

    private static bool Wants(string token, string[] kind)
    {
        var given = OnlyTokens.Where(kind.Contains).ToList();
        return given.Count == 0 || given.Contains(token);
    }

    /// <summary>Specimen sheet: font roles, icons, controller glyphs, textures, walls – to look at, not to assert.</summary>
    [Fact]
    public Task Specimen_sheet() => UiTest.Run(() =>
    {
        Assert.SkipWhen(string.IsNullOrEmpty(DesignDir), "RETROHUB_DESIGN_DIR is not set (design previews only on request)");
        Assert.SkipWhen(OnlyTokens.Length > 0 && !OnlyTokens.Contains("specimen"), "filtered out (RETROHUB_DESIGN_ONLY)");
        var outDir = Path.Combine(DesignDir!, "shots", Milestone);
        Directory.CreateDirectory(outDir);
        TestSupport.CreateContext();
        var stack = new Avalonia.Controls.StackPanel { Spacing = 14, Margin = new Avalonia.Thickness(48) };
        void Text(string cls, string text) { var tb = new Avalonia.Controls.TextBlock { Text = text }; tb.Classes.Add(cls); stack.Children.Add(tb); }
        Text("poster-xl", "Pokémon Smaragd-Edition 2004");
        Text("poster-s", "Plakat 40 · Archivo Expanded ExtraBold");
        Text("h2", "Etikett/Button · Archivo SemiCondensed SemiBold 24 – Bibliotheksordner");
        Text("ui-l", "UI · Archivo SemiCondensed Medium 20 – Startoptionen zurücksetzen");
        Text("read-l", "Lesen · Atkinson Hyperlegible Next 20 – Il1 O0 ÄÖÜäöüß „Zitat“ – … €");
        var sentence = new Avalonia.Controls.TextBlock(); sentence.Classes.Add("read-l");
        Controls.Sentence.SetText(sentence, "Game Boy Advance. 46 Std. in 41 Sitzungen gespielt, zuletzt 2004.");
        stack.Children.Add(sentence);
        Text("code", "GBA  SNES  N64  PS2  — Rücken · Archivo Condensed Bold");
        var icons = new Avalonia.Controls.WrapPanel { ItemSpacing = 18, LineSpacing = 12 };
        foreach (var key in new[] { "Start", "Library", "Systems", "Emulators", "Imports", "Settings", "Search", "Menu", "Back", "More", "Play", "Favorite",
                     "Random", "Folder", "External", "Refresh", "Sort", "Filter", "Grid", "List", "Cover", "Remove", "Add", "Close", "Fullscreen", "Window",
                     "Power", "Expand", "Collapse", "ArrowLeft", "ArrowRight", "Clock", "Calendar", "Players", "Region", "File", "Disk", "Controller",
                     "Starts", "Ok", "Warning", "Error", "Info", "Missing", "NotReady", "MediaCart8", "MediaCart16", "MediaCart64", "MediaDisc",
                     "MediaMiniDisc", "MediaCard", "MediaFloppy", "MediaHandheld", "MediaArcade" })
        {
            Avalonia.Application.Current!.TryGetResource("Icon." + key, null, out var g);
            var filled = key is "Play" or "ArrowLeft" or "ArrowRight";
            icons.Children.Add(new Controls.RhIcon { Data = g as Avalonia.Media.Geometry, Width = 28, Height = 28, Filled = filled });
        }
        stack.Children.Add(icons);
        var glyphs = new Avalonia.Controls.StackPanel { Orientation = Avalonia.Layout.Orientation.Horizontal, Spacing = 20 };
        foreach (var keyboard in new[] { false, true })
            foreach (var a in new[] { Input.NavAction.Accept, Input.NavAction.Back, Input.NavAction.Favorite, Input.NavAction.Search, Input.NavAction.PageLeft,
                         Input.NavAction.PageRight, Input.NavAction.Menu, Input.NavAction.View })
                glyphs.Children.Add(new Controls.ButtonGlyph { Action = a, Keyboard = keyboard });
        stack.Children.Add(glyphs);
        var walls = new Avalonia.Controls.WrapPanel { ItemSpacing = 12, LineSpacing = 12 };
        foreach (var id in Theming.SystemPalette.KnownIds.OrderBy(i => i))
        {
            var p = new Avalonia.Controls.Panel { Width = 150, Height = 120 };
            p.Children.Add(new Controls.SystemWall { SystemId = id, MotifShare = 0.6, MotifInset = 8 });
            var label = new Avalonia.Controls.TextBlock { Text = id, Margin = new Avalonia.Thickness(8, 14, 0, 0), FontSize = 15 };
            p.Children.Add(label);
            walls.Children.Add(p);
        }
        stack.Children.Add(walls);
        var window = new Avalonia.Controls.Window { Width = 1600, Height = 1500, Content = stack };
        window.Show();
        TestSupport.Settle(500);
        var frame = window.CaptureRenderedFrame();
        Assert.NotNull(frame);
        frame!.Save(Path.Combine(outDir, "specimen.png"), new Avalonia.Media.Imaging.PngBitmapEncoderOptions());
        window.Close();
    });

    // ===================== scratch roots =====================

    private static string PreviewRoot()
    {
        var root = Path.Combine(DesignDir!, "preview-root");
        CopyDatabase(DbSource, Path.Combine(root, "data", "database"));
        PrepareSettings(root);
        if (Environment.GetEnvironmentVariable("RETROHUB_DESIGN_FETCH_COVERS") == "1") FetchCovers(root);
        // covers stored before schema 2: their pixel sizes, so the library knows each system's format (DESIGN 11.4)
        using (var services = CreateReal(root)) services.Artwork!.FillCoverSizes();
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

    /// <summary>A fresh root with an empty library (recreated every run).</summary>
    private static string EmptyRoot()
    {
        var root = Path.Combine(DesignDir!, "preview-root-empty");
        if (Directory.Exists(root)) Directory.Delete(root, true);
        Directory.CreateDirectory(root);
        PrepareSettings(root);
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
        Assert.SkipWhen(!Wants(variant, VariantTokens), "variant filtered out (RETROHUB_DESIGN_ONLY)");
        var outDir = Path.Combine(DesignDir!, "shots", Milestone);
        Directory.CreateDirectory(outDir);
        if (root == null) root = variant == "preview" ? PreviewRoot() : variant == "nocovers" ? NoCoversRoot() : variant == "empty" ? EmptyRoot() : null;
        // RETROHUB_DESIGN_ONLY="1920x1080;1100x700;start" restricts sizes/pages/variants (quick iterations)
        var sizeTokens = OnlyTokens.Where(s => System.Text.RegularExpressions.Regex.IsMatch(s, @"^\d+x\d+$")).ToList();
        if (sizeTokens.Count > 0) sizes = sizes.Where(s => sizeTokens.Contains($"{s.W}x{s.H}")).ToArray();
        foreach (var (w, h) in sizes)
        {
            Services.Demo.DemoData.TargetCount = variant == "demo1000" ? 1000 : null;
            var ctx = root == null
                ? TestSupport.CreateContext(s => s.Motion = MotionLevel.Full)
                : new UiContext(CreateReal(root));
            Services.Demo.DemoData.TargetCount = null;
            if (root != null) App.InitializeUi(ctx.Services.Settings.Current, null);
            var vm = new MainViewModel(ctx);
            var window = new MainWindow(vm) { Width = w, Height = h };
            window.Show();
            try
            {
                TestSupport.Pump(() => vm.CurrentPage is HomeViewModel { IsLoaded: true }, 8000);
                var prefix = Path.Combine(outDir, $"{variant}-{w}x{h}");
                if (variant != "demo1000")
                {
                    if (Wants("start", PageTokens)) Save(window, prefix + "-1-start.png", 1400);
                    if (Wants("states", PageTokens)) StartStates(window, vm, prefix);
                }

                if (Wants("bibliothek", PageTokens)) LibraryStates(window, vm, prefix, deep: variant == "demo1000");
                if (variant == "demo1000") continue;

                var pick = PickGame(ctx);
                if (pick != null && Wants("detail", PageTokens))
                {
                    vm.Navigator.Navigate(PageKey.GameDetail, pick);
                    TestSupport.Pump(() => vm.CurrentPage is GameDetailViewModel { Game: not null }, 8000);
                    Save(window, prefix + "-3-detail.png", 1200);
                }

                if (Wants("konsolen", PageTokens))
                {
                    vm.Navigator.Navigate(PageKey.Systems);
                    TestSupport.Pump(() => vm.CurrentPage is SystemsViewModel { Systems.Count: > 0 }, 8000);
                    Save(window, prefix + "-4-konsolen.png", 600);
                }

                if (pick != null && Wants("systemseite", PageTokens))
                {
                    vm.Navigator.Navigate(PageKey.System, pick.SystemId);
                    TestSupport.Pump(() => vm.CurrentPage is SystemViewModel { Games.Count: > 0 }, 8000);
                    Save(window, prefix + "-5-systemseite.png", 1200);
                }

                if (Wants("emulatoren", PageTokens))
                {
                    vm.Navigator.Navigate(PageKey.Emulators);
                    TestSupport.Pump(() => vm.CurrentPage is EmulatorsViewModel, 8000);
                    Save(window, prefix + "-6-emulatoren.png", 600);
                }

                if (Wants("einstellungen", PageTokens))
                {
                    vm.Navigator.Navigate(PageKey.Settings);
                    Save(window, prefix + "-7-einstellungen.png", 600);
                }
            }
            finally
            {
                window.Close();
                if (root != null) ctx.Services.Dispose();
            }
        }
    });

    /// <summary>
    /// Start in the focus states that matter (DESIGN 7.1), driven like a controller: a box on the first shelf after the
    /// dwell (the stage follows), the next shelf with the row glided to the anchor, the "Konsolen" shelf with a spine
    /// turned to its front, and back up on "Spielen" (the stage keeps the browsed game).
    /// </summary>
    private static void StartStates(MainWindow window, MainViewModel vm, string prefix)
    {
        if (vm.CurrentPage is not HomeViewModel home || home.IsLibraryEmpty) return;
        vm.Navigator.Navigate(PageKey.Home);
        TestSupport.Settle(300);
        Control? Focused() => window.FocusManager?.GetFocusedElement() as Control;
        window.Execute(Input.NavAction.Down);              // first shelf, first box
        window.Execute(Input.NavAction.Right);             // its second box
        window.Execute(Input.NavAction.Right);
        Save(window, prefix + "-1b-start-regal.png", 1400);
        window.Execute(Input.NavAction.Down);              // the next shelf glides up to the anchor
        for (var i = 0; i < 4; i++) window.Execute(Input.NavAction.Right);
        Save(window, prefix + "-1c-start-regal2.png", 1400);
        for (var i = 0; i < 6 && Focused() is not Controls.SystemSpine; i++)
        {
            window.Execute(Input.NavAction.Down);
            TestSupport.Settle(60);
        }
        if (Focused() is Controls.SystemSpine)
        {
            window.Execute(Input.NavAction.Right);
            Save(window, prefix + "-1d-start-konsolen.png", 1400);
        }
        for (var i = 0; i < 6 && Focused() is not Button { Name: "PlayButton" }; i++)
        {
            window.Execute(Input.NavAction.Up);
            TestSupport.Settle(60);
        }
        Save(window, prefix + "-1e-start-zurueck.png", 1000);
        // right along the actions to the box on the stage (it takes the object focus like a box on a shelf)
        for (var i = 0; i < 4 && Focused() is not GameTile; i++) window.Execute(Input.NavAction.Right);
        if (Focused() is GameTile) Save(window, prefix + "-1f-start-schachtel.png", 600);
    }
    /// <summary>
    /// The library in the states that matter (DESIGN 7.2), driven like a controller: the first box focused; a box
    /// further in (bracket and mark of the mirror move); the system with the most games through the ticker (banner,
    /// wall ground, system light, slot format from the cover sizes) with a box focused; the list view with its
    /// preview; for the 1000-game demo a page deep into the list.
    /// </summary>
    private static void LibraryStates(MainWindow window, MainViewModel vm, string prefix, bool deep)
    {
        vm.Navigator.Navigate(PageKey.Library);
        TestSupport.Pump(() => vm.CurrentPage is LibraryViewModel { IsLoaded: true }, 8000);
        if (vm.CurrentPage is not LibraryViewModel lib) return;
        if (lib.IsListView) lib.IsListView = false;
        TestSupport.Settle(400);
        Save(window, prefix + "-2-bibliothek.png", 1000);
        if (lib.Items.Count == 0) return;
        window.Execute(Input.NavAction.Down);
        window.Execute(Input.NavAction.Right);
        window.Execute(Input.NavAction.Right);
        Save(window, prefix + "-2b-bibliothek-fokus.png", 1200);
        if (deep)
        {
            var deepGrid = window.GetVisualDescendants().OfType<Controls.CoverGrid>().First(g => g.Name == "Covers");
            for (var i = 0; i < 12; i++) { deepGrid.Move(Input.NavAction.PageRight); TestSupport.Settle(40); }
            window.Execute(Input.NavAction.Right);
            Save(window, prefix + "-2e-bibliothek-tief.png", 1200);
        }
        // the system with the most games, through the ticker (like the shoulder would)
        var chip = lib.Chips.Where(c => !c.IsAll).OrderByDescending(c => c.Count).FirstOrDefault();
        if (chip != null)
        {
            lib.SelectChipCommand.Execute(chip);
            TestSupport.Pump(() => lib.SelectedSystemId == chip.SystemId && lib.Items.Count > 0 && lib.Items.All(i => i.SystemId == chip.SystemId), 8000);
            TestSupport.Settle(300);
            var grid = window.GetVisualDescendants().OfType<Controls.CoverGrid>().First(g => g.Name == "Covers");
            grid.FocusIndex(Math.Min(1, lib.Items.Count - 1));
            Save(window, prefix + "-2c-bibliothek-system.png", 1200);
            lib.SelectChipCommand.Execute(lib.Chips.First(c => c.IsAll));
            TestSupport.Pump(() => lib.SelectedSystemId == null, 4000);
            TestSupport.Settle(300);
        }
        // the list with its preview (the view is a saved setting: switched back afterwards)
        lib.IsListView = true;
        TestSupport.Settle(400);
        var rows = window.GetVisualDescendants().OfType<Controls.CoverGrid>().First(g => g.Name == "Rows");
        rows.FocusIndex(Math.Min(4, lib.Items.Count - 1));
        Save(window, prefix + "-2d-bibliothek-liste.png", 1200);
        lib.IsListView = false;
        TestSupport.Settle(200);
    }

    /// <summary>
    /// Layout and render cost of the library with the 1000-game demo (DESIGN 9.5), measured headless: Avalonia's
    /// headless platform with REAL Skia rasterisation on the CPU (no GPU, no compositor vsync). Per step: one focus
    /// move (input → layout) and one full rendered frame of the window. Written to shots\&lt;milestone&gt;\perf-library.txt.
    /// The numbers are an upper bound of the CPU work per frame, not an fps measurement on a real GPU.
    /// </summary>
    [Fact]
    public Task Library_1000_perf() => UiTest.Run(() =>
    {
        Assert.SkipWhen(string.IsNullOrEmpty(DesignDir), "RETROHUB_DESIGN_DIR is not set (design previews only on request)");
        Assert.SkipWhen(!Wants("perf", VariantTokens), "variant filtered out (RETROHUB_DESIGN_ONLY)");
        var outDir = Path.Combine(DesignDir!, "shots", Milestone);
        Directory.CreateDirectory(outDir);
        var report = new System.Text.StringBuilder();
        report.AppendLine($"Library performance, headless Avalonia 12 + Skia CPU raster, {Environment.ProcessorCount} logical CPUs, {DateTime.Now:yyyy-MM-dd HH:mm}");
        foreach (var (w, h) in new[] { (1920, 1080), (2560, 1440) })
        {
            const int count = 1000;
            Services.Demo.DemoData.TargetCount = count;
            var ctx = TestSupport.CreateContext(s => s.Motion = MotionLevel.Full);
            Services.Demo.DemoData.TargetCount = null;
            var vm = new MainViewModel(ctx);
            var window = new MainWindow(vm) { Width = w, Height = h };
            window.Show();
            try
            {
                TestSupport.Pump(() => vm.CurrentPage is HomeViewModel { IsLoaded: true }, 8000);
                var sw = System.Diagnostics.Stopwatch.StartNew();
                vm.Navigator.Navigate(PageKey.Library);
                TestSupport.Pump(() => vm.CurrentPage is LibraryViewModel { IsLoaded: true, Items.Count: > 0 }, 15000);
                var loadMs = sw.Elapsed.TotalMilliseconds;
                var grid = window.GetVisualDescendants().OfType<Controls.CoverGrid>().First(g => g.Name == "Covers");
                TestSupport.Pump(() => grid.TileForIndex(0) != null, 4000);
                sw.Restart();
                window.CaptureRenderedFrame();
                var firstFrame = sw.Elapsed.TotalMilliseconds;
                TestSupport.Settle(500);
                window.Execute(Input.NavAction.Right);
                TestSupport.Settle(300);
                var mirror = window.GetVisualDescendants().OfType<Controls.ShelfMirror>().First();
                var rasterBefore = mirror.RasterCount;
                var lib = (LibraryViewModel)vm.CurrentPage!;

                // 1) focus moves like a held direction: down through the rows, right along one, back up
                var layout = new List<double>();
                var frame = new List<double>();
                var moves = new List<Input.NavAction>();
                for (var i = 0; i < 40; i++) moves.Add(Input.NavAction.Down);
                for (var i = 0; i < 6; i++) moves.Add(Input.NavAction.Right);
                for (var i = 0; i < 20; i++) moves.Add(Input.NavAction.Up);
                foreach (var move in moves)
                {
                    sw.Restart();
                    window.Execute(move);
                    Avalonia.Threading.Dispatcher.UIThread.RunJobs();
                    window.UpdateLayout();
                    layout.Add(sw.Elapsed.TotalMilliseconds);
                    sw.Restart();
                    window.CaptureRenderedFrame();
                    frame.Add(sw.Elapsed.TotalMilliseconds);
                }
                // 2) paging through the whole list (rows realised and recycled)
                var page = new List<double>();
                for (var i = 0; i < 80 && grid.SelectedIndex < lib.Items.Count - grid.Columns; i++)
                {
                    sw.Restart();
                    grid.Move(Input.NavAction.PageRight);
                    Avalonia.Threading.Dispatcher.UIThread.RunJobs();
                    window.UpdateLayout();
                    window.CaptureRenderedFrame();
                    page.Add(sw.Elapsed.TotalMilliseconds);
                }
                // 3) the shelf mirror: one rasterisation per list, none per move
                var rasterDuringMoves = mirror.RasterCount - rasterBefore;
                var spines = Enumerable.Range(0, count).Select(i => new Controls.ShelfMirror.Spine(i % 2 == 0 ? "snes" : "gba", Avalonia.Media.Colors.Red)).ToList();
                sw.Restart();
                for (var i = 0; i < 10; i++) Controls.ShelfMirror.Rasterise(spines, (int)(w * 0.9), 12, Theming.SystemPalette.Room).Dispose();
                var rasterMs = sw.Elapsed.TotalMilliseconds / 10;

                static string Stats(List<double> v)
                {
                    if (v.Count == 0) return "-";
                    var s = v.OrderBy(x => x).ToList();
                    double P(double q) => s[Math.Min(s.Count - 1, (int)Math.Floor(q * s.Count))];
                    return $"median {P(0.5):0.0} ms, p95 {P(0.95):0.0} ms, max {s[^1]:0.0} ms (n={s.Count})";
                }
                report.AppendLine();
                report.AppendLine($"{w}x{h}, {lib.Items.Count} games, {grid.Columns} columns, {grid.RowCount} rows, tiles realised: {window.GetVisualDescendants().OfType<GameTile>().Count()}");
                report.AppendLine($"  load (navigate -> items bound): {loadMs:0} ms; first full frame: {firstFrame:0.0} ms");
                report.AppendLine($"  focus move, input + layout:       {Stats(layout)}");
                report.AppendLine($"  focus move, full frame render:    {Stats(frame)}");
                report.AppendLine($"  page jump + layout + full frame:  {Stats(page)}");
                report.AppendLine($"  shelf mirror: {rasterDuringMoves} re-rasterisations during {moves.Count + page.Count} moves; one rasterisation of {count} spines at {(int)(w * 0.9)} px: {rasterMs:0.00} ms");
            }
            finally
            {
                window.Close();
            }
        }
        File.WriteAllText(Path.Combine(outDir, "perf-library.txt"), report.ToString());
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
