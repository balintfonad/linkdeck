using System.Text.Json.Nodes;
using LinkDeck.Core.Config;
using LinkDeck.Core.Models;

namespace LinkDeck.Core.Tests;

public class ConfigStoreTests : IDisposable
{
    private readonly TempDir _dir = new();

    private string ConfigPath => _dir.File("config.json");

    public void Dispose() => _dir.Dispose();

    private ConfigStore NewStore(int currentVersion = AppConfig.CurrentVersion, params IConfigMigration[] migrations) =>
        new(ConfigPath, currentVersion, migrations, TimeSpan.FromMilliseconds(50));

    private const string ValidJson = """
        {
          "version": 1,
          "settings": { "hotkey": { "hold": "Alt", "step": "Ctrl" }, "theme": "dark", "startWithWindows": false, "sortMode": "manual" },
          "groups": [
            {
              "id": "work", "name": "Work", "color": "#3B82F6",
              "items": [
                { "id": "a", "type": "url", "name": "Jira", "url": "https://jira.example.com" },
                { "id": "b", "type": "path", "name": "Projects", "path": "%USERPROFILE%\\Projects" },
                { "id": "c", "type": "path", "name": "Code", "path": "C:\\Code.exe", "args": "--new-window", "workingDir": "C:\\" },
                { "id": "d", "type": "rdp", "name": "Prod", "host": "10.0.0.15", "port": 3390, "username": "admin" }
              ]
            }
          ]
        }
        """;

    [Fact]
    public void Missing_file_creates_default_config_with_editable_type_groups()
    {
        using var store = NewStore();

        var notices = store.Load();

        Assert.True(File.Exists(ConfigPath));
        Assert.Equal(["web", "apps", "files", "rdp"], store.Current.Groups.Select(g => g.Id));
        Assert.NotEmpty(store.Current.AllItems);
        Assert.Contains(notices, n => n.Level == NoticeLevel.Info);
        Assert.False(store.IsReadOnly);
    }

    [Fact]
    public void Loads_all_item_types_and_settings()
    {
        File.WriteAllText(ConfigPath, ValidJson);
        using var store = NewStore();

        var notices = store.Load();

        Assert.Empty(notices);
        var s = store.Current.Settings;
        Assert.Equal(ThemeMode.Dark, s.Theme);
        Assert.Equal(SortMode.Manual, s.SortMode);
        Assert.False(s.StartWithWindows);
        Assert.Equal(Hotkey.Default, s.Hotkey);

        var items = store.Current.AllItems.ToList();
        Assert.Equal(4, items.Count);
        Assert.Equal("https://jira.example.com", Assert.IsType<UrlItem>(items[0]).Url);
        Assert.Equal("--new-window", Assert.IsType<PathItem>(items[2]).Args);
        var rdp = Assert.IsType<RdpItem>(items[3]);
        Assert.Equal(("10.0.0.15", 3390, "admin"), (rdp.Host, rdp.Port, rdp.Username));
    }

    [Fact]
    public void Discriminator_does_not_have_to_be_the_first_property()
    {
        File.WriteAllText(ConfigPath, """{ "version": 1, "groups": [ { "id": "g", "name": "G", "items": [ { "name": "X", "url": "https://x.example", "id": "1", "type": "url" } ] } ] }""");
        using var store = NewStore();

        store.Load();

        Assert.IsType<UrlItem>(Assert.Single(store.Current.AllItems));
    }

    [Fact]
    public void Save_then_load_round_trips()
    {
        File.WriteAllText(ConfigPath, ValidJson);
        using var store = NewStore();
        store.Load();
        var original = store.Current;

        store.Save(original);
        using var again = NewStore();
        again.Load();

        Assert.Equal(
            original.AllItems.Select(i => (i.Id, i.Type, i.Name, i.Target)),
            again.Current.AllItems.Select(i => (i.Id, i.Type, i.Name, i.Target)));
        Assert.Equal(original.Settings, again.Current.Settings);
    }

    [Fact]
    public void Save_is_atomic_and_keeps_a_backup()
    {
        File.WriteAllText(ConfigPath, ValidJson);
        using var store = NewStore();
        store.Load();

        store.Save(store.Current with { Groups = [] });

        Assert.True(File.Exists(ConfigPath + ".bak"));
        Assert.Contains("Jira", File.ReadAllText(ConfigPath + ".bak"), StringComparison.Ordinal);
        Assert.False(File.Exists(ConfigPath + ".tmp"));
        Assert.DoesNotContain("Jira", File.ReadAllText(ConfigPath), StringComparison.Ordinal);
    }

    [Fact]
    public void Saved_file_uses_type_discriminator_and_omits_nulls()
    {
        using var store = NewStore();
        store.Load();
        store.Save(store.Current with
        {
            Groups = [new Group { Id = "g", Name = "G", Items = [new UrlItem { Id = "1", Name = "X", Url = "https://x.example" }] }],
        });

        var root = JsonNode.Parse(File.ReadAllText(ConfigPath))!;
        var item = root["groups"]![0]!["items"]![0]!.AsObject();
        Assert.Equal("url", (string?)item["type"]);
        Assert.False(item.ContainsKey("icon"));
        Assert.Equal(AppConfig.SchemaUrl, (string?)root["$schema"]);
        Assert.Equal("system", (string?)root["settings"]!["theme"]);
    }

    [Fact]
    public void Invalid_json_keeps_last_valid_config_and_does_not_overwrite_the_file()
    {
        File.WriteAllText(ConfigPath, ValidJson);
        using var store = NewStore();
        store.Load();
        var good = store.Current;

        const string broken = "{\n  \"version\": 1,\n  \"groups\": [ oops ]\n}";
        File.WriteAllText(ConfigPath, broken);
        var reloaded = store.ReloadIfChanged();

        Assert.False(reloaded);
        Assert.Same(good, store.Current);
        Assert.True(store.IsReadOnly);
        Assert.Equal(broken, File.ReadAllText(ConfigPath));
        Assert.Throws<InvalidOperationException>(() => store.Save(good));
    }

    [Fact]
    public void Invalid_json_notice_reports_line_and_column()
    {
        File.WriteAllText(ConfigPath, "{\n  \"version\": 1,\n  \"groups\": [ oops ]\n}");
        using var store = NewStore();

        var notices = store.Load();

        var error = Assert.Single(notices, n => n.Level == NoticeLevel.Error);
        Assert.Contains("line 3", error.Message, StringComparison.Ordinal);
        Assert.Contains("column", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Comments_are_rejected_as_invalid_json()
    {
        File.WriteAllText(ConfigPath, "{ // comment\n \"version\": 1 }");
        using var store = NewStore();

        store.Load();

        Assert.True(store.HasInvalidFile);
    }

    [Fact]
    public void Fixing_the_file_clears_read_only_mode()
    {
        File.WriteAllText(ConfigPath, "{ nope");
        using var store = NewStore();
        store.Load();
        Assert.True(store.IsReadOnly);

        File.WriteAllText(ConfigPath, ValidJson);
        Assert.True(store.ReloadIfChanged());

        Assert.False(store.IsReadOnly);
        Assert.Equal(4, store.Current.AllItems.Count());
    }

    [Theory]
    [InlineData("""{ "id": "x", "type": "url", "name": "Empty", "url": "" }""")]
    [InlineData("""{ "id": "x", "type": "url", "name": "File", "url": "file:///c:/windows" }""")]
    [InlineData("""{ "id": "x", "type": "url", "name": "Js", "url": "javascript:alert(1)" }""")]
    [InlineData("""{ "id": "x", "type": "url", "name": "", "url": "https://ok.example" }""")]
    [InlineData("""{ "id": "x", "type": "path", "name": "NoPath", "path": "" }""")]
    [InlineData("""{ "id": "x", "type": "rdp", "name": "BadHost", "host": "a b" }""")]
    [InlineData("""{ "id": "x", "type": "rdp", "name": "Inject", "host": "host /admin" }""")]
    [InlineData("""{ "id": "x", "type": "rdp", "name": "BadPort", "host": "h", "port": 70000 }""")]
    [InlineData("""{ "id": "x", "type": "ssh", "name": "Unknown", "host": "h" }""")]
    [InlineData("""{ "id": "x", "name": "NoType" }""")]
    [InlineData("""{ "id": "x", "type": "url", "name": "WrongShape", "url": 42 }""")]
    [InlineData("\"not an object\"")]
    public void Invalid_items_are_skipped_with_a_notice(string itemJson)
    {
        File.WriteAllText(ConfigPath, $$"""
            { "version": 1, "groups": [ { "id": "g", "name": "G", "items": [
              { "id": "ok", "type": "url", "name": "Good", "url": "https://good.example" },
              {{itemJson}}
            ] } ] }
            """);
        using var store = NewStore();

        var notices = store.Load();

        Assert.Equal("ok", Assert.Single(store.Current.AllItems).Id);
        Assert.Contains(notices, n => n.Level == NoticeLevel.Warning && n.Message.Contains("Skipped", StringComparison.Ordinal));
    }

    [Fact]
    public void Duplicate_ids_get_a_new_id_and_a_notice()
    {
        File.WriteAllText(ConfigPath, """
            { "version": 1, "groups": [
              { "id": "a", "name": "A", "items": [ { "id": "same", "type": "url", "name": "One", "url": "https://one.example" } ] },
              { "id": "b", "name": "B", "items": [ { "id": "same", "type": "url", "name": "Two", "url": "https://two.example" } ] }
            ] }
            """);
        using var store = NewStore();

        var notices = store.Load();

        var ids = store.Current.AllItems.Select(i => i.Id).ToList();
        Assert.Equal(2, ids.Distinct().Count());
        Assert.Equal("same", ids[0]);
        Assert.True(Guid.TryParse(ids[1], out _));
        Assert.Contains(notices, n => n.Message.Contains("Duplicate", StringComparison.Ordinal));
    }

    [Fact]
    public void Unknown_fields_are_ignored_on_load_and_dropped_on_save()
    {
        File.WriteAllText(ConfigPath, """
            { "version": 1, "futureThing": true, "groups": [ { "id": "g", "name": "G", "extra": 1,
              "items": [ { "id": "1", "type": "url", "name": "X", "url": "https://x.example", "favourite": true } ] } ] }
            """);
        using var store = NewStore();
        store.Load();

        store.Save(store.Current);

        var text = File.ReadAllText(ConfigPath);
        Assert.DoesNotContain("futureThing", text, StringComparison.Ordinal);
        Assert.DoesNotContain("favourite", text, StringComparison.Ordinal);
        Assert.DoesNotContain("extra", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Newer_version_is_read_only()
    {
        File.WriteAllText(ConfigPath, """{ "version": 99, "groups": [ { "id": "g", "name": "G", "items": [ { "id": "1", "type": "url", "name": "X", "url": "https://x.example" } ] } ] }""");
        using var store = NewStore();

        var notices = store.Load();

        Assert.True(store.IsReadOnly);
        Assert.True(store.IsNewerVersion);
        Assert.Single(store.Current.AllItems);
        Assert.Contains(notices, n => n.Level == NoticeLevel.Warning);
        var before = File.ReadAllText(ConfigPath);
        Assert.Throws<InvalidOperationException>(() => store.Save(store.Current));
        Assert.Equal(before, File.ReadAllText(ConfigPath));
    }

    private sealed class RenameNameToTitle : IConfigMigration
    {
        public int FromVersion => 1;

        public void Migrate(JsonObject root)
        {
            foreach (var g in root["groups"]!.AsArray())
            {
                g!["name"] = "migrated-" + (string?)g["name"];
            }
        }
    }

    [Fact]
    public void Older_version_is_backed_up_then_migrated_and_saved()
    {
        const string v1 = """{ "version": 1, "groups": [ { "id": "g", "name": "G", "items": [] } ] }""";
        File.WriteAllText(ConfigPath, v1);
        using var store = NewStore(currentVersion: 2, new RenameNameToTitle());

        var notices = store.Load();

        Assert.Equal(v1, File.ReadAllText(_dir.File("config.v1.bak")));
        Assert.Equal("migrated-G", store.Current.Groups[0].Name);
        Assert.Equal(2, (int)JsonNode.Parse(File.ReadAllText(ConfigPath))!["version"]!);
        Assert.Contains(notices, n => n.Message.Contains("Migrated", StringComparison.Ordinal));
        Assert.False(store.IsReadOnly);
    }

    [Fact]
    public void Own_writes_are_ignored_by_reload()
    {
        File.WriteAllText(ConfigPath, ValidJson);
        using var store = NewStore();
        store.Load();
        var changed = 0;
        store.Changed += (_, _) => changed++;

        store.Save(store.Current with { Groups = [] });

        Assert.False(store.ReloadIfChanged());
        Assert.Equal(0, changed);
    }

    [Fact]
    public void External_change_is_reloaded_and_raises_changed()
    {
        File.WriteAllText(ConfigPath, ValidJson);
        using var store = NewStore();
        store.Load();
        AppConfig? received = null;
        store.Changed += (_, c) => received = c;

        File.WriteAllText(ConfigPath, """{ "version": 1, "groups": [] }""");

        Assert.True(store.ReloadIfChanged());
        Assert.NotNull(received);
        Assert.Empty(received.Groups);
    }

    [Fact]
    public async Task Watcher_reloads_after_debounce()
    {
        File.WriteAllText(ConfigPath, ValidJson);
        using var store = NewStore();
        store.Load();
        var tcs = new TaskCompletionSource<AppConfig>(TaskCreationOptions.RunContinuationsAsynchronously);
        store.Changed += (_, c) => tcs.TrySetResult(c);
        store.StartWatching();

        File.WriteAllText(ConfigPath, """{ "version": 1, "groups": [] }""");

        var completed = await Task.WhenAny(tcs.Task, Task.Delay(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken));
        Assert.Same(tcs.Task, completed);
        Assert.Empty((await tcs.Task).Groups);
    }

    [Fact]
    public void Invalid_hotkey_falls_back_to_default()
    {
        File.WriteAllText(ConfigPath, """{ "version": 1, "settings": { "hotkey": { "hold": "Alt", "step": "Alt" } }, "groups": [] }""");
        using var store = NewStore();

        var notices = store.Load();

        Assert.Equal(Hotkey.Default, store.Current.Settings.Hotkey);
        Assert.Contains(notices, n => n.Message.Contains("hotkey", StringComparison.OrdinalIgnoreCase));
    }
}
