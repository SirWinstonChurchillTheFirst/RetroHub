using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RetroHub.App.Localization;
using RetroHub.Core.Catalog;
using RetroHub.Core.Emulation;
using RetroHub.Core.Logging;

namespace RetroHub.App.ViewModels;

public enum LaunchPhase { Idle, Starting, Running, Ending }

/// <summary>
/// Starting a game from the UI: plan → show problems honestly or the "Spiel wird gestartet" transition → start the
/// session → running overlay (or the embedded <c>GameScreenHost</c>) → back to the game detail when the session ends.
/// </summary>
public sealed partial class LaunchCoordinator : ObservableObject
{
    private readonly UiContext _ctx;
    private IGameSession? _session;
    private readonly DispatcherTimer _clock;

    public LaunchCoordinator(UiContext ctx)
    {
        _ctx = ctx;
        // note: the (interval, priority, callback) constructor would start the timer immediately
        _clock = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromSeconds(1) };
        _clock.Tick += (_, _) => UpdateElapsed();
    }

    [ObservableProperty] public partial LaunchPhase Phase { get; private set; }
    [ObservableProperty] public partial GameItemViewModel? Game { get; private set; }
    [ObservableProperty] public partial string EmulatorName { get; private set; } = "";
    [ObservableProperty] public partial string ModeText { get; private set; } = "";
    [ObservableProperty] public partial string ElapsedText { get; private set; } = "";
    [ObservableProperty] public partial bool IsEmbedded { get; private set; }

    public bool IsActive => Phase != LaunchPhase.Idle;
    public bool IsStarting => Phase == LaunchPhase.Starting;
    public bool IsRunning => Phase is LaunchPhase.Running or LaunchPhase.Ending;

    partial void OnPhaseChanged(LaunchPhase value)
    {
        OnPropertyChanged(nameof(IsActive));
        OnPropertyChanged(nameof(IsStarting));
        OnPropertyChanged(nameof(IsRunning));
        StopCommand.NotifyCanExecuteChanged();
    }

    /// <summary>Minimum time the start transition stays visible (feels intentional, hides process start-up).</summary>
    public TimeSpan MinimumTransition { get; set; } = TimeSpan.FromMilliseconds(900);

    public async Task LaunchAsync(GameItemViewModel item, string? emulatorOverride = null, LaunchMode? modeOverride = null)
    {
        if (Phase != LaunchPhase.Idle || _ctx.Services.Sessions.Current != null)
        {
            _ctx.Toasts.Show(Loc.T("launch.alreadyRunning"), ToastKind.Warning);
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
            return;
        }
        if (!plan.CanLaunch)
        {
            await _ctx.Dialogs.InfoAsync(Loc.T("launch.cannotStart.title"), Loc.T("launch.cannotStart.message", item.Title),
                plan.Problems.Select(p => new DialogDetail(p.Message, p.Blocking)).ToList());
            return;
        }

        Game = item;
        EmulatorName = plan.Emulator?.Def.Name + (plan.Core != null ? $" · {plan.Core}" : "");
        ModeText = Fmt.Mode(plan.Mode);
        IsEmbedded = plan.Mode == LaunchMode.Embedded;
        ElapsedText = "";
        Phase = LaunchPhase.Starting;
        var shown = Task.Delay(MinimumTransition);
        try
        {
            var session = await Task.Run(() => services.Sessions.Start(plan)).ConfigureAwait(true);
            if (session.State == SessionState.Failed)
            {
                // the session service reports a failed process start as a Failed session (no events), not as an exception
                Log.Warn($"Starting '{item.Title}' failed: {session.Error}");
                Phase = LaunchPhase.Idle;
                Game = null;
                await _ctx.Dialogs.InfoAsync(Loc.T("launch.failed.title"),
                    Loc.T("launch.failed.message", item.Title, session.Error ?? Loc.T("launch.unknownError")));
                return;
            }
            if (plan.UseShellExecute || (session.State == SessionState.Exited && session.Process == null))
            {
                // shell start (.lnk / steam://): handed off to another program, cannot be followed – no running overlay
                await shown.ConfigureAwait(true);
                Phase = LaunchPhase.Idle;
                Game = null;
                _ctx.Toasts.Show(Loc.T("launch.detached", item.Title), ToastKind.Info, Loc.T("launch.detached.detail"), seconds: 6);
                return;
            }
            _session = session;
            session.Ended += OnSessionEnded;
            if (session.State is SessionState.Exited) OnSessionEnded(session);
            try { services.Input.SetSuspended(true); } catch (Exception ex) { Log.Warn("Input suspend failed", ex); }
            await shown.ConfigureAwait(true);
            if (_session == session && Phase == LaunchPhase.Starting)
            {
                Phase = LaunchPhase.Running;
                _clock.Start();
            }
            foreach (var warning in plan.Problems.Where(p => !p.Blocking))
                _ctx.Toasts.Show(warning.Message, ToastKind.Warning);
        }
        catch (Exception ex)
        {
            Log.Error($"Starting '{item.Title}' failed", ex);
            Phase = LaunchPhase.Idle;
            Game = null;
            await _ctx.Dialogs.InfoAsync(Loc.T("launch.failed.title"), Loc.T("launch.failed.message", item.Title, ex.Message));
        }
    }

    private void OnSessionEnded(IGameSession session) => Dispatcher.UIThread.Post(() =>
    {
        if (_session != session) return;
        session.Ended -= OnSessionEnded;
        _session = null;
        _clock.Stop();
        try { _ctx.Services.Input.SetSuspended(false); } catch (Exception ex) { Log.Warn("Input resume failed", ex); }
        var item = Game;
        Phase = LaunchPhase.Idle;
        Game = null;
        if (item == null) return;
        if (session.State == SessionState.Failed || session.Error != null)
            _ctx.Toasts.Show(Loc.T("launch.endedWithError", item.Title), ToastKind.Error, session.Error);
        else
            _ctx.Toasts.Show(Loc.T("launch.ended", item.Title), ToastKind.Success, Loc.T("launch.sessionTime", Fmt.PlayTime((long)session.Elapsed.TotalSeconds)));
        if (!(_ctx.Navigator.Current == PageKey.GameDetail && Equals(_ctx.Navigator.CurrentArgument, item)))
            _ctx.Navigator.Navigate(PageKey.GameDetail, item);
    });

    private void UpdateElapsed()
    {
        if (_session == null) return;
        var t = _session.Elapsed;
        ElapsedText = t.TotalHours >= 1 ? t.ToString(@"h\:mm\:ss") : t.ToString(@"mm\:ss");
    }

    private bool CanStop() => Phase == LaunchPhase.Running;

    /// <summary>Asks the emulator to close; offers to force-quit when it does not react.</summary>
    [RelayCommand(CanExecute = nameof(CanStop))]
    private async Task StopAsync()
    {
        var session = _session;
        if (session == null) return;
        Phase = LaunchPhase.Ending;
        bool closed;
        try { closed = await session.RequestCloseAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(true); }
        catch (Exception ex) { Log.Warn("RequestClose failed", ex); closed = false; }
        if (closed || _session != session) return;
        var kill = await _ctx.Dialogs.ConfirmAsync(Loc.T("launch.forceQuit.title"), Loc.T("launch.forceQuit.message"), Loc.T("launch.forceQuit.confirm"), danger: true);
        if (kill)
        {
            try { session.Kill(); } catch (Exception ex) { Log.Error("Kill failed", ex); }
        }
        else if (_session == session) Phase = LaunchPhase.Running;
    }
}
