using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LinkDeck.Core.Models;

namespace LinkDeck.Desktop.ViewModels;

public sealed partial class ItemTileViewModel : ObservableObject
{
    private readonly PopupViewModel? _owner;

    public ItemTileViewModel(Item item, string? groupColor, PopupViewModel? owner = null)
    {
        Item = item;
        _owner = owner;
        GroupColor = groupColor is not null && Color.TryParse(groupColor, out var c) ? new Avalonia.Media.Immutable.ImmutableSolidColorBrush(c) : null;
        if (item.Color is not null && Color.TryParse(item.Color, out var own))
        {
            ItemColor = new Avalonia.Media.Immutable.ImmutableSolidColorBrush(own);
            Tint = new Avalonia.Media.Immutable.ImmutableSolidColorBrush(own, 0.16);
        }
    }

    public Item Item { get; }

    public string Name => Item.Name;

    /// <summary>Target plus arguments, so a tile never hides what it runs.</summary>
    public string Target => Item.DisplayTarget;

    public IBrush? GroupColor { get; }

    /// <summary>The item's own colour (bar under the name), if set.</summary>
    public IBrush? ItemColor { get; }

    /// <summary>A faint wash of the item's colour behind the tile.</summary>
    public IBrush? Tint { get; }

    public bool HasItemColor => ItemColor is not null;

    public bool IsUrl => Item.Type == ItemType.Url;

    public bool IsPath => Item.Type == ItemType.Path;

    public bool IsRdp => Item.Type == ItemType.Rdp;

    public bool HasIcon => Icon is not null;

    /// <summary>"Ctrl+N" hint for the first nine tiles.</summary>
    [ObservableProperty]
    public partial string? ShortcutHint { get; set; }

    [ObservableProperty]
    public partial bool IsSelected { get; set; }

    /// <summary>This tile is being dragged (shown faded with a dashed outline).</summary>
    [ObservableProperty]
    public partial bool IsDragging { get; set; }

    /// <summary>The dragged tile would land before this one (insertion bar on the left).</summary>
    [ObservableProperty]
    public partial bool DropBefore { get; set; }

    /// <summary>The dragged tile would land after this one (insertion bar on the right).</summary>
    [ObservableProperty]
    public partial bool DropAfter { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasIcon))]
    public partial IImage? Icon { get; set; }

    public string AutomationName => $"{Name}, {TypeLabel}, {Target}";

    public string TypeLabel => Item.Type switch
    {
        ItemType.Url => "web page",
        ItemType.Rdp => "remote desktop",
        _ => "folder, file or program",
    };

    // Context-menu commands (the menu lives outside the popup's visual tree, so tiles carry them).
    [RelayCommand]
    private void Open() => _owner?.Launch(Item);

    [RelayCommand]
    private void Copy() => _owner?.Copy(Item);

    [RelayCommand]
    private void Edit() => _owner?.Edit(Item);

    [RelayCommand]
    private void PickColor() => _owner?.PickColor(Item);

    [RelayCommand]
    private void Remove() => _owner?.Remove(Item);
}

/// <summary>A popup tab: "All", an item kind (Web, Apps, …) or a user group.</summary>
public sealed class TabViewModel
{
    private readonly Func<Item, AppConfig, bool> _filter;

    private TabViewModel(string key, string name, Func<Item, AppConfig, bool> filter, string? groupId = null)
    {
        Key = key;
        Name = name;
        GroupId = groupId;
        _filter = filter;
    }

    public static TabViewModel All { get; } = new("all", "All", (_, _) => true);

    /// <summary>Stable identity used to keep the selected tab across config reloads.</summary>
    public string Key { get; }

    public string Name { get; }

    /// <summary>The user group behind this tab; null for "All" and the automatic kind tabs.</summary>
    public string? GroupId { get; }

    public bool IsGroup => GroupId is not null;

    public static TabViewModel ForGroup(Group group) =>
        new($"group:{group.Id}", group.Name, (item, config) => config.Groups.Any(g => g.Id == group.Id && g.Items.Any(i => i.Id == item.Id)), group.Id);

    public bool Matches(Item item, AppConfig config) => _filter(item, config);
}
