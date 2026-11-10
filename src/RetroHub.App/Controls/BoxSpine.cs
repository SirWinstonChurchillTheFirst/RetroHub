using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using RetroHub.App.Theming;

namespace RetroHub.App.Controls;

/// <summary>
/// The generated spine of the box pulled from the shelf (DESIGN 7.3, only on the detail stage, never in a grid): a
/// quad left of the box's actual image rectangle, <see cref="SpineWidth"/> wide and as tall as the image. Its foot
/// stands on the board, its top falls <see cref="Slant"/> px toward the outside – the box reads as slightly turned
/// without any 3D. Fill: the system colour mixed with 18 % black (the side in shadow), 1 px paper 20 % on the slanted
/// top edge. Print (−90°, read bottom to top, Archivo Condensed): at the foot the system code and the year (number
/// role), above them the title, ellipsised; without a year only the code. The text colour is chosen against the
/// darkened fill (ink or paper, whichever reads better).
/// <para>The spine fills the same panel as its <see cref="Art"/> (the cover, offset by the spine width) and draws
/// itself at the art's left edge, so it follows contain/bottom alignment and the lift of the panel. All text is laid
/// out on change, never in <see cref="Render"/>.</para>
/// </summary>
public sealed class BoxSpine : Control
{
    public static readonly StyledProperty<string?> TitleProperty = AvaloniaProperty.Register<BoxSpine, string?>(nameof(Title));
    public static readonly StyledProperty<string?> CodeProperty = AvaloniaProperty.Register<BoxSpine, string?>(nameof(Code));
    public static readonly StyledProperty<string?> YearProperty = AvaloniaProperty.Register<BoxSpine, string?>(nameof(Year));
    public static readonly StyledProperty<string?> SystemIdProperty = AvaloniaProperty.Register<BoxSpine, string?>(nameof(SystemId));
    public static readonly StyledProperty<Color> AccentProperty = AvaloniaProperty.Register<BoxSpine, Color>(nameof(Accent), Color.Parse("#A0A7B2"));
    public static readonly StyledProperty<double> SpineWidthProperty = AvaloniaProperty.Register<BoxSpine, double>(nameof(SpineWidth), 28);
    public static readonly StyledProperty<double> SlantProperty = AvaloniaProperty.Register<BoxSpine, double>(nameof(Slant), 8);

    private CoverArt? _art;
    private FormattedText? _title, _code, _year;
    private (string?, string?, string?, string?, int, int) _key;

    static BoxSpine()
    {
        AffectsRender<BoxSpine>(TitleProperty, CodeProperty, YearProperty, SystemIdProperty, AccentProperty, SpineWidthProperty, SlantProperty);
        IsHitTestVisibleProperty.OverrideDefaultValue<BoxSpine>(false);
        ClipToBoundsProperty.OverrideDefaultValue<BoxSpine>(false);
    }

    public BoxSpine() => TextOptions.SetTextRenderingMode(this, TextRenderingMode.Antialias);

    public string? Title { get => GetValue(TitleProperty); set => SetValue(TitleProperty, value); }
    public string? Code { get => GetValue(CodeProperty); set => SetValue(CodeProperty, value); }
    public string? Year { get => GetValue(YearProperty); set => SetValue(YearProperty, value); }
    public string? SystemId { get => GetValue(SystemIdProperty); set => SetValue(SystemIdProperty, value); }
    public Color Accent { get => GetValue(AccentProperty); set => SetValue(AccentProperty, value); }
    /// <summary>28 at the reference size, 20 in the ≈1100 layout.</summary>
    public double SpineWidth { get => GetValue(SpineWidthProperty); set => SetValue(SpineWidthProperty, value); }
    public double Slant { get => GetValue(SlantProperty); set => SetValue(SlantProperty, value); }

    /// <summary>The cover this spine belongs to (a sibling in the same panel).</summary>
    public CoverArt? Art
    {
        get => _art;
        set
        {
            if (_art == value) return;
            if (_art != null) _art.ArtRectChanged -= OnArtChanged;
            _art = value;
            if (_art != null) _art.ArtRectChanged += OnArtChanged;
            InvalidateVisual();
        }
    }

    private void OnArtChanged(object? sender, EventArgs e)
    {
        SpineChanged?.Invoke(this, EventArgs.Empty);
        InvalidateVisual();
    }

    /// <summary>Raised when the spine moved or changed height (the board redraws its contact shadow).</summary>
    public event EventHandler? SpineChanged;

    /// <summary>
    /// The spine's bounding rectangle in this control's coordinates (its slanted top included), or empty while the
    /// cover has no rectangle yet.
    /// </summary>
    public Rect SpineRect
    {
        get
        {
            if (_art is not { } art || art.ArtRect.Width <= 0 || !IsVisible) return default;
            var r = art.ArtRect;
            // the art's position relative to this control (siblings in one panel: layout bounds, no transforms)
            var x = art.Bounds.X - Bounds.X + r.X;
            var y = art.Bounds.Y - Bounds.Y + r.Y;
            var w = SpineWidth;
            return new Rect(Math.Round(x - w), Math.Round(y), w, Math.Round(r.Height));
        }
    }

    private static readonly IImmutableSolidColorBrush TopEdge = new ImmutableSolidColorBrush(Color.FromArgb(0x33, 0xF5, 0xF7, 0xFA));
    private static readonly IImmutableSolidColorBrush Fold = new ImmutableSolidColorBrush(Color.FromArgb(0x59, 0, 0, 0));

    public override void Render(DrawingContext ctx)
    {
        var s = SpineRect;
        if (s.Width <= 0 || s.Height <= 8) return;
        var look = SystemPalette.Get(SystemId, Accent);
        var fill = ColorUtil.Mix(look.Color, Colors.Black, 0.18);
        var slant = Math.Min(Slant, s.Height / 4);
        // the quad: outer edge lower by the slant at the top, both feet on the board
        var g = new StreamGeometry();
        using (var c = g.Open())
        {
            c.BeginFigure(new Point(s.X, s.Y + slant), true);
            c.LineTo(new Point(s.Right, s.Y));
            c.LineTo(new Point(s.Right, s.Bottom));
            c.LineTo(new Point(s.X, s.Bottom));
            c.EndFigure(true);
        }
        ctx.DrawGeometry(new ImmutableSolidColorBrush(fill), null, g);
        // 1 px light on the slanted top edge, a dark fold where the side meets the front
        ctx.DrawLine(new ImmutablePen(TopEdge, 1), new Point(s.X, s.Y + slant + 0.5), new Point(s.Right, s.Y + 0.5));
        ctx.FillRectangle(Fold, new Rect(s.Right - 1, s.Y, 1, s.Height));

        var ink = ColorUtil.Contrast(SystemPalette.Paper, fill) >= ColorUtil.Contrast(SystemPalette.Ink, fill) ? SystemPalette.Paper : SystemPalette.Ink;
        EnsureText(s, ink);
        var pad = Math.Round(s.Width * 0.5);
        var foot = s.Bottom - pad;
        void Up(FormattedText t)
        {
            using (ctx.PushTransform(Matrix.CreateRotation(-Math.PI / 2) * Matrix.CreateTranslation(s.X + (s.Width - t.Height) / 2, foot)))
                ctx.DrawText(t, new Point(0, 0));
            foot -= Math.Ceiling(t.Width) + Gap;
        }
        if (_code != null) Up(_code);
        if (_year != null) Up(_year);
        if (_title != null)
        {
            foot -= Gap; // a wider pause between the foot print and the title
            Up(_title);
        }
    }

    private const double Gap = 8;

    private void EnsureText(Rect s, Color ink)
    {
        var key = (Title, Code, Year, ink.ToString(), (int)s.Width, (int)s.Height);
        if (_key == key) return;
        _key = key;
        var brush = new ImmutableSolidColorBrush(ink);
        var big = Math.Round(Math.Clamp(s.Width * 0.64, 15, 18));
        const double small = 15; // code (spine 15) and year (number 15), DESIGN 7.3
        _code = string.IsNullOrEmpty(Code) ? null
            : new FormattedText(Code, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, FontRoles.SpineFace, small, brush);
        _year = string.IsNullOrEmpty(Year) ? null
            : new FormattedText(Year, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, FontRoles.NumberFace, small, brush);
        _title = null;
        if (string.IsNullOrEmpty(Title)) return;
        var pad = Math.Round(s.Width * 0.5);
        var used = pad + (_code != null ? Math.Ceiling(_code.Width) + Gap : 0) + (_year != null ? Math.Ceiling(_year.Width) + Gap : 0) + Gap;
        var room = s.Height - used - Math.Max(Slant, pad) - 6;
        if (room < 40) return;
        // a spine prints its title whole where it can (M3: never "Banjo-K…"): the full title, else the main title before
        // " – "/": ", only as a last resort the full title with an ellipsis
        FormattedText Make(string text, bool trim) =>
            new(FitText.Typeset(text), CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, FontRoles.SpineFace, big, brush)
            {
                MaxTextWidth = trim ? room : double.PositiveInfinity,
                MaxLineCount = 1,
                Trimming = trim ? TextTrimming.CharacterEllipsis : TextTrimming.None,
            };
        var full = Make(Title, false);
        if (full.Width <= room) { _title = full; return; }
        var (main, _) = CoverArt.SplitTitle(Title);
        var head = Make(main, false);
        _title = head.Width <= room ? head : Make(Title, true);
    }
}
