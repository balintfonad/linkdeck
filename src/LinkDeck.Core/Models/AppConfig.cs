using System.Text.Json.Serialization;

namespace LinkDeck.Core.Models;

public sealed record AppConfig
{
    public const int CurrentVersion = 1;

    public const string SchemaUrl = "https://raw.githubusercontent.com/balintfonad/linkdeck/master/schema/config.schema.json";

    [JsonPropertyName("$schema")]
    public string? Schema { get; init; } = SchemaUrl;

    public int Version { get; init; } = CurrentVersion;

    public AppSettings Settings { get; init; } = new();

    public IReadOnlyList<Group> Groups { get; init; } = [];

    /// <summary>All items across groups, in manual (configuration) order.</summary>
    public IEnumerable<Item> AllItems => Groups.SelectMany(g => g.Items);

    /// <summary>
    /// Hand-arranged order of the "All" tab (item ids). Arranging on "All" changes only this, never which group an item
    /// belongs to. Items not listed follow in configuration order. Null = configuration order.
    /// </summary>
    public IReadOnlyList<string>? AllOrder { get; init; }

    /// <summary>All items in the "All" tab's manual order (see <see cref="AllOrder"/>).</summary>
    public IReadOnlyList<Item> ItemsInAllOrder()
    {
        var items = AllItems.ToList();
        if (AllOrder is not { Count: > 0 } order)
        {
            return items;
        }

        var rank = new Dictionary<string, int>(StringComparer.Ordinal);
        for (var i = 0; i < order.Count; i++)
        {
            rank.TryAdd(order[i], i);
        }

        return [.. items.Select((item, index) => (item, index))
            .OrderBy(x => rank.TryGetValue(x.item.Id, out var r) ? r : order.Count + x.index)
            .Select(x => x.item)];
    }

    /// <summary>A fresh configuration: one ordinary, editable group per kind of item, with a few samples.</summary>
    public static AppConfig CreateDefault() => new()
    {
        Groups =
        [
            new Group
            {
                Id = "web",
                Name = "Web",
                Color = "#3B82F6",
                Items = [new UrlItem { Id = Item.NewId(), Name = "LinkDeck on GitHub", Url = "https://github.com/balintfonad/linkdeck" }],
            },
            new Group
            {
                Id = "apps",
                Name = "Apps",
                Color = "#8B5CF6",
                Items = [new PathItem { Id = Item.NewId(), Name = "Notepad", Path = @"%WINDIR%\System32\notepad.exe" }],
            },
            new Group
            {
                Id = "files",
                Name = "Files & folders",
                Color = "#F59E0B",
                Items = [new PathItem { Id = Item.NewId(), Name = "Documents", Path = @"%USERPROFILE%\Documents" }],
            },
            new Group { Id = "rdp", Name = "Remote desktop", Color = "#10B981" },
        ],
    };

    public AppConfig WithGroups(IEnumerable<Group> groups) => this with { Groups = [.. groups] };

    public AppConfig ReplaceGroup(Group group) =>
        WithGroups(Groups.Select(g => g.Id == group.Id ? group : g));
}

public sealed record Group
{
    public string Id { get; init; } = "";

    public string Name { get; init; } = "";

    /// <summary>Optional <c>#RRGGBB</c> colour.</summary>
    public string? Color { get; init; }

    public IReadOnlyList<Item> Items { get; init; } = [];
}

/// <remarks>
/// Settable (not init) on purpose: the JSON source generator only keeps the initializer defaults below for
/// properties missing from the file when they have a setter.
/// </remarks>
public sealed record AppSettings
{
    public Hotkey Hotkey { get; set; } = Hotkey.Default;

    public ThemeMode Theme { get; set; } = ThemeMode.System;

    public bool StartWithWindows { get; set; } = true;

    public SortMode SortMode { get; set; } = SortMode.Frecency;

    /// <summary>Keep the launcher open after launching or clicking elsewhere; only Esc closes it.</summary>
    public bool KeepOpen { get; set; }

    /// <summary>Releasing the hold key after stepping launches the selected item; when false it only selects (Enter launches).</summary>
    public bool LaunchOnRelease { get; set; } = true;

    public const double MinBackgroundOpacity = 0.5;

    public const int DefaultPopupColumns = 6;
    public const int MinPopupColumns = 2;
    public const int MaxPopupColumns = 20;
    public const int MaxPopupRows = 15;

    /// <summary>Opacity of the launcher background, 0.5 (see-through) to 1 (solid). Tiles and text stay opaque.</summary>
    public double BackgroundOpacity { get; set; } = 1.0;

    /// <summary>How strongly what is behind a see-through launcher is blurred.</summary>
    public BlurMode Blur { get; set; } = BlurMode.None;

    /// <summary>Launcher width in tile columns (like a terminal's columns).</summary>
    public int PopupColumns { get; set; } = DefaultPopupColumns;

    /// <summary>Launcher height in tile rows; 0 = fit the content (up to 4 rows, then scroll).</summary>
    public int PopupRows { get; set; }
}

[JsonConverter(typeof(JsonStringEnumConverter<ThemeMode>))]
public enum ThemeMode
{
    [JsonStringEnumMemberName("system")]
    System,

    [JsonStringEnumMemberName("light")]
    Light,

    [JsonStringEnumMemberName("dark")]
    Dark,
}

[JsonConverter(typeof(JsonStringEnumConverter<BlurMode>))]
public enum BlurMode
{
    [JsonStringEnumMemberName("none")]
    None,

    /// <summary>Light blur (Windows blur-behind).</summary>
    [JsonStringEnumMemberName("light")]
    Light,

    /// <summary>Strong blur (Windows acrylic).</summary>
    [JsonStringEnumMemberName("strong")]
    Strong,
}

[JsonConverter(typeof(JsonStringEnumConverter<SortMode>))]
public enum SortMode
{
    [JsonStringEnumMemberName("frecency")]
    Frecency,

    [JsonStringEnumMemberName("manual")]
    Manual,
}
