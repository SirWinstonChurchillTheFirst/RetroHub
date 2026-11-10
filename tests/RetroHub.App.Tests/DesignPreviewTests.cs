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
    /// <summary>The demo with EVERY catalog system (DESIGN 7.4: a wall of 32 systems), Konsolen and Systemseite only.</summary>
    [Fact]
    public Task Demo_all_systems() => Run("demoall", null, AllSizes);

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

    private static readonly string[] PageTokens = ["start", "states", "bibliothek", "detail", "konsolen", "systemseite", "emulatoren", "einstellungen", "importe", "einschalten", "switch", "maus"];
    private static readonly string[] VariantTokens = ["preview", "nocovers", "demo", "demo1000", "demoall", "empty", "perf"];

    /// <summary>Sheets rendered by their own facts ("specimen", "motive"): asked for alone, the page runs are skipped.</summary>
    private static readonly string[] SheetTokens = ["specimen", "motive"];

    private static bool OnlySheets => OnlyTokens.Any(SheetTokens.Contains) && !OnlyTokens.Any(t => VariantTokens.Contains(t) || PageTokens.Contains(t));

    private static bool Wants(string token, string[] kind)
    {
        if (OnlySheets) return false;
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

    /// <summary>
    /// The motifs of every catalog system (DESIGN 6, M5 acceptance), WITHOUT any text: <c>motive-farbig.png</c> (each
    /// system's wall with band, texture and motif – "distinguishable without reading"), <c>motive-umriss.png</c> (the
    /// outsider-test sheet: one flat paper silhouette per motif, no colour), <c>motive-klein.png</c> (the same walls at
    /// spine/placeholder size). The order is written to <c>motive-legende.txt</c>, never into the pictures.
    /// </summary>
    [Fact]
    public Task Motif_sheet() => UiTest.Run(() =>
    {
        Assert.SkipWhen(string.IsNullOrEmpty(DesignDir), "RETROHUB_DESIGN_DIR is not set (design previews only on request)");
        Assert.SkipWhen(OnlyTokens.Length > 0 && !OnlyTokens.Contains("motive"), "filtered out (RETROHUB_DESIGN_ONLY)");
        var outDir = Path.Combine(DesignDir!, "shots", Milestone);
        Directory.CreateDirectory(outDir);
        TestSupport.CreateContext();
        string[] ids =
        [
            "nes", "mastersystem", "snes", "megadrive", "n64", "sega32x", "pcengine", "atari2600",
            "gb", "gbc", "wonderswan", "gba", "gamegear", "lynx", "ngp", "psp",
            "nds", "3ds", "ps1", "saturn", "dreamcast", "segacd", "gamecube", "wii",
            "wiiu", "switch", "ps2", "virtualboy", "arcade", "pc", "msx", "atari7800",
        ];
        Assert.Equal(Theming.SystemPalette.KnownIds.Count, ids.Length);
        File.WriteAllLines(Path.Combine(outDir, "motive-legende.txt"),
            ids.Select((id, i) => $"{i / 8 + 1}.{i % 8 + 1}  {id}  {Theming.SystemPalette.Get(id).Motif}"));
        var room = new Avalonia.Media.SolidColorBrush(Theming.SystemPalette.Room);
        var paper = new Avalonia.Media.SolidColorBrush(Theming.SystemPalette.Paper);

        void Sheet(string file, double cell, double gap, Func<string, Control> make)
        {
            var grid = new Avalonia.Controls.WrapPanel { ItemSpacing = gap, LineSpacing = gap, Margin = new Avalonia.Thickness(gap) };
            foreach (var id in ids)
            {
                var c = make(id);
                c.Width = cell;
                c.Height = cell;
                grid.Children.Add(c);
            }
            var w = 8 * cell + 9 * gap;
            var window = new Avalonia.Controls.Window { Width = w, Height = 4 * cell + 5 * gap, Background = room, Content = grid };
            window.Show();
            TestSupport.Settle(300);
            var frame = window.CaptureRenderedFrame();
            Assert.NotNull(frame);
            frame!.Save(Path.Combine(outDir, file), new Avalonia.Media.Imaging.PngBitmapEncoderOptions());
            window.Close();
        }

        Sheet("motive-farbig.png", 200, 16, id => new Controls.SystemWall { SystemId = id, MotifShare = 0.72, MotifInset = 14 });
        Sheet("motive-umriss.png", 160, 32, id => new Controls.MotifView { SystemId = id, SilhouetteBrush = paper });
        Sheet("motive-klein.png", 72, 10, id => new Controls.SystemWall { SystemId = id, MotifShare = 0.72, MotifInset = 4, BandHeight = 4 });
        // hero size (≥ 160): the motif with its relief (edge light, foot, material) on its wall, as on the system page
        Sheet("motive-held.png", 240, 16, id => new Avalonia.Controls.Panel
        {
            Children =
            {
                new Controls.SystemWall { SystemId = id, MotifShare = 0 },
                new Controls.MotifView
                {
                    SystemId = id, Width = 176, Height = 176, Margin = new Avalonia.Thickness(0, 0, 14, 0),
                    HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right, VerticalAlignment = Avalonia.Layout.VerticalAlignment.Bottom,
                },
            },
        });
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
            Services.Demo.DemoData.AllSystems = variant == "demoall";
            var ctx = root == null
                ? TestSupport.CreateContext(s => s.Motion = MotionLevel.Full)
                : new UiContext(CreateReal(root));
            Services.Demo.DemoData.TargetCount = null;
            Services.Demo.DemoData.AllSystems = false;
            if (root != null) App.InitializeUi(ctx.Services.Settings.Current, null);
            var vm = new MainViewModel(ctx);
            var window = new MainWindow(vm) { Width = w, Height = h };
            window.Show();
            try
            {
                TestSupport.Pump(() => vm.CurrentPage is HomeViewModel { IsLoaded: true }, 8000);
                var prefix = Path.Combine(outDir, $"{variant}-{w}x{h}");
                var wallOnly = variant == "demoall"; // the many-systems demo: Konsolen and Systemseite only
                if (variant != "demo1000" && !wallOnly)
                {
                    if (Wants("start", PageTokens)) Save(window, prefix + "-1-start.png", 1400);
                    if (Wants("states", PageTokens)) StartStates(window, vm, prefix);
                }

                if (!wallOnly && Wants("bibliothek", PageTokens)) LibraryStates(window, vm, prefix, deep: variant == "demo1000");
                if (variant == "demo1000") continue;

                var pick = PickGame(ctx);
                if (!wallOnly && pick != null && Wants("detail", PageTokens)) DetailStates(window, vm, ctx, pick, prefix);
                // asked for explicitly only: the Switch games (GameTDB covers) in the library and on the detail page
                if (!wallOnly && OnlyTokens.Contains("switch")) SwitchStates(window, vm, ctx, prefix);
                // asked for explicitly only: what the mouse sees (R2) – hover, the Winkel plates, the right-click list
                if (!wallOnly && OnlyTokens.Contains("maus")) MouseStates(window, vm, prefix);

                if (Wants("konsolen", PageTokens)) SystemsStates(window, vm, prefix);

                if (pick != null && Wants("systemseite", PageTokens)) SystemStates(window, vm, ctx, pick, prefix);

                if (wallOnly) continue;
                if (Wants("emulatoren", PageTokens)) EmulatorStates(window, vm, prefix);
                if (Wants("einstellungen", PageTokens)) SettingsStates(window, vm, prefix);
                // a scan only in the demo: the preview roots are copies of a real library whose folders are not here
                if (Wants("importe", PageTokens)) ImportsStates(window, vm, prefix, scan: root == null);
                if (pick != null && root == null && Wants("einschalten", PageTokens)) PowerOnStates(window, vm, pick, prefix);
            }
            finally
            {
                window.Close();
                if (root != null) ctx.Services.Dispose();
            }
        }
    });

    /// <summary>
    /// Konsolen (DESIGN 7.4) driven like a controller: the first front open; three spines on (the row glides, the front
    /// stands in slot 3, the page ground is that system's wall); the last system (the row ends at the right edge); down
    /// to the "Noch leer" edge when the library has systems without games.
    /// </summary>
    private static void SystemsStates(MainWindow window, MainViewModel vm, string prefix)
    {
        Control? Focused() => window.FocusManager?.GetFocusedElement() as Control;
        vm.Navigator.Navigate(PageKey.Systems);
        TestSupport.Pump(() => vm.CurrentPage is SystemsViewModel { IsLoaded: true }, 8000);
        TestSupport.Pump(() => Focused() is Controls.SystemSpine, 3000);
        Save(window, prefix + "-4-konsolen.png", 900);
        if (vm.CurrentPage is not SystemsViewModel { HasSystems: true } systems) return;
        for (var i = 0; i < 3; i++) { window.Execute(Input.NavAction.Right); TestSupport.Settle(60); }
        Save(window, prefix + "-4b-konsolen-fokus.png", 900);
        for (var i = 0; i < systems.Systems.Count; i++) window.Execute(Input.NavAction.Right);
        Save(window, prefix + "-4c-konsolen-ende.png", 900);
        if (systems.HasEmptySystems)
        {
            window.Execute(Input.NavAction.Down);
            window.Execute(Input.NavAction.Right);
            window.Execute(Input.NavAction.Right);
            Save(window, prefix + "-4d-konsolen-leer.png", 900);
            window.Execute(Input.NavAction.Up);
        }
        for (var i = 0; i < systems.Systems.Count; i++) window.Execute(Input.NavAction.Left);
        TestSupport.Settle(300);
    }

    /// <summary>
    /// Systemseite (DESIGN 7.5): the subject's system with a box focused; up into the banner (Systeminfo focused);
    /// the Systeminfo overlay; deeper in the grid; the shoulder to the next system; a system with few games
    /// ("Fachende"); an empty system (when the library has one).
    /// </summary>
    private static void SystemStates(MainWindow window, MainViewModel vm, UiContext ctx, GameItemViewModel pick, string prefix)
    {
        Control? Focused() => window.FocusManager?.GetFocusedElement() as Control;
        vm.Navigator.Navigate(PageKey.Systems);
        TestSupport.Pump(() => vm.CurrentPage is SystemsViewModel { IsLoaded: true }, 8000);
        var systemId = ctx.Services.Catalog.GetSystem(pick.SystemId)?.Id ?? pick.SystemId;
        vm.Navigator.Navigate(PageKey.System, systemId);
        TestSupport.Pump(() => vm.CurrentPage is SystemViewModel { IsLoaded: true, Games.Count: > 0 } s && s.System?.Id == systemId, 8000);
        TestSupport.Pump(() => Focused() is GameTile, 3000);
        window.Execute(Input.NavAction.Right);
        Save(window, prefix + "-5-systemseite.png", 1200);
        if (vm.CurrentPage is not SystemViewModel page) return;
        window.Execute(Input.NavAction.Up);
        Save(window, prefix + "-5b-systemseite-banner.png", 700);
        page.ShowInfoCommand.Execute(null);
        Save(window, prefix + "-5c-systemseite-info.png", 700);
        window.Execute(Input.NavAction.Back);
        TestSupport.Settle(200);
        window.Execute(Input.NavAction.Down);
        for (var i = 0; i < 2; i++) window.Execute(Input.NavAction.Down);
        Save(window, prefix + "-5d-systemseite-tief.png", 900);
        var before = page.System?.Id;
        window.Execute(Input.NavAction.PageRight);
        TestSupport.Pump(() => page.System?.Id != before && page.IsLoaded, 4000);
        Save(window, prefix + "-5e-systemseite-wechsel.png", 1000);
        var stats = ctx.Services.Games.Stats();
        var few = ctx.Services.Catalog.Systems.Where(s => stats.GamesPerSystem.GetValueOrDefault(s.Id) is > 0 and <= 6)
            .OrderBy(s => stats.GamesPerSystem.GetValueOrDefault(s.Id)).FirstOrDefault();
        if (few != null)
        {
            vm.Navigator.Navigate(PageKey.System, few.Id);
            TestSupport.Pump(() => page.System?.Id == few.Id && page.IsLoaded, 4000);
            Save(window, prefix + "-5f-systemseite-wenige.png", 1000);
        }
        var empty = ctx.Services.Catalog.Systems.FirstOrDefault(s => stats.GamesPerSystem.GetValueOrDefault(s.Id) == 0);
        if (empty != null)
        {
            vm.Navigator.Navigate(PageKey.System, empty.Id);
            TestSupport.Pump(() => page.System?.Id == empty.Id && page.IsLoaded, 4000);
            Save(window, prefix + "-5g-systemseite-leer.png", 1000);
        }
    }

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
        report.AppendLine("Main runs with motion off (no transitions); 'frame' = time of window.CaptureRenderedFrame() after the step (dirty regions rasterised on the CPU).");
        foreach (var (w, h) in new[] { (1920, 1080), (2560, 1440) })
        {
            const int count = 1000;
            Services.Demo.DemoData.TargetCount = count;
            // motion off: the headless capture waits out running transitions (the 140 ms lift), which would hide the
            // actual work per frame – see the motion comparison at the end of the report
            var ctx = TestSupport.CreateContext(s => s.Motion = MotionLevel.Off);
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

                var createdBefore = GameTile.Created;
                // 1) focus moves like a held direction: down through the rows, right along one, back up
                var layout = new List<double>();
                var frame = new List<double>();
                var moves = new List<Input.NavAction>();
                for (var i = 0; i < 40; i++) moves.Add(Input.NavAction.Down);
                for (var i = 0; i < 6; i++) moves.Add(Input.NavAction.Right);
                for (var i = 0; i < 20; i++) moves.Add(Input.NavAction.Up);
                var downLayout = new List<double>();
                var frameScroll = new List<double>();
                var frameStill = new List<double>();
                var lastOffset = grid.ScrollViewer.Offset.Y;
                foreach (var move in moves)
                {
                    sw.Restart();
                    window.Execute(move);
                    Avalonia.Threading.Dispatcher.UIThread.RunJobs();
                    window.UpdateLayout();
                    layout.Add(sw.Elapsed.TotalMilliseconds);
                    if (move == Input.NavAction.Down) downLayout.Add(layout[^1]);
                    sw.Restart();
                    window.CaptureRenderedFrame();
                    frame.Add(sw.Elapsed.TotalMilliseconds);
                    (Math.Abs(grid.ScrollViewer.Offset.Y - lastOffset) > 0.5 ? frameScroll : frameStill).Add(frame[^1]);
                    lastOffset = grid.ScrollViewer.Offset.Y;
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
                var createdDuring = GameTile.Created - createdBefore;
                // 2a) a move broken down (no scrolling): the grid's focus alone, the view model's focus index alone, the
                // whole Execute path
                var parts = new Dictionary<string, List<double>> { ["grid.FocusIndex"] = [], ["vm.FocusIndex"] = [], ["window.Execute"] = [] };
                var baseIndex = grid.SelectedIndex - grid.SelectedIndex % grid.Columns;
                for (var i = 0; i < 8; i++)
                {
                    sw.Restart(); grid.FocusIndex(baseIndex + (i % 2 == 0 ? 1 : 2)); Avalonia.Threading.Dispatcher.UIThread.RunJobs(); window.UpdateLayout();
                    parts["grid.FocusIndex"].Add(sw.Elapsed.TotalMilliseconds);
                    window.CaptureRenderedFrame();
                    sw.Restart(); lib.FocusIndex = baseIndex + (i % 2 == 0 ? 3 : 4); Avalonia.Threading.Dispatcher.UIThread.RunJobs(); window.UpdateLayout();
                    parts["vm.FocusIndex"].Add(sw.Elapsed.TotalMilliseconds);
                    window.CaptureRenderedFrame();
                    sw.Restart(); window.Execute(i % 2 == 0 ? Input.NavAction.Right : Input.NavAction.Left); Avalonia.Threading.Dispatcher.UIThread.RunJobs(); window.UpdateLayout();
                    parts["window.Execute"].Add(sw.Elapsed.TotalMilliseconds);
                    window.CaptureRenderedFrame();
                }
                // 2b) one row rebound to other games (what a recycled row costs): DataContext swap + layout + frame
                var rebind = new List<double>();
                var rebindFrame = new List<double>();
                var presenter = grid.RowsControl.GetRealizedContainers().OfType<Panel>().First();
                for (var i = 0; i < 12; i++)
                {
                    var rowIndex = 40 + i * 7;
                    var row = new Controls.CoverRow(rowIndex, lib.Items.Skip(rowIndex * grid.Columns).Take(grid.Columns).Cast<object>().ToList());
                    sw.Restart();
                    presenter.DataContext = row;
                    window.UpdateLayout();
                    rebind.Add(sw.Elapsed.TotalMilliseconds);
                    sw.Restart(); window.CaptureRenderedFrame(); rebindFrame.Add(sw.Elapsed.TotalMilliseconds);
                }
                // 3) for scale: a full frame of a flat page (settings) and of the library standing still, same window
                var still = new List<double>();
                for (var i = 0; i < 10; i++) { sw.Restart(); window.CaptureRenderedFrame(); still.Add(sw.Elapsed.TotalMilliseconds); }
                // control: a change of 2 × 20 px (only the mirror's mark moves) – shows whether a capture repaints
                // just the dirty region or the whole window whenever anything changed
                var tiny = new List<double>();
                for (var i = 0; i < 10; i++)
                {
                    mirror.FocusIndex = (mirror.FocusIndex + 37) % lib.Items.Count;
                    Avalonia.Threading.Dispatcher.UIThread.RunJobs();
                    sw.Restart(); window.CaptureRenderedFrame(); tiny.Add(sw.Elapsed.TotalMilliseconds);
                }
                // isolate what a focus move dirties: (a) a row container's ZIndex, (b) the lift of one cover area,
                // (c) the footer location sentence
                var probe = new Dictionary<string, List<double>> { ["zindex"] = [], ["lift"] = [], ["footer"] = [] };
                var realisedRows = grid.RowsControl.GetRealizedContainers().ToList();
                var someTile = window.GetVisualDescendants().OfType<GameTile>().First(t => t.IsEffectivelyVisible);
                var area = someTile.GetVisualDescendants().OfType<Panel>().First(x => x.Name == "PART_CoverArea");
                for (var i = 0; i < 6; i++)
                {
                    var c = realisedRows[i % realisedRows.Count];
                    c.ZIndex = c.ZIndex == 0 ? 1 : 0;
                    Avalonia.Threading.Dispatcher.UIThread.RunJobs();
                    sw.Restart(); window.CaptureRenderedFrame(); probe["zindex"].Add(sw.Elapsed.TotalMilliseconds);
                    area.RenderTransform = Avalonia.Media.Transformation.TransformOperations.Parse(i % 2 == 0 ? "scale(1.06)" : "scale(1)");
                    Avalonia.Threading.Dispatcher.UIThread.RunJobs();
                    TestSupport.Settle(200);
                    window.CaptureRenderedFrame();
                    lib.FocusIndex = (lib.FocusIndex + 1) % lib.Items.Count;
                    Avalonia.Threading.Dispatcher.UIThread.RunJobs();
                    sw.Restart(); window.CaptureRenderedFrame(); probe["footer"].Add(sw.Elapsed.TotalMilliseconds);
                }
                // what one in-view move repaints: the renderer's dirty-rect overlay in a saved frame
                window.Execute(Input.NavAction.Left);
                TestSupport.Settle(400);
                window.CaptureRenderedFrame();
                window.RendererDiagnostics.DebugOverlays = Avalonia.Rendering.RendererDebugOverlays.DirtyRects;
                window.Execute(Input.NavAction.Right);
                Avalonia.Threading.Dispatcher.UIThread.RunJobs();
                window.CaptureRenderedFrame()?.Save(Path.Combine(outDir, $"perf-dirty-{w}x{h}.png"), new Avalonia.Media.Imaging.PngBitmapEncoderOptions());
                window.RendererDiagnostics.DebugOverlays = Avalonia.Rendering.RendererDebugOverlays.None;
                vm.Navigator.Navigate(PageKey.Settings);
                TestSupport.Settle(600);
                var flat = new List<double>();
                for (var i = 0; i < 10; i++) { sw.Restart(); window.CaptureRenderedFrame(); flat.Add(sw.Elapsed.TotalMilliseconds); }
                // 4) the shelf mirror: one rasterisation per list, none per move
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
                report.AppendLine($"  ... of which moves down (a new row realised at the bottom): {Stats(downLayout)}");
                report.AppendLine($"  frame after a move that scrolled the grid:     {Stats(frameScroll)}");
                report.AppendLine($"  frame after a move inside the view (no scroll): {Stats(frameStill)}");
                report.AppendLine($"  page jump + layout + full frame:  {Stats(page)}");
                var bare = new List<double>();
                for (var i = 0; i < 10; i++)
                {
                    var off = grid.ScrollViewer.Offset;
                    sw.Restart();
                    grid.ScrollViewer.Offset = off.WithY(off.Y + (i % 2 == 0 ? 1 : -1) * (grid.TileHeight + grid.RowSpacing));
                    Avalonia.Threading.Dispatcher.UIThread.RunJobs();
                    window.UpdateLayout();
                    bare.Add(sw.Elapsed.TotalMilliseconds);
                    window.CaptureRenderedFrame();
                }
                report.AppendLine($"  bare scroll by one row (offset + layout, no focus change): {Stats(bare)}");
                // the same down moves twice over the same rows: first visit vs. revisit
                var visits = new Dictionary<string, List<double>> { ["first visit"] = [], ["revisit"] = [] };
                grid.FocusIndex(lib.Items.Count / 2);
                TestSupport.Settle(100);
                var start = grid.SelectedIndex;
                foreach (var key in new[] { "first visit", "revisit" })
                {
                    grid.FocusIndex(start);
                    TestSupport.Settle(100);
                    for (var i = 0; i < 12; i++)
                    {
                        sw.Restart(); window.Execute(Input.NavAction.Down); Avalonia.Threading.Dispatcher.UIThread.RunJobs(); window.UpdateLayout();
                        visits[key].Add(sw.Elapsed.TotalMilliseconds);
                        window.CaptureRenderedFrame();
                    }
                }
                foreach (var (k, v) in visits) report.AppendLine($"  down moves, {k} of the rows: {Stats(v)}");
                foreach (var (k, v) in parts) report.AppendLine($"  in-view move part {k}: {Stats(v)}");
                report.AppendLine($"  tiles constructed during the {moves.Count + page.Count} moves: {createdDuring} (recycling)");
                report.AppendLine($"  one row of {grid.Columns} tiles rebound to unseen games: layout {Stats(rebind)}; its frame {Stats(rebindFrame)}");
                report.AppendLine($"  frame after a 2 x 20 px change (mirror mark only): {Stats(tiny)}");
                report.AppendLine($"  frame after a row container's ZIndex toggles: {Stats(probe["zindex"])}");
                report.AppendLine($"  frame after the footer/mirror follow a new focus index: {Stats(probe["footer"])}");
                report.AppendLine($"  full frame, library standing still: {Stats(still)}; settings page (flat rows), same window: {Stats(flat)}");
                report.AppendLine($"  shelf mirror: {rasterDuringMoves} re-rasterisations during {moves.Count + page.Count} moves; one rasterisation of {count} spines at {(int)(w * 0.9)} px: {rasterMs:0.00} ms");
            }
            finally
            {
                window.Close();
            }
        }
        // the placeholder text of a tile never seen before: title fit (expanded, maybe condensed) and the FormattedTexts
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var n = 0;
            for (var i = 0; i < 200; i++)
            {
                var title = $"Unseen Adventure Number {i} Deluxe";
                var fit = Controls.PlaceholderTitle.Fit(title, 140, 150, 19, Controls.PlaceholderTitle.MaxLines);
                _ = new Avalonia.Media.FormattedText(fit.Text, System.Globalization.CultureInfo.CurrentUICulture, Avalonia.Media.FlowDirection.LeftToRight,
                    Theming.FontRoles.PosterFace, fit.Size, null) { LineHeight = 21 }.Height;
                _ = new Avalonia.Media.FormattedText(title, System.Globalization.CultureInfo.CurrentUICulture, Avalonia.Media.FlowDirection.LeftToRight,
                    Theming.FontRoles.SpineFace, 15, null) { MaxTextWidth = 150, MaxLineCount = 1, Trimming = Avalonia.Media.TextTrimming.CharacterEllipsis }.Width;
                n++;
            }
            report.AppendLine();
            report.AppendLine($"placeholder text of an unseen title (fit + title + spine): {sw.Elapsed.TotalMilliseconds / n:0.00} ms per tile");
        }
        // in-view moves only (left/right inside one row, no scrolling), with motion on and off: separates the cost of the
        // running transitions (lift 140 ms, light) from the cost of what is redrawn
        foreach (var level in new[] { MotionLevel.Full, MotionLevel.Off })
        {
            Services.Demo.DemoData.TargetCount = 1000;
            var ctx = TestSupport.CreateContext(s => s.Motion = level);
            Services.Demo.DemoData.TargetCount = null;
            var vm = new MainViewModel(ctx);
            var window = new MainWindow(vm) { Width = 1920, Height = 1080 };
            window.Show();
            try
            {
                TestSupport.Pump(() => vm.CurrentPage is HomeViewModel { IsLoaded: true }, 8000);
                vm.Navigator.Navigate(PageKey.Library);
                TestSupport.Pump(() => vm.CurrentPage is LibraryViewModel { IsLoaded: true, Items.Count: > 0 }, 15000);
                TestSupport.Settle(600);
                var sw = System.Diagnostics.Stopwatch.StartNew();
                var frames = new List<double>();
                var settled = new List<double>();
                for (var i = 0; i < 14; i++)
                {
                    window.Execute(i % 2 == 0 ? Input.NavAction.Right : Input.NavAction.Left);
                    Avalonia.Threading.Dispatcher.UIThread.RunJobs();
                    sw.Restart(); window.CaptureRenderedFrame(); frames.Add(sw.Elapsed.TotalMilliseconds);
                    TestSupport.Settle(250); // transitions done
                    sw.Restart(); window.CaptureRenderedFrame(); settled.Add(sw.Elapsed.TotalMilliseconds);
                }
                var o = frames.OrderBy(x => x).ToList();
                var q = settled.OrderBy(x => x).ToList();
                report.AppendLine();
                report.AppendLine($"in-view moves, motion {level}: first frame after the move median {o[o.Count / 2]:0.0} ms (max {o[^1]:0.0}); a frame 250 ms later median {q[q.Count / 2]:0.0} ms");
            }
            finally { window.Close(); }
        }
        File.WriteAllText(Path.Combine(outDir, "perf-library.txt"), report.ToString());
    });

    /// <summary>
    /// The game detail in the states that matter (DESIGN 7.3, M4), driven like a controller: the stage with "Spielen"
    /// focused; the pulled box focused (lift, cone, board piece, ring around spine + front); the back ("Rückseite") after
    /// Down; the overlays Mehr, Dateiinfo, Cover and – when there are screens – the two screens large; then a plain game
    /// (never played, as little known as the library has: no cover if possible) on its stage and its back.
    /// </summary>
    private static void DetailStates(MainWindow window, MainViewModel vm, UiContext ctx, GameItemViewModel pick, string prefix)
    {
        Control? Focused() => window.FocusManager?.GetFocusedElement() as Control;
        GameDetailViewModel Open(GameItemViewModel game)
        {
            vm.Navigator.Navigate(PageKey.GameDetail, game);
            TestSupport.Pump(() => vm.CurrentPage is GameDetailViewModel { Game: not null } d && d.Game.Id == game.Id && d.Emulators.Count > 0, 8000);
            TestSupport.Settle(300);
            return (GameDetailViewModel)vm.CurrentPage!;
        }
        void Sheet(GameDetailViewModel d, System.Windows.Input.ICommand show, string file)
        {
            show.Execute(null);
            Save(window, prefix + file, 700);
            window.Execute(Input.NavAction.Back);
            TestSupport.Settle(200);
        }
        var detail = Open(pick);
        Save(window, prefix + "-3-detail.png", 1200);
        // the box: left from the actions
        for (var i = 0; i < 4 && Focused() is not GameTile; i++) window.Execute(Input.NavAction.Left);
        if (Focused() is GameTile) Save(window, prefix + "-3b-detail-schachtel.png", 700);
        // the back: down from the actions
        window.Execute(Input.NavAction.Right);
        window.Execute(Input.NavAction.Down);
        Save(window, prefix + "-3c-detail-rueckseite.png", 900);
        // the start rows of the back (the screens come first at 1920 when there are some)
        for (var i = 0; i < 3 && Focused()?.Name != "EmulatorBox"; i++) window.Execute(Input.NavAction.Right);
        if (Focused()?.Name == "EmulatorBox")
        {
            Save(window, prefix + "-3d-detail-start.png", 700);
            // the in-style choice list at the row (replaces the stock drop-down)
            window.Execute(Input.NavAction.Accept);
            Save(window, prefix + "-3l-detail-auswahl.png", 600);
            window.Execute(Input.NavAction.Back);
            TestSupport.Settle(200);
        }
        // the shoulder on to "Daneben im Regal" (the last section): its boxes take the object focus
        window.Execute(Input.NavAction.PageRight);
        TestSupport.Settle(150);
        if (Focused() is GameTile { Parent: StackPanel { Name: "NeighbourRow" } }) Save(window, prefix + "-3d2-detail-regal.png", 900);
        for (var i = 0; i < 4; i++) window.Execute(Input.NavAction.PageLeft); // shoulder: back to the stage
        TestSupport.Settle(200);
        Sheet(detail, detail.ShowMoreCommand, "-3e-detail-mehr.png");
        Sheet(detail, detail.ShowFileInfoCommand, "-3f-detail-datei.png");
        Sheet(detail, detail.ShowCoverCommand, "-3g-detail-cover.png");
        if (detail.HasScreens) Sheet(detail, detail.ShowScreensCommand, "-3h-detail-bildschirme.png");
        // "Weiterlesen" opens the text in place – only offered (and shot) when the 8 lines clip it (QA 2)
        if (detail.HasDescription && window.CurrentPageView is Views.Pages.GameDetailPage { } dp && dp.FindControl<Button>("ReadMoreButton") is { IsVisible: true })
            Sheet(detail, detail.ShowDescriptionCommand, "-3i-detail-text.png");

        // a plain game: never played, nothing known but the file; without a cover where the library has one
        var all = ctx.Services.Games.Query(new GameQuery());
        var plain = all.Where(g => g.PlayCount == 0 && g.Id != pick.Id)
            .OrderBy(g => (string.IsNullOrEmpty(g.CoverPath) ? 0 : 2) + (string.IsNullOrEmpty(g.Description) ? 0 : 1) + (string.IsNullOrEmpty(g.Genre) ? 0 : 1))
            .FirstOrDefault();
        if (plain == null) return;
        Open(ctx.Items.Get(plain));
        Save(window, prefix + "-3j-detail-schlicht.png", 1200);
        window.Execute(Input.NavAction.Down);
        Save(window, prefix + "-3k-detail-schlicht-rueckseite.png", 900);
    }

    /// <summary>
    /// Emulatoren (DESIGN 7.6): the system rows; the first one opened by confirm; the page filtered to a system that
    /// cannot start (as from a game's "Emulator einrichten"); the folded "Weitere Programme" opened.
    /// </summary>
    private static void EmulatorStates(MainWindow window, MainViewModel vm, string prefix)
    {
        Control? Focused() => window.FocusManager?.GetFocusedElement() as Control;
        vm.Navigator.Navigate(PageKey.Emulators);
        TestSupport.Pump(() => vm.CurrentPage is EmulatorsViewModel, 8000);
        Save(window, prefix + "-6-emulatoren.png", 700);
        if (vm.CurrentPage is not EmulatorsViewModel emus) return;
        window.Execute(Input.NavAction.Accept);
        Save(window, prefix + "-6b-emulatoren-offen.png", 600);
        window.Execute(Input.NavAction.Accept);
        TestSupport.Settle(300);
        if (emus.Rows.FirstOrDefault(r => r.NotReady) is { System: { } missing })
        {
            vm.Navigator.Navigate(PageKey.Emulators, missing.Id);
            TestSupport.Settle(400);
            Save(window, prefix + "-6c-emulatoren-fehlt.png", 700);
            vm.Navigator.GoBack();
            TestSupport.Settle(300);
            vm.Navigator.Navigate(PageKey.Emulators);
            TestSupport.Pump(() => vm.CurrentPage is EmulatorsViewModel { IsFiltered: false }, 3000);
        }
        if (emus.OthersRow is { } others)
        {
            others.IsOpen = true;
            var button = window.GetVisualDescendants().OfType<Button>().FirstOrDefault(b => b.Name == "OthersButton");
            button?.Focus(Avalonia.Input.NavigationMethod.Directional);
            TestSupport.Settle(400);
            button?.BringIntoView();
            Save(window, prefix + "-6d-emulatoren-weitere.png", 600);
            others.IsOpen = false;
        }
        _ = Focused();
    }

    /// <summary>
    /// Einstellungen as the service menu (DESIGN 7.6): the first row in focus; one step right on "Lichtfarbe" (and back);
    /// the shoulder to "Steuerung" with "Zurück zu RetroHub"; the folders at the end.
    /// </summary>
    private static void SettingsStates(MainWindow window, MainViewModel vm, string prefix)
    {
        Control? Focused() => window.FocusManager?.GetFocusedElement() as Control;
        vm.Navigator.Navigate(PageKey.Settings);
        TestSupport.Pump(() => Focused() is Controls.ServiceRow, 3000);
        Save(window, prefix + "-7-einstellungen.png", 600);
        if (Focused() is not Controls.ServiceRow) return;
        // a value changes at once: the next light colour – the shot shows the new light, then it goes back
        window.Execute(Input.NavAction.Right);
        Save(window, prefix + "-7b-einstellungen-wert.png", 600);
        window.Execute(Input.NavAction.Left);
        TestSupport.Settle(200);
        // the test-card TV with the channel display standing on it (as when Kanalanzeige is switched on)
        if (window.CurrentPageView is Views.Pages.SettingsPage settings)
        {
            settings.Card.FlashOsd();
            Save(window, prefix + "-7e-einstellungen-testbild.png", 200);
        }
        window.Execute(Input.NavAction.PageRight);
        window.Execute(Input.NavAction.PageRight);
        window.Execute(Input.NavAction.Down);
        Save(window, prefix + "-7c-einstellungen-steuerung.png", 600);
        for (var i = 0; i < 4; i++) window.Execute(Input.NavAction.PageRight);
        Save(window, prefix + "-7d-einstellungen-ordner.png", 600);
    }

    /// <summary>
    /// Importe (DESIGN 7.6): the folders and the scan column; in the demo a scan running (segment bar), its delivery
    /// note with the notes folded open, and a folder's "Mehr" list.
    /// </summary>
    private static void ImportsStates(MainWindow window, MainViewModel vm, string prefix, bool scan)
    {
        Control? Focused() => window.FocusManager?.GetFocusedElement() as Control;
        vm.Navigator.Navigate(PageKey.Imports);
        TestSupport.Pump(() => vm.CurrentPage is ImportsViewModel, 3000);
        Save(window, prefix + "-8-importe.png", 600);
        if (vm.CurrentPage is not ImportsViewModel imports) return;
        if (scan)
        {
            imports.ScanCommand.Execute(null);
            TestSupport.Pump(() => imports.IsScanning && !imports.IsIndeterminate && imports.Progress > 30, 6000);
            Save(window, prefix + "-8b-importe-scan.png", 120);
            TestSupport.Pump(() => imports.HasReport, 15_000);
            imports.IssuesOpen = true;
            Save(window, prefix + "-8c-importe-lieferschein.png", 600);
            imports.IssuesOpen = false;
        }
        // the first folder row (left of the scan column), then its Mehr list
        var row = window.GetVisualDescendants().OfType<Controls.ServiceRow>().FirstOrDefault(r => r.Classes.Contains("folder") && r.Setting?.IsFixed == false)
                  ?? window.GetVisualDescendants().OfType<Controls.ServiceRow>().FirstOrDefault(r => r.Classes.Contains("folder"));
        if (row == null) return;
        row.Focus(Avalonia.Input.NavigationMethod.Directional);
        Save(window, prefix + "-8d-importe-ordner.png", 400);
        window.Execute(Input.NavAction.Accept);
        Save(window, prefix + "-8e-importe-mehr.png", 600);
        window.Execute(Input.NavAction.Back);
        TestSupport.Settle(200);
        _ = Focused();
    }

    /// <summary>
    /// The start transition "Einschalten" (DESIGN 9.3) as fixed frames of the FlightLayer, rendered with the detail of
    /// <paramref name="pick"/> behind it: the box on its way (120 ms), the line in the system's light (270 ms), the
    /// overexposed frame (320 ms), the opening (420 ms), the start overlay (600 ms), the switch-off (return).
    /// </summary>
    private static void PowerOnStates(MainWindow window, MainViewModel vm, GameItemViewModel pick, string prefix)
    {
        Control? Focused() => window.FocusManager?.GetFocusedElement() as Control;
        vm.Navigator.Navigate(PageKey.GameDetail, pick);
        TestSupport.Pump(() => vm.CurrentPage is GameDetailViewModel { Game: not null } d && d.Game.Id == pick.Id && d.Emulators.Count > 0, 8000);
        TestSupport.Settle(600);
        if (window.CurrentPageView is Views.Pages.PageBase page) page.FocusDefault();
        TestSupport.Settle(300);
        // a real demo launch held in "Starting" (the start surface stays), the frames frozen on the flight's timeline
        var before = vm.Launch.MinimumTransition;
        vm.Launch.MinimumTransition = TimeSpan.FromSeconds(30);
        try
        {
            _ = vm.Launch.LaunchAsync(pick);
            if (!TestSupport.Pump(() => vm.Launch.IsStarting, 5000)) return;
            var flight = window.FlightLayerControl;
            TestSupport.Settle(50);
            foreach (var (ms, name) in new[] { (110, "a-flug"), (270, "b-linie"), (325, "c-blitz"), (400, "d-oeffnen"), (520, "e-start") })
            {
                flight.Freeze(ms);
                Save(window, prefix + $"-9{name}.png", 60);
            }
            if (vm.Launch.StopCommand.CanExecute(null)) vm.Launch.StopCommand.Execute(null);
            TestSupport.Pump(() => vm.Launch.Phase == LaunchPhase.Idle, 8000);
            TestSupport.Settle(400);
            var light = Theming.SystemPalette.Get(pick.SystemId, pick.Accent).Light;
            foreach (var (ms, name) in new[] { (80, "f-aus"), (200, "g-aus-punkt"), (380, "h-aus-seite") })
            {
                flight.FreezeOff(light, ms);
                Save(window, prefix + $"-9{name}.png", 60);
            }
            flight.Clear();
            // a message after the session (QA 2): docked left in the footer in place of the location sentence
            vm.Toasts.Show(Localization.Loc.T("artwork.removed"), ToastKind.Success, Localization.Loc.T("artwork.removedDetail"));
            Save(window, prefix + "-9i-meldung.png", 300);
        }
        finally
        {
            vm.Launch.MinimumTransition = before;
        }
        _ = Focused();
    }

    /// <summary>The detail/system subject: the last played game, else one with a cover, else the first.</summary>
    /// <summary>
    /// The mouse's view (R2), with real headless pointer input: Start with the pointer resting on a shelf box (hover ring,
    /// the Winkel plates on the board, the stage presenting it after the dwell); the library with a box hovered and its
    /// right-click list; the sort list; the footer with a hint under the pointer; Konsolen with a rail tick hovered.
    /// </summary>
    private static void MouseStates(MainWindow window, MainViewModel vm, string prefix)
    {
        Avalonia.Point At(Avalonia.Visual v, double fx = 0.5, double fy = 0.5) =>
            Avalonia.VisualExtensions.TranslatePoint(v, new Avalonia.Point(v.Bounds.Width * fx, v.Bounds.Height * fy), window) ?? default;
        vm.Navigator.Navigate(PageKey.Home);
        TestSupport.Settle(400);
        var viewport = window.GetVisualDescendants().OfType<Controls.ShelfViewport>().FirstOrDefault();
        if (viewport != null && viewport.Shelves.Count > 0)
        {
            var shelf = viewport.Shelves[viewport.ActiveIndex];
            var box = shelf.Items().OfType<GameTile>().Skip(2).FirstOrDefault() ?? shelf.Items().FirstOrDefault();
            if (box != null)
            {
                window.MouseMove(At(box));
                Save(window, prefix + "-8-maus-start.png", 900);
            }
        }
        vm.Navigator.Navigate(PageKey.Library);
        TestSupport.Pump(() => vm.CurrentPage is LibraryViewModel { IsLoaded: true }, 8000);
        TestSupport.Settle(400);
        var tile = window.GetVisualDescendants().OfType<GameTile>().Where(t => t.IsEffectivelyVisible && t.Bounds.Width > 0).Skip(1).FirstOrDefault();
        if (tile != null)
        {
            window.MouseMove(At(tile));
            Save(window, prefix + "-8b-maus-bibliothek-hover.png", 600);
            window.MouseDown(At(tile, 0.6, 0.4), Avalonia.Input.MouseButton.Right);
            window.MouseUp(At(tile, 0.6, 0.4), Avalonia.Input.MouseButton.Right);
            Save(window, prefix + "-8c-maus-rechtsklick.png", 500);
            window.Execute(Input.NavAction.Back);
            TestSupport.Settle(200);
        }
        var sort = window.GetVisualDescendants().OfType<Button>().FirstOrDefault(b => b.Name == "SortButton");
        if (sort != null)
        {
            window.MouseMove(At(sort));
            window.MouseDown(At(sort), Avalonia.Input.MouseButton.Left);
            window.MouseUp(At(sort), Avalonia.Input.MouseButton.Left);
            Save(window, prefix + "-8d-maus-sortierung.png", 500);
            window.Execute(Input.NavAction.Back);
            TestSupport.Settle(200);
        }
        var hint = window.GetVisualDescendants().OfType<Button>().LastOrDefault(b => b.DataContext is HintItem { IsClickable: true } && b.IsEffectivelyVisible);
        if (hint != null)
        {
            window.MouseMove(At(hint));
            Save(window, prefix + "-8e-maus-fussleiste.png", 400);
        }
        vm.Navigator.Navigate(PageKey.Systems);
        TestSupport.Pump(() => vm.CurrentPage is SystemsViewModel { IsLoaded: true }, 8000);
        TestSupport.Settle(400);
        var rail = window.GetVisualDescendants().OfType<Controls.PositionRail>().FirstOrDefault();
        var unused = window.GetVisualDescendants().OfType<Controls.Shelf>().FirstOrDefault(s => s.Name == "Unused");
        if (rail != null)
        {
            window.MouseMove(At(rail, 0.7, 0.8));
            Save(window, prefix + "-8f-maus-konsolen-schiene.png", 500);
        }
        if (unused is { IsEffectivelyVisible: true })
        {
            var spine = unused.Items().Skip(3).FirstOrDefault();
            if (spine != null)
            {
                window.MouseMove(At(spine));
                Save(window, prefix + "-8g-maus-konsolen-leer.png", 500);
            }
        }
        window.MouseMove(new Avalonia.Point(2, 2));
    }

    /// <summary>The library filtered to Nintendo Switch (a box focused) and the detail page of a Switch game.</summary>
    private static void SwitchStates(MainWindow window, MainViewModel vm, UiContext ctx, string prefix)
    {
        vm.Navigator.Navigate(PageKey.Library);
        TestSupport.Pump(() => vm.CurrentPage is LibraryViewModel { IsLoaded: true }, 8000);
        if (vm.CurrentPage is not LibraryViewModel lib) return;
        if (lib.IsListView) lib.IsListView = false;
        var chip = lib.Chips.FirstOrDefault(c => c.SystemId == "switch");
        if (chip == null) return;
        lib.SelectChipCommand.Execute(chip);
        TestSupport.Pump(() => lib.SelectedSystemId == "switch" && lib.Items.Count > 0 && lib.Items.All(i => i.SystemId == "switch"), 8000);
        TestSupport.Settle(300);
        var grid = window.GetVisualDescendants().OfType<Controls.CoverGrid>().First(g => g.Name == "Covers");
        grid.FocusIndex(Math.Min(2, lib.Items.Count - 1));
        Save(window, prefix + "-9-switch-bibliothek.png", 1400);
        var game = ctx.Services.Games.Query(new GameQuery { SystemId = "switch" })
            .FirstOrDefault(g => g.Title.Contains("Zeit der Verheerung", StringComparison.Ordinal))
            ?? ctx.Services.Games.Query(new GameQuery { SystemId = "switch" }).FirstOrDefault();
        lib.SelectChipCommand.Execute(lib.Chips.First(c => c.IsAll));
        TestSupport.Pump(() => lib.SelectedSystemId == null, 4000);
        if (game == null) return;
        var item = ctx.Items.Get(game);
        vm.Navigator.Navigate(PageKey.GameDetail, item);
        TestSupport.Pump(() => vm.CurrentPage is GameDetailViewModel { Game: not null } d && d.Game.Id == item.Id, 8000);
        Save(window, prefix + "-9b-switch-detail.png", 1400);
    }

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
