using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using RetroHub.App.Theming;

namespace RetroHub.App.Controls;

/// <summary>
/// Object "Brett" (DESIGN 1.2, 4.2): the front of a shelf board. Its height is the front (24 px stage board,
/// 10 px shelf edge); the top 1 px is the shared <see cref="ThemeManager.BoardLight"/> (TV light), the bottom 1 px the
/// edge colour, and a 16 px shadow falls below the bounds. Static brushes, one instance per layer, app-wide.
/// </summary>
public sealed class ShelfBoard : Control
{
    public static readonly IImmutableSolidColorBrush FrontBrush = new ImmutableSolidColorBrush(Color.Parse("#2A2620"));
    public static readonly IImmutableSolidColorBrush EdgeBrush = new ImmutableSolidColorBrush(Color.Parse("#3A342B"));
    public static readonly IBrush ShadowBrush = new ImmutableLinearGradientBrush(
        [new ImmutableGradientStop(0, Color.FromArgb(0x73, 0, 0, 0)), new ImmutableGradientStop(1, Color.FromArgb(0, 0, 0, 0))],
        startPoint: new RelativePoint(0, 0, RelativeUnit.Relative), endPoint: new RelativePoint(0, 1, RelativeUnit.Relative));
    /// <summary>Contact shadow of a box on the board (10 px #000 55 % → 0).</summary>
    public static readonly IBrush ContactBrush = new ImmutableLinearGradientBrush(
        [new ImmutableGradientStop(0, Color.FromArgb(0x8C, 0, 0, 0)), new ImmutableGradientStop(1, Color.FromArgb(0, 0, 0, 0))],
        startPoint: new RelativePoint(0, 0, RelativeUnit.Relative), endPoint: new RelativePoint(0, 1, RelativeUnit.Relative));

    /// <summary>The 2 px lip under the board light of a shelf edge: Regal hell + 6 % paper (M3 QA: the edge read as a hairline).</summary>
    public static readonly IImmutableSolidColorBrush LipBrush = new ImmutableSolidColorBrush(Color.Parse("#3A352D"));

    public const double StageHeight = 24;
    /// <summary>The library's shelf edge ("Regalkante", DESIGN 4.2): 12 px since the M3 QA (was 10, read as a rule).</summary>
    public const double EdgeHeight = 12;
    public const double ShadowHeight = 16;

    /// <summary>Draw as a shelf edge (board light, 2 px lip, front, edge) instead of a stage board.</summary>
    public static readonly StyledProperty<bool> IsEdgeProperty = AvaloniaProperty.Register<ShelfBoard, bool>(nameof(IsEdge));

    public bool IsEdge { get => GetValue(IsEdgeProperty); set => SetValue(IsEdgeProperty, value); }

    static ShelfBoard()
    {
        HeightProperty.OverrideDefaultValue<ShelfBoard>(StageHeight);
        ClipToBoundsProperty.OverrideDefaultValue<ShelfBoard>(false);
        IsHitTestVisibleProperty.OverrideDefaultValue<ShelfBoard>(false);
        VerticalAlignmentProperty.OverrideDefaultValue<ShelfBoard>(VerticalAlignment.Top);
    }

    /// <summary>Objects standing on this board: each casts the 10 px contact shadow on the front (covers by their ArtRect).</summary>
    public IReadOnlyList<Control> Casters { get; set; } = [];

    public override void Render(DrawingContext context)
    {
        var r = new Rect(Bounds.Size);
        if (IsEdge) DrawEdge(context, r);
        else Draw(context, r);
        foreach (var c in Casters)
        {
            if (!c.IsEffectivelyVisible) continue;
            var span = c is CoverArt art ? art.ArtRect : new Rect(c.Bounds.Size);
            if (span.Width <= 0) continue;
            if (c.TranslatePoint(span.TopLeft, this) is not { } p) continue;
            context.FillRectangle(ContactBrush, new Rect(p.X, 1, span.Width, Math.Min(10, r.Height - 1)));
        }
    }

    /// <summary>Draws a board front into <paramref name="r"/> (shadow below it).</summary>
    public static void Draw(DrawingContext context, Rect r)
    {
        if (r.Width <= 0 || r.Height <= 0) return;
        context.FillRectangle(FrontBrush, r);
        context.FillRectangle(ThemeManager.BoardLight, new Rect(r.X, r.Y, r.Width, 1));
        context.FillRectangle(EdgeBrush, new Rect(r.X, r.Bottom - 1, r.Width, 1));
        context.FillRectangle(ShadowBrush, new Rect(r.X, r.Bottom, r.Width, ShadowHeight));
    }

    /// <summary>
    /// Draws a shelf edge ("Regalkante") into <paramref name="r"/>: 1 px board light, a 2 px lip (#3A352D) below it,
    /// the front in Regal hell, 1 px edge, the 16 px shadow. The lip gives the edge a thickness the eye reads as a board
    /// the boxes stand on (M3 QA: the plain 10 px front on the room was ≈ 1.3 : 1 and read as a hairline).
    /// </summary>
    public static void DrawEdge(DrawingContext context, Rect r)
    {
        if (r.Width <= 0 || r.Height <= 0) return;
        context.FillRectangle(FrontBrush, r);
        context.FillRectangle(LipBrush, new Rect(r.X, r.Y + 1, r.Width, 2));
        context.FillRectangle(ThemeManager.BoardLight, new Rect(r.X, r.Y, r.Width, 1));
        context.FillRectangle(EdgeBrush, new Rect(r.X, r.Bottom - 1, r.Width, 1));
        context.FillRectangle(ShadowBrush, new Rect(r.X, r.Bottom, r.Width, ShadowHeight));
    }
}

/// <summary>
/// Object "Etikett" (DESIGN 1.2): a paper price label hanging on a board – paper #F4ECDF, ink, Archivo SemiCondensed
/// SemiBold 15, padding 8×3, square. <see cref="IsNew"/> = the "Neu" sticker variant in Bernstein.
/// </summary>
public sealed class ShelfLabel : Border
{
    public static readonly StyledProperty<string?> TextProperty =
        AvaloniaProperty.Register<ShelfLabel, string?>(nameof(Text));
    public static readonly StyledProperty<bool> IsNewProperty =
        AvaloniaProperty.Register<ShelfLabel, bool>(nameof(IsNew));

    private readonly TextBlock _text;

    public ShelfLabel()
    {
        _text = new TextBlock { VerticalAlignment = VerticalAlignment.Center };
        _text.Classes.Add("label");
        _text.Foreground = InkBrush;
        Child = _text;
        Padding = new Thickness(8, 3);
        HorizontalAlignment = HorizontalAlignment.Left;
        UpdateLook();
    }

    public string? Text { get => GetValue(TextProperty); set => SetValue(TextProperty, value); }
    public bool IsNew { get => GetValue(IsNewProperty); set => SetValue(IsNewProperty, value); }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == TextProperty) _text.Text = Text;
        else if (change.Property == IsNewProperty) UpdateLook();
    }

    private static readonly IImmutableSolidColorBrush InkBrush = new ImmutableSolidColorBrush(SystemPalette.Ink);
    private static readonly IImmutableSolidColorBrush PaperBrush = new ImmutableSolidColorBrush(SystemPalette.Paper);
    private IDisposable? _accent;

    private void UpdateLook()
    {
        _accent?.Dispose();
        _accent = null;
        if (IsNew) _accent = this.Bind(BackgroundProperty, this.GetResourceObservable("RhAccentBrush"));
        else Background = PaperBrush;
    }
}
