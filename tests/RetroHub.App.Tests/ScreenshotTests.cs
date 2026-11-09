using Avalonia.Headless;
using RetroHub.App.Controls;
using RetroHub.App.ViewModels;
using RetroHub.App.Views;
using Xunit;

namespace RetroHub.App.Tests;

/// <summary>
/// Renders the shell with the demo services (real Skia rendering) and writes PNGs to artifacts/screens/ for visual review.
/// The assertions only check that a frame was produced; the design review is done by looking at the images.
/// </summary>
public class ScreenshotTests
{
    private static (MainWindow Window, MainViewModel Vm) Open(int width, int height)
    {
        var ctx = TestSupport.CreateContext();
        var vm = new MainViewModel(ctx);
        var window = new MainWindow(vm) { Width = width, Height = height };
        window.Show();
        TestSupport.Pump(() => vm.CurrentPage is HomeViewModel { IsLoaded: true });
        TestSupport.Settle(600);
        return (window, vm);
    }

    private static string Save(MainWindow window, string name)
    {
        TestSupport.Settle(450);
        var frame = window.CaptureRenderedFrame();
        Assert.NotNull(frame);
        var file = Path.Combine(TestSupport.ScreensDir, name + ".png");
        frame!.Save(file, new Avalonia.Media.Imaging.PngBitmapEncoderOptions());
        Assert.True(new FileInfo(file).Length > 10_000, "screenshot looks empty");
        return file;
    }

    [Theory]
    [InlineData(1600, 900, "")]
    [InlineData(1100, 700, "-narrow")]
    public Task Render_main_pages(int width, int height, string suffix) => UiTest.Run(() =>
    {
        var (window, vm) = Open(width, height);
        // narrow windows scale the shell (UiScale 0.8 → 1375 px logical): the header gets compact (no controller name)
        Assert.Equal(width / window.UiScale < MainViewModel.CompactBelow, vm.IsSidebarCollapsed);
        Save(window, "home" + suffix);

        vm.Navigator.Navigate(PageKey.Library);
        TestSupport.Pump(() => vm.CurrentPage is LibraryViewModel { IsLoaded: true });
        Save(window, "library" + suffix);

        vm.Navigator.Navigate(PageKey.Systems);
        TestSupport.Pump(() => vm.CurrentPage is SystemsViewModel { Systems.Count: > 0 });
        Save(window, "systems" + suffix);

        var game = vm.Ctx.Items.Get(vm.Ctx.Services.Games.Query(new() { Text = "Smaragd" }).Single());
        vm.Navigator.Navigate(PageKey.GameDetail, game);
        TestSupport.Pump(() => vm.CurrentPage is GameDetailViewModel { Emulators.Count: > 0 });
        Save(window, "detail" + suffix);

        vm.Navigator.Navigate(PageKey.Settings);
        TestSupport.Settle(300);
        Save(window, "settings" + suffix);
        window.Close();
    });

    [Fact]
    public Task Render_secondary_pages() => UiTest.Run(() =>
    {
        var (window, vm) = Open(1600, 900);

        vm.Navigator.Navigate(PageKey.System, "gba");
        TestSupport.Pump(() => vm.CurrentPage is SystemViewModel { Games.Count: > 0 });
        Save(window, "system-gba");

        vm.Navigator.Navigate(PageKey.Emulators);
        TestSupport.Pump(() => vm.CurrentPage is EmulatorsViewModel { Items.Count: > 0 });
        Save(window, "emulators");

        vm.Navigator.Navigate(PageKey.Imports);
        var imports = (ImportsViewModel)vm.CurrentPage!;
        imports.ScanCommand.Execute(null);
        TestSupport.Pump(() => imports.HasReport, 10_000);
        Save(window, "imports");

        // a game whose system has no installed emulator → honest problem list
        var cube = vm.Ctx.Items.Get(vm.Ctx.Services.Games.Query(new() { Text = "Metroid Prime" }).Single());
        vm.Navigator.Navigate(PageKey.GameDetail, cube);
        TestSupport.Pump(() => vm.CurrentPage is GameDetailViewModel { HasProblems: true });
        Save(window, "detail-problems");

        vm.Navigator.Navigate(PageKey.Library);
        TestSupport.Pump(() => vm.CurrentPage is LibraryViewModel { IsLoaded: true });
        ((LibraryViewModel)vm.CurrentPage!).IsListView = true;
        TestSupport.Settle(300);
        Save(window, "library-list");

        // launch transition + running state (embedded → GameScreenHost placeholder)
        ((Services.Demo.DemoSessionService)vm.Ctx.Services.Sessions).AutoEndAfter = TimeSpan.FromSeconds(30);
        vm.Ctx.Launch.MinimumTransition = TimeSpan.FromSeconds(2);
        var gs = vm.Ctx.Items.Get(vm.Ctx.Services.Games.Query(new() { Text = "Golden Sun" }).Single());
        _ = vm.Ctx.Launch.LaunchAsync(gs);
        TestSupport.Pump(() => vm.Launch.Phase == LaunchPhase.Starting);
        Save(window, "launch-starting");
        TestSupport.Pump(() => vm.Launch.Phase == LaunchPhase.Running, 5000);
        Save(window, "launch-running");
        vm.Launch.StopCommand.Execute(null);
        TestSupport.Pump(() => vm.Launch.Phase == LaunchPhase.Idle, 5000);
        window.Close();
    });
}
