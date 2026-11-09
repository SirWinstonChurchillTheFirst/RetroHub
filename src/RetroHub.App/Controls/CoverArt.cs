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
/// the spine placeholder: a two-tone wall in the system's housing, the colour band as a spine with the system code,
/// the motif silhouette and the full title. Text layouts are built on change, never in <see cref="Render"/>.
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
    /// <summary>False = stage variant (Start, Detail): no title in the placeholder (the poster title stands beside it),
    /// motif at 60 % and the code in Rücken 28.</summary>
    public static readonly StyledProperty<bool> ShowTitleProperty = AvaloniaProperty.Register<CoverArt, bool>(nameof(ShowTitle), true);
    /// <summary>Legacy (v0): badge pills are gone (DESIGN 10.2).</summary>
    public static readonly StyledProperty<bool> ShowBadgeProperty = AvaloniaProperty.Register<CoverArt, bool>(nameof(ShowBadge), true);
    /// <summary>Fill the slot (UniformToFill) instead of contain – only for small list thumbnails.</summary>
    public static readonly StyledProperty<bool> FillProperty = AvaloniaProperty.Register<CoverArt, bool>(nameof(Fill));
    /// <summary>Favourite marker: star 18 px in favourite yellow on a room-deep square, top left of the box.</summary>
    public static readonly StyledProperty<bool> IsFavoriteProperty = AvaloniaProperty.Register<CoverArt, bool>(nameof(IsFavorite));
    /// <summary>Status "Datei fehlt": cover dimmed to 0.45 plus the Missing icon top right (dimming = status only).</summary>
    public static readonly StyledProperty<bool> IsMissingProperty = AvaloniaProperty.Register<CoverArt, bool>(nameof(IsMissing));

    /// <summary>Width/height of the placeholder box (DESIGN 10.2: 0.72, portrait).</summary>
    public const double PlaceholderAspect = 0.72;

    private static readonly IImmutableSolidColorBrush PaperBrush = new ImmutableSolidColorBrush(SystemPalette.Paper);
    private static readonly IImmutableSolidColorBrush LightEdge = new ImmutableSolidColorBrush(Color.Parse("#17FFF3E0"));

    private Bitmap? _bitmap;
    private CancellationTokenSource? _cts;
    private int _requestedWidth;
    private Rect _artRect;
    // placeholder text cache (built on change, never in Render)
    private FormattedText? _titleText;
    private FormattedText? _codeText;
    private (string? Title, string? Code, string? System, int W, int H) _textKey;

    static CoverArt()
    {
        AffectsRender<CoverArt>(TitleProperty, BadgeProperty, AccentProperty, SystemIdProperty, FillProperty, IsFavoriteProperty, IsMissingProperty,
            ShowTitleProperty);
        ClipToBoundsProperty.OverrideDefaultValue<CoverArt>(true);
    }

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

    /// <summary>True when a real cover bitmap is shown (tests / diagnostics).</summary>
    public bool HasImage => _bitmap != null;

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
        else if (change.Property == FillProperty) UpdateArtRect();
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
        var r = Compute(new Rect(Bounds.Size), _bitmap?.Size, Fill);
        if (r == _artRect) return;
        _artRect = r;
        ArtRectChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Contain + bottom-aligned + centred (image aspect, or the placeholder's 0.72).</summary>
    public static Rect Compute(Rect slot, Size? image, bool fill = false)
    {
        if (slot.Width <= 0 || slot.Height <= 0) return default;
        if (fill) return slot;
        var aspect = image is { Width: > 0, Height: > 0 } s ? s.Width / s.Height : PlaceholderAspect;
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
        if (IsFavorite)
        {
            // on a placeholder the marker sits bottom left beside the spine band (the title owns the top left)
            var placeholder = _bitmap == null && !Fill;
            var left = placeholder ? Math.Max(6, Math.Round(art.Width * 0.16)) + 6 : 6;
            var box = placeholder ? new Rect(art.X + left, art.Bottom - 32, 26, 26) : new Rect(art.X + left, art.Y + 6, 26, 26);
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

    /// <summary>Title size of the placeholder (Archivo Expanded): fixed per tile width, never shrunk per title.</summary>
    public static double PlaceholderTitleSize(double width) => Math.Clamp(Math.Round(width * 0.115), 18, 24);

    private void DrawPlaceholder(DrawingContext ctx, Rect r)
    {
        var look = SystemPalette.Get(SystemId, Accent);
        EnsureText(r, look);
        var w = r.Width;
        var stage = !ShowTitle;
        using (ctx.PushClip(new RoundedRect(r, 2)))
        {
            // ground: one tone in small tiles, the wall's 38/62 split from 200 px; texture 8 % (shared brush, offset
            // per title so neighbours differ)
            var split = w >= SplitMinWidth ? Math.Round(r.Height * 0.38) : 0;
            if (split > 0) ctx.FillRectangle(look.WallHiBrush, new Rect(r.X, r.Y, w, split));
            ctx.FillRectangle(split > 0 ? look.WallBrush : look.WallHiBrush, new Rect(r.X, r.Y + split, w, r.Height - split));
            if (TextureBrushes.Get(look.Texture) is { } tex)
            {
                var hash = Hash(Title ?? "");
                var ox = hash % (uint)Math.Max(1, tex.Size.Width);
                var oy = (hash >> 8) % (uint)Math.Max(1, tex.Size.Height);
                using (ctx.PushOpacity(0.08 / 0.07 * look.TextureOpacity))
                using (ctx.PushTransform(Matrix.CreateTranslation(r.X - ox, r.Y - oy)))
                    ctx.FillRectangle(tex.Brush, new Rect(ox, oy, w, r.Height));
            }
            // the motif (real category shape, details cut out in the ground tone) in colour at 18 %, standing flush
            // bottom right: 45 % of the height in the shelf, 60 % on the stages (no title there)
            var motif = Math.Round(r.Height * (stage ? 0.60 : 0.45));
            MotifCatalog.Draw(ctx, look, new Rect(r.Right - motif, r.Bottom - motif, motif, motif), 0.18, silhouette: true,
                cutout: split > 0 ? look.WallBrush : look.WallHiBrush, alignRight: true);
            // spine band: 16 % of the width, full colour, code −90°
            var band = Math.Max(6, Math.Round(w * 0.16));
            ctx.FillRectangle(look.ColorBrush, new Rect(r.X, r.Y, band, r.Height));
            if (_codeText is { } code)
            {
                using (ctx.PushTransform(Matrix.CreateRotation(-Math.PI / 2) * Matrix.CreateTranslation(r.X + (band - code.Height) / 2, r.Y + w * 0.08 + code.Width)))
                    ctx.DrawText(code, new Point(0, 0));
            }
            ctx.FillRectangle(LightEdge, new Rect(r.X, r.Y, w, 1));
            if (_titleText is { } title)
            {
                var pad = PlaceholderPad(w);
                ctx.DrawText(title, new Point(r.X + band + pad, r.Y + pad));
            }
        }
    }

    private static double PlaceholderPad(double w) => Math.Clamp(Math.Round(w * 0.082), 10, 16);

    private void EnsureText(Rect r, SystemLook look)
    {
        var key = (Title, Badge, SystemId, (int)r.Width, (int)r.Height);
        if (_textKey == key && (_titleText != null || !ShowTitle || string.IsNullOrEmpty(Title))) return;
        _textKey = key;
        var w = r.Width;
        var band = Math.Max(6, Math.Round(w * 0.16));
        var pad = PlaceholderPad(w);
        // the title (DESIGN 10.2): top left after the band, one size per tile width (never shrunk per title),
        // word wrap, at most 4 lines; the stage variants (Start, Detail) carry no title – the poster stands beside them
        _titleText = null;
        if (ShowTitle && !string.IsNullOrEmpty(Title))
        {
            var size = PlaceholderTitleSize(w);
            var maxWidth = Math.Max(1, w - band - pad - 6);
            // one size per tile width; only when a single word would break inside, two small steps down (never < 16)
            var longest = Title.Split([' ', '-', '/'], StringSplitOptions.RemoveEmptyEntries).OrderByDescending(s => s.Length).FirstOrDefault() ?? "";
            bool Overflows(double sz) => longest.Length > 0 &&
                new FormattedText(longest, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, FontRoles.PosterFace, sz, PaperBrush).Width > maxWidth;
            for (var step = 0; step < 2 && size - 2 >= 16 && Overflows(size); step++)
                size -= 2;
            // a word that still does not fit breaks inside (never an ellipsis in place of a whole first line)
            var trimming = Overflows(size) ? TextTrimming.None : TextTrimming.WordEllipsis;
            _titleText = new FormattedText(Title, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, FontRoles.PosterFace, size, PaperBrush)
            {
                MaxTextWidth = maxWidth,
                MaxTextHeight = Math.Max(1, r.Height * 0.62),
                MaxLineCount = 4,
                LineHeight = Math.Round(size * 1.1),
                Trimming = trimming,
            };
        }
        var codeSize = ShowTitle ? Math.Max(7, w * 0.09) : Math.Min(28, Math.Max(7, w * 0.11));
        _codeText = string.IsNullOrEmpty(Badge) ? null :
            new FormattedText(Badge, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, FontRoles.SpineFace, codeSize, look.TextOnColorBrush)
            {
                MaxTextWidth = Math.Max(1, r.Height - w * 0.16),
                MaxLineCount = 1,
                Trimming = TextTrimming.CharacterEllipsis,
            };
    }

    private static uint Hash(string s)
    {
        var h = 2166136261u;
        foreach (var c in s) { h ^= c; h *= 16777619u; }
        return h;
    }
}

internal static class VisualRootExtensions
{
    public static Visual? GetVisualRootSafe(this Visual v) => TopLevel.GetTopLevel(v);
}
