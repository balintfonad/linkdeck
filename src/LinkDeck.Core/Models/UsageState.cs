namespace LinkDeck.Core.Models;

public sealed class UsageState
{
    public const int CurrentVersion = 1;

    public int Version { get; set; } = CurrentVersion;

    public Dictionary<string, UsageEntry> Usage { get; set; } = new(StringComparer.Ordinal);
}

public sealed record UsageEntry
{
    public DateTimeOffset LastUsed { get; init; }

    public int Count { get; init; }
}
