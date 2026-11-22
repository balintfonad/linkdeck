using LinkDeck.Core.Abstractions;
using Microsoft.Win32;

namespace LinkDeck.Platform.Windows;

/// <summary>Start with Windows via <c>HKCU\Software\Microsoft\Windows\CurrentVersion\Run</c>.</summary>
public sealed class Autostart : IAutostart
{
    public const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";

    private readonly string _valueName;
    private readonly string _command;
    private readonly string _keyPath;

    public Autostart(string executablePath, string valueName = "LinkDeck", string keyPath = RunKeyPath)
    {
        _valueName = valueName;
        _command = $"\"{executablePath}\" --autostart";
        _keyPath = keyPath;
    }

    public bool IsEnabled
    {
        get
        {
            using var key = Registry.CurrentUser.OpenSubKey(_keyPath, writable: false);
            return key?.GetValue(_valueName) is string value
                && string.Equals(value, _command, StringComparison.OrdinalIgnoreCase);
        }
    }

    public void SetEnabled(bool enabled)
    {
        using var key = Registry.CurrentUser.CreateSubKey(_keyPath, writable: true);
        if (enabled)
        {
            key.SetValue(_valueName, _command, RegistryValueKind.String);
        }
        else if (key.GetValue(_valueName) is not null)
        {
            key.DeleteValue(_valueName, throwOnMissingValue: false);
        }
    }
}
