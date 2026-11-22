using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using LinkDeck.Core.Abstractions;
using LinkDeck.Core.Config;
using LinkDeck.Core.Import;
using LinkDeck.Desktop.Services;
using LinkDeck.Desktop.ViewModels;

namespace LinkDeck.Desktop.Views;

public partial class SettingsWindow : Window
{
    private const string ReorderFormat = "application/x-linkdeck-reorder";

    private Point? _dragStart;
    private ListBox? _dragList;
    private int _dragIndex = -1;

    public SettingsWindow()
    {
        InitializeComponent();
        AddHandler(DragDrop.DragOverEvent, OnDragOver);
        AddHandler(DragDrop.DropEvent, OnDrop);
        foreach (var list in new[] { GroupList, ItemList })
        {
            list.AddHandler(PointerPressedEvent, OnListPointerPressed, RoutingStrategies.Tunnel, handledEventsToo: true);
            list.AddHandler(PointerMovedEvent, OnListPointerMoved, RoutingStrategies.Tunnel, handledEventsToo: true);
            list.AddHandler(PointerReleasedEvent, (_, _) => _dragStart = null, RoutingStrategies.Tunnel, handledEventsToo: true);
        }

        Closing += OnClosing;
        BackButton.Click += (_, _) => GoBack();
        GroupColorButton.Click += async (_, _) =>
        {
            if (Vm?.SelectedGroup is { } group && await ColorDialog.ShowAsync($"Colour for the group '{group.Name}'", group.Color, null) is { } choice)
            {
                group.Color = choice.Hex ?? "";
            }
        };
        KeyDown += (_, e) =>
        {
            // Bubbling: an open drop-down or text box gets Esc first.
            if (e.Key == Key.Escape && !e.Handled)
            {
                e.Handled = true;
                GoBack();
            }
        };
    }

    /// <summary>"Choose…" next to the selected item's tile colour: opens the colour wheel.</summary>
    private async void OnChooseItemColor(object? sender, RoutedEventArgs e)
    {
        if (Vm?.SelectedItem is { } item && await ColorDialog.ShowAsync($"Tile colour for '{item.Name}'", item.ColorHex, null) is { } choice)
        {
            item.ColorHex = choice.Hex ?? "";
        }
    }

    /// <summary>Back (button or Esc) was used: the app returns to the launcher.</summary>
    public event EventHandler? ReturnToLauncherRequested;

    private bool _closeConfirmed;

    /// <summary>Asks "save changes?" when closing with unsaved edits (true = save). Replaceable for tests.</summary>
    public Func<Task<bool>>? AskSaveChanges { get; set; }

    private static async Task<bool> AskWithPromptAsync() =>
        await PromptWindow.ShowAsync(
            "Unsaved changes", "Save your changes to LinkDeck settings?", "", input: false, confirmText: "Save", bringToFront: null, cancelText: "Don't save") is not null;

    /// <summary>Saves pending changes (when valid), closes, and asks the app to show the launcher again.</summary>
    public void GoBack()
    {
        if (Vm is { IsDirty: true, CanEdit: true } vm && !vm.Save())
        {
            return; // invalid input: stay, the status line explains why
        }

        _closeConfirmed = true;
        ReturnToLauncherRequested?.Invoke(this, EventArgs.Empty);
        Close();
    }

    /// <summary>Parses dropped data into drafts (set by the app; needs the shortcut resolver).</summary>
    public DropParser? DropParser { get; set; }

    public INotifier? Notifier { get; set; }

    public IShellIdListResolver? ShellItems { get; set; }

    private SettingsViewModel? Vm => DataContext as SettingsViewModel;

    private async void OnClosing(object? sender, WindowClosingEventArgs e)
    {
        if (_closeConfirmed || Vm is not { IsDirty: true, CanEdit: true } vm)
        {
            Vm?.Detach();
            return;
        }

        // Unsaved changes and the window's X was used: ask instead of silently dropping them.
        e.Cancel = true;
        var save = await (AskSaveChanges ?? AskWithPromptAsync)();
        if (save && !vm.Save())
        {
            return; // invalid input: stay open
        }

        _closeConfirmed = true;
        Close();
    }

    // ---- Drag to reorder -------------------------------------------------

    private void OnListPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is not ListBox list || !e.GetCurrentPoint(list).Properties.IsLeftButtonPressed)
        {
            return;
        }

        var container = (e.Source as Visual)?.FindAncestorOfType<ListBoxItem>(includeSelf: true);
        _dragIndex = container is null ? -1 : list.IndexFromContainer(container);
        _dragList = list;
        _dragStart = _dragIndex >= 0 ? e.GetPosition(list) : null;
    }

    private async void OnListPointerMoved(object? sender, PointerEventArgs e)
    {
        if (_dragStart is not { } start || sender is not ListBox list || list != _dragList || Vm is not { CanEdit: true })
        {
            return;
        }

        var delta = e.GetPosition(list) - start;
        if (Math.Abs(delta.X) + Math.Abs(delta.Y) < 8)
        {
            return;
        }

        _dragStart = null;
        var data = new DataObject();
        data.Set(ReorderFormat, $"{list.Name}:{_dragIndex}");
        await DragDrop.DoDragDrop(e, data, DragDropEffects.Move | DragDropEffects.Copy);
    }

    // ---- Drop: reorder or add -------------------------------------------

    private void OnDragOver(object? sender, DragEventArgs e)
    {
        e.DragEffects = e.Data.Contains(ReorderFormat)
            ? (e.KeyModifiers.HasFlag(KeyModifiers.Control) ? DragDropEffects.Copy : DragDropEffects.Move)
            : DropDataReader.CanRead(e.Data) ? DragDropEffects.Copy | DragDropEffects.Link : DragDropEffects.None;
        e.Handled = true;
    }

    private void OnDrop(object? sender, DragEventArgs e)
    {
        e.Handled = true;
        if (Vm is not { } vm)
        {
            return;
        }

        if (e.Data.Get(ReorderFormat) is string token)
        {
            var parts = token.Split(':');
            var list = parts[0] == GroupList.Name ? GroupList : ItemList;
            if (!int.TryParse(parts[1], out var from))
            {
                return;
            }

            var dropOnItem = (e.Source as Visual)?.FindAncestorOfType<ListBoxItem>(includeSelf: true);
            if (list == ItemList && dropOnItem?.FindAncestorOfType<ListBox>() == GroupList
                && vm.SelectedGroup is { } sourceGroup && from < sourceGroup.Items.Count
                && dropOnItem.DataContext is GroupEditorViewModel targetGroup)
            {
                // An item dropped on a group: move it there, or copy it while Ctrl is held.
                vm.TransferItem(sourceGroup.Items[from], targetGroup, copy: e.KeyModifiers.HasFlag(KeyModifiers.Control));
                return;
            }

            var container = (e.Source as Visual)?.FindAncestorOfType<ListBoxItem>(includeSelf: true);
            var to = container is null ? list.ItemCount - 1 : list.IndexFromContainer(container);
            if (container is not null && container.FindAncestorOfType<ListBox>() != list)
            {
                return;
            }

            if (list == GroupList)
            {
                vm.ReorderGroup(from, to);
            }
            else
            {
                vm.ReorderItem(from, to);
            }

            return;
        }

        if (DropParser is null)
        {
            return;
        }

        var result = DropParser.Parse(DropDataReader.Read(e.Data, ShellItems));
        foreach (var draft in result.Drafts)
        {
            vm.AddDraft(draft);
        }

        if (result.Ignored.Count > 0)
        {
            Notifier?.Show(NoticeLevel.Info, $"Ignored {result.Ignored.Count} dropped item(s) LinkDeck cannot open.");
        }
    }
}
