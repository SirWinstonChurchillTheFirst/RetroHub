using Avalonia;
using Avalonia.Controls;
using Avalonia.VisualTree;
using Avalonia.Controls.Templates;
using Avalonia.Input;
using Avalonia.Headless;
using RetroHub.App.Controls;
using RetroHub.App.Input;
using Xunit;

namespace RetroHub.App.Tests;

/// <summary>Row chunking, virtualization and directional focus of <see cref="CoverGrid"/>.</summary>
public class CoverGridTests
{
    private sealed record Item(int N)
    {
        public override string ToString() => $"#{N}";
    }

    private static (Window Window, CoverGrid Grid, List<Item> Items) Create(int count, double width, double height = 700)
    {
        var items = Enumerable.Range(0, count).Select(i => new Item(i)).ToList();
        var grid = new CoverGrid
        {
            ItemWidth = 100,
            Spacing = 20,
            RowSpacing = 20,
            CoverAspect = 1.0,
            CaptionHeight = 0,
            MaxStretch = 1.0,
            ContentPadding = new Avalonia.Thickness(0),
            ItemTemplate = new FuncDataTemplate<Item>((_, _) => new Button { Focusable = true }),
            ItemsSource = items,
        };
        var window = new Window { Width = width, Height = height, Content = grid };
        window.Show();
        TestSupport.Settle(100);
        return (window, grid, items);
    }

    private static object? FocusedItem(Window w) => (w.FocusManager?.GetFocusedElement() as Control)?.DataContext;

    [Fact]
    public Task Chunks_items_into_rows_by_viewport_width() => UiTest.Run(() =>
    {
        // 600 px: (600 + 20) / (100 + 20) = 5 columns → 100 items = 20 rows
        var (window, grid, _) = Create(100, 600);
        Assert.Equal(5, grid.Columns);
        Assert.Equal(20, grid.RowCount);
        Assert.Equal(20, grid.RowsControl.ItemCount);
        var rows = grid.RowsControl.ItemsSource!.Cast<CoverRow>().ToList();
        Assert.All(rows.Take(19), r => Assert.Equal(5, r.Items.Count));
        Assert.Equal(95, ((Item)rows[19].Items[0]).N);
        window.Close();
    });

    [Fact]
    public Task Uneven_last_row_and_virtualization() => UiTest.Run(() =>
    {
        var (window, grid, _) = Create(1003, 600, height: 400);
        Assert.Equal(201, grid.RowCount);
        Assert.Equal(3, grid.RowsControl.ItemsSource!.Cast<CoverRow>().Last().Items.Count);
        // only a handful of rows are realized (virtualized), not 201
        var realized = grid.RowsControl.GetRealizedContainers().Count();
        Assert.InRange(realized, 1, 20);
        window.Close();
    });

    [Fact]
    public Task Rebuilds_only_when_the_column_count_changes_and_keeps_focus() => UiTest.Run(() =>
    {
        var (window, grid, items) = Create(100, 600);
        var rebuilds = grid.RebuildCount;

        // a few pixels wider: still 5 columns → no rebuild
        window.Width = 610;
        TestSupport.Settle(100);
        Assert.Equal(5, grid.Columns);
        Assert.Equal(rebuilds, grid.RebuildCount);

        Assert.True(grid.FocusIndex(7));
        TestSupport.Settle(50);
        Assert.Same(items[7], FocusedItem(window));

        // narrower: 3 columns → rebuild, item 7 keeps focus (now row 2, column 1)
        window.Width = 360;
        TestSupport.Settle(150);
        Assert.Equal(3, grid.Columns);
        Assert.True(grid.RebuildCount > rebuilds);
        Assert.Equal(7, grid.SelectedIndex);
        Assert.Same(items[7], FocusedItem(window));
        window.Close();
    });

    [Fact]
    public Task Directional_moves_by_item_and_row() => UiTest.Run(() =>
    {
        var (window, grid, items) = Create(23, 600); // 5 columns, rows: 5,5,5,5,3
        Assert.True(grid.FocusIndex(0));
        TestSupport.Settle(30);

        Assert.False(grid.Move(NavAction.Left));   // left edge → the shell may move focus to the sidebar
        Assert.False(grid.Move(NavAction.Up));     // top edge
        Assert.True(grid.Move(NavAction.Right));
        Assert.Equal(1, grid.SelectedIndex);
        Assert.True(grid.Move(NavAction.Down));
        Assert.Equal(6, grid.SelectedIndex);
        Assert.Same(items[6], FocusedItem(window));

        grid.FocusIndex(4);
        Assert.False(grid.Move(NavAction.Right));  // end of row
        Assert.True(grid.Move(NavAction.Down));
        Assert.Equal(9, grid.SelectedIndex);
        Assert.True(grid.Move(NavAction.Down));
        Assert.True(grid.Move(NavAction.Down));
        Assert.Equal(19, grid.SelectedIndex);
        Assert.True(grid.Move(NavAction.Down));    // last row is shorter → last item
        Assert.Equal(22, grid.SelectedIndex);
        Assert.False(grid.Move(NavAction.Down));   // bottom edge
        Assert.True(grid.Move(NavAction.Up));
        Assert.Equal(17, grid.SelectedIndex);
        window.Close();
    });

    [Fact]
    public Task Keyboard_arrows_scroll_virtualized_rows_into_view() => UiTest.Run(() =>
    {
        var (window, grid, items) = Create(500, 600, height: 300); // 100 rows, ~2 visible
        Assert.True(grid.FocusIndex(0));
        TestSupport.Settle(30);
        for (var i = 0; i < 40; i++)
        {
            window.KeyPress(Key.Down, RawInputModifiers.None, PhysicalKey.ArrowDown, null);
            TestSupport.Settle(5);
        }
        Assert.Equal(200, grid.SelectedIndex);
        Assert.Same(items[200], FocusedItem(window));
        Assert.True(grid.ScrollViewer.Offset.Y > 0);
        var tile = grid.TileForIndex(200);
        Assert.NotNull(tile);
        var top = tile!.TranslatePoint(new Avalonia.Point(0, 0), grid.ScrollViewer);
        Assert.NotNull(top);
        Assert.InRange(top!.Value.Y, -1, grid.ScrollViewer.Viewport.Height);

        window.KeyPress(Key.End, RawInputModifiers.None, PhysicalKey.End, null);
        TestSupport.Settle(30);
        Assert.Equal(499, grid.SelectedIndex);
        window.KeyPress(Key.Home, RawInputModifiers.None, PhysicalKey.Home, null);
        TestSupport.Settle(30);
        Assert.Equal(0, grid.SelectedIndex);
        window.Close();
    });

    [Fact]
    public Task Focus_request_waits_for_data() => UiTest.Run(() =>
    {
        var grid = new CoverGrid
        {
            ItemWidth = 100, Spacing = 20, CoverAspect = 1, CaptionHeight = 0, MaxStretch = 1,
            ContentPadding = new Avalonia.Thickness(0),
            ItemTemplate = new FuncDataTemplate<Item>((_, _) => new Button()),
        };
        var window = new Window { Width = 600, Height = 500, Content = grid };
        window.Show();
        TestSupport.Settle(50);
        Assert.True(grid.FocusWhenReady(0));
        var items = Enumerable.Range(0, 12).Select(i => new Item(i)).ToList();
        grid.ItemsSource = items;
        Assert.True(TestSupport.Pump(() => ReferenceEquals(FocusedItem(window), items[0]), 2000));
        window.Close();
    });

    [Fact]
    public Task Focus_stays_in_place_when_the_focused_item_leaves_the_list() => UiTest.Run(() =>
    {
        var (window, grid, items) = Create(100, 600, height: 400);
        Assert.True(grid.FocusIndex(57));
        TestSupport.Settle(50);
        var offset = grid.ScrollViewer.Offset.Y;
        Assert.True(offset > 0);
        // e.g. the game was un-favourited while the favourites filter is on → it disappears from the list
        grid.ItemsSource = items.Where(i => i.N != 57).ToList();
        TestSupport.Settle(80);
        Assert.Equal(57, grid.SelectedIndex);
        Assert.Equal(58, (FocusedItem(window) as Item)?.N);
        Assert.True(grid.ScrollViewer.Offset.Y > 0, "the grid jumped back to the top");
        window.Close();
    });
}
