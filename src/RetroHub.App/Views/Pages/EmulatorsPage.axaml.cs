namespace RetroHub.App.Views.Pages;

public partial class EmulatorsPage : PageBase
{
    public EmulatorsPage() => InitializeComponent();

    public override bool FocusDefault() => FocusVisible(RefreshButton);
}
