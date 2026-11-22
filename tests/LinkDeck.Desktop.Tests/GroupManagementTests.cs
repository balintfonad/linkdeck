using Avalonia.Headless.XUnit;
using LinkDeck.Core.Import;
using LinkDeck.Core.Models;
using LinkDeck.Core.Popup;
using LinkDeck.Desktop.Services;
using LinkDeck.Desktop.ViewModels;
using LinkDeck.Desktop.Views;

namespace LinkDeck.Desktop.Tests;

public sealed class FakeDialogs : IDialogService
{
    public Queue<string?> Prompts { get; } = new();

    public bool ConfirmResult { get; set; } = true;

    public List<string> Messages { get; } = [];

    public Task<string?> PromptAsync(string title, string label, string initial = "")
    {
        Messages.Add($"{title}: {initial}");
        return Task.FromResult(Prompts.Count > 0 ? Prompts.Dequeue() : null);
    }

    public Task<bool> ConfirmAsync(string title, string message, string confirmText)
    {
        Messages.Add(message);
        return Task.FromResult(ConfirmResult);
    }

    public LinkDeck.Desktop.Views.ColorChoice? ColorResult { get; set; }

    public Task<LinkDeck.Desktop.Views.ColorChoice?> PickColorAsync(string message, string? current)
    {
        Messages.Add($"{message} [{current}]");
        return Task.FromResult(ColorResult);
    }
}

public class DeckGroupTests : IDisposable
{
    private readonly DeckFixture _f = new(DeckFixture.Sample());

    public void Dispose() => _f.Dispose();

    [Fact]
    public void AddGroup_creates_a_unique_slug_id()
    {
        var a = _f.Deck.AddGroup("Szerverek");
        var b = _f.Deck.AddGroup("Szerverek");

        Assert.Equal("szerverek", a!.Id);
        Assert.Equal("szerverek-2", b!.Id);
        Assert.Null(_f.Deck.AddGroup("   "));
        Assert.Equal(4, _f.Deck.Config.Groups.Count);
    }

    [Fact]
    public void RenameGroup_changes_only_the_name()
    {
        Assert.True(_f.Deck.RenameGroup("work", "Munka"));

        var g = _f.Deck.Config.Groups.Single(x => x.Id == "work");
        Assert.Equal("Munka", g.Name);
        Assert.Equal(3, g.Items.Count);
    }

    [Fact]
    public void DeleteGroup_moves_its_items_to_the_first_other_group()
    {
        Assert.True(_f.Deck.DeleteGroup("work"));

        var home = Assert.Single(_f.Deck.Config.Groups);
        Assert.Equal(["docs", "jira", "wiki", "prod"], home.Items.Select(i => i.Id));
    }

    [Fact]
    public void Deleting_the_only_group_deletes_its_items()
    {
        _f.Deck.DeleteGroup("work");

        Assert.True(_f.Deck.DeleteGroup("home"));

        Assert.Empty(_f.Deck.Config.Groups);
        Assert.Empty(_f.Deck.Config.AllItems);
    }

    [Fact]
    public void MoveItem_moves_to_the_end_of_the_target_group()
    {
        Assert.True(_f.Deck.MoveItem("jira", "home"));

        Assert.Equal(["docs", "jira"], _f.Deck.Config.Groups.Single(g => g.Id == "home").Items.Select(i => i.Id));
        Assert.DoesNotContain(_f.Deck.Config.Groups.Single(g => g.Id == "work").Items, i => i.Id == "jira");
        Assert.False(_f.Deck.MoveItem("jira", "nope"));
    }
}

public class PopupGroupTests : IDisposable
{
    private readonly DeckFixture _f = new(DeckFixture.Sample(SortMode.Manual));

    public void Dispose() => _f.Dispose();

    private (PopupWindow Window, PopupViewModel Vm, PopupController Controller, FakeDialogs Dialogs) Create()
    {
        var vm = new PopupViewModel(_f.Deck, null);
        var window = new PopupWindow { DataContext = vm };
        var dialogs = new FakeDialogs();
        var controller = new PopupController(window, vm, _f.Deck, new FakeHotkey(), null, new DropParser(null), null, cyclingShowDelay: TimeSpan.Zero)
        {
            Dialogs = dialogs,
        };
        return (window, vm, controller, dialogs);
    }

    [AvaloniaFact]
    public async Task New_group_button_creates_a_group_and_shows_its_tab()
    {
        var (window, vm, controller, dialogs) = Create();
        controller.ShowInteractive();
        dialogs.Prompts.Enqueue("Szerverek");

        await controller.NewGroupAsync();

        Assert.Contains(_f.Deck.Config.Groups, g => g.Name == "Szerverek");
        Assert.True(window.IsVisible);
        Assert.Equal("Szerverek", vm.Tabs[vm.SelectedTabIndex].Name);
    }

    [AvaloniaFact]
    public async Task Cancelled_new_group_changes_nothing_and_reopens_the_popup()
    {
        var (window, _, controller, _) = Create();
        controller.ShowInteractive();

        await controller.NewGroupAsync();

        Assert.Equal(2, _f.Deck.Config.Groups.Count);
        Assert.True(window.IsVisible);
        Assert.Equal(PopupMode.Interactive, controller.Mode);
    }

    [AvaloniaFact]
    public void Rename_and_delete_from_a_group_tab()
    {
        var (_, vm, controller, dialogs) = Create();
        controller.ShowInteractive();
        dialogs.Prompts.Enqueue("Munka");

        vm.RenameGroup("work");
        Assert.Equal("Munka", _f.Deck.Config.Groups.Single(g => g.Id == "work").Name);

        vm.DeleteGroup("work");
        Assert.Contains(dialogs.Messages, m => m.Contains("3 item(s) will move to 'Home'", StringComparison.Ordinal));
        Assert.DoesNotContain(_f.Deck.Config.Groups, g => g.Id == "work");
        Assert.Equal(4, _f.Deck.Config.AllItems.Count());
    }

    [AvaloniaFact]
    public void Declined_delete_keeps_the_group()
    {
        var (_, vm, controller, dialogs) = Create();
        controller.ShowInteractive();
        dialogs.ConfirmResult = false;

        vm.DeleteGroup("work");

        Assert.Contains(_f.Deck.Config.Groups, g => g.Id == "work");
    }

    [AvaloniaFact]
    public void Move_to_lists_other_groups_and_moves_the_item()
    {
        var (_, vm, controller, _) = Create();
        controller.ShowInteractive();
        var jira = vm.Items.Single(t => t.Item.Id == "jira").Item;

        Assert.Equal(["home"], vm.MoveTargetsFor(jira).Select(g => g.Id));
        vm.MoveItem(jira, "home");

        Assert.Contains(_f.Deck.Config.Groups.Single(g => g.Id == "home").Items, i => i.Id == "jira");
    }

    [Fact]
    public void Group_tabs_know_their_group()
    {
        var vm = new PopupViewModel(_f.Deck, null);

        Assert.Equal([null, "work", "home"], vm.Tabs.Select(t => t.GroupId));
        vm.SelectGroupTab("home");
        Assert.Equal("Home", vm.Tabs[vm.SelectedTabIndex].Name);
    }
}

public class QuickAddGroupAndBrowseTests : IDisposable
{
    private readonly DeckFixture _f = new(DeckFixture.Sample());

    public void Dispose() => _f.Dispose();

    [Fact]
    public void Saving_with_a_new_group_creates_it_and_adds_the_item_there()
    {
        var vm = new QuickAddViewModel(_f.Deck) { Target = "https://example.org" };
        vm.ToggleNewGroupCommand.Execute(null);
        vm.NewGroupName = "Hírek";

        Assert.True(vm.Save());

        var group = _f.Deck.Config.Groups.Single(g => g.Name == "Hírek");
        Assert.Equal(vm.SavedItem!.Id, Assert.Single(group.Items).Id);
    }

    [Fact]
    public void New_group_without_a_name_is_an_error()
    {
        var vm = new QuickAddViewModel(_f.Deck) { Target = "https://example.org" };
        vm.ToggleNewGroupCommand.Execute(null);

        Assert.False(vm.Save());
        Assert.True(vm.HasError);
    }

    [Fact]
    public void Browsed_file_fills_the_form()
    {
        using var dir = new LinkDeck.Core.Tests.TempDir();
        var rdp = dir.File("Prod.rdp");
        File.WriteAllText(rdp, "full address:s:10.0.0.9:3390\r\nusername:s:admin\r\n");
        var vm = new QuickAddViewModel(_f.Deck);

        vm.ApplyDraft(new DropParser(null).ParseFile(rdp)!);

        Assert.Equal((ItemType.Rdp, "Prod", "10.0.0.9:3390", "admin"), (vm.Type, vm.Name, vm.Target, vm.Username));
        Assert.True(vm.Save());
    }
}

public class CopyAndTransferTests : IDisposable
{
    private readonly DeckFixture _f = new(DeckFixture.Sample(SortMode.Manual));

    public void Dispose() => _f.Dispose();

    [Fact]
    public void CopyItem_adds_a_copy_with_a_new_id_and_keeps_the_original()
    {
        var copy = _f.Deck.CopyItem("jira", "home");

        Assert.NotNull(copy);
        Assert.NotEqual("jira", copy.Id);
        Assert.Equal("Jira", copy.Name);
        Assert.Contains(_f.Deck.Config.Groups.Single(g => g.Id == "work").Items, i => i.Id == "jira");
        Assert.Equal(copy.Id, _f.Deck.Config.Groups.Single(g => g.Id == "home").Items[^1].Id);
        Assert.Null(_f.Deck.CopyItem("jira", "nope"));
    }

    [AvaloniaFact]
    public void Copy_to_from_the_popup_menu()
    {
        var vm = new PopupViewModel(_f.Deck, null);
        var window = new PopupWindow { DataContext = vm };
        var controller = new PopupController(window, vm, _f.Deck, new FakeHotkey(), null, new DropParser(null), null, cyclingShowDelay: TimeSpan.Zero);
        controller.ShowInteractive();
        var wiki = vm.Items.Single(t => t.Item.Id == "wiki").Item;

        Assert.Equal(["work", "home"], vm.CopyTargets.Select(g => g.Id));
        vm.CopyItemTo(wiki, "home");

        Assert.Equal(2, _f.Deck.Config.AllItems.Count(i => i.Name == "Wiki"));
        Assert.Contains(_f.Notifier.Shown, n => n.Message.Contains("Copied 'Wiki' to 'Home'", StringComparison.Ordinal));
    }

    [Fact]
    public void Settings_move_and_copy_between_groups()
    {
        var vm = new SettingsViewModel(_f.Deck, new NoRunner());
        var work = vm.Groups.Single(g => g.Id == "work");
        var home = vm.Groups.Single(g => g.Id == "home");

        Assert.True(vm.TransferItem(work.Items.Single(i => i.Id == "jira"), home, copy: false));
        Assert.True(vm.TransferItem(work.Items.Single(i => i.Id == "wiki"), home, copy: true));
        Assert.False(vm.TransferItem(home.Items[0], home, copy: false));
        Assert.True(vm.IsDirty);
        Assert.True(vm.Save());

        var config = _f.Deck.Config;
        Assert.Equal(["wiki", "prod"], config.Groups.Single(g => g.Id == "work").Items.Select(i => i.Id));
        var homeItems = config.Groups.Single(g => g.Id == "home").Items;
        Assert.Equal(["docs", "jira"], homeItems.Take(2).Select(i => i.Id));
        Assert.Equal("Wiki", homeItems[2].Name);
        Assert.NotEqual("wiki", homeItems[2].Id);
    }

    [Fact]
    public void Settings_move_button_uses_the_chosen_group()
    {
        var vm = new SettingsViewModel(_f.Deck, new NoRunner());
        vm.SelectedItem = vm.Groups[0].Items.Single(i => i.Id == "prod");
        vm.TransferTarget = vm.Groups[1];

        vm.CopySelectedItemCommand.Execute(null);

        Assert.Equal(2, vm.Groups[1].Items.Count);
        Assert.Same(vm.Groups[1], vm.SelectedGroup);
    }

    private sealed class NoRunner : LinkDeck.Core.Abstractions.IProcessRunner
    {
        public void Start(System.Diagnostics.ProcessStartInfo startInfo)
        {
        }
    }
}
