using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using LinkDeck.Core.Models;

namespace LinkDeck.Core.Config;

/// <summary>
/// Loads, validates, migrates, saves and watches <c>config.json</c>.
/// Thread-safe. Events may be raised on a background thread.
/// </summary>
public sealed class ConfigStore : IDisposable
{
    private static readonly JsonDocumentOptions DocumentOptions = new()
    {
        AllowTrailingCommas = false,
        CommentHandling = JsonCommentHandling.Disallow,
    };

    private readonly object _gate = new();
    private readonly int _currentVersion;
    private readonly IReadOnlyList<IConfigMigration> _migrations;
    private readonly TimeSpan _debounce;
    private FileSystemWatcher? _watcher;
    private Timer? _debounceTimer;
    private string? _knownHash;
    private bool _disposed;

    public ConfigStore(string configPath)
        : this(configPath, AppConfig.CurrentVersion, ConfigMigrations.All, TimeSpan.FromMilliseconds(300))
    {
    }

    public ConfigStore(string configPath, int currentVersion, IReadOnlyList<IConfigMigration> migrations, TimeSpan debounce)
    {
        ConfigPath = Path.GetFullPath(configPath);
        _currentVersion = currentVersion;
        _migrations = migrations;
        _debounce = debounce;
    }

    public string ConfigPath { get; }

    public string BackupPath => ConfigPath + ".bak";

    /// <summary>The last valid configuration.</summary>
    public AppConfig Current { get; private set; } = AppConfig.CreateDefault();

    /// <summary>
    /// True when saving is disabled: the file has a newer <c>version</c> than supported,
    /// or it currently contains invalid JSON (so a GUI save would destroy the user's edits).
    /// </summary>
    public bool IsReadOnly => IsNewerVersion || HasInvalidFile;

    public bool IsNewerVersion { get; private set; }

    /// <summary>The last <see cref="Load"/> found no file and created the default configuration.</summary>
    public bool CreatedDefault { get; private set; }

    public bool HasInvalidFile { get; private set; }

    /// <summary>Raised when <see cref="Current"/> changes because of an external edit.</summary>
    public event EventHandler<AppConfig>? Changed;

    public event EventHandler<ConfigNotice>? Notice;

    /// <summary>Loads the file, creating a default config if it does not exist. Returns the notices produced.</summary>
    public IReadOnlyList<ConfigNotice> Load()
    {
        lock (_gate)
        {
            var notices = new List<ConfigNotice>();
            CreatedDefault = !File.Exists(ConfigPath);
            if (CreatedDefault)
            {
                Current = AppConfig.CreateDefault();
                IsNewerVersion = false;
                HasInvalidFile = false;
                SaveCore(Current);
                notices.Add(new ConfigNotice(NoticeLevel.Info, $"Created a default configuration at {ConfigPath}."));
                Publish(notices);
                return notices;
            }

            var bytes = ReadAllBytesShared(ConfigPath);
            _knownHash = AtomicFile.Hash(bytes);
            LoadFromBytes(bytes, notices);
            Publish(notices);
            return notices;
        }
    }

    /// <summary>Saves atomically. Throws <see cref="InvalidOperationException"/> in read-only mode.</summary>
    public void Save(AppConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);
        lock (_gate)
        {
            if (IsReadOnly)
            {
                throw new InvalidOperationException(IsNewerVersion
                    ? "The configuration file was written by a newer LinkDeck version. Saving is disabled to prevent data loss."
                    : "The configuration file contains invalid JSON. Fix it by hand before saving from LinkDeck.");
            }

            var normalized = config with { Version = _currentVersion, Schema = config.Schema ?? AppConfig.SchemaUrl };
            SaveCore(normalized);
            Current = normalized;
        }
    }

    /// <summary>Starts watching the file for external changes (300 ms debounce, own writes ignored).</summary>
    public void StartWatching()
    {
        lock (_gate)
        {
            if (_watcher is not null)
            {
                return;
            }

            var dir = Path.GetDirectoryName(ConfigPath)!;
            Directory.CreateDirectory(dir);
            _debounceTimer = new Timer(_ => OnDebounceElapsed(), null, Timeout.Infinite, Timeout.Infinite);
            _watcher = new FileSystemWatcher(dir, Path.GetFileName(ConfigPath))
            {
                NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.Size | NotifyFilters.CreationTime,
                IncludeSubdirectories = false,
            };
            _watcher.Changed += OnFileEvent;
            _watcher.Created += OnFileEvent;
            _watcher.Renamed += OnFileEvent;
            _watcher.EnableRaisingEvents = true;
        }
    }

    /// <summary>Checks the file now (what the watcher does after the debounce). Returns true if a reload happened.</summary>
    public bool ReloadIfChanged()
    {
        AppConfig? changed = null;
        var notices = new List<ConfigNotice>();
        lock (_gate)
        {
            if (_disposed || !File.Exists(ConfigPath))
            {
                return false;
            }

            byte[] bytes;
            try
            {
                bytes = ReadAllBytesShared(ConfigPath);
            }
            catch (IOException)
            {
                // Still being written; the next event will retry.
                return false;
            }

            var hash = AtomicFile.Hash(bytes);
            if (hash == _knownHash)
            {
                return false;
            }

            _knownHash = hash;
            var before = Current;
            LoadFromBytes(bytes, notices);
            if (!ReferenceEquals(before, Current))
            {
                changed = Current;
            }
        }

        Publish(notices);
        if (changed is not null)
        {
            Changed?.Invoke(this, changed);
        }

        return changed is not null;
    }

    public void Dispose()
    {
        lock (_gate)
        {
            _disposed = true;
            _watcher?.Dispose();
            _watcher = null;
            _debounceTimer?.Dispose();
            _debounceTimer = null;
        }
    }

    private void OnFileEvent(object sender, FileSystemEventArgs e) =>
        _debounceTimer?.Change(_debounce, Timeout.InfiniteTimeSpan);

    private void OnDebounceElapsed()
    {
        try
        {
            ReloadIfChanged();
        }
        catch (Exception ex)
        {
            Notice?.Invoke(this, new ConfigNotice(NoticeLevel.Error, $"Could not reload configuration: {ex.Message}"));
        }
    }

    private void LoadFromBytes(byte[] bytes, List<ConfigNotice> notices)
    {
        JsonNode? rootNode;
        try
        {
            var text = Encoding.UTF8.GetString(bytes).TrimStart('﻿');
            rootNode = JsonNode.Parse(text, documentOptions: DocumentOptions);
        }
        catch (JsonException ex)
        {
            HasInvalidFile = true;
            var line = (ex.LineNumber ?? 0) + 1;
            var col = (ex.BytePositionInLine ?? 0) + 1;
            notices.Add(new ConfigNotice(NoticeLevel.Error,
                $"config.json is not valid JSON (line {line}, column {col}). The last valid configuration is kept and the file is not overwritten."));
            return;
        }

        if (rootNode is not JsonObject root)
        {
            HasInvalidFile = true;
            notices.Add(new ConfigNotice(NoticeLevel.Error, "config.json must contain a JSON object. The last valid configuration is kept."));
            return;
        }

        HasInvalidFile = false;
        var version = ReadVersion(root);
        IsNewerVersion = version > _currentVersion;
        var needsSave = false;

        if (IsNewerVersion)
        {
            notices.Add(new ConfigNotice(NoticeLevel.Warning,
                $"config.json has version {version}, newer than supported ({_currentVersion}). LinkDeck runs read-only until you update."));
        }
        else if (version < _currentVersion)
        {
            var backup = Path.Combine(Path.GetDirectoryName(ConfigPath)!, $"config.v{version}.bak");
            File.WriteAllBytes(backup, bytes);
            for (var v = version; v < _currentVersion; v++)
            {
                var step = _migrations.FirstOrDefault(m => m.FromVersion == v)
                    ?? throw new InvalidOperationException($"No migration from config version {v}.");
                step.Migrate(root);
            }

            root["version"] = _currentVersion;
            needsSave = true;
            notices.Add(new ConfigNotice(NoticeLevel.Info, $"Migrated config.json from version {version} to {_currentVersion}. Backup: {backup}"));
        }

        Current = Parse(root, notices);
        if (needsSave)
        {
            SaveCore(Current);
        }
    }

    private static int ReadVersion(JsonObject root)
    {
        if (root["version"] is JsonValue v && v.TryGetValue<int>(out var version))
        {
            return version;
        }

        return 1;
    }

    private AppConfig Parse(JsonObject root, List<ConfigNotice> notices)
    {
        var settings = new AppSettings();
        if (root["settings"] is JsonObject settingsNode)
        {
            try
            {
                settings = settingsNode.Deserialize(LinkDeckJsonContext.Default.AppSettings) ?? settings;
            }
            catch (JsonException ex)
            {
                notices.Add(new ConfigNotice(NoticeLevel.Warning, $"Invalid settings, defaults used: {ex.Message}"));
            }
        }

        if (double.IsNaN(settings.BackgroundOpacity) || settings.BackgroundOpacity is < AppSettings.MinBackgroundOpacity or > 1)
        {
            settings = settings with { BackgroundOpacity = Math.Clamp(double.IsNaN(settings.BackgroundOpacity) ? 1 : settings.BackgroundOpacity, AppSettings.MinBackgroundOpacity, 1) };
        }

        settings = settings with
        {
            PopupColumns = Math.Clamp(settings.PopupColumns, AppSettings.MinPopupColumns, AppSettings.MaxPopupColumns),
            PopupRows = Math.Clamp(settings.PopupRows, 0, AppSettings.MaxPopupRows),
        };

        if (!settings.Hotkey.IsValid())
        {
            notices.Add(new ConfigNotice(NoticeLevel.Warning, $"Invalid hotkey '{settings.Hotkey}', using {Hotkey.Default}."));
            settings = settings with { Hotkey = Hotkey.Default };
        }

        var groups = new List<Group>();
        var itemIds = new HashSet<string>(StringComparer.Ordinal);
        var groupIds = new HashSet<string>(StringComparer.Ordinal);
        if (root["groups"] is JsonArray groupArray)
        {
            foreach (var groupNode in groupArray)
            {
                if (groupNode is not JsonObject g)
                {
                    notices.Add(new ConfigNotice(NoticeLevel.Warning, "Skipped a group that is not a JSON object."));
                    continue;
                }

                var name = GetString(g, "name");
                var id = GetString(g, "id");
                if (string.IsNullOrWhiteSpace(id) || !groupIds.Add(id))
                {
                    id = Guid.NewGuid().ToString();
                    groupIds.Add(id);
                }

                var items = new List<Item>();
                if (g["items"] is JsonArray itemArray)
                {
                    var index = 0;
                    foreach (var itemNode in itemArray)
                    {
                        index++;
                        var item = ParseItem(itemNode, name, index, notices);
                        if (item is null)
                        {
                            continue;
                        }

                        if (string.IsNullOrWhiteSpace(item.Id) || !itemIds.Add(item.Id))
                        {
                            var newId = Item.NewId();
                            notices.Add(new ConfigNotice(NoticeLevel.Warning,
                                string.IsNullOrWhiteSpace(item.Id)
                                    ? $"Item '{item.Name}' had no id; assigned {newId}."
                                    : $"Duplicate item id '{item.Id}' on '{item.Name}'; assigned {newId}."));
                            item = item with { Id = newId };
                            itemIds.Add(newId);
                        }

                        items.Add(item);
                    }
                }

                groups.Add(new Group
                {
                    Id = id,
                    Name = string.IsNullOrWhiteSpace(name) ? "Unnamed" : name,
                    Color = GetString(g, "color"),
                    Items = items,
                });
            }
        }

        return new AppConfig
        {
            Schema = GetString(root, "$schema") ?? AppConfig.SchemaUrl,
            Version = ReadVersion(root),
            Settings = settings,
            Groups = groups,
            AllOrder = root["allOrder"] is JsonArray order
                ? [.. order.OfType<JsonValue>().Select(v => v.TryGetValue<string>(out var s) ? s : null).OfType<string>()]
                : null,
        };
    }

    private static Item? ParseItem(JsonNode? node, string? groupName, int index, List<ConfigNotice> notices)
    {
        var where = $"item #{index} in group '{groupName}'";
        if (node is not JsonObject obj)
        {
            notices.Add(new ConfigNotice(NoticeLevel.Warning, $"Skipped {where}: not a JSON object."));
            return null;
        }

        var type = GetString(obj, "type");
        if (type is not ("url" or "path" or "rdp"))
        {
            notices.Add(new ConfigNotice(NoticeLevel.Warning, $"Skipped {where}: unknown type '{type}'."));
            return null;
        }

        Item? item;
        try
        {
            item = obj.Deserialize(LinkDeckJsonContext.Default.Item);
        }
        catch (Exception ex) when (ex is JsonException or NotSupportedException or InvalidOperationException)
        {
            notices.Add(new ConfigNotice(NoticeLevel.Warning, $"Skipped {where}: {ex.Message}"));
            return null;
        }

        if (item is null)
        {
            return null;
        }

        var error = ItemValidator.Validate(item);
        if (error is not null)
        {
            var label = string.IsNullOrWhiteSpace(item.Name) ? where : $"'{item.Name}' ({where})";
            notices.Add(new ConfigNotice(NoticeLevel.Warning, $"Skipped {label}: {error}"));
            return null;
        }

        if (item.Color is not null && !HexColor.IsValid(item.Color))
        {
            notices.Add(new ConfigNotice(NoticeLevel.Warning, $"'{item.Name}': colour '{item.Color}' is not #RRGGBB and was ignored."));
            item = item with { Color = null };
        }

        if (item.Icon is not null && PathPolicy.IsRemote(Environment.ExpandEnvironmentVariables(item.Icon)))
        {
            // Reading the icon would connect to that host and send the user's Windows credentials.
            notices.Add(new ConfigNotice(NoticeLevel.Warning, $"'{item.Name}': icon '{item.Icon}' is on a network share and was ignored. Use a local icon file."));
            item = item with { Icon = null };
        }

        return item;
    }

    private static string? GetString(JsonObject obj, string property) =>
        obj[property] is JsonValue v && v.TryGetValue<string>(out var s) ? s : null;

    private void SaveCore(AppConfig config)
    {
        var json = Serialize(config);
        AtomicFile.Write(ConfigPath, json, BackupPath);
        _knownHash = AtomicFile.Hash(json);
    }

    public static string Serialize(AppConfig config) =>
        JsonSerializer.Serialize(config, LinkDeckJsonContext.Default.AppConfig) + Environment.NewLine;

    private void Publish(List<ConfigNotice> notices)
    {
        foreach (var n in notices)
        {
            Notice?.Invoke(this, n);
        }
    }

    private static byte[] ReadAllBytesShared(string path)
    {
        using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var ms = new MemoryStream();
        fs.CopyTo(ms);
        return ms.ToArray();
    }
}
