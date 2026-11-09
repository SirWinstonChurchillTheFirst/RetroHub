using RetroHub.App.ViewModels;

namespace RetroHub.App.Views.Pages;

public partial class SystemPage : PageBase
{
    public SystemPage()
    {
        InitializeComponent();
        // in the system context the ring, the cone and the lit board use the system's light (DESIGN 2.4, 8.1)
        DataContextChanged += (_, _) =>
        {
            if (DataContext is not SystemViewModel vm) return;
            vm.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(SystemViewModel.System)) ApplyLight(vm);
            };
            ApplyLight(vm);
        };
    }

    private void ApplyLight(SystemViewModel vm)
    {
        Covers.FocusVisuals.Light = vm.System?.Look.Light;
        Covers.FocusVisuals.Refresh();
    }

    public override bool FocusDefault() => Covers.FocusWhenReady(Math.Max(0, Covers.SelectedIndex));
}
