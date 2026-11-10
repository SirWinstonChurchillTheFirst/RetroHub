using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.VisualTree;
using RetroHub.App.Controls;
using RetroHub.App.Input;
using RetroHub.App.ViewModels;

namespace RetroHub.App.Views.Pages;

/// <summary>
/// The service menu (DESIGN 7.6): left/right on a row change its value (<see cref="ServiceRow.Step"/>) instead of
/// moving the focus; up/down walk the one list; shoulder left/right focus the first row of the neighbouring section.
/// </summary>
public partial class SettingsPage : PageBase
{
    private SettingsViewModel? _vm;
    private readonly TestCard _card = new();

    public SettingsPage()
    {
        InitializeComponent();
        Tv.Card = _card;
        DataContextChanged += (_, _) =>
        {
            if (_vm != null)
            {
                _vm.SectionRequested -= OnSectionRequested;
                _vm.Previewed -= OnPreviewed;
            }
            _vm = DataContext as SettingsViewModel;
            if (_vm != null)
            {
                _vm.SectionRequested += OnSectionRequested;
                _vm.Previewed += OnPreviewed;
            }
        };
    }

    /// <summary>The test-card TV plays what just changed (the light colour follows the resource by itself).</summary>
    private void OnPreviewed(string what)
    {
        if (what == "osd") _card.FlashOsd();
        else if (what == "motion") _card.Glide();
    }

    /// <summary>The test card (previews, tests).</summary>
    internal TestCard Card => _card;

    private IReadOnlyList<ServiceRow> Rows() =>
        SectionList.GetVisualDescendants().OfType<ServiceRow>().Where(r => r.IsEffectivelyVisible).ToList();

    public override bool MoveFocus(NavAction action, Control focused)
    {
        if (focused is not ServiceRow row) return false;
        switch (action)
        {
            case NavAction.Left or NavAction.Right:
                // a value row changes; an action row has nothing to the side (the focus stays)
                row.Step(action == NavAction.Left ? -1 : +1);
                return true;
            case NavAction.Up or NavAction.Down:
                var rows = Rows();
                var at = rows.ToList().IndexOf(row);
                var next = at + (action == NavAction.Down ? 1 : -1);
                if (at < 0 || next < 0 || next >= rows.Count) return false; // up from the first row: the header
                Focus(rows[next]);
                return true;
        }
        return false;
    }

    private void OnSectionRequested(int direction)
    {
        var sections = SectionList.GetVisualDescendants().OfType<StackPanel>().Where(s => s.Classes.Contains("section")).ToList();
        if (sections.Count == 0) return;
        var focused = TopLevel.GetTopLevel(this)?.FocusManager?.GetFocusedElement() as Visual;
        var current = focused == null ? -1 : sections.FindIndex(s => s.IsVisualAncestorOf(focused));
        var next = Math.Clamp(current + direction, 0, sections.Count - 1);
        if (current < 0) next = direction > 0 ? 0 : sections.Count - 1;
        if (sections[next].GetVisualDescendants().OfType<ServiceRow>().FirstOrDefault() is { } first) Focus(first);
    }

    /// <summary>Focus a row and keep it clear of the footer (40 px, DESIGN 8.4) and of the section label above it.</summary>
    private void Focus(ServiceRow row)
    {
        row.Focus(NavigationMethod.Directional);
        row.BringIntoView(new Rect(0, -64, row.Bounds.Width, row.Bounds.Height + 64 + 96));
    }

    public override bool FocusDefault() => Rows().FirstOrDefault() is { } first && FocusVisible(first);
}
