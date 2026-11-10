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
    public static readonly IImmutableSolidColorBrush FrontBrush = new ImmutableSolidColorBrush(Color.Parse("#24272D"));
    public static readonly IImmutableSolidColorBrush EdgeBrush = new ImmutableSolidColorBrush(Color.Parse("#343840"));
    public static readonly IBrush ShadowBrush = new ImmutableLinearGradientBrush(
        [new ImmutableGradientStop(0, Color.FromArgb(0x73, 0, 0, 0)), new ImmutableGradientStop(1, Color.FromArgb(0, 0, 0, 0))],
        startPoint: new RelativePoint(0, 0, RelativeUnit.Relative), endPoint: new RelativePoint(0, 1, RelativeUnit.Relative));
    /// <summary>Contact shadow of a box on the board (10 px #000 55 % → 0).</summary>
    public static readonly IBrush ContactBrush = new ImmutableLinearGradientBrush(
        [new ImmutableGradientStop(0, Color.FromArgb(0x8C, 0, 0, 0)), new ImmutableGradientStop(1, Color.FromArgb(0, 0, 0, 0))],
        startPoint: new RelativePoint(0, 0, RelativeUnit.Relative), endPoint: new RelativePoint(0, 1, RelativeUnit.Relative));

    /// <summary>The 2 px lip under the board light of a shelf edge: Regal hell + 6 % paper (M3 QA: the edge read as a hairline).</summary>
    public static readonly IImmutableSolidColorBrush LipBrush = new ImmutableSolidColorBrush(Color.Parse("#383C44"));

    public const double StageHeight = 24;
    /// <summary>The library's shelf edge ("Regalkante", DESIGN 4.2): 12 px since the M3 QA (was 10, read as a rule).</summary>
    public const double EdgeHeight = 12;
    public const double ShadowHeight = 16;

    /// <summary>Draw as a shelf edge (board light, 2 px lip, front, edge) instead of a stage board.</summary>
    public static readonly StyledProperty<bool> IsEdgeProperty = AvaloniaProperty.Register<ShelfBoard, bool>(nameof(IsEdge));

    public bool IsEdge { get => GetValue(IsEdgeProperty); set => SetValue(IsEdgeProperty, value); }

    /// <summary>
    /// The board stands in front of this system's wall (banner of the system page and the library compartment): its
    /// front is the wall's upper tone lifted by 10 % paper with a 2 px lip, so it reads against every wall – the plain
    /// Regal-hell front almost vanished in front of the warm-grey walls (M5, DESIGN 4.2).
    /// </summary>
    public static readonly StyledProperty<SystemLook?> WallLookProperty = AvaloniaProperty.Register<ShelfBoard, SystemLook?>(nameof(WallLook));

    public SystemLook? WallLook { get => GetValue(WallLookProperty); set => SetValue(WallLookProperty, value); }

    static ShelfBoard()
    {
        HeightProperty.OverrideDefaultValue<ShelfBoard>(StageHeight);
        AffectsRender<ShelfBoard>(WallLookProperty, IsEdgeProperty);
        ClipToBoundsProperty.OverrideDefaultValue<ShelfBoard>(false);
        IsHitTestVisibleProperty.OverrideDefaultValue<ShelfBoard>(false);
        VerticalAlignmentProperty.OverrideDefaultValue<ShelfBoard>(VerticalAlignment.Top);
    }

    /// <summary>Objects standing on this board: each casts the 10 px contact shadow on the front (covers by their ArtRect).</summary>
    public IReadOnlyList<Control> Casters { get; set; } = [];

    private uint? _seed;

    /// <summary>The seed of this board's wear (stable: its shelf's or its own name, never a per-process hash).</summary>
    private uint Seed => _seed ??= Craft.Seed((TemplatedParent as Control)?.Name + "/" + Name + "/" + (IsEdge ? "edge" : "stage"));

    public override void Render(DrawingContext context)
    {
        var r = new Rect(Bounds.Size);
        if (WallLook is { } look) DrawOnWall(context, r, look, this, Seed);
        else if (IsEdge) DrawEdge(context, r, this, Seed);
        else Draw(context, r, this, Seed);
        foreach (var c in Casters)
        {
            if (!c.IsEffectivelyVisible) continue;
            var span = c switch
            {
                CoverArt art => art.ArtRect,
                BoxSpine spine => spine.SpineRect,
                _ => new Rect(c.Bounds.Size),
            };
            if (span.Width <= 0) continue;
            if (c.TranslatePoint(span.TopLeft, this) is not { } p) continue;
            context.FillRectangle(ContactBrush, new Rect(p.X, 1, span.Width, Math.Min(10, r.Height - 1)));
        }
    }

    private static readonly IImmutableSolidColorBrush StageTop = new ImmutableSolidColorBrush(ColorUtil.Mix(Color.Parse("#24272D"), SystemPalette.Paper, 0.10));

    /// <summary>
    /// The body of every board (M7 "gezeichnet": the flat bars read as horizontal rules): a visible TOP FACE (we look
    /// slightly down onto the board; 5 px on a 24 px board, 3 px on a 12 px edge) with the lit arris in front of it,
    /// the front with a faint grain, a rounded lower edge (1 px light, 2 px dark) and seeded nicks along the arris.
    /// The board light (TV light) stays the 1 px line at the back edge of the top face.
    /// </summary>
    private static void DrawBody(DrawingContext context, Rect r, IBrush front, IBrush top, Visual? visual, uint seed)
    {
        context.FillRectangle(front, r);
        var topFace = r.Height >= 20 ? 5 : r.Height >= 10 ? 3 : 0;
        if (topFace > 0)
        {
            context.FillRectangle(top, new Rect(r.X, r.Y, r.Width, topFace));
            context.FillRectangle(Craft.Fade(Colors.Black, 0.22, 0, Craft.Toward.Up), new Rect(r.X, r.Y + 1, r.Width, topFace - 1));
            context.FillRectangle(Craft.Hi(0.11), new Rect(r.X, r.Y + topFace, r.Width, 1));
        }
        var face = new Rect(r.X, r.Y + topFace + 1, r.Width, Math.Max(0, r.Height - topFace - 1));
        if (visual != null && face.Height > 2) TextureFill.Draw(context, visual, face, Texture.BoardGrain, 1.0);
        if (r.Height >= 10)
        {
            context.FillRectangle(Craft.Hi(0.05), new Rect(r.X, r.Bottom - 3, r.Width, 1));
            context.FillRectangle(Craft.Dark(0.32), new Rect(r.X, r.Bottom - 2, r.Width, 2));
        }
        else context.FillRectangle(EdgeBrush, new Rect(r.X, r.Bottom - 1, r.Width, 1));
        if (seed != 0 && topFace > 0) Craft.Nicks(context, new Rect(r.X, r.Y + topFace, r.Width, 3), seed, 2 + (int)Math.Min(4, r.Width / 600));
    }

    /// <summary>
    /// The rendered board (R3, Blender): black powder-coated steel with a laminate top – the stage board (22 px lip) or
    /// the shelf edge (12 px), tiled along <paramref name="r"/> with its top face above and its shadow below. One board for
    /// every page and every system (theme v2: no per-system wood tones). False when the render is not available.
    /// </summary>
    private static bool DrawRendered(DrawingContext context, Rect r, Visual? visual, bool edge = false) =>
        Rendered.DrawBoard(context, visual, edge || r.Height < 18 ? "board-edge" : "board-stage", r);

    /// <summary>Draws a board front into <paramref name="r"/> (shadow below it).</summary>
    public static void Draw(DrawingContext context, Rect r, Visual? visual = null, uint seed = 0)
    {
        if (r.Width <= 0 || r.Height <= 0) return;
        if (DrawRendered(context, r, visual)) return;
        DrawBody(context, r, FrontBrush, StageTop, visual, seed);
        context.FillRectangle(ThemeManager.BoardLight, new Rect(r.X, r.Y, r.Width, 1));
        context.FillRectangle(ShadowBrush, new Rect(r.X, r.Bottom, r.Width, ShadowHeight));
    }

    private static readonly Dictionary<string, (IBrush Front, IBrush Lip, IBrush Edge)> WallBoards = new();

    /// <summary>
    /// A stage board in front of a system's wall: front = Wand hell + 10 % paper, its top face (+ 18 %) under the board
    /// light, the usual 16 px shadow. Brushes are shared per system.
    /// </summary>
    public static void DrawOnWall(DrawingContext context, Rect r, SystemLook look, Visual? visual = null, uint seed = 0)
    {
        if (r.Width <= 0 || r.Height <= 0) return;
        if (DrawRendered(context, r, visual)) return;
        (IBrush Front, IBrush Lip, IBrush Edge) b;
        lock (WallBoards)
        {
            if (!WallBoards.TryGetValue(look.Id, out b))
                WallBoards[look.Id] = b = (
                    new ImmutableSolidColorBrush(ColorUtil.Mix(look.WallHi, SystemPalette.Paper, 0.10)),
                    new ImmutableSolidColorBrush(ColorUtil.Mix(look.WallHi, SystemPalette.Paper, 0.18)),
                    new ImmutableSolidColorBrush(ColorUtil.Mix(look.Wall, Colors.Black, 0.35)));
        }
        DrawBody(context, r, b.Front, b.Lip, visual, seed);
        context.FillRectangle(ThemeManager.BoardLight, new Rect(r.X, r.Y, r.Width, 1));
        context.FillRectangle(ShadowBrush, new Rect(r.X, r.Bottom, r.Width, ShadowHeight));
    }

    /// <summary>
    /// Draws a shelf edge ("Regalkante") into <paramref name="r"/>: 1 px board light, the top face (#383C44) below it,
    /// the front in Regal hell with its rounded lower edge, the 16 px shadow (M3 QA: the plain 10 px front read as a
    /// hairline; M7: the top face and the grain make it a board the boxes stand on).
    /// </summary>
    public static void DrawEdge(DrawingContext context, Rect r, Visual? visual = null, uint seed = 0)
    {
        if (r.Width <= 0 || r.Height <= 0) return;
        if (DrawRendered(context, r, visual, edge: true)) return;
        DrawBody(context, r, FrontBrush, LipBrush, visual, seed);
        context.FillRectangle(ThemeManager.BoardLight, new Rect(r.X, r.Y, r.Width, 1));
        context.FillRectangle(ShadowBrush, new Rect(r.X, r.Bottom, r.Width, ShadowHeight));
    }
}

/// <summary>
/// Object "Etikett" (DESIGN 1.2): a paper price label hanging on a board – paper #F5F7FA, ink, Archivo SemiCondensed
/// SemiBold 15, padding 8×3, square. <see cref="IsNew"/> = the "Neu" sticker variant in Cyan.
/// </summary>
public sealed class ShelfLabel : Decorator
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

    /// <summary>The paper (paper, or the accent for the "Neu" sticker).</summary>
    public static readonly StyledProperty<IBrush?> BackgroundProperty = AvaloniaProperty.Register<ShelfLabel, IBrush?>(nameof(Background));

    public IBrush? Background { get => GetValue(BackgroundProperty); set => SetValue(BackgroundProperty, value); }

    static ShelfLabel()
    {
        AffectsRender<ShelfLabel>(BackgroundProperty, TextProperty);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == TextProperty) _text.Text = Text;
        else if (change.Property == IsNewProperty) UpdateLook();
    }

    /// <summary>
    /// The label is cut paper (M7 "gezeichnet"; the crisp rectangles read as CSS badges): an irregular outline seeded by
    /// its text, tilted by up to 0.8°, a darker bottom edge for its thickness, a soft shadow and faint fibres. The text
    /// stays upright (a hair off the paper's tilt, like a hand-applied sticker).
    /// </summary>
    public override void Render(DrawingContext context)
    {
        var r = new Rect(Bounds.Size);
        if (r.Width <= 0 || r.Height <= 0) return;
        var color = (Background as ISolidColorBrush)?.Color ?? SystemPalette.Paper;
        var seed = Craft.Seed(Text);
        Craft.DrawPaper(context, this, r, seed, color, Craft.Range(seed, 40, -0.8, 0.8), jitter: 0.6, shadow: 0.35, fibres: 0.35);
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
