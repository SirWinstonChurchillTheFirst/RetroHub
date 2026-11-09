using Avalonia;
using Avalonia.Headless;
using Avalonia.Threading;
using RetroHub.App.Services;
using RetroHub.App.Tests;
using RetroHub.App.ViewModels;
using RetroHub.Core;
using RetroHub.Core.Config;
using Xunit;

[assembly: AvaloniaTestApplication(typeof(TestAppBuilder))]
// UI tests share one dispatcher and static state (Loc, Motion, ThemeManager): run them one after another
[assembly: CollectionBehavior(CollectionBehavior.CollectionPerAssembly)]

namespace RetroHub.App.Tests;

/// <summary>Headless Avalonia with real Skia rendering (needed for the screenshots).</summary>
public static class TestAppBuilder
{
    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UseSkia()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
            .WithInterFont();
}

/// <summary>Runs a test body on the headless Avalonia dispatcher (one session per test assembly).</summary>
internal static class UiTest
{
    private static readonly Lazy<HeadlessUnitTestSession> Session =
        new(() => HeadlessUnitTestSession.GetOrStartForAssembly(typeof(UiTest).Assembly));

    public static Task Run(Action body) => Session.Value.Dispatch(body, TestContext.Current.CancellationToken);
}

internal static class TestSupport
{
    /// <summary>The worktree root (folder with RetroHub.root), found by walking up from the test binaries.</summary>
    public static string RepoRoot { get; } = FindRoot();

    private static string FindRoot()
    {
        for (var d = new DirectoryInfo(AppContext.BaseDirectory); d != null; d = d.Parent)
            if (File.Exists(Path.Combine(d.FullName, AppPaths.RootMarker))) return d.FullName;
        throw new InvalidOperationException("RetroHub.root not found above " + AppContext.BaseDirectory);
    }

    public static string SourceDir => Path.Combine(RepoRoot, "src", "RetroHub.App");

    public static string ScreensDir
    {
        get
        {
            var dir = Path.Combine(RepoRoot, "artifacts", "screens");
            Directory.CreateDirectory(dir);
            return dir;
        }
    }

    /// <summary>Demo services over a throw-away portable root (nothing is written into the repository).</summary>
    public static UiContext CreateContext(Action<AppSettings>? configure = null)
    {
        var root = Path.Combine(Path.GetTempPath(), "retrohub-ui-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var paths = new AppPaths(root, AppContext.BaseDirectory);
        var settings = new SettingsStore(Path.Combine(root, "settings.json"));
        configure?.Invoke(settings.Current);
        var ui = new UiPreferencesStore(null);
        App.InitializeUi(settings.Current, null);
        var services = AppServices.CreateDemo(paths, settings, ui);
        return new UiContext(services);
    }

    /// <summary>Runs dispatcher jobs and render ticks until <paramref name="done"/> is true (or the timeout passes).</summary>
    public static bool Pump(Func<bool> done, int timeoutMs = 4000)
    {
        var until = DateTime.UtcNow.AddMilliseconds(timeoutMs);
        while (DateTime.UtcNow < until)
        {
            Dispatcher.UIThread.RunJobs();
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            if (done()) return true;
            Thread.Sleep(10);
        }
        Dispatcher.UIThread.RunJobs();
        return done();
    }

    /// <summary>Lets layout, bindings and animations settle (≈ <paramref name="ms"/> of render ticks).</summary>
    public static void Settle(int ms = 400)
    {
        var until = DateTime.UtcNow.AddMilliseconds(ms);
        while (DateTime.UtcNow < until)
        {
            Dispatcher.UIThread.RunJobs();
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            Thread.Sleep(8);
        }
        Dispatcher.UIThread.RunJobs();
    }
}
