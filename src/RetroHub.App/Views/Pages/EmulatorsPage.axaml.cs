using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;

namespace RetroHub.App.Views.Pages;

public partial class EmulatorsPage : PageBase
{
    public EmulatorsPage()
    {
        InitializeComponent();
        // not for a mouse press: scrolling then would move the row away under the pointer and the click would be lost
        AddHandler(GotFocusEvent, (_, e) => { if (e.NavigationMethod != NavigationMethod.Pointer) KeepClear(e.Source as Control); }, RoutingStrategies.Bubble);
        // the niche shows the system of the focused row (and of the first row before anything is focused)
        AddHandler(GotFocusEvent, (_, e) =>
        {
            if (e.Source is Control { DataContext: ViewModels.EmulatorRowViewModel { Look: { } look } }) BayNiche.Look = look;
        }, RoutingStrategies.Bubble);
        Frame.LayoutUpdated += (_, _) =>
        {
            if (BayNiche.Look == null && DataContext is ViewModels.EmulatorsViewModel { Rows: [{ Look: { } first }, ..] }) BayNiche.Look = first;
        };
        // a segment of the Gerätefach clicked: its row (opened by the model) comes into view and takes the focus
        DataContextChanged += (_, _) =>
        {
            if (DataContext is ViewModels.EmulatorsViewModel vm)
            {
                vm.SegmentOpened -= OnSegmentOpened;
                vm.SegmentOpened += OnSegmentOpened;
            }
        };
        // the actions stand once: in the Gerätefach when the side column is shown, else in the head (QA 2)
        Frame.LayoutUpdated += (_, _) =>
        {
            var head = !Frame.SideShown;
            if (HeadButtons.IsVisible != head) HeadButtons.IsVisible = head;
        };
    }

    private void OnSegmentOpened(ViewModels.EmulatorRowViewModel row) => Avalonia.Threading.Dispatcher.UIThread.Post(() =>
    {
        var button = List.GetVisualDescendants().OfType<Button>().FirstOrDefault(b => b.Classes.Contains("sysrow") && ReferenceEquals(b.DataContext, row));
        if (button == null) return;
        button.Focus(NavigationMethod.Pointer);
        KeepClear(button);
    }, Avalonia.Threading.DispatcherPriority.Loaded);

    /// <summary>
    /// A focused row or button stays clear of the footer and its fade (DESIGN 8.4: the footer never hides the focus) and
    /// keeps the line above it in view.
    /// </summary>
    internal static void KeepClear(Control? c)
    {
        if (c == null || c.Bounds.Height <= 0) return;
        c.BringIntoView(new Rect(0, -48, c.Bounds.Width, c.Bounds.Height + 48 + 96));
    }

    /// <summary>The first system row (the opened one when the page was filtered from a game), else "Neu suchen".</summary>
    public override bool FocusDefault() =>
        FocusVisible(List.GetVisualDescendants().OfType<Button>().FirstOrDefault(b => b.Classes.Contains("sysrow") && b.IsEffectivelyVisible))
        || FocusVisible(RefreshButton) || FocusVisible(BayRefreshButton);
}
