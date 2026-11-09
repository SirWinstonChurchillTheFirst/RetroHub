using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using RetroHub.App.Localization;
using RetroHub.App.Theming;

namespace RetroHub.App.Controls;

/// <summary>
/// Object "Bildschirm" (DESIGN 1.2, 7.1) – interim version until the PixelScreen with snaps (M2): a 4:3 set with a
/// 10 px room-deep bezel (1 px shelf-light outline, light edge on top) standing on the stage board. The picture shows
/// "Kein Signal": the system's wall (band, texture) with its motif centred (40 % of the picture height, centre at
/// 52 %), the system code top left in Rücken 28 in the system's light like an OSD, and scanlines darkening 18 % over
/// everything inside the bezel – the only surface with scanlines.
/// <see cref="IsOff"/> (the box beside it already shows the system's wall, empty library): the picture is room-deep with
/// a thin paper line in the middle and the OSD, so box and screen never look the same.
/// </summary>
public sealed class TvScreen : Control
{
    public static readonly StyledProperty<string?> SystemIdProperty = AvaloniaProperty.Register<TvScreen, string?>(nameof(SystemId));
    public static readonly StyledProperty<Color> AccentProperty = AvaloniaProperty.Register<TvScreen, Color>(nameof(Accent), Color.Parse("#948A7A"));
    public static readonly StyledProperty<string?> CodeProperty = AvaloniaProperty.Register<TvScreen, string?>(nameof(Code));
    /// <summary>The set is on but gets no picture: dark screen with the centre line (and the OSD when a system is set).</summary>
    public static readonly StyledProperty<bool> IsOffProperty = AvaloniaProperty.Register<TvScreen, bool>(nameof(IsOff));

    private static readonly IImmutableSolidColorBrush Bezel = new ImmutableSolidColorBrush(Color.Parse("#0A0908"));
    private static readonly IImmutableSolidColorBrush BezelOutline = new ImmutableSolidColorBrush(Color.Parse("#2A2620"));
    private static readonly IImmutableSolidColorBrush BezelLight = new ImmutableSolidColorBrush(Color.FromArgb(0x26, 0xFF, 0xF3, 0xE0));
    private static readonly IImmutableSolidColorBrush DarkPicture = new ImmutableSolidColorBrush(Color.Parse("#100E0C"));
    private static readonly IImmutableSolidColorBrush OffLine = new ImmutableSolidColorBrush(Color.FromArgb(0x33, 0xF4, 0xEC, 0xDF));
    private static readonly IImmutableSolidColorBrush NoSignalBrush = new ImmutableSolidColorBrush(Color.Parse("#C9BFAF"));

    /// <summary>Bezel width at the reference size (560 × 420); scales with the set (≈ 7 px at 400 × 300).</summary>
    public const double FrameWidth = 10;
    /// <summary>How much the scanlines darken the picture (DESIGN 7.1: 18 %).</summary>
    public const double ScanOpacity = 0.18;
    /// <summary>Alpha of the dark row in scan-4x3.png (110 / 255): the fill strength is ScanOpacity / this.</summary>
    private const double ScanPngAlpha = 110 / 255.0;

    private FormattedText? _code;
    private FormattedText? _noSignal;
    private (string?, string?, int) _textKey;

    static TvScreen()
    {
        AffectsRender<TvScreen>(SystemIdProperty, AccentProperty, CodeProperty, IsOffProperty, BoundsProperty);
        IsHitTestVisibleProperty.OverrideDefaultValue<TvScreen>(false);
        ClipToBoundsProperty.OverrideDefaultValue<TvScreen>(true);
    }

    public TvScreen() => TextOptions.SetTextRenderingMode(this, TextRenderingMode.Antialias);

    public string? SystemId { get => GetValue(SystemIdProperty); set => SetValue(SystemIdProperty, value); }
    public Color Accent { get => GetValue(AccentProperty); set => SetValue(AccentProperty, value); }
    public string? Code { get => GetValue(CodeProperty); set => SetValue(CodeProperty, value); }
    public bool IsOff { get => GetValue(IsOffProperty); set => SetValue(IsOffProperty, value); }

    /// <summary>The picture area inside the bezel of a set of <paramref name="size"/>.</summary>
    public static Rect PictureRect(Size size)
    {
        var bezel = Math.Max(6, Math.Round(FrameWidth * size.Height / 420));
        return new Rect(bezel, bezel, Math.Max(0, size.Width - 2 * bezel), Math.Max(0, size.Height - 2 * bezel));
    }

    /// <summary>The motif box of the "Kein Signal" picture: 40 % of the picture height, centred, centre at 52 %.</summary>
    public static Rect MotifRect(Rect picture)
    {
        var m = Math.Round(picture.Height * 0.40);
        return new Rect(Math.Round(picture.Center.X - m / 2), Math.Round(picture.Y + picture.Height * 0.52 - m / 2), m, m);
    }

    public override void Render(DrawingContext context)
    {
        var r = new Rect(Bounds.Size);
        if (r.Width <= 0 || r.Height <= 0) return;
        // the set: bezel, a 1 px outline in shelf-light so it stands off the wall, light from above on its top edge
        context.FillRectangle(BezelOutline, r);
        context.FillRectangle(Bezel, r.Deflate(1));
        context.FillRectangle(BezelLight, new Rect(r.X + 1, r.Y + 1, r.Width - 2, 1));
        var inner = PictureRect(r.Size);
        if (inner.Width <= 0 || inner.Height <= 0) return;
        var look = string.IsNullOrEmpty(SystemId) ? null : SystemPalette.Get(SystemId, Accent);
        using (context.PushClip(inner))
        {
            if (look == null || IsOff)
            {
                context.FillRectangle(DarkPicture, inner);
                context.FillRectangle(OffLine, new Rect(inner.X, Math.Round(inner.Center.Y), inner.Width, 1));
            }
            else
            {
                // "Kein Signal" (DESIGN 7.1): the system's wall as a test card, the motif centred
                SystemWall.Draw(context, this, inner, look, band: 8, motifShare: 0, motifInset: 0);
                MotifCatalog.Draw(context, look, MotifRect(inner));
            }
            if (look != null)
            {
                // the OSD: code in the system's light, "Kein Signal" below (24 / 20 px inside at the reference size)
                EnsureText(look);
                var s = inner.Height / 400;
                var x = inner.X + Math.Round(24 * s);
                var y = inner.Y + Math.Round(20 * s) + (IsOff ? 0 : 8);
                if (_code is { } code)
                {
                    context.DrawText(code, new Point(x, y));
                    y += Math.Round(code.Height) + 2;
                }
                if (_noSignal is { } text) context.DrawText(text, new Point(x, y));
            }
            TextureFill.Draw(context, this, inner, Texture.Scan, ScanOpacity / ScanPngAlpha);
        }
    }

    private void EnsureText(SystemLook look)
    {
        var key = (Code, look.Id, (int)Bounds.Height);
        if (_textKey == key && _noSignal != null) return;
        _textKey = key;
        var size = Math.Clamp(Math.Round(Bounds.Height * 0.067), 18, 28);
        _code = string.IsNullOrEmpty(Code) ? null
            : new FormattedText(Code, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, FontRoles.SpineFace, size, look.LightBrush);
        _noSignal = new FormattedText(Loc.T("screen.noSignal"), Loc.Culture, FlowDirection.LeftToRight, FontRoles.UiFace,
            Math.Max(15, Math.Round(size * 0.72)), NoSignalBrush);
    }
}
