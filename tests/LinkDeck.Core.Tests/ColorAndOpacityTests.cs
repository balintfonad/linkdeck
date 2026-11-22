using LinkDeck.Core.Config;
using LinkDeck.Core.Import;
using LinkDeck.Core.Models;

namespace LinkDeck.Core.Tests;

public class ColorAndOpacityTests : IDisposable
{
    private readonly TempDir _dir = new();

    public void Dispose() => _dir.Dispose();

    private ConfigStore Load(string json, out IReadOnlyList<ConfigNotice> notices)
    {
        File.WriteAllText(_dir.File("config.json"), json);
        var store = new ConfigStore(_dir.File("config.json"));
        notices = store.Load();
        return store;
    }

    [Fact]
    public void Item_colour_round_trips()
    {
        using var store = Load("""{ "version": 1, "groups": [ { "id": "g", "name": "G", "items": [ { "id": "1", "type": "url", "name": "X", "url": "https://x.example", "color": "#ff8800" } ] } ] }""", out _);

        Assert.Equal("#ff8800", store.Current.AllItems.Single().Color);
        store.Save(store.Current);
        Assert.Contains("\"color\": \"#ff8800\"", File.ReadAllText(store.ConfigPath), StringComparison.Ordinal);
    }

    [Fact]
    public void Invalid_item_colour_is_dropped_but_the_item_is_kept()
    {
        using var store = Load("""{ "version": 1, "groups": [ { "id": "g", "name": "G", "items": [ { "id": "1", "type": "url", "name": "X", "url": "https://x.example", "color": "orange" } ] } ] }""", out var notices);

        Assert.Null(Assert.Single(store.Current.AllItems).Color);
        Assert.Contains(notices, n => n.Message.Contains("colour", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(0.6, 0.6)]
    [InlineData(0.05, 0.5)]
    [InlineData(3, 1)]
    public void Background_opacity_is_clamped(double stored, double expected)
    {
        using var store = Load($$"""{ "version": 1, "settings": { "backgroundOpacity": {{stored.ToString(System.Globalization.CultureInfo.InvariantCulture)}} }, "groups": [] }""", out _);

        Assert.Equal(expected, store.Current.Settings.BackgroundOpacity, 3);
    }

    [Fact]
    public void Missing_opacity_defaults_to_solid() =>
        Assert.Equal(1, Load("""{ "version": 1, "groups": [] }""", out _).Current.Settings.BackgroundOpacity);

    [Fact]
    public void Drafts_carry_and_normalise_the_colour()
    {
        var item = new UrlItem { Id = "1", Name = "X", Url = "https://x.example", Color = "#00AA00" };

        var (back, _) = ItemDraft.FromItem(item).ToItem("1");
        Assert.Equal("#00AA00", back!.Color);

        var (lower, _) = new ItemDraft { Type = ItemType.Url, Name = "X", Target = "https://x.example", Color = "#00aa00" }.ToItem();
        Assert.Equal("#00AA00", lower!.Color);

        var (bad, _) = new ItemDraft { Type = ItemType.Url, Name = "X", Target = "https://x.example", Color = "nope" }.ToItem();
        Assert.Null(bad!.Color);
    }
}

public class PartialSettingsTests : IDisposable
{
    private readonly TempDir _dir = new();

    public void Dispose() => _dir.Dispose();

    [Fact]
    public void Settings_missing_from_the_file_keep_their_defaults()
    {
        File.WriteAllText(_dir.File("config.json"), """{ "version": 1, "settings": { "theme": "dark" }, "groups": [] }""");
        using var store = new ConfigStore(_dir.File("config.json"));

        store.Load();

        var s = store.Current.Settings;
        Assert.Equal(ThemeMode.Dark, s.Theme);
        Assert.Equal(Hotkey.Default, s.Hotkey);
        Assert.True(s.StartWithWindows);
        Assert.Equal(1, s.BackgroundOpacity);
        Assert.Equal(SortMode.Frecency, s.SortMode);
    }

    [Fact]
    public void Hotkey_missing_a_field_keeps_its_default()
    {
        File.WriteAllText(_dir.File("config.json"), """{ "version": 1, "settings": { "hotkey": { "hold": "Win" } }, "groups": [] }""");
        using var store = new ConfigStore(_dir.File("config.json"));

        store.Load();

        Assert.Equal(new Hotkey { Hold = "Win", Step = "Ctrl" }, store.Current.Settings.Hotkey);
    }
}

public class PopupSizeSettingsTests : IDisposable
{
    private readonly TempDir _dir = new();

    public void Dispose() => _dir.Dispose();

    [Theory]
    [InlineData(8, 3, 8, 3)]
    [InlineData(1, -2, 2, 0)]
    [InlineData(99, 99, 20, 15)]
    [InlineData(6, 0, 6, 0)]
    public void Popup_grid_is_clamped_and_zero_rows_means_auto(int columns, int rows, int expectedColumns, int expectedRows)
    {
        File.WriteAllText(_dir.File("config.json"), $$"""{ "version": 1, "settings": { "popupColumns": {{columns}}, "popupRows": {{rows}}, "blur": "strong" }, "groups": [] }""");
        using var store = new ConfigStore(_dir.File("config.json"));

        store.Load();

        var s = store.Current.Settings;
        Assert.Equal((expectedColumns, expectedRows, BlurMode.Strong), (s.PopupColumns, s.PopupRows, s.Blur));
    }

    [Fact]
    public void Defaults_are_6_columns_auto_rows_no_blur()
    {
        var s = new AppSettings();

        Assert.Equal((6, 0, BlurMode.None), (s.PopupColumns, s.PopupRows, s.Blur));
    }
}
