using CommunityToolkit.Mvvm.ComponentModel;
using RetroHub.App.Input;
using RetroHub.App.Localization;

namespace RetroHub.App.ViewModels;

/// <summary>One controller hint in the bottom bar ("A Starten").</summary>
public sealed record HintItem(NavAction Action, string Label);

/// <summary>Base class of all page view models.</summary>
public abstract partial class PageViewModel(UiContext ctx) : ObservableObject
{
    protected UiContext Ctx { get; } = ctx;

    /// <summary>The shell context (used by page views for generic tile/card activation).</summary>
    internal UiContext Context => Ctx;

    public abstract PageKey Key { get; }

    /// <summary>Optional page ambience (static gradient behind the whole window, e.g. the system accent).</summary>
    [ObservableProperty] public partial Avalonia.Media.IBrush? Ambient { get; protected set; }

    /// <summary>Page title shown in the top bar.</summary>
    [ObservableProperty] public partial string Title { get; protected set; } = "";

    /// <summary>Hints for the bottom bar while this page is shown.</summary>
    public virtual IReadOnlyList<HintItem> Hints =>
    [
        new(NavAction.Accept, Loc.T("hint.select")),
        new(NavAction.Back, Loc.T("hint.back")),
        new(NavAction.Search, Loc.T("hint.search")),
        new(NavAction.Menu, Loc.T("hint.menu")),
    ];

    /// <summary>Called whenever the page becomes visible (also on Back). Load/refresh data here.</summary>
    public virtual void OnNavigatedTo(object? argument, bool isBack) { }

    /// <summary>Called when another page is shown.</summary>
    public virtual void OnNavigatedFrom() { }

    /// <summary>Handles a page-specific action (e.g. View toggles grid/list). Return true when handled.</summary>
    public virtual bool HandleAction(NavAction action) => false;

    protected void RaiseHintsChanged() => OnPropertyChanged(nameof(Hints));
}
