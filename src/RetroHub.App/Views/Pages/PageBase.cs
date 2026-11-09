using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using RetroHub.App.Controls;
using RetroHub.App.ViewModels;

namespace RetroHub.App.Views.Pages;

/// <summary>
/// Base class of the page views: generic activation of game tiles (→ game detail) and platform cards (→ platform
/// page) anywhere on the page, and the default focus target.
/// </summary>
public abstract class PageBase : UserControl, IPageView
{
    protected PageBase()
    {
        AddHandler(Button.ClickEvent, OnAnyClick, RoutingStrategies.Bubble);
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
            case SystemCard { DataContext: SystemItemViewModel system }:
                page.Context.Navigator.Navigate(PageKey.System, system.Id);
                e.Handled = true;
                break;
        }
    }

    /// <summary>Focus target when the page is shown (keyboard/controller). Return false to use the first focusable element.</summary>
    public virtual bool FocusDefault() => false;

    /// <summary>Focuses a control with visible focus (keyboard/controller style).</summary>
    protected static bool FocusVisible(Control? c) => c != null && c.IsEffectivelyVisible && c.Focus(NavigationMethod.Directional);
}
