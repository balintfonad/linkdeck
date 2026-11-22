using LinkDeck.Core.Abstractions;
using LinkDeck.Core.Config;
using LinkDeck.Core.Logging;
using LinkDeck.Core.Models;

namespace LinkDeck.Desktop.Services;

/// <summary>Shows short, non-blocking messages to the user.</summary>
public interface INotifier
{
    void Show(NoticeLevel level, string message);
}

/// <summary>
/// The application model: current configuration, usage statistics and launching.
/// <see cref="ConfigChanged"/> is raised on the UI thread (via the <c>post</c> delegate).
/// </summary>
public sealed class DeckService
{
    private readonly ConfigStore _store;
    private readonly UsageStore _usage;
    private readonly ILauncher _launcher;
    private readonly INotifier _notifier;
    private readonly ILog _log;
    private readonly Action<Action> _post;
    private readonly TrustStore? _trust;
    private readonly HashSet<string> _announced = new(StringComparer.Ordinal);

    /// <param name="trust">
    /// Items added or edited outside LinkDeck must be confirmed before they open. Null disables the check (tests).
    /// </param>
    public DeckService(ConfigStore store, UsageStore usage, ILauncher launcher, INotifier notifier, ILog log, Action<Action> post, TimeProvider? time = null, TrustStore? trust = null)
    {
        _store = store;
        _usage = usage;
        _launcher = launcher;
        _notifier = notifier;
        _log = log;
        _post = post;
        _trust = trust;
        Time = time ?? TimeProvider.System;

        _store.Notice += (_, n) => _post(() => Report(n));
        _store.Changed += (_, _) => _post(() =>
        {
            _log.Info("Configuration reloaded after an external change.");
            AnnounceUntrusted("changed outside LinkDeck");
            ConfigChanged?.Invoke(this, EventArgs.Empty);
        });
    }

    public event EventHandler? ConfigChanged;

    public AppConfig Config => _store.Current;

    public bool IsReadOnly => _store.IsReadOnly;

    public string ConfigPath => _store.ConfigPath;

    public TimeProvider Time { get; }

    public IReadOnlyDictionary<string, UsageEntry> Usage => _usage.Snapshot();

    public void Load()
    {
        _store.Load();
        _usage.Load();
        if (_trust is null)
        {
            return;
        }

        _trust.Load();
        if (_store.CreatedDefault)
        {
            // LinkDeck wrote these items itself.
            UpdateTrust(Config.AllItems);
            return;
        }

        if (_trust.WasUnreadable)
        {
            Report(new ConfigNotice(NoticeLevel.Warning,
                "LinkDeck's list of confirmed items could not be verified (it was changed, or copied from another user or computer). Every item asks for confirmation once before it opens."));
            foreach (var item in Config.AllItems)
            {
                _announced.Add(TrustStore.Fingerprint(item));
            }

            return;
        }

        AnnounceUntrusted(_trust.Existed ? "changed outside LinkDeck" : "not yet confirmed on this computer");
    }

    /// <summary>True when the item was added or changed outside LinkDeck and has not been confirmed since.</summary>
    public bool NeedsConfirmation(Item item) => _trust is not null && !_trust.IsTrusted(item);

    /// <summary>Saves the configuration. Returns false (and notifies) when saving is not possible.</summary>
    public bool TrySave(AppConfig config)
    {
        var before = Config.AllItems.Select(TrustStore.Fingerprint).ToHashSet(StringComparer.Ordinal);
        try
        {
            _store.Save(config);
        }
        catch (InvalidOperationException ex)
        {
            Report(new ConfigNotice(NoticeLevel.Error, ex.Message));
            return false;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _log.Error("Saving configuration failed.", ex);
            Report(new ConfigNotice(NoticeLevel.Error, $"Could not save configuration: {ex.Message}"));
            return false;
        }

        // Only items the user just added or edited here become trusted; untouched items keep their state, so a save
        // from Settings cannot launder an item that was slipped into config.json from outside.
        UpdateTrust(config.AllItems.Where(i => !before.Contains(TrustStore.Fingerprint(i))));
        ConfigChanged?.Invoke(this, EventArgs.Empty);
        return true;
    }

    private void UpdateTrust(IEnumerable<Item> trust)
    {
        if (_trust is null)
        {
            return;
        }

        try
        {
            _trust.Update(trust, Config.AllItems);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.Cryptography.CryptographicException or System.ComponentModel.Win32Exception)
        {
            _log.Warn("Saving the list of confirmed items failed.", ex);
        }
    }

    /// <summary>Shows one notice listing untrusted items that were not announced before.</summary>
    private void AnnounceUntrusted(string reason)
    {
        if (_trust is null)
        {
            return;
        }

        var fresh = Config.AllItems.Where(i => !_trust.IsTrusted(i) && _announced.Add(TrustStore.Fingerprint(i))).ToList();
        if (fresh.Count == 0)
        {
            return;
        }

        var names = string.Join(", ", fresh.Take(5).Select(i => $"'{i.Name}'")) + (fresh.Count > 5 ? $" and {fresh.Count - 5} more" : "");
        Report(new ConfigNotice(NoticeLevel.Warning,
            $"{names} {(fresh.Count == 1 ? "was" : "were")} {reason}. LinkDeck will show what {(fresh.Count == 1 ? "it runs" : "each runs")} and ask before opening."));
    }

    /// <summary>Appends an item to a group, creating a default group when there is none.</summary>
    public bool AddItem(string? groupId, Item item)
    {
        var config = Config;
        var groups = config.Groups.ToList();
        var index = groups.FindIndex(g => g.Id == groupId);
        if (index < 0)
        {
            if (groups.Count == 0)
            {
                groups.Add(new Group { Id = "general", Name = "General" });
            }

            index = 0;
        }

        groups[index] = groups[index] with { Items = [.. groups[index].Items, item] };
        return TrySave(config.WithGroups(groups));
    }

    /// <summary>Creates a group and returns it, or null when saving failed.</summary>
    public Group? AddGroup(string name)
    {
        name = name.Trim();
        if (name.Length == 0)
        {
            return null;
        }

        var group = new Group { Id = NewGroupId(name), Name = name };
        return TrySave(Config.WithGroups([.. Config.Groups, group])) ? group : null;
    }

    public bool RenameGroup(string groupId, string name)
    {
        name = name.Trim();
        var group = Config.Groups.FirstOrDefault(g => g.Id == groupId);
        return name.Length > 0 && group is not null && TrySave(Config.ReplaceGroup(group with { Name = name }));
    }

    /// <summary>The group that receives a deleted group's items: the first other group, or null if there is none.</summary>
    public Group? FallbackGroupFor(string groupId) => Config.Groups.FirstOrDefault(g => g.Id != groupId);

    /// <summary>
    /// Deletes a group. Its items move to <see cref="FallbackGroupFor"/>; when it is the only group, the items are deleted with it.
    /// </summary>
    public bool DeleteGroup(string groupId)
    {
        var group = Config.Groups.FirstOrDefault(g => g.Id == groupId);
        if (group is null)
        {
            return false;
        }

        var fallback = FallbackGroupFor(groupId);
        var groups = Config.Groups
            .Where(g => g.Id != groupId)
            .Select(g => g.Id == fallback?.Id ? g with { Items = [.. g.Items, .. group.Items] } : g);
        return TrySave(Config.WithGroups(groups));
    }

    /// <summary>Moves an item to the end of another group.</summary>
    public bool MoveItem(string itemId, string targetGroupId)
    {
        var item = Config.AllItems.FirstOrDefault(i => i.Id == itemId);
        if (item is null || Config.Groups.All(g => g.Id != targetGroupId))
        {
            return false;
        }

        var groups = Config.Groups.Select(g =>
        {
            var items = g.Items.Where(i => i.Id != itemId);
            return g with { Items = g.Id == targetGroupId ? [.. items, item] : [.. items] };
        });
        return TrySave(Config.WithGroups(groups));
    }

    /// <summary>
    /// Arranges an item before (or with <paramref name="after"/> after) another one. Group membership never changes:
    /// with <paramref name="groupId"/> null (the "All" tab) only the All order is updated; with a group id both items
    /// must be in that group and only its order changes. Arranging by hand switches the sort mode to manual.
    /// </summary>
    public bool MoveItemNextTo(string itemId, string targetItemId, bool after, string? groupId = null)
    {
        var config = Config;
        if (itemId == targetItemId)
        {
            return false;
        }

        AppConfig updated;
        if (groupId is null)
        {
            var order = config.ItemsInAllOrder().Select(i => i.Id).ToList();
            if (!order.Contains(itemId) || !order.Contains(targetItemId))
            {
                return false;
            }

            order.Remove(itemId);
            order.Insert(order.IndexOf(targetItemId) + (after ? 1 : 0), itemId);
            updated = config with { AllOrder = order };
        }
        else
        {
            var group = config.Groups.FirstOrDefault(g => g.Id == groupId);
            var items = group?.Items.ToList();
            var item = items?.FirstOrDefault(i => i.Id == itemId);
            if (group is null || items is null || item is null || items.All(i => i.Id != targetItemId))
            {
                return false;
            }

            items.Remove(item);
            items.Insert(items.FindIndex(i => i.Id == targetItemId) + (after ? 1 : 0), item);
            updated = config.ReplaceGroup(group with { Items = items });
        }

        var settings = config.Settings.SortMode == SortMode.Manual ? config.Settings : config.Settings with { SortMode = SortMode.Manual };
        return TrySave(updated with { Settings = settings });
    }

    /// <summary>Sets (or with null clears) an item's tile colour.</summary>
    public bool SetItemColor(string itemId, string? color)
    {
        var normalized = HexColor.Normalize(color);
        var groups = Config.Groups.Select(g => g with { Items = [.. g.Items.Select(i => i.Id == itemId ? i with { Color = normalized } : i)] });
        return Config.AllItems.Any(i => i.Id == itemId) && TrySave(Config.WithGroups(groups));
    }

    /// <summary>Copies an item to the end of a group (any group, including its own). The copy gets a new id.</summary>
    public Item? CopyItem(string itemId, string targetGroupId)
    {
        var item = Config.AllItems.FirstOrDefault(i => i.Id == itemId);
        if (item is null || Config.Groups.All(g => g.Id != targetGroupId))
        {
            return null;
        }

        var copy = item with { Id = Item.NewId() };
        var groups = Config.Groups.Select(g => g.Id == targetGroupId ? g with { Items = [.. g.Items, copy] } : g);
        return TrySave(Config.WithGroups(groups)) ? copy : null;
    }

    private string NewGroupId(string name)
    {
        var slug = new string([.. name.ToLowerInvariant().Select(c => char.IsAsciiLetterOrDigit(c) ? c : '-')]).Trim('-');
        if (slug.Length == 0)
        {
            slug = "group";
        }

        var id = slug;
        for (var n = 2; Config.Groups.Any(g => g.Id == id); n++)
        {
            id = $"{slug}-{n}";
        }

        return id;
    }

    /// <summary>Removes an item from whichever group holds it.</summary>
    public bool RemoveItem(string itemId)
    {
        var config = Config;
        if (!config.AllItems.Any(i => i.Id == itemId))
        {
            return false;
        }

        return TrySave(config.WithGroups(config.Groups.Select(g => g with { Items = [.. g.Items.Where(i => i.Id != itemId)] })));
    }

    /// <summary>Launches an item; on success records usage. Failures are shown as notifications.</summary>
    /// <param name="confirmed">
    /// The user has just confirmed an item that <see cref="NeedsConfirmation"/>; it becomes trusted. Without it such an item is refused.
    /// </param>
    public LaunchResult Launch(Item item, bool confirmed = false)
    {
        if (NeedsConfirmation(item))
        {
            if (!confirmed)
            {
                var refused = LaunchResult.Fail($"'{item.Name}' was changed outside LinkDeck and has to be confirmed before it opens.");
                _log.Warn($"Launch refused for unconfirmed item {item.Id}.");
                Report(new ConfigNotice(NoticeLevel.Error, refused.Error!));
                return refused;
            }

            _log.Info($"Item {item.Id} confirmed by the user.");
            UpdateTrust([item]);
        }

        var result = _launcher.Launch(item);
        if (!result.Success)
        {
            _log.Warn($"Launch failed for item {item.Id}: {result.Error}");
            Report(new ConfigNotice(NoticeLevel.Error, result.Error ?? $"Could not open '{item.Name}'."));
            return result;
        }

        _usage.RecordLaunch(item.Id);
        try
        {
            _usage.Save(Config.AllItems.Select(i => i.Id));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _log.Warn("Saving usage statistics failed.", ex);
        }

        return result;
    }

    public void Report(ConfigNotice notice)
    {
        var level = notice.Level switch
        {
            NoticeLevel.Error => LogLevel.Error,
            NoticeLevel.Warning => LogLevel.Warning,
            _ => LogLevel.Info,
        };
        _log.Write(level, notice.Message);
        _notifier.Show(notice.Level, notice.Message);
    }
}
