using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using LinkDeck.Core.Abstractions;
using LinkDeck.Core.Import;
using LinkDeck.Core.Models;
using LinkDeck.Core.Popup;
using LinkDeck.Desktop.Services;
using LinkDeck.Desktop.ViewModels;
using LinkDeck.Desktop.Views;

namespace LinkDeck.Desktop.Tests;

/// <summary>Settings must never lose an item's type when switching between items.</summary>
public class SettingsTypeIsKeptTests : IDisposable
{
    private readonly DeckFixture _f = new(DeckFixture.Sample());

    public void Dispose() => _f.Dispose();

    [AvaloniaFact]
    public void Switching_items_and_groups_keeps_every_items_type()
    {
        var vm = new SettingsViewModel(_f.Deck, new NoRunner());
        var window = new SettingsWindow { DataContext = vm };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        foreach (var group in vm.Groups)
        {
            vm.SelectedGroup = group;
            Dispatcher.UIThread.RunJobs();
            foreach (var item in group.Items)
            {
                vm.SelectedItem = item;
                Dispatcher.UIThread.RunJobs();
            }

            vm.SelectedItem = null;
            Dispatcher.UIThread.RunJobs();
        }

        Assert.True(vm.Save());
        Assert.Equal(
            DeckFixture.Sample().AllItems.Select(i => (i.Id, i.Type)),
            _f.Deck.Config.AllItems.Select(i => (i.Id, i.Type)));
        window.Close();
    }

    [Fact]
    public void A_combo_box_reporting_minus_one_cannot_erase_the_type()
    {
        var editor = new ItemEditorViewModel(new RdpItem { Id = "x", Name = "S", Host = "h" });

        editor.TypeIndex = -1;

        Assert.Equal(ItemType.Rdp, editor.Type);
    }

    [Fact]
    public void Sort_into_type_groups_creates_groups_and_moves_items()
    {
        var vm = new SettingsViewModel(_f.Deck, new NoRunner());

        vm.SortIntoTypeGroups();
        Assert.True(vm.Save());

        var groups = _f.Deck.Config.Groups.ToDictionary(g => g.Id);
        Assert.Equal(["jira", "wiki"], groups["web"].Items.Select(i => i.Id));
        Assert.Equal(["prod"], groups["rdp"].Items.Select(i => i.Id));
        Assert.Equal(["docs"], groups["files"].Items.Select(i => i.Id));
        Assert.Empty(groups["work"].Items); // user groups are kept, now empty
    }

    private sealed class NoRunner : IProcessRunner
    {
        public void Start(System.Diagnostics.ProcessStartInfo startInfo)
        {
        }
    }
}

public class QuickAddDefaultsAndReopenTests : IDisposable
{
    private readonly DeckFixture _f = new(DeckFixture.Sample(SortMode.Manual) with
    {
        Groups = [.. DeckFixture.Sample().Groups, new Group { Id = "web", Name = "Web" }, new Group { Id = "rdp", Name = "Remote desktop" }],
    });

    public void Dispose() => _f.Dispose();

    [Fact]
    public void New_items_go_to_the_group_for_their_type()
    {
        var vm = new QuickAddViewModel(_f.Deck);

        vm.Target = "https://example.org";
        Assert.Equal("web", vm.SelectedGroup!.Id);

        vm.TypeIndex = (int)ItemType.Rdp;
        Assert.Equal("rdp", vm.SelectedGroup!.Id);
    }

    [Fact]
    public void A_chosen_group_wins_over_the_type_group()
    {
        var fromTab = new QuickAddViewModel(_f.Deck, groupId: "home") { Target = "https://example.org" };
        Assert.Equal("home", fromTab.SelectedGroup!.Id);

        var picked = new QuickAddViewModel(_f.Deck);
        picked.SelectedGroup = picked.Groups.Single(g => g.Id == "home");
        picked.Target = "https://example.org";
        Assert.Equal("home", picked.SelectedGroup!.Id);
    }

    [AvaloniaFact]
    public async Task Launcher_comes_back_after_adding_from_it_on_the_same_tab()
    {
        var vm = new PopupViewModel(_f.Deck, null);
        var window = new PopupWindow { DataContext = vm };
        var controller = new PopupController(window, vm, _f.Deck, new FakeHotkey(), null, new DropParser(null), null, cyclingShowDelay: TimeSpan.Zero);
        controller.QuickAddDialog = qa =>
        {
            Assert.False(window.IsVisible); // hidden while the dialog is open
            qa.Target = "https://new.example";
            return Task.FromResult(qa.Save());
        };
        controller.ShowInteractive();
        vm.SelectGroupTab("home");

        await controller.QuickAddAsync([null], vm.CurrentGroupId);

        Assert.True(window.IsVisible);
        Assert.Equal(PopupMode.Interactive, controller.Mode);
        Assert.Equal("Home", vm.Tabs[vm.SelectedTabIndex].Name);
        Assert.Contains(vm.Items, t => t.Item.Target == "https://new.example");
    }

    [AvaloniaFact]
    public async Task Adding_from_the_tray_does_not_open_the_launcher()
    {
        var vm = new PopupViewModel(_f.Deck, null);
        var window = new PopupWindow { DataContext = vm };
        var controller = new PopupController(window, vm, _f.Deck, new FakeHotkey(), null, new DropParser(null), null, cyclingShowDelay: TimeSpan.Zero)
        {
            QuickAddDialog = _ => Task.FromResult(false),
        };

        await controller.QuickAddAsync([null]);

        Assert.False(window.IsVisible);
    }
}

public class DraggablePopupTests
{
    [AvaloniaFact]
    public void Popup_has_a_center_on_screen_menu_entry()
    {
        using var f = new DeckFixture(DeckFixture.Sample());
        var window = new PopupWindow { DataContext = new PopupViewModel(f.Deck, null) };

        var menu = (MenuFlyout)window.MainMenuButton.Flyout!;

        Assert.Contains(menu.Items.OfType<MenuItem>(), m => (string?)m.Header == "Center on screen");
    }
}
