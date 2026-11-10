using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using RetroHub.App.Theming;

namespace RetroHub.App.Controls;

/// <summary>
/// The spine of a system on an Emulatoren row (DESIGN 7.6.2, M6 QA): 40 × 72 in the system's wall – Wand hell over the
/// top 38 %, a 1 px light edge, Wand below – an 8 px band in Farbe on top, the catalog code vertical in Rücken 17 in the
/// system's Licht (read bottom to top, like the shelf spines of Start and Bibliothek) and the motif (6.2) 20 px in Licht
/// at 60 % at the foot. A code too long for the room above the motif takes the whole spine and the motif drops out.
/// Text is built once per look/code/size, never in <see cref="Render"/>.
/// </summary>
public sealed class MiniSpine : Control
{
    public static readonly StyledProperty<SystemLook?> LookProperty = AvaloniaProperty.Register<MiniSpine, SystemLook?>(nameof(Look));
    public static readonly StyledProperty<string?> CodeProperty = AvaloniaProperty.Register<MiniSpine, string?>(nameof(Code));
    /// <summary>The system when no <see cref="Look"/> is given (a game's spine on the delivery note).</summary>
    public static readonly StyledProperty<string?> SystemIdProperty = AvaloniaProperty.Register<MiniSpine, string?>(nameof(SystemId));
    /// <summary>Rücken size of the code (17; a game title on a 24 px spine: 14). Steps down to 13, then it is cut.</summary>
    public static readonly StyledProperty<double> CodeSizeProperty = AvaloniaProperty.Register<MiniSpine, double>(nameof(CodeSize), 17);
    /// <summary>Draw the motif at the foot (system spines) or not (game spines).</summary>
    public static readonly StyledProperty<bool> ShowMotifProperty = AvaloniaProperty.Register<MiniSpine, bool>(nameof(ShowMotif), true);

    public const double Band = 8, MotifSize = 20, Inset = 4, Gap = 3;
    private static readonly IImmutableSolidColorBrush LightEdge = new ImmutableSolidColorBrush(Color.Parse("#17FFF3E0"));

    private FormattedText? _code;
    private (string?, string?, double) _key;

    static MiniSpine()
    {
        AffectsRender<MiniSpine>(LookProperty, CodeProperty, SystemIdProperty, CodeSizeProperty, ShowMotifProperty);
        IsHitTestVisibleProperty.OverrideDefaultValue<MiniSpine>(false);
        WidthProperty.OverrideDefaultValue<MiniSpine>(40);
        HeightProperty.OverrideDefaultValue<MiniSpine>(72);
    }

    public SystemLook? Look { get => GetValue(LookProperty); set => SetValue(LookProperty, value); }
    public string? Code { get => GetValue(CodeProperty); set => SetValue(CodeProperty, value); }
    public string? SystemId { get => GetValue(SystemIdProperty); set => SetValue(SystemIdProperty, value); }
    public double CodeSize { get => GetValue(CodeSizeProperty); set => SetValue(CodeSizeProperty, value); }
    public bool ShowMotif { get => GetValue(ShowMotifProperty); set => SetValue(ShowMotifProperty, value); }

    /// <summary>Does the code fit above the motif (true) or does it take the whole spine (tests)?</summary>
    public bool ShowsMotif { get; private set; }

    public override void Render(DrawingContext ctx)
    {
        var look = Look ?? (string.IsNullOrEmpty(SystemId) ? null : SystemPalette.Get(SystemId));
        var r = new Rect(Bounds.Size);
        if (look == null || r.Width <= 0 || r.Height <= 0) return;
        var split = Math.Round(r.Height * 0.38);
        ctx.FillRectangle(look.WallHiBrush, new Rect(0, 0, r.Width, split));
        ctx.FillRectangle(look.WallBrush, new Rect(0, split, r.Width, r.Height - split));
        ctx.FillRectangle(LightEdge, new Rect(0, split, r.Width, 1));
        var band = Math.Min(Band, Math.Round(r.Width * 0.25));
        // a printed box spine (M7): curvature, the band printed with a hair of margin, edges
        ctx.FillRectangle(Craft.Curvature, r);
        ctx.FillRectangle(look.ColorBrush, new Rect(2, 2, r.Width - 4, band - 1));
        ctx.FillRectangle(Craft.Dark(0.30), new Rect(2, band + 1, r.Width - 4, 1));
        Craft.Edges(ctx, r, lit: 0.09, dark: 0.32, grime: 0.18, grimeHeight: 8, topLit: 0.10, softRight: 3);

        var code = Text(look, r);
        var motifTop = r.Height - Inset - MotifSize;
        var room = motifTop - Band - 2 * Gap;
        ShowsMotif = ShowMotif && (code == null || code.Width <= room);
        if (ShowsMotif)
            MotifCatalog.DrawSilhouette(ctx, look, new Rect((r.Width - MotifSize) / 2, motifTop, MotifSize, MotifSize), 0.6, look.LightBrush);
        if (code != null)
        {
            // read bottom to top; its foot stands above the motif (or at the spine's foot without one)
            var foot = ShowsMotif ? motifTop - Gap : r.Height - Inset;
            using (ctx.PushTransform(Matrix.CreateRotation(-Math.PI / 2) * Matrix.CreateTranslation((r.Width - code.Height) / 2, foot)))
                ctx.DrawText(code, new Point(0, 0));
        }
    }

    private FormattedText? Text(SystemLook look, Rect r)
    {
        var key = (look.Id, Code, r.Height + CodeSize * 1000);
        if (_key == key) return _code;
        _key = key;
        _code = null;
        if (string.IsNullOrEmpty(Code)) return null;
        // Rücken 17; a code longer than the spine steps down to 13 (never below)
        var max = r.Height - Band - Gap - Inset;
        // (a code asked for below 13 – the Lieferschein's 12 – is tried at its own size at least once)
        for (var size = CodeSize; size >= Math.Min(13, CodeSize); size -= 1)
        {
            _code = new FormattedText(Code, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, FontRoles.SpineFace, size, look.LightBrush);
            if (_code.Width <= max) return _code;
        }
        // still too long (a game title): cut with an ellipsis on one line
        _code!.MaxTextWidth = max;
        _code.MaxLineCount = 1;
        _code.Trimming = TextTrimming.CharacterEllipsis;
        return _code;
    }
}
