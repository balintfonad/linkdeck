using System.Text.Json.Serialization;

namespace LinkDeck.Core.Models;

/// <summary>The kinds of item LinkDeck can launch. Values match the JSON <c>type</c> discriminator.</summary>
public enum ItemType
{
    Url,
    Path,
    Rdp,
}

/// <summary>
/// A launchable entry. Serialized polymorphically using the <c>type</c> discriminator
/// (<c>url</c>, <c>path</c>, <c>rdp</c>).
/// </summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "type", UnknownDerivedTypeHandling = JsonUnknownDerivedTypeHandling.FailSerialization)]
[JsonDerivedType(typeof(UrlItem), "url")]
[JsonDerivedType(typeof(PathItem), "path")]
[JsonDerivedType(typeof(RdpItem), "rdp")]
public abstract record Item
{
    public string Id { get; init; } = "";

    public string Name { get; init; } = "";

    /// <summary>Optional icon file path. When absent the icon is resolved automatically.</summary>
    public string? Icon { get; init; }

    /// <summary>Optional <c>#RRGGBB</c> accent colour for the tile.</summary>
    public string? Color { get; init; }

    [JsonIgnore]
    public abstract ItemType Type { get; }

    /// <summary>The human-readable target (URL, path or host:port) shown in the footer and copied by Shift+Enter.</summary>
    [JsonIgnore]
    public abstract string Target { get; }

    /// <summary>Everything that decides what runs, for display (footer, tooltip): the target plus any arguments.</summary>
    [JsonIgnore]
    public virtual string DisplayTarget => Target;

    public static string NewId() => Guid.NewGuid().ToString();
}

public sealed record UrlItem : Item
{
    public string Url { get; init; } = "";

    [JsonIgnore]
    public override ItemType Type => ItemType.Url;

    [JsonIgnore]
    public override string Target => Url;
}

public sealed record PathItem : Item
{
    /// <summary>Folder, file or executable. Environment variables are expanded at launch time.</summary>
    public string Path { get; init; } = "";

    public string? Args { get; init; }

    public string? WorkingDir { get; init; }

    [JsonIgnore]
    public override ItemType Type => ItemType.Path;

    [JsonIgnore]
    public override string Target => Path;

    [JsonIgnore]
    public override string DisplayTarget => string.IsNullOrWhiteSpace(Args) ? Path : $"{Path} {Args}";
}

public sealed record RdpItem : Item
{
    public const int DefaultPort = 3389;

    public string Host { get; init; } = "";

    public int? Port { get; init; }

    public string? Username { get; init; }

    [JsonIgnore]
    public override ItemType Type => ItemType.Rdp;

    [JsonIgnore]
    public int EffectivePort => Port ?? DefaultPort;

    [JsonIgnore]
    public override string Target => RdpAddress.Format(Host, EffectivePort);
}
