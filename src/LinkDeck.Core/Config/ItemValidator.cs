using System.Globalization;
using LinkDeck.Core.Models;

namespace LinkDeck.Core.Config;

/// <summary>Validation rules for items. Returns a human-readable error, or <c>null</c> when valid.</summary>
public static class ItemValidator
{
    public static string? Validate(Item item) => ValidateCommon(item) ?? item switch
    {
        UrlItem u => ValidateUrl(u.Url),
        PathItem p => ValidatePath(p),
        RdpItem r => ValidateRdp(r),
        _ => "Unknown item type.",
    };

    private static string? ValidateCommon(Item item)
    {
        if (string.IsNullOrWhiteSpace(item.Name))
        {
            return "Name is empty.";
        }

        return TextSafety.HasControlOrDirectionChars(item.Name)
            ? "Name contains control or text-direction characters."
            : null;
    }

    private static string? ValidatePath(PathItem p)
    {
        if (string.IsNullOrWhiteSpace(p.Path))
        {
            return "Path is empty.";
        }

        if (TextSafety.HasHiddenChars(p.Path))
        {
            return "Path contains invisible or text-direction characters.";
        }

        if (TextSafety.HasHiddenChars(p.Args))
        {
            return "Arguments contain line breaks, invisible or text-direction characters.";
        }

        return TextSafety.HasHiddenChars(p.WorkingDir)
            ? "Working directory contains invisible or text-direction characters."
            : null;
    }

    public static string? ValidateUrl(string? url)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            return "URL is empty.";
        }

        if (TextSafety.HasHiddenChars(url))
        {
            return "URL contains invisible or text-direction characters.";
        }

        return UrlPolicy.IsAllowed(url) ? null : "Only absolute http and https URLs are allowed.";
    }

    private static string? ValidateRdp(RdpItem r)
    {
        if (!RdpAddress.IsValidHost(r.Host))
        {
            return $"Invalid host '{r.Host}'. Use a hostname, IPv4 or IPv6 address.";
        }

        if (!RdpAddress.IsValidPort(r.Port))
        {
            return "Port must be between 1 and 65535.";
        }

        if (r.Username is { } user && (user.Length > 256 || TextSafety.HasHiddenChars(user)))
        {
            return "Username contains invalid characters.";
        }

        return null;
    }
}

/// <summary>URL scheme allow-list: only <c>http</c> and <c>https</c>.</summary>
public static class UrlPolicy
{
    public static bool IsAllowed(string? url) =>
        url is not null
        && url == url.Trim()
        && Uri.TryCreate(url, UriKind.Absolute, out var uri)
        && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps)
        && !string.IsNullOrEmpty(uri.Host);
}

/// <summary>
/// Detects characters that make displayed text differ from what runs: control characters, invisible
/// format characters and Unicode direction overrides (e.g. <c>gpj\u202E.exe</c> displays as <c>exe.jpg</c>).
/// </summary>
public static class TextSafety
{
    /// <summary>Control characters and bidirectional controls. Other format characters (emoji joiners) are allowed.</summary>
    public static bool HasControlOrDirectionChars(string? text) =>
        text is not null && text.Any(c => char.IsControl(c) || IsDirectionControl(c));

    /// <summary>Control characters and every format character (zero-width, direction marks, BOM). For paths, arguments and URLs.</summary>
    public static bool HasHiddenChars(string? text) =>
        text is not null && text.Any(c => char.IsControl(c) || CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.Format);

    private static bool IsDirectionControl(char c) =>
        c is '\u061C' or '\u200E' or '\u200F' or (>= '\u202A' and <= '\u202E') or (>= '\u2066' and <= '\u2069');
}
