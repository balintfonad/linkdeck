using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;

namespace LinkDeck.Platform.Windows.Native;

// Source-generated COM interop (ComWrappers): works under NativeAOT, unlike built-in [ComImport].

[GeneratedComInterface(StringMarshalling = StringMarshalling.Utf16)]
[Guid("000214F9-0000-0000-C000-000000000046")]
internal unsafe partial interface IShellLinkW
{
    void GetPath(char* pszFile, int cch, nint pfd, uint fFlags);

    void GetIDList(out nint ppidl);

    void SetIDList(nint pidl);

    void GetDescription(char* pszName, int cch);

    void SetDescription(string pszName);

    void GetWorkingDirectory(char* pszDir, int cch);

    void SetWorkingDirectory(string pszDir);

    void GetArguments(char* pszArgs, int cch);

    void SetArguments(string pszArgs);

    void GetHotkey(out ushort pwHotkey);

    void SetHotkey(ushort wHotkey);

    void GetShowCmd(out int piShowCmd);

    void SetShowCmd(int iShowCmd);

    void GetIconLocation(char* pszIconPath, int cch, out int piIcon);

    void SetIconLocation(string pszIconPath, int iIcon);

    void SetRelativePath(string pszPathRel, uint dwReserved);

    void Resolve(nint hwnd, uint fFlags);

    void SetPath(string pszFile);
}

[GeneratedComInterface(StringMarshalling = StringMarshalling.Utf16)]
[Guid("0000010b-0000-0000-C000-000000000046")]
internal partial interface IPersistFile
{
    // IPersist
    void GetClassID(out Guid pClassID);

    // IPersistFile
    [PreserveSig]
    int IsDirty();

    void Load(string pszFileName, uint dwMode);

    void Save(string? pszFileName, [MarshalAs(UnmanagedType.Bool)] bool fRemember);

    void SaveCompleted(string pszFileName);

    void GetCurFile(out nint ppszFileName);
}

internal static partial class Ole32
{
    public const uint ClsctxInprocServer = 0x1;
    public const uint CoinitApartmentThreaded = 0x2;

    public static readonly Guid ClsidShellLink = new("00021401-0000-0000-C000-000000000046");

    [LibraryImport("ole32.dll")]
    public static partial int CoCreateInstance(in Guid rclsid, nint pUnkOuter, uint dwClsContext, in Guid riid, out nint ppv);

    [LibraryImport("ole32.dll")]
    public static partial int CoInitializeEx(nint pvReserved, uint dwCoInit);
}
