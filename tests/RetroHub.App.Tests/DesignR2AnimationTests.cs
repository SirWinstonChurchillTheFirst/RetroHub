using Avalonia;
using Avalonia.Controls;
using Avalonia.VisualTree;
using RetroHub.App.Controls;
using RetroHub.App.Input;
using RetroHub.App.Theming;
using RetroHub.App.ViewModels;
using RetroHub.App.Views;
using RetroHub.Core.Config;
using Xunit;

namespace RetroHub.App.Tests;

/// <summary>
/// R2 "animation" (DESIGN 9.6): the movements added for the owner's "mach mehr Animation rein" – every one of them
/// collapses to nothing under "Aus", none of them moves under "Reduziert", and the physical ones (pick-up, pull-out,
/// needle swing) follow their curves.
/// </summary>
public class DesignR2AnimationTests
{
    private static (MainWindow Window, MainViewModel Vm) Open(MotionLevel motion, int width = 1920, int height = 1080)
    {
        var ctx = TestSupport.CreateContext(s => s.Motion = motion);
        var vm = new MainViewModel(ctx);
        var window = new MainWindow(vm) { Width = width, Height = height };
        window.Show();
        TestSupport.Pump(() => vm.CurrentPage is HomeViewModel { IsLoaded: true });
        TestSupport.Settle(100);
        return (window, vm);
    }

    private static Control? Focused(Window w) => w.FocusManager?.GetFocusedElement() as Control;

    private static void ToLibraryBox(MainWindow window, MainViewModel vm)
    {
        vm.Navigator.Navigate(PageKey.Library);
        Assert.True(TestSupport.Pump(() => vm.CurrentPage is LibraryViewModel { IsLoaded: true, Items.Count: > 3 }, 6000));
        if (vm.CurrentPage is LibraryViewModel { IsListView: true } lib) lib.IsListView = false;
        TestSupport.Settle(200);
        window.Execute(NavAction.Down);
        Assert.True(TestSupport.Pump(() => Focused(window) is GameTile, 3000));
        window.Execute(NavAction.Right);
        TestSupport.Settle(200);
    }

    [Fact]
    public void The_pick_up_rises_past_the_lift_and_settles_on_it()
    {
        // DESIGN 9.2: 1.00 → 1.075 at 60 % → 1.06 at the end of 180 ms
        Assert.Equal(1.0, GameTile.Settle(1, 0), 3);
        Assert.Equal(GameTile.LiftPeak, GameTile.Settle(1, 0.6), 3);
        Assert.Equal(GameTile.LiftScale, GameTile.Settle(1, 1), 3);
        Assert.True(GameTile.Settle(1, 0.3) > 1.04);
        Assert.True(GameTile.Settle(1, 0.8) > GameTile.LiftScale && GameTile.Settle(1, 0.8) < GameTile.LiftPeak);
        // the needle swings 0 → 4° → −2° → 0 about its knob
        Assert.Equal(0, PositionRail.SwingAngle(0), 3);
        Assert.Equal(4, PositionRail.SwingAngle(0.3), 3);
        Assert.Equal(-2, PositionRail.SwingAngle(0.65), 3);
        Assert.Equal(0, PositionRail.SwingAngle(1), 3);
        // the Fach's boxes come in one after another and are all in when the front is open
        Assert.True(Controls.SystemSpine.BoxIn(0.7, 0) > Controls.SystemSpine.BoxIn(0.7, 1));
        Assert.True(Controls.SystemSpine.BoxIn(0.7, 1) > Controls.SystemSpine.BoxIn(0.7, 2));
        for (var i = 0; i < 3; i++) Assert.Equal(1, Controls.SystemSpine.BoxIn(1, i), 3);
    }

    [Fact]
    public Task Motion_off_starts_no_animation_anywhere() => UiTest.Run(() =>
    {
        // DESIGN 9.4 / 9.6: under "Aus" every R2 movement has a zero duration – nothing is started, the end states stand
        var (window, vm) = Open(MotionLevel.Off);
        var before = Tween.Started;
        ToLibraryBox(window, vm);
        var tile = (GameTile)Focused(window)!;
        Assert.Equal(1, tile.CurrentLift, 3); // no lift at all
        // open the game (pull-out) and come back (put-back)
        window.Execute(NavAction.Accept);
        Assert.True(TestSupport.Pump(() => vm.CurrentPage is GameDetailViewModel { Game: not null }, 4000));
        TestSupport.Settle(200);
        var pull = window.GetVisualDescendants().OfType<PullLayer>().Single();
        Assert.False(pull.IsFlying);
        var box = window.GetVisualDescendants().OfType<GameTile>().First(t => t.Name == "DetailBox");
        Assert.Equal(1, box.Opacity);
        window.Execute(NavAction.Back);
        Assert.True(TestSupport.Pump(() => vm.CurrentPage is LibraryViewModel, 3000));
        TestSupport.Settle(200);
        Assert.False(pull.IsFlying);
        // a channel change to the right (page enter, mark), a system switch in place, Konsolen with its rail
        vm.Navigator.Navigate(PageKey.Systems);
        Assert.True(TestSupport.Pump(() => vm.CurrentPage is SystemsViewModel { IsLoaded: true }, 4000));
        TestSupport.Settle(200);
        window.Execute(NavAction.Right);
        window.Execute(NavAction.Right);
        TestSupport.Settle(200);
        Assert.Equal(0, window.GetVisualDescendants().OfType<PositionRail>().Single().CurrentSwing, 3);
        // an empty sleeve opens its page at once (no exit lift)
        if (vm.CurrentPage is SystemsViewModel { HasEmptySystems: true })
        {
            window.Execute(NavAction.Down);
            TestSupport.Settle(100);
            window.Execute(NavAction.Accept);
            Assert.True(TestSupport.Pump(() => vm.CurrentPage is SystemViewModel, 1000), "an empty sleeve must open its page at once under Aus");
        }
        // the service menu: a value step
        vm.Navigator.Navigate(PageKey.Settings);
        Assert.True(TestSupport.Pump(() => Focused(window) is ServiceRow || vm.CurrentPage is SettingsViewModel, 3000));
        TestSupport.Settle(200);
        if (Focused(window) is ServiceRow) window.Execute(NavAction.Right);
        TestSupport.Settle(100);
        Assert.Equal(before, Tween.Started);
        window.Close();
    });

    [Fact]
    public Task Reduced_motion_lifts_and_moves_nothing() => UiTest.Run(() =>
    {
        // DESIGN 9.4: "Reduziert" – no lift, no translation; fades stay
        var (window, vm) = Open(MotionLevel.Reduced);
        ToLibraryBox(window, vm);
        var tile = (GameTile)Focused(window)!;
        TestSupport.Settle(300);
        Assert.Equal(1, tile.CurrentLift, 3);
        window.Execute(NavAction.Accept);
        Assert.True(TestSupport.Pump(() => vm.CurrentPage is GameDetailViewModel { Game: not null }, 4000));
        TestSupport.Settle(50);
        Assert.False(window.GetVisualDescendants().OfType<PullLayer>().Single().IsFlying, "no flight under Reduziert");
        var page = window.CurrentPageView!;
        Assert.True(page.RenderTransform is not Avalonia.Media.TranslateTransform t || (Math.Abs(t.X) < 0.01 && Math.Abs(t.Y) < 0.01), "the page enters without moving");
        window.Close();
    });

    [Fact]
    public Task A_focused_box_is_picked_up_and_the_one_left_drops_back() => UiTest.Run(() =>
    {
        var (window, vm) = Open(MotionLevel.Full);
        ToLibraryBox(window, vm);
        var first = (GameTile)Focused(window)!;
        Assert.True(TestSupport.Pump(() => Math.Abs(first.CurrentLift - GameTile.LiftScale) < 0.002, 2000), $"lift {first.CurrentLift}");
        // the moment of the peak: the next box at 108 ms of its 180 ms pick-up
        Tween.Hold = TimeSpan.FromMilliseconds(108);
        try
        {
            window.Execute(NavAction.Right);
            Assert.True(TestSupport.Pump(() => Focused(window) is GameTile g && g != first && g.CurrentLift > 1.07, 1000));
        }
        finally { Tween.ReleaseHeld(); }
        var second = (GameTile)Focused(window)!;
        Assert.Equal(GameTile.LiftScale, second.CurrentLift, 3);
        Assert.True(TestSupport.Pump(() => Math.Abs(first.CurrentLift - 1) < 0.002, 2000), $"left box at {first.CurrentLift}");
        window.Close();
    });

    [Fact]
    public Task The_favourite_star_is_stamped_on_a_toggle_not_on_a_new_game() => UiTest.Run(() =>
    {
        // DESIGN 9.2: Favorit is stamped (1 → 1.28 → 1, the fill growing from the centre) – only when the SAME game is
        // toggled, never because the page shows another game
        var (window, vm) = Open(MotionLevel.Full);
        var game = vm.Ctx.Items.GetAll(vm.Ctx.Services.Games.Query(new RetroHub.Core.Library.GameQuery())).First(g => !g.IsFavorite);
        vm.Navigator.Navigate(PageKey.GameDetail, game);
        Assert.True(TestSupport.Pump(() => vm.CurrentPage is GameDetailViewModel { Game: not null } d && d.Game.Id == game.Id, 4000));
        TestSupport.Settle(400);
        var detail = (GameDetailViewModel)vm.CurrentPage!;
        var filled = window.CurrentPageView!.GetVisualDescendants().OfType<RhIcon>().Single(i => i.Stamp && i.Filled);
        Assert.Equal(1, filled.StampScale, 3);
        Tween.Hold = TimeSpan.FromMilliseconds(80);
        try
        {
            detail.ToggleFavoriteCommand.Execute(null);
            Assert.True(TestSupport.Pump(() => detail.Game?.IsFavorite == true && filled.IsVisible, 3000));
            TestSupport.Settle(50);
            Assert.True(filled.StampScale > 1.2, $"stamp scale {filled.StampScale}");
        }
        finally { Tween.ReleaseHeld(); }
        Assert.Equal(1, filled.StampScale, 3);
        detail.ToggleFavoriteCommand.Execute(null);
        Assert.True(TestSupport.Pump(() => detail.Game?.IsFavorite == false, 3000));
        window.Close();
    });

    [Fact]
    public Task Opening_a_game_flies_its_box_to_the_stage_and_back_into_its_slot() => UiTest.Run(() =>
    {
        // DESIGN 9.2 "Herausziehen und zurückstellen": one copy flies on an arc; the stage box waits hidden until it lands;
        // going back, the shelf box stays a gap until its copy is in
        var (window, vm) = Open(MotionLevel.Full);
        ToLibraryBox(window, vm);
        var tile = (GameTile)Focused(window)!;
        var pull = window.GetVisualDescendants().OfType<PullLayer>().Single();
        var source = pull.RectOf(tile.Art)!.Value;
        Tween.Hold = TimeSpan.FromMilliseconds(150);
        try
        {
            window.Execute(NavAction.Accept);
            Assert.True(TestSupport.Pump(() => pull.IsFlying, 2000), "the box should be on its way");
            var box = window.GetVisualDescendants().OfType<GameTile>().First(t => t.Name == "DetailBox");
            Assert.Equal(0, box.Opacity);
            var target = pull.RectOf(box.Art)!.Value;
            var mid = pull.CopyRect;
            // half-way between shelf and stage, above the straight line (the arc)
            Assert.True(mid.Center.X > Math.Min(source.Center.X, target.Center.X) - 1 && mid.Center.X < Math.Max(source.Center.X, target.Center.X) + 1, $"{source} → {mid} → {target}");
            Assert.True(mid.Center.Y < (source.Center.Y + target.Center.Y) / 2, $"no arc: {source} → {mid} → {target}");
        }
        finally { Tween.ReleaseHeld(); }
        Assert.False(pull.IsFlying);
        Assert.Equal(1, window.GetVisualDescendants().OfType<GameTile>().First(t => t.Name == "DetailBox").Opacity);
        TestSupport.Settle(400);
        Tween.Hold = TimeSpan.FromMilliseconds(130);
        try
        {
            window.Execute(NavAction.Back);
            Assert.True(TestSupport.Pump(() => vm.CurrentPage is LibraryViewModel && pull.IsFlying, 2000), "the box should fly back");
            Assert.Equal(0, tile.Opacity); // its slot is a gap until it lands
        }
        finally { Tween.ReleaseHeld(); }
        Assert.Equal(1, tile.Opacity);
        window.Close();
    });
}
