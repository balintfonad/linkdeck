using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using LinkDeck.Core.Abstractions;
using LinkDeck.Core.Import;
using LinkDeck.Core.Models;
using LinkDeck.Desktop.Services;
using LinkDeck.Desktop.ViewModels;
using LinkDeck.Desktop.Views;

namespace LinkDeck.Desktop.Tests;

public class ColorAndOpacityUiTests : IDisposable
{
    private readonly DeckFixture _f = new(DeckFixture.Sample(SortMode.Manual));

    public void Dispose() => _f.Dispose();

    [Fact]
    public void SetItemColor_sets_and_clears()
    {
        Assert.True(_f.Deck.SetItemColor("jira", "#ff0000"));
        Assert.Equal("#FF0000", _f.Deck.Config.AllItems.Single(i => i.Id == "jira").Color);

        Assert.True(_f.Deck.SetItemColor("jira", null));
        Assert.Null(_f.Deck.Config.AllItems.Single(i => i.Id == "jira").Color);
        Assert.False(_f.Deck.SetItemColor("missing", "#ff0000"));
    }

    [Fact]
    public void Tile_shows_the_item_colour()
    {
        _f.Deck.SetItemColor("wiki", "#00AA00");
        var vm = new PopupViewModel(_f.Deck, null);

        var tile = vm.Items.Single(t => t.Item.Id == "wiki");
        Assert.True(tile.HasItemColor);
        Assert.NotNull(tile.Tint);
        Assert.False(vm.Items.Single(t => t.Item.Id == "jira").HasItemColor);
    }

    [AvaloniaFact]
    public async Task Colour_menu_on_a_tile_uses_the_colour_wheel_and_saves()
    {
        var vm = new PopupViewModel(_f.Deck, null);
        var window = new PopupWindow { DataContext = vm };
        var dialogs = new FakeDialogs { ColorResult = new ColorChoice("#123456") };
        var controller = new PopupController(window, vm, _f.Deck, new FakeHotkey(), null, new DropParser(null), null, cyclingShowDelay: TimeSpan.Zero)
        {
            Dialogs = dialogs,
        };
        controller.ShowInteractive();

        vm.Items.Single(t => t.Item.Id == "jira").PickColorCommand.Execute(null);
        await Task.Yield();
        Dispatcher.UIThread.RunJobs();

        Assert.Equal("#123456", _f.Deck.Config.AllItems.Single(i => i.Id == "jira").Color);
        Assert.True(window.IsVisible); // back to the launcher afterwards
    }

    [AvaloniaFact]
    public void Popup_background_uses_the_opacity_setting()
    {
        _f.Deck.TrySave(_f.Deck.Config with { Settings = _f.Deck.Config.Settings with { BackgroundOpacity = 0.55 } });
        var vm = new PopupViewModel(_f.Deck, null);
        var window = new PopupWindow { DataContext = vm };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(0.55, window.Surface.Opacity, 3);
        window.Close();
    }

    [Fact]
    public void Settings_save_opacity_and_item_colour_and_duplicate_keeps_colour()
    {
        var vm = new SettingsViewModel(_f.Deck, new NoRunner());

        vm.BackgroundOpacityPercent = 60;
        vm.SelectedItem!.ColorHex = "#abcdef";
        Assert.True(vm.SelectedItem.HasColor);
        Assert.Equal("#abcdef", vm.SelectedItem.Duplicate().ColorHex);
        Assert.True(vm.Save());

        Assert.Equal(0.6, _f.Deck.Config.Settings.BackgroundOpacity, 3);
        Assert.Equal("#ABCDEF", _f.Deck.Config.AllItems.First().Color);

        vm.SelectedItem!.ClearColorCommand.Execute(null);
        Assert.True(vm.Save());
        Assert.Null(_f.Deck.Config.AllItems.First().Color);
    }

    [Fact]
    public void Colour_dialog_formats_hex() =>
        Assert.Equal("#0A0B0C", ColorDialog.ToHex(Avalonia.Media.Color.FromRgb(10, 11, 12)));

    private sealed class NoRunner : IProcessRunner
    {
        public void Start(System.Diagnostics.ProcessStartInfo startInfo)
        {
        }
    }
}
