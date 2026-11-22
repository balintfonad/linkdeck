using System.Text;
using LinkDeck.Core.Config;
using LinkDeck.Core.Models;

namespace LinkDeck.Core.Tests;

public class TrustStoreTests : IDisposable
{
    private readonly TempDir _dir = new();

    public void Dispose() => _dir.Dispose();

    private TrustStore NewStore(string user = "me") => new(_dir.File("trust.dat"), new FakeProtector(user));

    private static readonly PathItem Outlook = new() { Id = "o", Name = "Outlook", Path = @"C:\Office\OUTLOOK.EXE" };

    [Fact]
    public void Nothing_is_trusted_before_anything_was_confirmed()
    {
        var store = NewStore();
        store.Load();

        Assert.False(store.Existed);
        Assert.False(store.IsTrusted(Outlook));
    }

    [Fact]
    public void Trusted_items_survive_a_restart()
    {
        var store = NewStore();
        store.Load();
        store.Update(trust: [Outlook], keep: [Outlook]);

        var reloaded = NewStore();
        reloaded.Load();

        Assert.True(reloaded.Existed);
        Assert.False(reloaded.WasUnreadable);
        Assert.True(reloaded.IsTrusted(Outlook));
    }

    [Theory]
    [InlineData("path")]
    [InlineData("args")]
    [InlineData("workingDir")]
    public void Changing_what_an_item_runs_makes_it_untrusted(string field)
    {
        var store = NewStore();
        store.Load();
        store.Update([Outlook], [Outlook]);

        PathItem changed = field switch
        {
            "path" => Outlook with { Path = @"C:\Users\me\AppData\Roaming\x\outlook.exe" },
            "args" => Outlook with { Args = "/c evil" },
            _ => Outlook with { WorkingDir = @"C:\Temp" },
        };

        Assert.False(store.IsTrusted(changed));
    }

    [Fact]
    public void Renaming_or_recolouring_keeps_trust()
    {
        var store = NewStore();
        store.Load();
        store.Update([Outlook], [Outlook]);

        Assert.True(store.IsTrusted(Outlook with { Name = "Mail", Color = "#112233", Id = "other" }));
    }

    [Fact]
    public void Url_and_rdp_targets_are_part_of_the_fingerprint()
    {
        var url = new UrlItem { Name = "Bank", Url = "https://bank.example" };
        var rdp = new RdpItem { Name = "Prod", Host = "prod.corp", Username = "admin" };
        var store = NewStore();
        store.Load();
        store.Update([url, rdp], [url, rdp]);

        Assert.False(store.IsTrusted(url with { Url = "https://bank.example.evil" }));
        Assert.False(store.IsTrusted(rdp with { Host = "prod.corp.evil" }));
        Assert.False(store.IsTrusted(rdp with { Port = 3390 }));
        Assert.False(store.IsTrusted(rdp with { Username = "other" }));
        Assert.True(store.IsTrusted(rdp with { Port = 3389 })); // same effective port
    }

    [Fact]
    public void Edited_or_foreign_trust_file_trusts_nothing()
    {
        var store = NewStore();
        store.Load();
        store.Update([Outlook], [Outlook]);

        var foreign = NewStore(user: "someone-else");
        foreign.Load();
        Assert.True(foreign.WasUnreadable);
        Assert.False(foreign.IsTrusted(Outlook));

        var bytes = File.ReadAllBytes(_dir.File("trust.dat"));
        bytes[^1] ^= 0xFF;
        File.WriteAllBytes(_dir.File("trust.dat"), bytes);
        var tampered = NewStore();
        tampered.Load();
        Assert.True(tampered.WasUnreadable);
        Assert.False(tampered.IsTrusted(Outlook));
    }

    [Fact]
    public void Update_forgets_items_that_are_no_longer_kept()
    {
        var other = new UrlItem { Name = "Wiki", Url = "https://wiki.example" };
        var store = NewStore();
        store.Load();
        store.Update([Outlook, other], [Outlook, other]);

        store.Update(trust: [], keep: [other]);

        Assert.False(store.IsTrusted(Outlook));
        Assert.True(store.IsTrusted(other));
    }

    [Fact]
    public void Trust_file_contains_no_readable_targets()
    {
        var store = NewStore();
        store.Load();
        store.Update([Outlook], [Outlook]);

        var text = Encoding.UTF8.GetString(File.ReadAllBytes(_dir.File("trust.dat")));
        Assert.DoesNotContain("OUTLOOK", text, StringComparison.OrdinalIgnoreCase);
    }
}
