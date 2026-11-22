using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using LinkDeck.Core.Import;
using LinkDeck.Core.Models;
using LinkDeck.Desktop.Services;
using LinkDeck.Desktop.ViewModels;
using LinkDeck.Desktop.Views;

namespace LinkDeck.Desktop.Tests;

public class ReorderTests : IDisposable
{
    // Work: jira, wiki, prod — Home: docs
    private readonly DeckFixture _f = new(DeckFixture.Sample(SortMode.Manual));

    public void Dispose() => _f.Dispose();

    private string[] Ids(string groupId) => [.. _f.Deck.Config.Groups.Single(g => g.Id == groupId).Items.Select(i => i.Id)];

    [Fact]
    public void Move_before_and_after_within_a_group()
    {
        Assert.True(_f.Deck.MoveItemNextTo("prod", "jira", after: false, groupId: "work"));
        Assert.Equal(["prod", "jira", "wiki"], Ids("work"));

        Assert.True(_f.Deck.MoveItemNextTo("prod", "wiki", after: true, groupId: "work"));
        Assert.Equal(["jira", "wiki", "prod"], Ids("work"));
    }

    [Fact]
    public void Arranging_on_All_never_changes_group_membership()
    {
        Assert.True(_f.Deck.MoveItemNextTo("docs", "jira", after: false, groupId: null));

        Assert.Equal(["jira", "wiki", "prod"], Ids("work"));
        Assert.Equal(["docs"], Ids("home"));
        Assert.Equal(["docs", "jira", "wiki", "prod"], _f.Deck.Config.ItemsInAllOrder().Select(i => i.Id));
    }

    [Fact]
    public void Arranging_in_a_group_only_reorders_that_group_and_rejects_other_groups_items()
    {
        Assert.True(_f.Deck.MoveItemNextTo("prod", "jira", after: false, groupId: "work"));
        Assert.Equal(["prod", "jira", "wiki"], Ids("work"));

        Assert.False(_f.Deck.MoveItemNextTo("docs", "jira", after: false, groupId: "work"));
        Assert.Equal(["docs"], Ids("home"));
    }

    [Fact]
    public void All_order_survives_save_and_reload_and_ignores_unknown_ids()
    {
        _f.Deck.MoveItemNextTo("docs", "jira", after: false);

        using var again = new LinkDeck.Core.Config.ConfigStore(_f.Store.ConfigPath);
        again.Load();
        Assert.Equal(["docs", "jira", "wiki", "prod"], again.Current.ItemsInAllOrder().Select(i => i.Id));

        var withGhost = again.Current with { AllOrder = ["ghost", "prod"] };
        Assert.Equal(["prod", "jira", "wiki", "docs"], withGhost.ItemsInAllOrder().Select(i => i.Id));
    }

    [Fact]
    public void All_tab_shows_the_all_order_and_group_tabs_their_own_order()
    {
        _f.Deck.MoveItemNextTo("docs", "jira", after: false);
        var vm = new PopupViewModel(_f.Deck, null);

        Assert.Equal(["docs", "jira", "wiki", "prod"], vm.Items.Select(t => t.Item.Id));
        vm.SelectGroupTab("work");
        Assert.Equal(["jira", "wiki", "prod"], vm.Items.Select(t => t.Item.Id));
    }

    [Fact]
    public void Arranging_by_hand_switches_to_manual_order()
    {
        using var f = new DeckFixture(DeckFixture.Sample(SortMode.Frecency));

        Assert.True(f.Deck.MoveItemNextTo("prod", "jira", after: false));

        Assert.Equal(SortMode.Manual, f.Deck.Config.Settings.SortMode);
    }

    [Fact]
    public void Invalid_moves_do_nothing()
    {
        Assert.False(_f.Deck.MoveItemNextTo("jira", "jira", after: false));
        Assert.False(_f.Deck.MoveItemNextTo("missing", "jira", after: false));
        Assert.False(_f.Deck.MoveItemNextTo("jira", "missing", after: false));
    }

    [AvaloniaFact]
    public void Ctrl_shift_arrows_move_the_selected_tile_and_keep_it_selected()
    {
        var (window, vm, controller) = Create(_f);
        controller.ShowInteractive();
        vm.SelectedIndex = 0; // jira

        window.KeyPressQwerty(PhysicalKey.ArrowRight, RawInputModifiers.Control | RawInputModifiers.Shift);

        Assert.Equal(["wiki", "jira", "prod", "docs"], _f.Deck.Config.ItemsInAllOrder().Select(i => i.Id));
        Assert.Equal(["jira", "wiki", "prod"], Ids("work")); // groups untouched
        Assert.Equal("jira", vm.SelectedItem!.Item.Id);
        Assert.Equal(1, vm.SelectedIndex);
    }

    [AvaloniaFact]
    public void First_arrangement_in_frecency_mode_tells_the_user_once()
    {
        using var f = new DeckFixture(DeckFixture.Sample(SortMode.Frecency));
        var (_, vm, controller) = Create(f);
        controller.ShowInteractive();
        vm.SelectedIndex = 0;

        vm.MoveSelected(+1);
        vm.MoveSelected(+1);

        Assert.Single(f.Notifier.Shown, n => n.Message.Contains("manual order", StringComparison.Ordinal));
        Assert.True(vm.IsManualOrder);
    }

    [Fact]
    public void No_arranging_while_searching()
    {
        var vm = new PopupViewModel(_f.Deck, null) { SearchText = "i" };
        var raised = false;
        vm.ReorderRequested += (_, _) => raised = true;

        vm.MoveSelected(+1);

        Assert.False(vm.CanReorder);
        Assert.False(raised);
    }

    [Fact]
    public void Manual_order_toggle_switches_the_sort_mode()
    {
        var vm = new PopupViewModel(_f.Deck, null);

        vm.ToggleManualOrder();
        Assert.Equal(SortMode.Frecency, _f.Deck.Config.Settings.SortMode);
        vm.ToggleManualOrder();
        Assert.Equal(SortMode.Manual, _f.Deck.Config.Settings.SortMode);
    }

    private static (PopupWindow Window, PopupViewModel Vm, PopupController Controller) Create(DeckFixture f)
    {
        var vm = new PopupViewModel(f.Deck, null);
        var window = new PopupWindow { DataContext = vm };
        var controller = new PopupController(window, vm, f.Deck, new FakeHotkey(), null, new DropParser(null), null, cyclingShowDelay: TimeSpan.Zero);
        return (window, vm, controller);
    }
}
