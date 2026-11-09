using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using RetroHub.App.Localization;
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
    private (string?, string?, int) _codeKey;

    static TvScreen()
    {
        AffectsRender<TvScreen>(SystemIdProperty, AccentProperty, CodeProperty, BoundsProperty);
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
            // "Kein Signal" (DESIGN 7.1): the system's wall as a test card – the motif stands centred on the bottom at
            // 50 % of the height, top left an OSD in the system's light: code (Rücken 28) and "Kein Signal" below
            SystemWall.Draw(context, this, inner, look, band: 8, motifShare: 0, motifInset: 0);
            var motif = Math.Round(inner.Height * 0.5);
            MotifCatalog.Draw(context, look, new Rect(inner.Center.X - motif / 2, inner.Bottom - motif, motif, motif));
            EnsureText(look);
            var x = inner.X + Math.Round(inner.Width * 0.05);
            var y = inner.Y + 8 + Math.Round(inner.Height * 0.05);
            if (_code is { } code)
            {
                context.DrawText(code, new Point(x, y));
                y += Math.Round(code.Height) + 2;
            }
            if (_noSignal is { } text) context.DrawText(text, new Point(x, y));
            TextureFill.Draw(context, this, inner, Texture.Scan, ScanOpacity);
        }
    }

    private FormattedText? _noSignal;

    private void EnsureText(SystemLook look)
    {
        var key = (Code, look.Id, (int)Bounds.Height);
        if (_codeKey == key && _noSignal != null) return;
        _codeKey = key;
        var size = Math.Clamp(Math.Round(Bounds.Height * 0.067), 18, 28);
        _code = string.IsNullOrEmpty(Code) ? null
            : new FormattedText(Code, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, FontRoles.SpineFace, size, look.LightBrush);
        _noSignal = new FormattedText(Loc.T("screen.noSignal"), Loc.Culture, FlowDirection.LeftToRight, FontRoles.UiFace,
            Math.Max(15, Math.Round(size * 0.72)), NoSignalBrush);
    }

    private static readonly IImmutableSolidColorBrush NoSignalBrush = new ImmutableSolidColorBrush(Color.Parse("#C9BFAF"));
}
