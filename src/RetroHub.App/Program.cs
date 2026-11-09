using Avalonia;
using RetroHub.App.Services;
using RetroHub.Core;
using RetroHub.Core.Logging;

namespace RetroHub.App;

internal static class Program
{
    /// <summary>Command line of this start (e.g. <c>--demo</c> forces the demo services).</summary>
    public static string[] Args { get; private set; } = [];
    public static AppPaths? Paths { get; private set; }
    public static SingleInstance? Instance { get; private set; }

    [STAThread]
    public static int Main(string[] args)
    {
        Args = args;
        // 1. portable root → 2. log → 3. single instance → 4. Avalonia (settings, services, window in App)
        AppPaths paths;
        try { paths = AppPaths.Resolve(); }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"RetroHub: cannot resolve the portable root: {ex}");
            return 2;
        }
        Paths = paths;
        try { Log.Init(paths.Logs); }
        catch (Exception ex) { Console.Error.WriteLine($"RetroHub: logging unavailable: {ex.Message}"); }
        Log.Info($"RetroHub {typeof(Program).Assembly.GetName().Version} · root {paths.Root} · app {paths.App}");

        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            Log.Error($"Unhandled exception (terminating: {e.IsTerminating})", e.ExceptionObject as Exception);
        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            Log.Error("Unobserved task exception", e.Exception);
            e.SetObserved(); // a forgotten background task must never take RetroHub down
        };

        try
        {
            Instance = SingleInstance.Acquire(paths.Root);
            if (!Instance.IsFirst)
            {
                Log.Info("RetroHub is already running for this root – bringing it to the front");
                Instance.SignalFirstInstance();
                Instance.Dispose();
                Log.Shutdown();
                return 0;
            }
        }
        catch (Exception ex)
        {
            Log.Warn("Single-instance check failed, continuing", ex);
        }

        try
        {
            return BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
        }
        catch (Exception ex)
        {
            Log.Error("Fatal error", ex);
            return 1;
        }
        finally
        {
            Instance?.Dispose();
            Log.Info("RetroHub exit");
            Log.Shutdown();
        }
    }

    /// <summary>Avalonia configuration (also used by the designer).</summary>
    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
}
