using Avalonia;
using Avalonia.Controls;
using Avalonia.VisualTree;
using RetroHub.App.Controls;
using RetroHub.App.Theming;
using RetroHub.App.ViewModels;
using RetroHub.App.Views;
using RetroHub.Core.Config;
using Xunit;
using SystemSpine = RetroHub.App.Controls.SystemSpine;

namespace RetroHub.App.Tests;

/// <summary>
/// R3 QA 3: the shelf's paper label rides with its row (it named whichever box stood at the board's start), the Konsolen
/// wall fills the page when there is no "Noch leer" row (no band of empty felt wider than 64 px), the open front is the
/// rendered binder (its print starts on the cover, its motif stays inside the front), binder labels follow one rule, and
/// a focused row shows one focus signal (no paper chip behind the label).
/// </summary>
public class DesignR3Qa3Tests
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
    public Task The_shelf_label_moves_with_the_row() => UiTest.Run(() =>
    {
        var (window, _) = Open(1920, 1080);
        var shelf = window.GetVisualDescendants().OfType<Shelf>().First(s => s.Name == "RecentShelf");
        Assert.True(TestSupport.Pump(() => shelf.Items().Count > 4, 4000));
        TestSupport.Settle(100);
        var label = shelf.GetVisualDescendants().OfType<ShelfLabel>().First();
        double Left(Visual v) => v.TranslatePoint(new Point(0, 0), shelf)!.Value.X;
        Assert.Equal(Left(shelf.Items()[0]), Left(label), 0);
        Assert.True(shelf.ScrollRow(-600));
        TestSupport.Settle(150);
        Assert.True(Left(shelf.Items()[0]) < 0, "the row moved");
        // the label stands under the first box wherever the row is
        Assert.Equal(Left(shelf.Items()[0]), Left(label), 0);
        Close(window);
    });

    [Fact]
    public Task Without_a_Noch_leer_row_the_wall_fills_the_page() => UiTest.Run(() =>
    {
        var (window, vm) = Open(1920, 1080);
        vm.Navigator.Navigate(PageKey.Systems);
        Assert.True(TestSupport.Pump(() => window.FocusManager?.GetFocusedElement() is SystemSpine { IsOpen: true }, 4000));
        TestSupport.Settle(200);
        var page = (SystemsViewModel)vm.CurrentPage!;
        Assert.False(page.HasEmptySystems); // the demo library: every system has games
        var root = window.GetVisualDescendants().OfType<Panel>().First(p => p.Name == "Root" && p.Classes.Contains("tall"));
        var stack = root.GetVisualDescendants().OfType<StackPanel>().First(s => s.Name == "Stack");
        var pageHeight = root.Bounds.Height;
        // above the wall and between the rail and the footer: at most 64 px of empty wall
        Assert.InRange(stack.Margin.Top, 0, 64);
        Assert.InRange(pageHeight - (stack.Margin.Top + stack.Bounds.Height), 0, 64);
        var front = window.GetVisualDescendants().OfType<SystemSpine>().First(s => s.IsOpen);
        Assert.Equal("front-l", front.FrontSprite);
        Assert.Equal(760, front.Bounds.Height, 0);
        Close(window);
    });

    [Fact]
    public void Binder_labels_follow_one_rule()
    {
        Assert.Equal("GC", SystemPalette.LabelCode("gamecube", "GameCube"));
        Assert.Equal("GC", SystemPalette.LabelCode("gamecube", "GC"));
        Assert.Equal("SNES", SystemPalette.LabelCode("snes", "SNES"));
        foreach (var id in SystemPalette.KnownIds)
            if (SystemPalette.ShortDisplayName(id) is { } s) Assert.True(s.Length <= 18, $"{id}: {s}");
        Assert.Equal("Super Nintendo", SystemPalette.ShortDisplayName("snes"));
        Assert.Equal("GameCube", SystemPalette.ShortDisplayName("gc"));
    }

    [Fact]
    public Task A_focused_row_has_one_focus_signal() => UiTest.Run(() =>
    {
        var (window, vm) = Open(1920, 1080);
        vm.Navigator.Navigate(PageKey.Settings);
        Assert.True(TestSupport.Pump(() => window.FocusManager?.GetFocusedElement() is ServiceRow, 4000));
        TestSupport.Settle(150);
        var row = (ServiceRow)window.FocusManager!.GetFocusedElement()!;
        var chip = row.GetVisualDescendants().OfType<Border>().First(b => b.Name == "PART_Chip");
        Assert.True(chip.Background is null || chip.Background is Avalonia.Media.ISolidColorBrush { Color.A: 0 }, "no paper chip behind the label");
        var label = row.GetVisualDescendants().OfType<TextBlock>().First(t => t.Name == "PART_Label");
        Assert.Equal(Avalonia.Media.Color.Parse("#FFFFFF"), ((Avalonia.Media.ISolidColorBrush)label.Foreground!).Color);
        var bar = row.GetVisualDescendants().OfType<Border>().First(b => b.Name == "PART_Bar");
        Assert.Equal(4, bar.Width);
        Assert.Equal(1, bar.Opacity);
        Close(window);
    });
}
