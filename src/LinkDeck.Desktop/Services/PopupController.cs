using Avalonia.Controls;
using Avalonia.Input.Platform;
using Avalonia.Threading;
using LinkDeck.Core.Abstractions;
using LinkDeck.Core.Config;
using LinkDeck.Core.Import;
using LinkDeck.Core.Logging;
using LinkDeck.Core.Models;
using LinkDeck.Core.Popup;
using LinkDeck.Desktop.ViewModels;
using LinkDeck.Desktop.Views;

namespace LinkDeck.Desktop.Services;

/// <summary>
/// Wires the hotkey service, the pure <see cref="PopupStateMachine"/>, the popup view model and the window.
/// All methods run on the UI thread.
/// </summary>
public sealed class PopupController
{
    private readonly PopupStateMachine _machine = new();
    private readonly PopupWindow _window;
    private readonly PopupViewModel _vm;
    private readonly DeckService _deck;
    private readonly IForegroundActivator? _activator;
    private readonly DropParser _dropParser;
    private readonly IIconSource? _icons;
    private readonly ILog _log;
    private readonly DispatcherTimer? _showTimer;
    private bool _quickAddOpen;

    /// <param name="cyclingShowDelay">
    /// How long the gesture must be held before the popup appears (default 150 ms), like Alt+Tab.
    /// A quick hold-step-release launches without flashing the popup, and other apps' Ctrl+Alt shortcuts do not flicker it.
    /// </param>
    public PopupController(PopupWindow window, PopupViewModel vm, DeckService deck, IHotkeyService hotkey, IForegroundActivator? activator, DropParser dropParser, IIconSource? icons, ILog? log = null, TimeSpan? cyclingShowDelay = null)
    {
        _window = window;
        _vm = vm;
        _deck = deck;
        _activator = activator;
        _dropParser = dropParser;
        _icons = icons;
        _log = log ?? NullLog.Instance;
        _window.CursorPosition = () => _activator?.GetCursorPosition();

        var delay = cyclingShowDelay ?? TimeSpan.FromMilliseconds(150);
        if (delay > TimeSpan.Zero)
        {
            _showTimer = new DispatcherTimer { Interval = delay };
            _showTimer.Tick += (_, _) =>
            {
                _showTimer.Stop();
                if (_machine.Mode == PopupMode.Cycling)
                {
                    ShowWindow(activate: false);
                }
            };
        }

        ApplySettings();
        deck.ConfigChanged += (_, _) => ApplySettings();

        hotkey.GestureStarted += (_, _) => Apply(_machine.GestureStarted(_vm.AllCount));
        hotkey.Stepped += (_, back) =>
        {
            if (_machine.Mode == PopupMode.Interactive)
            {
                _machine.Step(back);
                _vm.Cycle(back ? -1 : +1);
            }
            else
            {
                Apply(_machine.Step(back));
            }
        };
        hotkey.SpacePressed += (_, _) => Apply(_machine.Space());
        hotkey.HoldReleased += (_, _) => Apply(_machine.HoldReleased());
        hotkey.EscapePressed += (_, _) => Apply(_machine.Escape());
        hotkey.Cancelled += (_, _) => Apply(_machine.Cancel());
        hotkey.TabPressed += (_, back) =>
        {
            // Switching group while holding: the list changes, so stop cycling and keep the popup open.
            Apply(_machine.Space());
            if (_machine.Mode == PopupMode.Interactive)
            {
                _vm.NextTab(back ? -1 : +1);
            }
        };
        _window.ClickedWhileCycling += (_, _) => Apply(_machine.Space());

        _window.EscapeRequested += (_, _) => Apply(_machine.Escape());
        _window.FocusLost += (_, _) =>
        {
            _log.Write(LogLevel.Debug, $"Popup deactivated in {_machine.Mode} mode.");
            Apply(_machine.FocusLost());
        };
        _window.DataDropped += async (_, data) => await OnDropAsync(data);

        _vm.LaunchRequested += (_, item) => LaunchAndHide(item);
        _vm.CopyRequested += async (_, item) => await CopyAsync(item);
        _vm.RemoveRequested += (_, item) => Remove(item);
        _vm.ReorderRequested += (_, move) =>
        {
            var wasManual = _deck.Config.Settings.SortMode == SortMode.Manual;
            if (_deck.MoveItemNextTo(move.Item.Id, move.Target.Id, move.After, move.GroupId) && !wasManual)
            {
                _deck.Report(new ConfigNotice(NoticeLevel.Info, "Switched to manual order so your arrangement is kept (☰ menu → Manual order to switch back)."));
            }
        };
        _vm.ColorRequested += async (_, item) => await PickColorAsync(item);
        _vm.SettingsRequested += (_, _) =>
        {
            // Hide even in keep-open mode: the topmost popup would cover the Settings window.
            Apply(_machine.Hidden());
            SettingsRequested?.Invoke(this, EventArgs.Empty);
        };
        _vm.NewGroupRequested += async (_, _) => await NewGroupAsync();
        _vm.RenameGroupRequested += async (_, id) => await RenameGroupAsync(id);
        _vm.DeleteGroupRequested += async (_, id) => await DeleteGroupAsync(id);
        _vm.MoveItemRequested += (_, move) => MoveItem(move.Item, move.GroupId);
        _vm.CopyItemRequested += (_, copy) => CopyItem(copy.Item, copy.GroupId);
        _vm.EditRequested += (_, item) =>
        {
            Apply(_machine.Hidden());
            EditRequested?.Invoke(this, item);
        };
        _vm.QuickAddRequested += async (_, _) => await QuickAddAsync([null], _vm.CurrentGroupId);
    }

    /// <summary>The user chose "Edit…" on a tile; the app opens Settings on that item.</summary>
    public event EventHandler<Item>? EditRequested;

    /// <summary>The user chose Settings… in the popup menu.</summary>
    public event EventHandler? SettingsRequested;

    /// <summary>Prompts and confirmations for group management (optional; without it those actions do nothing).</summary>
    public IDialogService? Dialogs { get; set; }

    /// <summary>Running and Start-menu apps for the quick-add picker (optional).</summary>
    public IAppCatalog? AppCatalog { get; set; }

    public PopupMode Mode => _machine.Mode;

    private void ApplySettings()
    {
        _machine.KeepOpen = _deck.Config.Settings.KeepOpen;
        _machine.LaunchOnRelease = _deck.Config.Settings.LaunchOnRelease;
    }

    /// <summary>Tray click or a second instance: open in Interactive mode.</summary>
    public void ShowInteractive() => Apply(_machine.ShowInteractive(_vm.AllCount));

    /// <summary>Shows the window once off-screen so the first real show is fast.</summary>
    public void Prewarm()
    {
        _window.Position = new Avalonia.PixelPoint(-32000, -32000);
        _window.ShowActivated = false;
        _window.Show();
        _window.Hide();
    }

    private void Apply(IReadOnlyList<PopupEffect> effects)
    {
        Item? toLaunch = null;
        foreach (var effect in effects)
        {
            switch (effect)
            {
                case PopupEffect.Show show:
                    _vm.Reset();
                    _vm.IsCycling = show.Mode == PopupMode.Cycling;
                    if (show.Mode == PopupMode.Interactive)
                    {
                        ShowWindow(activate: true);
                        _window.FocusSearch();
                    }
                    else if (_showTimer is null)
                    {
                        ShowWindow(activate: false);
                    }
                    else
                    {
                        _showTimer.Start();
                    }

                    break;
                case PopupEffect.Select select:
                    _vm.SelectedIndex = select.Index;
                    break;
                case PopupEffect.EnterInteractive:
                    _showTimer?.Stop();
                    _vm.IsCycling = false;
                    if (!_window.IsVisible)
                    {
                        ShowWindow(activate: true);
                    }
                    else
                    {
                        Activate();
                    }

                    _window.FocusSearch();
                    break;
                case PopupEffect.Launch launch:
                    toLaunch = _vm.ItemAt(launch.Index);
                    break;
                case PopupEffect.LaunchSelection:
                    // Read before a following Hide resets the view model.
                    toLaunch = _vm.SelectedItem?.Item;
                    break;
                case PopupEffect.Hide:
                    HideWindow();
                    break;
            }
        }

        if (toLaunch is not null)
        {
            _ = LaunchAsync(toLaunch);
        }
    }

    /// <summary>
    /// Launches an item. One added or changed outside LinkDeck is first shown in full (program, arguments, folder,
    /// address or computer) and opens only when the user confirms; it is trusted from then on.
    /// </summary>
    public async Task LaunchAsync(Item item)
    {
        if (!_deck.NeedsConfirmation(item))
        {
            _deck.Launch(item);
            return;
        }

        if (Dialogs is null)
        {
            _deck.Launch(item); // refused and reported: nothing can ask the user
            return;
        }

        if (await Dialogs.ConfirmAsync("Confirm before opening", DescribeForConfirmation(item), "Open"))
        {
            _deck.Launch(item, confirmed: true);
        }
    }

    internal static string DescribeForConfirmation(Item item)
    {
        var details = item switch
        {
            PathItem p => $"Program or file: {p.Path}"
                + (string.IsNullOrWhiteSpace(p.Args) ? "" : $"\nArguments: {p.Args}")
                + (string.IsNullOrWhiteSpace(p.WorkingDir) ? "" : $"\nStart in: {p.WorkingDir}"),
            UrlItem u => $"Address: {u.Url}",
            RdpItem r => $"Computer: {r.Target}" + (string.IsNullOrEmpty(r.Username) ? "" : $"\nUser: {r.Username}"),
            _ => item.Target,
        };
        return $"'{item.Name}' was added or changed outside LinkDeck, for example by another program editing config.json. "
            + "Open it only if you recognise exactly what it runs:\n\n" + details;
    }

    /// <summary>
    /// While cycling the popup is shown without taking focus: keys are read by the global hook, and the
    /// foreground app keeps focus in case the gesture is cancelled (someone else's Ctrl+Alt shortcut).
    /// </summary>
    private void ShowWindow(bool activate)
    {
        _log.Write(LogLevel.Debug, activate ? "Popup shown (interactive)." : "Popup shown (cycling).");
        _window.ShowActivated = activate;
        _window.PlaceOnScreen(_activator?.GetCursorPosition());
        _window.Show();
        if (activate)
        {
            Activate();
        }

        // Size is known after layout: re-centre once measured.
        Dispatcher.UIThread.Post(() => _window.PlaceOnScreen(_activator?.GetCursorPosition()), DispatcherPriority.Loaded);
    }

    private void Activate()
    {
        _window.Activate();
        if (_activator is not null && _window.TryGetPlatformHandle() is { } handle)
        {
            _activator.Activate(handle.Handle);
        }
    }

    private void HideWindow()
    {
        _showTimer?.Stop();
        if (_window.IsVisible)
        {
            _log.Write(LogLevel.Debug, "Popup hidden.");
            _window.Hide();
        }

        _vm.IsCycling = false;
        _vm.Reset();
    }

    public async Task NewGroupAsync()
    {
        if (Dialogs is null)
        {
            return;
        }

        Apply(_machine.Hidden());
        var name = await Dialogs.PromptAsync("New group", "Name of the new group:");
        var group = name is null ? null : _deck.AddGroup(name);
        ShowInteractive();
        if (group is not null)
        {
            _vm.SelectGroupTab(group.Id);
        }
    }

    private async Task RenameGroupAsync(string groupId)
    {
        var group = _deck.Config.Groups.FirstOrDefault(g => g.Id == groupId);
        if (Dialogs is null || group is null)
        {
            return;
        }

        Apply(_machine.Hidden());
        var name = await Dialogs.PromptAsync("Rename group", "New name:", group.Name);
        if (name is not null)
        {
            _deck.RenameGroup(groupId, name);
        }

        ShowInteractive();
        _vm.SelectGroupTab(groupId);
    }

    private async Task DeleteGroupAsync(string groupId)
    {
        var group = _deck.Config.Groups.FirstOrDefault(g => g.Id == groupId);
        if (Dialogs is null || group is null)
        {
            return;
        }

        var fallback = _deck.FallbackGroupFor(groupId);
        var count = group.Items.Count;
        var message = count == 0
            ? $"Delete the group '{group.Name}'?"
            : fallback is not null
                ? $"Delete the group '{group.Name}'? Its {count} item(s) will move to '{fallback.Name}'."
                : $"Delete the group '{group.Name}' and its {count} item(s)? It is the only group.";

        Apply(_machine.Hidden());
        if (await Dialogs.ConfirmAsync("Delete group", message, "Delete") && _deck.DeleteGroup(groupId))
        {
            _deck.Report(new ConfigNotice(NoticeLevel.Info, $"Deleted the group '{group.Name}'."));
        }

        ShowInteractive();
    }

    private async Task PickColorAsync(Item item)
    {
        if (Dialogs is null)
        {
            return;
        }

        Apply(_machine.Hidden());
        var choice = await Dialogs.PickColorAsync($"Colour for '{item.Name}'", item.Color);
        if (choice is not null)
        {
            _deck.SetItemColor(item.Id, choice.Hex);
        }

        ShowInteractive();
    }

    private void CopyItem(Item item, string groupId)
    {
        var target = _deck.Config.Groups.FirstOrDefault(g => g.Id == groupId);
        if (target is not null && _deck.CopyItem(item.Id, groupId) is not null)
        {
            _deck.Report(new ConfigNotice(NoticeLevel.Info, $"Copied '{item.Name}' to '{target.Name}'."));
        }
    }

    private void MoveItem(Item item, string groupId)
    {
        var target = _deck.Config.Groups.FirstOrDefault(g => g.Id == groupId);
        if (target is not null && _deck.MoveItem(item.Id, groupId))
        {
            _deck.Report(new ConfigNotice(NoticeLevel.Info, $"Moved '{item.Name}' to '{target.Name}'."));
        }
    }

    private void Remove(Item item)
    {
        if (_deck.RemoveItem(item.Id))
        {
            _deck.Report(new ConfigNotice(NoticeLevel.Info, $"Removed '{item.Name}'. A backup of the previous configuration is in config.json.bak."));
        }

        if (_machine.Mode == PopupMode.Interactive)
        {
            Activate();
            _window.FocusSearch();
        }
    }

    private void LaunchAndHide(Item item)
    {
        if (!_machine.KeepOpen)
        {
            Apply(_machine.Hidden());
        }

        _ = LaunchAsync(item);
    }

    private async Task CopyAsync(Item item)
    {
        var clipboard = TopLevel.GetTopLevel(_window)?.Clipboard;
        if (clipboard is null)
        {
            return;
        }

        await clipboard.SetTextAsync(item.Target);
        _deck.Report(new ConfigNotice(NoticeLevel.Info, $"Copied: {item.Target}"));
    }

    private async Task OnDropAsync(DropData data)
    {
        var result = _dropParser.Parse(data);
        if (result.Ignored.Count > 0)
        {
            _deck.Report(new ConfigNotice(NoticeLevel.Info, "LinkDeck can only add links, files, folders, shortcuts and .rdp files."));
        }

        if (!result.IsEmpty)
        {
            await QuickAddAsync([.. result.Drafts], _vm.CurrentGroupId);
        }
    }

    /// <summary>Hides the popup, then opens the quick-add dialog once per draft (null = empty dialog).</summary>
    public async Task QuickAddAsync(IReadOnlyList<ItemDraft?> drafts, string? groupId = null)
    {
        if (_quickAddOpen)
        {
            return;
        }

        _quickAddOpen = true;
        var reopen = _machine.Mode != PopupMode.Hidden;
        try
        {
            Apply(_machine.Hidden());
            foreach (var draft in drafts)
            {
                var vm = new QuickAddViewModel(_deck, draft, groupId, AppCatalog);
                var saved = await (QuickAddDialog ?? ShowQuickAddWindowAsync)(vm);
                if (saved && vm.SavedItem is UrlItem url && _icons is not null)
                {
                    _ = _icons.RefreshFaviconAsync(url);
                }
            }
        }
        finally
        {
            _quickAddOpen = false;
        }

        // Started from the open launcher: bring it back instead of leaving the user with nothing.
        if (reopen)
        {
            ShowInteractive();
            if (groupId is not null)
            {
                _vm.SelectGroupTab(groupId);
            }
        }
    }

    /// <summary>Replaces the quick-add window (tests). Returns true when an item was saved.</summary>
    public Func<QuickAddViewModel, Task<bool>>? QuickAddDialog { get; set; }

    private Task<bool> ShowQuickAddWindowAsync(QuickAddViewModel vm)
    {
        var dialog = new QuickAddWindow { DataContext = vm, DropParser = _dropParser };
        WindowPlacement.CenterOnActiveScreen(dialog);
        dialog.Show();
        dialog.Activate();
        if (_activator is not null && dialog.TryGetPlatformHandle() is { } h)
        {
            _activator.Activate(h.Handle);
        }

        return dialog.Result;
    }
}
