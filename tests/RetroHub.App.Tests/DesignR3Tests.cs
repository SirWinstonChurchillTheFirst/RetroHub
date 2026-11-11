using Avalonia;
using Avalonia.Controls;
using Avalonia.VisualTree;
using RetroHub.App.Controls;
using RetroHub.App.Input;
using RetroHub.App.ViewModels;
using RetroHub.App.Views;
using RetroHub.Core.Config;
using Xunit;
using SystemSpine = RetroHub.App.Controls.SystemSpine;

namespace RetroHub.App.Tests;

/// <summary>
/// R3 (owner feedback "fühlt sich hin gecodet an", QA round 2): the Start spines keep their motif under the seeded drop,
/// the "Noch leer" row is a packed row of different sleeves (lean, one lying flat) whose pulled sleeve stays on the
/// board while its neighbours step back, the counter window speaks on the low edge, the Werkbank side column takes the
/// free room, and the favourite stamp lands on the box.
/// </summary>
public class DesignR3Tests
{
    private static (MainWindow Window, MainViewModel Vm) Open(int width, int height, MotionLevel motion = MotionLevel.Off)
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

    private static List<SystemSpine> StartSpines(Window window) =>
        window.GetVisualDescendants().OfType<SystemSpine>().Where(s => s.Opens && !s.ShowMotif && !s.IsOpen && s.Bounds.Height > 0).ToList();

    [Fact]
    public Task Start_spines_keep_their_motif_under_the_seeded_drop() => UiTest.Run(() =>
    {
        var (window, _) = Open(1920, 1080);
        Assert.True(TestSupport.Pump(() => StartSpines(window).Count >= 8, 4000));
        TestSupport.Settle(100);
        var spines = StartSpines(window);
        // 1920: every closed spine draws its motif at ≥ 26 px (R2 lost it on 8 of 12; long codes step down to 16 instead)
        Assert.All(spines, s => Assert.True(s.ClosedMotifSize >= 26, $"{(s.DataContext as SystemItemViewModel)?.Id}: motif {s.ClosedMotifSize}"));
        window.Close();

        // ≈1100: the short codes keep a motif too (a long code keeps its whole length and goes without one)
        var (small, _) = Open(1100, 700);
        Assert.True(TestSupport.Pump(() => StartSpines(small).Count >= 8, 4000));
        TestSupport.Settle(100);
        var compact = StartSpines(small);
        var with = compact.Count(s => s.ClosedMotifSize >= SystemSpine.MinSpineMotif);
        Assert.True(with >= compact.Count - 3, $"only {with} of {compact.Count} spines show a motif at 1100");
        small.Close();
    });

    [Fact]
    public Task Noch_leer_sleeves_differ_lean_stand_and_a_pulled_one_stays_on_the_board() => UiTest.Run(() =>
    {
        // the demo has no system without games: the low edge is built here with the demo's systems, as on the page
        var (window, vm) = Open(1920, 1080);
        vm.Navigator.Navigate(PageKey.Systems);
        Assert.True(TestSupport.Pump(() => Focused(window) is SystemSpine, 4000));
        var page = (SystemsViewModel)vm.CurrentPage!;
        var shelf = new Shelf
        {
            Bookend = true, ItemsSource = page.Systems, ItemSpacing = 2, ItemHeight = 168, TopGap = 0,
            ItemTemplate = new Avalonia.Controls.Templates.FuncDataTemplate<SystemItemViewModel>((_, _) =>
                new SystemSpine { Opens = false, Height = 168, SpineWidth = 56, VerticalAlignment = Avalonia.Layout.VerticalAlignment.Bottom }),
        };
        var host = new Window { Width = 1900, Height = 400, Content = shelf };
        host.Show();
        Assert.True(TestSupport.Pump(() => shelf.Items().Count == page.Systems.Count, 4000));
        TestSupport.Settle(150);
        var sleeves = shelf.Items().Cast<SystemSpine>().ToList();
        Assert.True(sleeves.Count >= 6);
        // widths: a real spread (SpineWidth −14 … +10), each item as wide as its own sleeve
        var widths = sleeves.Where(s => !s.SleeveFlat).Select(s => s.ClosedWidth).ToList();
        Assert.True(widths.Max() - widths.Min() >= 12, $"widths {widths.Min()}–{widths.Max()}");
        Assert.All(sleeves, s => Assert.Equal(s.ClosedWidth, s.Bounds.Width, 0));
        // R3 QA 3: every sleeve stands (the lying box before the bookend read as a placeholder); some lean 4–7°
        Assert.All(sleeves, s => Assert.False(s.SleeveFlat));
        Assert.All(sleeves.Where(s => s.SleeveLean != 0), s => Assert.InRange(-s.SleeveLean * 180 / Math.PI, 3.5, 7.5));
        Assert.False(sleeves[0].SleeveLean != 0, "the first sleeve has no neighbour to lean on");

        // focus pulls a sleeve FORWARD: its foot stays on the board (no gap under it), the others step back
        Assert.True(shelf.FocusItem(2));
        TestSupport.Settle(150);
        Assert.Equal(1, sleeves[2].Pull);
        Assert.Equal(sleeves[2].Bounds.Height, sleeves[2].ObjectRect.Bottom, 0);
        Assert.Equal(0, sleeves[2].RowShade);
        Assert.All(sleeves.Where((_, i) => i != 2), s => Assert.Equal(0.15, s.RowShade, 3));
        host.Close();
        window.Close();
    });

    [Fact]
    public void The_counter_window_speaks_on_the_low_edge_too()
    {
        TestSupport.CreateContext(); // German texts
        Assert.Equal("Fach 3 von 13", SystemsViewModel.Position(2, 13, -1, 20));
        Assert.Equal("Noch leer · 3 von 20", SystemsViewModel.Position(-1, 13, 2, 20));
        Assert.Equal("", SystemsViewModel.Position(-1, 13, -1, 20));
    }

    [Fact]
    public Task The_Werkbank_side_column_takes_the_free_room() => UiTest.Run(() =>
    {
        TestSupport.CreateContext();
        var frame = new WorkbenchFrame();
        var side = new Border();
        WorkbenchFrame.SetRole(side, WorkbenchRole.Side);
        frame.Children.Add(side);
        double? raised = null;
        frame.SideColumnWidthChanged += w => raised = w;
        frame.Measure(new Size(1920, 1080));
        // 1920 − 2 × 96 − 56 − 1100 − 24 = 548 (R3 QA: ≈ 250 px stood empty right of the 420 column)
        Assert.Equal(548, frame.SideColumnWidth);
        Assert.Equal(548, raised);
        Assert.Equal(1100, frame.ListWidth);
        frame.Measure(new Size(1600, 900));
        Assert.Equal(WorkbenchFrame.SideWidth, frame.SideColumnWidth);
        frame.Measure(new Size(2560, 1440));
        Assert.Equal(WorkbenchFrame.SideMax, frame.SideColumnWidth);
    });

    [Fact]
    public Task The_favourite_stamp_lands_only_with_motion() => UiTest.Run(() =>
    {
        foreach (var motion in new[] { MotionLevel.Full, MotionLevel.Off })
        {
            TestSupport.CreateContext(s => s.Motion = motion);
            var stamp = new InkStamp();
            var panel = new Panel { Width = 300, Height = 400, Children = { new CoverArt(), stamp }, DataContext = new object() };
            var host = new Window { Width = 400, Height = 500, Content = panel };
            host.Show();
            TestSupport.Settle(150);
            stamp.On = true;
            if (motion == MotionLevel.Full)
            {
                Assert.True(stamp.Progress < 1, "the stamp should be landing");
                Assert.True(TestSupport.Pump(() => stamp.Progress >= 1, 2000));
            }
            else Assert.Equal(1, stamp.Progress);
            // another game in the same place: shown at rest, no landing
            stamp.On = false;
            panel.DataContext = new object();
            stamp.On = true;
            Assert.Equal(1, stamp.Progress);
            host.Close();
        }
    });
}
