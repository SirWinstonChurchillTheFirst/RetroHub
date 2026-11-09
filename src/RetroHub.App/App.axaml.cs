using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using RetroHub.App.Localization;
using RetroHub.App.Services;
using RetroHub.App.Theming;
using RetroHub.App.ViewModels;
using RetroHub.App.Views;
using RetroHub.Core;
using RetroHub.Core.Config;
using RetroHub.Core.IO;
using RetroHub.Core.Logging;

namespace RetroHub.App;

public partial class App : Application
{
    private UiContext? _ctx;
    private MainViewModel? _main;
    private int _uiErrors;
    private DateTime _lastUiError;

    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    /// <summary>
    /// Everything that must happen before the first window: language, accent, motion. Also used by the headless tests.
    /// </summary>
    public static void InitializeUi(AppSettings settings, string? resourcesDir)
    {
        Loc.Load(settings.Language, resourcesDir);
        ThemeManager.ApplyAccent(settings.Accent);
        Motion.Apply(settings.Motion);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        base.OnFrameworkInitializationCompleted();
        // tests and the designer set things up themselves
        if (ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime desktop || Program.Paths is not { } paths) return;

        Dispatcher.UIThread.UnhandledException += OnUiException;
        try
        {
            var settings = new SettingsStore(paths.SettingsFile);
            settings.Load();
            var ui = new UiPreferencesStore(Path.Combine(paths.Config, "ui.json"));
            ui.Load();
            try { SafeFile.SweepTemps(paths.Config); } catch (Exception ex) { Log.Warn("Temp sweep failed", ex); }
            InitializeUi(settings.Current, paths.Resources);

            // --demo, --launch <path-or-id> [--launch-mode …] [--emulator …], dev-only --test-* switches
            var options = CommandLineOptions.Parse(Program.Args, Environment.CurrentDirectory);
            TestHooks.Configure(options.Test);
            var forceDemo = options.Demo;
            var services = AppServices.Create(paths, settings, ui, forceDemo);
            try
            {
                paths.EnsureCreated(services.Catalog.Systems.Select(s => s.Folders.FirstOrDefault() ?? s.Id));
            }
            catch (Exception ex) { Log.Warn("Creating the portable folders failed", ex); }

            _ctx = new UiContext(services);
            _main = new MainViewModel(_ctx);
            var window = new MainWindow(_main);
            desktop.MainWindow = window;
            desktop.ShutdownMode = ShutdownMode.OnMainWindowClose;
            desktop.Exit += (_, _) => Shutdown();
            TestHooks.Attach(_ctx.Launch, window, () => window.GameScreenControl.ReleaseToDesktop());
            var ctx = _ctx;
            // a second start: front, then whatever it asked for (e.g. --launch from a desktop shortcut)
            Program.Instance?.Listen(() => Dispatcher.UIThread.Post(() =>
            {
                window.BringToFront();
                foreach (var request in SingleInstance.TakeRequests(Program.RequestFolder(paths)))
                {
                    var forwarded = CommandLineOptions.Parse(request.Args, request.WorkingDirectory);
                    if (forwarded.Launch == null) continue;
                    Log.Info("Launch request forwarded by a second start");
                    _ = CommandLineLaunch.RunAsync(ctx, forwarded.Launch);
                }
            }));
            // Opened is raised again whenever the window is shown after Hide() (fullscreen hand-off): only the first counts
            var opened = false;
            window.Opened += (_, _) => Dispatcher.UIThread.Post(() =>
            {
                if (opened) return;
                opened = true;
                _main?.StartBackgroundTasks();
                foreach (var error in options.Errors) ctx.Toasts.Show(error, ToastKind.Warning, seconds: 8);
                if (options.Launch is { } launch)
                    Dispatcher.UIThread.Post(async () =>
                    {
                        if (!await CommandLineLaunch.RunAsync(ctx, launch)) TestHooks.ScheduleExit(window);
                    }, DispatcherPriority.Background);
                else if (options.Errors.Count > 0) TestHooks.ScheduleExit(window);
            }, DispatcherPriority.Background);
            Log.Info($"UI ready ({(services.IsDemo ? "demo" : "real")} services, {services.Catalog.Systems.Count} systems)");
        }
        catch (Exception ex)
        {
            Log.Error("Startup failed", ex);
            throw;
        }
    }

    private void Shutdown()
    {
        try
        {
            _main?.Dispose();
            _ctx?.Dispose();
            _ctx?.Services.Dispose();
        }
        catch (Exception ex) { Log.Warn("Shutdown cleanup failed", ex); }
    }

    /// <summary>UI-thread exceptions: logged and shown as a toast; a burst of errors (loop) ends the app cleanly.</summary>
    private void OnUiException(object? sender, DispatcherUnhandledExceptionEventArgs e)
    {
        Log.Error("Unhandled UI exception", e.Exception);
        var now = DateTime.UtcNow;
        _uiErrors = now - _lastUiError < TimeSpan.FromSeconds(3) ? _uiErrors + 1 : 1;
        _lastUiError = now;
        if (_uiErrors >= 5)
        {
            Log.Error("Repeated UI exceptions – shutting down");
            return; // let it propagate: something is fundamentally broken
        }
        e.Handled = true;
        _ctx?.Toasts.Show(Loc.T("error.unexpected"), ToastKind.Error, e.Exception.Message, seconds: 8);
    }
}
