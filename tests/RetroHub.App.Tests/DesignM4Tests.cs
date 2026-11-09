using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.VisualTree;
using RetroHub.App.Controls;
using RetroHub.App.Input;
using RetroHub.App.ViewModels;
using RetroHub.App.Views;
using RetroHub.App.Views.Pages;
using RetroHub.Core.Config;
using RetroHub.Core.Library;
using Xunit;

namespace RetroHub.App.Tests;

/// <summary>
/// Spiel-Detail "Die Schachtel aus dem Regal" (DESIGN 7.3, M4): the pulled box with its generated spine, the
/// Ausleihkarte, the back that is never half empty, the overlays (Back closes them first), the shelf the box came from.
/// </summary>
public class DesignM4Tests
{
    private static (MainWindow Window, MainViewModel Vm) Open()
    {
        var ctx = TestSupport.CreateContext(s => s.Motion = MotionLevel.Off);
        var vm = new MainViewModel(ctx);
        var window = new MainWindow(vm) { Width = 1400, Height = 850 };
        window.Show();
        TestSupport.Pump(() => vm.CurrentPage is HomeViewModel { IsLoaded: true });
        TestSupport.Settle(100);
        return (window, vm);
    }

    private static Control? Focused(Window w) => w.FocusManager?.GetFocusedElement() as Control;

    private static GameDetailViewModel OpenDetail(MainViewModel vm, string title)
    {
        var game = vm.Ctx.Items.Get(vm.Ctx.Services.Games.Query(new GameQuery { Text = title }).First());
        vm.Navigator.Navigate(PageKey.GameDetail, game);
        Assert.True(TestSupport.Pump(() => vm.CurrentPage is GameDetailViewModel { Game: not null } d && d.Game.Id == game.Id && d.Emulators.Count > 0, 4000));
        TestSupport.Settle(150);
        return (GameDetailViewModel)vm.CurrentPage!;
    }

    private static PlaySession S(DateTime start, long seconds) => new() { StartUtc = start, EndUtc = start.AddSeconds(seconds), Seconds = seconds };

    // ===================== Ausleihkarte =====================

    [Fact]
    public Task Card_shows_the_entry_stamp_the_sessions_oldest_first_and_waiting_places() => UiTest.Run(() =>
    {
        var detail = new GameDetailViewModel(TestSupport.CreateContext());
        var now = new DateTime(2026, 11, 9, 12, 0, 0, DateTimeKind.Utc);
        var game = new Game { SystemId = "gba", Path = "a.gba", Title = "A", AddedUtc = now.AddDays(-30) };

        // never played: the entry stamp and four empty places, nothing invented
        detail.BuildCard(game, [], now);
        Assert.Equal([CardPlaceKind.Entry, CardPlaceKind.Empty, CardPlaceKind.Empty, CardPlaceKind.Empty, CardPlaceKind.Empty],
            detail.CardPlaces.Select(p => p.Kind));
        Assert.Equal("", detail.CardSentence);

        // two sessions (the repository gives them newest first): entry, older, newer, then the empty places
        game.PlayCount = 2;
        detail.BuildCard(game, [S(now.AddDays(-1), 4320), S(now.AddDays(-3), 59)], now);
        Assert.Equal([CardPlaceKind.Entry, CardPlaceKind.Session, CardPlaceKind.Session, CardPlaceKind.Empty, CardPlaceKind.Empty],
            detail.CardPlaces.Select(p => p.Kind));
        Assert.Equal("unter 1 Min.", detail.CardPlaces[1].Note);
        Assert.Equal("1:12 Std.", detail.CardPlaces[2].Note);

        // a long history: the last five, no entry stamp, and the sentence says how many there are
        game.PlayCount = 41;
        var five = Enumerable.Range(0, 5).Select(i => S(now.AddDays(-i), 600)).ToList();
        detail.BuildCard(game, five, now);
        Assert.All(detail.CardPlaces, p => Assert.Equal(CardPlaceKind.Session, p.Kind));
        Assert.Equal("Die letzten 5 von 41 Sitzungen.", detail.CardSentence);

        // counted starts without a recorded log: no entry stamp (the card does not reach back to the start)
        game.PlayCount = 3;
        detail.BuildCard(game, [], now);
        Assert.DoesNotContain(detail.CardPlaces, p => p.IsEntry);
    });

    [Fact]
    public Task Stamp_durations_and_dates_read_like_a_stamp() => UiTest.Run(() =>
    {
        TestSupport.CreateContext(); // German texts
        Assert.Equal("unter 1 Min.", GameDetailViewModel.StampDuration(30));
        Assert.Equal("12 Min.", GameDetailViewModel.StampDuration(12 * 60 + 5));
        Assert.Equal("1:05 Std.", GameDetailViewModel.StampDuration(3900));
        var now = new DateTime(2026, 11, 9, 12, 0, 0, DateTimeKind.Utc);
        Assert.DoesNotContain("2026", GameDetailViewModel.StampDate(now.AddDays(-2), now));
        Assert.Contains("2025", GameDetailViewModel.StampDate(now.AddYears(-1), now));
    });

    [Fact]
    public void File_info_names_folder_and_crc_in_mono()
    {
        var g = new Game { SystemId = "snes", Path = "x", Title = "X", Size = 1048576, Crc32 = "04D55E12", AddedUtc = DateTime.UtcNow };
        var lines = GameDetailViewModel.BuildFileLines(g, @"I:\Spiele\SNES\X.sfc");
        Assert.Equal("X.sfc", lines[0].Value);
        Assert.Contains(lines, l => l.Value == @"I:\Spiele\SNES" && l.Mono);
        Assert.Contains(lines, l => l.Label == "CRC32" && l.Mono);
        Assert.DoesNotContain(lines, l => l.Value.Length == 0);
    }

    // ===================== the page =====================

    [Fact]
    public Task Pulled_box_has_a_spine_and_confirm_shows_the_cover_which_back_closes() => UiTest.Run(() =>
    {
        var (window, vm) = Open();
        var detail = OpenDetail(vm, "Pokémon Smaragd");
        Assert.Equal("PlayButton", Focused(window)?.Name);
        Assert.True(window.Execute(NavAction.Left));
        TestSupport.Settle(50);
        var box = Assert.IsType<GameTile>(Focused(window));
        Assert.Equal("DetailBox", box.Name);
        Assert.NotNull(box.Spine);
        Assert.True(box.Spine!.SpineRect.Width > 0, "the spine stands beside the cover");
        Assert.Equal(box.Art!.ArtRect.Height, box.Spine.SpineRect.Height, 0.5);

        // confirm on the box: the large cover, not the detail of itself again
        Assert.True(window.Execute(NavAction.Accept));
        TestSupport.Settle(80);
        Assert.Equal(DetailSheet.Cover, detail.Sheet);
        Assert.Equal(PageKey.GameDetail, vm.Navigator.Current);
        // Back closes the overlay first and the focus returns to the box; the next Back leaves the page
        Assert.True(window.Execute(NavAction.Back));
        TestSupport.Settle(80);
        Assert.Equal(DetailSheet.None, detail.Sheet);
        Assert.Equal(PageKey.GameDetail, vm.Navigator.Current);
        Assert.Same(box, Focused(window));
        window.Close();
    });

    [Fact]
    public Task More_overlay_keeps_the_focus_inside_and_offers_the_file_info() => UiTest.Run(() =>
    {
        var (window, vm) = Open();
        var detail = OpenDetail(vm, "Pokémon Smaragd");
        detail.ShowMoreCommand.Execute(null);
        TestSupport.Settle(80);
        var page = Assert.IsType<GameDetailPage>(window.PageHost.Current);
        var layer = page.FindControl<Panel>("SheetLayer")!;
        Assert.True(layer.IsVisible);
        bool Inside() => Focused(window)?.GetVisualAncestors().Contains(layer) == true;
        Assert.True(Inside(), "the focus moved into the overlay");
        for (var i = 0; i < 6; i++) window.Execute(NavAction.Down);
        Assert.True(Inside(), "the focus never leaves the overlay");
        detail.ShowFileInfoCommand.Execute(null);
        TestSupport.Settle(50);
        Assert.Equal(DetailSheet.FileInfo, detail.Sheet);
        Assert.Contains(detail.FileLines, l => l.Label == "CRC32");
        Assert.True(window.Execute(NavAction.Back));
        TestSupport.Settle(80);
        Assert.False(layer.IsVisible);
        window.Close();
    });

    [Fact]
    public Task Back_is_never_half_empty() => UiTest.Run(() =>
    {
        var (window, vm) = Open();
        // with a description: "Über das Spiel" left, "Start" and the card right
        OpenDetail(vm, "Pokémon Smaragd");
        var page = Assert.IsType<GameDetailPage>(window.PageHost.Current);
        var left = page.FindControl<StackPanel>("LeftStack")!;
        var right = page.FindControl<StackPanel>("RightStack")!;
        Assert.Equal("AboutSection", left.Children[0].Name);
        Assert.Equal(["StartSection", "CardSection"], right.Children.Select(c => c.Name));

        // nothing about the game: "Start" moves left, the card and the shelf take the right column
        var plain = vm.Ctx.Services.Games.Query(new GameQuery()).First(g => string.IsNullOrEmpty(g.Description));
        OpenDetail(vm, plain.Title);
        Assert.Equal("StartSection", left.Children[0].Name);
        Assert.Equal(["CardSection", "ShelfSection"], right.Children.Select(c => c.Name));
        Assert.True(right.Bounds.Height > 100, "the right column carries the card and the shelf");
        window.Close();
    });

    [Fact]
    public Task Shelf_shows_the_neighbours_and_the_gap_the_box_left() => UiTest.Run(() =>
    {
        var (window, vm) = Open();
        var detail = OpenDetail(vm, "Pokémon Smaragd");
        TestSupport.Settle(100);
        Assert.True(detail.HasNeighbours);
        var page = Assert.IsType<GameDetailPage>(window.PageHost.Current);
        var row = page.FindControl<StackPanel>("NeighbourRow")!;
        Assert.Single(row.Children.OfType<EmptySlot>());
        var tiles = row.Children.OfType<GameTile>().ToList();
        Assert.NotEmpty(tiles);
        Assert.All(tiles, t => Assert.Equal("gba", ((GameItemViewModel)t.DataContext!).SystemId));
        Assert.DoesNotContain(tiles, t => ((GameItemViewModel)t.DataContext!).Id == detail.Game!.Id);
        window.Close();
    });

    [Fact]
    public Task Missing_file_makes_the_solution_name_the_file_first() => UiTest.Run(() =>
    {
        var (window, vm) = Open();
        var detail = OpenDetail(vm, "Kid Icarus");
        Assert.True(detail.ShowSolution);
        Assert.Equal("Importe öffnen", detail.SolutionText);
        Assert.Equal("SolveButton", Focused(window)?.Name);
        window.Close();
    });
}
