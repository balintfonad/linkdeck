using Avalonia;
using Avalonia.Controls;

namespace LinkDeck.Desktop.Services;

/// <summary>
/// Opens LinkDeck's windows (Settings, quick-add, dialogs) on the monitor the user is working on —
/// the one with the mouse cursor, which is also where the launcher opened — instead of the primary monitor.
/// </summary>
public static class WindowPlacement
{
    /// <summary>The global cursor position in physical pixels (set by the app; null in tests).</summary>
    public static Func<(int X, int Y)?>? Cursor { get; set; }

    /// <summary>Call before <c>Show()</c>: centres now (estimated size) and again once the real size is known.</summary>
    public static void CenterOnActiveScreen(Window window)
    {
        if (Cursor is null)
        {
            return;
        }

        window.WindowStartupLocation = WindowStartupLocation.Manual;
        Center(window, double.IsNaN(window.Width) ? 460 : window.Width, double.IsNaN(window.Height) ? 420 : window.Height);
        window.Opened += (_, _) => Center(window, window.Bounds.Width, window.Bounds.Height);
    }

    private static void Center(Window window, double width, double height)
    {
        if (Cursor?.Invoke() is not { } cursor)
        {
            return;
        }

        var screen = window.Screens.ScreenFromPoint(new PixelPoint(cursor.X, cursor.Y)) ?? window.Screens.Primary;
        if (screen is null)
        {
            return;
        }

        var area = screen.WorkingArea;
        var w = (int)(width * screen.Scaling);
        var h = (int)(height * screen.Scaling);
        window.Position = new PixelPoint(
            area.X + Math.Max(0, (area.Width - w) / 2),
            area.Y + Math.Max(0, (area.Height - h) / 2));
    }
}
