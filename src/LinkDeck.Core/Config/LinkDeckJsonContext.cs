using System.Text.Json;
using System.Text.Json.Serialization;
using LinkDeck.Core.Models;

namespace LinkDeck.Core.Config;

/// <summary>Source-generated serializer context. All JSON goes through here so NativeAOT needs no reflection.</summary>
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    WriteIndented = true,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    AllowOutOfOrderMetadataProperties = true,
    ReadCommentHandling = JsonCommentHandling.Disallow,
    AllowTrailingCommas = false,
    PropertyNameCaseInsensitive = false,
    UnmappedMemberHandling = JsonUnmappedMemberHandling.Skip)]
[JsonSerializable(typeof(AppConfig))]
[JsonSerializable(typeof(AppSettings))]
[JsonSerializable(typeof(Item))]
[JsonSerializable(typeof(UsageState))]
internal sealed partial class LinkDeckJsonContext : JsonSerializerContext
{
}
