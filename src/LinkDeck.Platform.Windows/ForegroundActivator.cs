using LinkDeck.Core.Abstractions;
using LinkDeck.Platform.Windows.Native;

namespace LinkDeck.Platform.Windows;

/// <summary>
/// Windows refuses <c>SetForegroundWindow</c> from background processes. Attaching to the
/// foreground thread's input queue for the duration of the call lets the popup take focus.
/// </summary>
public sealed class ForegroundActivator : IForegroundActivator
{
    public void Activate(IntPtr windowHandle)
    {
        if (windowHandle == 0)
        {
            return;
        }

        if (Win32.IsIconic(windowHandle))
        {
            Win32.ShowWindow(windowHandle, Win32.SwRestore);
        }

        var foreground = Win32.GetForegroundWindow();
        if (foreground == windowHandle)
        {
            return;
        }

        var foregroundThread = foreground == 0 ? 0 : Win32.GetWindowThreadProcessId(foreground, 0);
        var currentThread = Win32.GetCurrentThreadId();
        var attached = foregroundThread != 0 && foregroundThread != currentThread
            && Win32.AttachThreadInput(currentThread, foregroundThread, true);
        try
        {
            Win32.BringWindowToTop(windowHandle);
            Win32.SetForegroundWindow(windowHandle);
            Win32.SetFocus(windowHandle);
        }
        finally
        {
            if (attached)
            {
                Win32.AttachThreadInput(currentThread, foregroundThread, false);
            }
        }
    }

    /// <summary>
    /// Lets another process take the foreground. A newly started second instance owns foreground rights;
    /// it calls this before signalling the running instance so that one can show its popup in front.
    /// </summary>
    public static void AllowAnyProcessToActivate() => Win32.AllowSetForegroundWindow(-1);

    public (int X, int Y)? GetCursorPosition() =>
        Win32.GetCursorPos(out var p) ? (p.X, p.Y) : null;
}
