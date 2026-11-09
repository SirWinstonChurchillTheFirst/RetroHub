namespace RetroHub.App.Views.Pages;

public partial class SystemPage : PageBase
{
    public SystemPage() => InitializeComponent();

    public override bool FocusDefault() => Covers.FocusWhenReady(Math.Max(0, Covers.SelectedIndex));
}
