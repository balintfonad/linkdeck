using System.Net;
using LinkDeck.Core.Config;
using LinkDeck.Core.Icons;
using LinkDeck.Core.Import;
using LinkDeck.Core.Models;

namespace LinkDeck.Core.Tests;

/// <summary>Security hardening: hidden characters, .url files, private addresses.</summary>
public class HardeningTests : IDisposable
{
    private readonly TempDir _dir = new();

    public void Dispose() => _dir.Dispose();

    [Theory]
    [InlineData("Invoice\u202Etxt.exe")] // right-to-left override
    [InlineData("A\u2066B")] // left-to-right isolate
    [InlineData("A\u200FB")] // right-to-left mark
    [InlineData("Line\nbreak")]
    public void Names_with_control_or_direction_characters_are_rejected(string name)
    {
        Assert.NotNull(ItemValidator.Validate(new UrlItem { Name = name, Url = "https://example.com" }));
    }

    [Fact]
    public void Names_may_contain_emoji_sequences()
    {
        // Zero-width joiner (a format character) is used in emoji and must stay allowed in names.
        Assert.Null(ItemValidator.Validate(new UrlItem { Name = "Family \U0001F468\u200D\U0001F469", Url = "https://example.com" }));
    }

    [Theory]
    [InlineData("C:\\Downloads\\gpj\u202E.exe", null, null)]
    [InlineData("C:\\Apps\\a.exe", "--x\u200B", null)]
    [InlineData("C:\\Apps\\a.exe", "--x\r\n--y", null)]
    [InlineData("C:\\Apps\\a.exe", null, "C:\\W\u202Eork")]
    public void Path_items_with_hidden_characters_are_rejected(string path, string? args, string? workingDir)
    {
        Assert.NotNull(ItemValidator.Validate(new PathItem { Name = "X", Path = path, Args = args, WorkingDir = workingDir }));
    }

    [Fact]
    public void Url_with_direction_override_is_rejected()
    {
        Assert.NotNull(ItemValidator.ValidateUrl("https://example.com/\u202Egnp.exe"));
    }

    [Fact]
    public void Rdp_username_with_direction_override_is_rejected()
    {
        Assert.NotNull(ItemValidator.Validate(new RdpItem { Name = "X", Host = "h", Username = "adm\u202Ein" }));
    }

    [Theory]
    [InlineData("search-ms:query=setup&crumb=location:\\\\evil@SSL\\s")]
    [InlineData("file://\\\\evil\\share\\setup.exe")]
    [InlineData("ms-msdt:/id PCWDiagnostic")]
    public void Url_file_with_non_web_address_is_not_imported(string url)
    {
        var file = _dir.File("VPN portal.url");
        File.WriteAllText(file, $"[InternetShortcut]\r\nURL={url}\r\n");

        Assert.Null(new DropParser(null).ParseFile(file));
    }

    [Fact]
    public void Url_file_with_web_address_becomes_a_url_item()
    {
        var file = _dir.File("Site.url");
        File.WriteAllText(file, "[InternetShortcut]\r\nURL=https://example.com/\r\n");

        var draft = new DropParser(null).ParseFile(file);

        Assert.NotNull(draft);
        Assert.Equal(ItemType.Url, draft.Type);
        Assert.Equal("https://example.com/", draft.Target);
    }

    [Theory]
    [InlineData(@"\\evil.example\share\i.png", true)]
    [InlineData(@"\\evil.example@SSL\i\a.png", true)]
    [InlineData("//evil.example/share/i.png", true)]
    [InlineData(@"\\?\UNC\evil\share\i.png", true)]
    [InlineData("file://evil.example/share/i.png", true)]
    [InlineData("\"\\\\evil\\s\\i.png\"", true)]
    [InlineData(@"C:\Icons\a.png", false)]
    [InlineData(@"\\?\C:\Icons\a.png", false)]
    [InlineData("file:///C:/Icons/a.png", false)]
    [InlineData("relative\\a.png", false)]
    [InlineData("", false)]
    public void Detects_network_paths(string path, bool expected) => Assert.Equal(expected, PathPolicy.IsRemote(path));

    [Theory]
    [InlineData("a.url", true)]
    [InlineData("A.WEBSITE", true)]
    [InlineData("x.library-ms", true)]
    [InlineData("\"C:\\x\\y.scf\"", true)]
    [InlineData("C:\\Apps\\a.exe", false)]
    [InlineData("C:\\Apps\\a.lnk", false)]
    public void Detects_link_files_that_bypass_the_url_allow_list(string path, bool expected) =>
        Assert.Equal(expected, PathPolicy.IsBlockedLinkFile(path));

    [Fact]
    public void Network_icon_paths_are_dropped_on_load_with_a_warning()
    {
        var path = _dir.File("config.json");
        File.WriteAllText(path, """
            {
              "version": 1,
              "groups": [ { "id": "g", "name": "G", "items": [
                { "id": "1", "type": "url", "name": "Mail", "url": "https://mail.example.com", "icon": "\\\\evil.example@SSL\\i\\a.png" },
                { "id": "2", "type": "url", "name": "Wiki", "url": "https://wiki.example.com", "icon": "C:\\Icons\\wiki.png" }
              ] } ]
            }
            """);
        using var store = new ConfigStore(path);

        var notices = store.Load();

        var items = store.Current.AllItems.ToList();
        Assert.Null(items.Single(i => i.Id == "1").Icon);
        Assert.Equal(@"C:\Icons\wiki.png", items.Single(i => i.Id == "2").Icon);
        Assert.Contains(notices, n => n.Level == NoticeLevel.Warning && n.Message.Contains("Mail", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("10.1.2.3", true)]
    [InlineData("192.168.0.1", true)]
    [InlineData("127.0.0.1", true)]
    [InlineData("169.254.1.1", true)]
    [InlineData("100.64.0.1", true)]
    [InlineData("224.0.0.251", true)]
    [InlineData("255.255.255.255", true)]
    [InlineData("::1", true)]
    [InlineData("fd00::1", true)]
    [InlineData("fe80::1", true)]
    [InlineData("ff02::1", true)]
    [InlineData("::ffff:192.168.1.1", true)]
    [InlineData("93.184.216.34", false)]
    [InlineData("2606:2800:220:1::1", false)]
    public void Classifies_private_addresses(string ip, bool expected) =>
        Assert.Equal(expected, FaviconFetcher.IsPrivateAddress(IPAddress.Parse(ip)));
}
