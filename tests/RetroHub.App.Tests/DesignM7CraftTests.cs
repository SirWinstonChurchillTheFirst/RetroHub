using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.VisualTree;
using RetroHub.App.Controls;
using RetroHub.App.Input;
using RetroHub.App.Theming;
using RetroHub.App.ViewModels;
using RetroHub.App.Views;
using RetroHub.Core.Config;
using Xunit;
using SystemSpine = RetroHub.App.Controls.SystemSpine;

namespace RetroHub.App.Tests;

/// <summary>
/// M7 "gezeichnet" (DESIGN 4.7, 7.4.3): the drawn objects vary by a STABLE seed (never a per-process hash), the
/// "Noch leer" row is a row of sleeves ending at a bookend, focus pulls a sleeve out, the position rail's needle stands
/// on the current system, and the shared drawing parts stay cached.
/// </summary>
public class DesignM7CraftTests
{
    private static (MainWindow Window, MainViewModel Vm) Open(int width = 1920, int height = 1080)
    {
        var ctx = TestSupport.CreateContext(s => s.Motion = MotionLevel.Off);
        var vm = new MainViewModel(ctx);
        var window = new MainWindow(vm) { Width = width, Height = height };
        window.Show();
        TestSupport.Pump(() => vm.CurrentPage is HomeViewModel { IsLoaded: true });
        TestSupport.Settle(100);
        return (window, vm);
    }

    private static Control? Focused(Window w) => w.FocusManager?.GetFocusedElement() as Control;

    [Fact]
    public void Seeds_are_stable_FNV_1a_and_well_mixed()
    {
        // FNV-1a 32 of "nes": the same in every process (string.GetHashCode is randomised per process)
        Assert.Equal(748264541u, Craft.Seed("nes"));
        Assert.Equal(2166136261u, Craft.Seed(null));
        Assert.NotEqual(Craft.Seed("nes"), Craft.Seed("snes"));
        var seed = Craft.Seed("gba");
        Assert.Equal(Craft.Rand(seed, 3), Craft.Rand(seed, 3));
        Assert.NotEqual(Craft.Rand(seed, 3), Craft.Rand(seed, 4));
        for (var i = 0; i < 200; i++)
        {
            var v = Craft.Range(seed, i, 2, 5);
            Assert.InRange(v, 2, 5);
            Assert.InRange(Craft.Pick(seed, i, 0, 2), 0, 2);
        }
    }

    [Fact]
    public Task Paper_outlines_are_cut_by_hand_but_the_same_for_the_same_label() => UiTest.Run(() =>
    {
        var size = new Size(120, 24);
        var a = Craft.PaperOutline(size, Craft.Seed("Noch leer"));
        Assert.Same(a, Craft.PaperOutline(size, Craft.Seed("Noch leer"))); // cached per (size, seed)
        var b = Craft.PaperOutline(size, Craft.Seed("Darstellung"));
        Assert.NotEqual(a.Bounds, b.Bounds);
        // never a crisp rectangle, never more than the jitter off the label's size
        Assert.NotEqual(new Rect(size), a.Bounds);
        Assert.InRange(a.Bounds.X, -0.8, 0.8);
        Assert.InRange(a.Bounds.Right, 119.2, 120.8);
        Assert.InRange(a.Bounds.Bottom, 23.2, 24.8);
    });

    [Fact]
    public Task Noch_leer_is_a_row_of_sleeves_that_ends_at_a_bookend_and_focus_pulls_a_sleeve_out() => UiTest.Run(() =>
    {
        // the demo has no system without games: the row of the Konsolen page is built here with the demo's systems
        var (window, vm) = Open();
        vm.Navigator.Navigate(PageKey.Systems);
        Assert.True(TestSupport.Pump(() => Focused(window) is SystemSpine, 4000));
        var page = (SystemsViewModel)vm.CurrentPage!;
        var shelf = new Shelf
        {
            Bookend = true, ItemsSource = page.Systems, ItemSpacing = 8, ItemHeight = 148, TopGap = 0,
            ItemTemplate = new Avalonia.Controls.Templates.FuncDataTemplate<SystemItemViewModel>((_, _) =>
                new SystemSpine { Opens = false, Height = 148, SpineWidth = 48, VerticalAlignment = Avalonia.Layout.VerticalAlignment.Bottom }),
        };
        var host = new Window { Width = 1600, Height = 400, Content = shelf };
        host.Show();
        Assert.True(TestSupport.Pump(() => shelf.GetVisualDescendants().OfType<Bookend>().Any() && shelf.Items().Count == page.Systems.Count, 4000));
        TestSupport.Settle(100);
        var bookend = shelf.GetVisualDescendants().OfType<Bookend>().Single();
        Assert.True(bookend.IsEffectivelyVisible);
        var sleeves = shelf.Items().Cast<SystemSpine>().ToList();
        // the bookend stands after the last sleeve
        Assert.True(bookend.TranslatePoint(default, shelf)!.Value.X > sleeves[^1].TranslatePoint(default, shelf)!.Value.X);
        // the drawn sleeves differ (seeded width/height): the row is not a comb of equal swatches
        var shapes = sleeves.Select(s => (s.ObjectRect.Width, s.ObjectRect.Height)).Distinct().Count();
        Assert.True(shapes > 3, $"only {shapes} different sleeve shapes");
        Assert.All(sleeves, s => Assert.Equal(0, s.Pull));

        // focus pulls the sleeve out (motion off: at once, without the lift), the next one takes over
        Assert.True(shelf.FocusItem(2));
        TestSupport.Settle(100);
        Assert.Equal(1, sleeves[2].Pull);
        Assert.False(sleeves[2].IsOpen);
        Assert.True(shelf.FocusItem(3));
        TestSupport.Settle(100);
        Assert.Equal(0, sleeves[2].Pull);
        Assert.Equal(1, sleeves[3].Pull);
        host.Close();
        window.Close();
    });

    [Fact]
    public Task The_dial_needle_stands_on_the_current_system() => UiTest.Run(() =>
    {
        var (window, vm) = Open();
        vm.Navigator.Navigate(PageKey.Systems);
        Assert.True(TestSupport.Pump(() => Focused(window) is SystemSpine, 4000));
        var rail = window.GetVisualDescendants().OfType<PositionRail>().Single();
        TestSupport.Settle(100);
        var first = rail.Needle;
        Assert.False(double.IsNaN(first));
        window.Execute(NavAction.Right);
        window.Execute(NavAction.Right);
        TestSupport.Settle(100);
        // motion off: the needle jumps to the third tick, evenly spaced over the rail
        var systems = (SystemsViewModel)vm.CurrentPage!;
        var step = (rail.Bounds.Width - 24) / (systems.Systems.Count - 1);
        Assert.Equal(Math.Round(12 + 2 * step), rail.Needle, 0);
        Assert.True(rail.Needle > first);
        window.Close();
    });
}
