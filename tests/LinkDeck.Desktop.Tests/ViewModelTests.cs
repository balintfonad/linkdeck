using LinkDeck.Core.Abstractions;
using LinkDeck.Core.Config;
using LinkDeck.Core.Import;
using LinkDeck.Core.Models;
using LinkDeck.Desktop.ViewModels;

namespace LinkDeck.Desktop.Tests;

public class PopupViewModelTests : IDisposable
{
    private readonly DeckFixture _f = new(DeckFixture.Sample());

    public void Dispose() => _f.Dispose();

    [Fact]
    public void Tabs_are_all_then_every_group()
    {
        var vm = new PopupViewModel(_f.Deck, null);

        Assert.Equal(["All", "Work", "Home"], vm.Tabs.Select(t => t.Name));
        Assert.Equal(4, vm.Items.Count);
        Assert.Equal(0, vm.SelectedIndex);
        Assert.True(vm.Items[0].IsSelected);
    }

    [Fact]
    public void Group_tab_shows_only_that_group()
    {
        var vm = new PopupViewModel(_f.Deck, null);

        vm.SelectedTabIndex = vm.Tabs.ToList().FindIndex(t => t.Name == "Home");

        Assert.Equal(["docs"], vm.Items.Select(i => i.Item.Id));
    }

    [Fact]
    public void Search_filters_and_shrinks_the_grid()
    {
        var vm = new PopupViewModel(_f.Deck, null);

        vm.SearchText = "wik";

        Assert.Equal(["wiki"], vm.Items.Select(i => i.Item.Id));
        Assert.Equal("https://wiki.example.com", vm.FooterTarget);
    }

    [Fact]
    public void Empty_query_orders_by_frecency()
    {
        _f.Deck.Launch(_f.Deck.Config.AllItems.Single(i => i.Id == "docs"));
        var vm = new PopupViewModel(_f.Deck, null);

        Assert.Equal("docs", vm.Items[0].Item.Id);
    }

    [Fact]
    public void Arrow_keys_move_in_a_two_dimensional_grid_and_clamp()
    {
        var vm = new PopupViewModel(_f.Deck, null);

        vm.Move(1, 0);
        Assert.Equal(1, vm.SelectedIndex);
        vm.Move(0, 1); // next row does not exist: clamp to last
        Assert.Equal(3, vm.SelectedIndex);
        vm.Move(-10, -10);
        Assert.Equal(0, vm.SelectedIndex);
    }

    [Fact]
    public void Enter_ctrl_number_and_shift_enter_raise_requests()
    {
        var vm = new PopupViewModel(_f.Deck, null);
        var launched = new List<Item>();
        var copied = new List<Item>();
        vm.LaunchRequested += (_, i) => launched.Add(i);
        vm.CopyRequested += (_, i) => copied.Add(i);

        vm.LaunchSelected();
        Assert.True(vm.LaunchNth(3));
        Assert.False(vm.LaunchNth(9));
        vm.CopySelected();

        Assert.Equal([vm.Items[0].Item, vm.Items[2].Item], launched);
        Assert.Equal([vm.Items[0].Item], copied);
    }

    [Fact]
    public void First_nine_tiles_show_ctrl_number_hints()
    {
        var vm = new PopupViewModel(_f.Deck, null);

        Assert.Equal("Ctrl+1", vm.Items[0].ShortcutHint);
        Assert.Equal("Ctrl+4", vm.Items[3].ShortcutHint);
    }

    [Fact]
    public void Tab_cycles_tabs_and_wraps()
    {
        var vm = new PopupViewModel(_f.Deck, null);

        vm.NextTab(+1);
        Assert.Equal(1, vm.SelectedTabIndex);
        vm.NextTab(-1);
        vm.NextTab(-1);
        Assert.Equal(vm.Tabs.Count - 1, vm.SelectedTabIndex);
    }

    [Fact]
    public void Reset_clears_search_and_returns_to_all()
    {
        var vm = new PopupViewModel(_f.Deck, null);
        vm.SelectedTabIndex = 1;
        vm.SearchText = "jira";
        vm.Move(1, 0);

        vm.Reset();

        Assert.Equal("", vm.SearchText);
        Assert.Equal(0, vm.SelectedTabIndex);
        Assert.Equal(4, vm.Items.Count);
        Assert.Equal(0, vm.SelectedIndex);
    }

    [Fact]
    public void Config_changes_rebuild_tabs_and_items()
    {
        var vm = new PopupViewModel(_f.Deck, null);

        _f.Deck.AddItem("home", new UrlItem { Id = "new", Name = "New", Url = "https://new.example" });

        Assert.Equal(5, vm.Items.Count);
        Assert.Equal(5, vm.AllCount);
    }
}

public class DeckServiceTests : IDisposable
{
    private readonly DeckFixture _f = new(DeckFixture.Sample());

    public void Dispose() => _f.Dispose();

    [Fact]
    public void Successful_launch_records_usage()
    {
        var item = _f.Deck.Config.AllItems.First();

        Assert.True(_f.Deck.Launch(item).Success);

        Assert.Equal(1, _f.Deck.Usage[item.Id].Count);
        Assert.True(File.Exists(_f.Dir.File("state.json")));
    }

    [Fact]
    public void Failed_launch_notifies_and_does_not_record_usage()
    {
        _f.Launcher.Next = LaunchResult.Fail("boom");
        var item = _f.Deck.Config.AllItems.First();

        Assert.False(_f.Deck.Launch(item).Success);

        Assert.False(_f.Deck.Usage.ContainsKey(item.Id));
        Assert.Contains(_f.Notifier.Shown, n => n.Level == NoticeLevel.Error && n.Message == "boom");
    }

    [Fact]
    public void AddItem_creates_a_group_when_none_exists()
    {
        using var empty = new DeckFixture(new AppConfig { Groups = [] });

        Assert.True(empty.Deck.AddItem(null, new UrlItem { Id = "x", Name = "X", Url = "https://x.example" }));

        Assert.Equal("General", Assert.Single(empty.Deck.Config.Groups).Name);
    }

    [Fact]
    public void Saving_in_read_only_mode_fails_with_a_notice()
    {
        File.WriteAllText(_f.Store.ConfigPath, "{ broken");
        _f.Store.ReloadIfChanged();

        Assert.False(_f.Deck.TrySave(_f.Deck.Config));
        Assert.Contains(_f.Notifier.Shown, n => n.Level == NoticeLevel.Error);
    }
}

public class QuickAddViewModelTests : IDisposable
{
    private readonly DeckFixture _f = new(DeckFixture.Sample());

    public void Dispose() => _f.Dispose();

    [Fact]
    public void Type_is_auto_detected_from_the_target()
    {
        var vm = new QuickAddViewModel(_f.Deck);

        vm.Target = "https://example.com";
        Assert.Equal(ItemType.Url, vm.Type);
        vm.Target = @"C:\Projects";
        Assert.Equal(ItemType.Path, vm.Type);
    }

    [Fact]
    public void User_chosen_type_is_not_overridden()
    {
        var vm = new QuickAddViewModel(_f.Deck) { TypeIndex = (int)ItemType.Rdp };

        vm.Target = "server.corp";

        Assert.Equal(ItemType.Rdp, vm.Type);
    }

    [Fact]
    public void Save_adds_item_to_the_selected_group_with_a_suggested_name()
    {
        var vm = new QuickAddViewModel(_f.Deck, groupId: "home") { Target = "https://www.example.org/x" };

        Assert.True(vm.Save());

        var added = _f.Deck.Config.Groups.Single(g => g.Id == "home").Items[^1];
        Assert.Equal(("example.org", "https://www.example.org/x"), (added.Name, added.Target));
        Assert.Same(added, _f.Deck.Config.AllItems.Single(i => i.Id == vm.SavedItem!.Id));
    }

    [Fact]
    public void Invalid_target_shows_an_error_and_does_not_save()
    {
        var vm = new QuickAddViewModel(_f.Deck) { Target = "javascript:alert(1)", Name = "Bad" };
        vm.TypeIndex = (int)ItemType.Url; // user overrides the detected type

        Assert.False(vm.Save());

        Assert.True(vm.HasError);
        Assert.Equal(4, _f.Deck.Config.AllItems.Count());
    }

    [Fact]
    public void Prefilled_draft_keeps_its_type_and_fields()
    {
        var draft = new ItemDraft { Type = ItemType.Rdp, Name = "Prod", Target = "10.0.0.1:3390", Username = "admin" };
        var vm = new QuickAddViewModel(_f.Deck, draft);

        Assert.True(vm.IsRdp);
        Assert.True(vm.Save());

        var rdp = Assert.IsType<RdpItem>(vm.SavedItem);
        Assert.Equal(("10.0.0.1", 3390, "admin"), (rdp.Host, rdp.Port, rdp.Username));
    }
}

public class SettingsViewModelTests : IDisposable
{
    private readonly DeckFixture _f = new(DeckFixture.Sample());

    public void Dispose() => _f.Dispose();

    private sealed class NoRunner : IProcessRunner
    {
        public void Start(System.Diagnostics.ProcessStartInfo startInfo)
        {
        }
    }

    private SettingsViewModel New() => new(_f.Deck, new NoRunner());

    [Fact]
    public void Loads_groups_items_and_general_settings()
    {
        var vm = New();

        Assert.Equal(["Work", "Home"], vm.Groups.Select(g => g.Name));
        Assert.Equal("Work", vm.SelectedGroup!.Name);
        Assert.Equal("Jira", vm.SelectedItem!.Name);
        Assert.Equal("Alt+Ctrl", vm.Hotkey.ToString());
        Assert.False(vm.IsDirty);
    }

    [Fact]
    public void Edits_mark_dirty_and_save_writes_the_config()
    {
        var vm = New();

        vm.SelectedItem!.Name = "Jira Cloud";
        vm.SelectedGroup!.Color = "#FF0000";
        vm.ThemeIndex = (int)ThemeMode.Dark;
        Assert.True(vm.IsDirty);
        Assert.True(vm.Save());

        Assert.False(vm.IsDirty);
        Assert.Equal("Jira Cloud", _f.Deck.Config.AllItems.First().Name);
        Assert.Equal("#FF0000", _f.Deck.Config.Groups[0].Color);
        Assert.Equal(ThemeMode.Dark, _f.Deck.Config.Settings.Theme);
    }

    [Fact]
    public void Invalid_item_blocks_save_and_selects_the_item()
    {
        var vm = New();
        vm.SelectedGroup = vm.Groups[1];
        vm.SelectedItem!.Target = "";

        Assert.False(vm.Save());

        Assert.Equal("Documents", vm.SelectedItem!.Name);
        Assert.True(vm.SelectedItem.HasError);
        Assert.Equal(@"C:\Docs", ((PathItem)_f.Deck.Config.Groups[1].Items[0]).Path);
    }

    [Fact]
    public void Invalid_group_colour_blocks_save()
    {
        var vm = New();
        vm.SelectedGroup!.Color = "blue";

        Assert.False(vm.Save());
        Assert.Contains("#RRGGBB", vm.Status, StringComparison.Ordinal);
    }

    [Fact]
    public void Groups_and_items_can_be_added_deleted_and_reordered()
    {
        var vm = New();

        vm.AddGroupCommand.Execute(null);
        vm.SelectedGroup!.Name = "Servers";
        vm.AddDraft(new ItemDraft { Type = ItemType.Rdp, Name = "DC", Target = "dc01" });
        vm.ReorderGroup(2, 0);
        vm.SelectedGroup = vm.Groups.Single(g => g.Name == "Work");
        vm.ReorderItem(2, 0);
        vm.SelectedItem = vm.SelectedGroup!.Items.Single(i => i.Name == "Wiki");
        vm.DeleteItemCommand.Execute(null);
        Assert.True(vm.Save());

        var config = _f.Deck.Config;
        Assert.Equal(["Servers", "Work", "Home"], config.Groups.Select(g => g.Name));
        Assert.Equal(["prod", "jira"], config.Groups[1].Items.Select(i => i.Id));
        Assert.IsType<RdpItem>(Assert.Single(config.Groups[0].Items));
    }

    [Fact]
    public void Item_type_can_be_changed_in_the_editor()
    {
        var vm = New();
        var editor = vm.SelectedItem!;

        editor.TypeIndex = (int)ItemType.Rdp;
        editor.Target = "server.corp:4000";
        editor.Username = "me";
        Assert.True(vm.Save());

        var rdp = Assert.IsType<RdpItem>(_f.Deck.Config.AllItems.Single(i => i.Id == "jira"));
        Assert.Equal(("server.corp", 4000, "me"), (rdp.Host, rdp.Port, rdp.Username));
    }

    [Fact]
    public void Hold_and_step_keys_are_chosen_from_dropdowns_and_must_differ()
    {
        var vm = New();
        Assert.Equal(("Alt", "Ctrl"), (vm.Modifiers[vm.HoldIndex], vm.Modifiers[vm.StepIndex]));

        vm.HoldIndex = vm.StepIndex;
        Assert.False(vm.Save());
        Assert.Contains("different", vm.HotkeyText, StringComparison.Ordinal);

        vm.HoldIndex = vm.Modifiers.ToList().IndexOf("Ctrl");
        vm.StepIndex = vm.Modifiers.ToList().IndexOf("Win");
        Assert.True(vm.IsDirty);
        Assert.True(vm.Save());

        Assert.Equal(new Hotkey { Hold = "Ctrl", Step = "Win" }, _f.Deck.Config.Settings.Hotkey);
    }

    [Fact]
    public void External_change_reloads_when_clean_and_warns_when_dirty()
    {
        var vm = New();

        _f.Deck.AddItem("home", new UrlItem { Id = "x", Name = "X", Url = "https://x.example" });
        Assert.Equal(2, vm.Groups[1].Items.Count);

        vm.Groups[0].Name = "Edited";
        _f.Deck.AddItem("home", new UrlItem { Id = "y", Name = "Y", Url = "https://y.example" });

        Assert.True(vm.HasExternalChange);
        Assert.Equal("Edited", vm.Groups[0].Name);
        vm.Revert();
        Assert.Equal("Work", vm.Groups[0].Name);
        Assert.Equal(3, vm.Groups[1].Items.Count);
    }
}
