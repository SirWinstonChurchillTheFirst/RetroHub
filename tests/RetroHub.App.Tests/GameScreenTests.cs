using Avalonia.Controls;
using RetroHub.App.Controls;
using RetroHub.App.Input;
using RetroHub.App.Localization;
using RetroHub.App.Services;
using RetroHub.App.ViewModels;
using RetroHub.App.Views;
using RetroHub.Core;
using RetroHub.Core.Catalog;
using RetroHub.Core.Config;
using RetroHub.Core.Emulation;
using RetroHub.Core.Library;
using Xunit;

namespace RetroHub.App.Tests;

/// <summary>The pure pieces of the embedded game screen: fit math, bar sampling, readiness rules, window search, auto-hide.</summary>
public class GameScreenLogicTests
{
    [Fact]
    public void Wide_area_is_narrowed_to_the_picture()
    {
        // GBA 3:2 in 1600x800 → height limits: 1200x800
        var fit = GameScreenFit.Compute(1600, 800, 1, 1.5);
        Assert.Equal(1200, fit.Width);
        Assert.Equal(800, fit.Height);
        Assert.Equal((1200, 800), (fit.PixelWidth, fit.PixelHeight));
    }

    [Fact]
    public void Tall_area_is_cut_in_height()
    {
        // NES 4:3 in 800x900 → width limits: 800x600
        var fit = GameScreenFit.Compute(800, 900, 1, 4.0 / 3);
        Assert.Equal(800, fit.Width);
        Assert.Equal(600, fit.Height);
    }

    [Fact]
    public void Emulator_bars_are_added_to_the_picture_height()
    {
        // mGBA: 21 px menu bar at 100 %: the picture keeps 3:2, the host is 21 px higher
        var fit = GameScreenFit.Compute(1556, 792, 1, 1.5, barTopPx: 21);
        Assert.Equal(792, fit.Height);
        Assert.Equal(Math.Floor((792 - 21) * 1.5), fit.Width);
        Assert.Equal(1.5, fit.Width / (fit.Height - 21), 2);
    }

    [Fact]
    public void Scaling_converts_bars_and_the_result_to_physical_pixels()
    {
        // 150 %: bars are physical pixels (32 px = 21.33 DIP); the host is reported in DIPs and in pixels
        var fit = GameScreenFit.Compute(1000, 600, 1.5, 1.5, barTopPx: 32);
        double pictureH = 600 - 32 / 1.5;
        Assert.Equal(Math.Floor(pictureH * 1.5), fit.Width);
        Assert.Equal(600, fit.Height);
        Assert.Equal((int)Math.Round(fit.Width * 1.5), fit.PixelWidth);
        Assert.Equal(900, fit.PixelHeight);
    }

    [Fact]
    public void No_aspect_or_a_tiny_area_fills_the_area()
    {
        Assert.Equal(new HostFit(640, 480, 640, 480), GameScreenFit.Compute(640, 480, 1, null));
        var tiny = GameScreenFit.Compute(30, 20, 1, 1.5);
        Assert.Equal((30.0, 20.0), (tiny.Width, tiny.Height));
        Assert.True(GameScreenFit.Compute(0, 300, 1, 1.5).IsEmpty);
        Assert.True(GameScreenFit.Compute(double.NaN, 300, 1, 1.5).IsEmpty);
        // a broken scale counts as 1
        Assert.Equal(GameScreenFit.Compute(900, 600, 1, 1.5), GameScreenFit.Compute(900, 600, 0, 1.5));
    }

    [Fact]
    public void Fit_never_exceeds_the_area_and_keeps_the_aspect()
    {
        var rnd = new Random(7);
        for (int i = 0; i < 500; i++)
        {
            double w = rnd.Next(40, 4000), h = rnd.Next(40, 3000), scale = 1 + rnd.Next(0, 5) * 0.25, aspect = 0.5 + rnd.NextDouble() * 1.5;
            int bars = rnd.Next(0, 40);
            var fit = GameScreenFit.Compute(w, h, scale, aspect, bars, 0);
            Assert.True(fit.Width <= w && fit.Height <= h, $"{fit} in {w}x{h}");
            if (h > bars / scale + GameScreenFit.MinPicture && w > GameScreenFit.MinPicture)
                Assert.InRange(fit.Width / (fit.Height - bars / scale), aspect * 0.97, aspect * 1.03);
        }
    }

    [Fact]
    public void Small_changes_are_not_worth_a_new_layout()
    {
        Assert.False(GameScreenFit.Differs(100, 101.5));
        Assert.True(GameScreenFit.Differs(100, 102));
        Assert.True(GameScreenFit.Differs(double.NaN, 100));
        Assert.False(GameScreenFit.Differs(double.NaN, double.NaN));
        Assert.Equal((150, 75), GameScreenFit.ToPixels(100, 50, 1.5));
    }

    [Fact]
    public void Toast_cutout_is_clipped_to_the_host_scaled_and_padded()
    {
        // toast 380x100 DIPs whose top-left lies 900,700 DIPs from the host's corner; host 1156x792 DIPs at 100 %
        Assert.Equal(new PixelHole(892, 692, 1156, 792), GameScreenCutout.Compute(900, 700, 380, 100, 1156, 792, 1));
        // 150 %: everything in physical pixels
        Assert.Equal(new PixelHole(1342, 1042, 1734, 1188), GameScreenCutout.Compute(900, 700, 380, 100, 1156, 792, 1.5));
        // fully inside
        Assert.Equal(new PixelHole(92, 92, 318, 218), GameScreenCutout.Compute(100, 100, 210, 110, 1156, 792, 1));
        // no toasts (height 0) or outside the host: no hole
        Assert.Null(GameScreenCutout.Compute(900, 700, 380, 0, 1156, 792, 1));
        Assert.Null(GameScreenCutout.Compute(1200, 700, 380, 100, 1156, 792, 1));
        Assert.Null(GameScreenCutout.Compute(-500, 100, 400, 100, 1156, 792, 1));
        Assert.Null(GameScreenCutout.Compute(double.NaN, 0, 10, 10, 1156, 792, 1));
        Assert.Null(GameScreenCutout.Compute(0, 0, 10, 10, 0, 0, 1));
    }

    [Fact]
    public void Bars_are_taken_only_when_two_samples_agree()
    {
        var bars = new BarSampler();
        Assert.False(bars.Add((21, 0)));
        Assert.Null(bars.Current);
        Assert.True(bars.Add((21, 0)));
        Assert.Equal((21, 0), bars.Current);
        // a half-way value right after a resize is ignored
        Assert.False(bars.Add((40, 0)));
        Assert.Equal((21, 0), bars.Current);
        Assert.False(bars.Add((21, 0)));
        Assert.Equal((21, 0), bars.Current);
        Assert.False(bars.Add((25, 3)));
        Assert.True(bars.Add((25, 3)));
        Assert.Equal((25, 3), bars.Current);
        bars.Reset();
        Assert.Null(bars.Current);
    }

    private static TimeSpan S(double seconds) => TimeSpan.FromSeconds(seconds);

    [Fact]
    public void Ready_after_five_answers_in_a_row_and_more_than_one_and_a_half_seconds()
    {
        var r = new EmbedReadiness(null);
        Assert.False(r.Update(S(1), true, "x")); // nothing taken yet
        r.Taken(S(10));
        // five answers quickly: still too early
        for (int i = 1; i <= 5; i++) Assert.False(r.Update(S(10 + i * 0.15), true, "mGBA"));
        Assert.Equal(5, r.Answers);
        // a missed answer starts the count again
        Assert.False(r.Update(S(11.0), false, "mGBA"));
        Assert.Equal(0, r.Answers);
        for (int i = 1; i <= 4; i++) Assert.False(r.Update(S(11.0 + i * 0.15), true, "mGBA"));
        Assert.True(r.Update(S(11.75), true, "mGBA"));
        Assert.Equal("answers", r.Reason);
        Assert.True(r.IsReady);
        Assert.True(r.Update(S(12), false, "mGBA")); // stays ready
    }

    [Fact]
    public void Title_profile_waits_for_the_title_even_when_the_emulator_answers()
    {
        // Azahar answers long before the game runs; its title names the game once it does
        var r = new EmbedReadiness(" | ");
        r.Taken(S(0));
        for (int i = 1; i <= 40; i++) Assert.False(r.Update(S(i * 0.15), true, "Azahar 2126.1.2"));
        Assert.True(r.Update(S(6.2), false, "Azahar 2126.1.2 | Pokémon Y"));
        Assert.Equal("title", r.Reason);
    }

    [Fact]
    public void Readiness_is_capped_at_thirty_seconds_after_the_take()
    {
        var r = new EmbedReadiness(" | ");
        r.Taken(S(5));
        Assert.False(r.Update(S(34.9), false, "Azahar"));
        Assert.True(r.Update(S(35.1), false, "Azahar"));
        Assert.Equal("timeout", r.Reason);
    }

    [Fact]
    public void A_new_take_restarts_the_count()
    {
        var r = new EmbedReadiness(null);
        r.Taken(S(0));
        for (int i = 1; i <= 4; i++) r.Update(S(i * 0.2), true, "");
        r.Taken(S(1)); // the emulator built a new window
        Assert.Equal(0, r.Answers);
        for (int i = 1; i <= 4; i++) Assert.False(r.Update(S(1 + i * 0.4), true, ""));
        Assert.True(r.Update(S(3), true, ""));
    }

    [Fact]
    public void Window_search_takes_only_into_a_real_host_and_gives_up_after_sixty_seconds()
    {
        Assert.Equal(WindowSearchStep.Take, WindowSearch.Decide(S(1), false, true, 800, 600));
        Assert.Equal(WindowSearchStep.Wait, WindowSearch.Decide(S(1), false, true, 63, 600)); // host not laid out yet
        Assert.Equal(WindowSearchStep.Wait, WindowSearch.Decide(S(1), false, false, 800, 600));
        Assert.Equal(WindowSearchStep.Wait, WindowSearch.Decide(S(59), false, false, 800, 600));
        Assert.Equal(WindowSearchStep.GiveUp, WindowSearch.Decide(S(61), false, false, 800, 600));
        // a window that was taken and then rebuilt by the emulator: keep looking, never give up
        Assert.Equal(WindowSearchStep.Wait, WindowSearch.Decide(S(300), true, false, 800, 600));
        Assert.Equal(WindowSearchStep.Take, WindowSearch.Decide(S(300), true, true, 800, 600));
    }

    [Fact]
    public void Full_screen_bar_shows_at_the_top_edge_and_hides_a_second_after()
    {
        var bar = new BarAutoHide();
        bar.Start(S(0));
        Assert.True(bar.ShouldShow(S(1.5), 500, 0, 64)); // shown a little longer first
        Assert.False(bar.ShouldShow(S(2.1), 500, 0, 64));
        Assert.True(bar.ShouldShow(S(5), 2, 0, null)); // mouse at the top edge
        Assert.True(bar.ShouldShow(S(5.5), 60, 0, 64)); // on the bar
        Assert.True(bar.ShouldShow(S(6.2), 400, 0, 64)); // left – lingers
        Assert.False(bar.ShouldShow(S(6.6), 400, 0, 64));
    }
}

/// <summary>Command line of RetroHub.App: --launch, --launch-mode, --emulator, test switches.</summary>
public class CommandLineTests
{
    [Fact]
    public void Launch_with_mode_and_emulator()
    {
        var o = CommandLineOptions.Parse(["--launch", "games/nes/240p Test Suite (Homebrew).nes", "--launch-mode", "fullscreen", "--emulator", "retroarch/fceumm"], @"C:\Work");
        Assert.NotNull(o.Launch);
        Assert.Equal("games/nes/240p Test Suite (Homebrew).nes", o.Launch!.Target);
        Assert.Equal(LaunchMode.Fullscreen, o.Launch.Mode);
        Assert.Equal("retroarch/fceumm", o.Launch.Emulator);
        Assert.Equal(@"C:\Work", o.Launch.WorkingDirectory);
        Assert.Empty(o.Errors);
        Assert.False(o.Demo);
    }

    [Theory]
    [InlineData("embedded", LaunchMode.Embedded)]
    [InlineData("Eingebettet", LaunchMode.Embedded)]
    [InlineData("FULLSCREEN", LaunchMode.Fullscreen)]
    [InlineData("vollbild", LaunchMode.Fullscreen)]
    [InlineData("windowed", LaunchMode.Windowed)]
    [InlineData("fenster", LaunchMode.Windowed)]
    public void Launch_modes(string text, LaunchMode mode) => Assert.Equal(mode, CommandLineOptions.ParseMode(text));

    [Fact]
    public void Equals_form_quotes_and_ids()
    {
        var o = CommandLineOptions.Parse(["--launch=\"42\"", "--launch-mode=windowed", "--demo"]);
        Assert.Equal("42", o.Launch!.Target);
        Assert.Equal(LaunchMode.Windowed, o.Launch.Mode);
        Assert.Null(o.Launch.Emulator);
        Assert.True(o.Demo);
    }

    [Fact]
    public void Problems_are_reported_not_thrown()
    {
        Loc.Load("de", null);
        var bad = CommandLineOptions.Parse(["--launch", "x.gba", "--launch-mode", "sideways"]);
        Assert.Null(bad.Launch!.Mode);
        Assert.Contains(bad.Errors, e => e.Contains("sideways"));

        var missing = CommandLineOptions.Parse(["--launch"]);
        Assert.Null(missing.Launch);
        Assert.Single(missing.Errors);

        var valueIsSwitch = CommandLineOptions.Parse(["--launch", "--demo"]);
        Assert.Null(valueIsSwitch.Launch);
        Assert.True(valueIsSwitch.Demo);

        var orphan = CommandLineOptions.Parse(["--emulator", "mgba"]);
        Assert.Null(orphan.Launch);
        Assert.Single(orphan.Errors);

        Assert.Empty(CommandLineOptions.Parse(["--unknown", "whatever"]).Errors);
    }

    [Fact]
    public void Test_switches()
    {
        var o = CommandLineOptions.Parse(["--test-quit-after", "12.5", "--test-exit-after-return", "3", "--test-release-after", "8", "--test-report", @"C:\t\r.jsonl"]);
        Assert.Equal(12.5, o.Test.QuitAfterSeconds);
        Assert.Equal(3, o.Test.ExitAfterReturnSeconds);
        Assert.Equal(8, o.Test.ReleaseAfterSeconds);
        Assert.Equal(@"C:\t\r.jsonl", o.Test.ReportFile);
        Assert.True(o.Test.Any);
        var more = CommandLineOptions.Parse(["--test-fullscreen-after=13", "--test-overlays-after", "5"]);
        Assert.Equal(13, more.Test.FullscreenAfterSeconds);
        Assert.Equal(5, more.Test.OverlaysAfterSeconds);
        Assert.True(more.Test.Any);
        Assert.False(CommandLineOptions.Parse(["--test-quit-after", "-1"]).Test.Any);
        Assert.False(CommandLineOptions.Parse([]).Test.Any);
    }

    [Fact]
    public void Target_candidates_absolute_relative_and_portable()
    {
        var paths = new AppPaths(@"D:\Hub", @"D:\Hub\app");
        Assert.Equal([@"D:\Games\x.gba"], LaunchTargetResolver.Candidates(paths, @"D:\Games\x.gba", @"C:\Work"));
        Assert.Equal([@"C:\Work\games\x.gba", @"D:\Hub\games\x.gba"], LaunchTargetResolver.Candidates(paths, "games/x.gba", @"C:\Work"));
        Assert.Equal([@"D:\Hub\games\x.gba"], LaunchTargetResolver.Candidates(paths, "games/x.gba", null));
        Assert.Empty(LaunchTargetResolver.Candidates(paths, "  ", null));
    }

    [Fact]
    public void Forwarded_requests_round_trip_and_are_consumed()
    {
        var dir = Path.Combine(Path.GetTempPath(), "retrohub-requests-" + Guid.NewGuid().ToString("N"));
        try
        {
            Assert.True(SingleInstance.PostRequest(dir, ["--launch", "a b.gba"], @"C:\Work"));
            Assert.True(SingleInstance.PostRequest(dir, ["--launch", "7"], null));
            File.WriteAllText(Path.Combine(dir, "zz-broken.json"), "{ not json");
            var requests = SingleInstance.TakeRequests(dir);
            Assert.Equal(2, requests.Count);
            Assert.Equal(["--launch", "a b.gba"], requests[0].Args);
            Assert.Equal(@"C:\Work", requests[0].WorkingDirectory);
            Assert.Equal("7", requests[1].Args[1]);
            Assert.Empty(Directory.GetFiles(dir));
            Assert.Empty(SingleInstance.TakeRequests(dir));
        }
        finally { if (Directory.Exists(dir)) Directory.Delete(dir, true); }
    }

    [Fact]
    public void Left_behind_requests_are_dropped_not_started()
    {
        // review regression: a request the running instance never took (it was closing) would start a game out of the
        // blue on some later second start
        var dir = Path.Combine(Path.GetTempPath(), "retrohub-requests-" + Guid.NewGuid().ToString("N"));
        try
        {
            Assert.True(SingleInstance.PostRequest(dir, ["--launch", "old.gba"], null));
            var old = Directory.GetFiles(dir).Single();
            File.SetLastWriteTimeUtc(old, DateTime.UtcNow - SingleInstance.MaxRequestAge - TimeSpan.FromMinutes(1));
            Assert.True(SingleInstance.PostRequest(dir, ["--launch", "new.gba"], null));
            var requests = SingleInstance.TakeRequests(dir);
            Assert.Equal("new.gba", Assert.Single(requests).Args[1]);
            Assert.Empty(Directory.GetFiles(dir)); // the old one is gone too
        }
        finally { if (Directory.Exists(dir)) Directory.Delete(dir, true); }
    }
}

/// <summary>Launch flow of the coordinator by mode, with a scripted session service (headless, no real processes).</summary>
public class LaunchFlowTests
{
    private sealed class FakeSession(LaunchPlan plan) : IGameSession
    {
        private Action<IGameSession>? _ended;
        public LaunchPlan Plan { get; } = plan;
        public SessionState State { get; private set; } = SessionState.Running;
        public System.Diagnostics.Process? Process => null;
        public DateTime StartedUtc { get; } = DateTime.UtcNow;
        public TimeSpan Elapsed => DateTime.UtcNow - StartedUtc;
        public int? ExitCode { get; private set; }
        public string? Error => null;
        public IReadOnlyList<nint> Windows { get; set; } = [];
        public bool ClosesOnRequest { get; set; } = true;
        public int CloseRequests { get; private set; }
        public int Kills { get; private set; }

        public event Action<IGameSession>? Ended
        {
            add { if (State == SessionState.Exited) value?.Invoke(this); else _ended += value; }
            remove => _ended -= value;
        }

        public void End(int code = 0)
        {
            if (State == SessionState.Exited) return;
            State = SessionState.Exited;
            ExitCode = code;
            _ended?.Invoke(this);
        }

        public Task<bool> RequestCloseAsync(TimeSpan timeout)
        {
            CloseRequests++;
            if (!ClosesOnRequest) return Task.FromResult(false);
            End();
            return Task.FromResult(true);
        }

        public void Kill() { Kills++; End(-1); }
        public IReadOnlyList<nint> FindWindows() => Windows;
    }

    private sealed class FakeSessions : ISessionService
    {
        public FakeSession? Last { get; private set; }
        public Action<FakeSession>? OnStart { get; set; }
        public IGameSession? Current => Last is { State: SessionState.Running } s ? s : null;
        public event Action<IGameSession>? SessionStarted { add { } remove { } }
        public event Action<IGameSession>? SessionEnded { add { } remove { } }
        public IGameSession Start(LaunchPlan plan)
        {
            Last = new FakeSession(plan);
            OnStart?.Invoke(Last);
            return Last;
        }
    }

    private sealed class FakeInput : IInputSource
    {
        public List<bool> Suspends { get; } = [];
        public event Action<NavAction, bool>? ActionRaised { add { } remove { } }
        public event Action? DirectionReleased { add { } remove { } }
        public event Action? ControllerChanged { add { } remove { } }
        public ControllerFamily Controller => ControllerFamily.None;
        public string? ControllerName => null;
        public void SetSuspended(bool suspended) => Suspends.Add(suspended);
        public void Dispose() { }
    }

    private sealed class FakeCombo : IExitComboSource
    {
        public event Action? Triggered;
        public bool Started { get; private set; }
        public bool Disposed { get; private set; }
        public void Start() => Started = true;
        public void Fire() => Triggered?.Invoke();
        public void Dispose() => Disposed = true;
    }

    private static (MainWindow Window, MainViewModel Vm, FakeSessions Sessions, FakeInput Input) Open(Action<AppSettings>? settings = null)
    {
        var demo = TestSupport.CreateContext(s => { s.Motion = MotionLevel.Off; settings?.Invoke(s); }).Services;
        var sessions = new FakeSessions();
        var input = new FakeInput();
        var services = new AppServices
        {
            Paths = demo.Paths, Settings = demo.Settings, Ui = demo.Ui, Catalog = demo.Catalog, Games = demo.Games,
            Scanner = demo.Scanner, Emulators = demo.Emulators, Launcher = demo.Launcher, Sessions = sessions, Input = input, IsDemo = true,
        };
        var vm = new MainViewModel(new UiContext(services));
        vm.Launch.MinimumTransition = TimeSpan.FromMilliseconds(50);
        var window = new MainWindow(vm) { Width = 1400, Height = 850 };
        window.Show();
        TestSupport.Pump(() => vm.CurrentPage is HomeViewModel { IsLoaded: true });
        return (window, vm, sessions, input);
    }

    private static GameItemViewModel GoldenSun(MainViewModel vm) =>
        vm.Ctx.Items.Get(vm.Ctx.Services.Games.Query(new GameQuery { Text = "Golden Sun" }).Single());

    [Fact]
    public Task Fullscreen_hides_after_the_first_window_and_comes_back_when_the_game_ends() => UiTest.Run(() =>
    {
        var (window, vm, sessions, input) = Open();
        var game = GoldenSun(vm);
        var returned = 0;
        vm.Launch.Returned += (_, _) => returned++;
        _ = vm.Launch.LaunchAsync(game, null, LaunchMode.Fullscreen);
        Assert.True(TestSupport.Pump(() => sessions.Last != null, 3000));
        // no window yet: the transition stays, RetroHub stays visible
        TestSupport.Settle(400);
        Assert.Equal(LaunchPhase.Starting, vm.Launch.Phase);
        Assert.False(vm.Launch.IsShellHidden);
        sessions.Last!.Windows = [(nint)0x1234];
        Assert.True(TestSupport.Pump(() => vm.Launch.IsShellHidden, 3000));
        Assert.Equal((nint)0x1234, vm.Launch.HandoffWindow);
        Assert.Equal(LaunchPhase.Running, vm.Launch.Phase);
        Assert.Equal(LaunchPresentation.Handoff, vm.Launch.Presentation);
        Assert.False(window.IsVisible); // Window.Hide()
        Assert.Equal([true], input.Suspends);

        sessions.Last.End();
        Assert.True(TestSupport.Pump(() => vm.Launch.Phase == LaunchPhase.Idle, 3000));
        TestSupport.Settle(150);
        Assert.False(vm.Launch.IsShellHidden);
        Assert.True(window.IsVisible);
        Assert.Equal(1, returned);
        Assert.Equal([true, false], input.Suspends);
        Assert.Contains(vm.Toasts.Items, t => t.Text == Loc.T("launch.ended"));
        Assert.True(vm.CurrentPage is GameDetailViewModel { Game.Title: "Golden Sun" });
        window.Close();
    });

    [Fact]
    public Task Fullscreen_without_a_window_keeps_the_overlay_with_quit() => UiTest.Run(() =>
    {
        var (window, vm, sessions, _) = Open();
        vm.Launch.FullscreenWindowTimeout = TimeSpan.FromMilliseconds(300);
        _ = vm.Launch.LaunchAsync(GoldenSun(vm), null, LaunchMode.Fullscreen);
        Assert.True(TestSupport.Pump(() => vm.Launch.Phase == LaunchPhase.Running, 4000));
        Assert.False(vm.Launch.IsShellHidden);
        Assert.True(vm.Launch.WindowMissing);
        Assert.True(vm.Launch.ShowRunningOverlay);
        Assert.True(window.IsVisible);
        Assert.True(vm.Launch.StopCommand.CanExecute(null));
        vm.Launch.StopCommand.Execute(null);
        Assert.True(TestSupport.Pump(() => vm.Launch.Phase == LaunchPhase.Idle, 3000));
        Assert.Equal(1, sessions.Last!.CloseRequests);
        window.Close();
    });

    [Fact]
    public Task Embedded_hands_the_session_to_the_game_screen_and_can_continue_windowed() => UiTest.Run(() =>
    {
        var (window, vm, sessions, _) = Open();
        _ = vm.Launch.LaunchAsync(GoldenSun(vm), null, LaunchMode.Embedded);
        Assert.True(TestSupport.Pump(() => vm.Launch.Phase == LaunchPhase.Running, 3000));
        Assert.Same(sessions.Last, vm.Launch.EmbeddedSession);
        Assert.True(vm.Launch.ShowGameScreen);
        Assert.False(vm.Launch.ShowRunningOverlay);
        var screen = window.FindControl<GameScreen>("GameScreen")!;
        Assert.True(screen.IsEffectivelyVisible);
        Assert.False(screen.HasGame); // headless: no native host, nothing to take

        // "Eigenes Fenster" / no window: the session goes on with the running overlay
        vm.Launch.EmbedReleased(EmbedRelease.NoWindow);
        Assert.Null(vm.Launch.EmbeddedSession);
        Assert.Equal(LaunchPresentation.Overlay, vm.Launch.Presentation);
        Assert.True(vm.Launch.ShowRunningOverlay);
        Assert.Equal(LaunchPhase.Running, vm.Launch.Phase);
        Assert.Contains(vm.Toasts.Items, t => t.Text == Loc.T("launch.embed.noWindow"));
        sessions.Last!.End();
        Assert.True(TestSupport.Pump(() => vm.Launch.Phase == LaunchPhase.Idle, 3000));
        window.Close();
    });

    [Fact]
    public Task Windowed_shows_the_running_overlay() => UiTest.Run(() =>
    {
        var (window, vm, sessions, _) = Open();
        _ = vm.Launch.LaunchAsync(GoldenSun(vm), null, LaunchMode.Windowed);
        Assert.True(TestSupport.Pump(() => vm.Launch.Phase == LaunchPhase.Running, 3000));
        Assert.Equal(LaunchPresentation.Overlay, vm.Launch.Presentation);
        Assert.Null(vm.Launch.EmbeddedSession);
        Assert.False(vm.Launch.IsShellHidden);
        sessions.Last!.End();
        Assert.True(TestSupport.Pump(() => vm.Launch.Phase == LaunchPhase.Idle, 3000));
        window.Close();
    });

    [Fact]
    public Task Exit_combo_runs_for_the_session_and_quits_the_game() => UiTest.Run(() =>
    {
        var (window, vm, sessions, _) = Open(s => s.ExitComboSeconds = 2);
        var combos = new List<(double Seconds, FakeCombo Combo)>();
        vm.Launch.ExitComboFactory = s => { var c = new FakeCombo(); combos.Add((s, c)); return c; };
        _ = vm.Launch.LaunchAsync(GoldenSun(vm), null, LaunchMode.Windowed);
        Assert.True(TestSupport.Pump(() => vm.Launch.Phase == LaunchPhase.Running, 3000));
        var (seconds, combo) = Assert.Single(combos);
        Assert.Equal(2, seconds);
        Assert.True(combo.Started);
        // the watcher's thread fires: marshalled to the UI thread, then the same path as "Spiel beenden"
        var t = new Thread(combo.Fire);
        t.Start();
        t.Join();
        Assert.True(TestSupport.Pump(() => vm.Launch.Phase == LaunchPhase.Idle, 3000));
        Assert.Equal(1, sessions.Last!.CloseRequests);
        Assert.True(combo.Disposed);
        window.Close();
    });

    [Fact]
    public Task Exit_combo_is_not_started_when_switched_off() => UiTest.Run(() =>
    {
        var (window, vm, sessions, _) = Open(s => s.ExitComboSeconds = 0);
        var created = 0;
        vm.Launch.ExitComboFactory = _ => { created++; return new FakeCombo(); };
        _ = vm.Launch.LaunchAsync(GoldenSun(vm), null, LaunchMode.Windowed);
        Assert.True(TestSupport.Pump(() => vm.Launch.Phase == LaunchPhase.Running, 3000));
        Assert.Equal(0, created);
        sessions.Last!.End();
        Assert.True(TestSupport.Pump(() => vm.Launch.Phase == LaunchPhase.Idle, 3000));
        window.Close();
    });

    [Fact]
    public Task A_game_that_does_not_close_offers_a_hard_kill_with_keep_waiting_first() => UiTest.Run(() =>
    {
        var (window, vm, sessions, input) = Open();
        vm.Launch.CloseTimeout = TimeSpan.FromMilliseconds(100);
        _ = vm.Launch.LaunchAsync(GoldenSun(vm), null, LaunchMode.Windowed);
        Assert.True(TestSupport.Pump(() => vm.Launch.Phase == LaunchPhase.Running, 3000));
        sessions.Last!.ClosesOnRequest = false;
        vm.Launch.StopCommand.Execute(null);
        Assert.True(TestSupport.Pump(() => vm.Dialogs.Current != null, 3000));
        var dialog = vm.Dialogs.Current!;
        Assert.Equal(Loc.T("launch.forceQuit.title"), dialog.Title);
        Assert.Equal("wait", dialog.Buttons[0].Result); // the default (first, focused) answer keeps the game
        Assert.Equal(LaunchPhase.Ending, vm.Launch.Phase);
        Assert.False(input.Suspends[^1]); // the controller works for the question

        // "Weiter warten": asked again after another timeout
        dialog.Cancel();
        Assert.True(TestSupport.Pump(() => vm.Dialogs.Current != null && vm.Dialogs.Current != dialog, 3000));
        Assert.Equal(2, sessions.Last.CloseRequests);
        vm.Dialogs.Current!.ChooseCommand.Execute(vm.Dialogs.Current.Buttons.Single(b => b.Result == "kill"));
        Assert.True(TestSupport.Pump(() => vm.Launch.Phase == LaunchPhase.Idle, 3000));
        Assert.Equal(1, sessions.Last.Kills);
        window.Close();
    });

    [Fact]
    public Task Hard_kill_question_goes_away_when_the_game_closes_by_itself() => UiTest.Run(() =>
    {
        // review regression: the question stayed open (about a game that was gone) until the user answered it
        var (window, vm, sessions, _) = Open();
        vm.Launch.CloseTimeout = TimeSpan.FromMilliseconds(100);
        _ = vm.Launch.LaunchAsync(GoldenSun(vm), null, LaunchMode.Windowed);
        Assert.True(TestSupport.Pump(() => vm.Launch.Phase == LaunchPhase.Running, 3000));
        sessions.Last!.ClosesOnRequest = false;
        vm.Launch.StopCommand.Execute(null);
        Assert.True(TestSupport.Pump(() => vm.Dialogs.Current != null, 3000));
        Assert.Equal(Loc.T("launch.forceQuit.title"), vm.Dialogs.Current!.Title);

        sessions.Last.End(); // the emulator finally saved and quit
        Assert.True(TestSupport.Pump(() => vm.Launch.Phase == LaunchPhase.Idle && vm.Dialogs.Current == null, 3000));
        TestSupport.Settle(300);
        Assert.Null(vm.Dialogs.Current); // not asked again
        Assert.Equal(0, sessions.Last.Kills);
        Assert.Equal(1, sessions.Last.CloseRequests);
        window.Close();
    });

    [Fact]
    public Task Hard_kill_question_shows_the_hidden_window_first() => UiTest.Run(() =>
    {
        var (window, vm, sessions, _) = Open();
        vm.Launch.CloseTimeout = TimeSpan.FromMilliseconds(100);
        sessions.OnStart = s => s.Windows = [(nint)0x42];
        _ = vm.Launch.LaunchAsync(GoldenSun(vm), null, LaunchMode.Fullscreen);
        Assert.True(TestSupport.Pump(() => vm.Launch.IsShellHidden, 3000));
        sessions.Last!.ClosesOnRequest = false;
        vm.Launch.StopCommand.Execute(null);
        Assert.True(TestSupport.Pump(() => vm.Dialogs.Current != null, 3000));
        Assert.False(vm.Launch.IsShellHidden);
        Assert.True(window.IsVisible);
        sessions.Last.End(); // it closed after all while the question was open
        Assert.True(TestSupport.Pump(() => vm.Launch.Phase == LaunchPhase.Idle, 3000));
        vm.Dialogs.Current?.Cancel();
        window.Close();
    });

    [Fact]
    public Task Recovered_session_shows_the_overlay_and_returns_normally() => UiTest.Run(() =>
    {
        var (window, vm, sessions, input) = Open();
        var game = GoldenSun(vm);
        var plan = vm.Ctx.Services.Launcher.Plan(game.Game);
        var session = (FakeSession)sessions.Start(plan);
        vm.Launch.AttachRecovered(session);
        Assert.Equal(LaunchPhase.Running, vm.Launch.Phase);
        Assert.True(vm.Launch.IsRecovered);
        Assert.Equal(LaunchPresentation.Overlay, vm.Launch.Presentation);
        Assert.Equal("Golden Sun", vm.Launch.Game?.Title);
        Assert.Equal([true], input.Suspends);
        vm.Launch.StopCommand.Execute(null);
        Assert.True(TestSupport.Pump(() => vm.Launch.Phase == LaunchPhase.Idle, 3000));
        Assert.True(TestSupport.Pump(() => vm.CurrentPage is GameDetailViewModel { Game.Title: "Golden Sun" }, 3000));
        window.Close();
    });

    [Fact]
    public Task Command_line_launch_resolves_ids_and_paths_and_reports_errors() => UiTest.Run(() =>
    {
        var (window, vm, sessions, _) = Open();
        var game = GoldenSun(vm).Game;
        // numeric id
        var byId = LaunchTargetResolver.ResolveAsync(vm.Ctx.Services, new LaunchRequest(game.Id.ToString())).GetAwaiter().GetResult();
        Assert.Equal(game.Id, byId.Game?.Id);
        // portable path of a library entry (the demo file does not exist on disk: found by its path in the library)
        var byPath = LaunchTargetResolver.ResolveAsync(vm.Ctx.Services, new LaunchRequest(game.Path)).GetAwaiter().GetResult();
        Assert.Equal(game.Id, byPath.Game?.Id);
        // unknown id / missing file
        Assert.Equal(Loc.T("cli.noGameId", 999999), LaunchTargetResolver.ResolveAsync(vm.Ctx.Services, new LaunchRequest("999999")).GetAwaiter().GetResult().Error);
        Assert.NotNull(LaunchTargetResolver.ResolveAsync(vm.Ctx.Services, new LaunchRequest("games/nowhere/x.gba")).GetAwaiter().GetResult().Error);

        // the whole request: game detail, then the start with the overrides
        _ = CommandLineLaunch.RunAsync(vm.Ctx, new LaunchRequest(game.Id.ToString(), LaunchMode.Windowed));
        Assert.True(TestSupport.Pump(() => vm.Launch.Phase == LaunchPhase.Running, 3000));
        Assert.Equal(LaunchMode.Windowed, sessions.Last!.Plan.Mode);
        Assert.Equal(PageKey.GameDetail, vm.Navigator.Current);
        sessions.Last.End();
        Assert.True(TestSupport.Pump(() => vm.Launch.Phase == LaunchPhase.Idle, 3000));

        // an unresolvable target shows a dialog (no exception, no start)
        _ = CommandLineLaunch.RunAsync(vm.Ctx, new LaunchRequest("does-not-exist.gba"));
        Assert.True(TestSupport.Pump(() => vm.Dialogs.Current != null, 3000));
        Assert.Equal(Loc.T("cli.title"), vm.Dialogs.Current!.Title);
        vm.Dialogs.Current.Cancel();
        TestSupport.Pump(() => vm.Dialogs.Current == null);
        window.Close();
    });
}
