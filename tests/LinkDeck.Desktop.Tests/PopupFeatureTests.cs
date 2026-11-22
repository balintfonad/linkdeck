using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using LinkDeck.Core.Abstractions;
using LinkDeck.Core.Import;
using LinkDeck.Core.Models;
using LinkDeck.Core.Popup;
using LinkDeck.Desktop.Services;
using LinkDeck.Desktop.ViewModels;
using LinkDeck.Desktop.Views;

namespace LinkDeck.Desktop.Tests;

public class PopupFeatureTests : IDisposable
{
    private readonly DeckFixture _f = new(DeckFixture.Sample(SortMode.Manual));

    public void Dispose() => _f.Dispose();

    private (PopupWindow Window, PopupViewModel Vm, PopupController Controller, FakeHotkey Hotkey) Create()
    {
        var vm = new PopupViewModel(_f.Deck, null);
        var window = new PopupWindow { DataContext = vm };
        var hotkey = new FakeHotkey();
        var controller = new PopupController(window, vm, _f.Deck, hotkey, null, new DropParser(null), null, cyclingShowDelay: TimeSpan.Zero);
        return (window, vm, controller, hotkey);
    }

    [AvaloniaFact]
    public void Delete_key_removes_the_selected_item_when_search_is_empty()
    {
        var (window, vm, controller, _) = Create();
        controller.ShowInteractive();
        vm.SelectedIndex = 1;

        window.KeyPressQwerty(PhysicalKey.Delete, RawInputModifiers.None);

        Assert.DoesNotContain(_f.Deck.Config.AllItems, i => i.Id == "wiki");
        Assert.Equal(3, vm.Items.Count);
        Assert.True(window.IsVisible);
    }

    [AvaloniaFact]
    public void Context_menu_commands_remove_and_edit()
    {
        var (_, vm, controller, _) = Create();
        Item? edited = null;
        controller.EditRequested += (_, i) => edited = i;
        controller.ShowInteractive();

        vm.Items.Single(t => t.Item.Id == "prod").RemoveCommand.Execute(null);
        Assert.DoesNotContain(_f.Deck.Config.AllItems, i => i.Id == "prod");

        vm.Items.Single(t => t.Item.Id == "jira").EditCommand.Execute(null);
        Assert.Equal("jira", edited?.Id);
        Assert.Equal(PopupMode.Hidden, controller.Mode);
    }

    [AvaloniaFact]
    public void Clicking_the_popup_while_cycling_keeps_it_open_like_space()
    {
        var (window, _, controller, hotkey) = Create();
        hotkey.Open();

        window.MouseDown(new Avalonia.Point(100, 100), MouseButton.Left);
        hotkey.Release();

        Assert.True(window.IsVisible);
        Assert.Equal(PopupMode.Interactive, controller.Mode);
        Assert.Empty(_f.Launcher.Launched);
    }

    [AvaloniaFact]
    public void Tab_while_holding_switches_group_and_keeps_the_popup_open()
    {
        var (window, vm, controller, hotkey) = Create();
        hotkey.Open();

        hotkey.Tab();
        hotkey.Release();

        Assert.True(window.IsVisible);
        Assert.Equal(PopupMode.Interactive, controller.Mode);
        Assert.Equal("Work", vm.Tabs[vm.SelectedTabIndex].Name);
        Assert.Empty(_f.Launcher.Launched);
    }

    [AvaloniaFact]
    public void Tab_key_in_interactive_mode_switches_groups()
    {
        var (window, vm, controller, _) = Create();
        controller.ShowInteractive();

        window.KeyPressQwerty(PhysicalKey.Tab, RawInputModifiers.None);
        Assert.Equal(1, vm.SelectedTabIndex);
        window.KeyPressQwerty(PhysicalKey.Tab, RawInputModifiers.Shift);
        Assert.Equal(0, vm.SelectedTabIndex);
    }
}

public class QuickAddAppPickerTests : IDisposable
{
    private readonly DeckFixture _f = new(DeckFixture.Sample());

    public void Dispose() => _f.Dispose();

    private sealed class FakeCatalog : IAppCatalog
    {
        public IReadOnlyList<AppCandidate> GetRunningApps() =>
        [
            new("Calculator", "shell:AppsFolder\\Microsoft.WindowsCalculator_8wekyb3d8bbwe!App", null, null, true),
        ];

        public IReadOnlyList<AppCandidate> GetInstalledApps() =>
        [
            new("Paint", @"C:\Windows\System32\mspaint.exe", null, null, false),
            new("Visual Studio Code", @"C:\Code\Code.exe", "--new-window", @"C:\Code", false),
            new("Calculator", "shell:AppsFolder\\Microsoft.WindowsCalculator_8wekyb3d8bbwe!App", null, null, false),
        ];
    }

    [Fact]
    public async Task Lists_running_apps_first_without_duplicates_and_filters()
    {
        var vm = new QuickAddViewModel(_f.Deck, apps: new FakeCatalog());

        await vm.LoadAppsAsync();
        Assert.Equal(["Calculator", "Paint", "Visual Studio Code"], vm.Apps.Select(a => a.Name));
        Assert.True(vm.Apps[0].IsRunning);

        vm.AppQuery = "code";
        Assert.Equal(["Visual Studio Code"], vm.Apps.Select(a => a.Name));
    }

    [Fact]
    public async Task Picking_an_app_fills_the_form_and_saves_a_path_item()
    {
        var vm = new QuickAddViewModel(_f.Deck, apps: new FakeCatalog());
        await vm.LoadAppsAsync();

        vm.SelectedApp = vm.Apps.Single(a => a.Name == "Visual Studio Code");

        Assert.Equal(ItemType.Path, vm.Type);
        Assert.True(vm.Save());
        var item = Assert.IsType<PathItem>(vm.SavedItem);
        Assert.Equal((@"C:\Code\Code.exe", "--new-window", @"C:\Code", "Visual Studio Code"), (item.Path, item.Args, item.WorkingDir, item.Name));
    }
}

public class DeckServiceRemoveTests : IDisposable
{
    private readonly DeckFixture _f = new(DeckFixture.Sample());

    public void Dispose() => _f.Dispose();

    [Fact]
    public void RemoveItem_removes_from_its_group_and_keeps_a_backup()
    {
        Assert.True(_f.Deck.RemoveItem("wiki"));

        Assert.DoesNotContain(_f.Deck.Config.AllItems, i => i.Id == "wiki");
        Assert.Contains("wiki", File.ReadAllText(_f.Store.BackupPath), StringComparison.Ordinal);
        Assert.False(_f.Deck.RemoveItem("missing"));
    }
}
