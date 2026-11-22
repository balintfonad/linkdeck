using System.Text.Json;
using LinkDeck.Core.Models;

namespace LinkDeck.Core.Config;

/// <summary>Reads and writes <c>state.json</c> (usage statistics, never shared). Thread-safe.</summary>
public sealed class UsageStore
{
    private readonly object _gate = new();
    private readonly TimeProvider _time;
    private UsageState _state = new();

    public UsageStore(string statePath, TimeProvider? time = null)
    {
        StatePath = Path.GetFullPath(statePath);
        _time = time ?? TimeProvider.System;
    }

    public string StatePath { get; }

    /// <summary>A snapshot of current usage, keyed by item id.</summary>
    public IReadOnlyDictionary<string, UsageEntry> Snapshot()
    {
        lock (_gate)
        {
            return new Dictionary<string, UsageEntry>(_state.Usage, StringComparer.Ordinal);
        }
    }

    /// <summary>Loads the file. A missing or unreadable file yields empty usage (statistics are not precious).</summary>
    public void Load()
    {
        lock (_gate)
        {
            _state = new UsageState();
            if (!File.Exists(StatePath))
            {
                return;
            }

            try
            {
                var loaded = JsonSerializer.Deserialize(File.ReadAllText(StatePath), LinkDeckJsonContext.Default.UsageState);
                if (loaded is not null)
                {
                    _state = new UsageState
                    {
                        Version = UsageState.CurrentVersion,
                        Usage = new Dictionary<string, UsageEntry>(loaded.Usage, StringComparer.Ordinal),
                    };
                }
            }
            catch (Exception ex) when (ex is JsonException or IOException or NotSupportedException)
            {
                _state = new UsageState();
            }
        }
    }

    /// <summary>Records one successful launch. Call only after the launch succeeded.</summary>
    public void RecordLaunch(string itemId)
    {
        lock (_gate)
        {
            var count = _state.Usage.TryGetValue(itemId, out var e) ? e.Count : 0;
            _state.Usage[itemId] = new UsageEntry { Count = count + 1, LastUsed = _time.GetUtcNow() };
        }
    }

    /// <summary>Saves atomically, dropping entries whose item no longer exists.</summary>
    public void Save(IEnumerable<string> existingItemIds)
    {
        var keep = existingItemIds.ToHashSet(StringComparer.Ordinal);
        string json;
        lock (_gate)
        {
            foreach (var id in _state.Usage.Keys.Where(k => !keep.Contains(k)).ToList())
            {
                _state.Usage.Remove(id);
            }

            json = JsonSerializer.Serialize(_state, LinkDeckJsonContext.Default.UsageState);
            AtomicFile.Write(StatePath, json + Environment.NewLine, StatePath + ".bak");
        }
    }
}
