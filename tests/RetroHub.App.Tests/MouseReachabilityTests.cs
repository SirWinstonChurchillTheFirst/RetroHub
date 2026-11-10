using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Chrome;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.VisualTree;
using RetroHub.App.Controls;
using RetroHub.App.Localization;
using RetroHub.App.ViewModels;
using RetroHub.App.Views;
using RetroHub.Core.Config;
using RetroHub.Core.Library;
using Xunit;
using SystemSpine = RetroHub.App.Controls.SystemSpine;

namespace RetroHub.App.Tests;

/// <summary>
/// Everything reachable with the mouse (R2), driven by real headless pointer input (press, release, wheel, back button)
/// with motion OFF – the worst case for the old bugs: a glide or a scroll that starts on the press then moves the target
/// away at once, and the release-time hit test of the button misses it (no click).
/// </summary>
public class MouseReachabilityTests
{
    private static (MainWindow Window, MainViewModel Vm) Open(int width = 1920, int height = 1080)
    {
        var ctx = TestSupport.CreateContext(s => s.Motion = MotionLevel.Off);
        var vm = new MainViewModel(ctx);
        var window = new MainWindow(vm) { Width = width, Height = height };
        window.Show();
        TestSupport.Pump(() => vm.CurrentPage is HomeViewModel { IsLoaded: true });
        TestSupport.Settle(200);
        return (window, vm);
    }

    private static Point Center(Window w, Visual v)
    {
        var p = v.TranslatePoint(new Point(v.Bounds.Width / 2, v.Bounds.Height / 2), w);
        Assert.True(p.HasValue, $"{v.GetType().Name} is not in the window");
        return p!.Value;
    }

    private static void Click(Window w, Point p, MouseButton button = MouseButton.Left)
    {
        w.MouseMove(p);
        w.MouseDown(p, button);
        TestSupport.Settle(30);
        w.MouseUp(p, button);
        TestSupport.Settle(120);
    }

    private static void Click(Window w, Visual v, MouseButton button = MouseButton.Left) => Click(w, Center(w, v), button);

    /// <summary>The Start shelf standing at the anchor (the others are clipped or lie under the fade).</summary>
    private static Shelf ActiveShelf(Window w)
    {
        var viewport = w.GetVisualDescendants().OfType<ShelfViewport>().First();
        return viewport.Shelves[viewport.ActiveIndex];
    }

    private static StackPanel ContextRows(Window w) => w.GetVisualDescendants().OfType<StackPanel>().First(s => s.Name == "ContextRows");

    private static GameItemViewModel AnyGame(MainViewModel vm) =>
        vm.Ctx.Items.Get(vm.Ctx.Services.Games.Query(new GameQuery()).First());

    private static void OpenDetailFromLibrary(MainViewModel vm, GameItemViewModel game)
    {
        vm.Navigator.Navigate(PageKey.Library);
        TestSupport.Pump(() => vm.CurrentPage is LibraryViewModel { IsLoaded: true }, 8000);
        vm.Navigator.Navigate(PageKey.GameDetail, game);
        TestSupport.Pump(() => vm.CurrentPage is GameDetailViewModel { Game: not null } d && d.Game.Id == game.Id, 8000);
        TestSupport.Settle(200);
    }

    // ===================== header: client area vs. the window's drag area (R3 owner test) =====================

    /// <summary>
    /// The window-chrome role at a point, resolved by Avalonia's own code – the same internal
    /// <c>IInputRoot.HitTestChromeElement</c> the Win32 backend asks on WM_NCHITTEST (nearest role above the hit visual).
    /// Headless pointer input never passes through it, which is why the old TitleBar role on the whole header (no mouse
    /// clicks on a real window) went unnoticed.
    /// </summary>
    private static WindowDecorationsElementRole? ChromeRoleAt(Window w, Point p)
    {
        const System.Reflection.BindingFlags any = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public
                                                   | System.Reflection.BindingFlags.NonPublic;
        var root = typeof(TopLevel).GetProperty("InputRoot", any)!.GetValue(w)!;
        var hitTest = typeof(IInputRoot).GetMethod("HitTestChromeElement", any)!;
        return (WindowDecorationsElementRole?)hitTest.Invoke(root, [p]);
    }

    /// <summary>What Win32 makes of a role (Avalonia 12.1.5 WindowImpl.CustomCaptionProc): User/DecorationsElement → client.</summary>
    private static bool IsClient(WindowDecorationsElementRole? role) =>
        role is WindowDecorationsElementRole.User or WindowDecorationsElementRole.DecorationsElement;

    [Fact]
    public Task Every_header_button_is_client_area_and_only_bare_header_space_drags_the_window() => UiTest.Run(() =>
    {
        foreach (var (width, height) in new[] { (1920, 1080), (1100, 700) })
        {
            var (w, vm) = Open(width, height);
            var top = w.GetVisualDescendants().OfType<Grid>().First(g => g.Name == "TopBar");
            var buttons = top.GetVisualDescendants().OfType<Button>().Where(b => b.IsEffectivelyVisible).ToList();
            Assert.True(buttons.Count >= 7, $"{buttons.Count} header buttons"); // 3 channels, search, 3 tools
            foreach (var b in buttons)
            {
                // the centre and points 2 px inside each corner: the whole button, including the part in the 40 px band
                var probes = new[]
                {
                    new Point(b.Bounds.Width / 2, b.Bounds.Height / 2), new Point(2, 2), new Point(b.Bounds.Width - 2, 2),
                    new Point(2, b.Bounds.Height - 2), new Point(b.Bounds.Width - 2, b.Bounds.Height - 2),
                };
                foreach (var probe in probes)
                {
                    var p = b.TranslatePoint(probe, w)!.Value;
                    var role = ChromeRoleAt(w, p);
                    Assert.True(IsClient(role), $"{b.GetValue(Avalonia.Automation.AutomationProperties.NameProperty)} at {p} ({width}): role {role?.ToString() ?? "none"}");
                }
                // and no drag role anywhere above it (the root cause of the R3 owner report)
                Assert.DoesNotContain(b.GetVisualAncestors().OfType<Visual>(),
                    a => WindowDecorationProperties.GetElementRole(a) == WindowDecorationsElementRole.TitleBar);
            }
            // the bare space between the channels and the search, and the word mark, still drag the window
            var channels = w.GetVisualDescendants().OfType<ItemsControl>().First(i => i.Name == "ChannelList");
            var search = w.GetVisualDescendants().OfType<Button>().First(b => b.Name == "SearchButton");
            var gapX = (channels.TranslatePoint(new Point(channels.Bounds.Width, 0), w)!.Value.X + search.TranslatePoint(default, w)!.Value.X) / 2;
            Assert.Equal(WindowDecorationsElementRole.TitleBar, ChromeRoleAt(w, new Point(gapX, 20)));
            Assert.Equal(WindowDecorationsElementRole.TitleBar, ChromeRoleAt(w, new Point(gapX, 50)));
            var brand = w.GetVisualDescendants().OfType<StackPanel>().First(s => s.Name == "BrandBar");
            Assert.Equal(WindowDecorationsElementRole.TitleBar, ChromeRoleAt(w, Center(w, brand)));
            Assert.Equal(WindowDecorationsElementRole.TitleBar, ChromeRoleAt(w, new Point(10, 10))); // left of the word mark
            // the caption buttons keep their system roles
            Assert.Equal(WindowDecorationsElementRole.CloseButton, ChromeRoleAt(w, Center(w, w.FindControl<Button>("CloseButton")!)));
            // the page below the header is plain client area
            Assert.Null(ChromeRoleAt(w, new Point(width / 2.0, height / 2.0)));
            w.Close();
        }
    });

    [Fact]
    public Task Every_header_button_navigates_on_a_click() => UiTest.Run(() =>
    {
        var (w, vm) = Open();
        Button Tool(PageKey key) => w.GetVisualDescendants().OfType<Button>()
            .First(b => b.DataContext is NavItemViewModel n && n.Key == key && b.IsEffectivelyVisible);
        foreach (var (key, expected) in new (PageKey, Type)[]
                 {
                     (PageKey.Library, typeof(LibraryViewModel)), (PageKey.Systems, typeof(SystemsViewModel)),
                     (PageKey.Imports, typeof(ImportsViewModel)), (PageKey.Emulators, typeof(EmulatorsViewModel)),
                     (PageKey.Settings, typeof(SettingsViewModel)), (PageKey.Home, typeof(HomeViewModel)),
                 })
        {
            Click(w, Tool(key));
            TestSupport.Pump(() => vm.CurrentPage?.GetType() == expected, 3000);
            Assert.IsType(expected, vm.CurrentPage);
        }
        Click(w, w.GetVisualDescendants().OfType<Button>().First(b => b.Name == "SearchButton"));
        TestSupport.Pump(() => vm.CurrentPage is LibraryViewModel, 3000);
        Assert.IsType<LibraryViewModel>(vm.CurrentPage);
        w.Close();
    });

    // ===================== back: mouse button, footer hint, breadcrumb =====================

    [Fact]
    public Task The_mouse_back_button_goes_back() => UiTest.Run(() =>
    {
        var (w, vm) = Open();
        OpenDetailFromLibrary(vm, AnyGame(vm));
        w.MouseDown(new Point(900, 500), MouseButton.XButton1);
        w.MouseUp(new Point(900, 500), MouseButton.XButton1);
        TestSupport.Pump(() => vm.CurrentPage is LibraryViewModel, 3000);
        Assert.IsType<LibraryViewModel>(vm.CurrentPage);
        w.Close();
    });

    [Fact]
    public Task Footer_hints_are_mouse_targets_and_Back_never_drops_out() => UiTest.Run(() =>
    {
        var (w, vm) = Open(1200, 760); // narrow: at most three hints, "Zurück" among them
        OpenDetailFromLibrary(vm, AnyGame(vm));
        Assert.Contains(vm.VisibleHints, h => h.Action == Input.NavAction.Back);
        var back = w.GetVisualDescendants().OfType<Button>()
            .First(b => b.DataContext is HintItem { Action: Input.NavAction.Back } && b.IsEffectivelyVisible);
        Assert.False(back.Focusable); // the controller focus stays on the page
        Click(w, back);
        TestSupport.Pump(() => vm.CurrentPage is LibraryViewModel, 3000);
        Assert.IsType<LibraryViewModel>(vm.CurrentPage);
        // "Öffnen/Starten" acts on the focused object: not a mouse target
        Assert.All(w.GetVisualDescendants().OfType<Button>().Where(b => b.DataContext is HintItem { Action: Input.NavAction.Accept }),
            b => Assert.False(b.IsHitTestVisible));
        w.Close();
    });

    [Fact]
    public Task The_section_word_in_the_footer_leads_back_up() => UiTest.Run(() =>
    {
        var (w, vm) = Open();
        var game = AnyGame(vm);
        vm.Navigator.Navigate(PageKey.Systems);
        TestSupport.Pump(() => vm.CurrentPage is SystemsViewModel { IsLoaded: true }, 8000);
        vm.Navigator.Navigate(PageKey.System, game.SystemId);
        TestSupport.Pump(() => vm.CurrentPage is SystemViewModel { IsLoaded: true }, 8000);
        vm.Navigator.Navigate(PageKey.GameDetail, game);
        TestSupport.Pump(() => vm.CurrentPage is GameDetailViewModel { Game: not null }, 8000);
        TestSupport.Settle(200);
        var crumb = w.GetVisualDescendants().OfType<Button>().First(b => b.Name == "LocationParentButton");
        Assert.True(crumb.IsHitTestVisible);
        Click(w, crumb);
        TestSupport.Pump(() => vm.CurrentPage is SystemsViewModel, 3000);
        Assert.IsType<SystemsViewModel>(vm.CurrentPage); // two steps up, to the section
        TestSupport.Settle(100);
        Assert.False(crumb.IsHitTestVisible); // a top-level page: nothing to go up to
        w.Close();
    });

    // ===================== Start: shelves =====================

    [Fact]
    public Task A_box_far_from_the_anchor_opens_with_one_click_and_the_row_does_not_glide_on_the_press() => UiTest.Run(() =>
    {
        var (w, vm) = Open();
        var shelf = ActiveShelf(w);
        var tiles = shelf.Items().OfType<GameTile>().ToList();
        Assert.True(tiles.Count >= 4, $"the demo shelf has {tiles.Count} boxes");
        var target = tiles[3]; // right of the anchor: focusing it with a key would glide the row
        var game = (GameItemViewModel)target.DataContext!;
        var before = shelf.Offset;
        var p = Center(w, target);
        w.MouseMove(p);
        w.MouseDown(p, MouseButton.Left);
        TestSupport.Settle(60);
        Assert.Equal(before, shelf.Offset); // the press focused it, the row stayed
        w.MouseUp(p, MouseButton.Left);
        TestSupport.Pump(() => vm.CurrentPage is GameDetailViewModel { Game: not null } d && d.Game.Id == game.Id, 3000);
        Assert.IsType<GameDetailViewModel>(vm.CurrentPage);
        w.Close();
    });

    [Fact]
    public Task The_dark_below_the_active_shelf_hides_nothing_clickable_and_brings_the_next_shelf_up() => UiTest.Run(() =>
    {
        var (w, vm) = Open();
        var fade = w.GetVisualDescendants().OfType<StackPanel>().First(s => s.Name == "ShelfFade");
        var viewport = w.GetVisualDescendants().OfType<ShelfViewport>().First();
        var active = viewport.ActiveIndex;
        var top = fade.TranslatePoint(new Point(200, 60), w)!.Value;
        Click(w, top);
        Assert.IsType<HomeViewModel>(vm.CurrentPage); // no invisible box opened
        Assert.Equal(active + 1, viewport.ActiveIndex);
        w.Close();
    });

    [Fact]
    public Task Shift_wheel_and_the_Winkel_plates_move_an_overflowing_row() => UiTest.Run(() =>
    {
        var (w, vm) = Open(1100, 700);
        var shelf = ActiveShelf(w);
        var p = Center(w, shelf.Items()[0]);
        w.MouseMove(p);
        TestSupport.Settle(60);
        w.MouseWheel(p, new Vector(0, -1), RawInputModifiers.Shift);
        TestSupport.Settle(60);
        Assert.True(shelf.Offset < 0, $"offset {shelf.Offset}");
        Assert.IsType<HomeViewModel>(vm.CurrentPage);
        var edges = shelf.GetVisualDescendants().OfType<ShelfEdge>().ToList();
        Assert.Equal(2, edges.Count);
        var back = edges.First(e => e.Direction < 0);
        Assert.True(back.IsHitTestVisible && back.IsLive, $"hit-test {back.IsHitTestVisible}, live {back.IsLive}, pointer over the shelf {shelf.IsPointerOver}");
        var offset = shelf.Offset;
        Click(w, back);
        Assert.True(shelf.Offset > offset);
        w.Close();
    });

    // ===================== Konsolen =====================

    [Fact]
    public Task Konsolen_first_click_turns_the_front_second_opens_the_system() => UiTest.Run(() =>
    {
        var (w, vm) = Open();
        vm.Navigator.Navigate(PageKey.Systems);
        TestSupport.Pump(() => vm.CurrentPage is SystemsViewModel { IsLoaded: true }, 8000);
        TestSupport.Pump(() => w.FocusManager?.GetFocusedElement() is SystemSpine, 3000);
        TestSupport.Settle(200);
        var wall = w.GetVisualDescendants().OfType<Shelf>().First(s => s.Name == "Wall");
        var spine = wall.Items().OfType<SystemSpine>().Skip(1).First(s => !s.IsOpen && s.Bounds.Width > 0);
        var system = (SystemItemViewModel)spine.DataContext!;
        Click(w, spine);
        Assert.IsType<SystemsViewModel>(vm.CurrentPage); // the first click only turned it forward
        TestSupport.Pump(() => spine.IsOpen, 2000);
        Assert.True(spine.IsOpen);
        Assert.Same(system, ((SystemsViewModel)vm.CurrentPage!).Current);
        Click(w, spine);
        TestSupport.Pump(() => vm.CurrentPage is SystemViewModel, 3000);
        Assert.Equal(system.Id, (vm.CurrentPage as SystemViewModel)?.System?.Id ?? system.Id);
        Assert.IsType<SystemViewModel>(vm.CurrentPage);
        w.Close();
    });

    [Fact]
    public Task Konsolen_wheel_steps_through_the_systems() => UiTest.Run(() =>
    {
        var (w, vm) = Open();
        vm.Navigator.Navigate(PageKey.Systems);
        TestSupport.Pump(() => vm.CurrentPage is SystemsViewModel { IsLoaded: true }, 8000);
        TestSupport.Pump(() => w.FocusManager?.GetFocusedElement() is SystemSpine, 3000);
        var page = (SystemsViewModel)vm.CurrentPage!;
        var first = page.Current;
        var wall = w.GetVisualDescendants().OfType<Shelf>().First(s => s.Name == "Wall");
        var p = Center(w, wall);
        w.MouseWheel(p, new Vector(0, -1));
        TestSupport.Settle(120);
        Assert.NotSame(first, page.Current);
        w.Close();
    });

    // ===================== right click, list view, folds, settings =====================

    [Fact]
    public Task Right_click_on_a_box_opens_the_in_style_list_and_a_click_beside_it_closes_it() => UiTest.Run(() =>
    {
        var (w, vm) = Open();
        vm.Navigator.Navigate(PageKey.Library);
        TestSupport.Pump(() => vm.CurrentPage is LibraryViewModel { IsLoaded: true, Items.Count: > 0 }, 8000);
        TestSupport.Settle(300);
        var tile = w.GetVisualDescendants().OfType<GameTile>().First(t => t.IsEffectivelyVisible && t.Bounds.Width > 0);
        var game = (GameItemViewModel)tile.DataContext!;
        var favourite = game.IsFavorite;
        Click(w, tile, MouseButton.Right);
        Assert.True(w.IsContextOpen);
        Assert.IsType<LibraryViewModel>(vm.CurrentPage); // the right click neither opened nor focused the box
        var rows = ContextRows(w).Children.OfType<Button>().ToList();
        Assert.True(rows.Count >= 3);
        Click(w, new Point(20, 1000 - 200)); // beside the list
        Assert.False(w.IsContextOpen);
        Assert.IsType<LibraryViewModel>(vm.CurrentPage);
        // again, and the favourite row
        Click(w, tile, MouseButton.Right);
        var fav = ContextRows(w).Children.OfType<Button>()
            .First(b => b.GetVisualDescendants().OfType<TextBlock>().Any(t => t.Text == Loc.T(favourite ? "hint.unfavorite" : "hint.favorite")));
        Click(w, fav);
        TestSupport.Pump(() => game.IsFavorite != favourite, 3000);
        Assert.NotEqual(favourite, game.IsFavorite);
        Assert.False(w.IsContextOpen);
        w.Close();
    });

    [Fact]
    public Task In_the_list_the_first_click_selects_the_second_opens() => UiTest.Run(() =>
    {
        var (w, vm) = Open();
        vm.Navigator.Navigate(PageKey.Library);
        TestSupport.Pump(() => vm.CurrentPage is LibraryViewModel { IsLoaded: true, Items.Count: > 2 }, 8000);
        var lib = (LibraryViewModel)vm.CurrentPage!;
        lib.IsListView = true;
        TestSupport.Settle(400);
        var rows = w.GetVisualDescendants().OfType<CoverGrid>().First(g => g.Name == "Rows");
        var row = rows.GetVisualDescendants().OfType<GameTile>().Where(t => t.IsEffectivelyVisible && t.Bounds.Height > 0).Skip(2).First();
        var game = (GameItemViewModel)row.DataContext!;
        Click(w, row);
        Assert.IsType<LibraryViewModel>(vm.CurrentPage);
        Assert.Same(game, lib.Items[rows.SelectedIndex]);
        Click(w, row);
        TestSupport.Pump(() => vm.CurrentPage is GameDetailViewModel, 3000);
        Assert.IsType<GameDetailViewModel>(vm.CurrentPage);
        lib.IsListView = false;
        w.Close();
    });

    [Fact]
    public Task An_open_choice_list_closes_on_its_row_and_on_a_click_beside_it() => UiTest.Run(() =>
    {
        var (w, vm) = Open();
        OpenDetailFromLibrary(vm, AnyGame(vm));
        var detail = (GameDetailViewModel)vm.CurrentPage!;
        var box = w.GetVisualDescendants().OfType<Button>().First(b => b.Name == "EmulatorBox");
        box.BringIntoView();
        TestSupport.Settle(200);
        Click(w, box);
        Assert.Equal(DetailSheet.Choice, detail.Sheet);
        Click(w, box);
        Assert.Equal(DetailSheet.None, detail.Sheet); // the row again closes it
        Click(w, box);
        Assert.Equal(DetailSheet.Choice, detail.Sheet);
        // a click on plain text of the page (the game's title): the list closes, nothing else happens
        var page = w.GetVisualDescendants().OfType<Views.Pages.GameDetailPage>().First();
        var plain = page.GetVisualDescendants().OfType<TextBlock>().First(t => t.IsEffectivelyVisible && t.Bounds.Width > 40
            && t.FindAncestorOfType<Button>() == null && t.FindAncestorOfType<FoldPanel>() == null && !string.IsNullOrEmpty(t.Text));
        Click(w, plain);
        Assert.IsType<GameDetailViewModel>(vm.CurrentPage);
        Assert.Equal(DetailSheet.None, detail.Sheet);
        w.Close();
    });

    [Fact]
    public Task A_settings_arrow_click_changes_the_value() => UiTest.Run(() =>
    {
        var (w, vm) = Open();
        vm.Navigator.Navigate(PageKey.Settings);
        TestSupport.Pump(() => vm.CurrentPage is SettingsViewModel, 3000);
        TestSupport.Settle(300);
        var row = w.GetVisualDescendants().OfType<ServiceRow>().First(r => r.Setting?.Label == Loc.T("settings.exitCombo"));
        row.BringIntoView();
        TestSupport.Settle(200);
        var before = row.Setting!.Index;
        var next = row.GetVisualDescendants().OfType<Border>().First(b => b.Name == (row.Setting.CanNext ? "PART_Next" : "PART_Previous"));
        Click(w, next);
        Assert.NotEqual(before, row.Setting.Index);
        w.Close();
    });

    [Fact]
    public Task A_click_on_the_dim_around_a_dialog_cancels_it() => UiTest.Run(() =>
    {
        var (w, vm) = Open();
        var task = vm.Dialogs.ConfirmAsync("Titel", "Text", "Ja");
        TestSupport.Settle(200);
        Assert.NotNull(vm.Dialogs.Current);
        Click(w, new Point(30, 540));
        TestSupport.Pump(() => task.IsCompleted, 2000);
        Assert.True(task.IsCompleted);
        Assert.False(task.Result);
        w.Close();
    });
}
