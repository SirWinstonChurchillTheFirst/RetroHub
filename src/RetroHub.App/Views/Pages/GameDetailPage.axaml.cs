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

    public GameDetailPage()
    {
        InitializeComponent();
        // the pulled box is an object like a box on a shelf: lift, cone (inside the stage), lit board piece, ring
        _boxFocus = new FocusLayer(BoxSlot, BoxSlot) { BoardFront = ShelfBoard.StageHeight, ConeHeadroom = 56, ConeOutsideBounds = true };
        BoxSlot.Children.Insert(0, _boxFocus.Back);
        BoxSlot.Children.Add(_boxFocus.Front);
        DetailBox.TemplateApplied += (_, _) =>
        {
            StageBoard.Casters = Casters();
            if (DetailBox.Art != null) DetailBox.Art.ArtRectChanged += (_, _) => { ApplyBoxFrame(); StageBoard.InvalidateVisual(); };
            ApplySpine();
        };
        Stage.LayoutUpdated += (_, _) => StageBoard.InvalidateVisual();
        ScreensBoard.Casters = [TitleSet, SnapSet];
        ScreenSets.LayoutUpdated += (_, _) => ScreensBoard.InvalidateVisual();
        CoverBoard.Casters = [CoverLarge];
        ScreensLargeBoard.Casters = [TitleLarge, SnapLarge];
        LargeSets.LayoutUpdated += (_, _) => ScreensLargeBoard.InvalidateVisual();
        DescriptionText.SizeChanged += (_, _) => UpdateReadMore();
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
        ScreensView.PointerPressed += (_, _) => _vm?.CloseSheetCommand.Execute(null);

        SizeChanged += (_, e) => ApplyLayout(e.NewSize);
        SheetLayer.SizeChanged += (_, _) => SizeSheetViews();
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
        Stage.Height = _compact ? 398 : 588;
        Stage.ColumnDefinitions[1].Width = new GridLength(_compact ? 64 : 56);
        StageTitle.Sizes = _compact ? "56,40" : "72,56,40";
        TextColumn.Margin = new Thickness(0, 0, 0, _compact ? 32 : 44);
        _boxFocus.ConeHeadroom = _compact ? 40 : 56;
        var set = _compact ? 360.0 : 400.0;
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
        var wide = DetailBox.Art?.ImageAspect is > 1.05;
        var frame = _compact ? (wide ? 360 : 240) : (wide ? WideFrameWidth : FrameWidth);
        var width = SpineWidth + frame;
        var height = _compact ? 336 : 504;
        if (!force && BoxSlot.Width == width && BoxSlot.Height == height) return;
        BoxSlot.Width = width;
        BoxSlot.Height = height;
        DetailBox.Width = width;
        DetailBox.Height = height;
    }

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
        Dispatcher.UIThread.Post(FillNeighbours, DispatcherPriority.Loaded);
    }

    /// <summary>Puts the sections into <paramref name="stack"/> in order: the first 16 px under the board's shadow, the others 48 apart.</summary>
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
            c.Margin = new Thickness(0, first ? 16 : 48, 0, 0);
            if (c.IsVisible) first = false;
        }
    }

    // ===================== Daneben im Regal =====================

    /// <summary>
    /// The shelf the box was pulled from: as many neighbours as fit the column, the gap (a dashed place as wide as one
    /// box) where this box stood, roughly in the middle. All boxes of one system share its box format (no FitToImage),
    /// so the count is known before the covers load.
    /// </summary>
    private void FillNeighbours()
    {
        if (_vm == null) return;
        var width = NeighbourHost.Bounds.Width;
        if (width <= 0) width = _compact ? 1100 : 744;
        var height = _compact ? 150.0 : 180.0;
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
                NeighbourRow.Children.Add(new EmptySlot { Width = slot, Height = height, VerticalAlignment = Avalonia.Layout.VerticalAlignment.Top });
            }
        }
        NeighbourBoard.Casters = tiles.Select(t => (Control)(t.Art ?? (Control)t)).ToList();
    }

    private static readonly object GapMarker = new();

    /// <summary>The caption under a box (GameTile: 24 = board 12 + gap 12, then the 66 px title and label block).</summary>
    private const double CaptionBlock = 90;

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
        ReadMoreButton.IsVisible = layout.TextLines.Count > DescriptionText.MaxLines;
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
                _ when x == EmulatorBox || x == ModeBox => DetailFocus.Row,
                _ when x == FileRow => DetailFocus.File,
                _ when x == TitleScreenButton || x == SnapScreenButton => DetailFocus.Screen,
                _ when x == ReadMoreButton => DetailFocus.ReadMore,
                GameTile when x.GetVisualParent() == NeighbourRow => DetailFocus.Neighbour,
                _ => null,
            };
        }
        if (zone is { } z) _vm.SetFocus(z);
        if (e.NavigationMethod != NavigationMethod.Directional) return;
        // the stage shows from the top; the back is shown whole when it fits under the box (turning the box around)
        if (IsInside(v, Stage)) Scroll.Offset = default;
        else if (IsInside(v, Back)) ShowBack(FocusTarget(v));
    }

    /// <summary>
    /// Scrolls so that <paramref name="focused"/> (in the back) is visible with 40 px to spare – and never cuts the
    /// stage's text in half: the top edge stops above the system label, just above the actions (the title has gone
    /// up, "Spielen" stays in reach) or anywhere from there down. Room below the back makes the cut above the actions
    /// reachable on a short page.
    /// </summary>
    private void ShowBack(Control? focused)
    {
        if (focused == null) return;
        var viewport = Scroll.Viewport.Height;
        if (viewport <= 0) return;
        var top = focused.TranslatePoint(new Point(0, 0), PageContent)?.Y ?? 0;
        var bottom = focused.TranslatePoint(new Point(0, focused.Bounds.Height), PageContent)?.Y ?? 0;
        var labelCut = Math.Max(0, (SystemButton.TranslatePoint(new Point(0, 0), PageContent)?.Y ?? 0) - 24);
        var actionsCut = Math.Max(0, (Actions.TranslatePoint(new Point(0, 0), PageContent)?.Y ?? 0) - 14);
        var offset = Scroll.Offset.Y;
        double target;
        if (bottom + 40 > offset + viewport)
        {
            var want = bottom + 40 - viewport;
            target = want <= labelCut ? want : want <= actionsCut ? actionsCut : want;
        }
        else if (top - 40 < offset)
        {
            var want = Math.Max(0, top - 40);
            target = want >= actionsCut ? want : want >= labelCut ? labelCut : want;
        }
        else return;
        // the cut above the actions must be reachable: room below the back when the page is too short for it
        var content = PageContent.Bounds.Height - BottomSpacer.Height;
        BottomSpacer.Height = Math.Max(0, Math.Ceiling(target + viewport - content));
        Dispatcher.UIThread.Post(() => Scroll.Offset = new Vector(0, target), DispatcherPriority.Loaded);
    }

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

    private void ApplySheet()
    {
        var sheet = _vm?.Sheet ?? DetailSheet.None;
        var panel = sheet is DetailSheet.More or DetailSheet.FileInfo or DetailSheet.Description;
        SheetPanel.IsVisible = panel;
        MoreRows.IsVisible = sheet == DetailSheet.More;
        FileLines.IsVisible = sheet == DetailSheet.FileInfo;
        DescriptionScroll.IsVisible = sheet == DetailSheet.Description;
        SheetFolderButton.IsVisible = sheet == DetailSheet.FileInfo;
        SheetButtons.IsVisible = sheet != DetailSheet.More;
        SheetTitle.Text = sheet switch
        {
            DetailSheet.More => Loc.T("detail.more"),
            DetailSheet.FileInfo => Loc.T("detail.fileInfo"),
            DetailSheet.Description => _vm?.Game?.Title ?? "",
            _ => "",
        };
        CoverView.IsVisible = sheet == DetailSheet.Cover;
        ScreensView.IsVisible = sheet == DetailSheet.Screens;
        // the cover overlay dims the page almost completely (the box is the only thing to look at)
        SheetScrim.Background = new ImmutableSolidColorBrush(Color.FromArgb(sheet is DetailSheet.Cover or DetailSheet.Screens ? (byte)0xF0 : (byte)0xB8, 0x0A, 0x09, 0x08));
        if (sheet == DetailSheet.None)
        {
            var back = _sheetOpener;
            _sheetOpener = null;
            if (back is { IsEffectivelyVisible: true }) Dispatcher.UIThread.Post(() => FocusVisible(back), DispatcherPriority.Loaded);
            else if (IsEffectivelyVisible) Dispatcher.UIThread.Post(() => FocusDefault(), DispatcherPriority.Loaded);
            return;
        }
        var focused = TopLevel.GetTopLevel(this)?.FocusManager?.GetFocusedElement() as Control;
        if (focused != null && !IsInside(focused, SheetLayer)) _sheetOpener = focused;
        SizeSheetViews();
        Dispatcher.UIThread.Post(FocusSheet, DispatcherPriority.Loaded);
    }

    private void FocusSheet()
    {
        if (_vm?.IsSheetOpen != true) return;
        var target = _vm.Sheet switch
        {
            DetailSheet.More => MoreRows.Children.OfType<Button>().FirstOrDefault(b => b.IsEffectivelyVisible),
            DetailSheet.FileInfo or DetailSheet.Description => SheetCloseButton,
            _ => null,
        };
        if (target != null) target.Focus(NavigationMethod.Directional);
        else SheetLayer.Focus(NavigationMethod.Directional);
    }

    /// <summary>The large views fill the page: the cover up to the board, the two screens side by side.</summary>
    private void SizeSheetViews()
    {
        var size = SheetLayer.Bounds.Size;
        if (size.Width <= 0) return;
        var count = (_vm?.HasTitleScreen == true ? 1 : 0) + (_vm?.HasSnapScreen == true ? 1 : 0);
        if (count == 0) return;
        var maxH = size.Height - 40 - 24 - 64 - 24;
        var w = Math.Floor((size.Width - 2 * 96 - (count - 1) * 48) / count);
        while (w > 200 && PixelScreen.SetHeightFor(w) > maxH) w -= 8;
        foreach (var s in new[] { TitleLarge, SnapLarge })
        {
            s.Width = w;
            s.Height = PixelScreen.SetHeightFor(w);
        }
    }

    // ===================== directional navigation =====================

    public override bool MoveFocus(NavAction action, Control focused)
    {
        if (_vm?.IsSheetOpen == true)
        {
            // an overlay is modal: the focus stays inside; the long description scrolls with up/down
            if (_vm.Sheet == DetailSheet.Description && action is NavAction.Up or NavAction.Down)
            {
                var y = DescriptionScroll.Offset.Y + (action == NavAction.Down ? 120 : -120);
                DescriptionScroll.Offset = new Vector(0, Math.Clamp(y, 0, Math.Max(0, DescriptionScroll.Extent.Height - DescriptionScroll.Viewport.Height)));
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
