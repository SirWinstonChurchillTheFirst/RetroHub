using Avalonia.Threading;
using RetroHub.App.Services;
using RetroHub.Core.Logging;

namespace RetroHub.App.ViewModels;

/// <summary>Everything a page view model needs: the core services plus the shell's UI services.</summary>
public sealed class UiContext : IDisposable
{
    private readonly DispatcherTimer _libraryDebounce;

    public UiContext(AppServices services)
    {
        Services = services;
        Items = new GameItemCache(services);
        Launch = new LaunchCoordinator(this);
        _libraryDebounce = new DispatcherTimer(TimeSpan.FromMilliseconds(150), DispatcherPriority.Background, (_, _) => FlushLibraryChanged());
        services.Games.Changed += OnRepositoryChanged;
        services.Emulators.Changed += OnEmulatorsChanged;
    }

    public AppServices Services { get; }
    public Navigator Navigator { get; } = new();
    public ToastService Toasts { get; } = new();
    public DialogService Dialogs { get; } = new();
    public GameItemCache Items { get; }
    public LaunchCoordinator Launch { get; }

    /// <summary>Repository changed (debounced, UI thread).</summary>
    public event Action? LibraryChanged;
    /// <summary>Emulator registry changed (UI thread).</summary>
    public event Action? EmulatorsChanged;

    private void OnRepositoryChanged() => Dispatcher.UIThread.Post(() =>
    {
        _libraryDebounce.Stop();
        _libraryDebounce.Start();
    });

    private void FlushLibraryChanged()
    {
        _libraryDebounce.Stop();
        try { LibraryChanged?.Invoke(); } catch (Exception ex) { Log.Error("LibraryChanged handler failed", ex); }
    }

    private void OnEmulatorsChanged() => Dispatcher.UIThread.Post(() =>
    {
        try { EmulatorsChanged?.Invoke(); } catch (Exception ex) { Log.Error("EmulatorsChanged handler failed", ex); }
    });

    /// <summary>Runs work off the UI thread; exceptions are logged and returned as default (never thrown into the UI).</summary>
    public static async Task<T?> Background<T>(Func<T> work, string what)
    {
        try { return await Task.Run(work).ConfigureAwait(true); }
        catch (Exception ex)
        {
            Log.Error($"{what} failed", ex);
            return default;
        }
    }

    /// <summary>Toggles a game's favourite flag (optimistic UI, repository write in the background, rollback on error).</summary>
    public async Task ToggleFavoriteAsync(GameItemViewModel item)
    {
        var fav = !item.IsFavorite;
        item.SetFavoriteLocal(fav);
        try { await Task.Run(() => Services.Games.SetFavorite(item.Id, fav)).ConfigureAwait(true); }
        catch (Exception ex)
        {
            Log.Error("SetFavorite failed", ex);
            item.SetFavoriteLocal(!fav);
            Toasts.Show(Localization.Loc.T("favorite.failed"), ToastKind.Error);
            return;
        }
        Toasts.Show(Localization.Loc.T(fav ? "favorite.added" : "favorite.removed", item.Title), ToastKind.Success, seconds: 2.5);
    }

    public void Dispose()
    {
        Services.Games.Changed -= OnRepositoryChanged;
        Services.Emulators.Changed -= OnEmulatorsChanged;
        _libraryDebounce.Stop();
    }
}
