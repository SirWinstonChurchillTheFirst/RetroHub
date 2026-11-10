using System.Globalization;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using RetroHub.App.Input;
using RetroHub.App.Theming;
using RetroHub.App.ViewModels;

namespace RetroHub.App.Controls;

/// <summary>
/// A system on a shelf (DESIGN 7.1 "Fächer-Brett", 7.4 "Fächerwand"): a SPINE (wall-light tone with the era texture, 8 px
/// band in the system's colour, the code turned −90° in the spine role HANGING 16 px under the band – on Start and on the
/// Konsolen wall alike –, the count at the foot and the motif standing above it: full width on the Konsolen wall
/// (<see cref="ShowMotif"/>), 40 px on a Start spine of ≥ 200) that turns its FRONT forward when it gets the focus (the
/// system's wall with band, texture and the motif; a big front (Konsolen) carries an inner shelf with up to three of its
/// games, the small front of Start the motif at 72 %; the name, maker and year, the count).
/// The front may be taller than the spine (<see cref="SpineHeight"/>; Konsolen: spine 460, front 600): both stand on the
/// same board, the drawn height grows with the width. Only the width of the visible part animates (Glide 220 ms; 90 ms
/// linear while a direction is held; immediate when motion is reduced/off); the front is always laid out at its full
/// size and clipped, so its text never re-wraps, and it fades in over the last 40 % of the growth. Focus: the chamfered
/// standing ring in the system's light and a lift of 1.03 (no cone: the wall is the light surface).
/// DataContext = <see cref="SystemItemViewModel"/>. Drawn in one Render pass; text layouts are built on change only.
/// </summary>
public sealed class SystemSpine : Button, Avalonia.Rendering.ICustomHitTest
{
    public static readonly StyledProperty<double> SpineWidthProperty = AvaloniaProperty.Register<SystemSpine, double>(nameof(SpineWidth), 72);
    public static readonly StyledProperty<double> FrontWidthProperty = AvaloniaProperty.Register<SystemSpine, double>(nameof(FrontWidth), 200);
    /// <summary>Height of the closed spine (NaN = the control's height; the open front always takes the full height).</summary>
    public static readonly StyledProperty<double> SpineHeightProperty = AvaloniaProperty.Register<SystemSpine, double>(nameof(SpineHeight), double.NaN);
    /// <summary>The motif stands at the foot of the spine (Konsolen: the wall reads without text, DESIGN 7.4).</summary>
    public static readonly StyledProperty<bool> ShowMotifProperty = AvaloniaProperty.Register<SystemSpine, bool>(nameof(ShowMotif));
    /// <summary>
    /// False: the spine never turns a front (the thin spines of systems without games, "Noch leer"): focus is ring and
    /// lift only, and the spine is drawn quieter (status: empty).
    /// </summary>
    public static readonly StyledProperty<bool> OpensProperty = AvaloniaProperty.Register<SystemSpine, bool>(nameof(Opens), true);
    /// <summary>
    /// How far an empty sleeve ("Noch leer", <see cref="Opens"/> false) is pulled out of its row: 0 = standing (maybe
    /// leaning on its neighbour), 1 = straightened and lifted 8 px with its label in full colour (Step 140 ms).
    /// </summary>
    public static readonly StyledProperty<double> PullProperty = AvaloniaProperty.Register<SystemSpine, double>(nameof(Pull));

    private static readonly IImmutableSolidColorBrush Paper = new ImmutableSolidColorBrush(SystemPalette.Paper);
    private static readonly IImmutableSolidColorBrush Text2 = new ImmutableSolidColorBrush(Color.Parse("#C9BFAF"));
    private static readonly IImmutableSolidColorBrush LightEdge = new ImmutableSolidColorBrush(Color.Parse("#17FFF3E0"));

    /// <summary>Where the code hangs: 16 px under the 8 px band (one anchor on Start and Konsolen, M5 QA).</summary>
    public const double CodeTop = 8 + 16;

    private SystemItemViewModel? _system;
    // the inner shelf of a big front: up to three boxes (built when the front first opens, after DecodeDwell)
    private Canvas? _shelf;
    private readonly List<CoverArt> _covers = [];
    private Rect[] _slots = [];
    private double _boardY, _recessTop;
    private Rect _motifBox;
    private Avalonia.Threading.DispatcherTimer? _dwell;
    private FormattedText? _code, _codeLit, _codeInk, _codeShadow, _count, _name, _sub, _frontCount;
    private (string?, double, double, int, double) _textKey;
    private bool _open;
    private double _ringOpacity;

    static SystemSpine()
    {
        AffectsRender<SystemSpine>(BoundsProperty, ShowMotifProperty, SpineHeightProperty, PullProperty);
        AffectsMeasure<SystemSpine>(SpineWidthProperty, FrontWidthProperty);
        FocusAdornerProperty.OverrideDefaultValue<SystemSpine>(null);
        ClipToBoundsProperty.OverrideDefaultValue<SystemSpine>(false);
    }

    public SystemSpine()
    {
        TextOptions.SetTextRenderingMode(this, TextRenderingMode.Antialias);
        Cursor = new Cursor(StandardCursorType.Hand);
        RenderTransformOrigin = new RelativePoint(0.5, 1, RelativeUnit.Relative);
        RenderTransform = new ScaleTransform(1, 1);
        Width = SpineWidth;
        FocusAdorner = null; // a local value: the theme's 2 px focus rectangle never shows (the chamfered ring is the focus)
    }

    // no Button theme: everything is drawn in Render (a theme's background would cover it)
    protected override Type StyleKeyOverride => typeof(SystemSpine);

    /// <summary>Only the drawn object takes the pointer (not the free space above a short spine or beside an open front).</summary>
    public bool HitTest(Point point) => ObjectRect.Contains(point);

    public double SpineWidth { get => GetValue(SpineWidthProperty); set => SetValue(SpineWidthProperty, value); }
    public double FrontWidth { get => GetValue(FrontWidthProperty); set => SetValue(FrontWidthProperty, value); }
    public double SpineHeight { get => GetValue(SpineHeightProperty); set => SetValue(SpineHeightProperty, value); }
    public bool ShowMotif { get => GetValue(ShowMotifProperty); set => SetValue(ShowMotifProperty, value); }
    public bool Opens { get => GetValue(OpensProperty); set => SetValue(OpensProperty, value); }
    public double Pull { get => GetValue(PullProperty); set => SetValue(PullProperty, value); }

    /// <summary>Free space on each side of an open front (M2 QA): ring (3 + 3) and lift 1.03 never touch the neighbours.</summary>
    public const double FrontGap = 16;

    /// <summary>Width of the item while its front is turned forward: the front plus <see cref="FrontGap"/> on both sides.</summary>
    public double OpenWidth => FrontWidth + 2 * FrontGap;

    /// <summary>The front is (or is becoming) visible.</summary>
    public bool IsOpen => _open;

    /// <summary>A big front (Konsolen): poster name in 44/36, maker and year in the reading role, the count in Zahl 40.</summary>
    private bool LargeFront => FrontWidth >= 360;

    /// <summary>
    /// A closed spine stands 0, 4 or 8 px lower than the row (Start: 0, 2 or 4), seeded by the system: printed boxes
    /// are never cut to one height (M7 "gezeichnet").
    /// </summary>
    private double SpineDrop => _system == null ? 0 : Craft.Pick(Craft.Seed(_system.Id), 11, 0, 2) * (ShowMotif ? 4 : 2);

    /// <summary>Share of the way from spine (0) to front (1), from the current (animating) width.</summary>
    private double Openness => Math.Clamp((Bounds.Width - SpineWidth) / Math.Max(1, OpenWidth - SpineWidth), 0, 1);

    /// <summary>The drawn object in local coordinates: the spine, or the front standing FrontGap in from both sides.</summary>
    public Rect ObjectRect
    {
        get
        {
            var r = new Rect(Bounds.Size);
            if (r.Width <= 0 || r.Height <= 0) return default;
            if (!Opens && _system != null)
            {
                var sl = Sleeve;
                var lift = SleeveLift;
                return new Rect(sl.Front.X, sl.Front.Y - SleeveTop - lift, sl.Front.Width + SleeveSide, r.Height - sl.Front.Y + SleeveTop + lift);
            }
            var t = Openness;
            var spineH = (double.IsNaN(SpineHeight) ? r.Height : Math.Min(SpineHeight, r.Height)) - SpineDrop;
            var h = Math.Round(spineH + (r.Height - spineH) * t);
            var pad = Math.Round(FrontGap * t);
            return new Rect(pad, r.Height - h, Math.Max(1, r.Width - 2 * pad), h);
        }
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (_system != null) _system.PropertyChanged -= OnSystemChanged;
        _system = DataContext as SystemItemViewModel;
        if (_system != null) _system.PropertyChanged += OnSystemChanged;
        _textKey = default;
        UpdateCovers();
        InvalidateVisual();
    }

    private void OnSystemChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(SystemItemViewModel.GameCount) or nameof(SystemItemViewModel.CountText))
        {
            _textKey = default;
            InvalidateVisual();
        }
        else if (e.PropertyName == nameof(SystemItemViewModel.Featured)) UpdateCovers();
    }

    /// <summary>A big front (Konsolen) carries an inner shelf with games (M5 QA: the open front was an empty slab).</summary>
    private bool HasShelf => LargeFront && Opens;

    /// <summary>
    /// The boxes of the inner shelf: built the first time the front opens, once the focus rested <see cref="Motion.DecodeDwell"/>
    /// and no direction is held (no decodes while the row glides past); then they only follow the system's games.
    /// </summary>
    private void UpdateCovers()
    {
        if (_shelf == null) return;
        var games = _system?.Featured ?? [];
        var count = Math.Min(games.Count, FrontWidth >= 480 ? 3 : 2);
        while (_covers.Count < count)
        {
            var art = new CoverArt { ShowFavorite = false, IsHitTestVisible = false };
            art.ArtRectChanged += (_, _) => InvalidateVisual();
            _covers.Add(art);
            _shelf.Children.Add(art);
        }
        for (var i = 0; i < _covers.Count; i++)
        {
            var art = _covers[i];
            art.IsVisible = i < count;
            if (i >= count) continue;
            var g = games[i];
            art.SystemId = g.SystemId;
            art.Accent = g.Accent;
            art.Badge = g.SystemShort;
            art.Title = g.Title;
            art.Year = g.YearText;
            art.IsMissing = g.IsMissing;
            art.SlotFormat = SystemPalette.BoxAspect(g.SystemId);
            art.Source = g.CoverPath;
        }
        InvalidateArrange();
        InvalidateVisual();
    }

    private void EnsureShelf()
    {
        if (_shelf != null || !HasShelf) return;
        if (NavRepeatTracker.Current?.IsActive == true || _dwell != null)
        {
            if (_dwell != null) return;
            _dwell = new Avalonia.Threading.DispatcherTimer { Interval = Motion.DecodeDwell };
            _dwell.Tick += (_, _) =>
            {
                if (NavRepeatTracker.Current?.IsActive == true && _open) return; // keep waiting while a direction is held
                _dwell!.Stop();
                _dwell = null;
                if (_open) EnsureShelf();
            };
            _dwell.Start();
            return;
        }
        _shelf = new Canvas { ClipToBounds = true, IsHitTestVisible = false };
        VisualChildren.Add(_shelf);
        LogicalChildren.Add(_shelf);
        UpdateCovers();
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        _shelf?.Measure(Size.Infinity);
        return default;
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        if (_shelf == null) return finalSize;
        var area = ObjectRect;
        LayoutFront(finalSize.Height);
        var t = Openness;
        _shelf.Opacity = Math.Clamp((t - 0.6) / 0.4, 0, 1);
        _shelf.IsVisible = t > 0.6;
        _shelf.Arrange(area);
        for (var i = 0; i < _covers.Count && i < _slots.Length; i++)
        {
            var slot = _slots[i];
            Canvas.SetLeft(_covers[i], slot.X);
            Canvas.SetTop(_covers[i], slot.Y - area.Y);
            _covers[i].Width = slot.Width;
            _covers[i].Height = slot.Height;
        }
        return finalSize;
    }

    /// <summary>
    /// The big front's layout (front coordinates, origin top left of the front): the inner board at 62 % of the height
    /// (10 px, the wall's board tone), up to three boxes standing on it from the inset (art height 150, ≈1100: 110, gap
    /// 12, the system's box format, never into the name and maker above), below it the count left and the motif right.
    /// </summary>
    private void LayoutFront(double height)
    {
        var inset = FrontInset;
        var big = FrontWidth >= 480;
        _boardY = Math.Round(height * 0.62);
        var textBottom = 8 + Math.Round(inset * 0.8) + (_name?.Height ?? 0) + 8 + (_sub?.Height ?? 0);
        var count = _covers.Count(c => c.IsVisible);
        var aspect = _system != null ? SystemPalette.BoxAspect(_system.Id) : 1.25;
        var h = Math.Min(big ? 150 : 110, _boardY - textBottom - 16);
        var room = FrontWidth - 2 * inset;
        if (count > 0 && count * h / aspect + (count - 1) * 12 > room) h = (room - (count - 1) * 12) / count * aspect;
        h = Math.Max(0, Math.Floor(h));
        var w = Math.Floor(h / aspect);
        _slots = new Rect[count];
        for (var i = 0; i < count; i++) _slots[i] = new Rect(inset + i * (w + 12), _boardY - h, w, h);
        // the inner compartment is cut into the front from just under the maker line down to the inner board
        _recessTop = Math.Round(Math.Max(textBottom + 6, _boardY - Math.Max(h, big ? 150 : 110) - 18));
        // the motif stands on the stage board, right, in the part under the inner board
        var below = height - (_boardY + 10);
        var size = Math.Round(Math.Min(below - (big ? 24 : 16), FrontWidth * 0.46));
        _motifBox = new Rect(FrontWidth - Math.Round(FrontWidth * 0.06) - size, height - size, size, size);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == SpineWidthProperty || change.Property == FrontWidthProperty)
        {
            SetWidth(animate: false);
            _textKey = default;
        }
        else if (change.Property == IsFocusedProperty || change.Property == IsPointerOverProperty) UpdateState();
    }

    protected override void OnGotFocus(FocusChangedEventArgs e)
    {
        base.OnGotFocus(e);
        UpdateState();
    }

    protected override void OnLostFocus(FocusChangedEventArgs e)
    {
        base.OnLostFocus(e);
        UpdateState();
    }

    /// <summary>
    /// Keyboard/controller focus opens the front; hover shows the ring at 40 %. The mouse: the first click on a closed spine
    /// turns its front forward (Shelf), the click on the open front opens the system's page.
    /// </summary>
    private void UpdateState()
    {
        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            var visible = IsFocused && Classes.Contains(":focus-visible");
            var open = visible && Opens;
            _ringOpacity = visible ? 1 : IsPointerOver ? 0.4 : 0;
            if (open != _open)
            {
                _open = open;
                SetWidth(animate: true);
                if (open) EnsureShelf();
            }
            // the ring stands above the neighbouring spines (they would cover it: later siblings draw on top)
            var z = _ringOpacity > 0 ? 1 : 0;
            ZIndex = z;
            if (Avalonia.VisualTree.VisualExtensions.GetVisualParent(this) is Avalonia.Controls.Presenters.ContentPresenter container) container.ZIndex = z;
            if (!Opens)
            {
                // an empty sleeve is pulled out of its row instead of growing (straightened, lifted, label lit)
                Transitions = Motion.Current == EffectiveMotion.Full && NavRepeatTracker.Current?.IsActive != true
                    ? [new DoubleTransition { Property = PullProperty, Duration = Motion.StepDuration, Easing = new CubicEaseOut() }]
                    : null;
                Pull = visible ? 1 : 0;
            }
            var lift = visible && Opens && Motion.AllowsMovement ? 1.03 : 1.0;
            if (RenderTransform is ScaleTransform st)
            {
                st.Transitions = Motion.Current == EffectiveMotion.Full && NavRepeatTracker.Current?.IsActive != true
                    ? [new DoubleTransition { Property = ScaleTransform.ScaleXProperty, Duration = Motion.StepDuration, Easing = new CubicEaseOut() },
                       new DoubleTransition { Property = ScaleTransform.ScaleYProperty, Duration = Motion.StepDuration, Easing = new CubicEaseOut() }]
                    : null;
                st.ScaleX = lift;
                st.ScaleY = lift;
            }
            InvalidateVisual();
        }, Avalonia.Threading.DispatcherPriority.Loaded);
    }

    private void SetWidth(bool animate)
    {
        var target = _open ? OpenWidth : SpineWidth;
        var repeat = NavRepeatTracker.Current?.IsActive == true;
        var d = !animate || Motion.Current != EffectiveMotion.Full ? TimeSpan.Zero : repeat ? Motion.GlideRepeatDuration : Motion.GlideDuration;
        Transitions = d == TimeSpan.Zero ? null :
            [new DoubleTransition { Property = WidthProperty, Duration = d, Easing = repeat ? new LinearEasing() : new CubicEaseOut() }];
        Width = target;
    }

    private void EnsureText()
    {
        var key = (_system?.Id, FrontWidth, SpineWidth, _system?.GameCount ?? -1, Opens ? 0 : Math.Round(Bounds.Height));
        if (_textKey == key && _code != null) return;
        _textKey = key;
        if (_system is not { } s) return;
        // the quiet "Noch leer" spines: 16 in Text 2 (M5 QA: ≈ 11 px in Text 3 was unreadable from the couch)
        var spineSize = Opens ? Math.Clamp(Math.Round(SpineWidth * 0.30), 15, 28) : 16;
        _code = new FormattedText(s.ShortName, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, FontRoles.SpineFace, spineSize, Opens ? Paper : Text2);
        _codeShadow = Opens ? new FormattedText(s.ShortName, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, FontRoles.SpineFace, spineSize,
            Craft.Dark(0.55)) : null;
        _codeLit = Opens ? null : new FormattedText(s.ShortName, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, FontRoles.SpineFace, spineSize, Paper);
        // the sleeve's label is printed in ink (a compact sleeve: 15)
        _codeInk = null;
        if (!Opens)
        {
            // a long code on a short (compact) sleeve steps down to 13 so the label holds it whole
            var size = Bounds.Height < 130 ? 15.0 : spineSize;
            var room = Bounds.Height - SleeveTop - 8 - 14 - 22;
            while (true)
            {
                _codeInk = new FormattedText(s.ShortName, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, FontRoles.SpineFace, size,
                    Craft.Solid(Craft.LabelInk));
                if (_codeInk.Width <= room || size <= 13) break;
                size--;
            }
        }
        var countSize = SpineWidth >= 96 ? Math.Min(28, Math.Round(SpineWidth * 0.22)) : 15;
        _count = new FormattedText(s.GameCount.ToString(CultureInfo.InvariantCulture), CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
            FontRoles.NumberFace, countSize, SpineWidth >= 96 ? Paper : Text2);
        // the front: the name in the poster role, measured (largest size whose words all fit, at most 3 lines; else the
        // code), the count bottom left beside the motif. A big front (Konsolen) adds maker and year under the name.
        var inset = FrontInset;
        _name = FitName(s, FrontWidth - 2 * inset);
        _sub = LargeFront && !string.IsNullOrEmpty(s.SubTitle)
            ? new FormattedText(s.SubTitle, Localization.Loc.Culture, FlowDirection.LeftToRight, FontRoles.ReadFace, FrontWidth >= 480 ? 18 : 16, Text2)
            { MaxTextWidth = FrontWidth - 2 * inset }
            : null;
        _frontCount = new FormattedText(s.CountText, Localization.Loc.Culture, FlowDirection.LeftToRight, FontRoles.NumberFace,
            LargeFront ? (FrontWidth >= 480 ? 40 : 32) : 15, Paper);
    }

    private double FrontInset => LargeFront ? (FrontWidth >= 480 ? 40 : 32) : Math.Round(FrontWidth * 0.08);

    /// <summary>
    /// The system's name at the largest size (big fronts 44/36/30 or 36/30/26, small fronts 22/19/16 scaled with the
    /// front) that breaks only between words into ≤ 3 lines; else the code.
    /// </summary>
    private FormattedText FitName(SystemItemViewModel s, double maxWidth)
    {
        var k = FrontWidth / 200;
        double[] sizes = LargeFront
            ? FrontWidth >= 480 ? [44, 36, 30] : [36, 30, 26]
            : new[] { 22, 19, 16 }.Select(v => Math.Max(15, Math.Round(v * k))).ToArray();
        foreach (var size in sizes)
        {
            var words = s.PosterName.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            var fits = words.All(w => new FormattedText(w, Localization.Loc.Culture, FlowDirection.LeftToRight, FontRoles.PosterFace, size, Paper).Width <= maxWidth);
            if (!fits) continue;
            var t = new FormattedText(s.PosterName, Localization.Loc.Culture, FlowDirection.LeftToRight, FontRoles.PosterFace, size, Paper)
            {
                MaxTextWidth = maxWidth,
                LineHeight = Math.Round(size * 1.1),
            };
            if (t.Height <= 3 * Math.Round(size * 1.1) + 1) return t;
        }
        return new FormattedText(s.ShortName, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, FontRoles.SpineFace, Math.Round(28 * k), Paper);
    }

    public override void Render(DrawingContext ctx)
    {
        var r = new Rect(Bounds.Size);
        if (r.Width <= 0 || r.Height <= 0 || _system is not { } s) return;
        EnsureText();
        var look = s.Look;
        if (!Opens)
        {
            DrawSleeve(ctx, look);
            if (_ringOpacity > 0)
                using (ctx.PushOpacity(_ringOpacity))
                    Chamfer.DrawStandingRing(ctx, ObjectRect, look.LightBrush);
            return;
        }
        var t = Openness; // 0 = spine, 1 = front
        var area = ObjectRect;
        // depth: the right side face in the gap to the next spine and the top face above (we look slightly down onto the
        // shelf), the same oblique view as the sleeves; the front shows its faces too
        DrawBoxFaces(ctx, area, look);
        using (ctx.PushClip(area))
        {
            if (t < 1) DrawSpine(ctx, area, look, 1 - Math.Clamp(t / 0.5, 0, 1));
            if (t > 0)
            {
                var front = new Rect(area.X, 0, FrontWidth, r.Height);
                var shelf = HasShelf;
                if (shelf) LayoutFront(r.Height);
                using (ctx.PushOpacity(Math.Clamp(t / 0.5, 0, 1)))
                {
                    SystemWall.Draw(ctx, this, front, look, band: 0, motifShare: shelf ? 0 : 0.72, motifInset: Math.Round(FrontWidth * 0.06));
                    if (shelf) DrawRecess(ctx, front, look);
                    DrawFrontLip(ctx, front, look);
                    DrawPrintedBand(ctx, front, look, 1, LargeFront ? 8 : 6, LargeFront ? 12 : 9);
                    if (shelf)
                    {
                        // the inner board across the recess, the contact shadows of the boxes on it, the motif below
                        ShelfBoard.DrawOnWall(ctx, new Rect(front.X + RecessInset, _boardY, FrontWidth - 2 * RecessInset, 10), look, this);
                        for (var i = 0; i < _covers.Count && i < _slots.Length; i++)
                        {
                            if (!_covers[i].IsVisible) continue;
                            var art = _covers[i].ArtRect;
                            var span = art.Width > 0 ? new Rect(_slots[i].X + art.X, 0, art.Width, 0) : _slots[i];
                            ctx.FillRectangle(ShelfBoard.ContactBrush, new Rect(front.X + span.X, _boardY + 1, span.Width, 9));
                        }
                        MotifCatalog.Draw(ctx, look, _motifBox.Translate(new Vector(front.X, 0)), relief: true);
                    }
                }
                using (ctx.PushOpacity(Math.Clamp((t - 0.6) / 0.4, 0, 1)))
                using (ctx.PushTransform(Matrix.CreateTranslation(area.X, 0)))
                    DrawFrontText(ctx, new Rect(0, 0, FrontWidth, r.Height));
            }
        }
        // the chamfered ring in the system's light: 3 px room-deep gap + 3 px light, chamfer 6; it stands on the board
        // like the ring of a box (open at the foot, M3 QA)
        if (_ringOpacity > 0)
            using (ctx.PushOpacity(_ringOpacity))
                Chamfer.DrawStandingRing(ctx, area, look.LightBrush);
    }

    // ===================== drawn parts of spine and front (M7 "gezeichnet") =====================

    /// <summary>Inset of the inner compartment from the sides of a big front.</summary>
    private const double RecessInset = 20;

    /// <summary>
    /// The inner compartment of a big front, cut into it: a darker back wall, a 6 px shadow under its top edge and 3 px
    /// side faces, so the boxes stand IN the front instead of on a line across a panel; each box casts its shadow on
    /// the back wall.
    /// </summary>
    private void DrawRecess(DrawingContext ctx, Rect front, SystemLook look)
    {
        var r = new Rect(front.X + RecessInset, _recessTop, FrontWidth - 2 * RecessInset, Math.Max(0, _boardY - _recessTop));
        if (r.Height < 20) return;
        ctx.FillRectangle(Craft.Solid(ColorUtil.Mix(look.WallHi, Colors.Black, 0.22)), r);
        SystemWall.DrawTextures(ctx, this, r, look);
        ctx.FillRectangle(Craft.Fade(Colors.Black, 0.50, 0, Craft.Toward.Down), new Rect(r.X, r.Y, r.Width, 8));
        ctx.FillRectangle(Craft.Fade(Colors.Black, 0.40, 0, Craft.Toward.Right), new Rect(r.X, r.Y, 4, r.Height));
        ctx.FillRectangle(Craft.Fade(Colors.Black, 0, 0.30, Craft.Toward.Right), new Rect(r.Right - 4, r.Y, 4, r.Height));
        // the cut edge of the front around the opening: lit along the bottom lip, dark along the top
        ctx.FillRectangle(Craft.Dark(0.45), new Rect(r.X, r.Y, r.Width, 1));
        ctx.FillRectangle(Craft.Hi(0.07), new Rect(r.X - 1, r.Y - 1, r.Width + 2, 1));
        for (var i = 0; i < _covers.Count && i < _slots.Length; i++)
        {
            if (!_covers[i].IsVisible) continue;
            var art = _covers[i].ArtRect;
            var box = art.Width > 0 ? new Rect(_slots[i].X + art.X, _slots[i].Y + art.Y, art.Width, art.Height) : _slots[i];
            Craft.CastShadow(ctx, box.Translate(new Vector(front.X, 0)), 9, 0.34);
        }
    }

    /// <summary>Depth of a spine's side face (in the gap to its neighbour) and top face.</summary>
    private const double BoxSide = 4, BoxTop = 3;

    /// <summary>
    /// The side face (right, in the gap) and the top face (above) of a standing spine: the side in the wall tone
    /// darkened by 45 %, the top lifted by 12 % paper, both leaning back to the right.
    /// </summary>
    private static void DrawBoxFaces(DrawingContext ctx, Rect r, SystemLook look)
    {
        var side = new StreamGeometry();
        using (var c = side.Open())
        {
            c.BeginFigure(new Point(r.Right, r.Y), true);
            c.LineTo(new Point(r.Right + BoxSide, r.Y - BoxTop));
            c.LineTo(new Point(r.Right + BoxSide, r.Bottom - BoxTop));
            c.LineTo(new Point(r.Right, r.Bottom));
            c.EndFigure(true);
        }
        ctx.DrawGeometry(Craft.Solid(ColorUtil.Mix(look.Wall, Colors.Black, 0.45)), null, side);
        var top = new StreamGeometry();
        using (var c = top.Open())
        {
            c.BeginFigure(new Point(r.X, r.Y), true);
            c.LineTo(new Point(r.X + BoxSide, r.Y - BoxTop));
            c.LineTo(new Point(r.Right + BoxSide, r.Y - BoxTop));
            c.LineTo(new Point(r.Right, r.Y));
            c.EndFigure(true);
        }
        ctx.DrawGeometry(Craft.Solid(ColorUtil.Mix(look.WallHi, Craft.Light, 0.12)), null, top);
    }

    /// <summary>The lip around a front: 6 px of bevel, lit on top and left, in shadow at the right.</summary>
    private static void DrawFrontLip(DrawingContext ctx, Rect front, SystemLook look)
    {
        var lip = front.Width >= 360 ? 6.0 : 4.0;
        ctx.FillRectangle(Craft.Fade(Craft.Light, 0.07, 0, Craft.Toward.Down), new Rect(front.X, front.Y, front.Width, lip));
        ctx.FillRectangle(Craft.Fade(Craft.Light, 0.06, 0, Craft.Toward.Right), new Rect(front.X, front.Y, lip, front.Height));
        ctx.FillRectangle(Craft.Fade(Colors.Black, 0, 0.30, Craft.Toward.Right), new Rect(front.Right - lip, front.Y, lip, front.Height));
        ctx.FillRectangle(Craft.Hi(0.12), new Rect(front.X, front.Y, front.Width, 1));
        ctx.FillRectangle(Craft.Hi(0.08), new Rect(front.X, front.Y, 1, front.Height));
        ctx.FillRectangle(Craft.Dark(0.40), new Rect(front.Right - 1, front.Y, 1, front.Height));
        ctx.FillRectangle(Craft.Fade(Colors.Black, 0, 0.22, Craft.Toward.Down), new Rect(front.X, front.Bottom - 10, front.Width, 10));
    }

    /// <summary>
    /// The colour band PRINTED on a spine or front instead of a flush top border: inset from the top and both sides,
    /// with a darker print edge under it and a hair of light above it.
    /// </summary>
    private static void DrawPrintedBand(DrawingContext ctx, Rect r, SystemLook look, double opacity, double inset, double height)
    {
        var band = new Rect(r.X + inset, r.Y + inset, Math.Max(0, r.Width - 2 * inset), height);
        if (band.Width <= 0) return;
        using (ctx.PushOpacity(opacity))
        {
            ctx.FillRectangle(look.ColorBrush, band);
            ctx.FillRectangle(Craft.Solid(ColorUtil.Mix(look.Color, Colors.Black, 0.32)), new Rect(band.X, band.Bottom, band.Width, 1));
            ctx.FillRectangle(Craft.Hi(0.16), new Rect(band.X, band.Y, band.Width, 1));
        }
    }

    private readonly Dictionary<(int, int, int), StreamGeometry> _spineGeo = new();

    /// <summary>A spine's outline: a rectangle with a 2 px chip off one top corner on about one in three (seeded).</summary>
    private Geometry SpineOutline(double w, double h, uint seed)
    {
        var chip = Craft.Rand(seed, 12) < 0.34 ? (Craft.Rand(seed, 13) < 0.5 ? 1 : 2) : 0;
        var key = ((int)w, (int)h, chip);
        if (_spineGeo.TryGetValue(key, out var g)) return g;
        g = new StreamGeometry();
        using (var c = g.Open())
        {
            c.BeginFigure(new Point(0, chip == 1 ? 2.5 : 0), true);
            if (chip == 1) c.LineTo(new Point(2.5, 0));
            c.LineTo(new Point(w - (chip == 2 ? 2.5 : 0), 0));
            if (chip == 2) c.LineTo(new Point(w, 2.5));
            c.LineTo(new Point(w, h));
            c.LineTo(new Point(0, h));
            c.EndFigure(true);
        }
        _spineGeo[key] = g;
        return g;
    }

    private void DrawSpine(DrawingContext ctx, Rect area, SystemLook look, double opacity)
    {
        using (ctx.PushOpacity(opacity))
        using (ctx.PushTransform(Matrix.CreateTranslation(area.X, area.Y)))
        {
            var w = SpineWidth;
            var r = new Rect(0, 0, area.Width, area.Height);
            var seed = Craft.Seed(_system?.Id);
            using (ctx.PushGeometryClip(SpineOutline(r.Width, r.Height, seed)))
            {
                // the Konsolen spine is a narrow slice of its wall (38 % Wand hell over 62 % Wand, the light edge between);
                // the small spines of Start stay one tone
                var split = ShowMotif ? Math.Round(r.Height * 0.38) : r.Height;
                ctx.FillRectangle(look.WallHiBrush, new Rect(0, 0, r.Width, split));
                if (split < r.Height) ctx.FillRectangle(look.WallBrush, new Rect(0, split, r.Width, r.Height - split));
                SystemWall.DrawTextures(ctx, this, r, look);
                if (split < r.Height) ctx.FillRectangle(LightEdge, new Rect(0, split, r.Width, 1));
                // a printed box spine wraps round its edges: light at the left, dark at the right
                ctx.FillRectangle(Craft.Curvature, r);
                // the colour band is printed on the spine (inset, with its print edge), not a flush top border
                var bandInset = w >= 96 ? 7 : 5;
                var bandHeight = w >= 96 ? 10 : 8;
                DrawPrintedBand(ctx, r, look, 1, bandInset, bandHeight);
                var codeTop = bandInset + bandHeight + (w >= 96 ? 14 : 12);
                // the code reads upward under the band (one anchor on Start and Konsolen, M5 QA); from the foot up: the
                // count on its stamped plate and the motif standing above it
                var code = _code;
                var codeEnd = code != null ? codeTop + code.Width : codeTop;
                var gap = w >= 96 ? 16 : 12;
                var foot = r.Height - gap;
                if (_count is { } count && _system?.GameCount > 0)
                {
                    var pw = Math.Round(Math.Max(count.Width + (w >= 96 ? 22 : 14), w >= 96 ? 44 : 34));
                    var ph = Math.Round(count.Height + (w >= 96 ? 6 : 4));
                    var plate = new Rect(Math.Round((w - pw) / 2), Math.Round(foot - ph + (w >= 96 ? 3 : 2)), pw, ph);
                    // a small stamped plate fixed to the spine: raised, lit on top, its shadow under it
                    using (ctx.PushTransform(Matrix.CreateTranslation(1, 2)))
                        ctx.DrawGeometry(Craft.Dark(0.40), null, Chamfer.Octagon(plate, 3));
                    ctx.DrawGeometry(Craft.Solid(ColorUtil.Mix(look.WallHi, Craft.Light, 0.07)), null, Chamfer.Octagon(plate, 3));
                    using (ctx.PushGeometryClip(Chamfer.Octagon(plate, 3)))
                    {
                        ctx.FillRectangle(Craft.Hi(0.16), new Rect(plate.X, plate.Y, plate.Width, 1));
                        ctx.FillRectangle(Craft.Fade(Colors.Black, 0, 0.30, Craft.Toward.Down), new Rect(plate.X, plate.Bottom - 5, plate.Width, 5));
                        ctx.FillRectangle(Craft.Dark(0.6), new Rect(plate.X + 3, plate.Y + Math.Round(ph / 2) - 1, 2, 2));
                        ctx.FillRectangle(Craft.Dark(0.6), new Rect(plate.Right - 5, plate.Y + Math.Round(ph / 2) - 1, 2, 2));
                    }
                    ctx.DrawText(count, new Point(Math.Round((w - count.Width) / 2), Math.Round(plate.Y + (ph - count.Height) / 2)));
                    foot = plate.Y - gap + (w >= 96 ? 4 : 2);
                }
                var motif = ShowMotif ? Math.Round(w - 2 * Math.Max(12, Math.Round(w * 0.14)))
                    // a Start spine of ≥ 200 shows the motif at 40 px – the same object at a smaller scale – where the code leaves room
                    : r.Height >= 200 && foot - 40 >= codeEnd + 8 ? 40 : 0;
                if (motif > 0)
                {
                    MotifCatalog.Draw(ctx, look, new Rect(Math.Round((w - motif) / 2), Math.Round(foot - motif), motif, motif), cutout: look.WallBrush,
                        relief: ShowMotif);
                    foot -= motif + gap + 4;
                }
                if (code != null)
                {
                    var x = Math.Round((w - code.Height) / 2);
                    // only a code longer than the room above the motif is cut (never on the catalog's codes); the code is
                    // embossed: a dark copy one pixel down and to the right
                    using (ctx.PushClip(new Rect(0, codeTop - 4, w, Math.Max(0, foot - codeTop + 4))))
                    using (ctx.PushTransform(Matrix.CreateRotation(-Math.PI / 2) * Matrix.CreateTranslation(x, Math.Round(codeEnd))))
                    {
                        if (_codeShadow is { } shadow) ctx.DrawText(shadow, new Point(-1, 1));
                        ctx.DrawText(code, new Point(0, 0));
                    }
                }
                // edges, grime at the foot, a faint scuff
                Craft.Edges(ctx, r, lit: 0.09, dark: 0.32, grime: 0.20, grimeHeight: 10, topLit: 0.10, softRight: 4);
                Craft.Scuffs(ctx, new Rect(0, codeTop, r.Width, r.Height * 0.5), seed, 1, 0.05);
            }
        }
    }

    // ===================== the empty sleeve ("Noch leer", DESIGN 7.4.2) =====================

    /// <summary>Depth of a sleeve's side face (right) and top face (above), the oblique view of the shelf.</summary>
    private const double SleeveSide = 5, SleeveTop = 3;

    /// <summary>The lift of a pulled sleeve (8 px at Pull 1; none when motion is reduced or off).</summary>
    private double SleeveLift => Motion.AllowsMovement ? Math.Round(8 * Pull, 1) : 0;

    private readonly record struct SleeveShape(Rect Front, uint Seed, double Lean, bool Dent, bool DentLeft, double DentSize);

    /// <summary>
    /// The sleeve of this system, seeded by its id: front 40–46 wide (of 48), 0–8 px shorter than the row, one in
    /// about five leaning on its left neighbour (never the first), every second with a dented top corner.
    /// </summary>
    private SleeveShape Sleeve
    {
        get
        {
            var seed = Craft.Seed(_system?.Id);
            var H = Bounds.Height;
            var fw = Math.Max(16, SpineWidth - 8 + Craft.Pick(seed, 1, 0, 6));
            var h = Math.Max(24, H - SleeveTop - Craft.Pick(seed, 2, 0, 8));
            var lean = 0.0;
            if (Craft.Rand(seed, 3) < 0.24 && IndexInRow() > 0)
                lean = -Math.Asin(Math.Min(0.2, 7 / h)); // the top corner comes to rest on the neighbour
            var dent = Craft.Rand(seed, 4) < 0.5;
            return new SleeveShape(new Rect(0, H - h, fw, h), seed, lean, dent, Craft.Rand(seed, 5) < 0.5, Craft.Pick(seed, 6, 2, 4));
        }
    }

    private int IndexInRow()
    {
        if (Avalonia.VisualTree.VisualExtensions.GetVisualParent(this) is not Control container) return 0;
        return Avalonia.VisualTree.VisualExtensions.FindAncestorOfType<ItemsControl>(container) is { } items ? items.IndexFromContainer(container) : 0;
    }

    private static readonly Color CardBrown = Color.Parse("#6B5E4C");
    private readonly Dictionary<(int, int, bool, bool, int), StreamGeometry> _sleeveGeo = new();

    /// <summary>The front outline of a sleeve: a rectangle with one top corner dented (cut).</summary>
    private StreamGeometry SleeveFront(Rect f, SleeveShape s)
    {
        var key = ((int)f.Width, (int)f.Height, s.Dent, s.DentLeft, (int)s.DentSize);
        if (_sleeveGeo.TryGetValue(key, out var g)) return g;
        g = new StreamGeometry();
        using (var c = g.Open())
        {
            var d = s.Dent ? s.DentSize : 0;
            var dl = s.DentLeft ? d : 0;
            var dr = s.DentLeft ? 0 : d;
            c.BeginFigure(new Point(0, dl), true);
            c.LineTo(new Point(dl, 0));
            c.LineTo(new Point(f.Width - dr, 0));
            c.LineTo(new Point(f.Width, dr * 0.7));
            c.LineTo(new Point(f.Width, f.Height));
            c.LineTo(new Point(0, f.Height));
            c.EndFigure(true);
        }
        _sleeveGeo[key] = g;
        return g;
    }

    /// <summary>
    /// An empty cardboard sleeve standing on the "Noch leer" edge (M7 "gezeichnet", replaces the flat 48 × 148 swatch):
    /// card-brown front in the system's housing with fibres, a side face and a top face with the open mouth, lit left and
    /// dark right edges, grime at the foot, scuffs and a dented corner; on it a hand-applied paper label with a stripe in
    /// the system colour and the code printed in ink. It casts its shadow on the wall behind. Focus pulls it out of the
    /// row (straightened, 8 px up, label in full colour).
    /// </summary>
    private void DrawSleeve(DrawingContext ctx, SystemLook look)
    {
        var s = Sleeve;
        var f = s.Front;
        var pull = Math.Clamp(Pull, 0, 1);
        var lift = SleeveLift;
        var lean = s.Lean * (1 - pull);
        var body = ColorUtil.Mix(look.WallHi, CardBrown, 0.30);
        var m = Matrix.CreateTranslation(f.X, f.Y - lift);
        if (Math.Abs(lean) > 0.0001)
            m = Matrix.CreateTranslation(0, -f.Height) * Matrix.CreateRotation(lean) * Matrix.CreateTranslation(f.X, f.Bottom - lift);
        // the shadow on the wall behind it (never rotated: it falls on the wall)
        Craft.CastShadow(ctx, new Rect(f.X, f.Y - SleeveTop - lift, f.Width + SleeveSide, f.Height + SleeveTop), 10, 0.38);
        using (ctx.PushTransform(m))
        {
            var w = f.Width;
            var h = f.Height;
            // side face (right) and top face (above), leaning back up and to the right
            var side = new StreamGeometry();
            using (var c = side.Open())
            {
                c.BeginFigure(new Point(w, s.Dent && !s.DentLeft ? s.DentSize * 0.7 : 0), true);
                c.LineTo(new Point(w + SleeveSide, -SleeveTop));
                c.LineTo(new Point(w + SleeveSide, h - SleeveTop));
                c.LineTo(new Point(w, h));
                c.EndFigure(true);
            }
            ctx.DrawGeometry(Craft.Solid(ColorUtil.Mix(body, Colors.Black, 0.40)), null, side);
            var dl = s.Dent && s.DentLeft ? s.DentSize : 0;
            var dr = s.Dent && !s.DentLeft ? s.DentSize : 0;
            var top = new StreamGeometry();
            using (var c = top.Open())
            {
                c.BeginFigure(new Point(dl, 0), true);
                c.LineTo(new Point(dl + SleeveSide, -SleeveTop));
                c.LineTo(new Point(w + SleeveSide, -SleeveTop));
                c.LineTo(new Point(w - dr, 0));
                c.EndFigure(true);
            }
            ctx.DrawGeometry(Craft.Solid(ColorUtil.Mix(body, Craft.Light, 0.14)), null, top);
            // the open mouth of the empty sleeve: a dark slot inside the top face
            ctx.FillRectangle(Craft.Solid(Craft.Deep), new Rect(dl + 3, -2, Math.Max(0, w - dl - dr - 3), 1.5));
            // the front: card, fibres, edges, grime, scuffs
            var outline = SleeveFront(f, s);
            ctx.DrawGeometry(Craft.Solid(body), null, outline);
            using (ctx.PushGeometryClip(outline))
            {
                var fr = new Rect(0, 0, w, h);
                TextureFill.Draw(ctx, this, fr, Texture.Card, 0.6);
                SystemWall.DrawTextures(ctx, this, fr, look, look.TextureOpacity * 0.5);
                ctx.FillRectangle(Craft.Curvature, fr);
                Craft.Edges(ctx, fr, lit: 0.10, dark: 0.35, grime: 0.24, grimeHeight: 8);
                Craft.Scuffs(ctx, fr, s.Seed, Craft.Pick(s.Seed, 7, 1, 2));
            }
            DrawSleeveLabel(ctx, look, s, pull);
        }
    }

    private void DrawSleeveLabel(DrawingContext ctx, SystemLook look, SleeveShape s, double pull)
    {
        if (_codeInk is not { } code) return;
        var f = s.Front;
        var lw = Math.Min(f.Width - 8, Math.Ceiling(code.Height) + 10);
        var y = 7 + Craft.Pick(s.Seed, 9, 0, 5);
        var lh = Math.Min(Math.Ceiling(code.Width) + 22, f.Height - y - 7);
        if (lw < 10 || lh < 20) return;
        var x = Math.Round((f.Width - lw) / 2 + Craft.Range(s.Seed, 8, -1.2, 1.2), 1);
        var tilt = Craft.Range(s.Seed, 10, -1.5, 1.5) * (1 - 0.6 * pull);
        var r = new Rect(x, y, lw, lh);
        // a quiet sleeve's label is paper in the shade of the row; the pulled one comes into the light
        var paper = ColorUtil.Mix(ColorUtil.Mix(Craft.LabelPaper, look.WallHi, 0.42), Craft.LabelPaper, pull);
        Craft.DrawPaper(ctx, this, r, s.Seed, paper, tilt, shadow: 0.35, fibres: 0.6);
        using (Craft.PushLabel(ctx, r, tilt))
        {
            // the stripe in the system colour, printed a hair off register
            using (ctx.PushOpacity(0.55 + 0.45 * pull))
                ctx.FillRectangle(look.ColorBrush, new Rect(2.5, 2, lw - 4.5, 4));
            using (ctx.PushClip(new Rect(0, 8, lw, lh - 10)))
            using (ctx.PushOpacity(0.78 + 0.22 * pull))
            using (ctx.PushTransform(Matrix.CreateRotation(-Math.PI / 2) * Matrix.CreateTranslation(Math.Round((lw - code.Height) / 2), Math.Round(8 + 6 + code.Width))))
                Craft.Print(ctx, code, new Point(0, 0));
        }
    }

    private void DrawFrontText(DrawingContext ctx, Rect front)
    {
        var inset = FrontInset;
        var y = 8 + Math.Round(LargeFront ? inset * 0.8 : inset * 0.9);
        if (_name is { } name)
        {
            ctx.DrawText(name, new Point(inset, y));
            y += name.Height + 8;
        }
        if (_sub is { } sub) ctx.DrawText(sub, new Point(inset, Math.Round(y)));
        if (_frontCount is { } count)
            ctx.DrawText(count, new Point(inset, Math.Round(front.Bottom - inset - count.Height + (LargeFront ? 6 : 0))));
    }
}
