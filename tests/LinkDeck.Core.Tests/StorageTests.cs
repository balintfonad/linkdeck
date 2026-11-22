using System.Text.Json.Nodes;
using LinkDeck.Core.Config;
using LinkDeck.Core.Logging;

namespace LinkDeck.Core.Tests;

public class UsageStoreTests : IDisposable
{
    private readonly TempDir _dir = new();
    private readonly FakeTime _time = new(new DateTimeOffset(2026, 11, 21, 15, 0, 0, TimeSpan.Zero));

    public void Dispose() => _dir.Dispose();

    [Fact]
    public void Records_launches_and_round_trips()
    {
        var path = _dir.File("state.json");
        var store = new UsageStore(path, _time);
        store.Load();

        store.RecordLaunch("a");
        store.RecordLaunch("a");
        store.Save(["a"]);

        var again = new UsageStore(path, _time);
        again.Load();
        var entry = again.Snapshot()["a"];
        Assert.Equal(2, entry.Count);
        Assert.Equal(_time.Now, entry.LastUsed);

        var json = JsonNode.Parse(File.ReadAllText(path))!;
        Assert.Equal(1, (int)json["version"]!);
        Assert.Equal(2, (int)json["usage"]!["a"]!["count"]!);
    }

    [Fact]
    public void Entries_for_deleted_items_are_removed_on_save()
    {
        var store = new UsageStore(_dir.File("state.json"), _time);
        store.RecordLaunch("kept");
        store.RecordLaunch("deleted");

        store.Save(["kept"]);

        Assert.Equal(["kept"], store.Snapshot().Keys);
    }

    [Fact]
    public void Corrupt_state_file_yields_empty_usage()
    {
        var path = _dir.File("state.json");
        File.WriteAllText(path, "{ not json");
        var store = new UsageStore(path, _time);

        store.Load();

        Assert.Empty(store.Snapshot());
    }
}

public class AppPathsTests : IDisposable
{
    private readonly TempDir _dir = new();

    public void Dispose() => _dir.Dispose();

    [Fact]
    public void Default_mode_uses_roaming_for_config_and_local_for_caches()
    {
        var exe = Directory.CreateDirectory(_dir.File("app")).FullName;

        var paths = AppPaths.Resolve(exe, _dir.File("roaming"), _dir.File("local"), _ => throw new InvalidOperationException("not needed without a portable config"));

        Assert.False(paths.IsPortable);
        Assert.False(paths.PortableConfigIgnored);
        Assert.Equal(Path.Combine(_dir.File("roaming"), "LinkDeck", "config.json"), paths.ConfigPath);
        Assert.Equal(Path.Combine(_dir.File("roaming"), "LinkDeck", "state.json"), paths.StatePath);
        Assert.Equal(Path.Combine(_dir.File("local"), "LinkDeck", "icons"), paths.IconsDirectory);
        Assert.Equal(Path.Combine(_dir.File("local"), "LinkDeck", "logs"), paths.LogsDirectory);
        Assert.Equal(Path.Combine(_dir.File("local"), "LinkDeck", "trust.dat"), paths.TrustPath);
    }

    [Fact]
    public void Config_next_to_the_executable_enables_portable_mode_in_a_private_folder()
    {
        var exe = Directory.CreateDirectory(_dir.File("app")).FullName;
        File.WriteAllText(Path.Combine(exe, "config.json"), "{}");

        var paths = AppPaths.Resolve(exe, _dir.File("roaming"), _dir.File("local"), dir => dir == exe);

        Assert.True(paths.IsPortable);
        Assert.Equal(Path.Combine(exe, "config.json"), paths.ConfigPath);
        Assert.Equal(Path.Combine(exe, "state.json"), paths.StatePath);
        Assert.Equal(Path.Combine(_dir.File("local"), "LinkDeck", "trust.dat"), paths.TrustPath); // never next to the exe
    }

    [Fact]
    public void Portable_config_in_a_folder_other_accounts_can_write_is_ignored()
    {
        var exe = Directory.CreateDirectory(_dir.File("app")).FullName;
        File.WriteAllText(Path.Combine(exe, "config.json"), "{}");

        var paths = AppPaths.Resolve(exe, _dir.File("roaming"), _dir.File("local"), _ => false);

        Assert.False(paths.IsPortable);
        Assert.True(paths.PortableConfigIgnored);
        Assert.Equal(Path.Combine(_dir.File("roaming"), "LinkDeck", "config.json"), paths.ConfigPath);
    }
}

public class FileLogTests : IDisposable
{
    private readonly TempDir _dir = new();

    public void Dispose() => _dir.Dispose();

    [Fact]
    public void Writes_daily_files_and_prunes_files_older_than_seven_days()
    {
        var time = new FakeTime(new DateTimeOffset(2026, 11, 21, 10, 0, 0, TimeSpan.Zero));
        File.WriteAllText(_dir.File("linkdeck-20261101.log"), "old");
        File.WriteAllText(_dir.File("linkdeck-20261118.log"), "recent");

        var log = new FileLog(_dir.Path, time);
        log.Info("hello");

        Assert.False(File.Exists(_dir.File("linkdeck-20261101.log")));
        Assert.True(File.Exists(_dir.File("linkdeck-20261118.log")));
        Assert.Contains("hello", File.ReadAllText(_dir.File("linkdeck-20261121.log")), StringComparison.Ordinal);
    }
}
