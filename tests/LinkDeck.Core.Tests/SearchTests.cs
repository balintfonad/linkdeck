using LinkDeck.Core.Models;
using LinkDeck.Core.Search;

namespace LinkDeck.Core.Tests;

public class SearchTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 21, 12, 0, 0, TimeSpan.Zero);

    private static UrlItem Url(string id, string name, string url = "https://example.com") => new() { Id = id, Name = name, Url = url };

    [Fact]
    public void Fuzzy_matches_subsequences_case_insensitively()
    {
        Assert.NotNull(FuzzyMatcher.Score("jra", "Jira"));
        Assert.NotNull(FuzzyMatcher.Score("VSC", "Visual Studio Code"));
        Assert.Null(FuzzyMatcher.Score("xyz", "Jira"));
        Assert.Null(FuzzyMatcher.Score("arij", "Jira"));
    }

    [Fact]
    public void Fuzzy_prefers_exact_then_prefix_then_word_start_then_scattered()
    {
        var exact = FuzzyMatcher.Score("mail", "Mail")!.Value;
        var prefix = FuzzyMatcher.Score("mail", "Mailbox")!.Value;
        var wordStart = FuzzyMatcher.Score("mail", "Work Mail")!.Value;
        var scattered = FuzzyMatcher.Score("mail", "Mobile Aid Inline")!.Value;

        Assert.True(exact > prefix, $"{exact} > {prefix}");
        Assert.True(prefix > wordStart, $"{prefix} > {wordStart}");
        Assert.True(wordStart > scattered, $"{wordStart} > {scattered}");
    }

    [Fact]
    public void Frecency_halves_every_seven_days()
    {
        var entry = new UsageEntry { Count = 8, LastUsed = Now };

        Assert.Equal(8, Frecency.Score(entry, Now), 6);
        Assert.Equal(4, Frecency.Score(entry, Now.AddDays(7)), 6);
        Assert.Equal(2, Frecency.Score(entry, Now.AddDays(14)), 6);
        Assert.Equal(0, Frecency.Score(null, Now));
    }

    [Fact]
    public void Empty_query_frecency_orders_by_score_with_manual_order_as_tiebreak()
    {
        Item[] items = [Url("a", "A"), Url("b", "B"), Url("c", "C"), Url("d", "D")];
        var usage = new Dictionary<string, UsageEntry>
        {
            ["c"] = new() { Count = 10, LastUsed = Now },
            ["b"] = new() { Count = 10, LastUsed = Now.AddDays(-14) }, // 2.5
            ["d"] = new() { Count = 5, LastUsed = Now.AddDays(-7) },   // 2.5, after b in manual order
        };

        var ranked = Ranker.Rank(items, "", usage, SortMode.Frecency, Now);

        Assert.Equal(["c", "b", "d", "a"], ranked.Select(i => i.Id));
    }

    [Fact]
    public void Empty_query_manual_mode_keeps_configuration_order()
    {
        Item[] items = [Url("a", "A"), Url("b", "B"), Url("c", "C")];
        var usage = new Dictionary<string, UsageEntry> { ["c"] = new() { Count = 100, LastUsed = Now } };

        var ranked = Ranker.Rank(items, null, usage, SortMode.Manual, Now);

        Assert.Equal(["a", "b", "c"], ranked.Select(i => i.Id));
    }

    [Fact]
    public void Query_filters_and_orders_by_match_then_frecency()
    {
        Item[] items =
        [
            Url("1", "Confluence"),
            Url("2", "Jira"),
            Url("3", "Jira Cloud"),
            Url("4", "Gmail"),
        ];
        var usage = new Dictionary<string, UsageEntry> { ["3"] = new() { Count = 50, LastUsed = Now } };

        var ranked = Ranker.Rank(items, "jira", usage, SortMode.Frecency, Now);

        // Exact "Jira" beats the more frequently used prefix match; non-matches are removed.
        Assert.Equal(["2", "3"], ranked.Select(i => i.Id));
    }

    [Fact]
    public void Equal_match_scores_fall_back_to_frecency()
    {
        Item[] items = [Url("1", "Server A"), Url("2", "Server B")];
        var usage = new Dictionary<string, UsageEntry> { ["2"] = new() { Count = 3, LastUsed = Now } };

        var ranked = Ranker.Rank(items, "server", usage, SortMode.Manual, Now);

        Assert.Equal(["2", "1"], ranked.Select(i => i.Id));
    }

    [Fact]
    public void Query_also_matches_the_target()
    {
        Item[] items = [Url("1", "Tickets", "https://jira.example.com"), new RdpItem { Id = "2", Name = "Prod", Host = "db01.corp" }];

        Assert.Equal(["1"], Ranker.Rank(items, "jira", new Dictionary<string, UsageEntry>(), SortMode.Manual, Now).Select(i => i.Id));
        Assert.Equal(["2"], Ranker.Rank(items, "db01", new Dictionary<string, UsageEntry>(), SortMode.Manual, Now).Select(i => i.Id));
    }
}
