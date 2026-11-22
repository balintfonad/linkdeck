using System.Runtime.InteropServices;
using LinkDeck.Core.Abstractions;

namespace LinkDeck.Platform.Windows;

/// <summary>
/// Light blur-behind via <c>SetWindowCompositionAttribute</c> (ACCENT_ENABLE_BLURBEHIND). Unlike acrylic it only
/// softens what is behind the window, and unlike the toolkit's "blur" level it also works on Windows 11.
/// </summary>
public sealed partial class WindowEffects : IWindowEffects
{
    private const int WcaAccentPolicy = 19;
    private const int AccentDisabled = 0;
    private const int AccentEnableBlurBehind = 3;

    [StructLayout(LayoutKind.Sequential)]
    private struct AccentPolicy
    {
        public int AccentState;
        public int AccentFlags;
        public uint GradientColor;
        public int AnimationId;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WindowCompositionAttributeData
    {
        public int Attribute;
        public nint Data;
        public int SizeOfData;
    }

    public void SetLightBlur(IntPtr windowHandle, bool enabled)
    {
        if (windowHandle == 0)
        {
            return;
        }

        var policy = new AccentPolicy { AccentState = enabled ? AccentEnableBlurBehind : AccentDisabled };
        var size = Marshal.SizeOf<AccentPolicy>();
        var ptr = Marshal.AllocHGlobal(size);
        try
        {
            Marshal.StructureToPtr(policy, ptr, false);
            var data = new WindowCompositionAttributeData { Attribute = WcaAccentPolicy, Data = ptr, SizeOfData = size };
            _ = SetWindowCompositionAttribute(windowHandle, ref data);
        }
        finally
        {
            Marshal.FreeHGlobal(ptr);
        }
    }

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetWindowCompositionAttribute(nint hwnd, ref WindowCompositionAttributeData data);
}
