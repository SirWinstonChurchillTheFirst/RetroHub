using RetroHub.App.Input;
using RetroHub.App.Services.Demo;
using RetroHub.Core;
using RetroHub.Core.Catalog;
using RetroHub.Core.Config;
using RetroHub.Core.Emulation;
using RetroHub.Core.Library;
using RetroHub.Core.Logging;

namespace RetroHub.App.Services;

/// <summary>
/// Holder of every core service the UI talks to. The UI only knows the contract interfaces; which implementation
/// sits behind them is decided once, in <see cref="Create"/> (the composition root).
/// </summary>
public sealed partial class AppServices : IDisposable
{
    public required AppPaths Paths { get; init; }
    public required SettingsStore Settings { get; init; }
    public required UiPreferencesStore Ui { get; init; }
    public required ICatalog Catalog { get; init; }
    public required IGameRepository Games { get; init; }
    public required ILibraryScanner Scanner { get; init; }
    public required IEmulatorRegistry Emulators { get; init; }
    public required ILaunchPlanner Launcher { get; init; }
    public required ISessionService Sessions { get; init; }
    /// <summary>Controller input; <see cref="NullInputSource"/> until the gamepad module is connected.</summary>
    public IInputSource Input { get; init; } = new NullInputSource();
    /// <summary>True when the design-time demo services are in use (shown in the UI as "Demo-Modus").</summary>
    public bool IsDemo { get; init; }

    /// <summary>
    /// Whether an emulator counts as installed. Demo installs point at exe paths that do not exist on disk, so the
    /// file check of <see cref="EmulatorInstall.IsInstalled"/> is skipped for them – real services are always checked.
    /// </summary>
    public bool IsInstalled(EmulatorInstall? install) =>
        install != null && (install.IsInstalled || (IsDemo && install.ExePath != null));

    /// <summary>
    /// Composition root: real services when available, otherwise the demo services (logged).
    /// <paramref name="forceDemo"/> (command line <c>--demo</c>) always uses the demo services.
    /// </summary>
    public static AppServices Create(AppPaths paths, SettingsStore settings, UiPreferencesStore ui, bool forceDemo = false)
    {
        if (!forceDemo)
        {
            AppServices? real = null;
            try
            {
                CreateReal(paths, settings, ui, ref real);
            }
            catch (Exception ex)
            {
                Log.Error("Creating the core services failed – falling back to the demo services", ex);
                real = null;
            }
            if (real != null)
            {
                Log.Info("Core services: real implementations");
                return real;
            }
            Log.Warn("Core services not available in this build – using the demo services");
        }
        else
        {
            Log.Info("Core services: demo (forced)");
        }
        return CreateDemo(paths, settings, ui);
    }

    // =====================================================================================================
    // INTEGRATION POINT – plug the real core services in here.
    // Implement this partial method in a new file Services/AppServices.Real.cs, e.g.:
    //
    //   static partial void CreateReal(AppPaths paths, SettingsStore settings, UiPreferencesStore ui, ref AppServices? services)
    //   {
    //       var catalog  = Catalog.Load(paths);                      // ICatalog
    //       var games    = new SqliteGameRepository(paths.DatabaseFile);
    //       var registry = new EmulatorRegistry(catalog, paths, settings);
    //       var planner  = new LaunchPlanner(catalog, registry, paths, settings);
    //       services = new AppServices { Paths = paths, Settings = settings, Ui = ui, Catalog = catalog, Games = games,
    //           Scanner = new LibraryScanner(catalog, games, paths, ...), Emulators = registry, Launcher = planner,
    //           Sessions = new SessionService(games, ...), Input = new GamepadInputSource(...) };
    //   }
    //
    // Leaving it unimplemented (as now) keeps services == null → demo services.
    // =====================================================================================================
    static partial void CreateReal(AppPaths paths, SettingsStore settings, UiPreferencesStore ui, ref AppServices? services);

    /// <summary>In-memory demo library (≈66 games, 13 systems). Never touches files.</summary>
    public static AppServices CreateDemo(AppPaths paths, SettingsStore settings, UiPreferencesStore ui)
    {
        var catalog = new DemoCatalog();
        var demoGames = DemoData.CreateGames(catalog);
        var games = new DemoGameRepository(demoGames, DemoData.CreateFolders(paths), DemoData.CreateSessions(demoGames));
        var registry = new DemoEmulatorRegistry(catalog, paths);
        return new AppServices
        {
            Paths = paths,
            Settings = settings,
            Ui = ui,
            Catalog = catalog,
            Games = games,
            Scanner = new DemoLibraryScanner(games),
            Emulators = registry,
            Launcher = new DemoLaunchPlanner(catalog, registry, paths),
            Sessions = new DemoSessionService(games),
            IsDemo = true,
        };
    }

    /// <summary>Disposes the extra resources of the real services (watcher, session service); see AppServices.Real.cs.</summary>
    partial void DisposeReal();

    public void Dispose()
    {
        DisposeReal();
        try { Input.Dispose(); } catch (Exception ex) { Log.Warn("Input source dispose failed", ex); }
        try { Games.Dispose(); } catch (Exception ex) { Log.Warn("Repository dispose failed", ex); }
    }
}
