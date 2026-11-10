using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using RetroHub.App.Controls;
using RetroHub.App.Input;
using RetroHub.App.Localization;
using RetroHub.App.ViewModels;
using RetroHub.App.Views;
using RetroHub.Core.Config;
using RetroHub.Core.Emulation;
using RetroHub.Core.Library;
using RetroHub.App.Services;
using Xunit;

namespace RetroHub.App.Tests;

/// <summary>Regression tests for defects found in the review of the UI module.</summary>
public class ReviewRegressionTests
{
    private static (MainWindow Window, MainViewModel Vm) Open(int width = 1400, int height = 850)
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
    public Task Focus_ring_of_buttons_is_not_clipped() => UiTest.Run(() =>
    {
        var (window, vm) = Open();
        var play = Assert.IsType<Button>(Focused(window));
        Assert.Contains(":focus-visible", play.Classes);
        TestSupport.Settle(250);
        var frame = window.CaptureRenderedFrame()!;
        var p = play.TranslatePoint(new Point(play.Bounds.Width / 2, 0), window)!.Value;
        // the white ring of the primary button sits 2–4 px outside the button (before the 1.03 scale)
        var bright = Enumerable.Range(2, 6).Select(dy => Pixel(frame, (int)p.X, (int)p.Y - dy)).Max(c => Math.Min(c.R, Math.Min(c.G, c.B)));
        Assert.True(bright > 200, $"no focus ring above the focused button (brightest min channel {bright})");
        window.Close();
    });

    private static (byte R, byte G, byte B) Pixel(Bitmap bitmap, int x, int y)
    {
        var buffer = new byte[4];
        unsafe
        {
            fixed (byte* ptr = buffer)
                bitmap.CopyPixels(new PixelRect(x, y, 1, 1), (nint)ptr, 4, 4);
        }
        return (buffer[2], buffer[1], buffer[0]); // BGRA
    }

    [Fact]
    public Task View_toggle_moves_focus_into_the_list() => UiTest.Run(() =>
    {
        var (window, vm) = Open();
        vm.Navigator.Navigate(PageKey.Library);
        TestSupport.Pump(() => Focused(window) is GameTile);
        Assert.True(window.Execute(NavAction.Right));
        TestSupport.Settle(30);
        var item = Focused(window)!.DataContext;
        Assert.True(window.Execute(NavAction.View));
        TestSupport.Settle(150);
        var focused = Focused(window);
        Assert.True(focused is GameTile { IsEffectivelyVisible: true }, $"focus after the view toggle: {focused}");
        Assert.Same(item, focused!.DataContext);
        window.Close();
    });

    [Fact]
    public Task Localized_bindings_are_released_with_their_controls() => UiTest.Run(() =>
    {
        var window = new Window { Width = 400, Height = 300 };
        window.Show();
        var before = Loc.TextSubscriberCount;
        WeakReference AddAndRemove()
        {
            var panel = new StackPanel();
            window.Content = panel;
            for (var i = 0; i < 50; i++)
            {
                var tb = new TextBlock();
                tb.Bind(TextBlock.TextProperty, new TExtension("nav.home").ProvideValue(null!));
                panel.Children.Add(tb);
            }
            TestSupport.Settle(50);
            Assert.True(Loc.TextSubscriberCount >= before + 50);
            var weak = new WeakReference(panel.Children[0]);
            window.Content = null;
            return weak;
        }
        var removed = AddAndRemove();
        TestSupport.Settle(50);
        GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
        Assert.False(removed.IsAlive, "a removed TextBlock is kept alive by its language binding");
        Assert.True(Loc.TextSubscriberCount <= before, $"{Loc.TextSubscriberCount - before} language subscriptions leaked");
        window.Close();
    });

    [Fact]
    public Task Localized_bindings_survive_gc_and_follow_language_switches() => UiTest.Run(() =>
    {
        Loc.Load("de");
        var window = new Window { Width = 400, Height = 300 };
        var tb = new TextBlock();
        tb.Bind(TextBlock.TextProperty, new TExtension("nav.library") { Upper = true }.ProvideValue(null!));
        window.Content = tb;
        window.Show();
        TestSupport.Settle(30);
        Assert.Equal("BIBLIOTHEK", tb.Text);
        GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
        try
        {
            Loc.Load("en");
            Assert.Equal("LIBRARY", tb.Text);
        }
        finally { Loc.Load("de"); }
        Assert.Equal("BIBLIOTHEK", tb.Text);
        window.Close();
    });

    // ---------- launch results the real SessionService produces ----------

    /// <summary>A session service that behaves like the real one for a failed process start or a shell start.</summary>
    private sealed class ScriptedSessions(SessionState state, string? error) : ISessionService
    {
        public IGameSession? Current => null;
        public int Starts { get; private set; }
        public event Action<IGameSession>? SessionStarted { add { } remove { } }
        public event Action<IGameSession>? SessionEnded { add { } remove { } }

        public IGameSession Start(LaunchPlan plan)
        {
            Starts++;
            return new FinishedSession(plan, state, error);
        }
    }

    private sealed class FinishedSession(LaunchPlan plan, SessionState state, string? error) : IGameSession
    {
        public LaunchPlan Plan { get; } = plan;
        public SessionState State { get; } = state;
        public System.Diagnostics.Process? Process => null;
        public DateTime StartedUtc { get; } = DateTime.UtcNow;
        public TimeSpan Elapsed => TimeSpan.Zero;
        public int? ExitCode => null;
        public string? Error { get; } = error;
        public event Action<IGameSession>? Ended { add { } remove { } }
        public Task<bool> RequestCloseAsync(TimeSpan timeout) => Task.FromResult(true);
        public void Kill() { }
        public IReadOnlyList<nint> FindWindows() => [];
    }

    private static (MainWindow Window, MainViewModel Vm) OpenWith(ISessionService sessions)
    {
        var demo = TestSupport.CreateContext(s => s.Motion = MotionLevel.Off).Services;
        var services = new AppServices
        {
            Paths = demo.Paths, Settings = demo.Settings, Ui = demo.Ui, Catalog = demo.Catalog, Games = demo.Games,
            Scanner = demo.Scanner, Emulators = demo.Emulators, Launcher = demo.Launcher, Sessions = sessions, IsDemo = true,
        };
        var vm = new MainViewModel(new UiContext(services));
        var window = new MainWindow(vm) { Width = 1400, Height = 850 };
        window.Show();
        TestSupport.Pump(() => vm.CurrentPage is HomeViewModel { IsLoaded: true });
        return (window, vm);
    }

    [Fact]
    public Task Failed_process_start_is_reported_as_a_failed_start() => UiTest.Run(() =>
    {
        var sessions = new ScriptedSessions(SessionState.Failed, "Zugriff verweigert");
        var (window, vm) = OpenWith(sessions);
        vm.Ctx.Launch.MinimumTransition = TimeSpan.FromMilliseconds(50);
        var game = vm.Ctx.Items.Get(vm.Ctx.Services.Games.Query(new GameQuery { Text = "Golden Sun" }).Single());
        _ = vm.Ctx.Launch.LaunchAsync(game);
        Assert.True(TestSupport.Pump(() => vm.Dialogs.Current != null, 3000));
        Assert.Equal(1, sessions.Starts);
        Assert.Equal(Loc.T("launch.failed.title"), vm.Dialogs.Current!.Title);
        Assert.Contains("Zugriff verweigert", vm.Dialogs.Current.Message);
        Assert.Equal(LaunchPhase.Idle, vm.Launch.Phase);
        Assert.DoesNotContain(vm.Toasts.Items, t => t.Text == Loc.T("launch.ended"));
        vm.Dialogs.Current.Cancel();
        TestSupport.Pump(() => vm.Dialogs.Current == null);
        window.Close();
    });

    [Fact]
    public Task Shell_started_game_is_not_shown_as_a_finished_session() => UiTest.Run(() =>
    {
        var (window, vm) = OpenWith(new ScriptedSessions(SessionState.Exited, null));
        vm.Ctx.Launch.MinimumTransition = TimeSpan.FromMilliseconds(50);
        var game = vm.Ctx.Items.Get(vm.Ctx.Services.Games.Query(new GameQuery { Text = "Golden Sun" }).Single());
        _ = vm.Ctx.Launch.LaunchAsync(game);
        Assert.True(TestSupport.Pump(() => vm.Toasts.Items.Count > 0 && vm.Launch.Phase == LaunchPhase.Idle, 3000));
        Assert.Contains(vm.Toasts.Items, t => t.Text == Loc.T("launch.detached", game.Title));
        Assert.DoesNotContain(vm.Toasts.Items, t => t.Text == Loc.T("launch.ended"));
        Assert.Null(vm.Dialogs.Current);
        window.Close();
    });

    // ---------- cover images (the demo library has none, so this path was never exercised) ----------

    private static string WritePng(string dir, string name, int w, int h)
    {
        using var rtb = new RenderTargetBitmap(new PixelSize(w, h));
        using (var dc = rtb.CreateDrawingContext())
            dc.FillRectangle(Avalonia.Media.Brushes.OrangeRed, new Rect(0, 0, w, h));
        var file = Path.Combine(dir, name);
        rtb.Save(file);
        return file;
    }

    [Fact]
    public Task Cover_images_are_decoded_to_width_cached_and_failures_yield_null() => UiTest.Run(() =>
    {
        var dir = Path.Combine(Path.GetTempPath(), "retrohub-ui-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var png = WritePng(dir, "cover.png", 600, 840);
        var broken = Path.Combine(dir, "broken.png");
        File.WriteAllBytes(broken, [1, 2, 3, 4, 5, 6, 7, 8, 9]);
        var loader = new AsyncImageLoader(capacity: 16);

        var bmp = loader.LoadAsync(png, 256, CancellationToken.None).GetAwaiter().GetResult();
        Assert.NotNull(bmp);
        Assert.Equal(256, bmp!.PixelSize.Width);
        Assert.InRange(bmp.PixelSize.Height, 356, 360);
        Assert.Same(bmp, loader.TryGetCached(png, 256));
        Assert.Same(bmp, loader.LoadAsync(png, 256, CancellationToken.None).GetAwaiter().GetResult());
        Assert.Null(loader.LoadAsync(Path.Combine(dir, "missing.png"), 256, CancellationToken.None).GetAwaiter().GetResult());
        Assert.Null(loader.LoadAsync(broken, 256, CancellationToken.None).GetAwaiter().GetResult());
        Assert.True(File.Exists(png) && File.Exists(broken)); // never touches the files

        // a CoverArt shows the decoded image instead of the placeholder
        var art = new CoverArt { Width = 180, Height = 252, Title = "Test", Source = png };
        var window = new Window { Width = 300, Height = 300, Content = art };
        window.Show();
        Assert.True(TestSupport.Pump(() => art.HasImage, 4000), "cover image was not shown");
        window.Close();
        try { Directory.Delete(dir, true); } catch (IOException) { }
    });

    // ---------- "Beim Start scannen" ----------

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public Task Startup_scan_follows_the_setting(bool scanOnStartup) => UiTest.Run(() =>
    {
        var ctx = TestSupport.CreateContext(s => { s.Motion = MotionLevel.Off; s.ScanOnStartup = scanOnStartup; });
        var vm = new MainViewModel(ctx);
        var task = vm.StartBackgroundTasks();
        if (!scanOnStartup)
        {
            Assert.Null(task);
            return;
        }
        Assert.NotNull(task);
        var imports = (ImportsViewModel)vm.GetPage(PageKey.Imports);
        Assert.True(TestSupport.Pump(() => task!.IsCompleted, 10_000));
        Assert.True(imports.HasReport);
        Assert.False(imports.IsScanning);
        Assert.All(ctx.Services.Games.Folders().Where(f => f.Enabled), f => Assert.NotNull(f.LastScanUtc));
        // quiet: the demo scan finds nothing new → no toast
        Assert.DoesNotContain(vm.Toasts.Items, t => t.Text == Loc.T("imports.done"));
    });

    // ---------- modality of the dialog / launch overlays ----------

    private static bool IsInside(Visual? v, Visual root)
    {
        for (; v != null; v = Avalonia.VisualTree.VisualExtensions.GetVisualParent(v)) if (v == root) return true;
        return false;
    }

    [Fact]
    public Task Dialog_keeps_tab_focus_inside_and_restores_the_focus_when_closed() => UiTest.Run(() =>
    {
        var (window, vm) = Open();
        vm.Navigator.Navigate(PageKey.Library);
        TestSupport.Pump(() => Focused(window) is GameTile);
        var tile = Focused(window)!;
        var cube = vm.Ctx.Items.Get(vm.Ctx.Services.Games.Query(new GameQuery { Text = "Pikmin" }).Single());
        _ = vm.Ctx.Launch.LaunchAsync(cube); // blocked → problems dialog
        TestSupport.Pump(() => vm.Dialogs.Current != null);
        TestSupport.Settle(80);
        var panel = window.FindControl<Border>("DialogPanel")!;
        Assert.True(IsInside(Focused(window), panel), "dialog did not take the focus");
        for (var i = 0; i < 4; i++)
        {
            window.KeyPress(Key.Tab, RawInputModifiers.None, PhysicalKey.Tab, null);
            TestSupport.Settle(20);
            Assert.True(IsInside(Focused(window), panel), $"Tab moved the focus behind the modal dialog to {Focused(window)}");
        }
        window.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null);
        TestSupport.Pump(() => vm.Dialogs.Current == null);
        TestSupport.Settle(80);
        // the same game is focused again (the tile control itself may have been recycled by the grid meanwhile)
        Assert.True(Focused(window) is GameTile { IsEffectivelyVisible: true } t && ReferenceEquals(t.DataContext, tile.DataContext),
            $"focus after closing the dialog: {(Focused(window) as Control)?.DataContext ?? Focused(window)}");
        window.Close();
    });

    [Fact]
    public Task Focus_returns_to_the_page_after_a_game_ended() => UiTest.Run(() =>
    {
        var (window, vm) = Open();
        ((Services.Demo.DemoSessionService)vm.Ctx.Services.Sessions).AutoEndAfter = TimeSpan.FromMilliseconds(300);
        vm.Ctx.Launch.MinimumTransition = TimeSpan.FromMilliseconds(100);
        var game = vm.Ctx.Items.Get(vm.Ctx.Services.Games.Query(new GameQuery { Text = "Golden Sun" }).Single());
        vm.Navigator.Navigate(PageKey.GameDetail, game);
        TestSupport.Pump(() => vm.CurrentPage is GameDetailViewModel { CanPlay: true });
        TestSupport.Settle(80);
        Assert.Equal("PlayButton", Focused(window)?.Name);
        Assert.True(window.Execute(NavAction.Accept));
        Assert.True(TestSupport.Pump(() => vm.Launch.Phase == LaunchPhase.Running, 4000));
        TestSupport.Settle(50);
        // Golden Sun runs embedded (mGBA): the focus is on the game screen inside the overlay – never on "Beenden",
        // so a stray Enter cannot quit the game (the keys go to the emulator once it is ready)
        Assert.Equal("GameScreen", Focused(window)?.Name);
        Assert.True(IsInside(Focused(window), window.FindControl<Panel>("LaunchOverlay")!));
        Assert.True(TestSupport.Pump(() => vm.Launch.Phase == LaunchPhase.Idle, 6000));
        TestSupport.Settle(150);
        var focused = Focused(window);
        Assert.True(focused is { IsEffectivelyVisible: true } && IsInside(focused, window.FindControl<Grid>("Shell")!),
            $"focus after the game ended: {focused?.Name ?? focused?.ToString() ?? "none"}");
        window.Close();
    });

    [Fact]
    public Task Arrow_navigation_still_works_after_tab() => UiTest.Run(() =>
    {
        var (window, vm) = Open();
        var play = Focused(window)!;
        Assert.Equal("PlayButton", play.Name);
        window.KeyPress(Key.Tab, RawInputModifiers.None, PhysicalKey.Tab, null);
        TestSupport.Settle(30);
        var afterTab = Focused(window);
        play.Focus(NavigationMethod.Directional);
        TestSupport.Settle(30);
        window.KeyPress(Key.Down, RawInputModifiers.None, PhysicalKey.ArrowDown, null);
        TestSupport.Settle(60);
        Assert.True(Focused(window) is GameTile, $"after Tab ({afterTab}) the Down key focused {Focused(window)?.ToString() ?? "nothing"}");
        window.Close();
    });

    // ---------- debounce timers ----------

    private static bool TimerRunning(object owner, string field) =>
        ((Avalonia.Threading.DispatcherTimer)owner.GetType().GetField(field, System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
            .GetValue(owner)!).IsEnabled;

    [Fact]
    public Task Debounce_timers_do_not_run_before_anything_changed() => UiTest.Run(() =>
    {
        // Avalonia's DispatcherTimer(interval, priority, callback) constructor STARTS the timer, so the debounce
        // timers fired once by themselves: a spurious LibraryChanged ~150 ms after start reloaded the dashboard and
        // dropped the focus of the tile the user had just moved to; the library page reloaded a second time.
        var ctx = TestSupport.CreateContext(s => s.Motion = MotionLevel.Off);
        var library = new LibraryViewModel(ctx);
        Assert.False(TimerRunning(ctx, "_libraryDebounce"));
        Assert.False(TimerRunning(library, "_searchDebounce"));
        Assert.False(TimerRunning(ctx.Launch, "_clock"));

        // a change starts them
        library.SearchText = "Zelda";
        Assert.True(TimerRunning(library, "_searchDebounce"));
        ctx.Services.Games.SetFavorite(ctx.Services.Games.Query(new GameQuery()).First().Id, true);
        Dispatcher.UIThread.RunJobs();
        Assert.True(TimerRunning(ctx, "_libraryDebounce"));
    });

    /// <summary>Runs the real dispatcher loop (incl. DispatcherTimers, which RunJobs() does not drive) for <paramref name="ms"/>.</summary>
    private static void RunDispatcher(int ms)
    {
        var frame = new DispatcherFrame();
        _ = Task.Delay(ms).ContinueWith(_ => Dispatcher.UIThread.Post(() => frame.Continue = false));
        Dispatcher.UIThread.PushFrame(frame);
    }

    [Fact]
    public Task Library_changes_and_search_are_debounced_without_spurious_reloads() => UiTest.Run(() =>
    {
        var ctx = TestSupport.CreateContext(s => s.Motion = MotionLevel.Off);
        var changes = 0;
        ctx.LibraryChanged += () => changes++;
        var library = new LibraryViewModel(ctx);
        var loads = 0;
        library.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(LibraryViewModel.Items)) loads++; };
        RunDispatcher(500);
        Assert.Equal(0, changes); // nothing changed → no LibraryChanged
        Assert.Equal(0, loads);   // nothing typed → no library load

        var repo = ctx.Services.Games;
        repo.SetFavorite(repo.Query(new GameQuery()).First().Id, true);
        repo.SetFavorite(repo.Query(new GameQuery()).Last().Id, true);
        RunDispatcher(600);
        Assert.Equal(1, changes); // two writes → one (debounced) refresh

        library.SearchText = "Zel";
        library.SearchText = "Zelda";
        RunDispatcher(600);
        Assert.Equal(1, loads);
        Assert.All(library.Items, i => Assert.Contains("Zelda", i.Title));
    });

    [Fact]
    public Task Library_refresh_keeps_the_focus_on_home_and_systems() => UiTest.Run(() =>
    {
        // e.g. during the start-up scan the repository reports changes again and again; the visible shelves did not
        // change, so the focused tile/card must stay focused (the controller user must not lose the position)
        var (window, vm) = Open();
        window.KeyPress(Key.Down, RawInputModifiers.None, PhysicalKey.ArrowDown, null);
        window.KeyPress(Key.Right, RawInputModifiers.None, PhysicalKey.ArrowRight, null);
        TestSupport.Settle(60);
        var tile = Assert.IsType<GameTile>(Focused(window));
        var repo = vm.Ctx.Services.Games;
        repo.SetHidden(repo.Query(new GameQuery { Text = "Tetris" }).First().Id, false); // a write that changes nothing visible
        RunDispatcher(700);
        Assert.Same(tile, Focused(window));

        vm.Navigator.Navigate(PageKey.Systems);
        TestSupport.Pump(() => Focused(window) is RetroHub.App.Controls.SystemSpine);
        window.Execute(NavAction.Right);
        TestSupport.Settle(40);
        var card = Assert.IsType<RetroHub.App.Controls.SystemSpine>(Focused(window));
        repo.SetHidden(repo.Query(new GameQuery { Text = "Tetris" }).First().Id, false);
        RunDispatcher(700);
        Assert.Same(card, Focused(window));
        window.Close();
    });
}
