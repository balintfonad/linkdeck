using System.Diagnostics;
using LinkDeck.Core.Models;

namespace LinkDeck.Core.Abstractions;

/// <summary>Result of a launch attempt. Usage is recorded only when <see cref="Success"/> is true.</summary>
public sealed record LaunchResult(bool Success, string? Error = null)
{
    public static LaunchResult Ok { get; } = new(true);

    public static LaunchResult Fail(string error) => new(false, error);
}

/// <summary>Opens an item with the platform's facilities.</summary>
public interface ILauncher
{
    LaunchResult Launch(Item item);

    /// <summary>Removes leftovers (e.g. temporary .rdp files) from earlier runs.</summary>
    void CleanupTemporaryFiles();
}

/// <summary>Every process start goes through here so tests can assert exact commands.</summary>
public interface IProcessRunner
{
    void Start(ProcessStartInfo startInfo);
}

/// <summary>Protects small local data so that only this user on this computer can read it (DPAPI on Windows).</summary>
public interface IDataProtector
{
    byte[] Protect(byte[] data);

    /// <summary>Returns null when the data was not produced by <see cref="Protect"/> for this user (edited, or copied from elsewhere).</summary>
    byte[]? Unprotect(byte[] data);
}

/// <summary>Start-with-OS registration.</summary>
public interface IAutostart
{
    bool IsEnabled { get; }

    void SetEnabled(bool enabled);
}

public sealed record ShortcutInfo(string TargetPath, string? Arguments, string? WorkingDirectory);

/// <summary>Resolves OS shortcut files (.lnk on Windows).</summary>
public interface IShortcutResolver
{
    ShortcutInfo? Resolve(string shortcutPath);
}

/// <summary>Raw BGRA pixels (straight alpha), top-down rows.</summary>
public sealed record IconImage(int Width, int Height, byte[] Bgra);

/// <summary>System shell icons for files, folders and executables.</summary>
public interface IShellIconProvider
{
    IconImage? GetIcon(string path);
}

/// <summary>Brings a native window to the foreground despite OS focus-stealing protection.</summary>
public interface IForegroundActivator
{
    void Activate(IntPtr windowHandle);

    /// <summary>The global mouse cursor position in physical pixels, if available.</summary>
    (int X, int Y)? GetCursorPosition();
}

/// <summary>Reads the physical up/down state of a key right now (not from the event stream).</summary>
public interface IKeyStateProbe
{
    bool IsDown(LinkDeck.Core.Popup.GestureKey key);
}

/// <summary>
/// Global listener for the hold-and-step gesture (default: hold Alt, step with Ctrl).
/// It reacts only to the gesture keys, and to Space, Esc and Shift while the gesture is held; it never
/// records or logs any keystroke. All events are raised on the UI thread.
/// </summary>
public interface IHotkeyService : IDisposable
{
    /// <summary>Hold and step keys pressed together.</summary>
    event EventHandler? GestureStarted;

    /// <summary>Step key tapped again while holding. Argument: true = backwards (Shift held).</summary>
    event EventHandler<bool>? Stepped;

    /// <summary>Space pressed while holding.</summary>
    event EventHandler? SpacePressed;

    /// <summary>Esc pressed while holding.</summary>
    event EventHandler? EscapePressed;

    /// <summary>Tab pressed while holding (swallowed so Alt+Tab does not open). Argument: true = Shift+Tab.</summary>
    event EventHandler<bool>? TabPressed;

    /// <summary>The hold key was released.</summary>
    event EventHandler? HoldReleased;

    /// <summary>The gesture was abandoned: another key was pressed or the hold key was lost.</summary>
    event EventHandler? Cancelled;

    bool IsPaused { get; set; }

    void Start(Hotkey hotkey);

    void UpdateHotkey(Hotkey hotkey);
}

/// <summary>An app the user can pick in the quick-add dialog.</summary>
/// <param name="Target">Executable path, shortcut path or <c>shell:AppsFolder\…</c> target.</param>
/// <param name="IsRunning">True for apps with an open window right now.</param>
public sealed record AppCandidate(string Name, string Target, string? Args, string? WorkingDir, bool IsRunning);

/// <summary>Lists running and installed apps (Start menu) for the quick-add picker.</summary>
public interface IAppCatalog
{
    IReadOnlyList<AppCandidate> GetRunningApps();

    IReadOnlyList<AppCandidate> GetInstalledApps();
}

/// <summary>A shell item from a drag (e.g. a Start-menu app), which may have no file-system path.</summary>
/// <param name="Target">A file-system path, or a shell target such as <c>shell:AppsFolder\…</c>.</param>
public sealed record ShellItemInfo(string Target, string DisplayName);

/// <summary>Decodes the Windows "Shell IDList Array" drag format.</summary>
public interface IShellIdListResolver
{
    IReadOnlyList<ShellItemInfo> Resolve(byte[] data);
}

/// <summary>OS window effects that the UI toolkit does not provide everywhere.</summary>
public interface IWindowEffects
{
    /// <summary>Turns a light blur of what is behind the window on or off.</summary>
    void SetLightBlur(IntPtr windowHandle, bool enabled);
}
