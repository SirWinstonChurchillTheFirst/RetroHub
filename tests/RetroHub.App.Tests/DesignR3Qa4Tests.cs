using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.VisualTree;
using RetroHub.App.Controls;
using RetroHub.App.Theming;
using RetroHub.App.ViewModels;
using RetroHub.App.Views;
using RetroHub.App.Views.Pages;
using RetroHub.Core.Config;
using RetroHub.Core.Library;
using Xunit;
using SystemSpine = RetroHub.App.Controls.SystemSpine;

namespace RetroHub.App.Tests;

/// <summary>
/// R3 QA 4 (owner: "mach das fertig"): the pulled binder's cover is a lighter graphite, the rail is a 34 px steel profile,
/// a "Noch leer" sleeve prints its motif under a short code (the PC floppy), and the detail page's back ends on whole
/// pieces above the footer – nothing is cut at the fold.
/// </summary>
public class DesignR3Qa4Tests
{
    private static (MainWindow Window, MainViewModel Vm) Open(int width, int height)
    {
        var ctx = TestSupport.CreateContext(s => s.Motion = MotionLevel.Off);
        var vm = new MainViewModel(ctx);
        var window = new MainWindow(vm) { Width = width, Height = height };
        window.Show();
        TestSupport.Pump(() => vm.CurrentPage is HomeViewModel { IsLoaded: true });
        TestSupport.Settle(100);
        return (window, vm);
    }

    private static void Close(Window window)
    {
        TestSupport.Settle(1000);
        window.Close();
        TestSupport.Settle(200);
    }

    [Fact]
    public void The_binder_cover_is_a_lighter_graphite_still_far_below_the_paper()
    {
        foreach (var id in new[] { "nes", "ps2", "gc", "md" })
        {
            var look = SystemPalette.Get(id);
            var body = SystemSpine.HousingBody(look);
            var cover = SystemSpine.CoverBody(look);
            Assert.True(ColorUtil.Contrast(cover, Colors.Black) > ColorUtil.Contrast(body, Colors.Black) * 1.3, $"{id}: {cover} not lighter than {body}");
            Assert.True(ColorUtil.Contrast(SystemPalette.Paper, cover) >= 4.5, $"{id}: the paper print must stand on {cover}");
        }
        // light housings keep their tone (the front prints in ink there)
        var pc = SystemPalette.Get("pc");
        Assert.Equal(SystemSpine.HousingBody(pc), SystemSpine.CoverBody(pc));
    }

    [Fact]
    public void A_sleeve_prints_its_motif_under_a_short_code_only()
    {
        var label = new Size(36, 80);
        // "PC" (≈ 18 px long) leaves room: the motif stands at the label's foot, under the code
        var box = SystemSpine.SleeveMotifBox(label, 18);
        Assert.True(box.Width >= 14, "the PC sleeve shows its motif");
        Assert.True(box.Bottom <= label.Height && box.Y >= 6 + 18 + 8);
        // "PC Engine" fills the label: no print squeezed under it
        Assert.Equal(0, SystemSpine.SleeveMotifBox(label, 70).Width);
        // PC's pale grey is printed darker so the floppy reads on the paper
        var ink = ((ISolidColorBrush)SystemSpine.SleevePrintInk(SystemPalette.Get("pc"))).Color;
        Assert.True(ColorUtil.Contrast(ink, Color.Parse("#D8DADC")) >= 3, $"PC ink {ink}");
    }

    [Fact]
    public Task The_rail_is_a_34_px_steel_profile_at_1080() => UiTest.Run(() =>
    {
        foreach (var (w, h, rail) in new[] { (1920, 1080, 34.0), (1100, 700, 28.0) })
        {
            var (window, vm) = Open(w, h);
            vm.Navigator.Navigate(PageKey.Systems);
            Assert.True(TestSupport.Pump(() => window.FocusManager?.GetFocusedElement() is SystemSpine { IsOpen: true }, 4000));
            TestSupport.Settle(150);
            var positionRail = window.GetVisualDescendants().OfType<PositionRail>().Single(r => r.IsEffectivelyVisible);
            Assert.Equal(rail, positionRail.RailHeight);
            Assert.True(positionRail.Bounds.Height >= rail + 12, "the row holds the rail and the Reiter's clip");
            Close(window);
        }
    });

    [Fact]
    public Task Nothing_of_the_back_is_cut_at_the_fold() => UiTest.Run(() =>
    {
        foreach (var (w, h) in new[] { (1920, 1080), (1100, 700) })
        {
            var (window, vm) = Open(w, h);
            var game = vm.Ctx.Items.Get(vm.Ctx.Services.Games.Query(new GameQuery { Text = "Pokémon Smaragd" }).First());
            vm.Navigator.Navigate(PageKey.GameDetail, game);
            Assert.True(TestSupport.Pump(() => vm.CurrentPage is GameDetailViewModel { Game: not null } d && d.Game.Id == game.Id, 4000));
            TestSupport.Settle(400);
            var page = Assert.IsType<GameDetailPage>(window.PageHost.Current);
            var scroll = page.FindControl<ScrollViewer>("Scroll")!;
            var back = page.FindControl<Grid>("Back")!;
            var fold = scroll.Viewport.Height - 20;
            // every text of the back that shows (all its ancestors opaque) lies wholly above the fold or wholly below it
            foreach (var text in back.GetVisualDescendants().OfType<TextBlock>().Where(t => t.IsEffectivelyVisible && t.Bounds.Height > 0))
            {
                // (a closed fold panel holds its rows at height 0: they are not on screen)
                var shown = text.GetSelfAndVisualAncestors().TakeWhile(v => v != scroll).All(v => v.Opacity > 0 && v.Bounds.Height > 0);
                if (!shown) continue;
                var top = text.TranslatePoint(new Point(0, 0), scroll)!.Value.Y;
                var bottom = text.TranslatePoint(new Point(0, text.Bounds.Height), scroll)!.Value.Y;
                Assert.False(top < fold && bottom > fold, $"{w}x{h}: \"{text.Text}\" is cut at the fold ({top:F0}–{bottom:F0}, fold {fold:F0})");
            }
            Close(window);
        }
    });
}
