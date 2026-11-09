using Avalonia.Input;
using Avalonia.Threading;
using RetroHub.App.ViewModels;

namespace RetroHub.App.Views.Pages;

public partial class LibraryPage : PageBase
{
    private LibraryViewModel? _vm;
    private bool _searchRequested;

    public LibraryPage()
    {
        InitializeComponent();
        DataContextChanged += (_, _) =>
        {
            if (_vm != null) _vm.FocusSearchRequested -= FocusSearch;
            _vm = DataContext as LibraryViewModel;
            if (_vm != null)
            {
                _vm.FocusSearchRequested += FocusSearch;
                _vm.PropertyChanged += (_, e) =>
                {
                    if (e.PropertyName != nameof(LibraryViewModel.IsListView)) return;
                    var (from, to) = _vm.IsListView ? (Covers, Rows) : (Rows, Covers);
                    if (!from.IsKeyboardFocusWithin) return;
                    var index = Math.Max(0, from.SelectedIndex);
                    Dispatcher.UIThread.Post(() => to.FocusWhenReady(index), DispatcherPriority.Loaded);
                };
            }
        };
    }

    private void FocusSearch()
    {
        _searchRequested = true; // wins over the default focus the shell applies after navigation
        Dispatcher.UIThread.Post(() =>
        {
            if (!_searchRequested) return;
            Search.Focus(NavigationMethod.Directional);
            Search.SelectAll();
        }, DispatcherPriority.Loaded);
    }

    public override bool FocusDefault()
    {
        var grid = _vm?.IsListView == true ? Rows : Covers;
        if (_searchRequested)
        {
            _searchRequested = false;
            grid.CancelPendingFocus();
            return FocusVisible(Search);
        }
        if (_vm is { IsLoaded: true } && grid.ItemCount == 0) return FocusVisible(Search);
        return grid.FocusWhenReady(Math.Max(0, grid.SelectedIndex));
    }
}
