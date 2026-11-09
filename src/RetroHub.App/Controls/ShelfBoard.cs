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

    public const double StageHeight = 24;
    public const double EdgeHeight = 10;
    public const double ShadowHeight = 16;

    static ShelfBoard()
    {
        HeightProperty.OverrideDefaultValue<ShelfBoard>(StageHeight);
        ClipToBoundsProperty.OverrideDefaultValue<ShelfBoard>(false);
        IsHitTestVisibleProperty.OverrideDefaultValue<ShelfBoard>(false);
        VerticalAlignmentProperty.OverrideDefaultValue<ShelfBoard>(VerticalAlignment.Top);
    }

    public override void Render(DrawingContext context) => Draw(context, new Rect(Bounds.Size));

    /// <summary>Draws a board front into <paramref name="r"/> (shadow below it).</summary>
    public static void Draw(DrawingContext context, Rect r)
    {
        if (r.Width <= 0 || r.Height <= 0) return;
        context.FillRectangle(FrontBrush, r);
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
