using LinkDeck.Core.Models;

namespace LinkDeck.Core.Search;

public static class Frecency
{
    public const double HalfLifeDays = 7.0;

    /// <summary><c>count × 0.5^(daysSinceLastUsed / 7)</c>.</summary>
    public static double Score(UsageEntry? entry, DateTimeOffset now)
    {
        if (entry is null || entry.Count <= 0)
        {
            return 0;
        }

        var days = Math.Max(0, (now - entry.LastUsed).TotalDays);
        return entry.Count * Math.Pow(0.5, days / HalfLifeDays);
    }
}

public static class Ranker
{
    /// <summary>
    /// Orders <paramref name="items"/> (given in manual order).
    /// Empty query: frecency (ties by manual order) or manual order, depending on <paramref name="sortMode"/>.
    /// Non-empty query: matching items only, by fuzzy score, then frecency, then manual order.
    /// </summary>
    public static IReadOnlyList<Item> Rank(
        IReadOnlyList<Item> items,
        string? query,
        IReadOnlyDictionary<string, UsageEntry> usage,
        SortMode sortMode,
        DateTimeOffset now)
    {
        var indexed = items.Select((item, index) => (item, index, frecency: Frecency.Score(usage.GetValueOrDefault(item.Id), now)));

        if (string.IsNullOrWhiteSpace(query))
        {
            return sortMode == SortMode.Manual
                ? [.. items]
                : [.. indexed.OrderByDescending(x => x.frecency).ThenBy(x => x.index).Select(x => x.item)];
        }

        var q = query.Trim();
        return
        [
            .. indexed
                .Select(x => (x.item, x.index, x.frecency, match: MatchScore(q, x.item)))
                .Where(x => x.match is not null)
                .OrderByDescending(x => x.match)
                .ThenByDescending(x => x.frecency)
                .ThenBy(x => x.index)
                .Select(x => x.item),
        ];
    }

    /// <summary>Best of name score and (down-weighted) target score.</summary>
    public static int? MatchScore(string query, Item item)
    {
        var name = FuzzyMatcher.Score(query, item.Name);
        var target = FuzzyMatcher.Score(query, item.Target);
        if (name is null && target is null)
        {
            return null;
        }

        return Math.Max(name ?? int.MinValue, target is null ? int.MinValue : target.Value / 2);
    }
}
