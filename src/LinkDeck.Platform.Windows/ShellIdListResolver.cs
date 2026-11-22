using System.Runtime.InteropServices;
using LinkDeck.Core.Abstractions;
using LinkDeck.Platform.Windows.Native;

namespace LinkDeck.Platform.Windows;

/// <summary>
/// Decodes the "Shell IDList Array" (CIDA) drag format. This is how the Start menu, the taskbar's jump lists and
/// virtual folders hand over items that have no file: Store apps and Start-menu entries become
/// <c>shell:AppsFolder\&lt;id&gt;</c> targets, which Explorer can launch.
/// </summary>
public sealed class ShellIdListResolver : IShellIdListResolver
{
    /// <summary>The Applications folder (shell:AppsFolder) CLSID, as it appears in parsing names.</summary>
    private const string AppsFolderPrefix = "::{4234D49B-0245-4DF3-B780-3893943456E1}\\";

    public IReadOnlyList<ShellItemInfo> Resolve(byte[] data)
    {
        // CIDA: UINT cidl; UINT aoffset[cidl + 1]; offset 0 = parent folder, 1..cidl = children (relative).
        if (data.Length < 8)
        {
            return [];
        }

        var count = BitConverter.ToUInt32(data, 0);
        if (count == 0 || count > 1000 || data.Length < 4 + (4 * (count + 1)))
        {
            return [];
        }

        var result = new List<ShellItemInfo>();
        var handle = GCHandle.Alloc(data, GCHandleType.Pinned);
        try
        {
            var basePtr = handle.AddrOfPinnedObject();
            var parentOffset = BitConverter.ToUInt32(data, 4);
            if (parentOffset >= data.Length)
            {
                return [];
            }

            for (var i = 1; i <= count; i++)
            {
                var offset = BitConverter.ToUInt32(data, 4 + (4 * i));
                if (offset >= data.Length)
                {
                    continue;
                }

                var pidl = Shell32.ILCombine(basePtr + (nint)parentOffset, basePtr + (nint)offset);
                if (pidl == 0)
                {
                    continue;
                }

                try
                {
                    var item = Describe(pidl);
                    if (item is not null)
                    {
                        result.Add(item);
                    }
                }
                finally
                {
                    Shell32.ILFree(pidl);
                }
            }
        }
        finally
        {
            handle.Free();
        }

        return result;
    }

    private static ShellItemInfo? Describe(nint pidl)
    {
        var display = Shell32.GetName(pidl, Shell32.SigdnNormalDisplay) ?? "";
        var fsPath = Shell32.GetName(pidl, Shell32.SigdnFileSysPath);
        if (!string.IsNullOrEmpty(fsPath))
        {
            return new ShellItemInfo(fsPath, display);
        }

        var parsing = Shell32.GetName(pidl, Shell32.SigdnDesktopAbsoluteParsing);
        var target = ToShellTarget(parsing);
        return target is null ? null : new ShellItemInfo(target, display);
    }

    /// <summary>Converts a desktop-absolute parsing name into a launchable <c>shell:</c> target.</summary>
    internal static string? ToShellTarget(string? parsing)
    {
        if (string.IsNullOrWhiteSpace(parsing))
        {
            return null;
        }

        if (parsing.StartsWith(AppsFolderPrefix, StringComparison.OrdinalIgnoreCase))
        {
            return "shell:AppsFolder\\" + parsing[AppsFolderPrefix.Length..];
        }

        return parsing.StartsWith("::", StringComparison.Ordinal) ? "shell:" + parsing : null;
    }
}
