using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using LinkDeck.Core.Models;

namespace LinkDeck.Desktop.Views;

/// <summary>The user's choice in <see cref="ColorDialog"/>: a <c>#RRGGBB</c> colour, or null for "no colour".</summary>
public sealed record ColorChoice(string? Hex);

/// <summary>A colour wheel (ring) with hex input. OK returns the colour, "No colour" clears it, Cancel/Esc returns null.</summary>
public partial class ColorDialog : Window
{
    private readonly TaskCompletionSource<ColorChoice?> _result = new();

    public ColorDialog()
    {
        InitializeComponent();
        OkButton.Click += (_, _) => Finish(new ColorChoice(ToHex(Picker.Color)));
        NoColorButton.Click += (_, _) => Finish(new ColorChoice(null));
        CancelButton.Click += (_, _) => Close();
        AddHandler(KeyDownEvent, (_, e) =>
        {
            if (e.Key == Key.Escape)
            {
                e.Handled = true;
                Close();
            }
        }, RoutingStrategies.Tunnel);
        Closed += (_, _) => _result.TrySetResult(null);
    }

    public static Task<ColorChoice?> ShowAsync(string message, string? current, Action<Window>? bringToFront)
    {
        var dialog = new ColorDialog();
        dialog.Message.Text = message;
        dialog.Picker.Color = current is not null && Color.TryParse(current, out var c) ? c : Color.Parse("#3B82F6");
        dialog.Opened += (_, _) => bringToFront?.Invoke(dialog);
        LinkDeck.Desktop.Services.WindowPlacement.CenterOnActiveScreen(dialog);
        dialog.Show();
        return dialog._result.Task;
    }

    internal static string ToHex(Color c) => HexColor.Normalize($"#{c.R:X2}{c.G:X2}{c.B:X2}")!;

    private void Finish(ColorChoice choice)
    {
        _result.TrySetResult(choice);
        Close();
    }
}
