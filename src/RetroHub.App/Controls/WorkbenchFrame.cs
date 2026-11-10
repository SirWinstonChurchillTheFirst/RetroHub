using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace RetroHub.App.Controls;

/// <summary>What a child of <see cref="WorkbenchFrame"/> is.</summary>
public enum WorkbenchRole { None, Head, Scroll, Side, Fade }

/// <summary>
/// The one frame of the three Werkbank pages (DESIGN 7.6.2, M6 QA): a pinned head (Plakat 56 with its top at page y 24
/// = window y 96, one meta line 8 px below, header buttons right on the list edge), and below it one scrolling list in a
/// two-column grid – list at most 1100 from the page margin, gutter 56, side column 420. The first section label stands
/// at window y 212 on every page. Nothing in the head ever scrolls, so a title is never cut under the header bar; the
/// list's viewport fades out over 12 px at its top edge (the grid's overlay, 8.4).
/// <list type="bullet">
/// <item><see cref="WorkbenchRole.Scroll"/>: the ScrollViewer. It is only as wide as the content + 24, so its scroll bar
/// stands 24 px right of the content and never at the window edge.</item>
/// <item><see cref="WorkbenchRole.Side"/>: a pinned side object (the test-card TV of Einstellungen), shown from a page
/// width of <see cref="SideMinWidth"/>. With <see cref="SideInScroll"/> the side column belongs to the scrolling content
/// instead (Importe's scan column) and is always there.</item>
/// <item><see cref="WorkbenchRole.Fade"/>: the 12 px Raum overlay on the viewport's top edge.</item>
/// </list>
/// </summary>
public sealed class WorkbenchFrame : Panel
{
    public const double HeadTop = 24, HeadHeight = 124, ListMax = 1100, Gutter = 56, SideWidth = 420, BarGap = 24, ListTop = 16, FadeHeight = 12;

    public static readonly AttachedProperty<WorkbenchRole> RoleProperty =
        AvaloniaProperty.RegisterAttached<WorkbenchFrame, Control, WorkbenchRole>("Role");

    public static readonly StyledProperty<bool> SideInScrollProperty =
        AvaloniaProperty.Register<WorkbenchFrame, bool>(nameof(SideInScroll));

    public static readonly StyledProperty<double> SideMinWidthProperty =
        AvaloniaProperty.Register<WorkbenchFrame, double>(nameof(SideMinWidth), 1500);

    public static WorkbenchRole GetRole(Control c) => c.GetValue(RoleProperty);
    public static void SetRole(Control c, WorkbenchRole value) => c.SetValue(RoleProperty, value);

    public bool SideInScroll { get => GetValue(SideInScrollProperty); set => SetValue(SideInScrollProperty, value); }
    public double SideMinWidth { get => GetValue(SideMinWidthProperty); set => SetValue(SideMinWidthProperty, value); }

    /// <summary>The list column's width of the last layout (≤ 1100).</summary>
    public double ListWidth { get; private set; }

    /// <summary>The pinned side object is shown (wide enough).</summary>
    public bool SideShown { get; private set; }

    /// <summary>
    /// The side column's width of the last layout: 420, growing with the free room beside a full list up to
    /// <see cref="SideMax"/> (R3 QA: ≈ 250 px stood empty right of the column at 1920).
    /// </summary>
    public double SideColumnWidth { get; private set; } = SideWidth;

    /// <summary>The widest the side column grows.</summary>
    public const double SideMax = 560;

    /// <summary>Raised when <see cref="SideColumnWidth"/> changed (a page with the side in its scrolling grid follows).</summary>
    public event Action<double>? SideColumnWidthChanged;

    static WorkbenchFrame()
    {
        AffectsMeasure<WorkbenchFrame>(SideInScrollProperty, SideMinWidthProperty);
    }

    private double Inset() => this.TryFindResource("RhPageInsetX", out var r) && r is Thickness t ? t.Left : 96;

    private (double Inset, double List, double Scroll, bool Side, double SideW) Columns(double width)
    {
        var inset = Inset();
        var hasSide = Children.Any(c => GetRole(c) == WorkbenchRole.Side);
        var side = !SideInScroll && hasSide && width >= SideMinWidth;
        var sideW = Math.Clamp(width - 2 * inset - Gutter - ListMax - BarGap, SideWidth, SideMax);
        var sideRoom = SideInScroll || side ? Gutter + sideW : 0;
        var list = Math.Clamp(width - 2 * inset - sideRoom, 240, ListMax);
        var scroll = list + (SideInScroll ? sideRoom : 0) + BarGap;
        return (inset, list, scroll, side, sideW);
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        var w = double.IsFinite(availableSize.Width) ? availableSize.Width : 1920;
        var h = double.IsFinite(availableSize.Height) ? availableSize.Height : 1080;
        var (_, list, scroll, side, sideW) = Columns(w);
        ListWidth = list;
        SideShown = side;
        if (Math.Abs(SideColumnWidth - sideW) > 0.5)
        {
            SideColumnWidth = sideW;
            SideColumnWidthChanged?.Invoke(sideW);
        }
        var body = Math.Max(0, h - HeadHeight);
        foreach (var c in Children)
        {
            switch (GetRole(c))
            {
                case WorkbenchRole.Head: c.Measure(new Size(list, HeadHeight - HeadTop)); break;
                case WorkbenchRole.Scroll: c.Measure(new Size(scroll, body)); break;
                case WorkbenchRole.Fade: c.Measure(new Size(scroll, FadeHeight)); break;
                case WorkbenchRole.Side:
                    if (c.IsVisible != side) c.IsVisible = side;
                    c.Measure(new Size(sideW, Math.Max(0, body - ListTop)));
                    break;
                default: c.Measure(availableSize); break;
            }
        }
        return new Size(w, h);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var (inset, list, scroll, side, sideW) = Columns(finalSize.Width);
        var body = Math.Max(0, finalSize.Height - HeadHeight);
        foreach (var c in Children)
        {
            switch (GetRole(c))
            {
                case WorkbenchRole.Head: c.Arrange(new Rect(inset, HeadTop, list, HeadHeight - HeadTop)); break;
                case WorkbenchRole.Scroll: c.Arrange(new Rect(inset, HeadHeight, scroll, body)); break;
                case WorkbenchRole.Fade: c.Arrange(new Rect(inset, HeadHeight, scroll - BarGap, FadeHeight)); break;
                case WorkbenchRole.Side:
                    c.Arrange(side ? new Rect(inset + list + Gutter, HeadHeight + ListTop, sideW, c.DesiredSize.Height) : default);
                    break;
                default: c.Arrange(new Rect(finalSize)); break;
            }
        }
        return finalSize;
    }

    /// <summary>The 12 px Raum overlay at the list viewport's top edge (100 % → 0), shared by the three pages.</summary>
    public static readonly IBrush FadeBrush = new ImmutableLinearGradientBrush(
        [new ImmutableGradientStop(0, Color.Parse("#FF14120F")), new ImmutableGradientStop(1, Color.Parse("#0014120F"))],
        startPoint: new RelativePoint(0, 0, RelativeUnit.Relative), endPoint: new RelativePoint(0, 1, RelativeUnit.Relative));
}
