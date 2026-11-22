using LinkDeck.Core.Abstractions;
using LinkDeck.Core.Import;
using LinkDeck.Core.Models;

namespace LinkDeck.Core.Tests;

public class RdpFileParserTests
{
    [Fact]
    public void Parses_full_address_and_username()
    {
        var info = RdpFileParser.Parse("screen mode id:i:2\r\nfull address:s:server.corp:3390\r\nusername:s:CORP\\admin\r\n");

        Assert.Equal(new RdpFileInfo("server.corp", 3390, "CORP\\admin"), info);
    }

    [Fact]
    public void Default_port_is_omitted_and_server_port_is_used_when_address_has_none()
    {
        Assert.Equal(new RdpFileInfo("h", null, null), RdpFileParser.Parse("full address:s:h:3389"));
        Assert.Equal(new RdpFileInfo("h", 4000, null), RdpFileParser.Parse("full address:s:h\nserver port:i:4000"));
    }

    [Fact]
    public void Parses_ipv6_and_ignores_passwords()
    {
        var info = RdpFileParser.Parse("full address:s:[fe80::1]:3390\npassword 51:b:01000000D08C9DDF\n");

        Assert.Equal(new RdpFileInfo("fe80::1", 3390, null), info);
    }

    [Theory]
    [InlineData("")]
    [InlineData("username:s:x")]
    [InlineData("full address:s:bad host")]
    [InlineData("full address:s:host:0")]
    public void Returns_null_without_a_valid_address(string content) => Assert.Null(RdpFileParser.Parse(content));
}

public class DropParserTests : IDisposable
{
    private readonly TempDir _dir = new();

    public void Dispose() => _dir.Dispose();

    private sealed class FakeShortcuts : IShortcutResolver
    {
        public ShortcutInfo? Result { get; set; }

        public ShortcutInfo? Resolve(string shortcutPath) => Result;
    }

    [Fact]
    public void Browser_url_with_title_becomes_url_item_named_after_the_title()
    {
        var result = new DropParser(null).Parse(new DropData { Url = "https://jira.example.com/browse/X-1", UrlTitle = "X-1 Bug" });

        var draft = Assert.Single(result.Drafts);
        Assert.Equal((ItemType.Url, "X-1 Bug", "https://jira.example.com/browse/X-1"), (draft.Type, draft.Name, draft.Target));
    }

    [Fact]
    public void Browser_url_without_title_is_named_after_the_host()
    {
        var draft = Assert.Single(new DropParser(null).Parse(new DropData { Url = "https://www.example.com/a" }).Drafts);

        Assert.Equal("example.com", draft.Name);
    }

    [Fact]
    public void Folder_becomes_path_item_named_after_the_folder()
    {
        var folder = Directory.CreateDirectory(_dir.File("Projects")).FullName;

        var draft = Assert.Single(new DropParser(null).Parse(new DropData { Files = [folder] }).Drafts);

        Assert.Equal((ItemType.Path, "Projects", folder), (draft.Type, draft.Name, draft.Target));
    }

    [Theory]
    [InlineData("report.pdf", "report")]
    [InlineData("Code.exe", "Code")]
    public void File_or_exe_becomes_path_item_named_after_the_file(string fileName, string expectedName)
    {
        var file = _dir.File(fileName);
        File.WriteAllText(file, "x");

        var draft = Assert.Single(new DropParser(null).Parse(new DropData { Files = [file] }).Drafts);

        Assert.Equal((ItemType.Path, expectedName, file), (draft.Type, draft.Name, draft.Target));
    }

    [Fact]
    public void Lnk_becomes_path_item_with_resolved_target_args_and_working_dir()
    {
        var lnk = _dir.File("Editor.lnk");
        File.WriteAllText(lnk, "x");
        var shortcuts = new FakeShortcuts { Result = new ShortcutInfo(@"C:\Apps\editor.exe", "--new", @"C:\Work") };

        var draft = Assert.Single(new DropParser(shortcuts).Parse(new DropData { Files = [lnk] }).Drafts);

        Assert.Equal(ItemType.Path, draft.Type);
        Assert.Equal("Editor", draft.Name);
        Assert.Equal(@"C:\Apps\editor.exe", draft.Target);
        Assert.Equal("--new", draft.Args);
        Assert.Equal(@"C:\Work", draft.WorkingDir);
    }

    [Fact]
    public void Unresolvable_lnk_falls_back_to_the_lnk_path()
    {
        var lnk = _dir.File("Broken.lnk");
        File.WriteAllText(lnk, "x");

        var draft = Assert.Single(new DropParser(new FakeShortcuts()).Parse(new DropData { Files = [lnk] }).Drafts);

        Assert.Equal(lnk, draft.Target);
    }

    [Fact]
    public void Rdp_file_becomes_rdp_item_with_address_and_username()
    {
        var rdp = _dir.File("Prod server.rdp");
        File.WriteAllText(rdp, "full address:s:10.0.0.15:3390\r\nusername:s:admin\r\n");

        var draft = Assert.Single(new DropParser(null).Parse(new DropData { Files = [rdp] }).Drafts);

        Assert.Equal((ItemType.Rdp, "Prod server", "10.0.0.15:3390", "admin"), (draft.Type, draft.Name, draft.Target, draft.Username));
        var (item, error) = draft.ToItem();
        Assert.Null(error);
        Assert.Equal(3390, Assert.IsType<RdpItem>(item).Port);
    }

    [Fact]
    public void Invalid_rdp_file_is_ignored()
    {
        var rdp = _dir.File("bad.rdp");
        File.WriteAllText(rdp, "nothing useful");

        var result = new DropParser(null).Parse(new DropData { Files = [rdp] });

        Assert.True(result.IsEmpty);
        Assert.Single(result.Ignored);
    }

    [Fact]
    public void Plain_text_url_becomes_url_item()
    {
        var draft = Assert.Single(new DropParser(null).Parse(new DropData { Text = "  https://example.org/docs  " }).Drafts);

        Assert.Equal((ItemType.Url, "https://example.org/docs"), (draft.Type, draft.Target));
    }

    [Theory]
    [InlineData("just some words")]
    [InlineData("javascript:alert(1)")]
    [InlineData("file:///C:/secret")]
    public void Anything_else_is_ignored(string text)
    {
        var result = new DropParser(null).Parse(new DropData { Text = text });

        Assert.True(result.IsEmpty);
        Assert.Single(result.Ignored);
    }

    [Fact]
    public void Non_http_browser_url_is_ignored()
    {
        var result = new DropParser(null).Parse(new DropData { Url = "ftp://files.example.com" });

        Assert.True(result.IsEmpty);
    }

    [Fact]
    public void Several_files_produce_one_draft_each_and_missing_files_are_ignored()
    {
        var a = _dir.File("a.txt");
        var b = _dir.File("b.txt");
        File.WriteAllText(a, "");
        File.WriteAllText(b, "");

        var result = new DropParser(null).Parse(new DropData { Files = [a, b, _dir.File("missing.txt")] });

        Assert.Equal(2, result.Drafts.Count);
        Assert.Single(result.Ignored);
    }
}

public class ItemDraftTests
{
    [Theory]
    [InlineData("https://x.example", ItemType.Url)]
    [InlineData(@"C:\Windows", ItemType.Path)]
    [InlineData("rdp://server", ItemType.Rdp)]
    [InlineData("notes.txt", ItemType.Path)]
    public void Detects_type(string target, ItemType expected) => Assert.Equal(expected, ItemDraft.DetectType(target));

    [Fact]
    public void Rdp_target_with_scheme_and_port_is_parsed()
    {
        var (item, error) = new ItemDraft { Type = ItemType.Rdp, Name = "S", Target = "rdp://srv.corp:4000/" }.ToItem("id");

        Assert.Null(error);
        var rdp = Assert.IsType<RdpItem>(item);
        Assert.Equal(("srv.corp", 4000), (rdp.Host, rdp.Port));
    }

    [Fact]
    public void Invalid_draft_returns_error()
    {
        var (item, error) = new ItemDraft { Type = ItemType.Url, Name = "Bad", Target = "javascript:x" }.ToItem();

        Assert.Null(item);
        Assert.NotNull(error);
    }

    [Fact]
    public void FromItem_round_trips()
    {
        Item original = new RdpItem { Id = "1", Name = "S", Host = "::1", Port = null, Username = "me" };

        var (item, _) = ItemDraft.FromItem(original).ToItem("1");

        Assert.Equal(original, item);
    }
}
