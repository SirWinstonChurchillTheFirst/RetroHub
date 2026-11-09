using RetroHub.App.Input;
using RetroHub.Core;
using RetroHub.Core.Catalog;
using RetroHub.Core.Config;
using RetroHub.Core.Data;
using RetroHub.Core.Emulation;
using RetroHub.Core.Emulation.Portable;
using RetroHub.Core.IO;
using RetroHub.Core.Library;
using RetroHub.Core.Logging;

namespace RetroHub.App.Services;

/// <summary>Composition root for the real core services (SQLite library, JSON catalog, scanner, emulators, sessions, gamepad).</summary>
public sealed partial class AppServices
{
    /// <summary>Watches the library folders (null in demo mode or when "Ordner überwachen" is off).</summary>
    public FolderWatcher? Watcher { get; private set; }

    /// <summary>Set when a broken database was copied aside at startup (shown once to the user).</summary>
    public string? PreservedCorruptDatabase { get; init; }

    static partial void CreateReal(AppPaths paths, SettingsStore settings, UiPreferencesStore ui, ref AppServices? services)
    {
        // Leftover temp files of a killed process (atomic writes) – never user files.
        SafeFile.SweepTemps(paths.Data);

        var catalog = RetroHub.Core.Catalog.Catalog.Load(paths);
        foreach (var w in catalog.LoadWarnings) Log.Warn($"Catalog: {w}");

        // games/<system>/ for every system (first folder name = canonical), plus all standard folders.
        paths.EnsureCreated(catalog.Systems.Where(s => s.Folders.Length > 0).Select(s => s.Folders[0]));

        var games = new SqliteGameRepository(paths.DatabaseFile);
        try
        {
            games.EnsureBuiltInFolder(paths.ToPortable(paths.Games));
            var registry = new EmulatorRegistry(catalog, paths, settings);
            var planner = new LaunchPlanner(catalog, registry, paths, settings);
            var sessions = new SessionService(games, paths, PortableSetups.All);
            // A game that was still running when RetroHub crashed/closed: re-attach or record its playtime.
            sessions.RecoverInterruptedSession();

            var s = settings.Current;
            var created = new AppServices
            {
                Paths = paths,
                Settings = settings,
                Ui = ui,
                Catalog = catalog,
                Games = games,
                Scanner = new LibraryScanner(catalog, games, paths),
                Emulators = registry,
                Launcher = planner,
                Sessions = sessions,
                Input = GamepadInputSource.TryCreate(swapAcceptBack: false),
                PreservedCorruptDatabase = games.PreservedCorruptFile,
                IsDemo = false,
            };
            created.ConfigureWatcher(s.WatchFolders);
            settings.Saved += st => created.ConfigureWatcher(st.WatchFolders);
            games.Changed += created.OnRepositoryChanged;
            services = created;
        }
        catch
        {
            games.Dispose();
            throw;
        }
    }

    private readonly object _watchGate = new();

    /// <summary>Starts/stops the folder watcher according to the setting "Ordner überwachen".</summary>
    private void ConfigureWatcher(bool enabled)
    {
        lock (_watchGate)
        {
            if (!enabled || IsDemo)
            {
                Watcher?.Dispose();
                Watcher = null;
                WatcherChanged?.Invoke();
                return;
            }
            if (Watcher == null)
            {
                Watcher = new FolderWatcher(Paths);
                WatcherChanged?.Invoke();
            }
            Watcher.SetFolders(Games.Folders().Where(f => f.Enabled));
        }
    }

    /// <summary>Raised when <see cref="Watcher"/> was created or removed (the shell subscribes to its FoldersChanged).</summary>
    public event Action? WatcherChanged;

    private int _folderCount = -1;

    private void OnRepositoryChanged()
    {
        // Library folders added/removed on the Imports page → watch the new set.
        if (Watcher == null) return;
        try
        {
            var folders = Games.Folders();
            var signature = folders.Count * 31 + folders.Count(f => f.Enabled);
            if (Interlocked.Exchange(ref _folderCount, signature) != signature)
                Watcher.SetFolders(folders.Where(f => f.Enabled));
        }
        catch (Exception ex) { Log.Warn("Updating watched folders failed", ex); }
    }

    partial void DisposeReal()
    {
        try { Watcher?.Dispose(); } catch (Exception ex) { Log.Warn("Watcher dispose failed", ex); }
        if (Sessions is IDisposable d)
        {
            try { d.Dispose(); } catch (Exception ex) { Log.Warn("Session service dispose failed", ex); }
        }
    }
}
