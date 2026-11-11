using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Media.TextFormatting;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Avalonia.VisualTree;
using RetroHub.App.Controls;
using RetroHub.App.Input;
using RetroHub.App.Localization;
using RetroHub.App.Theming;
using RetroHub.App.ViewModels;

namespace RetroHub.App.Views.Pages;

public partial class GameDetailPage : PageBase
{
    /// <summary>Below this page height (logical; 952 at the reference) the ≈1100 values apply (DESIGN 7.3).</summary>
    public const double CompactBelowHeight = 860;

    private GameDetailViewModel? _vm;
    private readonly FocusLayer _boxFocus;
    private readonly FocusLayer _shelfFocus;
    private bool _compact;
    private bool? _appliedCompact;
    private Control? _sheetOpener;
    private int _scrollVersion;

    public GameDetailPage()
    {
        InitializeComponent();
        // the pulled box is an object like a box on a shelf: lift, cone, lit board piece, ring. The layers lie over the
        // stage AND its board (M4 QA: inside the box slot the board covered the lit piece - the box looked sunk in); the
        // piece covers the board front opaquely, the cone is half as strong (the room is lit by the TV light already)
        _boxFocus = new FocusLayer(StageHost, BoxSlot) { BoardFront = ShelfBoard.StageHeight, ConeHeadroom = 56, ConeOpacity = 0.5, OpaquePiece = true };
        StageHost.Children.Insert(0, _boxFocus.Back);
        StageHost.Children.Add(_boxFocus.Front);
        DetailBox.TemplateApplied += (_, _) =>
        {
            StageBoard.Casters = Casters();
            if (DetailBox.Art != null) DetailBox.Art.ArtRectChanged += (_, _) => { ApplyBoxFrame(); ApplyStageHeight(); StageBoard.InvalidateVisual(); UpdateGap(); };
            ApplySpine();
        };
        Stage.LayoutUpdated += (_, _) => StageBoard.InvalidateVisual();
        TextColumn.SizeChanged += (_, _) => Dispatcher.UIThread.Post(ApplyStageHeight, DispatcherPriority.Loaded);
        Scroll.ScrollChanged += (_, _) => { OnScrolled(); UpdateFold(); };
        Back.LayoutUpdated += (_, _) => UpdateFold();
        ScreensBoard.Casters = [TitleSet, SnapSet];
        ScreenSets.LayoutUpdated += (_, _) => ScreensBoard.InvalidateVisual();
        CoverBoard.Casters = [CoverLarge];
        // the large cover's contact shadow follows its picture (M4 QA: drawn for the placeholder's square before the load)
        CoverLarge.ArtRectChanged += (_, _) => CoverBoard.InvalidateVisual();
        ScreensLargeBoard.Casters = [TitleLarge, SnapLarge];
        LargeSets.LayoutUpdated += (_, _) => ScreensLargeBoard.InvalidateVisual();
        DescriptionText.SizeChanged += (_, _) => UpdateReadMore();
        DescriptionScroll.ScrollChanged += (_, _) => UpdateDescriptionFade();
        // "Daneben im Regal": its boxes take the object focus like a shelf in the library
        _shelfFocus = new FocusLayer(NeighbourHost, NeighbourRow);
        NeighbourHost.Children.Insert(0, _shelfFocus.Back);
        NeighbourHost.Children.Add(_shelfFocus.Front);
        NeighbourHost.SizeChanged += (_, e) => { if (e.WidthChanged) FillNeighbours(); };
        NeighbourRow.LayoutUpdated += (_, _) => NeighbourBoard.InvalidateVisual();
        SheetLayer.FocusAdorner = null;

        // the box: confirm/click shows the large cover (never the detail of itself again)
        DetailBox.Click += (_, e) =>
        {
            e.Handled = true;
            _vm?.ShowCoverCommand.Execute(null);
        };
        // a click on the dimmed page closes the overlay
        SheetScrim.PointerPressed += (_, e) =>
        {
            e.Handled = true;
            _vm?.CloseSheetCommand.Execute(null);
        };
        CoverView.PointerPressed += (_, _) => _vm?.CloseSheetCommand.Execute(null);
        // a fold open in the page (a choice list, Dateiinfo, Weiterlesen) closes on a click anywhere else; the click
        // still reaches what it hit (R2)
        Root.AddHandler(PointerPressedEvent, OnRootPointerPressed, RoutingStrategies.Tunnel);
        ScreensView.PointerPressed += (_, _) => _vm?.CloseSheetCommand.Execute(null);

        SizeChanged += (_, e) => ApplyLayout(e.NewSize);
        SheetLayer.SizeChanged += (_, _) => { SizeSheetViews(); PlaceSheet(_vm?.Sheet ?? DetailSheet.None); };
        PageContent.AddHandler(GotFocusEvent, OnContentGotFocus, RoutingStrategies.Bubble, handledEventsToo: true);
        SheetLayer.AddHandler(GotFocusEvent, (_, _) => _vm?.SetFocus(DetailFocus.Sheet), RoutingStrategies.Bubble, handledEventsToo: true);

        DataContextChanged += (_, _) =>
        {
            if (_vm != null)
            {
                _vm.PropertyChanged -= OnVmChanged;
                _vm.SectionRequested -= OnSectionRequested;
            }
            _vm = DataContext as GameDetailViewModel;
            if (_vm == null) return;
            UpdateHeaderGround();
            _vm.PickImage = PickImageAsync;
            _vm.PropertyChanged += OnVmChanged;
            _vm.SectionRequested += OnSectionRequested;
            ApplyBackLayout();
            ApplySheet();
        };
    }

    private IReadOnlyList<Control> Casters() =>
        DetailBox.Art is { } art ? DetailBox.Spine is { } spine ? [art, spine] : [art] : [DetailBox];

    private void OnVmChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(GameDetailViewModel.HasAbout):
                ApplyBackLayout();
                break;
            case nameof(GameDetailViewModel.NeighboursBefore):
            case nameof(GameDetailViewModel.NeighboursAfter):
                FillNeighbours();
                break;
            case nameof(GameDetailViewModel.Description):
            case nameof(GameDetailViewModel.HasDescription):
                Dispatcher.UIThread.Post(UpdateReadMore, DispatcherPriority.Loaded);
                break;
            case nameof(GameDetailViewModel.Sheet):
                ApplySheet();
                break;
            case nameof(GameDetailViewModel.ShowSolution):
                // the primary plate changed its kind under the focus: the focus moves with it
                Dispatcher.UIThread.Post(() =>
                {
                    var focused = TopLevel.GetTopLevel(this)?.FocusManager?.GetFocusedElement() as Control;
                    if (focused is { IsEffectivelyVisible: false } && (focused == PlayButton || focused == SolveButton)) FocusDefault();
                }, DispatcherPriority.Loaded);
                break;
            case nameof(GameDetailViewModel.Game):
                // another game: the page starts at the stage again
                Scroll.Offset = default;
                break;
        }
    }

    // ===================== layout (DESIGN 7.3 and its ≈1100 values) =====================

    /// <summary>
    /// 1920×1080 logical: stage board at page y 588 (window 660), spine 28 + box frame 360×504 at x 96/124, the text
    /// column from x 540, the back in two columns (904 | 80 | 744). ≈1100 px window (logical 1375×875): board at page
    /// y 398 (logical 470), spine 20 + box 240×336, text from x 380, titles at most 56, the back in one column
    /// (Start, Ausleihkarte, Über das Spiel).
    /// </summary>
    private void ApplyLayout(Size size)
    {
        if (size.Height <= 0) return;
        _compact = size.Height < CompactBelowHeight;
        if (_appliedCompact == _compact) return;
        _appliedCompact = _compact;
        Root.Classes.Set("compact", _compact);
        Stage.ColumnDefinitions[1].Width = new GridLength(_compact ? 64 : 56);
        StageTitle.Sizes = _compact ? "56,40" : "72,56,40";
        TextColumn.Margin = new Thickness(0, 0, 0, _compact ? 32 : 44);
        _boxFocus.ConeHeadroom = _compact ? 40 : 56;
        // R2: 360 at 1920 too – set, chin caption and board end above the footer's fade under the stage
        var set = 360.0;
        var setHeight = PixelScreen.SetHeightFor(set);
        foreach (var screen in new[] { TitleSet, SnapSet })
        {
            screen.Width = set;
            screen.Height = setHeight;
        }
        TitleCaption.Width = SnapCaption.Width = set;
        ScreenSets.Height = setHeight;
        ApplySpine();
        ApplyBoxFrame(force: true);
        ApplyStageHeight();
        ApplyBackLayout();
    }

    private double SpineWidth => _compact ? 20 : 28;

    private void ApplySpine()
    {
        DetailBox.Padding = new Thickness(SpineWidth, 0, 0, 0);
        if (DetailBox.Spine is { } spine)
        {
            spine.SpineWidth = SpineWidth;
            spine.Slant = _compact ? 6 : 8;
        }
    }

    /// <summary>
    /// Box frame 360 × 504 (≈1100: 240 × 336) right of the spine; a landscape cover (SNES, N64 …) gets a frame up to 520
    /// (360) wide, so it does not stand as a low strip under an empty stage (M4: 460 left a landscape box 333 high).
    /// </summary>
    public const double FrameWidth = 360, WideFrameWidth = 520;

    private void ApplyBoxFrame(bool force = false)
    {
        var aspect = DetailBox.Art?.ImageAspect;
        var wide = aspect is > 1.05;
        var frame = _compact ? (wide ? 360 : 240) : (wide ? WideFrameWidth : FrameWidth);
        var width = SpineWidth + frame;
        var height = _compact ? 336.0 : 504.0;
        // a landscape box is only as high as its picture: the stage below can then follow it (ApplyStageHeight)
        if (wide) height = Math.Min(height, Math.Round(frame / aspect!.Value));
        if (!force && BoxSlot.Width == width && BoxSlot.Height == height) return;
        BoxSlot.Width = width;
        BoxSlot.Height = height;
        DetailBox.Width = width;
        DetailBox.Height = height;
        ApplyStageHeight();
    }

    /// <summary>
    /// The stage is as high as its content (M4 QA): a portrait box keeps the board at window y 660 (≈1100: 470); a
    /// landscape box (377 high at 1920) left ≈ 210 px of empty room above it while the back started below the fold.
    /// There the board sits <see cref="StageHeadroom"/> above the taller of box and text column, at least at window y
    /// 560 (≈1100: 420), so the back moves up with it.
    /// </summary>
    private void ApplyStageHeight()
    {
        var full = _compact ? 398.0 : 588.0;
        var h = full;
        // QA 2: not only a landscape cover – any box clearly lower than its frame (a square placeholder, a squat cover)
        // lets the stage follow it; the placeholder state had ≈ 220 px of empty backdrop above the box
        var frameHeight = _compact ? 336.0 : 504.0;
        var art = DetailBox.Art is { ArtRect.Height: > 0 } a ? a.ArtRect.Height : BoxSlot.Height;
        var wide = DetailBox.Art?.ImageAspect is > 1.05;
        if (wide || art < frameHeight * 0.9)
        {
            var text = TextColumn.DesiredSize.Height; // includes its bottom margin above the board
            var content = Math.Max(wide ? BoxSlot.Height : art, text);
            h = Math.Clamp(Math.Ceiling(StageHeadroom + content), _compact ? 348 : 488, full);
        }
        if (Stage.Height == h) return;
        Stage.Height = h;
        StageBoard.Margin = new Thickness(0, h, 0, 0);
    }

    /// <summary>Free room above the box (or the text column) on a stage that follows its content.</summary>
    private double StageHeadroom => _compact ? 64 : 88;

    /// <summary>
    /// The back (DESIGN 7.3, M4) in two stacks: "Über das Spiel" and "Daneben im Regal" left, "Start" with the
    /// Ausleihkarte right. Without anything about the game the left column drops out: "Start" moves to the page margin,
    /// the card and the shelf take the right column (M4: the right half is never empty). ≈1100: one column – Start,
    /// Ausleihkarte, Über das Spiel, Daneben im Regal.
    /// </summary>
    private void ApplyBackLayout()
    {
        var about = _vm?.HasAbout == true;
        IEnumerable<Control> left, right;
        if (_compact)
        {
            left = [StartSection, CardSection, AboutSection, ShelfSection];
            right = [];
        }
        else if (about)
        {
            left = [AboutSection, ShelfSection];
            right = [StartSection, CardSection];
        }
        else
        {
            left = [StartSection, AboutSection];
            right = [CardSection, ShelfSection];
        }
        Grid.SetColumnSpan(LeftStack, _compact ? 3 : 1);
        RightStack.IsVisible = !_compact;
        Fill(LeftStack, left);
        Fill(RightStack, right);
        // the start rows never run wider than 1100, also alone in a compact column
        StartSection.MaxWidth = _compact ? 1100 : double.PositiveInfinity;
        StartSection.HorizontalAlignment = _compact ? Avalonia.Layout.HorizontalAlignment.Left : Avalonia.Layout.HorizontalAlignment.Stretch;
        StartSection.Width = _compact ? 1100 : double.NaN;
        // the neighbour shelf ends where the start rows end (M4 QA: at 1100 its edge ran 130 px further)
        ShelfSection.Width = _compact ? 1100 : double.NaN;
        ShelfSection.HorizontalAlignment = _compact ? Avalonia.Layout.HorizontalAlignment.Left : Avalonia.Layout.HorizontalAlignment.Stretch;
        Dispatcher.UIThread.Post(FillNeighbours, DispatcherPriority.Loaded);
    }

    /// <summary>Puts the sections into <paramref name="stack"/> in order: the first 8 px under the board's shadow, the others 24 apart (R3 QA 3).</summary>
    private static void Fill(StackPanel stack, IEnumerable<Control> sections)
    {
        var list = sections.ToList();
        if (!stack.Children.SequenceEqual(list))
        {
            foreach (var c in list)
                if (c.Parent is Panel p && p != stack) p.Children.Remove(c);
            stack.Children.Clear();
            foreach (var c in list) stack.Children.Add(c);
        }
        var first = true;
        foreach (var c in stack.Children)
        {
            // R3 QA 3: 8 under the board's shadow, 24 between sections (48 put the shelf captions and "Gespielt" into the
            // footer's fold)
            c.Margin = new Thickness(0, first ? 8 : 24, 0, 0);
            if (c.IsVisible) first = false;
        }
    }

    // ===================== Daneben im Regal =====================

    /// <summary>
    /// The shelf the box was pulled from: as many neighbours as fit the column, the gap (a dashed place in the pulled
    /// box's own format, standing on the shelf edge) where this box stood, roughly in the middle. All boxes of one system
    /// share its box format (no FitToImage), so the count is known before the covers load. The shelf is one system:
    /// the captions drop the code like the library's compartment (bar, variant, year stay).
    /// </summary>
    private void FillNeighbours()
    {
        if (_vm == null) return;
        var width = NeighbourHost.Bounds.Width;
        if (width <= 0) width = _compact ? 1100 : 744;
        // R3 QA 4: 144 at 1920 (was 180) – under a one-line "Über das Spiel" the captions' year line ended in the footer
        var height = _compact ? 150.0 : NeighbourHeight;
        var aspect = SystemPalette.BoxAspect(_vm.Game?.SystemId);
        var slot = Math.Round(height / Math.Clamp(aspect, 0.6, 1.8));
        const double gap = 24;
        var fits = Math.Max(1, (int)Math.Floor((width + gap) / (slot + gap)));
        var before = _vm.NeighboursBefore;
        var after = _vm.NeighboursAfter;
        var total = Math.Min(fits, before.Count + after.Count + 1);
        var left = Math.Min(before.Count, (total - 1) / 2);
        var right = Math.Min(after.Count, total - 1 - left);
        left = Math.Min(before.Count, total - 1 - right);
        var items = new List<object>();
        items.AddRange(before.Skip(before.Count - left));
        items.Add(GapMarker);
        items.AddRange(after.Take(right));
        var focusedItem = (TopLevel.GetTopLevel(this)?.FocusManager?.GetFocusedElement() as Control)?.DataContext;
        NeighbourRow.Children.Clear();
        _gap = null;
        // each box keeps its caption under the board like in the library (namesakes differ in their label line)
        NeighbourRow.Height = height + CaptionBlock;
        NeighbourBoard.Margin = new Thickness(0, height, 0, 16);
        var tiles = new List<GameTile>();
        foreach (var item in items)
        {
            if (item is GameItemViewModel game)
            {
                var tile = new GameTile
                {
                    DataContext = game,
                    ShowCaption = true,
                    ShowFavorite = false,
                    Height = height + CaptionBlock,
                    Width = slot,
                    VerticalAlignment = Avalonia.Layout.VerticalAlignment.Bottom,
                };
                tile.Classes.Add("no-code");
                tile.Classes.Add("deep-caption");
                if (!game.HasLabelDetail) tile.Classes.Add("bare");
                tile.TemplateApplied += (_, _) =>
                {
                    if (tile.Art != null) tile.Art.ArtRectChanged += (_, _) => NeighbourBoard.InvalidateVisual();
                    NeighbourBoard.Casters = tiles.Select(t => (Control)(t.Art ?? (Control)t)).ToList();
                };
                NeighbourRow.Children.Add(tile);
                tiles.Add(tile);
                if (ReferenceEquals(game, focusedItem)) Dispatcher.UIThread.Post(() => tile.Focus(NavigationMethod.Directional), DispatcherPriority.Loaded);
            }
            else
            {
                // the place this box was pulled from (R3 QA: without a word it read as a broken, empty neighbour): the
                // footprint on the board and under it, where the neighbours have their titles, "Hier stand es"
                _gap = new EmptySlot
                {
                    Width = slot, Height = height, VerticalAlignment = Avalonia.Layout.VerticalAlignment.Top,
                    Caption = Loc.T("detail.shelfGap"), CaptionTop = 32,
                };
                NeighbourRow.Children.Add(_gap);
                UpdateGap();
            }
        }
        NeighbourBoard.Casters = tiles.Select(t => (Control)(t.Art ?? (Control)t)).ToList();
    }

    private static readonly object GapMarker = new();
    private EmptySlot? _gap;

    /// <summary>The gap has the pulled box's format: its picture's aspect, else the placeholder's (M4 QA).</summary>
    private void UpdateGap()
    {
        if (_gap == null) return;
        _gap.Aspect = DetailBox.Art?.ImageAspect ?? 1 / Math.Clamp(SystemPalette.BoxAspect(_vm?.Game?.SystemId), 0.6, 1.8);
    }

    /// <summary>The neighbours' box height at the 1080 values (≈1100: 150).</summary>
    internal const double NeighbourHeight = 144;

    /// <summary>The caption under a box (GameTile .deep-caption: 32 = board 12 + 20 under its dark underside, then the 66 px title and label block).</summary>
    private const double CaptionBlock = 98;

    /// <summary>"Weiterlesen" only when the description is longer than its 8 lines.</summary>
    private void UpdateReadMore()
    {
        if (_vm is not { HasDescription: true } vm || DescriptionText.Bounds.Width <= 0)
        {
            ReadMoreButton.IsVisible = false;
            return;
        }
        var face = new Typeface(DescriptionText.FontFamily, DescriptionText.FontStyle, DescriptionText.FontWeight);
        using var layout = new TextLayout(vm.Description, face, DescriptionText.FontSize, null, TextAlignment.Left, TextWrapping.Wrap,
            maxWidth: DescriptionText.Bounds.Width, lineHeight: DescriptionText.LineHeight);
        ReadMoreButton.IsVisible = layout.TextLines.Count > ClosedLines;
        UpdateDescriptionFade();
    }

    // ===================== focus: footer, scrolling, sections =====================

    private void OnContentGotFocus(object? sender, FocusChangedEventArgs e)
    {
        if (_vm == null || e.Source is not Visual v) return;
        DetailFocus? zone = null;
        for (Visual? x = v; x != null && zone == null; x = x.GetVisualParent())
        {
            zone = x switch
            {
                _ when x == PlayButton => DetailFocus.Play,
                _ when x == SolveButton => DetailFocus.Solve,
                _ when x == FavoriteButton => DetailFocus.Favorite,
                _ when x == MoreButton => DetailFocus.More,
                _ when x == DetailBox => DetailFocus.Box,
                _ when x == SystemButton => DetailFocus.System,
                _ when x == EmulatorFold || x == ModeFold || x == FileFold => DetailFocus.Sheet,
                _ when x == EmulatorBox || x == ModeBox => DetailFocus.Row,
                _ when x == FileRow => DetailFocus.File,
                _ when x == VariantsRow => DetailFocus.Variants,
                _ when x == TitleScreenButton || x == SnapScreenButton => DetailFocus.Screen,
                _ when x == ReadMoreButton => DetailFocus.ReadMore,
                GameTile when x.GetVisualParent() == NeighbourRow => DetailFocus.Neighbour,
                _ => null,
            };
        }
        if (zone is { } z) _vm.SetFocus(z);
        var fromStage = _focusInStage;
        _focusInStage = IsInside(v, Stage);
        if (e.NavigationMethod != NavigationMethod.Directional) return;
        // the stage shows from the top; the back is shown whole when it fits under the box (turning the box around)
        if (IsInside(v, Stage))
        {
            ++_scrollVersion; // a scroll still queued for the back must not pull the page down again
            Scroll.Offset = default;
        }
        else if (IsInside(v, Back))
        {
            // R2: turning the box around – from the stage into a back that does not fit under it, the page goes to the
            // board stop (the back from its top, nothing of it in the footer's fade) instead of the nearest pixel
            if (fromStage && !BackFitsUnderStage()) ScrollTo(BoardStop);
            else ShowBack(FocusTarget(v));
        }
    }

    private bool _focusInStage = true;

    /// <summary>The whole back (with its bottom margin) visible under the stage at offset 0.</summary>
    private bool BackFitsUnderStage()
    {
        var bottom = Back.TranslatePoint(new Point(0, Back.Bounds.Height), PageContent)?.Y ?? double.MaxValue;
        return bottom + 40 <= Scroll.Viewport.Height;
    }

    /// <summary>
    /// Scrolls so that <paramref name="focused"/> (in the back) is visible with 40 px to spare. Over the stage the page
    /// rests only at three stops (M4 QA: in between, slivers of the box and the buttons hung under the header): the top
    /// (0), the whole action row in view (<see cref="ActionsStop"/>: the title has gone up, "Spielen" stays in reach) and
    /// the board flush under the header (<see cref="BoardStop"/>); below it the back scrolls freely. Room below the back
    /// makes a stop reachable on a short page.
    /// </summary>
    private void ShowBack(Control? focused)
    {
        if (focused == null) return;
        var top = focused.TranslatePoint(new Point(0, 0), PageContent)?.Y ?? 0;
        var bottom = focused.TranslatePoint(new Point(0, focused.Bounds.Height), PageContent)?.Y ?? 0;
        ShowRange(top, bottom);
    }

    /// <summary>
    /// Brings page y <paramref name="top"/>…<paramref name="bottom"/> into view with 40 px to spare, resting only on the
    /// stops over the stage (0, actions, board) – also for the folds (R2: the choice list scrolled to wherever its
    /// BringIntoView ended, cutting the title under the header at ≈ 1100).
    /// </summary>
    private void ShowRange(double top, double bottom)
    {
        var viewport = Scroll.Viewport.Height;
        if (viewport <= 0) return;
        var actions = ActionsStop;
        var board = BoardStop;
        var offset = Scroll.Offset.Y;
        double target;
        if (bottom + 40 > offset + viewport)
        {
            var want = bottom + 40 - viewport;
            target = want <= actions ? actions : want <= board ? board : want;
        }
        else if (top - 40 < offset)
        {
            var want = Math.Max(0, top - 40);
            target = want >= board ? want : want >= actions ? actions : 0;
        }
        else return;
        ScrollTo(target);
    }

    /// <summary>The action row (64) whole under the header, 24 px above it.</summary>
    internal double ActionsStop => Math.Max(0, (Actions.TranslatePoint(new Point(0, 0), PageContent)?.Y ?? 0) - 24);

    /// <summary>The stage board's lower edge flush under the header: the back fills the page.</summary>
    internal double BoardStop => Math.Max(ActionsStop, Stage.Height + StageBoard.Height);

    private void ScrollTo(double target)
    {
        // the stop must be reachable: room below the back when the page is too short for it
        var viewport = Scroll.Viewport.Height;
        var content = PageContent.Bounds.Height - BottomSpacer.Height;
        BottomSpacer.Height = Math.Max(0, Math.Ceiling(target + viewport - content));
        var version = ++_scrollVersion;
        Dispatcher.UIThread.Post(() => { if (version == _scrollVersion) Scroll.Offset = new Vector(0, target); }, DispatcherPriority.Loaded);
    }

    // ===================== the fold: nothing cut at the footer =====================

    /// <summary>
    /// R3 QA 4 ("the year line under 'Daneben im Regal' is clipped by the footer"): while the page rests on the stage (over
    /// the board stop), a piece of the back that would be cut by the page's lower edge (incl. the footer's 40 px shade,
    /// <see cref="FoldShade"/> of it) is not shown – the back ends on whole pieces. A piece is a section's heading with its
    /// first content (a heading never stands alone), then each further row; once one piece is folded away, the rest of
    /// its section follows. Read further (board stop and below) everything shows and the page scrolls freely; a focused
    /// piece is always shown (the focus scrolls it whole into view anyway).
    /// </summary>
    private void UpdateFold()
    {
        var viewport = Scroll.Viewport.Height;
        if (viewport <= 0 || _updatingFold) return;
        _updatingFold = true;
        try
        {
            var reading = Scroll.Offset.Y >= BoardStop - 0.5;
            var fold = Scroll.Offset.Y + viewport - FoldShade;
            var focused = TopLevel.GetTopLevel(this)?.FocusManager?.GetFocusedElement() as Visual;
            foreach (var section in new Control[] { AboutSection, ShelfSection, StartSection, CardSection })
            {
                var pieces = FoldPieces(section);
                var folded = false;
                foreach (var piece in pieces)
                {
                    if (!folded && !reading)
                    {
                        var top = piece[0].TranslatePoint(new Point(0, 0), PageContent)?.Y ?? 0;
                        var last = piece[^1];
                        var bottom = last.TranslatePoint(new Point(0, last.Bounds.Height), PageContent)?.Y ?? 0;
                        var hasFocus = focused != null && piece.Any(c => c == focused || focused.GetVisualAncestors().Contains(c));
                        folded = top < fold && bottom > fold && !hasFocus;
                    }
                    foreach (var c in piece) c.Opacity = folded && !reading ? 0 : 1;
                }
            }
        }
        finally { _updatingFold = false; }
    }

    private bool _updatingFold;

    /// <summary>The lower part of the footer's shade that counts as cut (the shade is 40 px; its lower half is near black).</summary>
    private const double FoldShade = 20;

    /// <summary>A section's visible children as fold pieces: the heading joined with the first content, then one per row.</summary>
    private static List<Control[]> FoldPieces(Control section)
    {
        var list = new List<Control[]>();
        if (section is not Panel { IsVisible: true } panel) return list;
        var shown = panel.Children.Where(c => c.IsVisible && c.Bounds.Height > 0).ToList();
        if (shown.Count == 0) return list;
        list.Add(shown.Count > 1 ? new[] { shown[0], shown[1] } : new[] { shown[0] });
        for (var i = 2; i < shown.Count; i++) list.Add(new[] { shown[i] });
        return list;
    }

    private DispatcherTimer? _snapTimer;

    /// <summary>
    /// After free scrolling (wheel, scroll bar) the page settles on the nearest stop when it came to rest over the stage;
    /// inside the back the header gets the room's tone and the 12 px fade (DESIGN 8.4) softens the cut.
    /// </summary>
    private void OnScrolled()
    {
        UpdateHeaderGround();
        _snapTimer ??= new DispatcherTimer(TimeSpan.FromMilliseconds(180), DispatcherPriority.Background, (_, _) =>
        {
            _snapTimer!.Stop();
            SnapToStop();
        });
        _snapTimer.Stop();
        _snapTimer.Start();
    }

    private void SnapToStop()
    {
        var y = Scroll.Offset.Y;
        var actions = ActionsStop;
        var board = BoardStop;
        double target;
        if (y > 0.5 && y < actions - 0.5) target = y < actions / 2 ? 0 : actions;
        else if (y > actions + 0.5 && y < board - 0.5) target = y - actions < (board - actions) / 2 ? actions : board;
        else return;
        ScrollTo(target);
    }

    /// <summary>Reading the back: the header on Raum (no lighter TV-light band above the plain back) and the top fade.</summary>
    private void UpdateHeaderGround()
    {
        // QA 2: as soon as the page has scrolled, the box and the buttons fade out under the header like the Werkbank
        // lists (12 px Raum) instead of being cut hard at its edge
        var scrolled = Scroll.Offset.Y > 0.5;
        TopFade.IsVisible = scrolled;
        if (_vm != null) _vm.HeaderGround = scrolled ? RoomColor : default;
    }

    private static readonly Color RoomColor = Color.Parse("#0B0C0E");

    /// <summary>What has to be visible for a focused element: a lifted box with its board, a screen with its caption.</summary>
    private Control? FocusTarget(Visual v)
    {
        if (IsInside(v, NeighbourHost)) return NeighbourHost;
        if (IsInside(v, ScreensShelf)) return ScreensShelf;
        return v as Control;
    }

    /// <summary>Shoulder left/right (DESIGN 7.3): Bühne → Über das Spiel → Start, each at its first element.</summary>
    private void OnSectionRequested(int direction)
    {
        var focused = TopLevel.GetTopLevel(this)?.FocusManager?.GetFocusedElement() as Visual;
        var sections = new List<(Visual Root, Func<bool> Focus)> { (Stage, FocusDefault) };
        if (AboutSection.IsVisible && FirstFocusable(AboutSection) != null) sections.Add((AboutSection, () => FocusVisible(FirstFocusable(AboutSection))));
        sections.Add((StartSection, () => FocusVisible(EmulatorBox) || FocusVisible(FileRow)));
        // compact: Start comes before "Über das Spiel"
        if (_compact && sections.Count == 3) (sections[1], sections[2]) = (sections[2], sections[1]);
        if (ShelfSection.IsVisible && FirstFocusable(NeighbourRow) != null) sections.Add((ShelfSection, () => FocusVisible(FirstFocusable(NeighbourRow))));
        var current = focused == null ? 0 : sections.FindIndex(s => IsInside(focused, s.Root));
        if (current < 0) current = 0;
        var next = current + direction;
        if (next < 0 || next >= sections.Count) return;
        sections[next].Focus();
    }

    private static Control? FirstFocusable(Visual root) =>
        root.GetVisualDescendants().OfType<Control>().FirstOrDefault(c => c is Button && c.Focusable && c.IsEffectivelyVisible && c.IsEffectivelyEnabled);

    // ===================== overlays =====================

    /// <summary>Set while a click beside an open fold closes it: the focus then goes where the click goes, not back.</summary>
    private bool _closeByPointer;

    private void OnRootPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (_vm?.Sheet is not (DetailSheet.Choice or DetailSheet.FileInfo or DetailSheet.Description) || e.Source is not Visual v) return;
        var anchor = _vm.Sheet switch
        {
            DetailSheet.Choice => ChoiceAnchor(),
            DetailSheet.FileInfo => FileRow,
            _ => (Control)ReadMoreButton,
        };
        // inside the fold or on its own row: the row toggles, the fold handles its clicks
        if (IsInChoice(v) || IsInside(v, anchor) || (_vm.Sheet == DetailSheet.Description && IsInside(v, DescriptionHost))) return;
        _closeByPointer = true;
        try { _vm.CloseSheetCommand.Execute(null); }
        finally { _closeByPointer = false; }
    }

    private DetailSheet _appliedSheet;
    private bool _sheetAbove;

    private void ApplySheet()
    {
        var sheet = _vm?.Sheet ?? DetailSheet.None;
        var was = _appliedSheet;
        _appliedSheet = sheet;
        // Mehr and "Auch im Regal" are lists that hang at their button (M4 QA: a centred 680 px card for four short
        // items read as a stock modal)
        var list = sheet is DetailSheet.More or DetailSheet.Variants;
        // the choice list, the file info and the long description are no overlays: they fold open in the page (M6 QA,
        // QA 2 – three centred sheets with three typographies read as a default dialog kit); only the lists and the
        // large views lie on the dimmed layer
        var choice = sheet == DetailSheet.Choice;
        var fold = choice || sheet is DetailSheet.FileInfo or DetailSheet.Description;
        SheetLayer.IsVisible = sheet != DetailSheet.None && !fold;
        EmulatorFold.IsOpen = choice && _vm?.Choice == DetailChoice.Emulator;
        ModeFold.IsOpen = choice && _vm?.Choice == DetailChoice.Mode;
        FileFold.IsOpen = sheet == DetailSheet.FileInfo;
        ApplyDescription(sheet == DetailSheet.Description);
        SheetPanel.IsVisible = list;
        MoreRows.IsVisible = sheet == DetailSheet.More;
        VariantRows.IsVisible = sheet == DetailSheet.Variants;
        PlaceSheet(sheet);
        // R2 animation: a list unfolds from its button (rows 20 ms apart), the dim comes up in Scene
        if (sheet != was && sheet != DetailSheet.None && !fold)
        {
            if (was == DetailSheet.None || was is DetailSheet.Choice or DetailSheet.FileInfo or DetailSheet.Description) Anim.FadeIn(SheetScrim);
            if (list)
            {
                var rows = (sheet == DetailSheet.More ? MoreRows.Children.Where(c => c.IsVisible) : VariantRows.GetVisualDescendants().OfType<Button>()).Cast<Control>().ToList();
                Anim.Unfold(SheetPanel, rows, fromTop: !_sheetAbove);
            }
        }
        CoverView.IsVisible = sheet == DetailSheet.Cover;
        ScreensView.IsVisible = sheet == DetailSheet.Screens;
        // the cover overlay dims the page almost completely (the box is the only thing to look at); the header gets
        // the same dim from the page model, so the two meet without a seam
        SheetScrim.Background = new ImmutableSolidColorBrush(sheet is DetailSheet.Cover or DetailSheet.Screens ? GameDetailViewModel.SheetDimDeep : GameDetailViewModel.SheetDim);
        if (sheet == DetailSheet.None)
        {
            var back = _sheetOpener;
            _sheetOpener = null;
            if (_closeByPointer) return;
            if (back is { IsEffectivelyVisible: true }) Dispatcher.UIThread.Post(() => FocusVisible(back), DispatcherPriority.Loaded);
            else if (IsEffectivelyVisible) Dispatcher.UIThread.Post(() => FocusDefault(), DispatcherPriority.Loaded);
            return;
        }
        var focused = TopLevel.GetTopLevel(this)?.FocusManager?.GetFocusedElement() as Control;
        if (focused != null && !IsInside(focused, SheetLayer) && !IsInChoice(focused)) _sheetOpener = focused;
        SizeSheetViews();
        if (choice && ChoiceAnchor() is { } row)
        {
            // the row and its list come into view on a stop once the fold has its height (R2)
            var openFold = _vm?.Choice == DetailChoice.Mode ? ModeFold : EmulatorFold;
            Dispatcher.UIThread.Post(() => ShowFold(row, openFold, 260), DispatcherPriority.Loaded);
        }
        else if (sheet == DetailSheet.FileInfo)
            Dispatcher.UIThread.Post(() => ShowFold(FileRow, FileFold, 300), DispatcherPriority.Loaded);
        Dispatcher.UIThread.Post(FocusSheet, DispatcherPriority.Loaded);
    }

    /// <summary>A fold and its row in view on a stop: the row 48 px under the header's edge at most, the open list below.</summary>
    private void ShowFold(Control row, Control fold, double fallback)
    {
        var top = (row.TranslatePoint(new Point(0, 0), PageContent)?.Y ?? 0) - 8;
        var rowBottom = row.TranslatePoint(new Point(0, row.Bounds.Height), PageContent)?.Y ?? top;
        // the fold animates open: its final height is the content's desired height, else the estimate
        var content = (fold as Decorator)?.Child ?? fold;
        var height = content.DesiredSize.Height > 0 ? content.DesiredSize.Height : fallback;
        ShowRange(top, rowBottom + height);
    }

    private Control ChoiceAnchor() => _vm?.Choice == DetailChoice.Mode ? ModeBox : EmulatorBox;
    private ItemsControl ChoiceList() => _vm?.Choice == DetailChoice.Mode ? ModeChoices : EmulatorChoices;
    private bool IsInChoice(Visual v) => IsInside(v, EmulatorFold) || IsInside(v, ModeFold) || IsInside(v, FileFold);

    /// <summary>Lines of the description before "Weiterlesen"; opened it shows up to <see cref="OpenLines"/> and scrolls inside them.</summary>
    private const int ClosedLines = 8, OpenLines = 12;

    /// <summary>"Weiterlesen" opens the text in place (QA 2): up to 12 lines, the rest scrolls inside with a 12 px fade.</summary>
    private void ApplyDescription(bool open)
    {
        DescriptionText.MaxLines = open ? 0 : ClosedLines;
        DescriptionText.TextTrimming = open ? TextTrimming.None : TextTrimming.WordEllipsis;
        DescriptionScroll.MaxHeight = open ? OpenLines * DescriptionText.LineHeight : double.PositiveInfinity;
        if (!open) DescriptionScroll.Offset = default;
        ReadMoreText.Text = Loc.T(open ? "detail.readLess" : "detail.readMore");
        Dispatcher.UIThread.Post(UpdateDescriptionFade, DispatcherPriority.Loaded);
        if (open) AboutSection.BringIntoView(new Rect(0, -48, AboutSection.Bounds.Width, Math.Min(AboutSection.Bounds.Height, 600) + 48));
    }

    private void UpdateDescriptionFade() =>
        DescriptionFade.IsVisible = DescriptionScroll.Extent.Height > DescriptionScroll.Viewport.Height + DescriptionScroll.Offset.Y + 1;

    private void FocusSheet()
    {
        if (_vm?.IsSheetOpen != true) return;
        var target = _vm.Sheet switch
        {
            DetailSheet.More => MoreRows.Children.OfType<Button>().FirstOrDefault(b => b.IsEffectivelyVisible),
            DetailSheet.Variants => VariantRows.GetVisualDescendants().OfType<Button>().FirstOrDefault(b => b.IsEffectivelyVisible),
            // the list opens on the current choice
            DetailSheet.Choice => ChoiceList().GetVisualDescendants().OfType<Button>().FirstOrDefault(b => b.IsEffectivelyVisible && b.Classes.Contains("current"))
                                  ?? ChoiceList().GetVisualDescendants().OfType<Button>().FirstOrDefault(b => b.IsEffectivelyVisible),
            DetailSheet.FileInfo => FileFolderButton,
            DetailSheet.Description => ReadMoreButton,
            _ => null,
        };
        if (target != null) target.Focus(NavigationMethod.Directional);
        else SheetLayer.Focus(NavigationMethod.Directional);
    }

    /// <summary>
    /// A list hangs at its button: left = the button's left, top = its bottom + 8 (above the button when there is no
    /// room below). The other sheets stand centred.
    /// </summary>
    private void PlaceSheet(DetailSheet sheet)
    {
        var anchor = sheet switch
        {
            DetailSheet.More => (Control)MoreButton,
            DetailSheet.Variants => VariantsRow,
            _ => null,
        };
        if (anchor == null || anchor.TranslatePoint(new Point(0, 0), Root) is not { } at)
        {
            SheetPanel.HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center;
            SheetPanel.VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center;
            SheetPanel.Margin = default;
            return;
        }
        var layer = Root.Bounds.Size;
        // the variants list starts under the row's value column (label 200 + inset 16)
        // the row lists start under the row's value column (label 200 + inset 16)
        var x = anchor == VariantsRow ? at.X + 216 - 28 : at.X;
        x = Math.Clamp(x, 16, Math.Max(16, layer.Width - SheetPanel.Width - 16));
        var rows = sheet switch
        {
            DetailSheet.More => MoreRows.Children.Count(c => c.IsVisible),
            _ => _vm?.Variants.Count ?? 0,
        };
        var height = rows * 56 + 16 + 2;
        var below = at.Y + anchor.Bounds.Height + 8;
        var y = below + height <= layer.Height - 64 ? below : Math.Max(16, at.Y - 8 - height);
        _sheetAbove = y < below;
        SheetPanel.HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Left;
        SheetPanel.VerticalAlignment = Avalonia.Layout.VerticalAlignment.Top;
        SheetPanel.Margin = new Thickness(x, y, 0, 0);
    }

    /// <summary>The large views fill the page: the cover up to the board, the two screens side by side.</summary>
    private void SizeSheetViews()
    {
        var size = SheetLayer.Bounds.Size;
        if (size.Width <= 0) return;
        var count = (_vm?.HasTitleScreen == true ? 1 : 0) + (_vm?.HasSnapScreen == true ? 1 : 0);
        if (count == 0) return;
        // board 24, captions 12 + 21, 40 below, 24 above: at most 720 wide each (M4 QA: 840 px slabs read as monitor frames)
        var maxH = size.Height - 40 - 24 - 48 - 40 - 24;
        var w = Math.Min(MaxLargeScreen, Math.Floor((size.Width - 2 * 96 - (count - 1) * 48) / count));
        while (w > 200 && PixelScreen.SetHeightFor(w) > maxH) w -= 8;
        foreach (var s in new[] { TitleLarge, SnapLarge })
        {
            s.Width = w;
            s.Height = PixelScreen.SetHeightFor(w);
        }
        TitleLargeCaption.Width = SnapLargeCaption.Width = w;
    }

    private const double MaxLargeScreen = 720;

    // ===================== directional navigation =====================

    public override bool MoveFocus(NavAction action, Control focused)
    {
        if (_vm?.IsSheetOpen == true)
        {
            // an overlay is modal: the focus stays inside; the long description scrolls with up/down
            if (_vm.Sheet == DetailSheet.Description)
            {
                // the opened text: up/down scroll it inside its 12 lines, the focus stays on "Weniger anzeigen"
                if (action is NavAction.Up or NavAction.Down)
                {
                    var y = DescriptionScroll.Offset.Y + (action == NavAction.Down ? 120 : -120);
                    DescriptionScroll.Offset = new Vector(0, Math.Clamp(y, 0, Math.Max(0, DescriptionScroll.Extent.Height - DescriptionScroll.Viewport.Height)));
                    UpdateDescriptionFade();
                }
                if (focused != ReadMoreButton) FocusSheet();
                return true;
            }
            if (_vm.Sheet == DetailSheet.FileInfo)
            {
                // the folded-open file info has one button; the focus stays on it until Back or confirm on the row
                if (focused != FileFolderButton) FocusSheet();
                return true;
            }
            if (_vm.Sheet == DetailSheet.Choice)
            {
                // the folded-open list: up/down walk its rows, the focus never leaves it until a choice or Back
                var rows = ChoiceList().GetVisualDescendants().OfType<Button>().Where(b => b.IsEffectivelyVisible).ToList();
                var i = rows.IndexOf(focused as Button ?? null!);
                if (i < 0) { FocusSheet(); return true; }
                var n = i + (action == NavAction.Down ? 1 : action == NavAction.Up ? -1 : 0);
                if (n >= 0 && n < rows.Count && n != i) rows[n].Focus(NavigationMethod.Directional);
                return true;
            }
            var fm = TopLevel.GetTopLevel(this)?.FocusManager;
            if (!IsInside(focused, SheetLayer)) { FocusSheet(); return true; }
            var dir = action switch
            {
                NavAction.Up => NavigationDirection.Up,
                NavAction.Down => NavigationDirection.Down,
                NavAction.Left => NavigationDirection.Left,
                _ => NavigationDirection.Right,
            };
            fm?.FindNextElement(dir, new FindNextElementOptions { SearchRoot = SheetLayer })?.Focus(NavigationMethod.Directional);
            return true;
        }
        // from the actions down: "Start" first (the rows decide how the game starts), the screens are one Right away
        if (action == NavAction.Down && IsInside(focused, Stage))
        {
            if (_compact || !AboutSection.IsVisible) return FocusVisible(EmulatorBox) || FocusVisible(FileRow);
            return FocusVisible(FirstFocusable(AboutSection)) || FocusVisible(EmulatorBox);
        }
        // up from the top of the back: the actions (in the compact column "Über das Spiel" lies below "Start")
        if (action == NavAction.Up && (focused == EmulatorBox || !_compact && focused == FirstFocusable(AboutSection)))
            return FocusDefault();
        return false;
    }

    private static bool IsInside(Visual v, Visual root)
    {
        for (Visual? x = v; x != null; x = x.GetVisualParent()) if (x == root) return true;
        return false;
    }

    /// <summary>File picker for "Cover wählen…" (PNG/JPG/WebP).</summary>
    private async Task<string?> PickImageAsync()
    {
        var storage = TopLevel.GetTopLevel(this)?.StorageProvider;
        if (storage == null || !storage.CanOpen) return null;
        var result = await storage.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = Loc.T("artwork.pickTitle"),
            AllowMultiple = false,
            FileTypeFilter =
            [
                new FilePickerFileType(Loc.T("artwork.pickFilter")) { Patterns = ["*.png", "*.jpg", "*.jpeg", "*.webp"] },
            ],
        });
        return result.Count > 0 ? result[0].TryGetLocalPath() : null;
    }

    /// <summary>First focus: "Spielen", or – when the game cannot start – the solution button.</summary>
    public override bool FocusDefault() => FocusVisible(PlayButton) || FocusVisible(SolveButton) || FocusVisible(EmulatorBox);
}
