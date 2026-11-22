using System.Net;
using System.Net.Http.Headers;
using LinkDeck.Core.Icons;

namespace LinkDeck.Core.Tests;

public class FaviconFetcherTests : IDisposable
{
    private readonly TempDir _dir = new();

    public void Dispose() => _dir.Dispose();

    private sealed class FakeHandler : HttpMessageHandler
    {
        public Dictionary<string, Func<HttpResponseMessage>> Routes { get; } = new(StringComparer.Ordinal);

        public List<string> Requested { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var url = request.RequestUri!.ToString();
            Requested.Add(url);
            return Task.FromResult(Routes.TryGetValue(url, out var f) ? f() : new HttpResponseMessage(HttpStatusCode.NotFound));
        }
    }

    private static HttpResponseMessage Image(int size = 64, string type = "image/x-icon")
    {
        var content = new ByteArrayContent(new byte[size]);
        content.Headers.ContentType = new MediaTypeHeaderValue(type);
        return new HttpResponseMessage(HttpStatusCode.OK) { Content = content };
    }

    private static HttpResponseMessage Html(string html)
    {
        var content = new StringContent(html);
        content.Headers.ContentType = new MediaTypeHeaderValue("text/html");
        return new HttpResponseMessage(HttpStatusCode.OK) { Content = content };
    }

    private static HttpResponseMessage Redirect(string location)
    {
        var r = new HttpResponseMessage(HttpStatusCode.Found);
        r.Headers.Location = new Uri(location, UriKind.RelativeOrAbsolute);
        return r;
    }

    [Fact]
    public async Task Fetches_favicon_ico_from_the_site_itself_and_caches_it()
    {
        var handler = new FakeHandler();
        handler.Routes["https://site.example/favicon.ico"] = () => Image();
        using var fetcher = new FaviconFetcher(_dir.Path, handler);

        var path = await fetcher.FetchAsync("https://site.example/some/page", TestContext.Current.CancellationToken);

        Assert.NotNull(path);
        Assert.True(File.Exists(path));
        Assert.Equal(path, fetcher.TryGetCached("https://site.example/other"));
        Assert.All(handler.Requested, u => Assert.StartsWith("https://site.example/", u, StringComparison.Ordinal));
    }

    [Fact]
    public async Task Falls_back_to_link_rel_icon_in_html()
    {
        var handler = new FakeHandler();
        handler.Routes["https://site.example/"] = () => Html("""<html><head><link rel="stylesheet" href="a.css"><link href="/img/icon.png" rel="shortcut icon"></head></html>""");
        handler.Routes["https://site.example/img/icon.png"] = () => Image(type: "image/png");
        using var fetcher = new FaviconFetcher(_dir.Path, handler);

        Assert.NotNull(await fetcher.FetchAsync("https://site.example/", TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Rejects_non_image_content_types_and_svg()
    {
        var handler = new FakeHandler();
        handler.Routes["https://site.example/favicon.ico"] = () => Html("<html>not an icon</html>");
        handler.Routes["https://site.example/"] = () => Html("""<link rel="icon" href="/i.svg">""");
        handler.Routes["https://site.example/i.svg"] = () => Image(type: "image/svg+xml");
        using var fetcher = new FaviconFetcher(_dir.Path, handler);

        Assert.Null(await fetcher.FetchAsync("https://site.example/", TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Rejects_images_over_200_kb()
    {
        var handler = new FakeHandler();
        handler.Routes["https://site.example/favicon.ico"] = () => Image(FaviconFetcher.MaxBytes + 1);
        using var fetcher = new FaviconFetcher(_dir.Path, handler);

        Assert.Null(await fetcher.FetchAsync("https://site.example/", TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Follows_http_redirects_but_not_other_schemes()
    {
        var handler = new FakeHandler();
        handler.Routes["https://a.example/favicon.ico"] = () => Redirect("https://cdn.a.example/fav.ico");
        handler.Routes["https://cdn.a.example/fav.ico"] = () => Image();
        handler.Routes["https://b.example/favicon.ico"] = () => Redirect("file:///C:/Windows/win.ini");
        using var fetcher = new FaviconFetcher(_dir.Path, handler);

        Assert.NotNull(await fetcher.FetchAsync("https://a.example/", TestContext.Current.CancellationToken));
        Assert.Null(await fetcher.FetchAsync("https://b.example/", TestContext.Current.CancellationToken));
        Assert.DoesNotContain(handler.Requested, u => u.StartsWith("file:", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Https_redirect_to_plain_http_is_not_followed()
    {
        var handler = new FakeHandler();
        handler.Routes["https://a.example/favicon.ico"] = () => Redirect("http://a.example/fav.ico");
        handler.Routes["http://a.example/fav.ico"] = () => Image();
        using var fetcher = new FaviconFetcher(_dir.Path, handler);

        Assert.Null(await fetcher.FetchAsync("https://a.example/", TestContext.Current.CancellationToken));
        Assert.DoesNotContain(handler.Requested, u => u.StartsWith("http:", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Public_site_cannot_redirect_into_the_local_network()
    {
        var handler = new FakeHandler();
        handler.Routes["https://evil.example/favicon.ico"] = () => Redirect("http://192.168.1.1/apply.cgi?reboot=1");
        handler.Routes["http://192.168.1.1/apply.cgi?reboot=1"] = () => Image();
        using var fetcher = new FaviconFetcher(_dir.Path, handler);

        Assert.Null(await fetcher.FetchAsync("https://evil.example/", TestContext.Current.CancellationToken));
        Assert.DoesNotContain(handler.Requested, u => u.Contains("192.168.1.1", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Public_site_cannot_point_its_icon_link_at_loopback()
    {
        var handler = new FakeHandler();
        handler.Routes["https://evil.example/"] = () => Html("""<link rel="icon" href="http://127.0.0.1:8080/admin/delete">""");
        handler.Routes["http://127.0.0.1:8080/admin/delete"] = () => Image();
        using var fetcher = new FaviconFetcher(_dir.Path, handler);

        Assert.Null(await fetcher.FetchAsync("https://evil.example/", TestContext.Current.CancellationToken));
        Assert.DoesNotContain(handler.Requested, u => u.Contains("127.0.0.1", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Intranet_sites_still_get_favicons_from_private_addresses()
    {
        var handler = new FakeHandler();
        handler.Routes["http://10.0.0.5/favicon.ico"] = () => Redirect("http://10.0.0.6/fav.ico");
        handler.Routes["http://10.0.0.6/fav.ico"] = () => Image();
        using var fetcher = new FaviconFetcher(_dir.Path, handler);

        Assert.NotNull(await fetcher.FetchAsync("http://10.0.0.5/wiki", TestContext.Current.CancellationToken));
    }

    [Theory]
    [InlineData("http://localhost/", true)]
    [InlineData("http://app.localhost/", true)]
    [InlineData("http://127.0.0.1/", true)]
    [InlineData("http://0.0.0.0/", true)]
    [InlineData("http://10.1.2.3/", true)]
    [InlineData("http://172.16.0.1/", true)]
    [InlineData("http://172.31.255.255/", true)]
    [InlineData("http://192.168.0.1/", true)]
    [InlineData("http://169.254.169.254/", true)]
    [InlineData("http://100.64.0.1/", true)]
    [InlineData("http://[::1]/", true)]
    [InlineData("http://[fe80::1]/", true)]
    [InlineData("http://[fd00::1]/", true)]
    [InlineData("http://[::ffff:192.168.0.1]/", true)]
    [InlineData("http://2130706433/", true)]
    [InlineData("http://172.32.0.1/", false)]
    [InlineData("http://8.8.8.8/", false)]
    [InlineData("http://[2001:4860:4860::8888]/", false)]
    [InlineData("https://site.example/", false)]
    public void Classifies_private_hosts(string url, bool expected) =>
        Assert.Equal(expected, FaviconFetcher.IsPrivateHost(new Uri(url)));

    [Fact]
    public async Task Does_nothing_for_disallowed_urls()
    {
        var handler = new FakeHandler();
        using var fetcher = new FaviconFetcher(_dir.Path, handler);

        Assert.Null(await fetcher.FetchAsync("javascript:alert(1)", TestContext.Current.CancellationToken));
        Assert.Empty(handler.Requested);
    }

    [Theory]
    [InlineData("""<link rel="icon" href="/a.ico">""", "/a.ico")]
    [InlineData("""<LINK REL='apple-touch-icon' HREF='b.png'>""", "b.png")]
    [InlineData("""<link rel=icon href=c.png>""", "c.png")]
    [InlineData("""<link rel="stylesheet" href="x.css">""", null)]
    public void Finds_icon_href(string html, string? expected) => Assert.Equal(expected, FaviconFetcher.FindIconHref(html));
}
