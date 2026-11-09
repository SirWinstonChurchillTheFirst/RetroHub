namespace RetroHub.App.Views.Pages;

public partial class GameDetailPage : PageBase
{
    public GameDetailPage() => InitializeComponent();

    public override bool FocusDefault() => FocusVisible(PlayButton) || FocusVisible(EmulatorBox);
}
