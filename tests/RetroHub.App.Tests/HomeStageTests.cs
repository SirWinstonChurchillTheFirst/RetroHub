using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.VisualTree;
using RetroHub.App.Controls;
using RetroHub.App.Input;
using RetroHub.App.Localization;
using RetroHub.App.ViewModels;
using RetroHub.App.Views;
using RetroHub.App.Views.Pages;
using RetroHub.Core.Catalog;
using RetroHub.Core.Config;
using Xunit;
using SystemSpine = RetroHub.App.Controls.SystemSpine;

namespace RetroHub.App.Tests;

/// <summary>Start (DESIGN 7.1, M2): the TV's pixel placement, the stage sentences, the shelves' navigation and the stage following the focus.</summary>
public class HomeStageTests
{
    [Theory]
    // SNES 256×224 in the 534×401 tube at 1080p: no whole factor fills 85 % → a TV picture fills the 4:3 frame
    [InlineData(534, 401, 256, 224, true, 0, 0, 534, 401, false)]
    // the same as a handheld picture (no TV system): contain, letterboxed
    [InlineData(534, 401, 256, 224, false, 38, 0, 458, 401, false)]
    // GB 160×144 at 4K (1068×802): k = 5 fills 90 % of the height → nearest neighbour, centred
    [InlineData(1068, 802, 160, 144, false, 134, 41, 800, 720, true)]
    // a 16:9 snap on the 4:3 set: letterboxed
    [InlineData(534, 401, 512, 288, true, 0, 50, 534, 300, false)]
    public void Pictures_are_placed_in_device_pixels(double fw, double fh, int iw, int ih, bool tv, double x, double y, double w, double h, bool integer)
    {
        var (dest, isInteger) = PixelScreen.Place(new Size(fw, fh), new PixelSize(iw, ih), tv);
        Assert.Equal(new Rect(x, y, w, h), dest);
        Assert.Equal(integer, isInteger);
    }

    [Fact]
    public void The_set_has_a_4_to_3_tube_a_chin_and_feet()
    {
        var size = new Size(560, PixelScreen.SetHeightFor(560));
        var pic = PixelScreen.PictureRect(size);
        Assert.InRange(pic.Width / pic.Height, 1.32, 1.345); // 4:3 on whole pixels
        Assert.True(size.Height - pic.Bottom >= PixelScreen.ChinFor(560) + PixelScreen.FeetFor(560) - 0.5);
        Assert.InRange(PixelScreen.SetHeightFor(400), 300, 340); // ≈1100 px window: the set fits the 392 px stage
    }

    [Fact]
    public void Stage_sentences_are_sentences()
    {
        Loc.Load("de");
        var gba = new SystemDef { Id = "gba", Name = "Game Boy Advance", Manufacturer = "Nintendo", Year = 2001, Category = SystemCategory.Handheld };
        Assert.Equal("Handheld von Nintendo, 2001. 7 Spiele, zuletzt Pokémon Smaragd-Edition.",
            HomeViewModel.BuildSystemMeta(gba, 7, "Pokémon Smaragd-Edition"));
        Assert.Equal("Handheld von Nintendo, 2001. 1 Spiel.", HomeViewModel.BuildSystemMeta(gba, 1, null));
        var never = new Core.Library.Game { SystemId = "gba", Path = "x.gba", Title = "X", AddedUtc = new DateTime(2026, 9, 25, 12, 0, 0, DateTimeKind.Utc) };
        Assert.StartsWith("Game Boy Advance. Noch nie gespielt. Seit ", HomeViewModel.BuildStageMeta("Game Boy Advance", never));
        Assert.True(HomeViewModel.IsTvSystem(new SystemDef { Id = "snes", Name = "SNES", Category = SystemCategory.Console }));
        Assert.False(HomeViewModel.IsTvSystem(gba));
    }

    private static (MainWindow Window, MainViewModel Vm) Open()
    {
        var ctx = TestSupport.CreateContext(s => s.Motion = MotionLevel.Off);
        var vm = new MainViewModel(ctx);
        var window = new MainWindow(vm) { Width = 1920, Height = 1080 };
        window.Show();
        TestSupport.Pump(() => vm.CurrentPage is HomeViewModel { IsLoaded: true });
        TestSupport.Settle(150);
        return (window, vm);
    }

    private static Control? Focused(Window w) => w.FocusManager?.GetFocusedElement() as Control;

    [Fact]
    public Task Shelves_remember_their_position_and_up_from_the_first_returns_to_the_stage() => UiTest.Run(() =>
    {
        var (window, vm) = Open();
        var page = Assert.IsType<HomePage>(window.PageHost.Current);
        Assert.Equal("PlayButton", Focused(window)?.Name);

        Assert.True(window.Execute(NavAction.Down));
        TestSupport.Settle(40);
        var first = Assert.IsType<GameTile>(Focused(window));
        Assert.True(window.Execute(NavAction.Right));
        Assert.True(window.Execute(NavAction.Right));
        TestSupport.Settle(40);
        var third = Assert.IsType<GameTile>(Focused(window));
        Assert.NotSame(first, third);
        var shelf = third.FindAncestorOfType<Shelf>()!;
        Assert.Equal(2, shelf.RememberedIndex);

        // down to the next shelf, which becomes the active one at the anchor
        Assert.True(window.Execute(NavAction.Down));
        TestSupport.Settle(40);
        var next = Focused(window)!.FindAncestorOfType<Shelf>();
        Assert.NotNull(next);
        Assert.NotSame(shelf, next);
        var viewport = page.FindControl<ShelfViewport>("Shelves")!;
        Assert.Equal(1, viewport.ActiveIndex);

        // back up: the third box of the first shelf again (remembered), then the stage's primary action
        Assert.True(window.Execute(NavAction.Up));
        TestSupport.Settle(40);
        Assert.Same(third, Focused(window));
        Assert.True(window.Execute(NavAction.Up));
        TestSupport.Settle(40);
        Assert.Equal("PlayButton", Focused(window)?.Name);
        window.Close();
    });

    [Fact]
    public Task The_stage_follows_a_focused_box_and_returns_to_the_game_to_continue() => UiTest.Run(() =>
    {
        var (window, vm) = Open();
        var home = Assert.IsType<HomeViewModel>(vm.CurrentPage);
        Assert.Equal(StageKind.Continue, home.Stage!.Kind);
        Assert.Same(home.Continue, home.Stage.Game);
        Assert.Equal(Loc.T("detail.play"), home.Hints[0].Label);

        window.Execute(NavAction.Down);
        window.Execute(NavAction.Right);
        TestSupport.Settle(40);
        var tile = Assert.IsType<GameTile>(Focused(window));
        var item = Assert.IsType<GameItemViewModel>(tile.DataContext);
        // the footer follows the focus at once, the stage after the dwell
        Assert.Equal(Loc.T("home.location.shelf", Loc.T("home.shelf.recent"), 2, home.RecentlyPlayed.Count), home.Location);
        Assert.True(TestSupport.Pump(() => home.Stage is { Kind: StageKind.Game } s && ReferenceEquals(s.Game, item), 3000), "the stage did not follow the focus");
        Assert.Equal(Loc.T("home.shelf.recent"), home.Stage!.Label);
        Assert.Equal(Loc.T("hint.open"), home.Hints[0].Label);
        Assert.Contains(home.Hints, h => h.Action == NavAction.Favorite);

        // up to "Spielen": after the dwell the stage shows the game to continue again (Start's main action, M2 QA)
        window.Execute(NavAction.Up);
        TestSupport.Settle(40);
        Assert.Equal("PlayButton", Focused(window)?.Name);
        Assert.Equal(Loc.T("home.location"), home.Location);
        Assert.True(TestSupport.Pump(() => home.Stage is { Kind: StageKind.Continue } s && ReferenceEquals(s.Game, home.Continue), 3000),
            "the stage did not return to the game to continue");
        Assert.Equal(Loc.T("detail.play"), home.Stage!.PrimaryLabel);
        Assert.Equal(Loc.T("detail.play"), home.Hints[0].Label);
        window.Close();
    });

    [Theory]
    [InlineData("The Legend of Zelda - A Link to the Past (Germany)", "the legend of zelda - a link to the past")]
    [InlineData("The Legend of Zelda - A Link to the Past (Rev 1) [!]", "the legend of zelda - a link to the past")]
    [InlineData("  Tetris  (World) ", "tetris")]
    public void Start_shelves_fold_region_and_version_variants(string title, string normal)
    {
        Assert.Equal(normal, HomeViewModel.NormalizeTitle(title));
        Core.Library.Game G(string t, string sys) => new() { SystemId = sys, Path = t + ".x", Title = t };
        var list = HomeViewModel.Distinct([G(title, "snes"), G(normal, "snes"), G(title, "gba"), G("Other", "snes")], 10);
        Assert.Equal(3, list.Count); // same system + same normalised title once; another system stays
        Assert.Equal(title, list[0].Title); // the first (newest by the shelf's order) wins
    }

    [Fact]
    public Task A_system_spine_opens_its_front_and_puts_the_system_on_the_stage() => UiTest.Run(() =>
    {
        var (window, vm) = Open();
        var home = Assert.IsType<HomeViewModel>(vm.CurrentPage);
        for (var i = 0; i < 6 && Focused(window) is not SystemSpine; i++)
        {
            window.Execute(NavAction.Down);
            TestSupport.Settle(40);
        }
        var spine = Assert.IsType<SystemSpine>(Focused(window));
        TestSupport.Settle(100);
        Assert.True(spine.IsOpen);
        Assert.Equal(spine.OpenWidth, spine.Bounds.Width, 0); // motion off: the front stands at once, 16 px free on each side
        Assert.Equal(spine.FrontWidth + 2 * SystemSpine.FrontGap, spine.OpenWidth);
        Assert.Equal(1, spine.ZIndex); // its ring stands above the neighbouring spines
        var system = Assert.IsType<SystemItemViewModel>(spine.DataContext);
        Assert.True(TestSupport.Pump(() => home.Stage is { Kind: StageKind.System } s && ReferenceEquals(s.System, system), 3000));
        Assert.Equal(system.Name, home.Stage!.Title);
        Assert.Equal(Loc.T("home.open"), home.Stage.PrimaryLabel);

        // the next spine: the previous one turns back to its spine
        window.Execute(NavAction.Right);
        TestSupport.Settle(100);
        Assert.False(spine.IsOpen);
        Assert.Equal(spine.SpineWidth, spine.Bounds.Width, 0);
        window.Close();
    });
}
