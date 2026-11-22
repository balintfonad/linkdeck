using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;
using LinkDeck.Core.Abstractions;
using LinkDeck.Platform.Windows.Native;

namespace LinkDeck.Platform.Windows;

/// <summary>Resolves .lnk files through the shell's IShellLink COM object.</summary>
public sealed unsafe class ShortcutResolver : IShortcutResolver
{
    private const int MaxPath = 32768;
    private const uint StgmRead = 0x0;
    private static readonly StrategyBasedComWrappers ComWrappers = new();

    public ShortcutInfo? Resolve(string shortcutPath)
    {
        if (!File.Exists(shortcutPath))
        {
            return null;
        }

        try
        {
            var link = CreateShellLink();
            ((IPersistFile)link).Load(Path.GetFullPath(shortcutPath), StgmRead);

            var buffer = new char[MaxPath];
            fixed (char* p = buffer)
            {
                link.GetPath(p, buffer.Length, 0, 0);
                var target = new string(p);
                link.GetArguments(p, buffer.Length);
                var args = new string(p);
                link.GetWorkingDirectory(p, buffer.Length);
                var dir = new string(p);

                return string.IsNullOrWhiteSpace(target)
                    ? null
                    : new ShortcutInfo(target, NullIfEmpty(args), NullIfEmpty(dir));
            }
        }
        catch (COMException)
        {
            return null;
        }
    }

    /// <summary>
    /// True when a .lnk's target or icon location is a network path, or when the link cannot be read. Extracting the
    /// icon of such a link would make Windows connect to that host and send the user's credentials.
    /// </summary>
    internal static bool PointsToNetwork(string shortcutPath)
    {
        try
        {
            var link = CreateShellLink();
            ((IPersistFile)link).Load(Path.GetFullPath(shortcutPath), StgmRead);

            var buffer = new char[MaxPath];
            fixed (char* p = buffer)
            {
                link.GetPath(p, buffer.Length, 0, 0);
                var target = Environment.ExpandEnvironmentVariables(new string(p));
                link.GetIconLocation(p, buffer.Length, out _);
                var icon = Environment.ExpandEnvironmentVariables(new string(p));
                return Core.Config.PathPolicy.IsRemote(target) || Core.Config.PathPolicy.IsRemote(icon);
            }
        }
        catch (COMException)
        {
            return true;
        }
    }

    /// <summary>Creates a .lnk file (used by tests and potential future "export" features).</summary>
    internal static void Create(string shortcutPath, string target, string? arguments, string? workingDirectory, string? iconLocation = null)
    {
        var link = CreateShellLink();
        link.SetPath(target);
        if (iconLocation is not null)
        {
            link.SetIconLocation(iconLocation, 0);
        }

        if (arguments is not null)
        {
            link.SetArguments(arguments);
        }

        if (workingDirectory is not null)
        {
            link.SetWorkingDirectory(workingDirectory);
        }

        ((IPersistFile)link).Save(Path.GetFullPath(shortcutPath), true);
    }

    private static IShellLinkW CreateShellLink()
    {
        // Ensure COM is initialized on this thread; S_FALSE / RPC_E_CHANGED_MODE are fine.
        _ = Ole32.CoInitializeEx(0, Ole32.CoinitApartmentThreaded);

        var iid = typeof(IShellLinkW).GUID;
        var hr = Ole32.CoCreateInstance(Ole32.ClsidShellLink, 0, Ole32.ClsctxInprocServer, iid, out var ptr);
        Marshal.ThrowExceptionForHR(hr);
        try
        {
            return (IShellLinkW)ComWrappers.GetOrCreateObjectForComInstance(ptr, CreateObjectFlags.UniqueInstance);
        }
        finally
        {
            Marshal.Release(ptr);
        }
    }

    private static string? NullIfEmpty(string s) => string.IsNullOrWhiteSpace(s) ? null : s;
}
