using System.Collections.ObjectModel;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LinkDeck.Core.Models;
using LinkDeck.Core.Search;
using LinkDeck.Desktop.Services;

namespace LinkDeck.Desktop.ViewModels;

/// <summary>State of the popup: tabs, search, ranked tiles and selection. Keyboard routing lives in the view.</summary>
public sealed partial class PopupViewModel : ObservableObject
{
    /// <summary>Cell size of one tile in the grid (tile plus its margin).</summary>
    public const double TileWidth = 122;

    public const double TileHeight = 112;
    public const string KeyHints = "Enter open · Shift+Enter copy · Del remove · Ctrl+Shift+arrows / drag arrange · Tab group · Esc close";

    private readonly DeckService _deck;
    private readonly IIconSource? _icons;
    private bool _suppressRefresh;
    private string? _reselectId;

    public PopupViewModel(DeckService deck, IIconSource? icons)
    {
        _deck = deck;
        _icons = icons;
        _deck.ConfigChanged += (_, _) => RebuildTabs();
        RebuildTabs();
    }

    /// <summary>The user asked to launch an item (Enter, Ctrl+N, click).</summary>
    public event EventHandler<Item>? LaunchRequested;

    /// <summary>The user asked to copy an item's target (Shift+Enter).</summary>
    public event EventHandler<Item>? CopyRequested;

    /// <summary>The user asked to remove an item (Del, context menu).</summary>
    public event EventHandler<Item>? RemoveRequested;

    /// <summary>The user asked to edit an item (context menu).</summary>
    public event EventHandler<Item>? EditRequested;

    public event EventHandler? QuickAddRequested;

    /// <summary>The user arranged an item next to another (drag and drop, Ctrl+Shift+arrows).</summary>
    public event EventHandler<(Item Item, Item Target, bool After, string? GroupId)>? ReorderRequested;

    /// <summary>The user chose Color… on a tile.</summary>
    public event EventHandler<Item>? ColorRequested;

    /// <summary>The user chose Settings… in the menu.</summary>
    public event EventHandler? SettingsRequested;

    public event EventHandler? NewGroupRequested;

    /// <summary>Argument: group id.</summary>
    public event EventHandler<string>? RenameGroupRequested;

    /// <summary>Argument: group id.</summary>
    public event EventHandler<string>? DeleteGroupRequested;

    /// <summary>Argument: the item and the target group id.</summary>
    public event EventHandler<(Item Item, string GroupId)>? MoveItemRequested;

    /// <summary>Argument: the item and the target group id.</summary>
    public event EventHandler<(Item Item, string GroupId)>? CopyItemRequested;

    public ObservableCollection<TabViewModel> Tabs { get; } = [];

    public ObservableCollection<ItemTileViewModel> Items { get; } = [];

    [ObservableProperty]
    public partial string SearchText { get; set; } = "";

    [ObservableProperty]
    public partial int SelectedTabIndex { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SelectedItem), nameof(FooterTarget))]
    public partial int SelectedIndex { get; set; } = -1;

    /// <summary>Tiles per row; follows the popup width (see <see cref="UpdateColumns"/>).</summary>
    [ObservableProperty]
    public partial int Columns { get; set; } = 6;

    /// <summary>Launcher width in tile columns (setting).</summary>
    [ObservableProperty]
    public partial int PopupColumns { get; set; } = AppSettings.DefaultPopupColumns;

    /// <summary>Launcher height in tile rows (setting); 0 = fit the content.</summary>
    [ObservableProperty]
    public partial int PopupRows { get; set; }

    [ObservableProperty]
    public partial BlurMode Blur { get; set; } = BlurMode.None;

    /// <summary>Recomputes <see cref="Columns"/> for the width available to the tile grid.</summary>
    public void UpdateColumns(double gridWidth) =>
        Columns = Math.Max(2, (int)Math.Floor(gridWidth / TileWidth));

    /// <summary>Stores a size the user dragged the launcher to, in whole tiles (columns × rows).</summary>
    public void SaveGrid(int columns, int rows)
    {
        var settings = _deck.Config.Settings;
        columns = Math.Clamp(columns, AppSettings.MinPopupColumns, AppSettings.MaxPopupColumns);
        rows = Math.Clamp(rows, 1, AppSettings.MaxPopupRows);
        if (settings.PopupColumns != columns || settings.PopupRows != rows)
        {
            _deck.TrySave(_deck.Config with { Settings = settings with { PopupColumns = columns, PopupRows = rows } });
        }
    }

    /// <summary>Opacity of the popup background (setting); tiles and text stay opaque.</summary>
    [ObservableProperty]
    public partial double BackgroundOpacity { get; set; } = 1.0;

    /// <summary>Keep-open mode (mirrors the setting; toggled from the menu).</summary>
    [ObservableProperty]
    public partial bool KeepOpen { get; set; }

    /// <summary>True while cycling (hold-and-step): the search box is hidden.</summary>
    [ObservableProperty]
    public partial bool IsCycling { get; set; }

    public bool IsReadOnly => _deck.IsReadOnly;

    public ItemTileViewModel? SelectedItem => SelectedIndex >= 0 && SelectedIndex < Items.Count ? Items[SelectedIndex] : null;

    public string FooterTarget => SelectedItem?.Item.DisplayTarget ?? (Items.Count == 0 ? "No items. Press + or drop a link, file, folder or app here." : "");

    public string FooterHints => KeyHints;

    public bool IsEmpty => Items.Count == 0;

    /// <summary>The group of the selected tab (null on "All"); new items go there.</summary>
    public string? CurrentGroupId => SelectedTabIndex >= 0 && SelectedTabIndex < Tabs.Count ? Tabs[SelectedTabIndex].GroupId : null;

    /// <summary>Number of items in the "All" list (what cycling iterates).</summary>
    public int AllCount => _deck.Config.AllItems.Count();

    /// <summary>Back to the initial state: "All" tab, empty search, first item selected.</summary>
    public void Reset()
    {
        _suppressRefresh = true;
        SearchText = "";
        SelectedTabIndex = 0;
        _suppressRefresh = false;
        Refresh();
    }

    public void Refresh()
    {
        if (_suppressRefresh)
        {
            return;
        }

        var config = _deck.Config;
        var tab = SelectedTabIndex >= 0 && SelectedTabIndex < Tabs.Count ? Tabs[SelectedTabIndex] : null;
        var colorById = new Dictionary<string, string?>(StringComparer.Ordinal);
        foreach (var g in config.Groups)
        {
            foreach (var i in g.Items)
            {
                colorById.TryAdd(i.Id, g.Color);
            }
        }

        // "All" uses its own hand-arranged order; a group tab uses the group's order.
        var items = tab?.GroupId is { } gid
            ? config.Groups.Where(g => g.Id == gid).SelectMany(g => g.Items).ToList()
            : [.. config.ItemsInAllOrder().Where(i => tab?.Matches(i, config) ?? true)];
        var ranked = Ranker.Rank(items, SearchText, _deck.Usage, config.Settings.SortMode, _deck.Time.GetUtcNow());

        var previous = Items.ToDictionary(t => t.Item.Id, StringComparer.Ordinal);
        Items.Clear();
        for (var n = 0; n < ranked.Count; n++)
        {
            var item = ranked[n];
            var tile = previous.TryGetValue(item.Id, out var existing) && existing.Item == item
                ? existing
                : new ItemTileViewModel(item, colorById.GetValueOrDefault(item.Id), this);
            tile.ShortcutHint = n < 9 ? $"Ctrl+{n + 1}" : null;
            tile.IsSelected = false;
            Items.Add(tile);
            if (tile.Icon is null)
            {
                _ = LoadIconAsync(tile);
            }
        }

        var keep = _reselectId is null ? -1 : Items.ToList().FindIndex(t => t.Item.Id == _reselectId);
        _reselectId = null;
        SelectedIndex = keep >= 0 ? keep : Items.Count > 0 ? 0 : -1;
        UpdateSelectionFlags();
        OnPropertyChanged(nameof(IsEmpty));
        OnPropertyChanged(nameof(FooterTarget));
    }

    public Item? ItemAt(int index) => index >= 0 && index < Items.Count ? Items[index].Item : null;

    /// <summary>Moves the selection in the tile grid; clamps at the edges.</summary>
    public void Move(int dx, int dy)
    {
        if (Items.Count == 0)
        {
            return;
        }

        var index = Math.Max(SelectedIndex, 0) + dx + (dy * Columns);
        SelectedIndex = Math.Clamp(index, 0, Items.Count - 1);
    }

    /// <summary>Hotkey step (Ctrl tap while holding Alt): next/previous item, wrapping around at either end.</summary>
    public void Cycle(int delta)
    {
        if (Items.Count == 0)
        {
            return;
        }

        if (SelectedIndex < 0)
        {
            SelectedIndex = delta > 0 ? 0 : Items.Count - 1;
            return;
        }

        SelectedIndex = ((SelectedIndex + delta) % Items.Count + Items.Count) % Items.Count;
    }

    public void NextTab(int delta)
    {
        if (Tabs.Count == 0)
        {
            return;
        }

        SelectedTabIndex = ((SelectedTabIndex + delta) % Tabs.Count + Tabs.Count) % Tabs.Count;
    }

    [RelayCommand]
    public void LaunchSelected()
    {
        if (SelectedItem is { } tile)
        {
            LaunchRequested?.Invoke(this, tile.Item);
        }
    }

    [RelayCommand]
    public void LaunchTile(ItemTileViewModel? tile)
    {
        if (tile is not null)
        {
            LaunchRequested?.Invoke(this, tile.Item);
        }
    }

    /// <summary>Ctrl+1…9: launch the Nth visible item (1-based).</summary>
    public bool LaunchNth(int n)
    {
        var item = ItemAt(n - 1);
        if (item is null)
        {
            return false;
        }

        LaunchRequested?.Invoke(this, item);
        return true;
    }

    public void CopySelected()
    {
        if (SelectedItem is { } tile)
        {
            CopyRequested?.Invoke(this, tile.Item);
        }
    }

    public void RemoveSelected()
    {
        if (SelectedItem is { } tile)
        {
            RemoveRequested?.Invoke(this, tile.Item);
        }
    }

    internal void Copy(Item item) => CopyRequested?.Invoke(this, item);

    internal void Remove(Item item) => RemoveRequested?.Invoke(this, item);

    internal void Edit(Item item) => EditRequested?.Invoke(this, item);

    internal void PickColor(Item item) => ColorRequested?.Invoke(this, item);

    internal void Launch(Item item) => LaunchRequested?.Invoke(this, item);

    /// <summary>Arranging is possible only on the unfiltered list (no search text).</summary>
    public bool CanReorder => string.IsNullOrEmpty(SearchText) && !IsCycling;

    /// <summary>True when the empty-search order is manual (arranged by hand) rather than most-used-first.</summary>
    public bool IsManualOrder => _deck.Config.Settings.SortMode == SortMode.Manual;

    /// <summary>Moves <paramref name="item"/> before or after <paramref name="target"/> and keeps it selected.</summary>
    public void Reorder(Item item, Item target, bool after)
    {
        if (!CanReorder || item.Id == target.Id)
        {
            return;
        }

        _reselectId = item.Id;
        ReorderRequested?.Invoke(this, (item, target, after, CurrentGroupId));
    }

    /// <summary>Ctrl+Shift+arrow: moves the selected item by <paramref name="delta"/> places in the visible order.</summary>
    public void MoveSelected(int delta)
    {
        if (SelectedItem is not { } tile || Items.Count < 2)
        {
            return;
        }

        var to = Math.Clamp(SelectedIndex + delta, 0, Items.Count - 1);
        if (to != SelectedIndex)
        {
            Reorder(tile.Item, Items[to].Item, after: delta > 0);
        }
    }

    /// <summary>Switches between manual order and most-used-first.</summary>
    public void ToggleManualOrder()
    {
        var settings = _deck.Config.Settings;
        _deck.TrySave(_deck.Config with { Settings = settings with { SortMode = IsManualOrder ? SortMode.Frecency : SortMode.Manual } });
    }

    [RelayCommand]
    public void OpenSettings() => SettingsRequested?.Invoke(this, EventArgs.Empty);

    [RelayCommand]
    public void ToggleKeepOpen() => KeepOpen = !KeepOpen;

    partial void OnKeepOpenChanged(bool value)
    {
        var settings = _deck.Config.Settings;
        if (settings.KeepOpen != value)
        {
            _deck.TrySave(_deck.Config with { Settings = settings with { KeepOpen = value } });
        }
    }

    [RelayCommand]
    public void NewGroup() => NewGroupRequested?.Invoke(this, EventArgs.Empty);

    public void RenameGroup(string groupId) => RenameGroupRequested?.Invoke(this, groupId);

    public void DeleteGroup(string groupId) => DeleteGroupRequested?.Invoke(this, groupId);

    public void MoveItem(Item item, string groupId) => MoveItemRequested?.Invoke(this, (item, groupId));

    public void CopyItemTo(Item item, string groupId) => CopyItemRequested?.Invoke(this, (item, groupId));

    /// <summary>Groups an item can be copied to (all groups; a copy in the same group is allowed).</summary>
    public IReadOnlyList<Group> CopyTargets => _deck.Config.Groups;

    /// <summary>Groups an item can be moved to (all except the one that holds it).</summary>
    public IReadOnlyList<Group> MoveTargetsFor(Item item) =>
        [.. _deck.Config.Groups.Where(g => g.Items.All(i => i.Id != item.Id))];

    /// <summary>Shows the tab of a group (after creating or renaming it).</summary>
    public void SelectGroupTab(string groupId)
    {
        var index = Tabs.ToList().FindIndex(t => t.GroupId == groupId);
        if (index >= 0)
        {
            SelectedTabIndex = index;
        }
    }

    [RelayCommand]
    public void QuickAdd() => QuickAddRequested?.Invoke(this, EventArgs.Empty);

    partial void OnSearchTextChanged(string value) => Refresh();

    partial void OnSelectedTabIndexChanged(int value) => Refresh();

    partial void OnSelectedIndexChanged(int value) => UpdateSelectionFlags();

    private void UpdateSelectionFlags()
    {
        for (var i = 0; i < Items.Count; i++)
        {
            Items[i].IsSelected = i == SelectedIndex;
        }
    }

    /// <summary>Tabs: All, then every group (all of them are ordinary, editable groups).</summary>
    private void RebuildTabs()
    {
        var selectedKey = SelectedTabIndex > 0 && SelectedTabIndex < Tabs.Count ? Tabs[SelectedTabIndex].Key : null;
        var config = _deck.Config;
        _suppressRefresh = true;
        Tabs.Clear();
        Tabs.Add(TabViewModel.All);
        foreach (var g in config.Groups)
        {
            Tabs.Add(TabViewModel.ForGroup(g));
        }

        var index = selectedKey is null ? 0 : Tabs.ToList().FindIndex(t => t.Key == selectedKey);
        SelectedTabIndex = Math.Max(0, index);
        _suppressRefresh = false;
        KeepOpen = config.Settings.KeepOpen;
        BackgroundOpacity = config.Settings.BackgroundOpacity;
        Blur = config.Settings.Blur;
        PopupColumns = config.Settings.PopupColumns;
        PopupRows = config.Settings.PopupRows;
        OnPropertyChanged(nameof(IsReadOnly));
        Refresh();
    }

    private async Task LoadIconAsync(ItemTileViewModel tile)
    {
        if (_icons is null)
        {
            return;
        }

        var icon = await _icons.GetIconAsync(tile.Item).ConfigureAwait(false);
        if (icon is not null)
        {
            Dispatcher.UIThread.Post(() => tile.Icon = icon);
        }
    }
}
