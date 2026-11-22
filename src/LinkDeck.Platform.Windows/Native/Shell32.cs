using System.Runtime.InteropServices;

namespace LinkDeck.Platform.Windows.Native;

internal static partial class Shell32
{
    public const uint SigdnNormalDisplay = 0x00000000;
    public const uint SigdnDesktopAbsoluteParsing = 0x80028000;
    public const uint SigdnFileSysPath = 0x80058000;
    public const uint ShgfiPidl = 0x8;

    [LibraryImport("shell32.dll")]
    public static partial nint ILCombine(nint pidl1, nint pidl2);

    [LibraryImport("shell32.dll")]
    public static partial void ILFree(nint pidl);

    [LibraryImport("shell32.dll")]
    public static partial int SHGetNameFromIDList(nint pidl, uint sigdnName, out nint ppszName);

    [LibraryImport("shell32.dll", StringMarshalling = StringMarshalling.Utf16)]
    public static partial int SHParseDisplayName(string pszName, nint pbc, out nint ppidl, uint sfgaoIn, out uint psfgaoOut);

    [LibraryImport("shell32.dll", EntryPoint = "SHGetFileInfoW")]
    public static partial nint SHGetFileInfoPidl(nint pidl, uint dwFileAttributes, ref Win32.ShFileInfoW psfi, uint cbFileInfo, uint uFlags);

    public static string? GetName(nint pidl, uint sigdn)
    {
        if (SHGetNameFromIDList(pidl, sigdn, out var ptr) != 0 || ptr == 0)
        {
            return null;
        }

        try
        {
            return Marshal.PtrToStringUni(ptr);
        }
        finally
        {
            Marshal.FreeCoTaskMem(ptr);
        }
    }
}

internal static partial class Processes
{
    public const uint ProcessQueryLimitedInformation = 0x1000;
    public const int GwOwner = 4;
    public const int GwlExStyle = -20;
    public const long WsExToolWindow = 0x80;
    public const int DwmwaCloaked = 14;

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static unsafe partial bool EnumWindows(delegate* unmanaged<nint, nint, int> lpEnumFunc, nint lParam);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static unsafe partial bool EnumChildWindows(nint hWndParent, delegate* unmanaged<nint, nint, int> lpEnumFunc, nint lParam);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool IsWindowVisible(nint hWnd);

    [LibraryImport("user32.dll")]
    public static partial nint GetWindow(nint hWnd, int uCmd);

    [LibraryImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    public static partial nint GetWindowLongPtr(nint hWnd, int nIndex);

    [LibraryImport("user32.dll", EntryPoint = "GetWindowTextW", StringMarshalling = StringMarshalling.Utf16)]
    public static unsafe partial int GetWindowText(nint hWnd, char* lpString, int nMaxCount);

    [LibraryImport("user32.dll")]
    public static partial uint GetWindowThreadProcessId(nint hWnd, out uint lpdwProcessId);

    [LibraryImport("dwmapi.dll")]
    public static partial int DwmGetWindowAttribute(nint hwnd, int dwAttribute, out int pvAttribute, int cbAttribute);

    [LibraryImport("kernel32.dll")]
    public static partial nint OpenProcess(uint dwDesiredAccess, [MarshalAs(UnmanagedType.Bool)] bool bInheritHandle, uint dwProcessId);

    [LibraryImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool CloseHandle(nint hObject);

    [LibraryImport("kernel32.dll", EntryPoint = "QueryFullProcessImageNameW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static unsafe partial bool QueryFullProcessImageName(nint hProcess, uint dwFlags, char* lpExeName, ref uint lpdwSize);

    [LibraryImport("kernel32.dll")]
    public static unsafe partial int GetApplicationUserModelId(nint hProcess, ref uint applicationUserModelIdLength, char* applicationUserModelId);
}
