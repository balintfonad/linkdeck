using System.Runtime.InteropServices;
using LinkDeck.Core.Abstractions;
using LinkDeck.Core.Popup;

namespace LinkDeck.Platform.Windows;

/// <summary>Physical key state via <c>GetAsyncKeyState</c>. Only modifier keys are queried.</summary>
public sealed partial class KeyStateProbe : IKeyStateProbe
{
    public bool IsDown(GestureKey key)
    {
        int vk = key switch
        {
            GestureKey.LeftCtrl => 0xA2,
            GestureKey.RightCtrl => 0xA3,
            GestureKey.LeftAlt => 0xA4,
            GestureKey.RightAlt => 0xA5,
            GestureKey.LeftShift => 0xA0,
            GestureKey.RightShift => 0xA1,
            GestureKey.LeftWin => 0x5B,
            GestureKey.RightWin => 0x5C,
            _ => 0,
        };
        return vk != 0 && (GetAsyncKeyState(vk) & 0x8000) != 0;
    }

    [LibraryImport("user32.dll")]
    private static partial short GetAsyncKeyState(int vKey);
}
