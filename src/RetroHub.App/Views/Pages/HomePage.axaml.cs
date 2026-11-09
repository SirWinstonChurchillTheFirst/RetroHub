namespace RetroHub.App.Views.Pages;

public partial class HomePage : PageBase
{
    public HomePage() => InitializeComponent();

    public override bool FocusDefault() => FocusVisible(PlayButton);
}
