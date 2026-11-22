using LinkDeck.Core.Abstractions;
using LinkDeck.Core.Config;
using LinkDeck.Core.Models;

namespace LinkDeck.Core.Import;

/// <summary>What the UI extracted from a drag-and-drop operation.</summary>
public sealed record DropData
{
    public IReadOnlyList<string> Files { get; init; } = [];

    /// <summary>A URL dragged from a browser (address bar icon or link).</summary>
    public string? Url { get; init; }

    /// <summary>The page title, when the browser provides it.</summary>
    public string? UrlTitle { get; init; }

    public string? Text { get; init; }

    /// <summary>Shell items without a usable file (e.g. apps dragged from the Start menu).</summary>
    public IReadOnlyList<ShellItemInfo> ShellItems { get; init; } = [];
}

public sealed record DropResult(IReadOnlyList<ItemDraft> Drafts, IReadOnlyList<string> Ignored)
{
    public bool IsEmpty => Drafts.Count == 0;
}

/// <summary>Turns dropped data into item drafts according to the spec's drag-and-drop table.</summary>
public sealed class DropParser
{
    private readonly IShortcutResolver? _shortcuts;

    public DropParser(IShortcutResolver? shortcuts) => _shortcuts = shortcuts;

    public DropResult Parse(DropData data)
    {
        var drafts = new List<ItemDraft>();
        var ignored = new List<string>();

        if (data.Files.Count == 0 && data.ShellItems.Count > 0)
        {
            foreach (var shell in data.ShellItems)
            {
                var draft = ItemCategories.IsShellTarget(shell.Target)
                    ? new ItemDraft { Type = ItemType.Path, Name = shell.DisplayName, Target = shell.Target }
                    : ParseFile(shell.Target) is { } fileDraft ? fileDraft with { Name = string.IsNullOrWhiteSpace(shell.DisplayName) ? fileDraft.Name : shell.DisplayName } : null;
                if (draft is null)
                {
                    ignored.Add(shell.DisplayName);
                }
                else
                {
                    drafts.Add(draft);
                }
            }

            return new DropResult(drafts, ignored);
        }

        if (data.Files.Count > 0)
        {
            foreach (var file in data.Files)
            {
                var draft = ParseFile(file);
                if (draft is null)
                {
                    ignored.Add(file);
                }
                else
                {
                    drafts.Add(draft);
                }
            }

            return new DropResult(drafts, ignored);
        }

        var url = data.Url?.Trim();
        if (!string.IsNullOrEmpty(url))
        {
            if (UrlPolicy.IsAllowed(url))
            {
                drafts.Add(new ItemDraft { Type = ItemType.Url, Name = NameForUrl(url, data.UrlTitle), Target = url });
            }
            else
            {
                ignored.Add(url);
            }

            return new DropResult(drafts, ignored);
        }

        var text = data.Text?.Trim();
        if (!string.IsNullOrEmpty(text))
        {
            // Browsers sometimes put "url\ntitle" on the text format.
            var lines = text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (lines.Length > 0 && UrlPolicy.IsAllowed(lines[0]))
            {
                var title = lines.Length > 1 ? lines[1] : null;
                drafts.Add(new ItemDraft { Type = ItemType.Url, Name = NameForUrl(lines[0], title), Target = lines[0] });
            }
            else
            {
                ignored.Add(text.Length > 80 ? text[..80] + "…" : text);
            }
        }

        return new DropResult(drafts, ignored);
    }

    public ItemDraft? ParseFile(string file)
    {
        if (string.IsNullOrWhiteSpace(file))
        {
            return null;
        }

        if (Directory.Exists(file))
        {
            var trimmed = Path.TrimEndingDirectorySeparator(file);
            var name = Path.GetFileName(trimmed);
            return new ItemDraft { Type = ItemType.Path, Name = string.IsNullOrEmpty(name) ? trimmed : name, Target = file };
        }

        if (!File.Exists(file))
        {
            return null;
        }

        var baseName = Path.GetFileNameWithoutExtension(file);
        var ext = Path.GetExtension(file).ToLowerInvariant();
        switch (ext)
        {
            case ".lnk":
                var link = _shortcuts?.Resolve(file);
                if (link is not null && !string.IsNullOrWhiteSpace(link.TargetPath))
                {
                    return new ItemDraft
                    {
                        Type = ItemType.Path,
                        Name = baseName,
                        Target = link.TargetPath,
                        Args = string.IsNullOrWhiteSpace(link.Arguments) ? null : link.Arguments,
                        WorkingDir = string.IsNullOrWhiteSpace(link.WorkingDirectory) ? null : link.WorkingDirectory,
                    };
                }

                // Unresolvable (e.g. advertised MSI shortcut): the shell can still open the .lnk itself.
                return new ItemDraft { Type = ItemType.Path, Name = baseName, Target = file };

            case ".rdp":
                RdpFileInfo? info;
                try
                {
                    info = RdpFileParser.Parse(File.ReadAllText(file));
                }
                catch (IOException)
                {
                    info = null;
                }

                if (info is null)
                {
                    return null;
                }

                var target = info.Port is null
                    ? (info.Host.Contains(':', StringComparison.Ordinal) ? $"[{info.Host}]" : info.Host)
                    : RdpAddress.Format(info.Host, info.Port.Value);
                return new ItemDraft { Type = ItemType.Rdp, Name = baseName, Target = target, Username = info.Username };

            case ".url":
                // Internet shortcut: [InternetShortcut] URL=... Only web addresses are imported. Opening the .url file
                // itself would let any scheme through (search-ms:, file://\\host\…), bypassing the http(s) allow-list.
                try
                {
                    var urlLine = File.ReadLines(file).FirstOrDefault(l => l.StartsWith("URL=", StringComparison.OrdinalIgnoreCase));
                    var u = urlLine?[4..].Trim();
                    if (u is not null && UrlPolicy.IsAllowed(u))
                    {
                        return new ItemDraft { Type = ItemType.Url, Name = baseName, Target = u };
                    }
                }
                catch (IOException)
                {
                }

                return null;

            case { } when PathPolicy.IsBlockedLinkFile(file):
                return null;

            default:
                return new ItemDraft { Type = ItemType.Path, Name = baseName, Target = file };
        }
    }

    public static string NameForUrl(string url, string? title)
    {
        if (!string.IsNullOrWhiteSpace(title))
        {
            return title.Trim();
        }

        if (Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            var host = uri.Host;
            return host.StartsWith("www.", StringComparison.OrdinalIgnoreCase) ? host[4..] : host;
        }

        return url;
    }
}
