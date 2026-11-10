using CommunityToolkit.Mvvm.ComponentModel;
using RetroHub.App.Input;
using RetroHub.App.Localization;

namespace RetroHub.App.ViewModels;

/// <summary>One controller hint in the bottom bar ("A Starten").</summary>
public sealed record HintItem(NavAction Action, string Label)
{
    /// <summary>
    /// The footer hint is a mouse target that runs its action (R2). Not for Accept: "Öffnen"/"Starten" acts on the
    /// focused object, which a mouse user clicks directly.
    /// </summary>
    public bool IsClickable => Action != NavAction.Accept;
}

/// <summary>Base class of all page view models.</summary>
public abstract partial class PageViewModel(UiContext ctx) : ObservableObject
{
    protected UiContext Ctx { get; } = ctx;

    /// <summary>The shell context (used by page views for generic tile/card activation).</summary>
    internal UiContext Context => Ctx;

    public abstract PageKey Key { get; }

    /// <summary>Legacy (v0 ambient gradients): unused by the shell, removed in M6.</summary>
    [ObservableProperty] public partial Avalonia.Media.IBrush? Ambient { get; protected set; }

    /// <summary>What the room behind this page shows (TV light, a system wall, or the plain room).</summary>
    [ObservableProperty] public partial BackdropSpec Backdrop { get; protected set; } = BackdropSpec.Room;

    /// <summary>
    /// A tone laid under the header's content (transparent = the room shows through): the detail page sets Raum while
    /// its back is read, so the TV light does not hang above the plain back as a lighter band (M4 QA).
    /// </summary>
    [ObservableProperty] public partial Avalonia.Media.Color HeaderGround { get; set; }

    /// <summary>The dim of an open overlay, continued under the header's content (no seam at the header's edge, M4 QA).</summary>
    [ObservableProperty] public partial Avalonia.Media.Color HeaderDim { get; protected set; }

    /// <summary>The page part of the footer's location sentence ("Pokémon Smaragd-Edition", "66 Spiele"); null = title.</summary>
    [ObservableProperty] public partial string? Location { get; protected set; }

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
