using LinkDeck.Core.Config;
using LinkDeck.Core.Models;

namespace LinkDeck.Core.Import;

/// <summary>
/// A not-yet-saved item, as shown in the quick-add dialog.
/// <see cref="Target"/> is the URL, the path, or <c>host[:port]</c> for RDP.
/// </summary>
public sealed record ItemDraft
{
    public ItemType Type { get; init; }

    public string Name { get; init; } = "";

    public string Target { get; init; } = "";

    public string? Args { get; init; }

    public string? WorkingDir { get; init; }

    public string? Username { get; init; }

    public string? Color { get; init; }

    /// <summary>Guesses the type of a typed target: http(s) URL → url; otherwise path.</summary>
    public static ItemType DetectType(string? target)
    {
        var t = target?.Trim() ?? "";
        if (UrlPolicy.IsAllowed(t))
        {
            return ItemType.Url;
        }

        if (t.StartsWith("rdp://", StringComparison.OrdinalIgnoreCase))
        {
            return ItemType.Rdp;
        }

        return ItemType.Path;
    }

    /// <summary>Builds and validates an item. Returns the error instead when invalid.</summary>
    public (Item? Item, string? Error) ToItem(string? id = null)
    {
        var itemId = id ?? Item.NewId();
        var name = Name.Trim();
        var target = Target.Trim();
        Item item;
        switch (Type)
        {
            case ItemType.Url:
                item = new UrlItem { Id = itemId, Name = name, Url = target };
                break;
            case ItemType.Path:
                item = new PathItem { Id = itemId, Name = name, Path = target, Args = NullIfEmpty(Args), WorkingDir = NullIfEmpty(WorkingDir) };
                break;
            case ItemType.Rdp:
                if (target.StartsWith("rdp://", StringComparison.OrdinalIgnoreCase))
                {
                    target = target[6..].TrimEnd('/');
                }

                if (!RdpAddress.TryParse(target, out var host, out var port))
                {
                    return (null, $"Invalid RDP address '{target}'. Use host or host:port.");
                }

                item = new RdpItem { Id = itemId, Name = name, Host = host, Port = port, Username = NullIfEmpty(Username) };
                break;
            default:
                return (null, "Unknown item type.");
        }

        item = item with { Color = HexColor.Normalize(Color) };
        var error = ItemValidator.Validate(item);
        return error is null ? (item, null) : (null, error);
    }

    public static ItemDraft FromItem(Item item) => FromItemCore(item) with { Color = item.Color };

    private static ItemDraft FromItemCore(Item item) => item switch
    {
        UrlItem u => new ItemDraft { Type = ItemType.Url, Name = u.Name, Target = u.Url },
        PathItem p => new ItemDraft { Type = ItemType.Path, Name = p.Name, Target = p.Path, Args = p.Args, WorkingDir = p.WorkingDir },
        RdpItem r => new ItemDraft
        {
            Type = ItemType.Rdp,
            Name = r.Name,
            Target = r.Port is null ? FormatHost(r.Host) : r.Target,
            Username = r.Username,
        },
        _ => new ItemDraft(),
    };

    private static string FormatHost(string host) => host.Contains(':', StringComparison.Ordinal) ? $"[{host}]" : host;

    private static string? NullIfEmpty(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
}
