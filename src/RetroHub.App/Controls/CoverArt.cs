using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using RetroHub.App.Services;
using RetroHub.App.Theming;
using RetroHub.App.ViewModels;

namespace RetroHub.App.Controls;

/// <summary>
/// A game cover. Loads <see cref="Source"/> lazily through <see cref="AsyncImageLoader"/> (cancelled when the tile is
/// recycled or detached); without a cover it draws generated placeholder art from the system accent + the title's
/// initials (deterministic per title, so every game keeps "its" look).
/// </summary>
public sealed class CoverArt : Control
{
    public static readonly StyledProperty<string?> SourceProperty = AvaloniaProperty.Register<CoverArt, string?>(nameof(Source));
    public static readonly StyledProperty<string?> TitleProperty = AvaloniaProperty.Register<CoverArt, string?>(nameof(Title));
    public static readonly StyledProperty<string?> BadgeProperty = AvaloniaProperty.Register<CoverArt, string?>(nameof(Badge));
    public static readonly StyledProperty<Color> AccentProperty = AvaloniaProperty.Register<CoverArt, Color>(nameof(Accent), Color.Parse("#7B61FF"));
    /// <summary>Draw the title on placeholder art (used where no caption is shown below the cover).</summary>
    public static readonly StyledProperty<bool> ShowTitleProperty = AvaloniaProperty.Register<CoverArt, bool>(nameof(ShowTitle));
    public static readonly StyledProperty<bool> ShowBadgeProperty = AvaloniaProperty.Register<CoverArt, bool>(nameof(ShowBadge), true);

    private Bitmap? _bitmap;
    private CancellationTokenSource? _cts;
    private int _requestedWidth;

    static CoverArt()
    {
        AffectsRender<CoverArt>(TitleProperty, BadgeProperty, AccentProperty, ShowTitleProperty, ShowBadgeProperty);
        ClipToBoundsProperty.OverrideDefaultValue<CoverArt>(true);
    }

    public string? Source { get => GetValue(SourceProperty); set => SetValue(SourceProperty, value); }
    public string? Title { get => GetValue(TitleProperty); set => SetValue(TitleProperty, value); }
    public string? Badge { get => GetValue(BadgeProperty); set => SetValue(BadgeProperty, value); }
    public Color Accent { get => GetValue(AccentProperty); set => SetValue(AccentProperty, value); }
    public bool ShowTitle { get => GetValue(ShowTitleProperty); set => SetValue(ShowTitleProperty, value); }
    public bool ShowBadge { get => GetValue(ShowBadgeProperty); set => SetValue(ShowBadgeProperty, value); }

    /// <summary>True when a real cover bitmap is shown (tests / diagnostics).</summary>
    public bool HasImage => _bitmap != null;

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == SourceProperty)
        {
            _bitmap = null;
            _requestedWidth = 0;
            InvalidateVisual();
            RequestImage();
        }
        else if (change.Property == BoundsProperty)
        {
            var wanted = AsyncImageLoader.Bucket(Bounds.Width * Scaling);
            if (Source != null && wanted > _requestedWidth) RequestImage();
        }
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

    private double Scaling => TopLevel.GetTopLevel(this)?.RenderScaling ?? 1.0;

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
        if (string.IsNullOrEmpty(source) || !IsAttachedToVisualTree() || Bounds.Width <= 0) return;
        var width = AsyncImageLoader.Bucket(Bounds.Width * Scaling);
        _requestedWidth = width;
        var cached = AsyncImageLoader.Shared.TryGetCached(source, width);
        if (cached != null)
        {
            _bitmap = cached;
            InvalidateVisual();
            return;
        }
        var cts = new CancellationTokenSource();
        _cts = cts;
        _ = LoadAsync(source, width, cts.Token);
    }

    private bool IsAttachedToVisualTree() => this.GetVisualRootSafe() != null;

    private async Task LoadAsync(string source, int width, CancellationToken ct)
    {
        Bitmap? bitmap;
        try { bitmap = await AsyncImageLoader.Shared.LoadAsync(source, width, ct).ConfigureAwait(false); }
        catch (OperationCanceledException) { return; }
        if (ct.IsCancellationRequested || bitmap == null) return;
        Dispatcher.UIThread.Post(() =>
        {
            if (ct.IsCancellationRequested || Source != source) return;
            _bitmap = bitmap;
            InvalidateVisual();
        }, DispatcherPriority.Background);
    }

    public override void Render(DrawingContext ctx)
    {
        var rect = new Rect(Bounds.Size);
        if (rect.Width <= 0 || rect.Height <= 0) return;
        if (_bitmap is { } bmp)
        {
            // UniformToFill: crop the image to the tile
            var src = bmp.Size;
            var scale = Math.Max(rect.Width / src.Width, rect.Height / src.Height);
            var w = rect.Width / scale;
            var h = rect.Height / scale;
            var srcRect = new Rect((src.Width - w) / 2, (src.Height - h) / 2, w, h);
            ctx.DrawImage(bmp, srcRect, rect);
            return;
        }
        DrawPlaceholder(ctx, rect);
    }

    private void DrawPlaceholder(DrawingContext ctx, Rect rect)
    {
        var title = Title ?? "";
        var hash = Hash(title);
        var accent = Accent;
        var top = ColorUtil.Shift(ColorUtil.Mix(accent, Color.Parse("#1E1C3A"), 0.30), dHue: (hash % 21) - 10);
        var bottom = ColorUtil.Mix(accent, Color.Parse("#0B0A1A"), 0.80);
        ctx.FillRectangle(ThemeManager.Gradient(top, bottom, new RelativePoint(0, 0, RelativeUnit.Relative), new RelativePoint(0.8, 1, RelativeUnit.Relative)), rect);

        var w = rect.Width;
        var h = rect.Height;
        // soft light from a corner
        var lightX = (hash >> 3) % 2 == 0 ? 0.85 : 0.15;
        ctx.FillRectangle(ThemeManager.Radial(ColorUtil.WithAlpha(ColorUtil.Mix(accent, Colors.White, 0.45), 0x55),
            new RelativePoint(lightX, 0.05, RelativeUnit.Relative), 0.75), rect);

        // generative shapes: a large disc and two rings, positioned by the title hash
        var cx = w * (0.25 + ((hash >> 5) % 50) / 100.0);
        var cy = h * (0.30 + ((hash >> 11) % 40) / 100.0);
        var r = w * (0.42 + ((hash >> 17) % 18) / 100.0);
        ctx.DrawEllipse(new SolidColorBrush(ColorUtil.WithAlpha(ColorUtil.Mix(accent, Colors.White, 0.15), 0x2A)), null, new Point(cx, cy), r, r);
        var ringPen = new Pen(new SolidColorBrush(Color.FromArgb(0x26, 255, 255, 255)), Math.Max(1, w / 140));
        ctx.DrawEllipse(null, ringPen, new Point(w - cx * 0.6, h - cy * 0.5), r * 0.75, r * 0.75);
        ctx.DrawEllipse(null, ringPen, new Point(w - cx * 0.6, h - cy * 0.5), r * 0.48, r * 0.48);

        var family = GetValue(TextElement.FontFamilyProperty);

        // big initials
        var initials = Fmt.Initials(title);
        var initialsText = new FormattedText(initials, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight,
            new Typeface(family, FontStyle.Normal, FontWeight.Bold), Math.Max(12, w * 0.36), new SolidColorBrush(Color.FromArgb(0xEE, 255, 255, 255)));
        var ix = (w - initialsText.Width) / 2;
        var iy = ShowTitle ? h * 0.22 : (h - initialsText.Height) / 2 - h * 0.02;
        ctx.DrawText(initialsText, new Point(ix, iy));

        // bottom scrim + title (only where no caption is shown)
        if (ShowTitle && title.Length > 0)
        {
            ctx.FillRectangle(ThemeManager.Gradient(Color.FromArgb(0, 11, 10, 26), Color.FromArgb(0xD8, 11, 10, 26),
                new RelativePoint(0, 0.45, RelativeUnit.Relative), new RelativePoint(0, 1, RelativeUnit.Relative)), rect);
            var pad = w * 0.09;
            var titleText = new FormattedText(title, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight,
                new Typeface(family, FontStyle.Normal, FontWeight.SemiBold), Math.Max(10, w * 0.085), new SolidColorBrush(Color.Parse("#F3F4FA")))
            {
                MaxTextWidth = Math.Max(1, w - 2 * pad),
                MaxLineCount = 3,
                Trimming = TextTrimming.CharacterEllipsis,
                LineHeight = Math.Max(10, w * 0.085) * 1.2,
            };
            ctx.DrawText(titleText, new Point(pad, h - pad - titleText.Height));
        }

        // system badge (top left)
        if (ShowBadge && !string.IsNullOrEmpty(Badge))
        {
            var size = Math.Clamp(w * 0.062, 9, 13);
            var badgeText = new FormattedText(Badge.ToUpperInvariant(), CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
                new Typeface(family, FontStyle.Normal, FontWeight.SemiBold), size, new SolidColorBrush(Color.FromArgb(0xF0, 255, 255, 255)));
            var m = Math.Max(6, w * 0.06);
            var box = new Rect(m, m, badgeText.Width + size * 1.3, badgeText.Height + size * 0.55);
            ctx.DrawRectangle(new SolidColorBrush(Color.FromArgb(0x52, 11, 10, 26)), new Pen(new SolidColorBrush(Color.FromArgb(0x30, 255, 255, 255)), 1),
                new RoundedRect(box, box.Height / 2));
            ctx.DrawText(badgeText, new Point(box.X + size * 0.65, box.Y + size * 0.275));
        }
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
