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
        AddHandler(GotFocusEvent, (_, e) => KeepClear(e.Source as Control), RoutingStrategies.Bubble);
        // the actions stand once: in the Gerätefach when the side column is shown, else in the head (QA 2)
        Frame.LayoutUpdated += (_, _) =>
        {
            var head = !Frame.SideShown;
            if (HeadButtons.IsVisible != head) HeadButtons.IsVisible = head;
        };
    }

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
