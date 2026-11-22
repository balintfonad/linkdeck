namespace LinkDeck.Core.Config;

/// <summary>Rules for file-system targets: link files that bypass the URL allow-list, and network paths.</summary>
public static class PathPolicy
{
    /// <summary>
    /// Shell link formats that can point at any URL scheme or remote location (<c>search-ms:</c>, <c>file://\\host\…</c>,
    /// <c>ms-msdt:</c>…). Opening them would bypass the http(s) allow-list, so they are neither imported nor launched.
    /// </summary>
    private static readonly HashSet<string> BlockedLinkExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".url", ".website", ".scf", ".search-ms", ".searchconnector-ms", ".library-ms",
    };

    public static bool IsBlockedLinkFile(string path) =>
        BlockedLinkExtensions.Contains(Path.GetExtension(path.Trim().Trim('"')));

    /// <summary>
    /// True for UNC paths (<c>\\host\share</c>, <c>\\?\UNC\…</c>, <c>file://host/…</c>). Touching such a path, even to read
    /// an icon, makes Windows connect to that host and send the user's NTLM credentials. Expand environment variables first.
    /// </summary>
    public static bool IsRemote(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        var trimmed = path.Trim().Trim('"');
        if (trimmed.StartsWith("file:", StringComparison.OrdinalIgnoreCase))
        {
            return !Uri.TryCreate(trimmed, UriKind.Absolute, out var uri) || uri.IsUnc || !string.IsNullOrEmpty(uri.Host);
        }

        var p = trimmed.Replace('/', '\\');
        if (p.StartsWith(@"\\?\UNC\", StringComparison.OrdinalIgnoreCase) || p.StartsWith(@"\\.\UNC\", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (p.StartsWith(@"\\?\", StringComparison.Ordinal) || p.StartsWith(@"\\.\", StringComparison.Ordinal))
        {
            return false; // local device path such as \\?\C:\…
        }

        return p.StartsWith(@"\\", StringComparison.Ordinal);
    }
}
