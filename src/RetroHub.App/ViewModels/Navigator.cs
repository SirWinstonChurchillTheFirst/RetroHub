using CommunityToolkit.Mvvm.ComponentModel;

namespace RetroHub.App.ViewModels;

public enum PageKey { Home, Library, Systems, System, GameDetail, Emulators, Imports, Settings }

/// <summary>A navigation change: which page, its argument, and whether it came from Back.</summary>
public sealed record NavigationEntry(PageKey Key, object? Argument, bool IsBack);

/// <summary>
/// Page navigation with a back stack. Top-level destinations (sidebar entries) reset the stack; drill-in pages
/// (system, game detail) push onto it.
/// </summary>
public sealed partial class Navigator : ObservableObject
{
    private readonly Stack<(PageKey Key, object? Arg)> _back = new();

    [ObservableProperty] public partial PageKey Current { get; private set; } = PageKey.Home;
    [ObservableProperty] public partial object? CurrentArgument { get; private set; }
    [ObservableProperty] public partial bool CanGoBack { get; private set; }

    public event Action<NavigationEntry>? Navigated;

    /// <summary>Sidebar entries.</summary>
    public static bool IsTopLevel(PageKey key) => key is not (PageKey.System or PageKey.GameDetail);

    public IReadOnlyCollection<(PageKey Key, object? Arg)> BackStack => _back;

    public void Navigate(PageKey key, object? argument = null)
    {
        if (key == Current && Equals(argument, CurrentArgument) && IsTopLevel(key))
        {
            Raise(new NavigationEntry(key, argument, false)); // re-select → page may refresh/scroll to top
            return;
        }
        if (IsTopLevel(key)) _back.Clear();
        else _back.Push((Current, CurrentArgument));
        Set(key, argument);
        Raise(new NavigationEntry(key, argument, false));
    }

    /// <summary>
    /// The current page shows another subject in place (the system page's shoulders): the argument changes, the back
    /// stack does not, so Back still returns to where the page was opened from.
    /// </summary>
    public void ReplaceArgument(object? argument) => CurrentArgument = argument;

    public bool GoBack()
    {
        if (_back.Count == 0) return false;
        var (key, arg) = _back.Pop();
        Set(key, arg);
        Raise(new NavigationEntry(key, arg, true));
        return true;
    }

    /// <summary>The top-level page the current page belongs to (for the sidebar highlight).</summary>
    public PageKey Section
    {
        get
        {
            if (IsTopLevel(Current)) return Current;
            foreach (var (k, _) in _back) if (IsTopLevel(k)) return k;
            return PageKey.Home;
        }
    }

    private void Set(PageKey key, object? arg)
    {
        Current = key;
        CurrentArgument = arg;
        CanGoBack = _back.Count > 0;
        OnPropertyChanged(nameof(Section));
    }

    private void Raise(NavigationEntry e) => Navigated?.Invoke(e);
}
