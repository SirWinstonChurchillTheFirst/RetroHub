using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using RetroHub.App.Theming;

namespace RetroHub.App.Controls;

/// <summary>
/// Object "Bildschirm" (DESIGN 1.2, 7.1) – interim version until the PixelScreen with snaps (M2): a 4:3 frame of
/// 2 px room-deep standing on the stage board, showing "Kein Signal": the system's wall (band, texture) with its motif
/// at 50 % of the screen height, the system code top left in Rücken 28 in the system's light like an OSD, and
/// scanlines (scan-4x3) at 18 % over everything inside the frame – the only surface with scanlines.
/// Without a system (empty library) the screen is off: room-deep with a thin paper line in the middle.
/// </summary>
public sealed class TvScreen : Control
{
    public static readonly StyledProperty<string?> SystemIdProperty = AvaloniaProperty.Register<TvScreen, string?>(nameof(SystemId));
    public static readonly StyledProperty<Color> AccentProperty = AvaloniaProperty.Register<TvScreen, Color>(nameof(Accent), Color.Parse("#948A7A"));
    public static readonly StyledProperty<string?> CodeProperty = AvaloniaProperty.Register<TvScreen, string?>(nameof(Code));

    private static readonly IImmutableSolidColorBrush Frame = new ImmutableSolidColorBrush(Color.Parse("#0A0908"));
    private static readonly IImmutableSolidColorBrush OffLine = new ImmutableSolidColorBrush(Color.FromArgb(0x33, 0xF4, 0xEC, 0xDF));
    public const double FrameWidth = 2;
    public const double ScanOpacity = 0.18;

    private FormattedText? _code;
    private (string?, string?) _codeKey;

    static TvScreen()
    {
        AffectsRender<TvScreen>(SystemIdProperty, AccentProperty, CodeProperty);
        IsHitTestVisibleProperty.OverrideDefaultValue<TvScreen>(false);
        ClipToBoundsProperty.OverrideDefaultValue<TvScreen>(true);
    }

    public string? SystemId { get => GetValue(SystemIdProperty); set => SetValue(SystemIdProperty, value); }
    public Color Accent { get => GetValue(AccentProperty); set => SetValue(AccentProperty, value); }
    public string? Code { get => GetValue(CodeProperty); set => SetValue(CodeProperty, value); }

    public override void Render(DrawingContext context)
    {
        var r = new Rect(Bounds.Size);
        if (r.Width <= 0 || r.Height <= 0) return;
        context.FillRectangle(Frame, r);
        var inner = r.Deflate(FrameWidth);
        if (string.IsNullOrEmpty(SystemId))
        {
            context.FillRectangle(OffLine, new Rect(inner.X, Math.Round(inner.Center.Y), inner.Width, 1));
            return;
        }
        var look = SystemPalette.Get(SystemId, Accent);
        using (context.PushClip(inner))
        {
            SystemWall.Draw(context, this, inner, look, band: 8, motifShare: 0.5, motifInset: Math.Round(inner.Width * 0.08));
            if (EnsureCode(look) is { } code)
                context.DrawText(code, new Point(inner.X + Math.Round(inner.Width * 0.05), inner.Y + 8 + Math.Round(inner.Height * 0.05)));
            TextureFill.Draw(context, this, inner, Texture.Scan, ScanOpacity);
        }
    }

    private FormattedText? EnsureCode(SystemLook look)
    {
        var key = (Code, look.Id);
        if (_codeKey == key) return _code;
        _codeKey = key;
        _code = string.IsNullOrEmpty(Code) ? null
            : new FormattedText(Code, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, FontRoles.SpineFace, 28, look.LightBrush);
        return _code;
    }
}
