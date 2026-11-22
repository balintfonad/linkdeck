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
    private static readonly DataFormat<byte[]> UrlFormat = DataFormat.CreateBytesPlatformFormat("UniformResourceLocatorW");
    private static readonly DataFormat<byte[]> UrlFormatAnsi = DataFormat.CreateBytesPlatformFormat("UniformResourceLocator");
    private static readonly DataFormat<byte[]> FileGroupDescriptor = DataFormat.CreateBytesPlatformFormat("FileGroupDescriptorW");
    private static readonly DataFormat<byte[]> MozUrl = DataFormat.CreateBytesPlatformFormat("text/x-moz-url");
    private static readonly DataFormat<byte[]> UriList = DataFormat.CreateBytesPlatformFormat("text/uri-list");
    private static readonly DataFormat<byte[]> ShellIdList = DataFormat.CreateBytesPlatformFormat("Shell IDList Array");

    public static bool CanRead(IDataTransfer data) =>
        data.Contains(DataFormat.File) || data.Contains(DataFormat.Text)
        || data.Contains(UrlFormat) || data.Contains(UrlFormatAnsi) || data.Contains(MozUrl) || data.Contains(UriList) || data.Contains(ShellIdList);

    public static DropData Read(IDataTransfer data, IShellIdListResolver? shellItems = null)
    {
        var files = data.TryGetFiles()?
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
            var bytes = data.TryGetValue(ShellIdList);
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
            Text = files.Count == 0 ? data.TryGetText() : null,
        };
    }

    private static string? ReadString(IDataTransfer data, DataFormat<byte[]> format, Encoding encoding) =>
        data.Contains(format) && data.TryGetValue(format) is { } bytes
            ? encoding.GetString(bytes).TrimEnd('\0').Trim()
            : null;

    /// <summary>FILEGROUPDESCRIPTORW: uint count, then FILEDESCRIPTORW with cFileName (WCHAR[260]) at offset 72.</summary>
    private static string? ReadTitleFromFileGroupDescriptor(IDataTransfer data)
    {
        var bytes = data.Contains(FileGroupDescriptor) ? data.TryGetValue(FileGroupDescriptor) : null;
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
