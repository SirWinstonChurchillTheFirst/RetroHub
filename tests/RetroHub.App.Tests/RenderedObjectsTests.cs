using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
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
/// R3 "gezeichnet, nicht gecodet" (owner feedback on fd82d98): the Konsolen page and the shelves are drawn from rendered
/// objects (Blender, Assets/Rendered) – every object the app asks for exists at 1x and 2x, one render takes every system
/// colour through its light maps, the wall, the front, the sleeves and Start's spines pick their renders, and the asset
/// budget holds.
/// </summary>
public class RenderedObjectsTests
{
    /// <summary>
    /// Closes after letting the library load, the settings save and the cover decodes this window set off finish inside
    /// the test (a pool-thread continuation reaching the dispatcher after the session ends failed the NEXT test).
    /// </summary>
    private static void Close(Window window)
    {
        TestSupport.Settle(1500);
        window.Close();
        TestSupport.Settle(300);
    }

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

    private static readonly string[] Objects =
    [
        "spine-l0", "spine-l1", "spine-l2", "spine-s0", "spine-s1", "spine-s2", "spine-m0", "spine-m1", "spine-m2",
        "spine-xs0", "spine-xs1", "spine-xs2", "front-l", "front-s", "front-m", "front-xs",
        "sleeve-l0", "sleeve-l1", "sleeve-l2", "sleeve-l3", "sleeve-llean",
        "sleeve-s0", "sleeve-s1", "sleeve-s2", "sleeve-s3", "sleeve-slean",
        "bookend", "case", "tv",
    ];

    [Fact]
    public Task Every_object_the_app_draws_has_its_render_at_1x_and_2x() => UiTest.Run(() =>
    {
        Assert.True(Rendered.Available);
        var missing = new List<string>();
        foreach (var name in Objects.Concat(Enum.GetValues<Motif>().Select(m => "motif-" + m)))
        {
            if (Rendered.MetaOf(name) is not { Front.Width: > 0 }) { missing.Add(name + ".json"); continue; }
            if (Rendered.Composite(name, 1, true) == null) missing.Add(name + "@1x");
            if (Rendered.Composite(name, 2, true) == null) missing.Add(name + "@2x");
        }
        foreach (var part in new[] { "rail", "tag-l", "tag-s", "reiter", "sign" })
            if (Rendered.Strip(part, 300, 2).Bitmap == null) missing.Add(part + " strip");
        Assert.True(missing.Count == 0, string.Join(", ", missing));
    });

    /// <summary>Premultiplied BGRA of the pixel at (x, y) of <paramref name="bmp"/>.</summary>
    private static (byte B, byte G, byte R, byte A) Pixel(Bitmap bmp, int x, int y)
    {
        var buf = new byte[4];
        var h = System.Runtime.InteropServices.GCHandle.Alloc(buf, System.Runtime.InteropServices.GCHandleType.Pinned);
        try { bmp.CopyPixels(new PixelRect(x, y, 1, 1), h.AddrOfPinnedObject(), 4, 4); }
        finally { h.Free(); }
        return (buf[0], buf[1], buf[2], buf[3]);
    }

    [Fact]
    public Task One_render_takes_every_system_colour_through_its_light_map() => UiTest.Run(() =>
    {
        var m = Rendered.MetaOf("spine-l0")!;
        var band = m.Rect("band");
        var x = (int)Math.Round((band.X + band.Width / 2) * 2);
        var y = (int)Math.Round((band.Y + band.Height / 2) * 2);
        var red = Rendered.Composite("spine-l0", 2, true, new Rendered.Tint("band", Color.Parse("#E02020")))!;
        var blue = Rendered.Composite("spine-l0", 2, true, new Rendered.Tint("band", Color.Parse("#2040E0")))!;
        var bare = Rendered.Composite("spine-l0", 2, true)!;
        var (rb, _, rr, _) = Pixel(red, x, y);
        var (bb, _, br, _) = Pixel(blue, x, y);
        var (nb, ng, nr, _) = Pixel(bare, x, y);
        Assert.True(rr > rb + 40, $"red band: R {rr} B {rb}");
        Assert.True(bb > br + 40, $"blue band: R {br} B {bb}");
        // untinted, the band was rendered at albedo 0: only sheen, no colour of its own
        Assert.True(Math.Max(nr, Math.Max(ng, nb)) - Math.Min(nr, Math.Min(ng, nb)) < 24, $"bare band {nr}/{ng}/{nb}");
        // the composite is cached: the same tint gives the same bitmap
        Assert.Same(red, Rendered.Composite("spine-l0", 2, true, new Rendered.Tint("band", Color.Parse("#E02020"))));
    });

    [Fact]
    public Task The_Konsolen_wall_its_front_and_the_sleeves_stand_as_renders() => UiTest.Run(() =>
    {
        foreach (var (w, h, spine, front, sleeve) in new[] { (1920, 1080, "l", "front-l", "l"), (1100, 700, "s", "front-s", "s") })
        {
            var (window, vm) = Open(w, h);
            vm.Navigator.Navigate(PageKey.Systems);
            Assert.True(TestSupport.Pump(() => window.FocusManager?.GetFocusedElement() is SystemSpine { IsOpen: true }, 4000));
            TestSupport.Settle(150);
            var wall = window.GetVisualDescendants().OfType<SystemSpine>().Where(s => s.ShowMotif && s.IsEffectivelyVisible).ToList();
            Assert.NotEmpty(wall);
            Assert.All(wall, s => Assert.Equal(spine, s.SpineSprite));
            Assert.Contains(wall, s => s.IsOpen && s.FrontSprite == front);
            Close(window);
        }

        // the "Noch leer" row: one of four upright cases or the leaning one – never the drawn card, none lies flat (R3 QA 3)
        var (win, vm2) = Open(1920, 1080);
        vm2.Navigator.Navigate(PageKey.Systems);
        Assert.True(TestSupport.Pump(() => win.FocusManager?.GetFocusedElement() is SystemSpine, 4000));
        var page = (SystemsViewModel)vm2.CurrentPage!;
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
        Assert.All(sleeves, s => Assert.StartsWith("sleeve-l", s.SleeveSpriteName));
        Assert.All(sleeves, s => Assert.NotEqual("sleeve-lflat", s.SleeveSpriteName));
        // a rendered lean looks alike wherever it stands: never two near each other
        var leans = sleeves.Select((s, i) => (s, i)).Where(t => t.s.SleeveSpriteName == "sleeve-llean").Select(t => t.i).ToList();
        Assert.All(leans.Zip(leans.Skip(1)), p => Assert.True(p.Second - p.First >= 4, $"leans at {p.First} and {p.Second}"));
        host.Close();
        Close(win);
    });

    [Fact]
    public Task Start_spines_and_their_front_are_renders_too() => UiTest.Run(() =>
    {
        foreach (var (w, h, size) in new[] { (1920, 1080, "m"), (1100, 700, "xs") })
        {
            var (window, _) = Open(w, h);
            var spines = window.GetVisualDescendants().OfType<SystemSpine>().Where(s => s.Opens && !s.ShowMotif && s.Bounds.Height > 0).ToList();
            Assert.NotEmpty(spines);
            Assert.All(spines, s => Assert.Equal(size, s.SpineSprite));
            Assert.All(spines, s => Assert.Equal(size == "m" ? "front-m" : "front-xs", s.FrontSprite));
            Close(window);
        }
    });

    [Fact]
    public void The_rendered_assets_stay_within_their_budget()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, "src", "RetroHub.App", "Assets", "Rendered"))) dir = dir.Parent;
        Assert.SkipWhen(dir == null, "source tree not found next to the test binaries");
        var files = new DirectoryInfo(Path.Combine(dir!.FullName, "src", "RetroHub.App", "Assets", "Rendered")).GetFiles("*", SearchOption.AllDirectories);
        var bytes = files.Sum(f => f.Length);
        Assert.True(bytes < 15L * 1024 * 1024, $"{bytes / 1048576.0:F2} MB in {files.Length} files");
    }
}
