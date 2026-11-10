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
    private double _boardY;
    private Rect _motifBox;
    private Avalonia.Threading.DispatcherTimer? _dwell;
    private FormattedText? _code, _codeLit, _count, _name, _sub, _frontCount;
    private (string?, double, double, int) _textKey;
    private bool _open;
    private double _ringOpacity;

    static SystemSpine()
    {
        AffectsRender<SystemSpine>(BoundsProperty, ShowMotifProperty, SpineHeightProperty);
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

    /// <summary>Free space on each side of an open front (M2 QA): ring (3 + 3) and lift 1.03 never touch the neighbours.</summary>
    public const double FrontGap = 16;

    /// <summary>Width of the item while its front is turned forward: the front plus <see cref="FrontGap"/> on both sides.</summary>
    public double OpenWidth => FrontWidth + 2 * FrontGap;

    /// <summary>The front is (or is becoming) visible.</summary>
    public bool IsOpen => _open;

    /// <summary>A big front (Konsolen): poster name in 44/36, maker and year in the reading role, the count in Zahl 40.</summary>
    private bool LargeFront => FrontWidth >= 360;

    /// <summary>Share of the way from spine (0) to front (1), from the current (animating) width.</summary>
    private double Openness => Math.Clamp((Bounds.Width - SpineWidth) / Math.Max(1, OpenWidth - SpineWidth), 0, 1);

    /// <summary>The drawn object in local coordinates: the spine, or the front standing FrontGap in from both sides.</summary>
    public Rect ObjectRect
    {
        get
        {
            var r = new Rect(Bounds.Size);
            if (r.Width <= 0 || r.Height <= 0) return default;
            var t = Openness;
            var spineH = double.IsNaN(SpineHeight) ? r.Height : Math.Min(SpineHeight, r.Height);
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
            var lift = visible && Motion.AllowsMovement ? 1.03 : 1.0;
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
        var key = (_system?.Id, FrontWidth, SpineWidth, _system?.GameCount ?? -1);
        if (_textKey == key && _code != null) return;
        _textKey = key;
        if (_system is not { } s) return;
        // the quiet "Noch leer" spines: 16 in Text 2 (M5 QA: ≈ 11 px in Text 3 was unreadable from the couch)
        var spineSize = Opens ? Math.Clamp(Math.Round(SpineWidth * 0.30), 15, 28) : 16;
        _code = new FormattedText(s.ShortName, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, FontRoles.SpineFace, spineSize, Opens ? Paper : Text2);
        _codeLit = Opens ? null : new FormattedText(s.ShortName, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, FontRoles.SpineFace, spineSize, Paper);
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
        var t = Openness; // 0 = spine, 1 = front
        var area = ObjectRect;
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
                    SystemWall.Draw(ctx, this, front, look, band: 8, motifShare: shelf ? 0 : 0.72, motifInset: Math.Round(FrontWidth * 0.06));
                    if (shelf)
                    {
                        // the inner board across the front, the contact shadows of the boxes on it, the motif below
                        ShelfBoard.DrawOnWall(ctx, new Rect(front.X, _boardY, FrontWidth, 10), look);
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

    private void DrawSpine(DrawingContext ctx, Rect area, SystemLook look, double opacity)
    {
        using (ctx.PushOpacity(opacity))
        using (ctx.PushTransform(Matrix.CreateTranslation(area.X, area.Y)))
        {
            var w = SpineWidth;
            var r = new Rect(0, 0, area.Width, area.Height);
            var lit = Opens || _ringOpacity > 0.5;
            // the Konsolen spine is a narrow slice of its wall (38 % Wand hell over 62 % Wand, the light edge between);
            // the small spines of Start stay one tone
            var split = ShowMotif ? Math.Round(r.Height * 0.38) : r.Height;
            ctx.FillRectangle(look.WallHiBrush, new Rect(0, 0, r.Width, split));
            if (split < r.Height) ctx.FillRectangle(look.WallBrush, new Rect(0, split, r.Width, r.Height - split));
            SystemWall.DrawTextures(ctx, this, r, look);
            if (split < r.Height) ctx.FillRectangle(LightEdge, new Rect(0, split, r.Width, 1));
            // a spine without games is quiet through its band (50 %), not through unreadable text (M5 QA)
            using (ctx.PushOpacity(lit ? 1 : 0.5))
                ctx.FillRectangle(look.ColorBrush, new Rect(0, 0, r.Width, 8));
            ctx.FillRectangle(LightEdge, new Rect(0, 8, r.Width, 1));
            // the code hangs 16 px under the band and reads upward (one anchor on Start and Konsolen, M5 QA); from the
            // foot up: the count and the motif standing above it
            var code = lit ? _code : _codeLit ?? _code;
            var codeEnd = code != null ? CodeTop + code.Width : CodeTop;
            var gap = w >= 96 ? 16 : 12;
            var foot = r.Height - gap;
            if (_count is { } count && _system?.GameCount > 0)
            {
                ctx.DrawText(count, new Point(Math.Round((w - count.Width) / 2), Math.Round(foot - count.Height)));
                foot -= count.Height + gap;
            }
            var motif = ShowMotif ? Math.Round(w - 2 * Math.Max(12, Math.Round(w * 0.14)))
                // a Start spine of ≥ 200 shows the motif at 40 px – the same object at a smaller scale – where the code leaves room
                : Opens && r.Height >= 200 && foot - 40 >= codeEnd + 8 ? 40 : 0;
            if (motif > 0)
            {
                MotifCatalog.Draw(ctx, look, new Rect(Math.Round((w - motif) / 2), Math.Round(foot - motif), motif, motif), cutout: look.WallBrush);
                foot -= motif + gap + 4;
            }
            if (code != null)
            {
                var x = Math.Round((w - code.Height) / 2);
                // only a code longer than the room above the motif is cut (never on the catalog's codes)
                using (ctx.PushClip(new Rect(0, 12, w, Math.Max(0, foot - 12))))
                using (ctx.PushTransform(Matrix.CreateRotation(-Math.PI / 2) * Matrix.CreateTranslation(x, Math.Round(codeEnd))))
                    ctx.DrawText(code, new Point(0, 0));
            }
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
