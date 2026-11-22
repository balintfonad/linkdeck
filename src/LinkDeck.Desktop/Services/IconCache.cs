using System.Collections.Concurrent;
using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using LinkDeck.Core.Abstractions;
using LinkDeck.Core.Config;
using LinkDeck.Core.Icons;
using LinkDeck.Core.Logging;
using LinkDeck.Core.Models;

namespace LinkDeck.Desktop.Services;

/// <summary>Resolves tile icons: explicit icon file → favicon cache (url) → shell icon (path). Null means "use the type glyph".</summary>
public interface IIconSource
{
    Task<IImage?> GetIconAsync(Item item);

    /// <summary>Fetches the favicon for a newly added url item (the only network access).</summary>
    Task RefreshFaviconAsync(UrlItem item);
}

public sealed class IconCache : IIconSource, IDisposable
{
    private readonly IShellIconProvider? _shell;
    private readonly FaviconFetcher? _favicons;
    private readonly ILog _log;
    private readonly ConcurrentDictionary<string, Task<IImage?>> _cache = new(StringComparer.OrdinalIgnoreCase);

    public IconCache(IShellIconProvider? shell, FaviconFetcher? favicons, ILog log)
    {
        _shell = shell;
        _favicons = favicons;
        _log = log;
    }

    public Task<IImage?> GetIconAsync(Item item)
    {
        var key = KeyFor(item);
        return key is null ? Task.FromResult<IImage?>(null) : _cache.GetOrAdd(key, _ => Task.Run(() => Load(item)));
    }

    public async Task RefreshFaviconAsync(UrlItem item)
    {
        if (_favicons is null)
        {
            return;
        }

        var path = await _favicons.FetchAsync(item.Url).ConfigureAwait(false);
        _log.Info(path is null ? "Favicon not available for a new web item." : "Favicon cached for a new web item.");
        var key = KeyFor(item);
        if (key is not null)
        {
            _cache.TryRemove(key, out _);
        }
    }

    public void Dispose() => _favicons?.Dispose();

    private string? KeyFor(Item item) => item switch
    {
        { Icon: { Length: > 0 } icon } => "file:" + icon,
        UrlItem u when _favicons is not null => "fav:" + _favicons.CachePathFor(u.Url),
        PathItem p => "shell:" + p.Path,
        _ => null,
    };

    private IImage? Load(Item item)
    {
        try
        {
            if (!string.IsNullOrWhiteSpace(item.Icon))
            {
                // Never touch a network path just to draw a tile: Windows would send the user's credentials to that host.
                var path = Environment.ExpandEnvironmentVariables(item.Icon);
                if (!PathPolicy.IsRemote(path) && File.Exists(path))
                {
                    return new Bitmap(path);
                }
            }

            switch (item)
            {
                case UrlItem u when _favicons?.TryGetCached(u.Url) is { } cached:
                    return new Bitmap(cached);
                case PathItem p when _shell?.GetIcon(p.Path) is { } icon:
                    return ToBitmap(icon);
            }
        }
        catch (Exception ex)
        {
            _log.Warn($"Could not load icon for item {item.Id}.", ex);
        }

        return null;
    }

    private static WriteableBitmap ToBitmap(IconImage icon)
    {
        var bmp = new WriteableBitmap(new PixelSize(icon.Width, icon.Height), new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Unpremul);
        using var fb = bmp.Lock();
        for (var y = 0; y < icon.Height; y++)
        {
            System.Runtime.InteropServices.Marshal.Copy(icon.Bgra, y * icon.Width * 4, fb.Address + (y * fb.RowBytes), icon.Width * 4);
        }

        return bmp;
    }
}
