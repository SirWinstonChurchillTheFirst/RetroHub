using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media;
using RetroHub.App.Controls;
using RetroHub.App.Input;
using RetroHub.App.Theming;
using RetroHub.App.ViewModels;
using RetroHub.App.Views;
using RetroHub.Core.Catalog;
using RetroHub.Core.Config;
using Xunit;
using SystemSpine = RetroHub.App.Controls.SystemSpine;

namespace RetroHub.App.Tests;

/// <summary>
/// Konsolen "Die Fächerwand" and Systemseite "Das Fach" (DESIGN 6, 7.4, 7.5, M5): one motif per system that stands
/// on its grid, the wall of spines with the open front in slot 3, the board that reads against every wall, the
/// neighbour switch in place and the Systeminfo overlay.
/// </summary>
public class DesignM5Tests
{
    private static (MainWindow Window, MainViewModel Vm) Open(int width = 1600, int height = 900)
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

    // ===================== motifs =====================

    [Fact]
    public Task Every_motif_stands_on_its_grid_and_stays_inside_it() => UiTest.Run(() =>
    {
        foreach (var motif in Enum.GetValues<Motif>())
        {
            var bounds = MotifCatalog.Silhouette(motif).Bounds;
            Assert.True(bounds.Width > 20 && bounds.Height > 20, $"{motif}: {bounds}");
            Assert.InRange(bounds.Bottom, 117, 121); // "Jedes Motiv steht": the foot on y = 120 (strokes may reach ±1)
            Assert.True(bounds.Left >= -1 && bounds.Right <= 121 && bounds.Top >= -1, $"{motif} leaves the 120 grid: {bounds}");
        }
    });

    [Fact]
    public void The_stage_board_reads_in_front_of_every_wall()
    {
        // the plain Regal-hell front almost vanished in front of the warm-grey walls; the wall board is lifted from both tones
        foreach (var id in SystemPalette.KnownIds)
        {
            var look = SystemPalette.Get(id);
            var front = ColorUtil.Mix(look.WallHi, SystemPalette.Paper, 0.10);
            Assert.True(ColorUtil.Contrast(front, look.Wall) >= 1.35, $"{id}: board/wall {ColorUtil.Contrast(front, look.Wall):0.00}");
            Assert.True(ColorUtil.Contrast(front, look.WallHi) >= 1.15, $"{id}: board/wall-hi {ColorUtil.Contrast(front, look.WallHi):0.00}");
        }
    }

    [Fact]
    public void The_16_bit_modules_carry_dither_and_grooves()
    {
        foreach (var id in new[] { "snes", "megadrive" })
        {
            var look = SystemPalette.Get(id);
            Assert.Equal(Texture.Dither16, look.Texture);
            Assert.Equal(Texture.Grooves, look.Overlay);
        }
        Assert.Equal(Texture.None, SystemPalette.Get("sega32x").Overlay); // DESIGN 6.3: 32X without grooves
    }

    // ===================== Konsolen =====================

    [Fact]
    public Task Konsolen_is_a_wall_of_spines_whose_focused_front_stands_in_slot_3() => UiTest.Run(() =>
    {
        var (window, vm) = Open(1920, 1080);
        vm.Navigator.Navigate(PageKey.Systems);
        Assert.True(TestSupport.Pump(() => Focused(window) is SystemSpine, 4000));
        var systems = Assert.IsType<SystemsViewModel>(vm.CurrentPage);
        var first = Assert.IsType<SystemSpine>(Focused(window));
        TestSupport.Settle(100);
        Assert.True(first.IsOpen);
        Assert.Equal(520, first.FrontWidth);
        Assert.Equal(600, first.Bounds.Height, 0);
        Assert.Same(systems.Systems[0], systems.Current);
        Assert.True(systems.Systems[0].IsCurrent);
        // the page ground is the focused system's wall, in shadow
        Assert.Equal(BackdropKind.Wall, systems.Backdrop.Kind);
        Assert.Equal(systems.Systems[0].Id, systems.Backdrop.SystemId);
        Assert.True(systems.Backdrop.WallShade > 0);
        Assert.Equal("Fach 1 von 13", systems.PositionText);

        for (var i = 0; i < 4; i++) { window.Execute(NavAction.Right); TestSupport.Settle(40); }
        var fifth = Assert.IsType<SystemSpine>(Focused(window));
        Assert.Same(systems.Systems[4], fifth.DataContext);
        Assert.False(first.IsOpen);
        Assert.Equal(first.SpineWidth, first.Bounds.Width, 0);
        Assert.Equal("Fach 5 von 13", systems.PositionText);
        Assert.Equal(systems.Systems[4].Id, systems.Backdrop.SystemId);
        // slot 3: two spines (128 + 8 each) before the open item, measured from the page margin
        var wall = window.GetVisualDescendantsOf<Shelf>().First(s => s.Name == "Wall");
        Assert.Equal(-(4 * 136 - 2 * 136), wall.Offset, 0);

        // up leaves the wall for the header; confirm opens the system page
        window.Execute(NavAction.Accept);
        Assert.True(TestSupport.Pump(() => vm.CurrentPage is SystemViewModel { IsLoaded: true } s && s.System?.Id == systems.Systems[4].Id, 4000));
        window.Close();
    });

    [Fact]
    public Task Konsolen_wall_ends_with_boundary_feedback_and_keeps_the_last_front_inside() => UiTest.Run(() =>
    {
        var (window, vm) = Open(1920, 1080);
        vm.Navigator.Navigate(PageKey.Systems);
        Assert.True(TestSupport.Pump(() => Focused(window) is SystemSpine, 4000));
        var systems = (SystemsViewModel)vm.CurrentPage!;
        for (var i = 0; i < systems.Systems.Count + 2; i++) window.Execute(NavAction.Right);
        TestSupport.Settle(100);
        var last = Assert.IsType<SystemSpine>(Focused(window));
        Assert.Same(systems.Systems[^1], last.DataContext);
        var wall = window.GetVisualDescendantsOf<Shelf>().First(s => s.Name == "Wall");
        // the row is clamped: the open front's right edge stays inside the page margin
        var right = last.TranslatePoint(new Point(last.Bounds.Width, 0), wall);
        Assert.NotNull(right);
        Assert.True(right!.Value.X <= wall.Bounds.Width - 96 + 1, $"front ends at {right.Value.X}, wall {wall.Bounds.Width}");
        window.Close();
    });

    // ===================== Systemseite =====================

    [Fact]
    public Task Shoulders_switch_the_system_in_place_and_back_returns_to_the_wall() => UiTest.Run(() =>
    {
        var (window, vm) = Open();
        vm.Navigator.Navigate(PageKey.Systems);
        Assert.True(TestSupport.Pump(() => Focused(window) is SystemSpine, 4000));
        vm.Navigator.Navigate(PageKey.System, "gba");
        Assert.True(TestSupport.Pump(() => vm.CurrentPage is SystemViewModel { IsLoaded: true } s && s.System?.Id == "gba", 4000));
        var page = (SystemViewModel)vm.CurrentPage!;
        Assert.Equal("gbc", page.Previous?.Id);
        Assert.Equal("nds", page.Next?.Id);
        Assert.True(TestSupport.Pump(() => Focused(window) is GameTile, 3000));

        Assert.True(window.Execute(NavAction.PageRight));
        Assert.True(TestSupport.Pump(() => page.System?.Id == "nds" && page.IsLoaded, 4000));
        Assert.Equal(PageKey.System, vm.Navigator.Current);
        Assert.Equal("nds", vm.Navigator.CurrentArgument);
        Assert.Equal("nds", page.Backdrop.SystemId);
        Assert.True(TestSupport.Pump(() => Focused(window) is GameTile { DataContext: GameItemViewModel g } && g.SystemId == "nds", 3000));

        // one Back: to the wall, not to the previous system
        Assert.True(window.Execute(NavAction.Back));
        Assert.True(TestSupport.Pump(() => vm.CurrentPage is SystemsViewModel, 3000));
        window.Close();
    });

    [Fact]
    public Task Up_from_the_first_row_reaches_the_banner_and_systeminfo_is_an_overlay_back_closes() => UiTest.Run(() =>
    {
        var (window, vm) = Open();
        vm.Navigator.Navigate(PageKey.System, "snes");
        Assert.True(TestSupport.Pump(() => vm.CurrentPage is SystemViewModel { IsLoaded: true } s && s.System?.Id == "snes", 4000));
        var page = (SystemViewModel)vm.CurrentPage!;
        Assert.True(TestSupport.Pump(() => Focused(window) is GameTile, 3000));
        window.Execute(NavAction.Up);
        TestSupport.Settle(60);
        Assert.Equal("InfoButton", Focused(window)?.Name);

        window.Execute(NavAction.Accept);
        Assert.True(TestSupport.Pump(() => page.IsInfoOpen, 2000));
        Assert.True(TestSupport.Pump(() => Focused(window)?.Name == "InfoCloseButton", 2000));
        Assert.Contains(page.InfoLines, l => l.Value.Contains("games/snes/"));
        Assert.NotEqual(default, page.HeaderDim); // the dim continues under the header
        // the overlay is modal: directions stay on it, the shoulders do nothing
        window.Execute(NavAction.Left);
        Assert.Equal("InfoCloseButton", Focused(window)?.Name);
        window.Execute(NavAction.PageRight);
        Assert.Equal("snes", page.System?.Id);
        // Back closes it (the page stays) and the focus returns to the button
        Assert.True(window.Execute(NavAction.Back));
        Assert.False(page.IsInfoOpen);
        Assert.Equal(PageKey.System, vm.Navigator.Current);
        Assert.True(TestSupport.Pump(() => Focused(window)?.Name == "InfoButton", 2000));
        window.Execute(NavAction.Down);
        Assert.True(TestSupport.Pump(() => Focused(window) is GameTile, 2000));
        window.Close();
    });

    [Fact]
    public Task System_sentence_says_when_it_was_last_played_only_after_real_play() => UiTest.Run(() =>
    {
        TestSupport.CreateContext(); // German texts
        var def = new SystemDef { Id = "gba", Name = "Game Boy Advance", Manufacturer = "Nintendo", Year = 2001, Category = SystemCategory.Handheld };
        var now = new DateTime(2026, 11, 9, 12, 0, 0, DateTimeKind.Utc);
        Assert.Equal("Handheld von Nintendo, 2001. 7 Spiele, 46 Stunden gespielt, zuletzt heute.",
            SystemViewModel.BuildSentence(def, 7, 46 * 3600, now.AddHours(-1), now));
        // under a minute in all: "noch nicht gespielt" never gets a "zuletzt" behind it
        Assert.Equal("Handheld von Nintendo, 2001. 7 Spiele, noch nicht gespielt.", SystemViewModel.BuildSentence(def, 7, 20, now.AddDays(-1), now));
        Assert.Equal("Handheld von Nintendo, 2001. Noch keine Spiele im Fach.", SystemViewModel.BuildSentence(def, 0, 0, null, now));
        Assert.EndsWith("zuletzt am 3. März.", SystemViewModel.BuildSentence(def, 2, 7200, new DateTime(2026, 3, 3, 12, 0, 0, DateTimeKind.Utc), now));
    });
}

internal static class VisualTreeTestExtensions
{
    public static IEnumerable<T> GetVisualDescendantsOf<T>(this Visual root) where T : Visual =>
        Avalonia.VisualTree.VisualExtensions.GetVisualDescendants(root).OfType<T>();
}
