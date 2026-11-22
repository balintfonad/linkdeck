namespace LinkDeck.Core.Models;

/// <summary>Automatic popup tabs by kind of item.</summary>
public enum ItemCategory
{
    Web,
    App,
    FileOrFolder,
    RemoteDesktop,
}

public static class ItemCategories
{
    private static readonly HashSet<string> AppExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".exe", ".lnk", ".bat", ".cmd", ".com", ".msc", ".appref-ms", ".ps1",
    };

    public static ItemCategory Of(Item item) => item switch
    {
        UrlItem => ItemCategory.Web,
        RdpItem => ItemCategory.RemoteDesktop,
        PathItem p when IsShellTarget(p.Path) || AppExtensions.Contains(Path.GetExtension(p.Path.Trim().Trim('"'))) => ItemCategory.App,
        _ => ItemCategory.FileOrFolder,
    };

    public static string DisplayName(ItemCategory category) => category switch
    {
        ItemCategory.Web => "Web",
        ItemCategory.App => "Apps",
        ItemCategory.FileOrFolder => "Files & folders",
        _ => "Remote desktop",
    };

    /// <summary>Id of the editable group that holds this kind of item by default (created for new configs).</summary>
    public static string DefaultGroupId(ItemCategory category) => category switch
    {
        ItemCategory.Web => "web",
        ItemCategory.App => "apps",
        ItemCategory.FileOrFolder => "files",
        _ => "rdp",
    };

    /// <summary>The kind of item a draft of this type will be (path drafts are judged by their target).</summary>
    public static ItemCategory OfDraft(ItemType type, string target) => type switch
    {
        ItemType.Url => ItemCategory.Web,
        ItemType.Rdp => ItemCategory.RemoteDesktop,
        _ => Of(new PathItem { Path = target ?? "" }),
    };

    public static IReadOnlyList<ItemCategory> All { get; } = [ItemCategory.Web, ItemCategory.App, ItemCategory.FileOrFolder, ItemCategory.RemoteDesktop];

    /// <summary>Shell namespace targets such as <c>shell:AppsFolder\…</c> (Start-menu and Store apps).</summary>
    public static bool IsShellTarget(string path) =>
        path.StartsWith("shell:", StringComparison.OrdinalIgnoreCase);
}
