using Avalonia.Controls;
using Avalonia.VisualTree;
using RetroHub.App.Controls;

namespace RetroHub.App.Views.Pages;

public partial class SystemsPage : PageBase
{
    /// <summary>Preferred minimum card width; the column count follows the available width.</summary>
    private const double CardWidth = 270;

    public SystemsPage()
    {
        InitializeComponent();
        Cards.SizeChanged += (_, e) => UpdateColumns(e.NewSize.Width);
    }

    private void UpdateColumns(double width)
    {
        if (Cards.ItemsPanelRoot is not Avalonia.Controls.Primitives.UniformGrid grid || width <= 0) return;
        var cols = Math.Max(1, (int)((width + 20) / (CardWidth + 20)));
        if (grid.Columns != cols) grid.Columns = cols;
    }

    public override bool FocusDefault() => FocusVisible(Cards.GetVisualDescendants().OfType<SystemCard>().FirstOrDefault());
}
