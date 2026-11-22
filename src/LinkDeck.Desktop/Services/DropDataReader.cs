using System.Text;
using Avalonia.Input;
using Avalonia.Platform.Storage;
using LinkDeck.Core.Abstractions;
using LinkDeck.Core.Import;

namespace LinkDeck.Desktop.Services;

/// <summary>Extracts files, browser URLs (with titles where available) and text from a drop.</summary>
public static class DropDataReader
{
    // Windows browser formats: Chromium/Edge provide UniformResourceLocatorW + FileGroupDescriptorW ("Title.url"),
    // Firefox provides text/x-moz-url ("url\ntitle").
    private const string UrlFormat = "UniformResourceLocatorW";
    private const string UrlFormatAnsi = "UniformResourceLocator";
    private const string FileGroupDescriptor = "FileGroupDescriptorW";
    private const string MozUrl = "text/x-moz-url";
    private const string UriList = "text/uri-list";
    private const string ShellIdList = "Shell IDList Array";

    public static bool CanRead(IDataObject data) =>
        data.Contains(DataFormats.Files) || data.Contains(DataFormats.Text)
        || data.Contains(UrlFormat) || data.Contains(UrlFormatAnsi) || data.Contains(MozUrl) || data.Contains(UriList) || data.Contains(ShellIdList);

    public static DropData Read(IDataObject data, IShellIdListResolver? shellItems = null)
    {
        var files = data.GetFiles()?
            .Select(f => f.TryGetLocalPath())
            .Where(p => !string.IsNullOrEmpty(p))
            .Select(p => p!)
            .ToList() ?? [];

        string? url = null, title = null;
        if (files.Count == 0)
        {
            var moz = ReadString(data, MozUrl, Encoding.Unicode);
            if (moz is not null)
            {
                var parts = moz.Split('\n', StringSplitOptions.TrimEntries);
                url = parts.ElementAtOrDefault(0);
                title = parts.ElementAtOrDefault(1);
            }

            url ??= ReadString(data, UrlFormat, Encoding.Unicode)
                ?? ReadString(data, UrlFormatAnsi, Encoding.Latin1)
                ?? ReadString(data, UriList, Encoding.UTF8)?.Split('\n').FirstOrDefault(l => !l.StartsWith('#'))?.Trim();
            title ??= ReadTitleFromFileGroupDescriptor(data);
        }

        IReadOnlyList<ShellItemInfo> shell = [];
        if (files.Count == 0 && shellItems is not null && data.Contains(ShellIdList))
        {
            var bytes = data.Get(ShellIdList) switch
            {
                byte[] b => b,
                MemoryStream ms => ms.ToArray(),
                _ => null,
            };
            if (bytes is not null)
            {
                shell = shellItems.Resolve(bytes);
            }
        }

        return new DropData
        {
            Files = files,
            ShellItems = shell,
            Url = string.IsNullOrWhiteSpace(url) ? null : url.Trim(),
            UrlTitle = string.IsNullOrWhiteSpace(title) ? null : title.Trim(),
            Text = files.Count == 0 ? data.GetText() : null,
        };
    }

    private static string? ReadString(IDataObject data, string format, Encoding encoding)
    {
        if (!data.Contains(format))
        {
            return null;
        }

        return data.Get(format) switch
        {
            string s => s.TrimEnd('\0').Trim(),
            byte[] b => encoding.GetString(b).TrimEnd('\0').Trim(),
            MemoryStream ms => encoding.GetString(ms.ToArray()).TrimEnd('\0').Trim(),
            _ => null,
        };
    }

    /// <summary>FILEGROUPDESCRIPTORW: uint count, then FILEDESCRIPTORW with cFileName (WCHAR[260]) at offset 72.</summary>
    private static string? ReadTitleFromFileGroupDescriptor(IDataObject data)
    {
        if (!data.Contains(FileGroupDescriptor))
        {
            return null;
        }

        var bytes = data.Get(FileGroupDescriptor) switch
        {
            byte[] b => b,
            MemoryStream ms => ms.ToArray(),
            _ => null,
        };
        const int nameOffset = 4 + 72;
        if (bytes is null || bytes.Length < nameOffset + 2)
        {
            return null;
        }

        var name = Encoding.Unicode.GetString(bytes, nameOffset, Math.Min(520, bytes.Length - nameOffset));
        var end = name.IndexOf('\0', StringComparison.Ordinal);
        if (end >= 0)
        {
            name = name[..end];
        }

        return name.EndsWith(".url", StringComparison.OrdinalIgnoreCase) ? name[..^4] : name;
    }
}
