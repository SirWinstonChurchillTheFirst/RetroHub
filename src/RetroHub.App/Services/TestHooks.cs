using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Avalonia.Controls;
using Avalonia.Threading;
using RetroHub.App.ViewModels;
using RetroHub.Core.Logging;

namespace RetroHub.App.Services;

/// <summary>
/// Development-only switches for end-to-end tests on the real desktop (see docs/TESTS-SPIELSTART.md):
/// <c>--test-quit-after &lt;s&gt;</c> presses "Spiel beenden" through the normal code path after the game ran that long,
/// <c>--test-exit-after-return &lt;s&gt;</c> closes RetroHub that long after the return to the hub, and
/// <c>--test-report &lt;file&gt;</c> writes JSON lines of what the launch flow and the game screen observed.
/// Honoured only in DEBUG builds – a release build ignores them (logged once).
/// </summary>
public static class TestHooks
{
    /// <summary>True in DEBUG builds only.</summary>
    public static bool Allowed { get; } =
#if DEBUG
        true;
#else
        false;
#endif

    private static readonly Stopwatch Clock = Stopwatch.StartNew();
    private static readonly object Gate = new();
    private static readonly UTF8Encoding NoBom = new(false);

    public static TestHookOptions Options { get; private set; } = new();

    public static bool Enabled => Allowed && Options.Any;

    public static void Configure(TestHookOptions options)
    {
        if (!options.Any) return;
        if (!Allowed)
        {
            Log.Warn("Test switches (--test-*) are ignored in release builds");
            return;
        }
        Options = options;
        Log.Info($"Test hooks on: quit after {options.QuitAfterSeconds?.ToString() ?? "-"} s, exit after return {options.ExitAfterReturnSeconds?.ToString() ?? "-"} s, report {options.ReportFile ?? "-"}");
        Report("hooks-on");
    }

    /// <summary>Appends one JSON line {"t": ms since start, "event": …, …data} to the report file (no-op without one).</summary>
    public static void Report(string evt, params (string Key, object? Value)[] data)
    {
        if (!Allowed || Options.ReportFile is not { } file) return;
        try
        {
            var dict = new Dictionary<string, object?> { ["t"] = Clock.ElapsedMilliseconds, ["event"] = evt };
            foreach (var (k, v) in data) dict[k] = v;
            var line = JsonSerializer.Serialize(dict) + Environment.NewLine;
            lock (Gate) File.AppendAllText(file, line, NoBom);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            Log.Warn("Test report could not be written", ex);
        }
    }

    /// <summary>
    /// Wires --test-quit-after (press "Spiel beenden" after the session ran that long, same command as the button) and
    /// --test-exit-after-return (close the window that long after the return / after a launch that did not start).
    /// </summary>
    public static void Attach(LaunchCoordinator launch, Window window, Action? releaseEmbedded = null, Action<bool>? setFullscreen = null,
        UiContext? ctx = null)
    {
        if (!Enabled) return;
        if (Options.ReleaseAfterSeconds is double release && releaseEmbedded != null)
            launch.SessionBegan += session => DispatcherTimer.RunOnce(() =>
            {
                if (launch.Session != session) return;
                Report("test-release", ("embedded", launch.IsEmbedded));
                if (!launch.IsEmbedded) return;
                releaseEmbedded();
                // who has the foreground a moment later (the released game must keep it)
                DispatcherTimer.RunOnce(() => Report("release-foreground", ("foregroundPid", ForegroundPid()),
                    ("retroHubPid", Environment.ProcessId), ("gamePid", SafePid(session))), TimeSpan.FromSeconds(1.5));
            }, TimeSpan.FromSeconds(release));
        if (Options.FullscreenAfterSeconds is double full && setFullscreen != null)
            launch.SessionBegan += session => DispatcherTimer.RunOnce(() =>
            {
                if (launch.Session != session) return;
                Report("test-fullscreen", ("embedded", launch.IsEmbedded));
                setFullscreen(true);
            }, TimeSpan.FromSeconds(full));
        if (Options.OverlaysAfterSeconds is double overlays && ctx != null)
            launch.SessionBegan += session => DispatcherTimer.RunOnce(() =>
            {
                if (launch.Session != session) return;
                Report("test-toast");
                ctx.Toasts.Show(Localization.Loc.T("test.toast"), ToastKind.Info, Localization.Loc.T("test.toast.detail"), seconds: 5);
                DispatcherTimer.RunOnce(() =>
                {
                    if (launch.Session != session) return;
                    Report("test-dialog-open", ("keyboardFocus", Controls.GameScreenNative.KeyboardFocus().ToString("X")));
                    _ = ctx.Dialogs.InfoAsync(Localization.Loc.T("test.dialog.title"), Localization.Loc.T("test.dialog.message"));
                    DispatcherTimer.RunOnce(() =>
                    {
                        Report("test-dialog-close");
                        ctx.Dialogs.Current?.Cancel();
                        DispatcherTimer.RunOnce(() => Report("test-dialog-closed", ("keyboardFocus", Controls.GameScreenNative.KeyboardFocus().ToString("X"))),
                            TimeSpan.FromSeconds(1));
                    }, TimeSpan.FromSeconds(3));
                }, TimeSpan.FromSeconds(2.5));
            }, TimeSpan.FromSeconds(overlays));
        if (Options.QuitAfterSeconds is double quit)
            launch.SessionBegan += session => DispatcherTimer.RunOnce(() =>
            {
                if (launch.Session != session) return;
                Report("test-quit", ("canExecute", launch.StopCommand.CanExecute(null)), ("phase", launch.Phase.ToString()));
                if (launch.StopCommand.CanExecute(null)) launch.StopCommand.Execute(null);
            }, TimeSpan.FromSeconds(quit));
        launch.Returned += (_, _) => ScheduleExit(window);
        launch.LaunchAborted += _ => ScheduleExit(window);
    }

    private static int? SafePid(Core.Emulation.IGameSession session)
    {
        try { return session.Process?.Id; }
        catch (InvalidOperationException) { return null; }
    }

    private static int ForegroundPid()
    {
        if (!OperatingSystem.IsWindows()) return 0;
        var fg = Controls.GameScreenNative.GetForegroundWindow();
        return fg == 0 ? 0 : (int)Controls.GameScreenNative.ProcessOf(fg);
    }

    /// <summary>--test-exit-after-return: closes the window after the configured delay (no-op without the switch).</summary>
    public static void ScheduleExit(Window window)
    {
        if (!Enabled || Options.ExitAfterReturnSeconds is not double seconds) return;
        DispatcherTimer.RunOnce(() =>
        {
            Report("test-exit", ("visible", window.IsVisible), ("active", window.IsActive));
            window.Close();
        }, TimeSpan.FromSeconds(seconds));
    }
}
