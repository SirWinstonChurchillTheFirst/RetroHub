using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.VisualTree;
using RetroHub.App.Controls;
using RetroHub.App.Input;
using RetroHub.App.Theming;
using RetroHub.App.ViewModels;
using RetroHub.App.Views;
using RetroHub.Core.Config;
using RetroHub.Core.Library;
using Xunit;

namespace RetroHub.App.Tests;

/// <summary>
/// Werkbank & Start (DESIGN 7.6, 9.3, M6): the service menu instead of stock toggles/sliders/drop-downs, Emulatoren by
/// the library's systems, Importe with the delivery note, the in-style choice list of the detail, the power-on
/// transition and the removal of the old theme.
/// </summary>
public class DesignM6Tests
{
    private static (MainWindow Window, MainViewModel Vm) Open(int width = 1600, int height = 900, MotionLevel motion = MotionLevel.Off)
    {
        var ctx = TestSupport.CreateContext(s => s.Motion = motion);
        var vm = new MainViewModel(ctx);
        var window = new MainWindow(vm) { Width = width, Height = height };
        window.Show();
        TestSupport.Pump(() => vm.CurrentPage is HomeViewModel { IsLoaded: true });
        TestSupport.Settle(100);
        return (window, vm);
    }

    private static Control? Focused(Window w) => w.FocusManager?.GetFocusedElement() as Control;

    // ===================== the service menu =====================

    [Fact]
    public void A_service_row_wraps_its_choices_and_stops_at_the_ends_of_ordered_values()
    {
        var seen = new List<bool>();
        var onOff = ServiceSetting.Switch("Kanalanzeige", null, true, seen.Add, "An", "Aus");
        Assert.Equal("An", onOff.ValueText);
        Assert.True(onOff.Step(+1));
        Assert.Equal("Aus", onOff.ValueText);
        Assert.True(onOff.Step(+1)); // a choice wraps round
        Assert.Equal("An", onOff.ValueText);
        onOff.Activate(); // confirm moves on
        Assert.Equal("Aus", onOff.ValueText);
        Assert.Equal([false, true, false], seen);

        var applied = 0;
        var seconds = ServiceSetting.Choice("Zurück zu RetroHub", null,
            [new ServiceValue(0.0, "Aus"), new ServiceValue(1.5, "1,5 s"), new ServiceValue(3.0, "3,0 s")], 2, _ => applied++, wraps: false);
        Assert.False(seconds.CanNext);
        Assert.False(seconds.Step(+1)); // an ordered value stops at its end …
        Assert.Equal("3,0 s", seconds.ValueText);
        Assert.True(seconds.Step(-1));
        Assert.Equal("1,5 s", seconds.ValueText);
        seconds.Activate();
        seconds.Activate(); // … but confirm still moves on round
        Assert.Equal("Aus", seconds.ValueText);
        Assert.Equal(3, applied);

        var opened = 0;
        var action = ServiceSetting.Action("Spiele", "games/", "Öffnen", () => opened++);
        Assert.False(action.Step(+1));
        action.Activate();
        Assert.Equal(1, opened);
        Assert.True(action.IsAction);

        var fixedRow = ServiceSetting.Choice("games/", null, [new ServiceValue(true, "Immer aktiv")], 0, _ => applied++);
        Assert.True(fixedRow.IsFixed);
        Assert.False(fixedRow.Step(+1));
    }

    [Fact]
    public Task Settings_are_a_service_menu_driven_by_left_and_right() => UiTest.Run(() =>
    {
        var (window, vm) = Open();
        try
        {
            vm.Navigator.Navigate(PageKey.Settings);
            Assert.True(TestSupport.Pump(() => Focused(window) is ServiceRow, 3000));
            var page = window.CurrentPageView!;
            // no stock switch, slider or drop-down anywhere on the page
            Assert.Empty(page.GetVisualDescendants().Where(v => v is ToggleSwitch or Slider or ComboBox or ProgressBar));

            var row = (ServiceRow)Focused(window)!;
            Assert.Equal("Lichtfarbe", row.Setting!.Label);
            window.Execute(NavAction.Right); // the next light colour, at once and saved
            Assert.Equal(Color.Parse("#8FE3A0"), ThemeManager.CurrentAccent);
            Assert.Equal("#8FE3A0", vm.Ctx.Services.Settings.Current.Accent);
            Assert.Same(row, Focused(window)); // left/right never move the focus on a value row
            window.Execute(NavAction.Left);
            Assert.Equal(Color.Parse("#FFB547"), ThemeManager.CurrentAccent);
            Assert.Null(vm.Ctx.Services.Settings.Current.Accent);

            // down walks the one list; confirm moves a value on
            window.Execute(NavAction.Down);
            window.Execute(NavAction.Down);
            Assert.Equal("Kanalanzeige", ((ServiceRow)Focused(window)!).Setting!.Label);
            window.Execute(NavAction.Accept);
            Assert.False(vm.Ctx.Services.Ui.Current.ShowOsd);
            TestSupport.Pump(() => !vm.ShowOsd, 1000);
            Assert.False(vm.ShowOsd);
            window.Execute(NavAction.Accept);
            Assert.True(vm.Ctx.Services.Ui.Current.ShowOsd);

            // the shoulder jumps to "Steuerung"; "Zurück zu RetroHub" stops at 3 s
            window.Execute(NavAction.PageRight);
            Assert.Equal("Mit Controller bedienen", ((ServiceRow)Focused(window)!).Setting!.Label);
            window.Execute(NavAction.Down);
            for (var i = 0; i < 10; i++) window.Execute(NavAction.Right);
            Assert.Equal(3.0, vm.Ctx.Services.Settings.Current.ExitComboSeconds);
            for (var i = 0; i < 10; i++) window.Execute(NavAction.Left);
            Assert.Equal(0.0, vm.Ctx.Services.Settings.Current.ExitComboSeconds);
        }
        finally
        {
            ThemeManager.ApplyAccent(null);
            window.Close();
        }
    });

    [Fact]
    public Task The_werkbank_head_never_scrolls_and_a_detail_from_there_belongs_to_the_library() => UiTest.Run(() =>
    {
        var (window, vm) = Open(1920, 1080);
        try
        {
            foreach (var key in new[] { PageKey.Settings, PageKey.Emulators, PageKey.Imports })
            {
                vm.Navigator.Navigate(key);
                TestSupport.Settle(300);
                var page = window.CurrentPageView!;
                var title = page.GetVisualDescendants().OfType<TextBlock>().First(t => t.Classes.Contains("poster"));
                var before = title.TranslatePoint(default, window)!.Value;
                var scroller = page.GetVisualDescendants().OfType<ScrollViewer>().First(s => s.Name == "Scroller");
                scroller.Offset = new Vector(0, 400);
                TestSupport.Settle(100);
                Assert.Equal(before, title.TranslatePoint(default, window)!.Value); // pinned: never cut under the header
                // the first section label stands at the same height on every page
                var label = page.GetVisualDescendants().OfType<ShelfLabel>().First(l => l.IsEffectivelyVisible);
                scroller.Offset = default;
                TestSupport.Settle(100);
                Assert.InRange(label.TranslatePoint(default, window)!.Value.Y, 205, 219);
            }
            // a game opened from a Werkbank page lives in the library
            var game = vm.Ctx.Items.Get(vm.Ctx.Services.Games.Query(new GameQuery { Text = "Smaragd" }).Single());
            vm.Navigator.Navigate(PageKey.GameDetail, game);
            TestSupport.Settle(200);
            Assert.Equal(RetroHub.App.Localization.Loc.T("nav.library"), vm.LocationParent);
        }
        finally { window.Close(); }
    });

    // ===================== Emulatoren =====================

    [Fact]
    public Task Emulators_are_listed_by_the_systems_of_the_library() => UiTest.Run(() =>
    {
        var (window, vm) = Open();
        try
        {
            vm.Navigator.Navigate(PageKey.Emulators);
            Assert.True(TestSupport.Pump(() => vm.CurrentPage is EmulatorsViewModel { Rows.Count: > 0 }, 3000));
            var emus = (EmulatorsViewModel)vm.CurrentPage!;
            var games = vm.Ctx.Services.Games.Stats().GamesPerSystem;
            Assert.All(emus.Rows, r => Assert.True(games.GetValueOrDefault(r.System!.Id) > 0, r.System.Id));
            // the demo's N64 has RetroArch without the core, the GameCube nothing: both say so in one sentence
            var n64 = emus.Rows.Single(r => r.System!.Id == "n64");
            Assert.True(n64.NotReady);
            Assert.Contains("Core", n64.MissingText);
            Assert.True(n64.HasChoices);
            Assert.True(emus.Rows.Single(r => r.System!.Id == "gba").Ready);

            // confirm folds the focused row open, the focus stays on it
            TestSupport.Pump(() => Focused(window) is Button { Classes: var c } && c.Contains("sysrow"), 2000);
            var header = Focused(window)!;
            var first = (EmulatorRowViewModel)header.DataContext!;
            window.Execute(NavAction.Accept);
            Assert.True(first.IsOpen);
            Assert.Same(header, Focused(window));
            window.Execute(NavAction.Accept);
            Assert.False(first.IsOpen);

            // from a game's "Emulator einrichten": only that system, opened
            vm.Navigator.Navigate(PageKey.Emulators, "gc");
            TestSupport.Settle(200);
            Assert.True(emus.IsFiltered);
            Assert.Single(emus.Rows);
            Assert.True(emus.Rows[0].IsOpen);
        }
        finally { window.Close(); }
    });

    // ===================== Importe =====================

    [Fact]
    public Task A_scan_leaves_a_delivery_note_instead_of_a_toast() => UiTest.Run(() =>
    {
        var (window, vm) = Open();
        try
        {
            vm.Navigator.Navigate(PageKey.Imports);
            var imports = (ImportsViewModel)vm.CurrentPage!;
            var toasts = vm.Ctx.Toasts.Items.Count;
            imports.ScanCommand.Execute(null);
            Assert.True(TestSupport.Pump(() => imports.HasReport, 15_000));
            Assert.Equal(toasts, vm.Ctx.Toasts.Items.Count);
            var r = imports.Report!;
            Assert.Equal(r.Added + r.Updated + r.Unchanged + r.Duplicates.Count + r.Unknown.Count + r.Errors.Count, imports.Delivery[0].Count);
            Assert.Equal(r.Duplicates.Count + r.Unknown.Count + r.Errors.Count, imports.IssueCount);
            Assert.True(imports.HasIssues);
            // the note stands until the next scan begins
            imports.ScanCommand.Execute(null);
            Assert.True(TestSupport.Pump(() => imports.IsScanning, 2000));
            Assert.False(imports.HasReport);
            Assert.True(TestSupport.Pump(() => imports.HasReport, 15_000));
        }
        finally { window.Close(); }
    });

    [Fact]
    public Task A_folder_row_switches_with_left_and_right_and_opens_its_list_on_confirm() => UiTest.Run(() =>
    {
        var (window, vm) = Open();
        try
        {
            vm.Navigator.Navigate(PageKey.Imports);
            TestSupport.Settle(300);
            var imports = (ImportsViewModel)vm.CurrentPage!;
            var row = window.GetVisualDescendants().OfType<ServiceRow>().First(r => r.Classes.Contains("folder") && r.Setting?.IsFixed == false);
            var folder = imports.Folders.First(f => ReferenceEquals(f.Row, row.Setting));
            var before = folder.Folder.Enabled;
            row.Focus(Avalonia.Input.NavigationMethod.Directional);
            window.Execute(NavAction.Right);
            Assert.Equal(!before, vm.Ctx.Services.Games.Folders().Single(f => f.Id == folder.Folder.Id).Enabled);
            Assert.Same(row, Focused(window));

            window.Execute(NavAction.Accept);
            Assert.True(imports.IsMenuOpen);
            Assert.True(TestSupport.Pump(() => Focused(window) is Button b && IsIn(b, "MenuLayer"), 2000));
            window.Execute(NavAction.Back);
            Assert.False(imports.IsMenuOpen);
            Assert.True(TestSupport.Pump(() => ReferenceEquals(Focused(window), row), 2000));
            window.Execute(NavAction.Left);
            Assert.Equal(before, vm.Ctx.Services.Games.Folders().Single(f => f.Id == folder.Folder.Id).Enabled);
        }
        finally { window.Close(); }
    });

    private static bool IsIn(Visual v, string name)
    {
        for (Visual? x = v; x != null; x = x.GetVisualParent())
            if (x is Control { Name: { } n } && n == name) return true;
        return false;
    }

    // ===================== the detail's choice list =====================

    [Fact]
    public Task The_detail_chooses_emulator_and_mode_from_its_own_list() => UiTest.Run(() =>
    {
        var (window, vm) = Open();
        try
        {
            var game = vm.Ctx.Items.Get(vm.Ctx.Services.Games.Query(new GameQuery { Text = "Smaragd" }).Single());
            vm.Navigator.Navigate(PageKey.GameDetail, game);
            Assert.True(TestSupport.Pump(() => vm.CurrentPage is GameDetailViewModel { Emulators.Count: > 1 }, 5000));
            var detail = (GameDetailViewModel)vm.CurrentPage!;
            Assert.Empty(window.CurrentPageView!.GetVisualDescendants().OfType<ComboBox>());

            detail.ShowEmulatorChoiceCommand.Execute(null);
            Assert.Equal(DetailSheet.Choice, detail.Sheet);
            Assert.Single(detail.ChoiceItems, c => c.IsCurrent);
            // the list opens on the current choice
            Assert.True(TestSupport.Pump(() => Focused(window) is Button { Classes: var c } && c.Contains("current"), 2000));
            var other = detail.ChoiceItems.First(c => !c.IsCurrent);
            detail.PickCommand.Execute(other);
            Assert.Equal(DetailSheet.None, detail.Sheet);
            Assert.Same(other.Value, detail.SelectedEmulator);
            var stored = ((EmulatorOption)other.Value).Ref;
            Assert.True(TestSupport.Pump(() => vm.Ctx.Services.Games.Get(game.Id)?.EmulatorOverride == stored, 3000));

            // Back closes the list without a change
            detail.ShowModeChoiceCommand.Execute(null);
            var mode = detail.SelectedMode;
            window.Execute(NavAction.Back);
            Assert.Equal(DetailSheet.None, detail.Sheet);
            Assert.Same(mode, detail.SelectedMode);
            detail.ResetLaunchOptionsCommand.Execute(null);
        }
        finally { window.Close(); }
    });

    // ===================== the power-on =====================

    [Fact]
    public Task The_power_on_flies_the_box_and_drops_the_flash_under_reduced_motion() => UiTest.Run(() =>
    {
        var (window, vm) = Open();
        try
        {
            var game = vm.Ctx.Items.Get(vm.Ctx.Services.Games.Query(new GameQuery { Text = "Smaragd" }).Single());
            vm.Navigator.Navigate(PageKey.GameDetail, game);
            TestSupport.Pump(() => vm.CurrentPage is GameDetailViewModel { Game: not null }, 5000);
            TestSupport.Settle(400);
            var tile = window.CurrentPageView!.GetVisualDescendants().OfType<GameTile>().First(t => t.Name == "DetailBox");
            var art = tile.GetVisualDescendants().OfType<CoverArt>().First();
            var flight = window.FlightLayerControl;
            var light = SystemPalette.Get(game.SystemId, game.Accent).Light;

            flight.PowerOn(art, light, EffectiveMotion.Full);
            Assert.Equal(0, tile.Opacity); // the original box stays in place, unseen
            // the copy becomes the start surface's box: the same bitmap, not a second placeholder (M6 QA)
            Assert.NotNull(flight.LandingImage?.Source);
            Assert.True(flight.LandingImage!.IsVisible);
            Assert.False(flight.LandingArt!.IsVisible);
            flight.Freeze(FlightLayer.LineEnd + 5);
            Assert.True(flight.FlashShown); // one frame of overexposure
            flight.Cancel();
            Assert.Equal(1, tile.Opacity);
            Assert.Null(flight.LandingImage.Source);
            Assert.True(flight.LandingArt.IsVisible);

            flight.PowerOn(art, light, EffectiveMotion.Reduced);
            Assert.Equal(1, tile.Opacity); // reduced: no flight, the box stays
            foreach (var ms in new[] { 0.0, 50, 100, 150, 330 })
                flight.Freeze(ms);
            Assert.False(flight.FlashShown);
            flight.Cancel();

            flight.PowerOff(light, EffectiveMotion.Full);
            flight.Freeze(80);
            Assert.True(flight.IsActive);
            flight.Clear();
            Assert.False(flight.IsActive);
        }
        finally { window.Close(); }
    });

    // ===================== the old theme is gone =====================

    [Fact]
    public void The_old_theme_and_the_stock_controls_are_gone()
    {
        var fonts = Path.Combine(TestSupport.SourceDir, "Assets", "Fonts");
        Assert.Empty(Directory.GetFiles(fonts, "Poppins*", SearchOption.AllDirectories));
        var offenders = new List<string>();
        foreach (var file in Directory.EnumerateFiles(TestSupport.SourceDir, "*.*", SearchOption.AllDirectories)
                     .Where(f => (f.EndsWith(".axaml") || f.EndsWith(".cs")) && !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")))
        {
            var text = File.ReadAllText(file);
            foreach (var bad in new[] { "<ComboBox", "<ToggleSwitch", "<Slider", "<ProgressBar", "RhGlass", "StatTile", "Border.glass", "Poppins", "RhAccentGradient", "GlyphStyle" })
                if (text.Contains(bad, StringComparison.Ordinal)) offenders.Add($"{Path.GetFileName(file)}: {bad}");
        }
        Assert.True(offenders.Count == 0, string.Join("\n", offenders));
    }
}
