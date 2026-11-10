using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Immutable;
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
    private bool _compact;

    /// <summary>Height of the wall's light zone: the neighbour row (DESIGN 7.5.1, M5 QA); 24 without neighbours.</summary>
    public double BandHeight => _vm?.HasNeighbours == false ? 24 : _compact ? 64 : 88;

    /// <summary>Gap between the light zone and the title's top (the tone edge never touches a glyph).</summary>
    public const double TitleClearance = 24;

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
        // the grid does not scroll to focused elements by itself (it snaps rows): the footer's button asks for its place,
        // with the footer and its fade below it (they lie over the page's last ≈ 96 px)
        // keyboard/controller only: on a mouse press the scroll would move the button away and the click would be lost
        ImportsButton.GotFocus += (_, e) =>
        {
            if (e.NavigationMethod != NavigationMethod.Pointer) ImportsButton.BringIntoView(new Rect(0, 0, ImportsButton.Bounds.Width, ImportsButton.Bounds.Height + 96));
        };
        InfoButton.GotFocus += (_, e) => { if (e.NavigationMethod != NavigationMethod.Pointer) Banner.BringIntoView(); };
        SizeChanged += (_, e) => ApplySize(e.NewSize);
        BannerSentence.SizeChanged += (_, _) => FitTitle();
        StatusRow.SizeChanged += (_, _) => FitTitle();
        Covers.Scrolled += UpdateCompactHeader;
        // the compact header's name: back up to the banner
        CompactName.PointerPressed += (_, e) =>
        {
            if (!e.GetCurrentPoint(CompactName).Properties.IsLeftButtonPressed) return;
            e.Handled = true;
            Covers.ScrollViewer.Offset = default;
        };
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
            case nameof(SystemViewModel.HasNeighbours):
                ApplyBanner();
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
        var w = system.Look.Wall;
        CompactFade.Background = new ImmutableLinearGradientBrush(
            [new ImmutableGradientStop(0, w), new ImmutableGradientStop(1, Avalonia.Media.Color.FromArgb(0, w.R, w.G, w.B))],
            startPoint: new RelativePoint(0, 0, RelativeUnit.Relative), endPoint: new RelativePoint(0, 1, RelativeUnit.Relative));
    }

    /// <summary>
    /// The compact header shows once ≥ 70 % of the banner has scrolled out: rows ≥ 1 snap below it, so the page never
    /// shows a sliver of the banner with a cut motif (M5 QA).
    /// </summary>
    private void UpdateCompactHeader()
    {
        var header = Covers.HeaderHeight;
        var show = header > 0 && Covers.ScrollOffsetY >= 0.7 * header;
        CompactHeader.Opacity = show ? 1 : 0;
        CompactHeader.IsHitTestVisible = show;
    }

    /// <summary>1080 or ≈1100 values: banner 392/320 high, motif 300/220, name 72·56·44 / 56·44·36.</summary>
    private void ApplySize(Size size)
    {
        _compact = size.Height < CompactBelowHeight;
        var compact = _compact;
        BannerTitle.Sizes = compact ? "56,44,36" : "72,56,44";
        BannerText.Margin = new Thickness(0, 0, compact ? 280 : 380, compact ? 36 : 48);
        Covers.Spacing = size.Width >= 1600 ? 28 : 20;
        // the empty compartment at ≈ 1100: figures 120 high and a closer title, so the way to the imports stays clear of
        // the footer's fade under the banner (M5 QA)
        EmptyDrawing.Width = compact ? 84 : 140;
        EmptyDrawing.Height = compact ? 120 : 200;
        foreach (var slot in EmptyPlaces.Children.OfType<EmptySlot>())
        {
            slot.Width = compact ? 90 : 150;
            slot.Height = compact ? 120 : 200;
        }
        EmptyTitle.Margin = new Thickness(0, compact ? 32 : 48, 0, 0);
        ApplyBanner();
    }

    /// <summary>
    /// The banner's geometry: the light zone holds only the neighbour row and has a fixed height (88, ≈1100: 64; 24 when
    /// there are no neighbours – no dead stripe), the banner shrinks with it; the motif stands on the board.
    /// </summary>
    private void ApplyBanner()
    {
        var band = BandHeight;
        var full = _compact ? 320.0 : 392.0;
        var bannerHeight = full - ((_compact ? 64 : 88) - band);
        Banner.Height = bannerHeight;
        BannerWall.SplitHeight = band;
        // the neighbour row centred in the light zone under the 8 px colour band
        Neighbours.Margin = new Thickness(-10, Math.Round(8 + (band - 8 - 40) / 2), 0, 0);
        var wall = bannerHeight - 24;
        BannerMotif.Width = BannerMotif.Height = Math.Min(_compact ? 220 : 300, wall - 24);
        FitTitle();
    }

    /// <summary>
    /// The title may take what is left between the light zone (+ <see cref="TitleClearance"/>) and the sentence and status
    /// row anchored to the board: two lines at 72 never reach the tone edge, they fall back to 56/44 (M5 QA).
    /// </summary>
    private void FitTitle()
    {
        var bottom = Banner.Height - BannerText.Margin.Bottom;
        var below = BannerSentence.Bounds.Height + BannerSentence.Margin.Top + StatusRow.Bounds.Height + StatusRow.Margin.Top;
        if (BannerSentence.Bounds.Height <= 0) below = 30 + 8 + 40 + 16;
        var room = Math.Floor(bottom - below - (BandHeight + TitleClearance));
        if (Math.Abs(BannerTitle.MaxTextHeight - room) > 0.5) BannerTitle.MaxTextHeight = Math.Max(30, room);
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
            PlaceInfo();
            Dispatcher.UIThread.Post(() =>
            {
                PlaceInfo();
                InfoCloseButton.Focus(NavigationMethod.Directional);
            }, DispatcherPriority.Loaded);
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

    /// <summary>
    /// The info hangs under the banner's action row (QA 2): left on the banner text (page margin), 12 px under the row;
    /// above it when the page has no room below.
    /// </summary>
    private void PlaceInfo()
    {
        if (StatusRow.TranslatePoint(new Point(0, 0), Root) is not { } at) return;
        var layer = Root.Bounds.Size;
        // Bounds, not DesiredSize: the desired size includes the margin this method sets
        var height = InfoPanel.Bounds.Height > 0 ? InfoPanel.Bounds.Height : 300;
        var x = Math.Clamp(at.X, 16, Math.Max(16, layer.Width - InfoPanel.Width - 16));
        var below = at.Y + StatusRow.Bounds.Height + 12;
        var y = below + height <= layer.Height - 24 ? below : Math.Max(16, at.Y - 12 - height);
        InfoPanel.Margin = new Thickness(x, y, 0, 0);
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
