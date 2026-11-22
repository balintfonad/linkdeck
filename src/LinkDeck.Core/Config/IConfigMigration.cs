using System.Text.Json.Nodes;

namespace LinkDeck.Core.Config;

/// <summary>One step of the migration chain: transforms a config document from <see cref="FromVersion"/> to <c>FromVersion + 1</c>.</summary>
public interface IConfigMigration
{
    int FromVersion { get; }

    void Migrate(JsonObject root);
}

public static class ConfigMigrations
{
    /// <summary>The built-in chain. Empty while the format is still at version 1.</summary>
    public static IReadOnlyList<IConfigMigration> All { get; } = [];
}
