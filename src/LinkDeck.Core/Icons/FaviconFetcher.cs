using System.Net;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using LinkDeck.Core.Config;

namespace LinkDeck.Core.Icons;

/// <summary>
/// Fetches a site's favicon directly from the site itself (never from a third-party service) and caches it.
/// This is LinkDeck's only network access: 5 s timeout, 200 KB limit, image content types only,
/// redirects followed only to http(s).
/// </summary>
public sealed partial class FaviconFetcher : IDisposable
{
    public const int MaxBytes = 200 * 1024;
    public const int MaxRedirects = 5;
    public static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);

    // Two clients so a pooled connection opened for an intranet site is never reused by a public site's request.
    private readonly HttpClient _publicHttp;
    private readonly HttpClient _intranetHttp;
    private readonly string _cacheDirectory;

    /// <param name="handler">For tests. The handler must not follow redirects itself. Used for both clients.</param>
    public FaviconFetcher(string cacheDirectory, HttpMessageHandler? handler = null)
    {
        _cacheDirectory = cacheDirectory;
        _publicHttp = CreateClient(handler ?? CreateHandler(allowPrivate: false), disposeHandler: handler is null);
        _intranetHttp = handler is null ? CreateClient(CreateHandler(allowPrivate: true), disposeHandler: true) : _publicHttp;
    }

    private static HttpClient CreateClient(HttpMessageHandler handler, bool disposeHandler)
    {
        var client = new HttpClient(handler, disposeHandler) { Timeout = System.Threading.Timeout.InfiniteTimeSpan };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("LinkDeck/1.0");
        return client;
    }

    private static SocketsHttpHandler CreateHandler(bool allowPrivate)
    {
        var handler = new SocketsHttpHandler
        {
            AllowAutoRedirect = false,
            AutomaticDecompression = DecompressionMethods.All,
            UseCookies = false,

            // Through a proxy the resolved address cannot be checked, so public sites are fetched directly.
            UseProxy = allowPrivate,
        };
        if (!allowPrivate)
        {
            // Checks the addresses a name actually resolves to, so a public hostname pointing at 192.168.x.x
            // (or DNS rebinding) cannot steer the request into the local network.
            handler.ConnectCallback = ConnectToPublicAddressAsync;
        }

        return handler;
    }

    private static async ValueTask<Stream> ConnectToPublicAddressAsync(SocketsHttpConnectionContext context, CancellationToken ct)
    {
        var addresses = await Dns.GetHostAddressesAsync(context.DnsEndPoint.Host, ct).ConfigureAwait(false);
        if (addresses.Length == 0 || addresses.Any(IsPrivateAddress))
        {
            throw new HttpRequestException($"Blocked: {context.DnsEndPoint.Host} resolves to a private address.");
        }

        var socket = new Socket(SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
        try
        {
            await socket.ConnectAsync(addresses, context.DnsEndPoint.Port, ct).ConfigureAwait(false);
            return new NetworkStream(socket, ownsSocket: true);
        }
        catch
        {
            socket.Dispose();
            throw;
        }
    }

    /// <summary>The cache file for a URL's host (whether or not it exists).</summary>
    public string CachePathFor(string url)
    {
        var host = Uri.TryCreate(url, UriKind.Absolute, out var uri) ? uri.Host.ToLowerInvariant() : url;
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(host)))[..16];
        return Path.Combine(_cacheDirectory, $"{hash}.img");
    }

    public string? TryGetCached(string url)
    {
        var path = CachePathFor(url);
        return File.Exists(path) ? path : null;
    }

    /// <summary>Fetches and caches the favicon. Returns the cache path, or null when none could be fetched.</summary>
    public async Task<string?> FetchAsync(string url, CancellationToken cancellationToken = default)
    {
        if (!UrlPolicy.IsAllowed(url))
        {
            return null;
        }

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(Timeout);
        try
        {
            var site = new Uri(url);
            var root = new Uri(site.GetLeftPart(UriPartial.Authority) + "/");

            // Intranet links may fetch from private addresses; public sites may not steer requests into the local network.
            var allowPrivate = IsPrivateHost(site);

            var bytes = await TryGetImageAsync(new Uri(root, "favicon.ico"), allowPrivate, cts.Token).ConfigureAwait(false);
            if (bytes is null)
            {
                var html = await TryGetTextAsync(site, allowPrivate, cts.Token).ConfigureAwait(false);
                var href = html is null ? null : FindIconHref(html);
                if (href is not null && Uri.TryCreate(site, href, out var iconUri) && IsHttp(iconUri) && !IsDowngrade(site, iconUri))
                {
                    bytes = await TryGetImageAsync(iconUri, allowPrivate, cts.Token).ConfigureAwait(false);
                }
            }

            if (bytes is null)
            {
                return null;
            }

            Directory.CreateDirectory(_cacheDirectory);
            var path = CachePathFor(url);
            await File.WriteAllBytesAsync(path, bytes, cts.Token).ConfigureAwait(false);
            return path;
        }
        catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException or IOException or UriFormatException)
        {
            return null;
        }
    }

    public void Dispose()
    {
        _publicHttp.Dispose();
        if (!ReferenceEquals(_intranetHttp, _publicHttp))
        {
            _intranetHttp.Dispose();
        }
    }

    /// <summary>Finds the first <c>&lt;link rel="...icon..." href="..."&gt;</c> in HTML.</summary>
    public static string? FindIconHref(string html)
    {
        foreach (Match link in LinkTagRegex().Matches(html))
        {
            var tag = link.Value;
            var rel = AttributeRegex("rel").Match(tag);
            if (!rel.Success || !rel.Groups["v"].Value.Contains("icon", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var href = AttributeRegex("href").Match(tag);
            if (href.Success && !string.IsNullOrWhiteSpace(href.Groups["v"].Value))
            {
                return WebUtility.HtmlDecode(href.Groups["v"].Value.Trim());
            }
        }

        return null;
    }

    /// <summary>
    /// True for loopback, private, link-local, CGNAT and unique-local addresses (IP literals) and <c>localhost</c> names.
    /// Hostnames that merely resolve to private addresses are not detected (no DNS lookup here).
    /// </summary>
    public static bool IsPrivateHost(Uri uri)
    {
        var host = uri.Host;
        if (uri.IsLoopback
            || host.Equals("localhost", StringComparison.OrdinalIgnoreCase)
            || host.EndsWith(".localhost", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return uri.HostNameType is UriHostNameType.IPv4 or UriHostNameType.IPv6
            && IPAddress.TryParse(host.Trim('[', ']'), out var ip)
            && IsPrivateAddress(ip);
    }

    /// <summary>Loopback, private, link-local, CGNAT, unique-local, multicast and broadcast addresses.</summary>
    public static bool IsPrivateAddress(IPAddress ip)
    {
        if (ip.AddressFamily == AddressFamily.InterNetworkV6)
        {
            if (ip.IsIPv4MappedToIPv6)
            {
                ip = ip.MapToIPv4();
            }
            else
            {
                return IPAddress.IPv6Loopback.Equals(ip) || IPAddress.IPv6Any.Equals(ip)
                    || ip.IsIPv6LinkLocal || ip.IsIPv6SiteLocal || ip.IsIPv6UniqueLocal || ip.IsIPv6Multicast;
            }
        }

        var b = ip.GetAddressBytes();
        return b[0] is 0 or 10 or 127 or >= 224
            || (b[0] == 100 && b[1] is >= 64 and <= 127)
            || (b[0] == 169 && b[1] == 254)
            || (b[0] == 172 && b[1] is >= 16 and <= 31)
            || (b[0] == 192 && b[1] == 168);
    }

    private async Task<byte[]?> TryGetImageAsync(Uri uri, bool allowPrivate, CancellationToken ct)
    {
        using var response = await SendFollowingRedirectsAsync(uri, allowPrivate, ct).ConfigureAwait(false);
        if (response is null || !response.IsSuccessStatusCode || !IsAcceptedImage(response.Content.Headers.ContentType))
        {
            return null;
        }

        var bytes = await ReadLimitedAsync(response, ct).ConfigureAwait(false);
        return bytes is { Length: > 0 } ? bytes : null;
    }

    private async Task<string?> TryGetTextAsync(Uri uri, bool allowPrivate, CancellationToken ct)
    {
        using var response = await SendFollowingRedirectsAsync(uri, allowPrivate, ct).ConfigureAwait(false);
        if (response is null || !response.IsSuccessStatusCode)
        {
            return null;
        }

        var media = response.Content.Headers.ContentType?.MediaType;
        if (media is not null && !media.Contains("html", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var bytes = await ReadLimitedAsync(response, ct, truncate: true).ConfigureAwait(false);
        return bytes is null ? null : Encoding.UTF8.GetString(bytes);
    }

    private async Task<HttpResponseMessage?> SendFollowingRedirectsAsync(Uri uri, bool allowPrivate, CancellationToken ct)
    {
        var current = uri;
        for (var i = 0; i <= MaxRedirects; i++)
        {
            if (!IsHttp(current) || (!allowPrivate && IsPrivateHost(current)))
            {
                return null;
            }

            using var request = new HttpRequestMessage(HttpMethod.Get, current);
            var http = allowPrivate ? _intranetHttp : _publicHttp;
            var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
            var status = (int)response.StatusCode;
            if (status is >= 300 and < 400 && response.Headers.Location is { } location)
            {
                response.Dispose();
                var next = location.IsAbsoluteUri ? location : new Uri(current, location);
                if (IsDowngrade(current, next))
                {
                    return null;
                }

                current = next;
                continue;
            }

            return response;
        }

        return null;
    }

    /// <summary>Reads at most <see cref="MaxBytes"/>. Images over the limit are rejected; HTML is truncated.</summary>
    private static async Task<byte[]?> ReadLimitedAsync(HttpResponseMessage response, CancellationToken ct, bool truncate = false)
    {
        if (!truncate && response.Content.Headers.ContentLength > MaxBytes)
        {
            return null;
        }

        await using var stream = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        var buffer = new byte[MaxBytes + 1];
        var total = 0;
        int read;
        while (total < buffer.Length && (read = await stream.ReadAsync(buffer.AsMemory(total), ct).ConfigureAwait(false)) > 0)
        {
            total += read;
        }

        if (total > MaxBytes)
        {
            if (!truncate)
            {
                return null;
            }

            total = MaxBytes;
        }

        return buffer.AsSpan(0, total).ToArray();
    }

    private static bool IsAcceptedImage(MediaTypeHeaderValue? contentType)
    {
        var media = contentType?.MediaType;
        // SVG is excluded: it is a document format, not a raster image the UI decodes.
        return media is not null
            && media.StartsWith("image/", StringComparison.OrdinalIgnoreCase)
            && !media.Contains("svg", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>https → http: the rest of the exchange could be read or altered on the network.</summary>
    private static bool IsDowngrade(Uri from, Uri to) =>
        from.Scheme == Uri.UriSchemeHttps && to.Scheme == Uri.UriSchemeHttp;

    private static bool IsHttp(Uri uri) => uri.IsAbsoluteUri && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);

    [GeneratedRegex(@"<link\b[^>]*>", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex LinkTagRegex();

    [GeneratedRegex(@"\brel\s*=\s*(?:""(?<v>[^""]*)""|'(?<v>[^']*)'|(?<v>[^\s>]+))", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex RelRegex();

    [GeneratedRegex(@"\bhref\s*=\s*(?:""(?<v>[^""]*)""|'(?<v>[^']*)'|(?<v>[^\s>]+))", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex HrefRegex();

    private static Regex AttributeRegex(string name) => name == "rel" ? RelRegex() : HrefRegex();
}
