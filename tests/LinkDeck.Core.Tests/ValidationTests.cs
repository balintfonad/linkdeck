using LinkDeck.Core.Config;
using LinkDeck.Core.Models;

namespace LinkDeck.Core.Tests;

public class ValidationTests
{
    [Theory]
    [InlineData("https://example.com", true)]
    [InlineData("http://example.com/path?q=1", true)]
    [InlineData("HTTPS://EXAMPLE.COM", true)]
    [InlineData("ftp://example.com", false)]
    [InlineData("file:///C:/Windows", false)]
    [InlineData("javascript:alert(1)", false)]
    [InlineData("ms-settings:display", false)]
    [InlineData("example.com", false)]
    [InlineData("/relative", false)]
    [InlineData(" https://example.com", false)]
    [InlineData("", false)]
    public void Url_scheme_allow_list(string url, bool allowed) => Assert.Equal(allowed, UrlPolicy.IsAllowed(url));

    [Theory]
    [InlineData("server", true)]
    [InlineData("server.example.com", true)]
    [InlineData("my-host-01", true)]
    [InlineData("10.0.0.15", true)]
    [InlineData("::1", true)]
    [InlineData("fe80::1", true)]
    [InlineData("2001:db8::42", true)]
    [InlineData("", false)]
    [InlineData(" server", false)]
    [InlineData("a b", false)]
    [InlineData("host /admin", false)]
    [InlineData("host\"", false)]
    [InlineData("-bad", false)]
    [InlineData("bad-", false)]
    [InlineData("a..b", false)]
    [InlineData("host:3389", false)]
    [InlineData("10.1", false)]
    [InlineData("fe80::1%eth0", false)]
    [InlineData("host\nusername:s:x", false)]
    public void Rdp_host_rules(string host, bool valid) => Assert.Equal(valid, RdpAddress.IsValidHost(host));

    [Theory]
    [InlineData(null, true)]
    [InlineData(1, true)]
    [InlineData(3389, true)]
    [InlineData(65535, true)]
    [InlineData(0, false)]
    [InlineData(-1, false)]
    [InlineData(65536, false)]
    public void Rdp_port_rules(int? port, bool valid) => Assert.Equal(valid, RdpAddress.IsValidPort(port));

    [Theory]
    [InlineData("server", "server", null)]
    [InlineData("server:3390", "server", 3390)]
    [InlineData("10.0.0.1:1", "10.0.0.1", 1)]
    [InlineData("[::1]", "::1", null)]
    [InlineData("[::1]:3390", "::1", 3390)]
    [InlineData("fe80::1", "fe80::1", null)]
    public void Rdp_address_parsing(string text, string host, int? port)
    {
        Assert.True(RdpAddress.TryParse(text, out var h, out var p));
        Assert.Equal(host, h);
        Assert.Equal(port, p);
    }

    [Theory]
    [InlineData("server:0")]
    [InlineData("server:99999")]
    [InlineData("server:abc")]
    [InlineData("[::1")]
    [InlineData("a b:3389")]
    public void Rdp_address_parsing_rejects(string text) => Assert.False(RdpAddress.TryParse(text, out _, out _));

    [Fact]
    public void Rdp_format_brackets_ipv6()
    {
        Assert.Equal("[::1]:3389", RdpAddress.Format("::1", 3389));
        Assert.Equal("host:3390", RdpAddress.Format("host", 3390));
    }

    [Fact]
    public void Rdp_username_with_newline_is_rejected()
    {
        var item = new RdpItem { Id = "1", Name = "X", Host = "h", Username = "admin\r\nfull address:s:evil" };

        Assert.NotNull(ItemValidator.Validate(item));
    }

    [Fact]
    public void Valid_items_pass()
    {
        Assert.Null(ItemValidator.Validate(new UrlItem { Id = "1", Name = "X", Url = "https://x.example" }));
        Assert.Null(ItemValidator.Validate(new PathItem { Id = "1", Name = "X", Path = "C:\\x" }));
        Assert.Null(ItemValidator.Validate(new RdpItem { Id = "1", Name = "X", Host = "h", Port = 3390, Username = "DOMAIN\\me" }));
    }

    [Theory]
    [InlineData("Alt", "Ctrl", true)]
    [InlineData("Ctrl", "Win", true)]
    [InlineData("win", "alt", true)]
    [InlineData("Alt", "Alt", false)]
    [InlineData("Shift", "Ctrl", false)]
    [InlineData("Alt", "Space", false)]
    [InlineData("", "Ctrl", false)]
    public void Hotkey_validation(string hold, string step, bool valid) =>
        Assert.Equal(valid, new Hotkey { Hold = hold, Step = step }.IsValid());

    [Fact]
    public void Default_hotkey_is_hold_alt_step_ctrl()
    {
        Assert.Equal(("Alt", "Ctrl"), (Hotkey.Default.Hold, Hotkey.Default.Step));
        Assert.Equal("Alt+Ctrl", Hotkey.Default.ToString());
    }
}
