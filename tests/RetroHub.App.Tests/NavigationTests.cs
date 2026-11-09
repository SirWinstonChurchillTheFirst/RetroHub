using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using RetroHub.App.Controls;
using RetroHub.App.Input;
using RetroHub.App.ViewModels;
using RetroHub.App.Views;
using RetroHub.App.Views.Pages;
using RetroHub.Core.Config;
using Xunit;

namespace RetroHub.App.Tests;

/// <summary>Shell navigation: keep-alive pages, back stack, sidebar sections, NavAction routing, launch flow.</summary>
public class NavigationTests
{
    private static (MainWindow Window, MainViewModel Vm) Open(Action<AppSettings>? configure = null)
    {
        var ctx = TestSupport.CreateContext(s =>
        {
            s.Motion = MotionLevel.Off; // deterministic: no transitions
            configure?.Invoke(s);
        });
        var vm = new MainViewModel(ctx);
        var window = new MainWindow(vm) { Width = 1400, Height = 850 };
        window.Show();
        TestSupport.Pump(() => vm.CurrentPage is HomeViewModel { IsLoaded: true });
        TestSupport.Settle(100);
        return (window, vm);
    }

    private static Control? Focused(Window w) => w.FocusManager?.GetFocusedElement() as Control;

    [Fact]
    public Task Starts_on_home_with_focus_on_play() => UiTest.Run(() =>
    {
        var (window, vm) = Open();
        Assert.Equal(PageKey.Home, vm.Navigator.Current);
        Assert.IsType<HomePage>(window.PageHost.Current);
        Assert.Equal("PlayButton", Focused(window)?.Name);
        Assert.True(vm.NavItems.Single(n => n.Key == PageKey.Home).IsSelected);
        window.Close();
    });

    [Fact]
    public Task Arrow_keys_move_visible_focus_from_the_hero_into_the_shelves() => UiTest.Run(() =>
    {
        var (window, vm) = Open();
        Assert.Equal("PlayButton", Focused(window)?.Name);
        window.KeyPress(Key.Down, RawInputModifiers.None, PhysicalKey.ArrowDown, null);
        TestSupport.Settle(60);
        var tile = Assert.IsType<GameTile>(Focused(window));
        Assert.Contains(":focus-visible", tile.Classes); // controller/keyboard focus shows the ring
        window.KeyPress(Key.Right, RawInputModifiers.None, PhysicalKey.ArrowRight, null);
        TestSupport.Settle(60);
        Assert.NotSame(tile, Focused(window));
        Assert.IsType<GameTile>(Focused(window));
        // Up from the top of the page reaches the header channels (DESIGN 7.0: the "left edge → sidebar" jump is gone)
        for (var i = 0; i < 6; i++) window.KeyPress(Key.Up, RawInputModifiers.None, PhysicalKey.ArrowUp, null);
        TestSupport.Settle(60);
        Assert.Contains("navitem", Focused(window)?.Classes ?? []);
        // Down goes back into the page
        window.KeyPress(Key.Down, RawInputModifiers.None, PhysicalKey.ArrowDown, null);
        TestSupport.Settle(60);
        Assert.DoesNotContain("navitem", Focused(window)?.Classes ?? []);
        window.Close();
    });

    [Fact]
    public Task Pages_are_kept_alive_and_back_stack_works() => UiTest.Run(() =>
    {
        var (window, vm) = Open();
        var home = window.PageHost.Current;

        vm.Navigator.Navigate(PageKey.Library);
        TestSupport.Pump(() => vm.CurrentPage is LibraryViewModel { IsLoaded: true });
        var library = window.PageHost.Current;
        Assert.IsType<LibraryPage>(library);
        Assert.False(home!.IsVisible);
        Assert.False(vm.Navigator.CanGoBack); // top-level navigation resets the stack

        var game = vm.Ctx.Items.Get(vm.Ctx.Services.Games.Query(new() { Text = "Chrono" }).Single());
        vm.Navigator.Navigate(PageKey.GameDetail, game);
        TestSupport.Pump(() => vm.CurrentPage is GameDetailViewModel { Game.Title: "Chrono Trigger", Emulators.Count: > 0 });
        Assert.IsType<GameDetailPage>(window.PageHost.Current);
        Assert.True(vm.Navigator.CanGoBack);
        Assert.True(vm.NavItems.Single(n => n.Key == PageKey.Library).IsSelected); // section of the drill-in page

        // Back (Escape) returns to the very same library page instance
        window.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null);
        TestSupport.Settle(100);
        Assert.Equal(PageKey.Library, vm.Navigator.Current);
        Assert.Same(library, window.PageHost.Current);
        Assert.True(library!.IsVisible);

        vm.Navigator.Navigate(PageKey.Home);
        TestSupport.Settle(50);
        Assert.Same(home, window.PageHost.Current);
        Assert.Equal(2, window.PageHost.Pages.Count(p => p.Key is PageKey.Home or PageKey.Library));
        window.Close();
    });

    [Fact]
    public Task Library_grid_gets_focus_and_accept_opens_the_game() => UiTest.Run(() =>
    {
        var (window, vm) = Open();
        vm.Navigator.Navigate(PageKey.Library);
        TestSupport.Pump(() => Focused(window) is GameTile);
        var tile = Assert.IsType<GameTile>(Focused(window));
        var item = Assert.IsType<GameItemViewModel>(tile.DataContext);
        Assert.Equal("Advance Wars", item.Title); // sorted by title, first tile

        Assert.True(window.Execute(NavAction.Right));
        TestSupport.Settle(30);
        Assert.Equal("Banjo-Kazooie", (Focused(window)?.DataContext as GameItemViewModel)?.Title);

        Assert.True(window.Execute(NavAction.Accept));
        TestSupport.Pump(() => vm.CurrentPage is GameDetailViewModel { Game.Title: "Banjo-Kazooie" });
        Assert.Equal(PageKey.GameDetail, vm.Navigator.Current);
        window.Close();
    });

    [Fact]
    public Task Favorite_action_toggles_the_focused_game() => UiTest.Run(() =>
    {
        var (window, vm) = Open();
        vm.Navigator.Navigate(PageKey.Library);
        TestSupport.Pump(() => Focused(window) is GameTile);
        var item = (GameItemViewModel)Focused(window)!.DataContext!;
        var before = item.IsFavorite;
        Assert.True(window.Execute(NavAction.Favorite));
        TestSupport.Pump(() => vm.Ctx.Services.Games.Get(item.Id)!.Favorite != before);
        Assert.Equal(!before, item.IsFavorite);
        Assert.Equal(!before, vm.Ctx.Services.Games.Get(item.Id)!.Favorite);
        window.Close();
    });

    [Fact]
    public Task Shoulder_buttons_cycle_channels_and_search_opens_library() => UiTest.Run(() =>
    {
        var (window, vm) = Open();
        Assert.True(window.Execute(NavAction.PageRight));
        TestSupport.Settle(50);
        Assert.Equal(PageKey.Library, vm.Navigator.Current);
        Assert.True(window.Execute(NavAction.PageLeft));
        Assert.True(window.Execute(NavAction.PageLeft));
        TestSupport.Settle(50);
        Assert.Equal(PageKey.Systems, vm.Navigator.Current); // channels 1–3 wrap around

        // keys 1/2/3 switch the channel directly and show the channel display
        window.KeyPress(Key.D2, RawInputModifiers.None, PhysicalKey.Digit2, "2");
        TestSupport.Settle(50);
        Assert.Equal(PageKey.Library, vm.Navigator.Current);
        Assert.True(window.FindControl<OsdLayer>("Osd")!.IsShowing);

        Assert.True(window.Execute(NavAction.Search));
        TestSupport.Pump(() => Focused(window) is TextBox);
        Assert.Equal(PageKey.Library, vm.Navigator.Current);
        Assert.Equal("Search", Focused(window)?.Name);
        window.Close();
    });

    [Fact]
    public Task Blocked_launch_shows_the_problems_dialog() => UiTest.Run(() =>
    {
        var (window, vm) = Open();
        // GameCube: Dolphin is not installed in the demo → blocking problem, dialog instead of a start
        var cube = vm.Ctx.Items.Get(vm.Ctx.Services.Games.Query(new() { Text = "Pikmin" }).Single());
        _ = vm.Ctx.Launch.LaunchAsync(cube);
        TestSupport.Pump(() => vm.Dialogs.Current != null);
        var dialog = vm.Dialogs.Current!;
        Assert.Contains(dialog.Details, d => d.IsBlocking && d.Text.Contains("Dolphin"));
        Assert.Equal(LaunchPhase.Idle, vm.Launch.Phase);
        // Back closes the dialog
        Assert.True(window.Execute(NavAction.Back));
        TestSupport.Pump(() => vm.Dialogs.Current == null);
        window.Close();
    });

    [Fact]
    public Task Launch_runs_and_returns_to_the_game_detail() => UiTest.Run(() =>
    {
        var (window, vm) = Open();
        ((Services.Demo.DemoSessionService)vm.Ctx.Services.Sessions).AutoEndAfter = TimeSpan.FromMilliseconds(300);
        vm.Ctx.Launch.MinimumTransition = TimeSpan.FromMilliseconds(100);
        var game = vm.Ctx.Items.Get(vm.Ctx.Services.Games.Query(new() { Text = "Golden Sun" }).Single());
        var playedBefore = game.Game.PlayCount;

        _ = vm.Ctx.Launch.LaunchAsync(game);
        Assert.True(TestSupport.Pump(() => vm.Launch.Phase == LaunchPhase.Starting, 2000));
        Assert.True(TestSupport.Pump(() => vm.Launch.Phase == LaunchPhase.Running, 4000));
        Assert.True(vm.Launch.IsEmbedded); // mGBA defaults to embedded → GameScreenHost placeholder
        Assert.True(TestSupport.Pump(() => vm.Launch.Phase == LaunchPhase.Idle, 6000));
        Assert.True(TestSupport.Pump(() => vm.CurrentPage is GameDetailViewModel { Game.Title: "Golden Sun" }, 3000));
        Assert.Equal(playedBefore + 1, vm.Ctx.Services.Games.Get(game.Id)!.PlayCount);
        window.Close();
    });

    [Fact]
    public Task Narrow_window_scales_the_shell_and_compacts_the_header() => UiTest.Run(() =>
    {
        var (window, vm) = Open();
        window.Width = 2560;
        window.Height = 1440;
        TestSupport.Settle(80);
        Assert.Equal(160 / 120.0, window.UiScale, 3); // 2560×1440 → 1.333
        Assert.False(vm.IsSidebarCollapsed);
        window.Width = 1100;
        window.Height = 700;
        TestSupport.Settle(80);
        Assert.Equal(0.8, window.UiScale, 3); // 1100×700 → clamped to 0.8 = 1375×875 logical
        Assert.True(vm.IsSidebarCollapsed);   // < 1500 logical: no controller name
        // nothing of the header is cut off: the clock still ends left of the caption buttons
        var clock = window.FindControl<TextBlock>("ClockText")!;
        var right = clock.TranslatePoint(new Avalonia.Point(clock.Bounds.Width, 0), window)!.Value.X;
        Assert.True(right <= 1100 - 170 + 1, $"clock ends at {right}");
        var channels = window.FindControl<ItemsControl>("ChannelList")!;
        Assert.True(channels.IsEffectivelyVisible);
        window.Close();
    });

    [Theory]
    [InlineData(1920, 1080, 1.0)]
    [InlineData(1600, 900, 100 / 120.0)]
    [InlineData(1100, 700, 0.8)]
    [InlineData(960, 600, 0.8)]
    [InlineData(3840, 2160, 2.0)]
    public void Ui_scale_follows_the_reference_1080p(int w, int h, double expected) =>
        Assert.Equal(expected, MainWindow.ComputeScale(new Avalonia.Size(w, h)), 3);
}
