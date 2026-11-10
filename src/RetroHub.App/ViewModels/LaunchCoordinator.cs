using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RetroHub.App.Localization;
using RetroHub.App.Services;
using RetroHub.Core.Catalog;
using RetroHub.Core.Emulation;
using RetroHub.Core.Logging;
using RetroHub.Input;

namespace RetroHub.App.ViewModels;

public enum LaunchPhase { Idle, Starting, Running, Ending }

/// <summary>How a running game is shown.</summary>
public enum LaunchPresentation
{
    /// <summary>RetroHub stays visible with the running overlay (Windowed mode, released/failed embedding, recovered session, fullscreen without a window).</summary>
    Overlay,
    /// <summary>The emulator window sits inside RetroHub's game screen (<see cref="Controls.GameScreen"/>).</summary>
    Embedded,
    /// <summary>The emulator owns the screen; RetroHub hides until it exits.</summary>
    Handoff,
}

/// <summary>Why an embedded game left the game screen.</summary>
public enum EmbedRelease { User, NoWindow, Failed }

/// <summary>Watches the controller exit combo during a game (XInput Back+Start held / Guide). Events on any thread.</summary>
public interface IExitComboSource : IDisposable
{
    event Action? Triggered;
    void Start();
}

/// <summary>The real exit combo: <see cref="ExitComboWatcher"/> (RetroHub.Input, XInput only).</summary>
internal sealed class XInputExitCombo : IExitComboSource
{
    private readonly ExitComboWatcher _watcher;
    public XInputExitCombo(double holdSeconds)
    {
        _watcher = new ExitComboWatcher(holdSeconds);
        _watcher.Triggered += () => Triggered?.Invoke();
    }
    public event Action? Triggered;
    public void Start() => _watcher.Start();
    public void Dispose() => _watcher.Dispose();
}

/// <summary>
/// Starting a game from the UI and everything until the return to the hub:
/// plan → show problems honestly or the "Spiel wird gestartet" transition → start the session → by launch mode
/// <list type="bullet">
/// <item><b>Embedded</b>: the session is handed to the game screen (<see cref="EmbeddedSession"/>), which takes the
/// emulator window in; it can fall back to <see cref="LaunchPresentation.Overlay"/> (<see cref="EmbedReleased"/>);</item>
/// <item><b>Fullscreen</b>: waits for the first emulator window (≤ <see cref="FullscreenWindowTimeout"/>), then
/// <see cref="IsShellHidden"/> asks the window to hide; without a window the overlay with "Spiel beenden" stays;</item>
/// <item><b>Windowed</b> (and recovered sessions): the running overlay.</item>
/// </list>
/// During the whole session the controller navigation is suspended and the exit combo is watched; "Spiel beenden"
/// asks the emulator to close (≤ <see cref="CloseTimeout"/>), then offers a hard kill. When the session ends the
/// game detail comes back ("Willkommen zurück" with the play time).
/// </summary>
public sealed partial class LaunchCoordinator : ObservableObject
{
    private readonly UiContext _ctx;
    private IGameSession? _session;
    private IExitComboSource? _exitCombo;
    private readonly DispatcherTimer _clock;
    /// <summary>The open "Hart beenden?" question (null when none).</summary>
    private DialogViewModel? _forceQuitQuestion;

    public LaunchCoordinator(UiContext ctx)
    {
        _ctx = ctx;
        // note: the (interval, priority, callback) constructor would start the timer immediately
        _clock = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromSeconds(1) };
        _clock.Tick += (_, _) => UpdateElapsed();
        ExitComboFactory = seconds => ctx.Services.IsDemo ? null : new XInputExitCombo(seconds);
    }

    [ObservableProperty] public partial LaunchPhase Phase { get; private set; }
    [ObservableProperty] public partial LaunchPresentation Presentation { get; private set; }
    [ObservableProperty] public partial GameItemViewModel? Game { get; private set; }
    [ObservableProperty] public partial string EmulatorName { get; private set; } = "";
    [ObservableProperty] public partial string ModeText { get; private set; } = "";
    /// <summary>"mGBA startet …" on the start surface (DESIGN 9.3).</summary>
    [ObservableProperty] public partial string StartingText { get; private set; } = "";
    [ObservableProperty] public partial string ElapsedText { get; private set; } = "";
    /// <summary>Short status next to the elapsed time ("wird beendet …").</summary>
    [ObservableProperty] public partial string StatusNote { get; private set; } = "";
    /// <summary>The session the game screen should take in (null when nothing is embedded).</summary>
    [ObservableProperty] public partial IGameSession? EmbeddedSession { get; private set; }
    /// <summary>Fullscreen hand-off: RetroHub's window should be hidden now (the emulator owns the screen).</summary>
    [ObservableProperty] public partial bool IsShellHidden { get; private set; }
    /// <summary>The emulator window RetroHub handed the screen to (foreground before hiding).</summary>
    public nint HandoffWindow { get; private set; }
    /// <summary>Fullscreen mode: no emulator window appeared in time – the overlay explains it.</summary>
    [ObservableProperty] public partial bool WindowMissing { get; private set; }
    /// <summary>The session was still running from before RetroHub was restarted (re-attached by the session service).</summary>
    [ObservableProperty] public partial bool IsRecovered { get; private set; }

    public bool IsActive => Phase != LaunchPhase.Idle;
    public bool IsStarting => Phase == LaunchPhase.Starting;
    public bool IsRunning => Phase is LaunchPhase.Running or LaunchPhase.Ending;
    public bool IsEnding => Phase == LaunchPhase.Ending;
    public bool IsEmbedded => Presentation == LaunchPresentation.Embedded;
    /// <summary>The game screen is shown (also during the start transition, where it is masked below the transition).</summary>
    public bool ShowGameScreen => IsActive && EmbeddedSession != null;
    /// <summary>The running overlay ("Läuft gerade" + "Spiel beenden").</summary>
    public bool ShowRunningOverlay => IsRunning && Presentation != LaunchPresentation.Embedded;
    /// <summary>The session currently followed (null when idle).</summary>
    public IGameSession? Session => _session;

    partial void OnPhaseChanged(LaunchPhase value)
    {
        OnPropertyChanged(nameof(IsActive));
        OnPropertyChanged(nameof(IsStarting));
        OnPropertyChanged(nameof(IsRunning));
        OnPropertyChanged(nameof(IsEnding));
        OnPropertyChanged(nameof(ShowGameScreen));
        OnPropertyChanged(nameof(ShowRunningOverlay));
        StopCommand.NotifyCanExecuteChanged();
    }

    partial void OnPresentationChanged(LaunchPresentation value)
    {
        OnPropertyChanged(nameof(IsEmbedded));
        OnPropertyChanged(nameof(ShowRunningOverlay));
    }

    partial void OnEmbeddedSessionChanged(IGameSession? value) => OnPropertyChanged(nameof(ShowGameScreen));

    /// <summary>Minimum time the start transition stays visible (feels intentional, hides process start-up).</summary>
    public TimeSpan MinimumTransition { get; set; } = TimeSpan.FromMilliseconds(900);
    /// <summary>Fullscreen mode: how long to wait for the emulator's first window before keeping RetroHub visible.</summary>
    public TimeSpan FullscreenWindowTimeout { get; set; } = TimeSpan.FromSeconds(20);
    /// <summary>"Spiel beenden": how long the emulator gets to close by itself before the hard-kill question.</summary>
    public TimeSpan CloseTimeout { get; set; } = TimeSpan.FromSeconds(8);
    /// <summary>Creates the exit-combo watcher for a session (hold seconds → source; null = none). Replaceable for tests.</summary>
    public Func<double, IExitComboSource?> ExitComboFactory { get; set; }

    /// <summary>A session began (UI thread): started by <see cref="LaunchAsync"/> or re-attached.</summary>
    public event Action<IGameSession>? SessionBegan;
    /// <summary>The session ended and RetroHub is back at the game detail (UI thread). Argument: the game, if known.</summary>
    public event Action<GameItemViewModel?, IGameSession>? Returned;
    /// <summary>A launch request finished without starting a session (problems dialog, failed start, already running).</summary>
    public event Action<GameItemViewModel>? LaunchAborted;

    public async Task LaunchAsync(GameItemViewModel item, string? emulatorOverride = null, LaunchMode? modeOverride = null)
    {
        if (Phase != LaunchPhase.Idle || _ctx.Services.Sessions.Current != null)
        {
            _ctx.Toasts.Show(Loc.T("launch.alreadyRunning"), ToastKind.Warning);
            LaunchAborted?.Invoke(item);
            return;
        }
        var services = _ctx.Services;
        var plan = await UiContext.Background(() =>
        {
            var game = services.Games.Get(item.Id) ?? item.Game;
            return services.Launcher.Plan(game, emulatorOverride, modeOverride);
        }, "Launch planning");
        if (plan == null)
        {
            _ctx.Toasts.Show(Loc.T("launch.planFailed"), ToastKind.Error);
            LaunchAborted?.Invoke(item);
            return;
        }
        TestHooks.Report("plan", ("game", item.Title), ("emulator", plan.Emulator?.Def.Id), ("core", plan.Core), ("mode", plan.Mode.ToString()),
            ("canLaunch", plan.CanLaunch), ("problems", plan.Problems.Select(p => p.Message).ToArray()));
        if (!plan.CanLaunch)
        {
            var dialog = _ctx.Dialogs.InfoAsync(Loc.T("launch.cannotStart.title"), Loc.T("launch.cannotStart.message", item.Title),
                plan.Problems.Select(p => new DialogDetail(p.Message, p.Blocking)).ToList());
            LaunchAborted?.Invoke(item);
            await dialog;
            return;
        }

        Game = item;
        EmulatorName = plan.Emulator?.Def.Name + (plan.Core != null ? $" · {plan.Core}" : "");
        StartingText = Loc.T("launch.startingWith", plan.Emulator?.Def.Name ?? Loc.T("launch.theEmulator"));
        ModeText = Fmt.Mode(plan.Mode);
        ElapsedText = "";
        StatusNote = "";
        WindowMissing = false;
        IsRecovered = false;
        Presentation = plan.Mode switch
        {
            LaunchMode.Embedded => LaunchPresentation.Embedded,
            LaunchMode.Fullscreen => LaunchPresentation.Handoff,
            _ => LaunchPresentation.Overlay,
        };
        Phase = LaunchPhase.Starting;
        var shown = Task.Delay(MinimumTransition);
        IGameSession session;
        try
        {
            session = await Task.Run(() => services.Sessions.Start(plan)).ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            Log.Error($"Starting '{item.Title}' failed", ex);
            ResetToIdle();
            TestHooks.Report("start-failed", ("error", ex.Message));
            var dialog = _ctx.Dialogs.InfoAsync(Loc.T("launch.failed.title"), Loc.T("launch.failed.message", item.Title, ex.Message));
            LaunchAborted?.Invoke(item);
            await dialog;
            return;
        }
        if (session.State == SessionState.Failed)
        {
            // the session service reports a failed process start as a Failed session (no events), not as an exception
            Log.Warn($"Starting '{item.Title}' failed: {session.Error}");
            ResetToIdle();
            TestHooks.Report("start-failed", ("error", session.Error));
            var dialog = _ctx.Dialogs.InfoAsync(Loc.T("launch.failed.title"),
                Loc.T("launch.failed.message", item.Title, session.Error ?? Loc.T("launch.unknownError")));
            LaunchAborted?.Invoke(item);
            await dialog;
            return;
        }
        if (plan.UseShellExecute || (session.State == SessionState.Exited && session.Process == null))
        {
            // shell start (.lnk / steam://): handed off to another program, cannot be followed – no running overlay
            await shown.ConfigureAwait(true);
            ResetToIdle();
            _ctx.Toasts.Show(Loc.T("launch.detached", item.Title), ToastKind.Info, Loc.T("launch.detached.detail"), seconds: 6);
            LaunchAborted?.Invoke(item);
            return;
        }

        Follow(session);
        if (_session != session) return;
        if (Presentation == LaunchPresentation.Embedded)
        {
            // the game screen takes the window in while the transition still shows (it stays masked below it)
            EmbeddedSession = session;
            await shown.ConfigureAwait(true);
            if (_session == session && Phase == LaunchPhase.Starting) EnterRunning();
        }
        else if (Presentation == LaunchPresentation.Handoff)
        {
            var window = await WaitForWindowAsync(session, FullscreenWindowTimeout).ConfigureAwait(true);
            await shown.ConfigureAwait(true);
            if (_session != session || Phase != LaunchPhase.Starting) return;
            EnterRunning();
            if (window != 0)
            {
                HandoffWindow = window;
                TestHooks.Report("handoff", ("window", window.ToString("X")));
                Log.Info($"Fullscreen hand-off: emulator window {window:X} – hiding RetroHub");
                _clock.Stop(); // nothing to show while hidden
                IsShellHidden = true;
            }
            else
            {
                Log.Warn($"Fullscreen: no window of '{item.Title}' after {FullscreenWindowTimeout.TotalSeconds:0} s – RetroHub stays visible");
                TestHooks.Report("handoff-no-window");
                Presentation = LaunchPresentation.Overlay;
                WindowMissing = true;
                _ctx.Toasts.Show(Loc.T("launch.fullscreen.noWindow"), ToastKind.Warning, Loc.T("launch.fullscreen.noWindow.detail"), seconds: 8);
            }
        }
        else
        {
            await shown.ConfigureAwait(true);
            if (_session == session && Phase == LaunchPhase.Starting) EnterRunning();
        }
        if (_session == session)
            foreach (var warning in plan.Problems.Where(p => !p.Blocking))
                _ctx.Toasts.Show(warning.Message, ToastKind.Warning);
    }

    /// <summary>
    /// A game that was still running when RetroHub started (re-attached by the session service after a crash or a
    /// close): shown with the running overlay, "Spiel beenden" works, the return is the normal one.
    /// </summary>
    public void AttachRecovered(IGameSession session)
    {
        if (Phase != LaunchPhase.Idle || _session != null || session.State is SessionState.Exited or SessionState.Failed) return;
        var plan = session.Plan;
        Log.Info($"Showing the recovered session of game {plan.Game.Id} '{plan.Game.DisplayTitle}'");
        Game = _ctx.Items.Get(plan.Game);
        // a re-attached session's plan is rebuilt from the game only; the session itself knows the emulator id
        var emulatorId = plan.Emulator?.Def.Id ?? (session as GameSession)?.EmulatorId;
        EmulatorName = plan.Emulator?.Def.Name ?? (emulatorId != null ? _ctx.Services.Catalog.GetEmulator(emulatorId)?.Name ?? emulatorId : Loc.T("launch.theEmulator"));
        ModeText = Fmt.Mode(plan.Mode == LaunchMode.Embedded ? LaunchMode.Windowed : plan.Mode);
        StatusNote = "";
        WindowMissing = false;
        IsRecovered = true;
        Presentation = LaunchPresentation.Overlay;
        Follow(session);
        if (_session != session) return;
        EnterRunning();
        _ctx.Toasts.Show(Loc.T("launch.recovered", Game.Title), ToastKind.Info, Loc.T("launch.recovered.detail"), seconds: 8);
    }

    /// <summary>
    /// The game screen let the emulator window go (button "Eigenes Fenster", no window to take, or taking failed):
    /// the session continues with the running overlay.
    /// </summary>
    public void EmbedReleased(EmbedRelease reason)
    {
        if (Presentation != LaunchPresentation.Embedded) return;
        Log.Info($"Embedded game released ({reason}) – continuing as a windowed session");
        TestHooks.Report("embed-released", ("reason", reason.ToString()));
        EmbeddedSession = null;
        Presentation = LaunchPresentation.Overlay;
        ModeText = Fmt.Mode(LaunchMode.Windowed);
        var title = Game?.Title ?? "";
        if (reason == EmbedRelease.User)
            _ctx.Toasts.Show(Loc.T("launch.embed.released", title), ToastKind.Info, Loc.T("launch.embed.released.detail"));
        else
            _ctx.Toasts.Show(Loc.T("launch.embed.noWindow"), ToastKind.Warning, Loc.T("launch.embed.noWindow.detail", title), seconds: 8);
    }

    /// <summary>Starts following a session: end event, controller suspended, exit combo.</summary>
    private void Follow(IGameSession session)
    {
        _session = session;
        try { _ctx.Services.Input.SetSuspended(true); } catch (Exception ex) { Log.Warn("Input suspend failed", ex); }
        StartExitCombo();
        TestHooks.Report("session-started", ("game", session.Plan.Game.DisplayTitle), ("mode", session.Plan.Mode.ToString()),
            ("pid", SafePid(session)), ("presentation", Presentation.ToString()));
        try { SessionBegan?.Invoke(session); } catch (Exception ex) { Log.Error("SessionBegan handler failed", ex); }
        // a handler added after the session ended is called at once (GameSession contract) → posted to OnSessionEnded
        session.Ended += OnSessionEnded;
    }

    private static int? SafePid(IGameSession session)
    {
        try { return session.Process?.Id; }
        catch (InvalidOperationException) { return null; }
    }

    private void EnterRunning()
    {
        Phase = LaunchPhase.Running;
        UpdateElapsed();
        _clock.Start();
    }

    private void StartExitCombo()
    {
        StopExitCombo();
        var seconds = _ctx.Services.Settings.Current.ExitComboSeconds;
        if (seconds <= 0) return; // setting "off"
        try
        {
            var combo = ExitComboFactory(seconds);
            if (combo == null) return;
            combo.Triggered += OnExitCombo;
            combo.Start();
            _exitCombo = combo;
        }
        catch (Exception ex)
        {
            Log.Warn("Exit combo watcher could not start", ex);
        }
    }

    private void StopExitCombo()
    {
        var combo = _exitCombo;
        _exitCombo = null;
        if (combo == null) return;
        combo.Triggered -= OnExitCombo;
        try { combo.Dispose(); } catch (Exception ex) { Log.Warn("Exit combo watcher dispose failed", ex); }
    }

    private void OnExitCombo() => Dispatcher.UIThread.Post(() =>
    {
        Log.Info("Exit combo → Spiel beenden");
        TestHooks.Report("exit-combo");
        if (StopCommand.CanExecute(null)) StopCommand.Execute(null);
    });

    /// <summary>Polls the session's windows until one shows up (0 when none within <paramref name="timeout"/> or the session ended).</summary>
    private static async Task<nint> WaitForWindowAsync(IGameSession session, TimeSpan timeout)
    {
        var until = DateTime.UtcNow + timeout;
        while (true)
        {
            if (session.State is SessionState.Exited or SessionState.Failed) return 0;
            IReadOnlyList<nint> windows;
            try { windows = await Task.Run(session.FindWindows).ConfigureAwait(true); }
            catch (Exception ex)
            {
                Log.Warn("Looking for the emulator window failed", ex);
                windows = [];
            }
            if (windows.Count > 0) return windows[0];
            if (DateTime.UtcNow >= until) return 0;
            await Task.Delay(200).ConfigureAwait(true);
        }
    }

    private void OnSessionEnded(IGameSession session) => Dispatcher.UIThread.Post(() =>
    {
        if (_session != session) return;
        var item = Game;
        Log.Info($"Session ended after {session.Elapsed.TotalSeconds:0} s (exit code {session.ExitCode?.ToString() ?? "-"}) – back to the hub");
        TestHooks.Report("session-ended", ("seconds", Math.Round(session.Elapsed.TotalSeconds, 1)), ("exitCode", session.ExitCode), ("error", session.Error));
        ResetToIdle();
        try { _ctx.Services.Input.SetSuspended(false); } catch (Exception ex) { Log.Warn("Input resume failed", ex); }
        if (item != null)
        {
            if (session.State == SessionState.Failed || session.Error != null)
                _ctx.Toasts.Show(Loc.T("launch.endedWithError", item.Title), ToastKind.Error, session.Error);
            else
                _ctx.Toasts.Show(Loc.T("launch.ended"), ToastKind.Success, Loc.T("launch.sessionTime", Fmt.PlayTime((long)session.Elapsed.TotalSeconds)));
            if (!(_ctx.Navigator.Current == PageKey.GameDetail && Equals(_ctx.Navigator.CurrentArgument, item)))
                _ctx.Navigator.Navigate(PageKey.GameDetail, item);
        }
        try { Returned?.Invoke(item, session); } catch (Exception ex) { Log.Error("Returned handler failed", ex); }
    });

    /// <summary>Back to idle: everything about the last session is cleared (the window shows again if it was hidden).</summary>
    private void ResetToIdle()
    {
        _clock.Stop();
        // "Hart beenden?" about a game that is gone now: closes ("Weiter warten" result; StopAsync then sees the end)
        _forceQuitQuestion?.Cancel();
        _forceQuitQuestion = null;
        StopExitCombo();
        if (_session != null) _session.Ended -= OnSessionEnded;
        _session = null;
        EmbeddedSession = null;
        IsShellHidden = false;
        HandoffWindow = 0;
        Phase = LaunchPhase.Idle;
        Presentation = LaunchPresentation.Overlay;
        WindowMissing = false;
        IsRecovered = false;
        StatusNote = "";
        Game = null;
    }

    private void UpdateElapsed()
    {
        if (_session == null) return;
        var t = _session.Elapsed;
        ElapsedText = t.TotalHours >= 1 ? t.ToString(@"h\:mm\:ss") : t.ToString(@"mm\:ss");
    }

    private bool CanStop() => _session != null && Phase is LaunchPhase.Starting or LaunchPhase.Running;

    /// <summary>
    /// "Spiel beenden" (button, exit combo, test hook): asks the emulator to close like its own close button, so it can
    /// save; when it is still running after <see cref="CloseTimeout"/>, a controller-navigable question offers to end
    /// it the hard way ("Weiter warten" is the default and asks again after another timeout).
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanStop))]
    private async Task StopAsync()
    {
        var session = _session;
        if (session == null) return;
        var resumeTo = Phase == LaunchPhase.Starting ? LaunchPhase.Starting : LaunchPhase.Running;
        Phase = LaunchPhase.Ending;
        StatusNote = Loc.T("launch.closing");
        Log.Info($"Spiel beenden: asking '{session.Plan.Game.DisplayTitle}' to close");
        TestHooks.Report("quit-requested");
        while (true)
        {
            bool closed;
            try { closed = await session.RequestCloseAsync(CloseTimeout).ConfigureAwait(true); }
            catch (Exception ex) { Log.Warn("RequestClose failed", ex); closed = false; }
            TestHooks.Report("close-result", ("closed", closed));
            if (closed || _session != session) return;

            // still running: RetroHub must be visible for the question, and the controller must work for it
            if (IsShellHidden)
            {
                IsShellHidden = false;
                Presentation = LaunchPresentation.Overlay;
            }
            try { _ctx.Services.Input.SetSuspended(false); } catch (Exception ex) { Log.Warn("Input resume failed", ex); }
            var question = new DialogViewModel
            {
                Title = Loc.T("launch.forceQuit.title"),
                Message = Loc.T("launch.forceQuit.message", EmulatorName.Length > 0 ? EmulatorName : Loc.T("launch.theEmulator"), (int)CloseTimeout.TotalSeconds),
                IsDanger = true,
                Buttons =
                [
                    new DialogButton(Loc.T("launch.forceQuit.wait"), "wait", IsCancel: true),
                    new DialogButton(Loc.T("launch.forceQuit.confirm"), "kill", IsPrimary: true, IsDanger: true),
                ],
            };
            // the game may still close by itself while the question is open: then the question goes away on its own
            _forceQuitQuestion = question;
            string? answer;
            try { answer = await _ctx.Dialogs.ShowAsync(question).ConfigureAwait(true); }
            finally { if (_forceQuitQuestion == question) _forceQuitQuestion = null; }
            if (_session != session) return; // it closed while the question was open
            if (answer == "kill")
            {
                Log.Warn($"Hard kill of '{session.Plan.Game.DisplayTitle}' (did not close in {CloseTimeout.TotalSeconds:0} s)");
                TestHooks.Report("killed");
                try { session.Kill(); } catch (Exception ex) { Log.Error("Kill failed", ex); }
                return;
            }
            try { _ctx.Services.Input.SetSuspended(true); } catch (Exception ex) { Log.Warn("Input suspend failed", ex); }
            if (answer != "wait")
            {
                // the question could not be shown: keep playing
                Phase = resumeTo;
                StatusNote = "";
                return;
            }
            // "Weiter warten": ask again after another timeout
        }
    }
}
