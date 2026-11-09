using Avalonia;
using Avalonia.Controls;
using Avalonia.VisualTree;
using RetroHub.App.Controls;

namespace RetroHub.App.Views.Pages;

public partial class SystemsPage : PageBase
{
    public SystemsPage()
    {
        InitializeComponent();
        // the boards run edge to edge: bleed them over the page margin
        Cards.SizeChanged += (_, _) =>
        {
            if (Cards.ItemsPanelRoot is ShelfGridPanel panel && Cards.TranslatePoint(new Point(0, 0), this) is { } p) panel.BoardBleed = p.X;
        };
    }

    public override bool FocusDefault() => FocusVisible(Cards.GetVisualDescendants().OfType<SystemCard>().FirstOrDefault());
}
