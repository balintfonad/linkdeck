using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using LinkDeck.Core.Abstractions;
using LinkDeck.Core.Import;
using LinkDeck.Core.Models;
using LinkDeck.Core.Popup;
using LinkDeck.Desktop.Services;
using LinkDeck.Desktop.ViewModels;
using LinkDeck.Desktop.Views;
using SharpHook.Data;

namespace LinkDeck.Desktop.Tests;

/// <summary>Raises the gesture signals a real hook would raise.</summary>
public sealed class FakeHotkey : IHotkeyService
{
    public event EventHandler? GestureStarted;

    public event EventHandler<bool>? Stepped;

    public event EventHandler? SpacePressed;

    public event EventHandler? EscapePressed;

    public event EventHandler<bool>? TabPressed;

    public event EventHandler? HoldReleased;

    public event EventHandler? Cancelled;

    public bool IsPaused { get; set; }

    /// <summary>Alt+Ctrl pressed.</summary>
    public void Open() => GestureStarted?.Invoke(this, EventArgs.Empty);

    /// <summary>Ctrl tapped again while holding Alt.</summary>
    public void Step(bool back = false) => Stepped?.Invoke(this, back);

    public void Space() => SpacePressed?.Invoke(this, EventArgs.Empty);

    public void Escape() => EscapePressed?.Invoke(this, EventArgs.Empty);

    public void Tab(bool back = false) => TabPressed?.Invoke(this, back);

    /// <summary>Alt released.</summary>
    public void Release() => HoldReleased?.Invoke(this, EventArgs.Empty);

    public void Cancel() => Cancelled?.Invoke(this, EventArgs.Empty);

    public void Start(Hotkey hotkey)
    {
    }

    public void UpdateHotkey(Hotkey hotkey)
    {
    }

    public void Dispose()
    {
    }
}

public class PopupWindowTests : IDisposable
{
    private readonly DeckFixture _f = new(DeckFixture.Sample(SortMode.Manual));

    public void Dispose() => _f.Dispose();

    private (PopupWindow Window, PopupViewModel Vm, PopupController Controller, FakeHotkey Hotkey) Create(TimeSpan? showDelay = null)
    {
        var vm = new PopupViewModel(_f.Deck, null);
        var window = new PopupWindow { DataContext = vm };
        var hotkey = new FakeHotkey();
        var controller = new PopupController(window, vm, _f.Deck, hotkey, null, new DropParser(null), null, cyclingShowDelay: showDelay ?? TimeSpan.Zero);
        return (window, vm, controller, hotkey);
    }

    [AvaloniaFact]
    public void Renders_one_tile_per_item_with_accessible_names()
    {
        var (window, _, controller, _) = Create();

        controller.ShowInteractive();
        Dispatcher.UIThread.RunJobs();

        var tiles = window.GetVisualDescendants().OfType<Button>().Where(b => b.Classes.Contains("tile")).ToList();
        Assert.Equal(4, tiles.Count);
        Assert.StartsWith("Jira, web page", AutomationProperties.GetName(tiles[0]), StringComparison.Ordinal);
        Assert.Contains(tiles, t => t.Classes.Contains("selected"));
    }

    [AvaloniaFact]
    public void Alt_ctrl_then_release_only_opens_the_launcher()
    {
        var (window, _, controller, hotkey) = Create();

        hotkey.Open();
        Assert.True(window.IsVisible);
        Assert.Equal(PopupMode.Cycling, controller.Mode);
        hotkey.Release();

        Assert.True(window.IsVisible);
        Assert.Equal(PopupMode.Interactive, controller.Mode);
        Assert.Empty(_f.Launcher.Launched);
    }

    [AvaloniaFact]
    public void Each_ctrl_tap_steps_and_releasing_alt_launches_the_selection()
    {
        var (window, vm, _, hotkey) = Create();

        hotkey.Open();
        hotkey.Step();
        hotkey.Step();
        Assert.Equal(2, vm.SelectedIndex);
        hotkey.Release();

        Assert.False(window.IsVisible);
        Assert.Equal("prod", Assert.Single(_f.Launcher.Launched).Id);
        Assert.Equal(1, _f.Deck.Usage["prod"].Count);
    }

    [AvaloniaFact]
    public void Shift_ctrl_steps_backwards()
    {
        var (_, vm, _, hotkey) = Create();

        hotkey.Open();
        hotkey.Step(back: true);

        Assert.Equal(3, vm.SelectedIndex);
    }

    [AvaloniaFact]
    public void Space_keeps_the_popup_open_after_alt_is_released()
    {
        var (window, vm, controller, hotkey) = Create();

        hotkey.Open();
        hotkey.Step();
        hotkey.Space();
        hotkey.Release();

        Assert.True(window.IsVisible);
        Assert.Equal(PopupMode.Interactive, controller.Mode);
        Assert.False(vm.IsCycling);
        Assert.Equal(1, vm.SelectedIndex);
        Assert.Empty(_f.Launcher.Launched);
    }

    [AvaloniaFact]
    public void Ctrl_taps_still_move_the_selection_after_space()
    {
        var (_, vm, _, hotkey) = Create();

        hotkey.Open();
        hotkey.Space();
        hotkey.Step();

        Assert.Equal(1, vm.SelectedIndex);
    }

    [AvaloniaFact]
    public void Gesture_on_open_launcher_does_not_step_then_ctrl_taps_wrap_around()
    {
        var (window, vm, controller, hotkey) = Create();
        controller.ShowInteractive();
        Assert.Equal(0, vm.SelectedIndex);

        hotkey.Open();
        Assert.Equal(0, vm.SelectedIndex);

        hotkey.Step();
        hotkey.Step();
        hotkey.Step();
        Assert.Equal(3, vm.SelectedIndex);

        hotkey.Step();
        Assert.Equal(0, vm.SelectedIndex);

        hotkey.Step(back: true);
        Assert.Equal(3, vm.SelectedIndex);
        var expected = vm.SelectedItem!.Item.Id;

        hotkey.Release();
        Assert.False(window.IsVisible);
        Assert.Equal(expected, Assert.Single(_f.Launcher.Launched).Id);
    }

    [AvaloniaFact]
    public void Launch_on_release_off_keeps_the_open_launcher_on_the_selection()
    {
        _f.Deck.TrySave(_f.Deck.Config with { Settings = _f.Deck.Config.Settings with { LaunchOnRelease = false } });
        var (window, vm, controller, hotkey) = Create();
        controller.ShowInteractive();

        hotkey.Open();
        hotkey.Step();
        hotkey.Release();

        Assert.True(window.IsVisible);
        Assert.Equal(PopupMode.Interactive, controller.Mode);
        Assert.Equal(1, vm.SelectedIndex);
        Assert.Empty(_f.Launcher.Launched);
    }

    [AvaloniaFact]
    public void Another_apps_shortcut_cancels_without_launching()
    {
        var (window, _, _, hotkey) = Create();

        hotkey.Open();
        hotkey.Cancel();
        hotkey.Release();

        Assert.False(window.IsVisible);
        Assert.Empty(_f.Launcher.Launched);
    }

    [AvaloniaFact]
    public void Escape_while_holding_cancels()
    {
        var (window, _, _, hotkey) = Create();

        hotkey.Open();
        hotkey.Step();
        hotkey.Escape();
        hotkey.Release();

        Assert.False(window.IsVisible);
        Assert.Empty(_f.Launcher.Launched);
    }

    [AvaloniaFact]
    public void Quick_gesture_launches_without_ever_showing_the_popup()
    {
        var (window, _, _, hotkey) = Create(showDelay: TimeSpan.FromSeconds(5));

        hotkey.Open();
        hotkey.Step();
        Assert.False(window.IsVisible);
        hotkey.Release();

        Assert.False(window.IsVisible);
        Assert.Equal("wiki", Assert.Single(_f.Launcher.Launched).Id);
    }

    [AvaloniaFact]
    public void Space_during_the_show_delay_shows_the_popup_immediately()
    {
        var (window, _, controller, hotkey) = Create(showDelay: TimeSpan.FromSeconds(5));

        hotkey.Open();
        hotkey.Space();

        Assert.True(window.IsVisible);
        Assert.Equal(PopupMode.Interactive, controller.Mode);
    }

    [AvaloniaFact]
    public void Typing_filters_and_enter_launches_the_match()
    {
        var (window, vm, controller, _) = Create();
        controller.ShowInteractive();

        window.KeyTextInput("wiki");
        Dispatcher.UIThread.RunJobs();
        Assert.Equal("wiki", vm.SearchText);
        Assert.Single(vm.Items);

        window.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None);

        Assert.Equal("wiki", Assert.Single(_f.Launcher.Launched).Id);
        Assert.False(window.IsVisible);
        Assert.Equal("", vm.SearchText);
    }

    [AvaloniaFact]
    public void Ctrl_number_launches_the_nth_visible_item()
    {
        var (window, _, controller, _) = Create();
        controller.ShowInteractive();

        window.KeyPressQwerty(PhysicalKey.Digit2, RawInputModifiers.Control);

        Assert.Equal("wiki", Assert.Single(_f.Launcher.Launched).Id);
    }

    [AvaloniaFact]
    public void Arrow_keys_move_the_selection()
    {
        var (window, vm, controller, _) = Create();
        controller.ShowInteractive();

        window.KeyPressQwerty(PhysicalKey.ArrowRight, RawInputModifiers.None);
        window.KeyPressQwerty(PhysicalKey.ArrowRight, RawInputModifiers.None);

        Assert.Equal(2, vm.SelectedIndex);
    }

    [AvaloniaFact]
    public void Escape_hides_but_the_gesture_does_not_close_interactive()
    {
        var (window, _, controller, hotkey) = Create();
        controller.ShowInteractive();

        window.KeyPressQwerty(PhysicalKey.Escape, RawInputModifiers.None);
        Assert.False(window.IsVisible);

        controller.ShowInteractive();
        hotkey.Open();
        hotkey.Release();
        Assert.True(window.IsVisible);
        Assert.Equal(PopupMode.Interactive, controller.Mode);
        Assert.Empty(_f.Launcher.Launched);
    }
}

public class SettingsWindowTests
{
    [AvaloniaFact]
    public void Settings_window_renders_with_groups()
    {
        using var f = new DeckFixture(DeckFixture.Sample());
        var vm = new SettingsViewModel(f.Deck, new NullRunner());
        var window = new SettingsWindow { DataContext = vm };

        window.Show();
        Dispatcher.UIThread.RunJobs();

        var groupList = window.GetVisualDescendants().OfType<ListBox>().First(l => l.Name == "GroupList");
        Assert.Equal(2, groupList.ItemCount);
        window.Close();
    }

    private sealed class NullRunner : IProcessRunner
    {
        public void Start(System.Diagnostics.ProcessStartInfo startInfo)
        {
        }
    }
}

public class HotkeyKeyMappingTests
{
    [Theory]
    [InlineData(KeyCode.VcLeftAlt, GestureKey.LeftAlt)]
    [InlineData(KeyCode.VcRightAlt, GestureKey.RightAlt)]
    [InlineData(KeyCode.VcLeftControl, GestureKey.LeftCtrl)]
    [InlineData(KeyCode.VcRightControl, GestureKey.RightCtrl)]
    [InlineData(KeyCode.VcLeftShift, GestureKey.LeftShift)]
    [InlineData(KeyCode.VcLeftMeta, GestureKey.LeftWin)]
    [InlineData(KeyCode.VcSpace, GestureKey.Space)]
    [InlineData(KeyCode.VcEscape, GestureKey.Escape)]
    [InlineData(KeyCode.VcT, GestureKey.Other)]
    public void SharpHook_key_codes_map_to_gesture_keys(KeyCode code, GestureKey expected) =>
        Assert.Equal(expected, HotkeyService.Map(code));
}
