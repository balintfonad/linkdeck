using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using LinkDeck.Core.Import;
using LinkDeck.Core.Models;

namespace LinkDeck.Desktop.ViewModels;

public sealed partial class GroupEditorViewModel : ObservableObject
{
    public GroupEditorViewModel(Group group)
    {
        Id = group.Id;
        Name = group.Name;
        Color = group.Color ?? "";
        foreach (var item in group.Items)
        {
            Items.Add(new ItemEditorViewModel(item));
        }
    }

    public string Id { get; }

    [ObservableProperty]
    public partial string Name { get; set; }

    /// <summary>Empty or <c>#RRGGBB</c>.</summary>
    [ObservableProperty]
    public partial string Color { get; set; }

    public ObservableCollection<ItemEditorViewModel> Items { get; } = [];

    public bool IsColorValid => Color.Length == 0 || IsHexColor(Color);

    public Avalonia.Media.IBrush? Swatch =>
        IsHexColor(Color) && Avalonia.Media.Color.TryParse(Color, out var c) ? new Avalonia.Media.Immutable.ImmutableSolidColorBrush(c) : null;

    partial void OnColorChanged(string value)
    {
        OnPropertyChanged(nameof(Swatch));
        OnPropertyChanged(nameof(IsColorValid));
    }

    public static bool IsHexColor(string s) =>
        s.Length == 7 && s[0] == '#' && s.Skip(1).All(char.IsAsciiHexDigit);
}

public sealed partial class ItemEditorViewModel : ObservableObject
{
    public ItemEditorViewModel(Item item)
    {
        Id = item.Id;
        var draft = ItemDraft.FromItem(item);
        TypeIndex = (int)draft.Type;
        Name = draft.Name;
        Target = draft.Target;
        Args = draft.Args ?? "";
        WorkingDir = draft.WorkingDir ?? "";
        Username = draft.Username ?? "";
        Icon = item.Icon ?? "";
        ColorHex = item.Color ?? "";
    }

    public ItemEditorViewModel(ItemDraft draft)
        : this(new UrlItem { Id = Item.NewId() })
    {
        TypeIndex = (int)draft.Type;
        Name = draft.Name;
        Target = draft.Target;
        Args = draft.Args ?? "";
        WorkingDir = draft.WorkingDir ?? "";
        Username = draft.Username ?? "";
        ColorHex = draft.Color ?? "";
    }

    public string Id { get; }

    public IReadOnlyList<string> Types => QuickAddViewModel.TypeNames;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsPath), nameof(IsRdp), nameof(TargetLabel), nameof(TypeLabel))]
    public partial int TypeIndex { get; set; }

    [ObservableProperty]
    public partial string Name { get; set; }

    [ObservableProperty]
    public partial string Target { get; set; }

    [ObservableProperty]
    public partial string Args { get; set; }

    [ObservableProperty]
    public partial string WorkingDir { get; set; }

    [ObservableProperty]
    public partial string Username { get; set; }

    [ObservableProperty]
    public partial string Icon { get; set; }

    /// <summary>The tile colour as <c>#RRGGBB</c>, or empty for none.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasColor), nameof(ColorSwatch))]
    public partial string ColorHex { get; set; } = "";

    public bool HasColor => HexColor.IsValid(ColorHex);

    public Avalonia.Media.IBrush? ColorSwatch =>
        HasColor && Avalonia.Media.Color.TryParse(ColorHex, out var c) ? new Avalonia.Media.Immutable.ImmutableSolidColorBrush(c) : null;

    [CommunityToolkit.Mvvm.Input.RelayCommand]
    private void ClearColor() => ColorHex = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasError))]
    public partial string? Error { get; set; }

    private int _lastValidTypeIndex;

    /// <summary>A combo box that loses its items reports -1; never let that erase the item's type.</summary>
    partial void OnTypeIndexChanged(int value)
    {
        if (value is < 0 or > 2)
        {
            TypeIndex = _lastValidTypeIndex;
            return;
        }

        _lastValidTypeIndex = value;
    }

    public ItemType Type => (ItemType)TypeIndex;

    public bool IsPath => Type == ItemType.Path;

    public bool IsRdp => Type == ItemType.Rdp;

    public bool HasError => !string.IsNullOrEmpty(Error);

    public string TypeLabel => QuickAddViewModel.TypeNames[Math.Clamp(TypeIndex, 0, 2)];

    public string TargetLabel => Type switch
    {
        ItemType.Url => "URL",
        ItemType.Rdp => "Host (host or host:port)",
        _ => "Path",
    };

    /// <summary>A copy of this editor's current values with a new id.</summary>
    public ItemEditorViewModel Duplicate() => new(new ItemDraft
    {
        Type = Type,
        Name = Name,
        Target = Target,
        Args = Args,
        WorkingDir = WorkingDir,
        Username = Username,
    })
    {
        Icon = Icon,
        ColorHex = ColorHex,
    };

    /// <summary>Builds and validates the item; sets <see cref="Error"/> when invalid.</summary>
    public Item? ToItem()
    {
        var draft = new ItemDraft
        {
            Type = Type,
            Name = Name,
            Target = Target,
            Args = IsPath ? Args : null,
            WorkingDir = IsPath ? WorkingDir : null,
            Username = IsRdp ? Username : null,
            Color = ColorHex,
        };
        var (item, error) = draft.ToItem(Id);
        Error = error;
        if (item is null)
        {
            return null;
        }

        return string.IsNullOrWhiteSpace(Icon) ? item : item with { Icon = Icon.Trim() };
    }
}
