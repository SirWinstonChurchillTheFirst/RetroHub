using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media.Transformation;
using Avalonia.Threading;
using Avalonia.VisualTree;
using RetroHub.App.Controls;
using RetroHub.App.Input;
using RetroHub.App.Theming;
using RetroHub.App.ViewModels;

namespace RetroHub.App.Views.Pages;

/// <summary>
/// Systemseite (DESIGN 7.5): the wall banner over the grid, the system light on ring, cone and board, the neighbour
/// switch (the banner's text and motif slide out 32 px and the new ones in), the Systeminfo overlay. Up from the first
/// row reaches the banner's buttons, down from the banner returns to the remembered box.
/// </summary>
public partial class SystemPage : PageBase
{
    /// <summary>Below this page height the ≈1100 values apply (banner 300, motif 220, name ≤ 56).</summary>
    public const double CompactBelowHeight = 860;

    private SystemViewModel? _vm;
    private Control? _infoOpener;

    public SystemPage()
    {
        InitializeComponent();
        DataContextChanged += (_, _) =>
        {
            if (_vm != null)
            {
                _vm.PropertyChanged -= OnVmChanged;
                _vm.SystemSwitched -= OnSystemSwitched;
            }
            _vm = DataContext as SystemViewModel;
            if (_vm == null) return;
            _vm.PropertyChanged += OnVmChanged;
            _vm.SystemSwitched += OnSystemSwitched;
            ApplyLight();
        };
        BannerBoard.Casters = [BannerMotif];
        SizeChanged += (_, e) => ApplySize(e.NewSize);
        InfoScrim.PointerPressed += (_, e) =>
        {
            e.Handled = true;
            _vm?.CloseInfoCommand.Execute(null);
        };
    }

    private void OnVmChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(SystemViewModel.System):
                ApplyLight();
                break;
            case nameof(SystemViewModel.SlotAspect):
                // the spine placeholders take the slot format of the system's covers (DESIGN 11.4)
                Covers.PlaceholderAspect = _vm?.SlotAspect ?? 0;
                break;
            case nameof(SystemViewModel.IsInfoOpen):
                ApplyInfo();
                break;
        }
    }

    /// <summary>In the system context ring, cone and lit board use the system's light (DESIGN 2.4, 8.1); fades in its wall.</summary>
    private void ApplyLight()
    {
        if (_vm?.System is not { } system) return;
        Covers.FocusVisuals.Light = system.Look.Light;
        Covers.FocusVisuals.Refresh();
        Covers.TopFadeColor = system.Look.Wall;
        Covers.PlaceholderAspect = _vm.SlotAspect;
    }

    /// <summary>1080 or ≈1100 values: banner 392/300 high, motif 300/220, name 72·56·44 / 56·44·36.</summary>
    private void ApplySize(Size size)
    {
        var compact = size.Height < CompactBelowHeight;
        Banner.Height = compact ? 320 : 392;
        BannerMotif.Width = BannerMotif.Height = compact ? 220 : 300;
        BannerTitle.Sizes = compact ? "56,44,36" : "72,56,44";
        BannerText.Margin = new Thickness(0, 0, compact ? 280 : 380, compact ? 36 : 48);
        Neighbours.Margin = new Thickness(-10, compact ? 20 : 28, 0, 0);
        Covers.Spacing = size.Width >= 1600 ? 28 : 20;
        foreach (var slot in EmptyPlaces.Children.OfType<EmptySlot>().Skip(compact ? 2 : 3)) slot.IsVisible = !compact;
    }

    /// <summary>
    /// The shoulders switched the system: the wall changes its tone (BrushTransition on the page ground, the banner wall
    /// redraws at once), the old text and motif are gone and the new ones slide 32 px in (Glide); nothing moves when
    /// motion is reduced or off.
    /// </summary>
    private void OnSystemSwitched(int direction)
    {
        Covers.FocusWhenReady(0);
        if (Motion.Current != EffectiveMotion.Full) return;
        foreach (var part in new Control[] { BannerText, BannerMotif })
        {
            part.Transitions = null;
            part.Opacity = 0;
            part.RenderTransform = TransformOperations.Parse($"translateX({32 * direction}px)");
        }
        Dispatcher.UIThread.Post(() =>
        {
            foreach (var part in new Control[] { BannerText, BannerMotif })
            {
                part.Transitions =
                [
                    new DoubleTransition { Property = OpacityProperty, Duration = Motion.Glide, Easing = new CubicEaseOut() },
                    new TransformOperationsTransition { Property = RenderTransformProperty, Duration = Motion.Glide, Easing = new CubicEaseOut() },
                ];
                part.Opacity = 1;
                part.RenderTransform = TransformOperations.Parse("translateX(0px)");
            }
        }, DispatcherPriority.Render);
    }

    private void ApplyInfo()
    {
        if (_vm?.IsInfoOpen == true)
        {
            _infoOpener = TopLevel.GetTopLevel(this)?.FocusManager?.GetFocusedElement() as Control;
            Dispatcher.UIThread.Post(() => InfoCloseButton.Focus(NavigationMethod.Directional), DispatcherPriority.Loaded);
            return;
        }
        var back = _infoOpener;
        _infoOpener = null;
        Dispatcher.UIThread.Post(() =>
        {
            if (back is { IsEffectivelyVisible: true }) FocusVisible(back);
            else if (IsEffectivelyVisible) FocusDefault();
        }, DispatcherPriority.Loaded);
    }

    private bool InBanner(Control c) => IsInside(c, Banner);

    private static bool IsInside(Visual v, Visual root)
    {
        for (Visual? x = v; x != null; x = x.GetVisualParent()) if (x == root) return true;
        return false;
    }

    /// <summary>
    /// The overlay keeps the focus; up from the first row reaches the banner's line (Systeminfo), down from the banner
    /// returns to the remembered box (or the way to the imports when the compartment is empty).
    /// </summary>
    public override bool MoveFocus(NavAction action, Control focused)
    {
        if (_vm == null) return false;
        if (_vm.IsInfoOpen)
        {
            InfoCloseButton.Focus(NavigationMethod.Directional);
            return true;
        }
        if (action == NavAction.Up && focused.FindAncestorOfType<CoverGrid>(includeSelf: true) == Covers
            && Covers.SelectedIndex >= 0 && Covers.SelectedIndex < Math.Max(1, Covers.Columns))
            return FocusVisible(InfoButton);
        if (action == NavAction.Up && focused == ImportsButton)
            return Covers.ItemCount > 0 ? Covers.FocusWhenReady(Math.Max(0, Covers.SelectedIndex)) : FocusVisible(InfoButton);
        if (action == NavAction.Down && InBanner(focused))
        {
            if (focused == PrevSystem || focused == NextSystem) return FocusVisible(InfoButton);
            if (Covers.ItemCount > 0) return Covers.FocusWhenReady(Math.Max(0, Covers.SelectedIndex));
            return FocusVisible(ImportsButton);
        }
        return false;
    }

    public override bool FocusDefault()
    {
        if (_vm is { IsLoaded: true, IsEmpty: true }) return FocusVisible(ImportsButton) || FocusVisible(InfoButton);
        return Covers.FocusWhenReady(Math.Max(0, Covers.SelectedIndex));
    }
}
