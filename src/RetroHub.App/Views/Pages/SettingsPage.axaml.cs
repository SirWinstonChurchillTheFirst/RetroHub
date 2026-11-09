using Avalonia.Controls;
using Avalonia.VisualTree;

namespace RetroHub.App.Views.Pages;

public partial class SettingsPage : PageBase
{
    public SettingsPage() => InitializeComponent();

    public override bool FocusDefault() => FocusVisible(AccentList.GetVisualDescendants().OfType<Button>().FirstOrDefault());
}
