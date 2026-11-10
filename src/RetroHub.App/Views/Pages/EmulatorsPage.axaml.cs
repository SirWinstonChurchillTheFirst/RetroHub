using Avalonia.Controls;
using Avalonia.VisualTree;

namespace RetroHub.App.Views.Pages;

public partial class EmulatorsPage : PageBase
{
    public EmulatorsPage() => InitializeComponent();

    /// <summary>The first system row (the opened one when the page was filtered from a game), else "Neu suchen".</summary>
    public override bool FocusDefault() =>
        FocusVisible(List.GetVisualDescendants().OfType<Button>().FirstOrDefault(b => b.Classes.Contains("sysrow") && b.IsEffectivelyVisible))
        || FocusVisible(RefreshButton);
}
