using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using RetroHub.App.Services;
using RetroHub.App.Theming;

namespace RetroHub.App.Controls;

/// <summary>
/// Object "Schachtel" (DESIGN 1.2, 10.2): a game cover, drawn CONTAIN, bottom-aligned and horizontally centred –
/// never cropped; mixed box formats are intended. <see cref="ArtRect"/> is the rectangle actually covered, read by the
/// shelf board (contact shadow) and the <see cref="FocusLayer"/> (ring, cone). Loads <see cref="Source"/> lazily
/// through <see cref="AsyncImageLoader"/> (cancelled when the tile is recycled or detached). Without a cover it draws
/// the spine placeholder: a wall in the system's housing, the colour band as a spine with the system code, the motif
/// silhouette top right and the full title anchored bottom left (grid, shelves AND the stage boxes of Start and Detail).
/// Text layouts are built on change, never in <see cref="Render"/>; text is drawn with grayscale antialiasing (no
/// colour fringes over textured walls and rotated codes).
/// No focus visuals here: covers clip.
/// </summary>
public sealed class CoverArt : Control
{
    public static readonly StyledProperty<string?> SourceProperty = AvaloniaProperty.Register<CoverArt, string?>(nameof(Source));
    public static readonly StyledProperty<string?> TitleProperty = AvaloniaProperty.Register<CoverArt, string?>(nameof(Title));
    /// <summary>System code shown on the placeholder spine ("GBA").</summary>
    public static readonly StyledProperty<string?> BadgeProperty = AvaloniaProperty.Register<CoverArt, string?>(nameof(Badge));
    public static readonly StyledProperty<string?> SystemIdProperty = AvaloniaProperty.Register<CoverArt, string?>(nameof(SystemId));
    /// <summary>Catalog accent: only used for systems without a palette entry (<see cref="SystemPalette.From"/>).</summary>
    public static readonly StyledProperty<Color> AccentProperty = AvaloniaProperty.Register<CoverArt, Color>(nameof(Accent), Color.Parse("#948A7A"));
    /// <summary>False = a placeholder without title (band, code and motif only). Since the M1 QA the stages keep the title.</summary>
    public static readonly StyledProperty<bool> ShowTitleProperty = AvaloniaProperty.Register<CoverArt, bool>(nameof(ShowTitle), true);
    /// <summary>Legacy (v0): badge pills are gone (DESIGN 10.2).</summary>
    public static readonly StyledProperty<bool> ShowBadgeProperty = AvaloniaProperty.Register<CoverArt, bool>(nameof(ShowBadge), true);
    /// <summary>Fill the slot (UniformToFill) instead of contain – only for small list thumbnails.</summary>
    public static readonly StyledProperty<bool> FillProperty = AvaloniaProperty.Register<CoverArt, bool>(nameof(Fill));
    /// <summary>Favourite marker: star 18 px in favourite yellow on a room-deep square, top left of the box.</summary>
    public static readonly StyledProperty<bool> IsFavoriteProperty = AvaloniaProperty.Register<CoverArt, bool>(nameof(IsFavorite));
    /// <summary>
    /// False = no favourite marker on this box (Start: shelves and stage carry no badges, DESIGN 10.2; the favourite
    /// state is the filled star button of the stage).
    /// </summary>
    public static readonly StyledProperty<bool> ShowFavoriteProperty = AvaloniaProperty.Register<CoverArt, bool>(nameof(ShowFavorite), true);
    /// <summary>Status "Datei fehlt": cover dimmed to 0.45 plus the Missing icon top right (dimming = status only).</summary>
    public static readonly StyledProperty<bool> IsMissingProperty = AvaloniaProperty.Register<CoverArt, bool>(nameof(IsMissing));

    /// <summary>Width/height of a placeholder box of an unknown system (DESIGN 10.2: 0.72, portrait).</summary>
    public const double PlaceholderAspect = 0.72;

    /// <summary>
    /// Width/height of the placeholder of system <paramref name="systemId"/>: the system's typical box format
    /// (<see cref="SystemPalette.BoxAspect"/>), so a spine placeholder stands as tall as its neighbours with covers.
    /// </summary>
    public static double PlaceholderAspectFor(string? systemId) =>
        SystemPalette.IsKnown(systemId) ? 1 / SystemPalette.BoxAspect(systemId) : PlaceholderAspect;

    private static readonly IImmutableSolidColorBrush PaperBrush = new ImmutableSolidColorBrush(SystemPalette.Paper);
    private static readonly IImmutableSolidColorBrush LightEdge = new ImmutableSolidColorBrush(Color.Parse("#17FFF3E0"));

    private Bitmap? _bitmap;
    private CancellationTokenSource? _cts;
    private int _requestedWidth;
    private Rect _artRect;
    // placeholder text cache (built on change, never in Render)
    private FormattedText? _titleText;
    private FormattedText? _subText;
    private FormattedText? _codeText;
    private (string? Title, string? Code, string? System, int W, int H) _textKey;

    static CoverArt()
    {
        AffectsRender<CoverArt>(TitleProperty, BadgeProperty, AccentProperty, SystemIdProperty, FillProperty, IsFavoriteProperty, IsMissingProperty,
            ShowTitleProperty, ShowFavoriteProperty);
        ClipToBoundsProperty.OverrideDefaultValue<CoverArt>(true);
    }

    public CoverArt() => TextOptions.SetTextRenderingMode(this, TextRenderingMode.Antialias);

    public string? Source { get => GetValue(SourceProperty); set => SetValue(SourceProperty, value); }
    public string? Title { get => GetValue(TitleProperty); set => SetValue(TitleProperty, value); }
    public string? Badge { get => GetValue(BadgeProperty); set => SetValue(BadgeProperty, value); }
    public string? SystemId { get => GetValue(SystemIdProperty); set => SetValue(SystemIdProperty, value); }
    public Color Accent { get => GetValue(AccentProperty); set => SetValue(AccentProperty, value); }
    public bool ShowTitle { get => GetValue(ShowTitleProperty); set => SetValue(ShowTitleProperty, value); }
    public bool ShowBadge { get => GetValue(ShowBadgeProperty); set => SetValue(ShowBadgeProperty, value); }
    public bool Fill { get => GetValue(FillProperty); set => SetValue(FillProperty, value); }
    public bool IsFavorite { get => GetValue(IsFavoriteProperty); set => SetValue(IsFavoriteProperty, value); }
    public bool IsMissing { get => GetValue(IsMissingProperty); set => SetValue(IsMissingProperty, value); }
    public bool ShowFavorite { get => GetValue(ShowFavoriteProperty); set => SetValue(ShowFavoriteProperty, value); }

    /// <summary>True when a real cover bitmap is shown (tests / diagnostics).</summary>
    public bool HasImage => _bitmap != null;

    /// <summary>Width / height of the loaded cover image; null while none is shown.</summary>
    public double? ImageAspect => _bitmap is { Size: { Width: > 0, Height: > 0 } s } ? s.Width / s.Height : null;

    /// <summary>The covered rectangle in local coordinates (contain, bottom-aligned, centred).</summary>
    public Rect ArtRect => _artRect;

    /// <summary>Raised when <see cref="ArtRect"/> changed (image loaded, resized).</summary>
    public event EventHandler? ArtRectChanged;

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == SourceProperty)
        {
            _bitmap = null;
            _requestedWidth = 0;
            UpdateArtRect();
            InvalidateVisual();
            RequestImage();
        }
        else if (change.Property == BoundsProperty)
        {
            UpdateArtRect();
            var wanted = AsyncImageLoader.Bucket(Bounds.Width * Scaling);
            if (Source != null && wanted > _requestedWidth) RequestImage();
        }
        else if (change.Property == FillProperty || change.Property == SystemIdProperty) UpdateArtRect();
        if (change.Property == TitleProperty || change.Property == BadgeProperty || change.Property == SystemIdProperty
            || change.Property == BoundsProperty || change.Property == SourceProperty || change.Property == ShowTitleProperty)
            _textKey = default; // rebuilt lazily on the next placeholder layout (see EnsureText)
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        RequestImage();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        Cancel();
    }

    /// <summary>Device-pixel factor (render scaling × UI scale), so decodes match the real on-screen size.</summary>
    private double Scaling
    {
        get
        {
            var top = TopLevel.GetTopLevel(this);
            if (top == null) return 1.0;
            var m = this.TransformToVisual(top);
            var ui = m is { } mm ? Math.Sqrt(mm.M11 * mm.M11 + mm.M12 * mm.M12) : 1.0;
            return top.RenderScaling * (ui > 0 ? ui : 1.0);
        }
    }

    private void Cancel()
    {
        _cts?.Cancel();
        _cts?.Dispose();
        _cts = null;
    }

    private void RequestImage()
    {
        Cancel();
        var source = Source;
        if (string.IsNullOrEmpty(source) || TopLevel.GetTopLevel(this) == null || Bounds.Width <= 0) return;
        var width = AsyncImageLoader.Bucket(Bounds.Width * Scaling);
        _requestedWidth = width;
        var cached = AsyncImageLoader.Shared.TryGetCached(source, width);
        if (cached != null)
        {
            SetBitmap(cached);
            return;
        }
        var cts = new CancellationTokenSource();
        _cts = cts;
        _ = LoadAsync(source, width, cts.Token);
    }

    private async Task LoadAsync(string source, int width, CancellationToken ct)
    {
        Bitmap? bitmap;
        try { bitmap = await AsyncImageLoader.Shared.LoadAsync(source, width, ct).ConfigureAwait(false); }
        catch (OperationCanceledException) { return; }
        if (ct.IsCancellationRequested || bitmap == null) return;
        Dispatcher.UIThread.Post(() =>
        {
            if (ct.IsCancellationRequested || Source != source) return;
            SetBitmap(bitmap);
        }, DispatcherPriority.Background);
    }

    private void SetBitmap(Bitmap bitmap)
    {
        _bitmap = bitmap;
        UpdateArtRect();
        InvalidateVisual();
    }

    private void UpdateArtRect()
    {
        var r = Compute(new Rect(Bounds.Size), _bitmap?.Size, Fill, PlaceholderAspectFor(SystemId));
        if (r == _artRect) return;
        _artRect = r;
        ArtRectChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Contain + bottom-aligned + centred (image aspect, or the placeholder's aspect).</summary>
    public static Rect Compute(Rect slot, Size? image, bool fill = false, double placeholderAspect = PlaceholderAspect)
    {
        if (slot.Width <= 0 || slot.Height <= 0) return default;
        if (fill) return slot;
        var aspect = image is { Width: > 0, Height: > 0 } s ? s.Width / s.Height : placeholderAspect;
        var w = slot.Width;
        var h = w / aspect;
        if (h > slot.Height)
        {
            h = slot.Height;
            w = h * aspect;
        }
        w = Math.Round(w);
        h = Math.Round(h);
        return new Rect(slot.X + Math.Round((slot.Width - w) / 2), slot.Bottom - h, w, h);
    }

    public override void Render(DrawingContext ctx)
    {
        var art = _artRect;
        if (art.Width <= 0 || art.Height <= 0) return;
        if (_bitmap is { } bmp)
        {
            var src = new Rect(bmp.Size);
            if (Fill)
            {
                var scale = Math.Max(art.Width / src.Width, art.Height / src.Height);
                var w = art.Width / scale;
                var h = art.Height / scale;
                src = new Rect((src.Width - w) / 2, (src.Height - h) / 2, w, h);
            }
            using (ctx.PushOpacity(IsMissing ? 0.45 : 1))
            using (ctx.PushClip(new RoundedRect(art, 2)))
                ctx.DrawImage(bmp, src, art);
        }
        else
        {
            using (ctx.PushOpacity(IsMissing ? 0.45 : 1))
                DrawPlaceholder(ctx, art);
        }
        DrawMarkers(ctx, art);
    }

    private static readonly IImmutableSolidColorBrush MarkerGround = new ImmutableSolidColorBrush(Color.Parse("#0A0908"));
    private static readonly IImmutableSolidColorBrush FavoriteBrush = new ImmutableSolidColorBrush(Color.Parse("#FFD36A"));
    private static readonly IImmutableSolidColorBrush MissingBrush = new ImmutableSolidColorBrush(Color.Parse("#FF6A5E"));
    private static readonly Geometry StarGeometry = Geometry.Parse("M12,3 L14.6,8.6 L20.6,9.2 L16,13.2 L17.4,19.2 L12,16 L6.6,19.2 L8,13.2 L3.4,9.2 L9.4,8.6 Z");
    private static readonly Geometry MissingGeometry = Geometry.Parse("M6,3 H14 L19,8 V20 L18,21 H6 L5,20 V4 Z M3,3 L21,21");

    /// <summary>Status markers on the box (DESIGN 7.2.1): favourite top left, missing top right. No system badges.</summary>
    private void DrawMarkers(DrawingContext ctx, Rect art)
    {
        if (art.Width < 60) return;
        if (IsFavorite && ShowFavorite)
        {
            // on a placeholder the marker sits top left beside the spine band (the title owns the bottom)
            var placeholder = _bitmap == null && !Fill;
            var left = placeholder ? BandWidth(art.Width) + 6 : 6;
            var box = new Rect(art.X + left, art.Y + 6, 26, 26);
            ctx.DrawRectangle(MarkerGround, null, new RoundedRect(box, 2));
            using (ctx.PushTransform(Matrix.CreateScale(0.75, 0.75) * Matrix.CreateTranslation(box.X + 4, box.Y + 4)))
                ctx.DrawGeometry(FavoriteBrush, null, StarGeometry);
        }
        if (IsMissing)
        {
            var box = new Rect(art.Right - 34, art.Y + 6, 28, 28);
            ctx.DrawRectangle(MarkerGround, null, new RoundedRect(box, 2));
            using (ctx.PushTransform(Matrix.CreateScale(20 / 24.0, 20 / 24.0) * Matrix.CreateTranslation(box.X + 4, box.Y + 4)))
                ctx.DrawGeometry(null, new ImmutablePen(MissingBrush, 2.2, lineCap: PenLineCap.Square, lineJoin: PenLineJoin.Miter), MissingGeometry);
        }
    }

    /// <summary>Tiles narrower than this get one wall tone (a 38/62 split inside a small box reads as a grey card).</summary>
    public const double SplitMinWidth = 200;

    /// <summary>
    /// Size of the placeholder title (Archivo Expanded, DESIGN 10.2, M2 QA): 0.105 × min(width, 0.72 × height), at least
    /// 18 px, at most 26 px on shelf and grid boxes (lower than 300 px) and 40 px on stage boxes. A row of boxes of one
    /// height therefore sets all its titles in ONE size, whatever the box format (a narrow GameCube case beside a wide
    /// DS case); <see cref="PlaceholderTitle"/> steps it down only for a single word wider than the line.
    /// </summary>
    public static double PlaceholderTitleSize(double width, double height) =>
        Math.Clamp(Math.Round(0.105 * Math.Min(width, 0.72 * height)), 18, height >= 300 ? 40 : 26);

    /// <summary>Subtitle size (Atkinson Medium, Text 2): never below 15 px (DESIGN 3.2), 0.068 × the title's base.</summary>
    public static double PlaceholderSubSize(double width, double height) =>
        Math.Clamp(Math.Round(0.068 * Math.Min(width, 0.72 * height)), 15, 22);

    /// <summary>Inset of the placeholder motif from the right and bottom edge: 14 px at 196, less on small boxes.</summary>
    private static double MotifInset(double w) => Math.Clamp(Math.Round(w * 0.0714), 6, 14);

    /// <summary>Inner margin of the placeholder text: 14 px at 196, 24 px on the stage boxes.</summary>
    public static double PlaceholderPad(double w) => Math.Clamp(Math.Round(w * 0.0714), 10, 24);

    /// <summary>Spine band width: 16 % of the box width.</summary>
    private static double BandWidth(double w) => Math.Max(6, Math.Round(w * 0.16));

    /// <summary>Texture strength on placeholders, relative to the system's wall strength (boxes read as material).</summary>
    private const double PlaceholderTextureFactor = 2.0;

    private void DrawPlaceholder(DrawingContext ctx, Rect r)
    {
        var look = SystemPalette.Get(SystemId, Accent);
        EnsureText(r, look);
        var w = r.Width;
        using (ctx.PushClip(new RoundedRect(r, 2)))
        {
            // ground: one tone in small tiles, the wall's 38/62 split from 200 px; the system texture (shared brush,
            // offset per title so neighbours differ)
            var split = w >= SplitMinWidth ? Math.Round(r.Height * 0.38) : 0;
            if (split > 0) ctx.FillRectangle(look.WallHiBrush, new Rect(r.X, r.Y, w, split));
            ctx.FillRectangle(split > 0 ? look.WallBrush : look.WallHiBrush, new Rect(r.X, r.Y + split, w, r.Height - split));
            if (TextureBrushes.Get(look.Texture) is { } tex)
            {
                var hash = Hash(Title ?? "");
                var ox = hash % (uint)Math.Max(1, tex.Size.Width);
                var oy = (hash >> 8) % (uint)Math.Max(1, tex.Size.Height);
                using (ctx.PushOpacity(Math.Min(0.3, look.TextureOpacity * PlaceholderTextureFactor)))
                using (ctx.PushTransform(Matrix.CreateTranslation(r.X - ox, r.Y - oy)))
                    ctx.FillRectangle(tex.Brush, new Rect(ox, oy, w, r.Height));
            }
            if (split > 0) ctx.FillRectangle(LightEdge, new Rect(r.X, r.Y + split, w, 1));
            // the motif silhouette (DESIGN 10.2): ONE geometry in the system colour at 18 %, 45 % of the height, anchored
            // bottom right with a 14 px inset – always inside the lower 62 % (never across the 38/62 split)
            var band = BandWidth(w);
            var pad = PlaceholderPad(w);
            var inset = MotifInset(w);
            var room = split > 0 ? r.Height - split - inset - 2 : r.Height - 2 * inset;
            var motif = Math.Round(Math.Min(Math.Min(r.Height * 0.45, (w - band) * 0.8), room));
            if (motif > 8)
                MotifCatalog.DrawSilhouette(ctx, look, new Rect(r.Right - inset - motif, r.Bottom - inset - motif, motif, motif), 0.18);
            // spine band: full colour, code −90°
            ctx.FillRectangle(look.ColorBrush, new Rect(r.X, r.Y, band, r.Height));
            if (_codeText is { } code)
            {
                using (ctx.PushTransform(Matrix.CreateRotation(-Math.PI / 2) * Matrix.CreateTranslation(r.X + (band - code.Height) / 2, r.Y + w * 0.08 + code.Width)))
                    ctx.DrawText(code, new Point(0, 0));
            }
            ctx.FillRectangle(LightEdge, new Rect(r.X, r.Y, w, 1));
            // title and subtitle: left after the band, anchored at the bottom with the inner margin
            if (_titleText is { } title)
            {
                var x = r.X + band + pad;
                var bottom = r.Bottom - pad;
                if (_subText is { } sub)
                {
                    ctx.DrawText(sub, new Point(x, bottom - sub.Height));
                    bottom -= Math.Ceiling(sub.Height) + SubGap;
                }
                ctx.DrawText(title, new Point(x, bottom - title.Height));
            }
        }
    }

    private void EnsureText(Rect r, SystemLook look)
    {
        var key = (Title, Badge, SystemId, (int)r.Width, (int)r.Height);
        if (_textKey == key && (_titleText != null || !ShowTitle || string.IsNullOrEmpty(Title))) return;
        _textKey = key;
        var w = r.Width;
        var band = BandWidth(w);
        var pad = PlaceholderPad(w);
        // the title (DESIGN 10.2): bottom left after the band, in full – wrapped only at spaces and hyphens, the size
        // stepped down (× 0.88, never below 11 px) until every word and all lines fit; the subtitle in Atkinson below
        _titleText = null;
        _subText = null;
        if (ShowTitle && !string.IsNullOrEmpty(Title))
        {
            var maxWidth = Math.Max(1, w - band - pad - Math.Max(6, Math.Round(pad * 0.5)));
            var budget = Math.Max(1, r.Height * 0.62);
            var (main, subtitle) = SplitTitle(Title);
            // one size per box height (a whole row alike), ≤ 4 lines, then a character ellipsis; line height 1.1
            var fit = PlaceholderTitle.Fit(main, maxWidth, budget, PlaceholderTitleSize(w, r.Height), PlaceholderTitle.MaxLines);
            _titleText = new FormattedText(fit.Text, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, FontRoles.PosterFace, fit.Size, PaperBrush)
            {
                LineHeight = Math.Round(fit.Size * PlaceholderTitle.LineFactor),
            };
            if (subtitle != null)
            {
                var subSize = PlaceholderSubSize(w, r.Height);
                var subLine = Math.Round(subSize * 1.2);
                var room = budget - Math.Ceiling(_titleText.Height) - SubGap;
                var lines = (int)Math.Min(w >= 300 ? 3 : 2, Math.Floor(room / subLine));
                if (lines >= 1)
                    _subText = new FormattedText(FitText.Typeset(subtitle), CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, FontRoles.ReadMediumFace, subSize, SubBrush)
                    {
                        MaxTextWidth = maxWidth,
                        MaxLineCount = lines,
                        LineHeight = subLine,
                        Trimming = TextTrimming.WordEllipsis,
                    };
            }
        }
        var codeSize = Math.Min(28, Math.Max(7, w * 0.09));
        _codeText = string.IsNullOrEmpty(Badge) ? null :
            new FormattedText(Badge, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, FontRoles.SpineFace, codeSize, look.TextOnColorBrush)
            {
                MaxTextWidth = Math.Max(1, r.Height - w * 0.16),
                MaxLineCount = 1,
                Trimming = TextTrimming.CharacterEllipsis,
            };
    }

    private const double SubGap = 4;
    private static readonly IImmutableSolidColorBrush SubBrush = new ImmutableSolidColorBrush(Color.Parse("#C9BFAF"));

    /// <summary>Main title and subtitle at the first " - ", " – " or ": " (both parts non-empty); else the whole title.</summary>
    public static (string Main, string? Sub) SplitTitle(string title)
    {
        foreach (var sep in new[] { " - ", " – ", ": " })
        {
            var i = title.IndexOf(sep, StringComparison.Ordinal);
            if (i < 2) continue;
            var main = title[..i].Trim();
            var sub = title[(i + sep.Length)..].Trim();
            if (main.Length > 0 && sub.Length > 0) return (main, sub);
        }
        return (title, null);
    }

    private static uint Hash(string s)
    {
        var h = 2166136261u;
        foreach (var c in s) { h ^= c; h *= 16777619u; }
        return h;
    }
}

/// <summary>
/// Line breaking of placeholder titles (DESIGN 10.2, M2 QA): breaks ONLY at spaces and after hyphens, never inside a
/// word. The size stays the given one (one size for a row of boxes); it steps down (× 0.88, ≥ <see cref="MinSize"/>)
/// only while a single word is wider than the line. More lines than allowed (count, or the height budget): the last
/// line takes the rest of the title and ends in a character ellipsis. Results are cached per (text, width, height,
/// size) – the grid recycles tiles while browsing.
/// </summary>
public static class PlaceholderTitle
{
    public const double MinSize = 13;
    public const int MaxLines = 4;
    public const double LineFactor = 1.1;

    public readonly record struct Result(string Text, double Size, int Lines, bool Trimmed);

    private static readonly Dictionary<(string, int, int, int, int), Result> Cache = new();

    public static Result Fit(string text, double maxWidth, double maxHeight, double startSize, int maxLines)
    {
        var key = (text, (int)maxWidth, (int)maxHeight, (int)Math.Round(startSize * 10), maxLines);
        lock (Cache)
            if (Cache.TryGetValue(key, out var hit)) return hit;
        var result = Compute(text, maxWidth, maxHeight, startSize, maxLines);
        lock (Cache)
        {
            if (Cache.Count > 4096) Cache.Clear();
            Cache[key] = result;
        }
        return result;
    }

    private static Result Compute(string text, double maxWidth, double maxHeight, double startSize, int maxLines)
    {
        var tokens = Tokens(text);
        var size = Math.Max(MinSize, startSize);
        List<string>? lines;
        while ((lines = Wrap(tokens, size, maxWidth)) == null && size > MinSize)
            size = Math.Max(MinSize, Math.Floor(size * 0.88 * 2) / 2);
        lines ??= tokens.Select(t => t.Text).ToList(); // a word wider than the line even at the minimum: one per line
        maxLines = Math.Max(1, Math.Min(maxLines, (int)Math.Floor((maxHeight + 0.5) / Math.Round(size * LineFactor))));
        if (lines.Count <= maxLines) return new Result(string.Join('\n', lines), size, lines.Count, false);
        // too many lines: the last allowed line carries the rest, cut with a character ellipsis
        var rest = lines[maxLines - 1];
        for (var i = maxLines; i < lines.Count; i++) rest += (rest.EndsWith('-') ? "" : " ") + lines[i];
        static string Cut(string s) => s.TrimEnd(' ', '-', ',', ':', '.') + "…";
        while (rest.Length > 1 && Measure(Cut(rest), size) > maxWidth + 0.5) rest = rest[..^1];
        var kept = lines.Take(maxLines - 1).Append(Cut(rest));
        return new Result(string.Join('\n', kept), size, maxLines, true);
    }

    /// <summary>Words with their joiner: a space before it, or nothing (the part after a hyphen).</summary>
    private static List<(string Text, bool Space)> Tokens(string text)
    {
        var list = new List<(string, bool)>();
        foreach (var word in text.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            var first = true;
            var start = 0;
            for (var i = 0; i < word.Length; i++)
            {
                // a break after a hyphen inside a word ("Smaragd-|Edition"), never before a leading/trailing one
                if (word[i] != '-' || i == 0 || i == word.Length - 1) continue;
                list.Add((word[start..(i + 1)], first));
                first = false;
                start = i + 1;
            }
            list.Add((word[start..], first));
        }
        return list;
    }

    /// <summary>Greedy lines at <paramref name="size"/>; null when one token alone is wider than the line.</summary>
    private static List<string>? Wrap(List<(string Text, bool Space)> tokens, double size, double maxWidth)
    {
        var lines = new List<string>();
        var current = "";
        foreach (var (word, space) in tokens)
        {
            if (Measure(word, size) > maxWidth + 0.5) return null;
            if (current.Length == 0) { current = word; continue; }
            var candidate = space ? current + " " + word : current + word;
            if (Measure(candidate, size) <= maxWidth + 0.5) current = candidate;
            else
            {
                lines.Add(current);
                current = word;
            }
        }
        if (current.Length > 0) lines.Add(current);
        return lines;
    }

    private static double Measure(string s, double size) =>
        new FormattedText(s, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, FontRoles.PosterFace, size, null).Width;
}

internal static class VisualRootExtensions
{
    public static Visual? GetVisualRootSafe(this Visual v) => TopLevel.GetTopLevel(v);
}
