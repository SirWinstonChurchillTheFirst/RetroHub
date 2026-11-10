using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using RetroHub.App.Controls;
using RetroHub.App.Localization;
using RetroHub.App.Services;
using RetroHub.App.ViewModels;

namespace RetroHub.App.Views.Pages;

/// <summary>
/// Base class of the page views: generic activation of game tiles (→ game detail) and platform cards (→ platform
/// page) anywhere on the page, the right-click list on boxes and system spines (R2), and the default focus target.
/// </summary>
public abstract class PageBase : UserControl, IPageView
{
    protected PageBase()
    {
        AddHandler(Button.ClickEvent, OnAnyClick, RoutingStrategies.Bubble);
        AddHandler(PointerPressedEvent, OnRightPress, RoutingStrategies.Tunnel);
    }

    /// <summary>
    /// Right click on a box or a system spine: the in-style list (Spielen, Details, Favorit, Ordner öffnen / Öffnen, In der
    /// Bibliothek zeigen). The press is consumed: it neither focuses nor opens the object.
    /// </summary>
    private void OnRightPress(object? sender, PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(this).Properties.PointerUpdateKind != PointerUpdateKind.RightButtonPressed) return;
        if (Page is not { } page || TopLevel.GetTopLevel(this) is not MainWindow window) return;
        var source = e.Source as Avalonia.Visual;
        if (source?.FindAncestorOfType<GameTile>(includeSelf: true) is { DataContext: GameItemViewModel game } tile)
        {
            e.Handled = true;
            window.ShowContext(tile, e.GetPosition(tile), GameEntries(page, game));
        }
        else if (source?.FindAncestorOfType<Controls.SystemSpine>(includeSelf: true) is { DataContext: SystemItemViewModel system } spine)
        {
            e.Handled = true;
            window.ShowContext(spine, e.GetPosition(spine), SystemEntries(page.Context, system));
        }
    }

    private static List<ContextEntry> GameEntries(PageViewModel page, GameItemViewModel game)
    {
        var ctx = page.Context;
        var list = new List<ContextEntry> { new("Icon.Play", Loc.T("detail.play"), () => _ = ctx.Launch.LaunchAsync(game)) };
        // "Details" except on this game's own detail page
        if (page is not GameDetailViewModel { Game: { } shown } || shown.Id != game.Id)
            list.Add(new("Icon.Info", Loc.T("home.details"), () => ctx.Navigator.Navigate(PageKey.GameDetail, game)));
        list.Add(new("Icon.Favorite", Loc.T(game.IsFavorite ? "hint.unfavorite" : "hint.favorite"), () => _ = ctx.ToggleFavoriteAsync(game), Lit: game.IsFavorite));
        var path = game.Game.Path;
        if (!string.IsNullOrEmpty(path) && !path.Contains("://", StringComparison.Ordinal))
        {
            list.Add(new("Icon.Folder", Loc.T("detail.openFolder"), () =>
            {
                string? folder = null;
                try { folder = Path.GetDirectoryName(ctx.Services.Paths.FromPortable(path)); }
                catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException) { _ = ex; }
                if (folder == null || !ShellOpen.Folder(folder)) ctx.Toasts.Show(Loc.T("common.folderMissing", folder ?? path), ToastKind.Warning);
            }));
        }
        return list;
    }

    private static List<ContextEntry> SystemEntries(UiContext ctx, SystemItemViewModel system)
    {
        var list = new List<ContextEntry> { new("Icon.Grid", Loc.T("context.open"), () => ctx.Navigator.Navigate(PageKey.System, system.Id)) };
        if (system.GameCount > 0)
            list.Add(new("Icon.Library", Loc.T("context.showInLibrary"), () => ctx.Navigator.Navigate(PageKey.Library, new LibraryRequest(SystemId: system.Id))));
        return list;
    }

    protected PageViewModel? Page => DataContext as PageViewModel;

    private void OnAnyClick(object? sender, RoutedEventArgs e)
    {
        if (Page is not { } page) return;
        switch (e.Source)
        {
            case GameTile { DataContext: GameItemViewModel game }:
                page.Context.Navigator.Navigate(PageKey.GameDetail, game);
                e.Handled = true;
                break;
            case Controls.SystemSpine { DataContext: SystemItemViewModel spine }:
                page.Context.Navigator.Navigate(PageKey.System, spine.Id);
                e.Handled = true;
                break;
            // a plain button standing for a game (the Lieferschein's spines): its detail
            case Button { DataContext: GameItemViewModel linked } link when link.Classes.Contains("game-link"):
                page.Context.Navigator.Navigate(PageKey.GameDetail, linked);
                e.Handled = true;
                break;
        }
    }

    /// <summary>
    /// Page-specific directional navigation before the shell's spatial search (e.g. the shelves of Start with their
    /// anchors and remembered positions). Return true when the move was handled.
    /// </summary>
    public virtual bool MoveFocus(RetroHub.App.Input.NavAction action, Control focused) => false;

    /// <summary>Focus target when the page is shown (keyboard/controller). Return false to use the first focusable element.</summary>
    public virtual bool FocusDefault() => false;

    /// <summary>Focuses a control with visible focus (keyboard/controller style).</summary>
    protected static bool FocusVisible(Control? c) => c != null && c.IsEffectivelyVisible && c.Focus(NavigationMethod.Directional);
}
