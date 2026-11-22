using System.Runtime.InteropServices;
using LinkDeck.Core.Abstractions;
using LinkDeck.Core.Config;
using LinkDeck.Platform.Windows.Native;

namespace LinkDeck.Platform.Windows;

/// <summary>Reads the system shell icon (offline) for files, folders and executables as BGRA pixels.</summary>
public sealed unsafe class ShellIconProvider : IShellIconProvider, IDisposable
{
    private const uint ShgfiUseFileAttributes = 0x10;
    private const uint FileAttributeDirectory = 0x10;
    private const uint FileAttributeNormal = 0x80;

    // The shell's icon APIs need COM initialized and are not reliable when called concurrently
    // from thread-pool (MTA) threads, so every request runs on one dedicated STA thread.
    // Process-wide: concurrent calls from different threads fail intermittently, even on separate STA threads.
    private static readonly System.Collections.Concurrent.BlockingCollection<Action> Queue = [];
    private static readonly Thread Worker = StartWorker();

    private static Thread StartWorker()
    {
        var thread = new Thread(() =>
        {
            _ = Ole32.CoInitializeEx(0, Ole32.CoinitApartmentThreaded);
            foreach (var work in Queue.GetConsumingEnumerable())
            {
                work();
            }
        })
        {
            IsBackground = true,
            Name = "LinkDeck shell icons",
        };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        return thread;
    }

    public IconImage? GetIcon(string path)
    {
        if (Thread.CurrentThread == Worker)
        {
            return GetIconCore(path);
        }

        var tcs = new TaskCompletionSource<IconImage?>(TaskCreationOptions.RunContinuationsAsynchronously);
        try
        {
            Queue.Add(() =>
            {
                try
                {
                    tcs.SetResult(GetIconCore(path));
                }
                catch (Exception ex)
                {
                    tcs.SetException(ex);
                }
            });
        }
        catch (InvalidOperationException)
        {
            return null;
        }

        return tcs.Task.GetAwaiter().GetResult();
    }

    /// <summary>The worker is shared and lives for the whole process; nothing to release per instance.</summary>
    public void Dispose()
    {
    }

    private static IconImage? GetIconCore(string path)
    {
        var expanded = Environment.ExpandEnvironmentVariables(path.Trim()).Trim('"');
        var info = default(Win32.ShFileInfoW);
        nint result;
        if (LinkDeck.Core.Models.ItemCategories.IsShellTarget(expanded))
        {
            // shell:AppsFolder… and other virtual items: resolve to a PIDL first.
            if (Shell32.SHParseDisplayName(expanded, 0, out var pidl, 0, out _) != 0 || pidl == 0)
            {
                return null;
            }

            try
            {
                result = Shell32.SHGetFileInfoPidl(pidl, 0, ref info, (uint)Marshal.SizeOf<Win32.ShFileInfoW>(), Win32.ShgfiIcon | Win32.ShgfiLargeIcon | Shell32.ShgfiPidl);
            }
            finally
            {
                Shell32.ILFree(pidl);
            }
        }
        else if (MustNotOpen(expanded))
        {
            // Network path, or a link whose icon or target is on the network: the generic icon for the type,
            // without opening anything (SHGFI_USEFILEATTRIBUTES), so no connection and no credentials are sent.
            var attributes = Path.HasExtension(expanded) ? FileAttributeNormal : FileAttributeDirectory;
            result = Win32.SHGetFileInfo(expanded, attributes, ref info, (uint)Marshal.SizeOf<Win32.ShFileInfoW>(), Win32.ShgfiIcon | Win32.ShgfiLargeIcon | ShgfiUseFileAttributes);
        }
        else if (File.Exists(expanded) || Directory.Exists(expanded))
        {
            result = Win32.SHGetFileInfo(expanded, 0, ref info, (uint)Marshal.SizeOf<Win32.ShFileInfoW>(), Win32.ShgfiIcon | Win32.ShgfiLargeIcon);
        }
        else
        {
            return null;
        }

        if (result == 0 || info.HIcon == 0)
        {
            return null;
        }

        try
        {
            return ToImage(info.HIcon);
        }
        finally
        {
            Win32.DestroyIcon(info.HIcon);
        }
    }

    internal static bool MustNotOpen(string path) =>
        PathPolicy.IsRemote(path)
        || PathPolicy.IsBlockedLinkFile(path)
        || (Path.GetExtension(path).Equals(".lnk", StringComparison.OrdinalIgnoreCase) && File.Exists(path) && ShortcutResolver.PointsToNetwork(path));

    private static IconImage? ToImage(nint hIcon)
    {
        if (!Win32.GetIconInfo(hIcon, out var iconInfo))
        {
            return null;
        }

        var dc = Win32.CreateCompatibleDC(0);
        try
        {
            if (iconInfo.HbmColor == 0 || Win32.GetObject(iconInfo.HbmColor, Marshal.SizeOf<Win32.Bitmap>(), out var bmp) == 0)
            {
                return null;
            }

            int width = bmp.BmWidth, height = bmp.BmHeight;
            var pixels = new byte[width * height * 4];
            var header = new Win32.BitmapInfoHeader
            {
                BiSize = (uint)Marshal.SizeOf<Win32.BitmapInfoHeader>(),
                BiWidth = width,
                BiHeight = -height, // top-down
                BiPlanes = 1,
                BiBitCount = 32,
            };
            fixed (byte* p = pixels)
            {
                if (Win32.GetDIBits(dc, iconInfo.HbmColor, 0, (uint)height, p, ref header, Win32.DibRgbColors) == 0)
                {
                    return null;
                }
            }

            ApplyMaskIfNoAlpha(dc, iconInfo.HbmMask, pixels, width, height);
            return new IconImage(width, height, pixels);
        }
        finally
        {
            Win32.DeleteDC(dc);
            if (iconInfo.HbmColor != 0)
            {
                Win32.DeleteObject(iconInfo.HbmColor);
            }

            if (iconInfo.HbmMask != 0)
            {
                Win32.DeleteObject(iconInfo.HbmMask);
            }
        }
    }

    /// <summary>Legacy icons carry no alpha channel; derive it from the AND mask instead.</summary>
    private static void ApplyMaskIfNoAlpha(nint dc, nint hbmMask, byte[] pixels, int width, int height)
    {
        for (var i = 3; i < pixels.Length; i += 4)
        {
            if (pixels[i] != 0)
            {
                return;
            }
        }

        var mask = new byte[width * height * 4];
        var header = new Win32.BitmapInfoHeader
        {
            BiSize = (uint)Marshal.SizeOf<Win32.BitmapInfoHeader>(),
            BiWidth = width,
            BiHeight = -height,
            BiPlanes = 1,
            BiBitCount = 32,
        };
        var ok = false;
        if (hbmMask != 0)
        {
            fixed (byte* p = mask)
            {
                ok = Win32.GetDIBits(dc, hbmMask, 0, (uint)height, p, ref header, Win32.DibRgbColors) != 0;
            }
        }

        for (var i = 0; i < pixels.Length; i += 4)
        {
            // Mask white = transparent.
            pixels[i + 3] = ok && mask[i] != 0 ? (byte)0 : (byte)255;
        }
    }
}
