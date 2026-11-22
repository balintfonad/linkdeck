using System.Globalization;
using System.Net;
using System.Net.Sockets;

namespace LinkDeck.Core.Models;

/// <summary>Helpers for RDP host/port values. Validation is strict so nothing can be injected into mstsc arguments or .rdp files.</summary>
public static class RdpAddress
{
    /// <summary>Formats <c>host:port</c>, wrapping IPv6 literals in brackets.</summary>
    public static string Format(string host, int port)
    {
        var h = IsIPv6(host) ? $"[{host}]" : host;
        return string.Create(CultureInfo.InvariantCulture, $"{h}:{port}");
    }

    public static bool IsValidHost(string? host)
    {
        if (string.IsNullOrWhiteSpace(host) || host.Length > 253 || host != host.Trim())
        {
            return false;
        }

        if (IsIPv6(host))
        {
            return true;
        }

        if (IPAddress.TryParse(host, out var ip) && ip.AddressFamily == AddressFamily.InterNetwork)
        {
            // IPAddress.TryParse accepts shorthand like "10.1"; require dotted quad.
            return host.Count(c => c == '.') == 3;
        }

        // Hostname: labels of letters, digits and hyphens, not starting/ending with a hyphen.
        var labels = host.Split('.');
        foreach (var label in labels)
        {
            if (label.Length is 0 or > 63 || label[0] == '-' || label[^1] == '-')
            {
                return false;
            }

            foreach (var c in label)
            {
                if (!(char.IsAsciiLetterOrDigit(c) || c == '-'))
                {
                    return false;
                }
            }
        }

        return true;
    }

    public static bool IsValidPort(int? port) => port is null or (>= 1 and <= 65535);

    /// <summary>Parses <c>host</c>, <c>host:port</c>, <c>[v6]</c> or <c>[v6]:port</c>.</summary>
    public static bool TryParse(string? text, out string host, out int? port)
    {
        host = "";
        port = null;
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        text = text.Trim();
        if (text.StartsWith('['))
        {
            var end = text.IndexOf(']', StringComparison.Ordinal);
            if (end < 0)
            {
                return false;
            }

            host = text[1..end];
            var rest = text[(end + 1)..];
            if (rest.Length > 0)
            {
                if (rest[0] != ':' || !TryParsePort(rest[1..], out var p))
                {
                    return false;
                }

                port = p;
            }

            return IsIPv6(host);
        }

        if (IsIPv6(text))
        {
            host = text;
            return true;
        }

        var colon = text.LastIndexOf(':');
        if (colon >= 0)
        {
            if (!TryParsePort(text[(colon + 1)..], out var p))
            {
                return false;
            }

            port = p;
            text = text[..colon];
        }

        host = text;
        return IsValidHost(host);
    }

    private static bool TryParsePort(string s, out int port) =>
        int.TryParse(s, NumberStyles.None, CultureInfo.InvariantCulture, out port) && IsValidPort(port);

    private static bool IsIPv6(string host) =>
        host.Contains(':', StringComparison.Ordinal)
        && IPAddress.TryParse(host, out var ip)
        && ip.AddressFamily == AddressFamily.InterNetworkV6
        && !host.Contains('%', StringComparison.Ordinal);
}
