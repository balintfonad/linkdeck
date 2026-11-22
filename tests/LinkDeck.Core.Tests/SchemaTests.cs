using System.Text.Json;
using Json.Schema;
using LinkDeck.Core.Config;
using LinkDeck.Core.Models;

namespace LinkDeck.Core.Tests;

public class SchemaTests
{
    private static readonly JsonSchema Schema = JsonSchema.FromFile(Path.Combine(AppContext.BaseDirectory, "schema", "config.schema.json"));

    private static EvaluationResults Validate(string json) =>
        Schema.Evaluate(JsonDocument.Parse(json).RootElement, new EvaluationOptions { OutputFormat = OutputFormat.List });

    private static string Describe(EvaluationResults r) =>
        string.Join("\n", (r.Details ?? []).Where(d => d.Errors is { Count: > 0 }).SelectMany(d => d.Errors!.Select(e => $"{d.InstanceLocation}: {e.Value}")));

    [Fact]
    public void Example_config_is_valid()
    {
        var result = Validate(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "config.example.json")));

        Assert.True(result.IsValid, Describe(result));
    }

    [Fact]
    public void Default_config_as_saved_is_valid()
    {
        var result = Validate(ConfigStore.Serialize(AppConfig.CreateDefault()));

        Assert.True(result.IsValid, Describe(result));
    }

    [Fact]
    public void Serialized_config_with_every_item_type_is_valid()
    {
        var config = new AppConfig
        {
            Groups =
            [
                new Group
                {
                    Id = "g",
                    Name = "G",
                    Color = "#ABCDEF",
                    Items =
                    [
                        new UrlItem { Id = "1", Name = "U", Url = "https://u.example", Icon = "C:\\icon.png" },
                        new PathItem { Id = "2", Name = "P", Path = "C:\\x.exe", Args = "-a", WorkingDir = "C:\\" },
                        new RdpItem { Id = "3", Name = "R", Host = "fe80::1", Port = 3390, Username = "me" },
                    ],
                },
            ],
        };

        var result = Validate(ConfigStore.Serialize(config));

        Assert.True(result.IsValid, Describe(result));
    }

    [Theory]
    [InlineData("""{ "id": "1", "type": "url", "name": "X", "url": "javascript:alert(1)" }""")]
    [InlineData("""{ "id": "1", "type": "url", "name": "X" }""")]
    [InlineData("""{ "id": "1", "type": "rdp", "name": "X", "host": "a b" }""")]
    [InlineData("""{ "id": "1", "type": "rdp", "name": "X", "host": "h", "port": 0 }""")]
    [InlineData("""{ "id": "1", "type": "ssh", "name": "X", "host": "h" }""")]
    [InlineData("""{ "type": "url", "name": "X", "url": "https://x.example" }""")]
    public void Schema_rejects_invalid_items(string item)
    {
        var result = Validate($$"""{ "version": 1, "groups": [ { "id": "g", "name": "G", "items": [ {{item}} ] } ] }""");

        Assert.False(result.IsValid);
    }

    [Fact]
    public void Schema_rejects_bad_settings()
    {
        Assert.False(Validate("""{ "version": 1, "groups": [], "settings": { "theme": "blue" } }""").IsValid);
        Assert.False(Validate("""{ "version": 1, "groups": [], "settings": { "hotkey": { "hold": "Alt", "step": "Shift" } } }""").IsValid);
    }
}
