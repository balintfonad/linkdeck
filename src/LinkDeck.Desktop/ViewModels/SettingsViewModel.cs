using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LinkDeck.Core.Abstractions;
using LinkDeck.Core.Import;
using LinkDeck.Core.Models;
using LinkDeck.Desktop.Services;

namespace LinkDeck.Desktop.ViewModels;

/// <summary>
/// Settings window. Edits a working copy of the configuration; <see cref="Save"/> writes it.
/// </summary>
public sealed partial class SettingsViewModel : ObservableObject
{
    public static readonly IReadOnlyList<string> ThemeNames = ["System", "Light", "Dark"];
    public static readonly IReadOnlyList<string> SortModeNames = ["Most used first (frecency)", "Manual order"];

    private readonly DeckService _deck;
    private readonly IProcessRunner _runner;
    private readonly IIconSource? _icons;
    private AppConfig _loaded;
    private bool _loading;

    public SettingsViewModel(DeckService deck, IProcessRunner runner, IIconSource? icons = null)
    {
        _deck = deck;
        _runner = runner;
        _icons = icons;
        _loaded = deck.Config;
        _deck.ConfigChanged += OnDeckConfigChanged;
        LoadFrom(deck.Config);
    }

    public ObservableCollection<GroupEditorViewModel> Groups { get; } = [];

    public IReadOnlyList<string> Themes => ThemeNames;

    public IReadOnlyList<string> SortModes => SortModeNames;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelectedGroup))]
    public partial GroupEditorViewModel? SelectedGroup { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelectedItem))]
    public partial ItemEditorViewModel? SelectedItem { get; set; }

    [ObservableProperty]
    public partial int ThemeIndex { get; set; }

    [ObservableProperty]
    public partial int SortModeIndex { get; set; }

    [ObservableProperty]
    public partial bool StartWithWindows { get; set; }

    /// <summary>Launcher background opacity in percent (50–100).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(BackgroundOpacityText))]
    public partial double BackgroundOpacityPercent { get; set; } = 100;

    public string BackgroundOpacityText => $"{Math.Round(BackgroundOpacityPercent)} %";

    public static readonly IReadOnlyList<string> BlurNames = ["Off (sharp)", "Soft", "Strong"];

    public IReadOnlyList<string> BlurModes => BlurNames;

    /// <summary>Index into <see cref="BlurModes"/> (matches <see cref="BlurMode"/>).</summary>
    [ObservableProperty]
    public partial int BlurIndex { get; set; } = (int)BlurMode.None;

    /// <summary>Launcher width in tile columns.</summary>
    [ObservableProperty]
    public partial decimal PopupColumns { get; set; } = AppSettings.DefaultPopupColumns;

    /// <summary>True: the launcher height fits its content.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsFixedHeight))]
    public partial bool AutoHeight { get; set; } = true;

    public bool IsFixedHeight => !AutoHeight;

    /// <summary>Launcher height in tile rows (used when <see cref="AutoHeight"/> is off).</summary>
    [ObservableProperty]
    public partial decimal PopupRows { get; set; } = 3;

    /// <summary>Keep the launcher open after launching or clicking elsewhere.</summary>
    [ObservableProperty]
    public partial bool KeepOpen { get; set; }

    /// <summary>Releasing the held key after stepping launches the selection.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HotkeyText))]
    public partial bool LaunchOnRelease { get; set; } = true;

    /// <summary>Index into <see cref="Modifiers"/> of the key held during the gesture.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HotkeyText))]
    public partial int HoldIndex { get; set; }

    /// <summary>Index into <see cref="Modifiers"/> of the key tapped to step.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HotkeyText))]
    public partial int StepIndex { get; set; }

    [ObservableProperty]
    public partial bool IsDirty { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasStatus))]
    public partial string? Status { get; set; }

    /// <summary>The file changed on disk while there were unsaved edits.</summary>
    [ObservableProperty]
    public partial bool HasExternalChange { get; set; }

    public bool HasStatus => !string.IsNullOrEmpty(Status);

    public bool HasSelectedGroup => SelectedGroup is not null;

    public bool HasSelectedItem => SelectedItem is not null;

    public bool IsReadOnly => _deck.IsReadOnly;

    public bool CanEdit => !_deck.IsReadOnly;

    public IReadOnlyList<string> Modifiers => Hotkey.KnownModifiers;

    public Hotkey Hotkey => new() { Hold = Modifiers[Math.Clamp(HoldIndex, 0, Modifiers.Count - 1)], Step = Modifiers[Math.Clamp(StepIndex, 0, Modifiers.Count - 1)] };

    public string HotkeyText => Hotkey.IsValid()
        ? $"Press {Hotkey.Hold}+{Hotkey.Step} to open. Keep holding {Hotkey.Hold}, tap {Hotkey.Step} to move, "
          + (LaunchOnRelease ? $"release {Hotkey.Hold} to open the selection. " : "then press Enter to open the selection. ")
          + "Space keeps the popup open."
        : "The hold and step keys must be different.";

    public string ConfigPath => _deck.ConfigPath;

    [RelayCommand]
    private void AddGroup()
    {
        var group = new GroupEditorViewModel(new Group { Id = Guid.NewGuid().ToString(), Name = "New group" });
        Track(group);
        Groups.Add(group);
        SelectedGroup = group;
        MarkDirty();
    }

    [RelayCommand]
    private void DeleteGroup()
    {
        if (SelectedGroup is not { } group)
        {
            return;
        }

        var index = Groups.IndexOf(group);
        Groups.Remove(group);
        SelectedGroup = Groups.Count == 0 ? null : Groups[Math.Min(index, Groups.Count - 1)];
        MarkDirty();
    }

    [RelayCommand]
    private void MoveGroupUp() => MoveGroup(SelectedGroup, -1);

    [RelayCommand]
    private void MoveGroupDown() => MoveGroup(SelectedGroup, +1);

    [RelayCommand]
    private void AddItem() => AddDraft(new ItemDraft { Type = ItemType.Url, Name = "New item", Target = "https://" });

    /// <summary>Adds a dropped or typed draft to the selected group and selects it.</summary>
    public void AddDraft(ItemDraft draft)
    {
        if (SelectedGroup is null)
        {
            if (Groups.Count == 0)
            {
                AddGroup();
            }

            SelectedGroup = Groups[0];
        }

        var editor = new ItemEditorViewModel(draft);
        Track(editor);
        SelectedGroup!.Items.Add(editor);
        SelectedItem = editor;
        MarkDirty();
    }

    [RelayCommand]
    private void DeleteItem()
    {
        if (SelectedGroup is not { } group || SelectedItem is not { } item)
        {
            return;
        }

        var index = group.Items.IndexOf(item);
        group.Items.Remove(item);
        SelectedItem = group.Items.Count == 0 ? null : group.Items[Math.Min(index, group.Items.Count - 1)];
        MarkDirty();
    }

    [RelayCommand]
    private void MoveItemUp() => MoveItem(SelectedItem, -1);

    [RelayCommand]
    private void MoveItemDown() => MoveItem(SelectedItem, +1);

    /// <summary>Target group for the Move / Copy buttons under the item list.</summary>
    [ObservableProperty]
    public partial GroupEditorViewModel? TransferTarget { get; set; }

    [RelayCommand]
    private void MoveSelectedItem() => TransferSelected(copy: false);

    [RelayCommand]
    private void CopySelectedItem() => TransferSelected(copy: true);

    private void TransferSelected(bool copy)
    {
        if (SelectedItem is { } item && TransferTarget is { } target)
        {
            TransferItem(item, target, copy);
        }
    }

    /// <summary>
    /// Moves an item to another group, or copies it (the copy gets a new id). Also used by dragging an item onto a group.
    /// Returns false when nothing changed (moving onto its own group).
    /// </summary>
    public bool TransferItem(ItemEditorViewModel item, GroupEditorViewModel target, bool copy)
    {
        var source = Groups.FirstOrDefault(g => g.Items.Contains(item));
        if (source is null || (!copy && source == target))
        {
            return false;
        }

        var moved = copy ? item.Duplicate() : item;
        if (!copy)
        {
            source.Items.Remove(item);
        }

        target.Items.Add(moved);
        Track(moved);
        SelectedGroup = target;
        SelectedItem = moved;
        Status = copy ? $"Copied '{item.Name}' to '{target.Name}'. Save to keep it." : $"Moved '{item.Name}' to '{target.Name}'. Save to keep it.";
        IsDirty = true;
        return true;
    }

    /// <summary>
    /// Puts every item into the Web, Apps, Files &amp; folders or Remote desktop group (created when missing).
    /// Other groups are kept (possibly empty). Nothing is written until Save.
    /// </summary>
    [RelayCommand]
    public void SortIntoTypeGroups()
    {
        var colors = new Dictionary<ItemCategory, string>
        {
            [ItemCategory.Web] = "#3B82F6",
            [ItemCategory.App] = "#8B5CF6",
            [ItemCategory.FileOrFolder] = "#F59E0B",
            [ItemCategory.RemoteDesktop] = "#10B981",
        };
        var targets = new Dictionary<ItemCategory, GroupEditorViewModel>();
        foreach (var category in ItemCategories.All)
        {
            var id = ItemCategories.DefaultGroupId(category);
            var group = Groups.FirstOrDefault(g => g.Id == id);
            if (group is null)
            {
                group = new GroupEditorViewModel(new Group { Id = id, Name = ItemCategories.DisplayName(category), Color = colors[category] });
                Track(group);
                Groups.Add(group);
            }

            targets[category] = group;
        }

        var moved = 0;
        foreach (var group in Groups.ToList())
        {
            foreach (var editor in group.Items.ToList())
            {
                var item = editor.ToItem();
                var category = item is null ? ItemCategories.OfDraft(editor.Type, editor.Target) : ItemCategories.Of(item);
                var target = targets[category];
                if (target != group)
                {
                    group.Items.Remove(editor);
                    target.Items.Add(editor);
                    moved++;
                }
            }
        }

        SelectedGroup = targets[ItemCategory.Web];
        IsDirty = true;
        Status = $"Sorted {moved} item(s) into type groups. Empty groups were kept; delete them if you like. Save to keep the result.";
    }

    /// <summary>Drag-and-drop reorder of groups.</summary>
    public void ReorderGroup(int from, int to) => Reorder(Groups, from, to);

    /// <summary>Drag-and-drop reorder of items within the selected group.</summary>
    public void ReorderItem(int from, int to)
    {
        if (SelectedGroup is not null)
        {
            Reorder(SelectedGroup.Items, from, to);
        }
    }

    [RelayCommand]
    private void SaveSettings() => Save();

    public bool Save()
    {
        if (!Hotkey.IsValid())
        {
            Status = "The hold and step keys must be different.";
            return false;
        }

        var groups = new List<Group>();
        foreach (var g in Groups)
        {
            if (!g.IsColorValid)
            {
                SelectedGroup = g;
                Status = $"Group '{g.Name}': colour must be #RRGGBB or empty.";
                return false;
            }

            var items = new List<Item>();
            foreach (var editor in g.Items)
            {
                var item = editor.ToItem();
                if (item is null)
                {
                    SelectedGroup = g;
                    SelectedItem = editor;
                    Status = $"'{editor.Name}': {editor.Error}";
                    return false;
                }

                items.Add(item);
            }

            groups.Add(new Group
            {
                Id = g.Id,
                Name = string.IsNullOrWhiteSpace(g.Name) ? "Unnamed" : g.Name.Trim(),
                Color = string.IsNullOrEmpty(g.Color) ? null : g.Color,
                Items = items,
            });
        }

        var config = _deck.Config with
        {
            Groups = groups,
            Settings = new AppSettings
            {
                Hotkey = Hotkey,
                Theme = (ThemeMode)ThemeIndex,
                SortMode = (SortMode)SortModeIndex,
                StartWithWindows = StartWithWindows,
                KeepOpen = KeepOpen,
                LaunchOnRelease = LaunchOnRelease,
                BackgroundOpacity = Math.Clamp(Math.Round(BackgroundOpacityPercent) / 100.0, AppSettings.MinBackgroundOpacity, 1),
                Blur = (BlurMode)Math.Clamp(BlurIndex, 0, 2),
                PopupColumns = Math.Clamp((int)PopupColumns, AppSettings.MinPopupColumns, AppSettings.MaxPopupColumns),
                PopupRows = AutoHeight ? 0 : Math.Clamp((int)PopupRows, 1, AppSettings.MaxPopupRows),
            },
        };

        var previousUrls = _loaded.AllItems.OfType<UrlItem>().Select(u => u.Url).ToHashSet(StringComparer.Ordinal);
        if (!_deck.TrySave(config))
        {
            Status = _deck.IsReadOnly ? "Saving is disabled (read-only configuration)." : "Saving failed.";
            return false;
        }

        _loaded = _deck.Config;
        IsDirty = false;
        HasExternalChange = false;
        Status = "Saved.";

        if (_icons is not null)
        {
            foreach (var u in config.AllItems.OfType<UrlItem>().Where(u => !previousUrls.Contains(u.Url)))
            {
                _ = _icons.RefreshFaviconAsync(u);
            }
        }

        return true;
    }

    [RelayCommand]
    public void Revert()
    {
        LoadFrom(_deck.Config);
        Status = null;
    }

    [RelayCommand]
    private void OpenConfigFile() => _runner.Start(new ProcessStartInfo(_deck.ConfigPath) { UseShellExecute = true });

    [RelayCommand]
    private void OpenConfigFolder() =>
        _runner.Start(new ProcessStartInfo(Path.GetDirectoryName(_deck.ConfigPath)!) { UseShellExecute = true });

    /// <summary>Selects the group and item with this id (used by the popup's "Edit…").</summary>
    public void SelectItem(string itemId)
    {
        var group = Groups.FirstOrDefault(g => g.Items.Any(i => i.Id == itemId));
        if (group is null)
        {
            return;
        }

        SelectedGroup = group;
        SelectedItem = group.Items.First(i => i.Id == itemId);
    }

    public void Detach() => _deck.ConfigChanged -= OnDeckConfigChanged;

    private void OnDeckConfigChanged(object? sender, EventArgs e)
    {
        if (ReferenceEquals(_deck.Config, _loaded))
        {
            return;
        }

        if (IsDirty)
        {
            HasExternalChange = true;
            Status = "config.json changed on disk. Revert to load it, or Save to overwrite it.";
        }
        else
        {
            LoadFrom(_deck.Config);
        }
    }

    private void LoadFrom(AppConfig config)
    {
        _loading = true;
        _loaded = config;
        var selectedGroupId = SelectedGroup?.Id;
        var selectedItemId = SelectedItem?.Id;
        Groups.Clear();
        foreach (var g in config.Groups)
        {
            var editor = new GroupEditorViewModel(g);
            Track(editor);
            Groups.Add(editor);
        }

        SelectedGroup = Groups.FirstOrDefault(g => g.Id == selectedGroupId) ?? Groups.FirstOrDefault();
        SelectedItem = SelectedGroup?.Items.FirstOrDefault(i => i.Id == selectedItemId) ?? SelectedGroup?.Items.FirstOrDefault();
        ThemeIndex = (int)config.Settings.Theme;
        SortModeIndex = (int)config.Settings.SortMode;
        StartWithWindows = config.Settings.StartWithWindows;
        KeepOpen = config.Settings.KeepOpen;
        LaunchOnRelease = config.Settings.LaunchOnRelease;
        BackgroundOpacityPercent = Math.Round(config.Settings.BackgroundOpacity * 100);
        BlurIndex = (int)config.Settings.Blur;
        PopupColumns = config.Settings.PopupColumns;
        AutoHeight = config.Settings.PopupRows <= 0;
        PopupRows = config.Settings.PopupRows > 0 ? config.Settings.PopupRows : 3;
        HoldIndex = IndexOfModifier(config.Settings.Hotkey.Hold);
        StepIndex = IndexOfModifier(config.Settings.Hotkey.Step);
        IsDirty = false;
        HasExternalChange = false;
        _loading = false;
        OnPropertyChanged(nameof(IsReadOnly));
        OnPropertyChanged(nameof(CanEdit));
    }

    partial void OnSelectedGroupChanged(GroupEditorViewModel? value) => SelectedItem = value?.Items.FirstOrDefault();

    partial void OnThemeIndexChanged(int value) => MarkDirty();

    partial void OnSortModeIndexChanged(int value) => MarkDirty();

    partial void OnStartWithWindowsChanged(bool value) => MarkDirty();

    partial void OnKeepOpenChanged(bool value) => MarkDirty();

    partial void OnLaunchOnReleaseChanged(bool value) => MarkDirty();

    partial void OnBackgroundOpacityPercentChanged(double value) => MarkDirty();

    partial void OnBlurIndexChanged(int value) => MarkDirty();

    partial void OnPopupColumnsChanged(decimal value) => MarkDirty();

    partial void OnAutoHeightChanged(bool value) => MarkDirty();

    partial void OnPopupRowsChanged(decimal value) => MarkDirty();

    partial void OnHoldIndexChanged(int value) => MarkDirty();

    partial void OnStepIndexChanged(int value) => MarkDirty();

    private int IndexOfModifier(string name) =>
        Math.Max(0, Hotkey.KnownModifiers.ToList().FindIndex(m => string.Equals(m, name, StringComparison.OrdinalIgnoreCase)));

    private void Track(GroupEditorViewModel group)
    {
        group.PropertyChanged += OnChildChanged;
        foreach (var item in group.Items)
        {
            Track(item);
        }

        group.Items.CollectionChanged += (_, e) =>
        {
            foreach (var added in e.NewItems?.OfType<ItemEditorViewModel>() ?? [])
            {
                Track(added);
            }
        };
    }

    private void Track(ItemEditorViewModel item)
    {
        item.PropertyChanged -= OnChildChanged;
        item.PropertyChanged += OnChildChanged;
    }

    private void OnChildChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is not (nameof(ItemEditorViewModel.Error) or nameof(GroupEditorViewModel.Swatch) or nameof(GroupEditorViewModel.IsColorValid) or nameof(ItemEditorViewModel.HasError)))
        {
            MarkDirty();
        }
    }

    private void MarkDirty()
    {
        if (!_loading)
        {
            IsDirty = true;
            Status = null;
        }
    }

    private void MoveGroup(GroupEditorViewModel? group, int delta)
    {
        if (group is null)
        {
            return;
        }

        var from = Groups.IndexOf(group);
        Reorder(Groups, from, from + delta);
        SelectedGroup = group;
    }

    private void MoveItem(ItemEditorViewModel? item, int delta)
    {
        if (item is null || SelectedGroup is null)
        {
            return;
        }

        var from = SelectedGroup.Items.IndexOf(item);
        Reorder(SelectedGroup.Items, from, from + delta);
        SelectedItem = item;
    }

    private void Reorder<T>(ObservableCollection<T> list, int from, int to)
    {
        if (from < 0 || from >= list.Count || to < 0 || to >= list.Count || from == to)
        {
            return;
        }

        list.Move(from, to);
        MarkDirty();
    }
}
