using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media;
using Avalonia.VisualTree;
using RetroHub.App.Controls;
using RetroHub.App.Input;
using RetroHub.App.Localization;
using RetroHub.App.Theming;
using RetroHub.App.ViewModels;
using RetroHub.App.Views;
using RetroHub.Core.Catalog;
using RetroHub.Core.Config;
using RetroHub.Core.Library;
using Xunit;

namespace RetroHub.App.Tests;

/// <summary>
/// Library "Das Regal" (DESIGN 7.2, M3): ticker and shoulders, the system compartment (banner, wall, light, slot
/// format from stored cover sizes), the shelf mirror, namesakes, stickers, the list view, the printed spine.
/// </summary>
public class DesignM3Tests
{
    private static Game G(string system, string title, Action<Game>? setup = null)
    {
        var g = new Game { SystemId = system, Path = $"games/{system}/{title}.rom", Title = title, AddedUtc = DateTime.UtcNow.AddDays(-60) };
        setup?.Invoke(g);
        return g;
    }

    // ===================== pure rules =====================

    [Fact]
    public void Slot_format_is_the_median_of_the_stored_cover_sizes()
    {
        // fewer than 8 sizes: the system's start value (SNES 1.00)
        var few = Enumerable.Range(0, 7).Select(i => G("snes", "A" + i, g => { g.CoverPath = "c.png"; g.CoverWidth = 512; g.CoverHeight = 360; })).ToList();
        Assert.Equal(SystemPalette.BoxAspect("snes"), LibraryViewModel.SlotAspectFor("snes", few));
        // 8 landscape PAL boxes and one portrait outlier: the median, not the mean
        var many = few.Append(G("snes", "B", g => { g.CoverPath = "c.png"; g.CoverWidth = 512; g.CoverHeight = 360; }))
            .Append(G("snes", "C", g => { g.CoverPath = "c.png"; g.CoverWidth = 300; g.CoverHeight = 900; })).ToList();
        Assert.Equal(0.70, LibraryViewModel.SlotAspectFor("snes", many));
        // a removed cover ("") or an unknown size does not count
        var removed = many.Select(g => { g.CoverPath = ""; return g; }).ToList();
        Assert.Equal(SystemPalette.BoxAspect("snes"), LibraryViewModel.SlotAspectFor("snes", removed));
    }

    [Fact]
    public void Mixed_slot_format_is_the_median_box_format_of_the_whole_collection()
    {
        // M3 QA: square SNES/GBA boxes in 1 : 1.25 slots left 55 px of dead headroom; covers count by their stored
        // size, games without one by their system's start value, clamped to 1.00–1.40
        var squares = Enumerable.Range(0, 5).Select(i => G("snes", "S" + i)).Concat([G("gba", "A"), G("gba", "B")]).ToList();
        Assert.Equal(1.0, LibraryViewModel.MixedSlotAspect(squares.Append(G("ps2", "P"))));
        var cases = Enumerable.Range(0, 5).Select(i => G("psp", "P" + i)).ToList(); // 1.72 → clamped
        Assert.Equal(1.4, LibraryViewModel.MixedSlotAspect(cases));
        var landscape = Enumerable.Range(0, 5).Select(i => G("snes", "L" + i, g => { g.CoverPath = "c.png"; g.CoverWidth = 512; g.CoverHeight = 360; })).ToList();
        Assert.Equal(1.0, LibraryViewModel.MixedSlotAspect(landscape)); // never wider than square
        Assert.Equal(LibraryViewModel.MixedAspect, LibraryViewModel.MixedSlotAspect([]));
    }

    [Fact]
    public void Namesake_titles_keep_their_distinguishing_tail()
    {
        // M3 QA: four BS chapters all read "… Ancient Stone Tablet…"
        Assert.Equal("BS The Legend of Zelda – … Kapitel 1", TitleTrimming.HeadAndTail("BS The Legend of Zelda – Ancient Stone Tablets – Kapitel 1"));
        Assert.Null(TitleTrimming.HeadAndTail("Hyrule Warriors – Zeit der Verheerung")); // two parts: nothing to drop
        Assert.Null(TitleTrimming.HeadAndTail("Freshly-Picked Tingle's Rosy Rupeeland"));
        Assert.Equal("Kapitel 1", CoverArt.SubtitleTail("Ancient Stone Tablets – Kapitel 1"));
        Assert.Null(CoverArt.SubtitleTail("A Link to the Past"));
        Assert.Equal("Zelda – Ancient Stone Tablets – Kapitel 1", TitleTrimming.Glue("Zelda – Ancient Stone Tablets – Kapitel 1"));
        Assert.True(TitleTrimming.PrefixLength("BS The Legend of Zelda – Ancient Stone Tablets – Kapitel 1") == "BS The Legend of Zelda – ".Length);
    }

    [Fact]
    public void Banner_sentence_is_a_sentence()
    {
        Loc.Load("de");
        var gba = new SystemDef { Id = "gba", Name = "Game Boy Advance", Manufacturer = "Nintendo", Year = 2001, Category = SystemCategory.Handheld };
        Assert.Equal("Handheld von Nintendo, 2001. 7 Spiele, 46 Stunden gespielt.", LibraryViewModel.BuildBannerSentence(gba, 7, 46 * 3600 + 120));
        Assert.Equal("Handheld von Nintendo, 2001. 1 Spiel, noch nicht gespielt.", LibraryViewModel.BuildBannerSentence(gba, 1, 0));
        Assert.Equal("Handheld von Nintendo, 2001. 2 Spiele, 12 Minuten gespielt.", LibraryViewModel.BuildBannerSentence(gba, 2, 12 * 60));
    }

    [Fact]
    public void Mirror_spines_stand_as_tall_as_their_box_format()
    {
        Assert.True(ShelfMirror.SpineShare("snes") < ShelfMirror.SpineShare("ps2"));
        Assert.True(ShelfMirror.SpineShare("ps2") < ShelfMirror.SpineShare("switch"));
        Assert.Equal(1.0, ShelfMirror.SpineShare("switch"));
        Assert.InRange(ShelfMirror.SpineShare("3ds"), 0.5, 0.6);
    }

    [Fact]
    public Task Mirror_rasterises_one_spine_per_game_and_the_most_frequent_colour_per_column() => UiTest.Run(() =>
    {
        // 4 games over 40 px: 10 px spines with a 1 px gap; over 2 px: two games per column, the majority wins
        var red = new ShelfMirror.Spine("snes", Colors.Red);
        var blue = new ShelfMirror.Spine("gba", Colors.Blue);
        using var wide = ShelfMirror.Rasterise([red, blue, red, blue], 40, 12, SystemPalette.Room);
        Assert.Equal(new PixelSize(40, 12), wide.PixelSize);
        using var narrow = ShelfMirror.Rasterise([red, red, blue, red, blue, blue], 3, 12, SystemPalette.Room);
        Assert.Equal(new PixelSize(3, 12), narrow.PixelSize);
    });

    [Fact]
    public Task Namesakes_carry_their_region_and_others_carry_nothing() => UiTest.Run(() =>
    {
        Loc.Load("de");
        var ctx = TestSupport.CreateContext(s => s.Motion = MotionLevel.Off);
        var items = ctx.Items.GetAll(
        [
            G("snes", "BS Zelda - Kapitel 1", g => { g.Id = 9001; g.Region = "Englisch"; }),
            G("snes", "BS Zelda - Kapitel 1", g => { g.Id = 9002; g.Region = "Japan"; }),
            G("nes", "Zelda", g => { g.Id = 9003; }),
            G("nes", "Zelda", g => { g.Id = 9004; g.Region = "Deutsch"; }),
            G("gb", "240p Test Suite", g => g.Id = 9005),
            G("gba", "240p Test Suite", g => g.Id = 9006), // same title, another system: the code tells them apart
        ]);
        LibraryViewModel.MarkVariants(items);
        Assert.Equal(["Englisch", "Japan", "Original", "Deutsch", "", ""], items.Select(i => i.Variant));
    });

    [Fact]
    public Task New_stickers_mark_a_few_new_games_but_never_a_freshly_imported_shelf() => UiTest.Run(() =>
    {
        var ctx = TestSupport.CreateContext(s => s.Motion = MotionLevel.Off);
        var now = DateTime.UtcNow;
        Game Added(long id, double days, bool played = false) => G("gba", "Game " + id, g =>
        {
            g.Id = id;
            g.AddedUtc = now.AddDays(-days);
            if (played) { g.LastPlayedUtc = now; g.PlayCount = 1; }
        });
        var shelf = ctx.Items.GetAll(Enumerable.Range(0, 20).Select(i => Added(9100 + i, 40)).Concat(
            [Added(9200, 1), Added(9201, 2, played: true), Added(9202, 8)]).ToList());
        LibraryViewModel.MarkNew(shelf, now);
        Assert.Equal([9200L], shelf.Where(i => i.IsNew).Select(i => i.Id));

        // unpacked on the first focus: not new any more in this session
        shelf.First(i => i.Id == 9200).MarkUnpacked();
        LibraryViewModel.MarkNew(shelf, now);
        Assert.DoesNotContain(shelf, i => i.IsNew);

        // a library imported this week: no stickers at all (a wall of stickers marks nothing)
        var fresh = ctx.Items.GetAll(Enumerable.Range(0, 12).Select(i => Added(9300 + i, 1)).ToList());
        LibraryViewModel.MarkNew(fresh, now);
        Assert.DoesNotContain(fresh, i => i.IsNew);
    });

    [Fact]
    public Task Captions_print_an_en_dash() => UiTest.Run(() =>
    {
        var ctx = TestSupport.CreateContext(s => s.Motion = MotionLevel.Off);
        var item = ctx.Items.Get(G("snes", "The Legend of Zelda - A Link to the Past", g => g.Id = 9400));
        Assert.Equal("The Legend of Zelda – A Link to the Past", item.Caption);
        Assert.Equal("The Legend of Zelda - A Link to the Past", item.Title); // the data keeps its hyphen
    });

    [Theory]
    [InlineData("Hyrule Warriors Definitive Edition", 87)]
    [InlineData("Professor Layton und das geheimnisvolle Dorf", 87)]
    public Task Narrow_boxes_set_long_words_condensed_instead_of_shrinking(string title, double width) => UiTest.Run(() =>
    {
        // the M1 leftover: in a narrow Switch/Wii case "Definitive" did not fit in Archivo Expanded and shrank to 13 px
        var expanded = PlaceholderTitle.Fit(title, width, 1000, 18, PlaceholderTitle.MaxLines);
        Assert.True(expanded.Size < 18);
        var condensed = PlaceholderTitle.Fit(title, width, 1000, 18, PlaceholderTitle.MaxLines, condensed: true);
        Assert.True(condensed.Size >= PlaceholderTitle.CondensedMinSize, $"{condensed.Size}");
        Assert.True(condensed.Size > expanded.Size);
    });

    // ===================== the page =====================

    private static (MainWindow Window, MainViewModel Vm, LibraryViewModel Library) OpenLibrary(int w = 1920, int h = 1080)
    {
        var ctx = TestSupport.CreateContext(s => s.Motion = MotionLevel.Off);
        var vm = new MainViewModel(ctx);
        var window = new MainWindow(vm) { Width = w, Height = h };
        window.Show();
        TestSupport.Pump(() => vm.CurrentPage is HomeViewModel { IsLoaded: true });
        vm.Navigator.Navigate(PageKey.Library);
        TestSupport.Pump(() => vm.CurrentPage is LibraryViewModel { IsLoaded: true } && window.FocusManager?.GetFocusedElement() is GameTile);
        TestSupport.Settle(150);
        return (window, vm, (LibraryViewModel)vm.CurrentPage!);
    }

    /// <summary>Closes the window even when an assertion fails (an open headless window keeps the focus from later tests).</summary>
    private sealed class Closer(Window window) : IDisposable
    {
        public void Dispose() => window.Close();
    }

    private static T Find<T>(Window window, string name) where T : Control =>
        window.GetVisualDescendants().OfType<T>().First(c => c.Name == name);

    [Fact]
    public Task Shoulders_open_a_system_compartment_with_banner_wall_and_light() => UiTest.Run(() =>
    {
        var (window, vm, library) = OpenLibrary();
        using var closer = new Closer(window);
        Assert.Equal(BackdropKind.Room, library.Backdrop.Kind);
        Assert.True(library.Backdrop.AccentBoard); // "Alle": the boards carry the light colour at 22 %
        Assert.Null(library.ContextLight);

        Assert.True(window.Execute(NavAction.PageRight));
        TestSupport.Pump(() => library.SelectedSystemId != null && library.Items.All(i => i.SystemId == library.SelectedSystemId));
        TestSupport.Settle(150);
        var look = library.SystemLook!;
        Assert.Equal(BackdropKind.Wall, library.Backdrop.Kind);
        Assert.Equal(look.Wall, library.Ground);
        Assert.Equal(look.Light, library.ContextLight);
        var covers = Find<CoverGrid>(window, "Covers");
        Assert.Equal(look.Light, covers.FocusVisuals.Light); // ring and cone in the system's light
        var banner = Find<Panel>(window, "Banner");
        Assert.True(banner.IsEffectivelyVisible);
        // the banner runs edge to edge over the grid's padding and starts at the grid's top
        var origin = banner.TranslatePoint(new Point(0, 0), covers)!.Value;
        Assert.Equal(0, origin.X, 0.5);
        Assert.Equal(0, origin.Y, 0.5);
        Assert.Equal(covers.Bounds.Width, banner.Bounds.Width, 0.5);
        Assert.Equal(PageKey.Library, vm.Navigator.Current);

        Assert.True(window.Execute(NavAction.PageLeft));
        TestSupport.Pump(() => library.SelectedSystemId == null);
        Assert.Equal(BackdropKind.Room, library.Backdrop.Kind);
        Assert.False(Find<Panel>(window, "Banner").IsEffectivelyVisible);
    });

    [Fact]
    public Task Mirror_follows_list_view_and_focus_without_rasterising_again() => UiTest.Run(() =>
    {
        var (window, _, library) = OpenLibrary();
        using var closer = new Closer(window);
        var mirror = window.GetVisualDescendants().OfType<ShelfMirror>().First();
        window.CaptureRenderedFrame();
        Assert.Equal(library.Items.Count, mirror.Count);
        Assert.Equal(0, mirror.FirstVisible);
        Assert.True(mirror.LastVisible >= 15); // ≈ 2.4 rows of 8 in view
        var rasters = mirror.RasterCount;
        Assert.True(rasters >= 1);
        for (var i = 0; i < 6; i++)
        {
            window.Execute(NavAction.Down);
            TestSupport.Settle(30);
            window.CaptureRenderedFrame();
        }
        Assert.Equal(rasters, mirror.RasterCount);      // a frame draws the cached spines, the bracket and the mark
        Assert.Equal(library.FocusIndex, mirror.FocusIndex);
        Assert.True(mirror.FirstVisible > 0);           // the bracket moved with the scrolled grid
        Assert.InRange(mirror.FocusIndex, mirror.FirstVisible, mirror.LastVisible);
    });

    [Fact]
    public Task Up_from_the_first_row_reaches_the_active_ticker_entry_and_down_returns() => UiTest.Run(() =>
    {
        var (window, _, library) = OpenLibrary();
        using var closer = new Closer(window);
        window.Execute(NavAction.Right);
        window.Execute(NavAction.Right);
        Assert.True(window.Execute(NavAction.Up));
        var focused = window.FocusManager?.GetFocusedElement() as Control;
        Assert.Equal(library.Chips.First(c => c.IsSelected), focused?.DataContext);
        Assert.True(window.Execute(NavAction.Down));
        TestSupport.Settle(50);
        var back = (window.FocusManager?.GetFocusedElement() as Control)?.DataContext;
        Assert.Same(library.Items[2], back); // the remembered box, not the first one
    });

    [Fact]
    public Task Focused_list_row_keeps_its_title_light_on_the_row_surface() => UiTest.Run(() =>
    {
        var (window, _, library) = OpenLibrary();
        using var closer = new Closer(window);
        library.IsListView = true;
        TestSupport.Settle(200);
        var rows = Find<CoverGrid>(window, "Rows");
        Assert.True(rows.FocusIndex(2));
        TestSupport.Settle(150);
        var tile = (GameTile)rows.TileForIndex(2)!;
        var title = tile.GetVisualDescendants().OfType<TextBlock>().First(t => t.Name == "PART_Title");
        // R3 QA 3 (one focus signal, DESIGN 8.2): no paper chip behind the title any more – the title stays Text 1 on the
        // lit row, the chip stays clear
        Assert.Equal(Color.Parse("#FFFFFF"), ((ISolidColorBrush)title.Foreground!).Color);
        var chip = tile.GetVisualDescendants().OfType<Border>().First(b => b.Name == "PART_Chip");
        Assert.True(chip.Background is not ISolidColorBrush { Color.A: > 0 }, "no paper chip");
        Assert.Equal(library.Items[2], library.PreviewItem); // the preview shows the focused game
        library.IsListView = false;
    });

    [Fact]
    public Task Scrolling_reuses_rows_and_tiles_instead_of_building_new_ones() => UiTest.Run(() =>
    {
        // M3 perf: the items control clears a recycled container, which threw away the row presenter and its 8 tiles –
        // every row scrolled into view built new tiles (2096 in 118 moves of the 1000-game demo)
        var (window, _, _) = OpenLibrary();
        using var closer = new Closer(window);
        var covers = Find<CoverGrid>(window, "Covers");
        void Pass()
        {
            for (var i = 0; i < 4; i++) { covers.Move(NavAction.PageRight); TestSupport.Settle(20); }
            for (var i = 0; i < 4; i++) { covers.Move(NavAction.PageLeft); TestSupport.Settle(20); }
        }
        Pass(); // the first pass may realise rows for the first time
        var presenters = covers.PresentersCreated;
        var tiles = GameTile.Created;
        Pass();
        Pass();
        var rebuilds = covers.RebuildCount;
        Assert.True(covers.PresentersCreated - presenters <= 1, $"{covers.PresentersCreated - presenters} row presenters built in two warm passes (rebuilds {rebuilds})");
        Assert.True(GameTile.Created - tiles <= covers.Columns, $"{GameTile.Created - tiles} tiles built in two warm passes");
    });

    [Fact]
    public Task Grid_reports_the_range_in_view_and_scrolls_to_an_index() => UiTest.Run(() =>
    {
        var (window, _, library) = OpenLibrary();
        using var closer = new Closer(window);
        var covers = Find<CoverGrid>(window, "Covers");
        var (first, last) = covers.VisibleRange();
        Assert.Equal(0, first);
        Assert.InRange(last, covers.Columns * 2 - 1, covers.Columns * 3 - 1); // ≈ 2.4 rows: a row counts once half of it shows
        covers.ScrollToIndex(library.Items.Count - 1);
        TestSupport.Settle(50);
        var (f2, l2) = covers.VisibleRange();
        Assert.True(f2 > first);
        Assert.Equal(library.Items.Count - 1, l2);
    });
}
