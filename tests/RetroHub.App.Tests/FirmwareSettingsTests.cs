using System.Text;
using Avalonia.Controls;
using Avalonia.VisualTree;
using RetroHub.App.Controls;
using RetroHub.App.Localization;
using RetroHub.App.ViewModels;
using RetroHub.Core.Emulation.Firmware;
using Xunit;

namespace RetroHub.App.Tests;

/// <summary>
/// Einstellungen › Firmware &amp; Keys: one row per console, states as descriptions, import from the pickers and the
/// controller's bios/import folder. FAKE files only (made-up key lines), in the test's scratch root.
/// </summary>
public sealed class FirmwareSettingsTests
{
    private static string FakeProdKeys() => "header_key = " + new string('0', 64) + "\nmaster_key_00 = " + new string('1', 32) + "\n";

    /// <summary>Demo services (scratch root) with the REAL built-in catalog, so every console of the release appears.</summary>
    private static UiContext RealCatalogContext()
    {
        var demo = TestSupport.CreateContext().Services;
        var services = new Services.AppServices
        {
            Paths = demo.Paths, Settings = demo.Settings, Ui = demo.Ui, Catalog = Core.Catalog.Catalog.Load(demo.Paths),
            Games = demo.Games, Scanner = demo.Scanner, Emulators = demo.Emulators, Launcher = demo.Launcher,
            Sessions = demo.Sessions, IsDemo = true,
        };
        return new UiContext(services);
    }

    private static ServiceSection Section(SettingsViewModel vm) => vm.Sections.Single(s => s.Title == Loc.T("firmware.section"));

    private static ServiceSetting Row(SettingsViewModel vm, string label) => Section(vm).Rows.Single(r => r.Label == label);

    [Fact]
    public Task Every_console_that_needs_files_has_an_import_row_and_dolphin_says_not_needed() => UiTest.Run(() =>
    {
        var ctx = RealCatalogContext();
        var vm = new SettingsViewModel(ctx);
        var section = Section(vm);
        Assert.Equal(Loc.T("firmware.section.note"), section.Note);
        // the three general rows come first: files, folder, bios/import (controller)
        Assert.Equal([Loc.T("firmware.files"), Loc.T("firmware.folder"), Loc.T("firmware.inbox")], section.Rows.Take(3).Select(r => r.Label));
        var switchName = ctx.Services.Catalog.GetSystem("switch")!.Name;
        Assert.True(Row(vm, switchName).IsAction);
        Assert.Equal(Loc.T("firmware.import"), Row(vm, switchName).ActionText);
        Assert.True(Row(vm, Loc.T("firmware.switchFolder", switchName)).IsAction);
        Assert.True(Row(vm, ctx.Services.Catalog.GetSystem("wiiu")!.Name).IsAction);
        // Wii (Dolphin): a fixed row "Nicht nötig" without arrows
        var wiiName = ctx.Services.Catalog.GetSystem("wii")!.Name;
        var wii = Section(vm).Rows.Single(r => r.Label.Contains(wiiName, StringComparison.Ordinal) && r.IsFixed);
        Assert.True(wii.IsFixed);
        Assert.Equal(Loc.T("firmware.notNeeded"), wii.ValueText);
        Assert.Contains("Dolphin", wii.Hint);
        // numbered on with the other sections
        Assert.All(section.Rows, r => Assert.True(r.HasNumber));
    });

    [Fact]
    public Task States_are_read_and_an_import_updates_them() => UiTest.Run(() =>
    {
        var ctx = RealCatalogContext();
        var vm = new SettingsViewModel(ctx) { AllowDemoImport = true };
        var row = Row(vm, ctx.Services.Catalog.GetSystem("switch")!.Name);
        Assert.True(TestSupport.Pump(() => row.Hint.Contains(Loc.T("firmware.state.missing", "prod.keys")), 4000), row.Hint);
        Assert.EndsWith(Loc.T("firmware.for", "Eden"), row.Hint);

        var picked = Path.Combine(ctx.Services.Paths.Root, "..", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(picked);
        var file = Path.Combine(picked, "my-keys.txt");
        File.WriteAllText(file, FakeProdKeys());
        var task = vm.ImportAsync([file]);
        Assert.True(TestSupport.Pump(() => task.IsCompleted, 8000));

        Assert.True(File.Exists(Path.Combine(ctx.Services.Paths.Bios, "switch", "prod.keys")));
        Assert.Contains(vm.LastImport!.Items, i => i is { Name: "prod.keys", Outcome: ImportOutcome.Imported });
        Assert.True(TestSupport.Pump(() => row.Hint.Contains(Loc.T("firmware.state.present", "prod.keys")), 4000), row.Hint);
        Assert.Contains(ctx.Toasts.Items, t => t.Text == Loc.T("firmware.result.done") && (t.Detail ?? "").Contains("prod.keys"));
        // the key content never reaches the UI
        Assert.DoesNotContain(ctx.Toasts.Items, t => (t.Detail ?? "").Contains(new string('0', 32)));
        Directory.Delete(picked, true);
    });

    [Fact]
    public Task Confirm_on_a_console_row_opens_the_picker_and_imports_what_was_chosen() => UiTest.Run(() =>
    {
        var ctx = RealCatalogContext();
        var vm = new SettingsViewModel(ctx) { AllowDemoImport = true };
        var dir = Path.Combine(Path.GetTempPath(), "retrohub-fw-pick", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "notes.txt"), "nothing to see");
        string? asked = null;
        vm.PickFiles = title => { asked = title; return Task.FromResult<IReadOnlyList<string>>([Path.Combine(dir, "notes.txt")]); };
        var switchName = ctx.Services.Catalog.GetSystem("switch")!.Name;
        Row(vm, switchName).Activate();
        Assert.True(TestSupport.Pump(() => vm.LastImport != null && !vm.IsImporting, 8000));
        Assert.Equal(Loc.T("firmware.pickTitleFor", switchName), asked);
        Assert.Contains(vm.LastImport!.Skipped, s => s.Name == "notes.txt");
        Assert.Contains(ctx.Toasts.Items, t => t.Text == Loc.T("firmware.result.nothing"));
        Directory.Delete(dir, true);
    });

    [Fact]
    public Task The_controller_flow_takes_the_files_from_bios_import() => UiTest.Run(() =>
    {
        var ctx = RealCatalogContext();
        var vm = new SettingsViewModel(ctx) { AllowDemoImport = true };
        var inbox = vm.ImportInbox;
        Assert.Equal(Path.Combine(ctx.Services.Paths.Bios, "import"), inbox);
        Directory.CreateDirectory(inbox);
        File.WriteAllText(Path.Combine(inbox, "prod.keys"), FakeProdKeys());
        Row(vm, Loc.T("firmware.inbox")).Activate();
        Assert.True(TestSupport.Pump(() => vm.LastImport != null && !vm.IsImporting, 8000));
        Assert.True(File.Exists(Path.Combine(ctx.Services.Paths.Bios, "switch", "prod.keys")));
        Assert.True(File.Exists(Path.Combine(inbox, "prod.keys")), "the source stays");
    });

    [Fact]
    public Task The_demo_imports_nothing() => UiTest.Run(() =>
    {
        var ctx = RealCatalogContext();
        var vm = new SettingsViewModel(ctx);
        var called = false;
        vm.PickFiles = _ => { called = true; return Task.FromResult<IReadOnlyList<string>>([]); };
        Row(vm, Loc.T("firmware.files")).Activate();
        TestSupport.Settle(100);
        Assert.False(called);
        Assert.Contains(ctx.Toasts.Items, t => t.Text == Loc.T("firmware.demo"));
        Assert.False(Directory.Exists(Path.Combine(ctx.Services.Paths.Bios, "switch")));
    });

    [Fact]
    public void Status_sentences_name_files_while_they_are_few()
    {
        var con = new FirmwareConsole { Id = "ps1", SystemIds = ["ps1"], Name = "PS1", Emulators = "DuckStation", Kind = FirmwareConsoleKind.CatalogBios, Need = FirmwareNeed.Sometimes };
        string Text(params FirmwareItemStatus[] items) => SettingsViewModel.StatusText(new FirmwareConsoleStatus(con, items));
        var a = new FirmwareItemStatus("a.bin", FirmwareItemState.Present);
        var b = new FirmwareItemStatus("b.bin", FirmwareItemState.Missing);
        var c = new FirmwareItemStatus("c.bin", FirmwareItemState.Missing);
        var d = new FirmwareItemStatus("d.bin", FirmwareItemState.Missing);
        var u = new FirmwareItemStatus("u.bin", FirmwareItemState.Unknown);
        Assert.Equal(Loc.T("firmware.list.all", "a.bin") + " " + Loc.T("firmware.for", "DuckStation"), Text(a));
        Assert.Contains("b.bin, c.bin", Text(a, b, c));
        Assert.Contains(Loc.T("firmware.files.count", 3), Text(b, c, d));
        Assert.Contains(Loc.T("firmware.need.sometimes"), Text(b));
        Assert.Contains(Loc.T("firmware.list.unknown", "u.bin"), Text(a, u));
    }

    [Fact]
    public Task The_settings_page_shows_the_section_with_service_rows() => UiTest.Run(() =>
    {
        var ctx = TestSupport.CreateContext();
        var main = new MainViewModel(ctx);
        var window = new Views.MainWindow(main) { Width = 1920, Height = 1080 };
        window.Show();
        TestSupport.Pump(() => main.CurrentPage is HomeViewModel { IsLoaded: true });
        main.Navigator.Navigate(PageKey.Settings);
        Assert.True(TestSupport.Pump(() => window.GetVisualDescendants().OfType<ServiceRow>().Any(r => r.Setting?.Label == Loc.T("firmware.files")), 4000));
        var rows = window.GetVisualDescendants().OfType<ServiceRow>().Select(r => r.Setting?.Label).ToList();
        Assert.Contains(Loc.T("firmware.inbox"), rows);
        // the demo catalog: PS2 (BIOS), PSP (data folder), 3DS (nothing needed)
        Assert.Contains(ctx.Services.Catalog.GetSystem("ps2")!.Name, rows);
        Assert.Contains(ctx.Services.Catalog.GetSystem("3ds")!.Name, rows);
        window.Close();
    });
}
