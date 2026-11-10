using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using RetroHub.App.Controls;
using RetroHub.App.Input;
using RetroHub.App.Theming;
using RetroHub.App.ViewModels;

namespace RetroHub.App.Views.Pages;

public partial class HomePage : PageBase
{
    /// <summary>Below this page height (logical; 1080 − header − footer = 952 at the reference) the ≈1100 values apply.</summary>
    public const double CompactBelowHeight = 860;

    private bool? _compact;
    private HomeViewModel? _vm;
    private readonly FocusLayer _stageFocus;
    private int _dwell;
    private int _swap;
    private (Shelf Shelf, Control Item, int Index, int Count)? _pending;

    public HomePage()
    {
        InitializeComponent();
        // box and TV cast their contact shadows on the stage board
        StageBoard.Casters = [StageBox.Art ?? (Control)StageBox, Screen];
        StageBox.TemplateApplied += (_, _) => StageBoard.Casters = [StageBox.Art ?? (Control)StageBox, Screen];
        SizeChanged += (_, e) => ApplyLayout(e.NewSize);
        Stage.LayoutUpdated += (_, _) => StageBoard.InvalidateVisual();
        // the stage box gets the object focus like a box on a shelf: lift, cone (inside the stage), lit board piece, ring
        _stageFocus = new FocusLayer(BoxSlot, BoxSlot) { BoardFront = ShelfBoard.StageHeight, ConeHeadroom = 56, ConeOutsideBounds = true };
        BoxSlot.Children.Insert(0, _stageFocus.Back);
        BoxSlot.Children.Add(_stageFocus.Front);

        PlayButton.Click += (_, _) => _vm?.StagePrimaryCommand.Execute(null);
        DetailsButton.Click += (_, _) => _vm?.StageDetailsCommand.Execute(null);
        FavoriteButton.Click += (_, _) => _vm?.StageFavoriteCommand.Execute(null);
        ImportsButton.Click += (_, _) => _vm?.ShowImportsCommand.Execute(null);

        foreach (var shelf in ShelfList.Children.OfType<Shelf>()) shelf.ItemFocused += OnShelfItemFocused;
        EmptyShelf.ItemsSource = Enumerable.Range(0, 5).ToArray();
        Stage.AddHandler(GotFocusEvent, OnStageGotFocus, Avalonia.Interactivity.RoutingStrategies.Bubble, handledEventsToo: true);
        NavRepeatTracker.AnyChanged += OnRepeatChanged;
        DetachedFromVisualTree += (_, _) => NavRepeatTracker.AnyChanged -= OnRepeatChanged;
        AttachedToVisualTree += (_, _) =>
        {
            NavRepeatTracker.AnyChanged -= OnRepeatChanged;
            NavRepeatTracker.AnyChanged += OnRepeatChanged;
        };
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (_vm != null) _vm.PropertyChanged -= OnVmChanged;
        _vm = DataContext as HomeViewModel;
        if (_vm == null) return;
        _vm.PropertyChanged += OnVmChanged;
        ShowStage(_vm.Stage, animate: false);
    }

    private void OnVmChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(HomeViewModel.Stage) && _vm != null) ShowStage(_vm.Stage, animate: true);
    }

    // ===================== layout (DESIGN 7.1 and its ≈1100 values) =====================

    /// <summary>
    /// 1920×1080 logical → stage board at page y 488 (window 560), TV 560 wide, box frame 300×420, text column 764, shelf
    /// boxes 200 high anchored at page y 560 (window 632), the next shelf from y 840; the shelf area is clipped from the
    /// stage board's bottom edge (window 584), leaving 48 px above the active row for lift (12), ring (6) and cone.
    /// ≈1100 px window (logical 1375×875) → board at page y 384 (logical 456, M2 QA: 40 px headroom above the active
    /// row), TV 400, box 214×300, shelf boxes 160 anchored at page y 448 (logical 520), the next shelf from y 676.
    /// </summary>
    private void ApplyLayout(Size size)
    {
        if (size.Height <= 0) return;
        var compact = size.Height < CompactBelowHeight;
        if (_compact == compact) return;
        _compact = compact;
        Root.Classes.Set("compact", compact);
        var board = compact ? 384.0 : 488.0;
        Stage.Height = board;
        StageBoard.Margin = new Thickness(0, board, 0, 0);
        BoxSlot.Width = compact ? 214 : 300;
        BoxSlot.Height = compact ? 300 : 420;
        StageBox.Width = BoxSlot.Width;
        StageBox.Height = BoxSlot.Height;
        EmptyDrawing.Width = EmptyDrawing.Height = compact ? 180 : 240;
        var tv = compact ? 400.0 : 560.0;
        Screen.Width = tv;
        Screen.Height = PixelScreen.SetHeightFor(tv);
        var gap = compact ? 40 : 52;
        Stage.ColumnDefinitions[1].Width = new GridLength(gap);
        Stage.ColumnDefinitions[3].Width = new GridLength(gap);
        StageTitle.Sizes = compact ? "56,40" : "72,56,40";
        TextColumn.Margin = new Thickness(0, 0, 0, compact ? 24 : 32);
        _stageFocus.ConeHeadroom = compact ? 40 : 56;
        var shelvesTop = board + 24; // clipped from the stage board's bottom edge on
        var anchor = (compact ? 448.0 : 560.0) - shelvesTop;
        var item = compact ? 160.0 : 200.0;
        var spacing = compact ? 40.0 : 52.0;
        Shelves.Margin = new Thickness(0, shelvesTop, 0, 0);
        Shelves.AnchorY = anchor;
        ShelfList.Spacing = spacing;
        // QA 2: the next shelf no longer peeks in – between the footer's own fade and this one only a strip of 2 px colour
        // dashes was left (it read as a rendering glitch). The fade now ends 8 px above the next shelf's boxes.
        var topGap = compact ? 32.0 : 40.0; // Shelf.TopGap of the Start shelves (HomePage.axaml)
        var nextBoxes = shelvesTop + anchor + item + Shelf.BoardHeight + Shelf.BelowBoard + spacing + topGap;
        ShelfFade.Margin = new Thickness(0, Math.Max(0, nextBoxes - 8 - 24), 0, 0);
    }

    public override bool FocusDefault() => FocusVisible(PlayButton);

    // ===================== stage follows the focus (dwell) =====================

    /// <summary>
    /// The focus came to the stage (actions or the stage box): the footer follows at once; after the dwell the stage
    /// returns to the game to continue (DESIGN 7.1: Start's main action is always "Spielen" for it).
    /// </summary>
    private void OnStageGotFocus(object? sender, FocusChangedEventArgs e)
    {
        if (_vm == null || e.Source is not Visual v) return;
        HomeFocus? zone = null;
        for (Visual? x = v; x != null && x != Stage; x = x.GetVisualParent())
        {
            zone = x switch
            {
                _ when x == PlayButton => HomeFocus.Primary,
                _ when x == DetailsButton => HomeFocus.Details,
                _ when x == FavoriteButton => HomeFocus.Star,
                _ when x == ImportsButton => HomeFocus.Imports,
                _ when x == StageBox => HomeFocus.StageBox,
                _ => null,
            };
            if (zone != null) break;
        }
        if (zone is not { } z) return;
        _pending = null;
        _vm.SetFocus(z);
        var version = ++_dwell;
        _ = Task.Delay(Motion.Dwell).ContinueWith(_ => Dispatcher.UIThread.Post(() =>
        {
            if (version != _dwell || _vm == null || !Stage.IsKeyboardFocusWithin) return;
            _vm.ReturnToContinue();
        }), TaskScheduler.Default);
    }

    private void OnShelfItemFocused(Shelf shelf, Control item, int index, int count)
    {
        var dataCount = count - (shelf.ActionLabel != null ? 1 : 0);
        _vm?.SetFocus(item.DataContext switch
        {
            GameItemViewModel => HomeFocus.ShelfGame,
            SystemItemViewModel => HomeFocus.ShelfSystem,
            _ => HomeFocus.ShelfAction,
        }, shelf.Header, index, dataCount, shelf.ActionLabel);
        // the "Alle anzeigen" spine is not a subject; it keeps what the stage shows
        if (item.DataContext is not (GameItemViewModel or SystemItemViewModel)) { _pending = null; ++_dwell; return; }
        _pending = (shelf, item, index, dataCount);
        var version = ++_dwell;
        if (NavRepeatTracker.Current?.IsActive == true) return; // resumes when the repeat ends
        StartDwell(version);
    }

    private void StartDwell(int version) =>
        _ = Task.Delay(Motion.Dwell).ContinueWith(_ => Dispatcher.UIThread.Post(() =>
        {
            if (version != _dwell || _pending is not { } p || _vm == null) return;
            if (NavRepeatTracker.Current?.IsActive == true) return;
            if (!p.Item.IsKeyboardFocusWithin) return; // the focus went on (e.g. up to the stage)
            switch (p.Item.DataContext)
            {
                case GameItemViewModel g: _vm.Present(g, p.Shelf.Header ?? "", p.Index, p.Count); break;
                case SystemItemViewModel s: _vm.Present(s, p.Shelf.Header ?? "", p.Index, p.Count); break;
            }
        }), TaskScheduler.Default);

    private void OnRepeatChanged(bool active)
    {
        // after a held direction only one dwell, and only when the target differs from what is shown
        if (active || _pending is not { } p || _vm?.Stage is not { } stage) return;
        if (ReferenceEquals(p.Item.DataContext, stage.Game) && stage.Kind == StageKind.Game) return;
        if (p.Item.DataContext is SystemItemViewModel s && ReferenceEquals(stage.System, s)) return;
        StartDwell(++_dwell);
    }

    // ===================== stage swap (DESIGN 9.2: text 120 ms out, 180 ms in with 8 px; box and TV 180 ms) =====================

    private void ShowStage(StageState? stage, bool animate)
    {
        var old = TextColumn.DataContext as StageState;
        var version = ++_swap;
        if (stage == null) return;
        if (!animate || stage.SameSubject(old) || old == null || !Motion.Enabled)
        {
            Fade(TextColumn, 1, TimeSpan.Zero);
            Fade(BoxSlot, 1, TimeSpan.Zero);
            Apply(stage);
            return;
        }
        var outTime = TimeSpan.FromMilliseconds(Motion.AllowsMovement ? 120 : 80);
        Fade(TextColumn, 0, outTime);
        Fade(BoxSlot, 0, outTime);
        _ = Task.Delay(outTime).ContinueWith(_ => Dispatcher.UIThread.Post(() =>
        {
            if (version != _swap) return;
            Apply(stage);
            if (TextColumn.RenderTransform is TranslateTransform tt)
            {
                tt.Transitions = null;
                tt.X = Motion.AllowsMovement ? 8 : 0;
                tt.Transitions = [new DoubleTransition { Property = TranslateTransform.XProperty, Duration = TimeSpan.FromMilliseconds(180), Easing = new CubicEaseOut() }];
                tt.X = 0;
            }
            Fade(TextColumn, 1, TimeSpan.FromMilliseconds(180));
            Fade(BoxSlot, 1, TimeSpan.FromMilliseconds(180));
        }), TaskScheduler.Default);
    }

    /// <summary>Opacity via a transition of the given duration (zero = at once).</summary>
    private static void Fade(Control c, double to, TimeSpan duration)
    {
        c.Transitions = duration == TimeSpan.Zero ? null : [new DoubleTransition { Property = OpacityProperty, Duration = duration }];
        c.Opacity = to;
    }
    private void Apply(StageState stage)
    {
        var focusWasInText = TextColumn.IsKeyboardFocusWithin || BoxSlot.IsKeyboardFocusWithin;
        TextColumn.DataContext = stage;
        StageBox.DataContext = stage.Game;
        StageBox.IsVisible = stage.Game != null && !stage.IsEmpty;
        EmptyDrawing.IsVisible = stage.IsEmpty;

        // a focused button that disappeared with the subject (Details of a game → a system): back to the primary action
        if (focusWasInText && TopLevel.GetTopLevel(this)?.FocusManager?.GetFocusedElement() is Control { IsEffectivelyVisible: false })
            PlayButton.Focus(NavigationMethod.Directional);
    }

    // ===================== directional navigation =====================

    public override bool MoveFocus(NavAction action, Control focused)
    {
        if (IsInside(focused, Shelves))
        {
            if (Shelves.Move(action, focused)) return true;
            // up from the first shelf: the stage's primary action
            if (action == NavAction.Up) return FocusVisible(PlayButton);
            return false;
        }
        if (IsInside(focused, Stage) && action == NavAction.Down && Shelves.IsVisible && Shelves.Shelves.Count > 0)
            return Shelves.FocusShelf(Shelves.ActiveIndex);
        return false;
    }

    private static bool IsInside(Visual v, Visual root)
    {
        for (Visual? x = v; x != null; x = x.GetVisualParent()) if (x == root) return true;
        return false;
    }
}
