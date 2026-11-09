using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace RetroHub.App.Controls;

/// <summary>
/// Rows of objects standing on boards ("Nichts schwebt", DESIGN 0.2): uniform columns (as many as fit
/// <see cref="MinItemWidth"/>), the items of a row stand flush on the top edge of a <see cref="ShelfBoard"/>
/// (<see cref="BoardHeight"/>, running <see cref="BoardBleed"/> beyond the panel on both sides), with the boxes'
/// contact shadows on the board. Used by Konsolen until the spine shelf (M4/M5). Static brushes, one draw per row.
/// </summary>
public sealed class ShelfGridPanel : Panel
{
    public static readonly StyledProperty<double> MinItemWidthProperty = AvaloniaProperty.Register<ShelfGridPanel, double>(nameof(MinItemWidth), 268);
    public static readonly StyledProperty<double> ItemHeightProperty = AvaloniaProperty.Register<ShelfGridPanel, double>(nameof(ItemHeight), 200);
    public static readonly StyledProperty<double> ColumnSpacingProperty = AvaloniaProperty.Register<ShelfGridPanel, double>(nameof(ColumnSpacing), 8);
    /// <summary>Gap between a board's lower edge and the top of the next row.</summary>
    public static readonly StyledProperty<double> RowGapProperty = AvaloniaProperty.Register<ShelfGridPanel, double>(nameof(RowGap), 48);
    public static readonly StyledProperty<double> BoardHeightProperty = AvaloniaProperty.Register<ShelfGridPanel, double>(nameof(BoardHeight), ShelfBoard.StageHeight);
    public static readonly StyledProperty<double> BoardBleedProperty = AvaloniaProperty.Register<ShelfGridPanel, double>(nameof(BoardBleed));

    static ShelfGridPanel()
    {
        AffectsMeasure<ShelfGridPanel>(MinItemWidthProperty, ItemHeightProperty, ColumnSpacingProperty, RowGapProperty, BoardHeightProperty);
        AffectsRender<ShelfGridPanel>(BoardBleedProperty);
        ClipToBoundsProperty.OverrideDefaultValue<ShelfGridPanel>(false);
    }

    public double MinItemWidth { get => GetValue(MinItemWidthProperty); set => SetValue(MinItemWidthProperty, value); }
    public double ItemHeight { get => GetValue(ItemHeightProperty); set => SetValue(ItemHeightProperty, value); }
    public double ColumnSpacing { get => GetValue(ColumnSpacingProperty); set => SetValue(ColumnSpacingProperty, value); }
    public double RowGap { get => GetValue(RowGapProperty); set => SetValue(RowGapProperty, value); }
    public double BoardHeight { get => GetValue(BoardHeightProperty); set => SetValue(BoardHeightProperty, value); }
    public double BoardBleed { get => GetValue(BoardBleedProperty); set => SetValue(BoardBleedProperty, value); }

    /// <summary>Columns for <paramref name="width"/> (at least one).</summary>
    public static int ColumnsFor(double width, double minItem, double spacing) =>
        Math.Max(1, (int)Math.Floor((width + spacing) / (minItem + spacing)));

    private readonly BoardLayer _boards;

    public ShelfGridPanel()
    {
        // the boards are drawn by one extra visual behind the items (Panel.Render is sealed); it is not a logical child
        _boards = new BoardLayer(this) { ZIndex = -1 };
        VisualChildren.Add(_boards);
    }

    private int _columns = 1;
    private double _itemWidth;

    private int VisibleCount => Children.Count(c => c.IsVisible);

    protected override Size MeasureOverride(Size availableSize)
    {
        var width = double.IsInfinity(availableSize.Width) ? MinItemWidth * 4 : availableSize.Width;
        _columns = ColumnsFor(width, MinItemWidth, ColumnSpacing);
        _itemWidth = Math.Floor((width - (_columns - 1) * ColumnSpacing) / _columns);
        foreach (var child in Children) child.Measure(new Size(_itemWidth, ItemHeight));
        _boards.Measure(availableSize);
        var rows = (VisibleCount + _columns - 1) / _columns;
        var height = rows == 0 ? 0 : rows * (ItemHeight + BoardHeight) + (rows - 1) * RowGap;
        return new Size(width, height);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var i = 0;
        foreach (var child in Children)
        {
            if (!child.IsVisible) continue;
            var row = i / _columns;
            var col = i % _columns;
            child.Arrange(new Rect(col * (_itemWidth + ColumnSpacing), RowTop(row), _itemWidth, ItemHeight));
            i++;
        }
        _boards.Arrange(new Rect(finalSize));
        _boards.InvalidateVisual();
        return finalSize;
    }

    private double RowTop(int row) => row * (ItemHeight + BoardHeight + RowGap);

    private void RenderBoards(DrawingContext context)
    {
        var count = VisibleCount;
        if (count == 0) return;
        var rows = (count + _columns - 1) / _columns;
        for (var row = 0; row < rows; row++)
        {
            var y = RowTop(row) + ItemHeight;
            ShelfBoard.Draw(context, new Rect(-BoardBleed, y, Bounds.Width + 2 * BoardBleed, BoardHeight));
            var inRow = Math.Min(_columns, count - row * _columns);
            for (var col = 0; col < inRow; col++)
                context.FillRectangle(ShelfBoard.ContactBrush, new Rect(col * (_itemWidth + ColumnSpacing), y + 1, _itemWidth, 10));
        }
    }

    private sealed class BoardLayer(ShelfGridPanel owner) : Control
    {
        static BoardLayer() { IsHitTestVisibleProperty.OverrideDefaultValue<BoardLayer>(false); }
        public override void Render(DrawingContext context) => owner.RenderBoards(context);
    }
}
