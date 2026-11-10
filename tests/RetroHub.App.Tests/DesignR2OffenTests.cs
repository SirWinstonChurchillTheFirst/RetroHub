using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.VisualTree;
using RetroHub.App.Controls;
using RetroHub.App.Input;
using RetroHub.App.ViewModels;
using RetroHub.App.Views;
using RetroHub.Core.Config;
using Xunit;

namespace RetroHub.App.Tests;

/// <summary>
/// R2 "offen": the last open items of the design round – the dithered room light, and the finished pages (each test
/// names the DESIGN section it guards).
/// </summary>
public class DesignR2OffenTests
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
    public void Room_light_scrims_and_glow_are_dithered_not_banded()
    {
        // DESIGN 2.5: at 1920 the horizontal scrim falls from 94 % to 52 % over 1150 px – 107 alpha steps; drawn as a
        // gradient every step was a 10 px band. The float render adds ±½ LSB of noise: no long runs of one value.
        var key = new RoomBackdrop.LightKey(1920, 1088, 640, Color.Parse("#D8A060").ToUInt32(), 160, 160, 380, 1.0);
        var px = RoomBackdrop.LightPixels(key, out var w, out var h);
        Assert.Equal(1920, w);
        Assert.Equal(1088, h);
        var y = 300;
        var blue = Enumerable.Range(100, 1000).Select(x => px[(y * w + x) * 4]).ToList();
        var alpha = Enumerable.Range(100, 1000).Select(x => px[(y * w + x) * 4 + 3]).ToList();
        // plain rounding changes the value only at the ~6 band edges of this row; the dither flips it hundreds of times
        var changes = Enumerable.Range(1, blue.Count - 1).Count(i => blue[i] != blue[i - 1]);
        Assert.True(changes > 150, $"value changes along 1000 px: {changes}");
        // the mean still follows the scrim: darker (more room) at the left than at 60 % of the width
        double Mean(int x0) => Enumerable.Range(x0, 64).Average(x => px[(y * w + x) * 4 + 3]);
        Assert.True(Mean(0) > Mean(1200) + 60, $"alpha left {Mean(0):0} vs. right {Mean(1200):0}");
        // premultiplied: no colour above its alpha
        for (var i = 0; i < px.Length; i += 4)
            Assert.True(px[i] <= px[i + 3] && px[i + 1] <= px[i + 3] && px[i + 2] <= px[i + 3]);
        // the glow lifts the stage around its centre (x 0.16, y 0.38), and nothing below the board
        var glowAt = px[((int)(0.38 * 1088) * w + (int)(0.16 * 1920)) * 4 + 2];
        var noGlow = RoomBackdrop.LightPixels(key with { Glow = 0, GlowStrength = 0 }, out _, out _);
        Assert.True(glowAt > noGlow[((int)(0.38 * 1088) * w + (int)(0.16 * 1920)) * 4 + 2] + 10);
        Assert.Equal(alpha.Count, blue.Count);
        var below = (700 * w + 960) * 4;
        Assert.Equal(255, px[below + 3]);
    }

    [Theory]
    [InlineData(1920, 1080)]
    [InlineData(1100, 700)]
    public Task Few_games_end_in_a_note_beside_the_row_not_in_the_footer_fade(int width, int height) => UiTest.Run(() =>
    {
        // DESIGN 7.5.4 (R2): one row of games – the note and "Importe öffnen" stand in the compartment right of the last
        // box, inside the window above the footer (at ≈ 1100 they used to lie in the fade below the row)
        var (window, vm) = Open(width, height);
        var stats = vm.Ctx.Services.Games.Stats();
        var few = vm.Ctx.Services.Catalog.Systems.Where(s => stats.GamesPerSystem.GetValueOrDefault(s.Id) is > 0 and <= 3)
            .OrderBy(s => stats.GamesPerSystem.GetValueOrDefault(s.Id)).First();
        vm.Navigator.Navigate(PageKey.System, few.Id);
        Assert.True(TestSupport.Pump(() => vm.CurrentPage is SystemViewModel { IsLoaded: true, IsFewGames: true } s && s.System?.Id == few.Id, 4000));
        Assert.True(TestSupport.Pump(() => Focused(window) is GameTile, 3000));
        TestSupport.Settle(300);
        var page = window.GetVisualDescendants().OfType<Views.Pages.SystemPage>().Single();
        Assert.True(page.EndBeside, "the ending should stand beside the row");
        var button = page.FindControl<Button>("ImportsButton")!;
        var note = page.FindControl<PaperNote>("FewNote")!;
        var grid = page.FindControl<CoverGrid>("Covers")!;
        var last = grid.TileForIndex(grid.ItemCount - 1)!;
        // window coordinates throughout (the shell is scaled by UiScale at ≈ 1100)
        Point At(Visual v, double x, double y) => v.TranslatePoint(new Point(x, y), window)!.Value;
        var b = At(button, 0, button.Bounds.Height);
        var n = At(note, 0, 0);
        var lastRight = At(last, last.Bounds.Width, 0);
        var board = At(last, 0, grid.TileWidth * grid.CoverAspect);
        var hints = window.FindControl<Border>("HintBar")!;
        var fadeTop = At(hints, 0, 0).Y - (At(hints, 0, hints.Bounds.Height).Y - At(hints, 0, 0).Y);
        var info = $"button foot {b}, note {n}, last tile right {lastRight}, board {board.Y}, fade top {fadeTop}";
        Assert.True(button.IsEffectivelyVisible && note.IsEffectivelyVisible, info);
        Assert.True(n.X > lastRight.X, info);                 // right of the last box
        Assert.True(n.Y >= lastRight.Y - 2, info);            // not above the row
        Assert.True(b.Y <= board.Y + 1, info);                // stands on the board
        Assert.True(b.Y <= fadeTop, info);                    // clear of the footer and its fade
        // really there (not clipped away by the grid's footer host): the mouse finds the button
        var centre = At(button, button.Bounds.Width / 2, button.Bounds.Height / 2);
        var hit = window.InputHitTest(centre) as Visual;
        Assert.True(hit != null && (hit == button || button.IsVisualAncestorOf(hit)), $"hit {hit?.GetType().Name} at {centre}; {info}");
        // right from the last box reaches the button, left goes back
        grid.FocusIndex(grid.ItemCount - 1);
        TestSupport.Settle(60);
        window.Execute(NavAction.Right);
        Assert.Equal("ImportsButton", Focused(window)?.Name);
        window.Execute(NavAction.Left);
        Assert.True(TestSupport.Pump(() => Focused(window) is GameTile, 1000));
        window.Close();
    });

    [Theory]
    [InlineData(1920, 1080)]
    [InlineData(1100, 700)]
    public Task Detail_back_and_choice_list_rest_on_a_scroll_stop(int width, int height) => UiTest.Run(() =>
    {
        // DESIGN 7.3 (R2): Down from the stage turns to the back at the board stop when the back does not fit under the
        // stage; the emulator choice folds open on a stop too – never an offset that cuts the title under the header
        var (window, vm) = Open(width, height);
        var game = vm.Ctx.Items.Get(vm.Ctx.Services.Games.Query(new RetroHub.Core.Library.GameQuery()).First());
        vm.Navigator.Navigate(PageKey.GameDetail, game);
        Assert.True(TestSupport.Pump(() => vm.CurrentPage is GameDetailViewModel { Game: not null } d && d.Emulators.Count > 0, 6000));
        TestSupport.Settle(300);
        var page = window.GetVisualDescendants().OfType<Views.Pages.GameDetailPage>().Single();
        var scroll = page.FindControl<ScrollViewer>("Scroll")!;
        bool OnStop()
        {
            var y = scroll.Offset.Y;
            return y < 0.5 || Math.Abs(y - page.ActionsStop) < 1 || y >= page.BoardStop - 1;
        }
        for (var i = 0; i < 6 && Focused(window)?.Name != "EmulatorBox"; i++)
        {
            window.Execute(i == 0 ? NavAction.Down : NavAction.Right);
            TestSupport.Settle(120);
            Assert.True(OnStop(), $"offset {scroll.Offset.Y} (actions {page.ActionsStop}, board {page.BoardStop}) after a move to {Focused(window)?.Name}");
        }
        Assert.Equal("EmulatorBox", Focused(window)?.Name);
        window.Execute(NavAction.Accept);
        TestSupport.Settle(400);
        Assert.True(OnStop(), $"choice open: offset {scroll.Offset.Y} (actions {page.ActionsStop}, board {page.BoardStop})");
        window.Close();
    });

    [Fact]
    public void Wide_windows_get_a_capped_light_bitmap()
    {
        var key = new RoomBackdrop.LightKey(3840, 2176, 1300, 0, 0, 500, 400, 1.0);
        RoomBackdrop.LightPixels(key, out var w, out var h);
        Assert.Equal(2560, w);
        Assert.InRange(h, 1440, 1460);
    }

    [Fact]
    public void Softened_picture_is_upsampled_and_dithered()
    {
        // a flat field of 20.4 (between two 8-bit values): plain rounding gives one value everywhere, the dither mixes
        // 20 and 21 at about 40/60 – the mean keeps the fraction
        var field = new float[64 * 36 * 4];
        for (var i = 0; i < field.Length; i++) field[i] = (i & 3) == 3 ? 255 : 20.4f;
        var px = RoomBackdrop.Upsample(field);
        Assert.Equal(256 * 144 * 4, px.Length);
        var blues = Enumerable.Range(0, 256 * 144).Select(i => (int)px[i * 4]).ToList();
        Assert.All(blues, b => Assert.InRange(b, 20, 21));
        Assert.InRange(blues.Average(), 20.3, 20.5);
    }
}
