using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace LinkDeck.Desktop.Views;

/// <summary>A tiny dialog: a message, an optional text box and OK/Cancel. Enter confirms, Esc cancels.</summary>
public partial class PromptWindow : Window
{
    private readonly TaskCompletionSource<string?> _result = new();

    public PromptWindow()
    {
        InitializeComponent();
        OkButton.Click += (_, _) => Confirm();
        CancelButton.Click += (_, _) => Close();
        AddHandler(KeyDownEvent, OnPreviewKeyDown, RoutingStrategies.Tunnel);
        Closed += (_, _) => _result.TrySetResult(null);
    }

    /// <summary>Returns the entered text (empty string for a confirmation), or null when cancelled.</summary>
    public static Task<string?> ShowAsync(string title, string message, string initial, bool input, string confirmText, Action<Window>? bringToFront, string cancelText = "Cancel")
    {
        var window = new PromptWindow { Title = title };
        window.Message.Text = message;
        window.Input.IsVisible = input;
        window.Input.Text = initial;
        window.OkButton.Content = confirmText;
        window.CancelButton.Content = cancelText;
        window.Opened += (_, _) =>
        {
            bringToFront?.Invoke(window);
            if (input)
            {
                window.Input.Focus();
                window.Input.SelectAll();
            }
            else
            {
                window.OkButton.Focus();
            }
        };
        LinkDeck.Desktop.Services.WindowPlacement.CenterOnActiveScreen(window);
        window.Show();
        return window._result.Task;
    }

    private void OnPreviewKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            e.Handled = true;
            Close();
        }
        else if (e.Key == Key.Enter)
        {
            e.Handled = true;
            Confirm();
        }
    }

    private void Confirm()
    {
        var text = Input.IsVisible ? (Input.Text ?? "").Trim() : "";
        if (Input.IsVisible && text.Length == 0)
        {
            return;
        }

        _result.TrySetResult(text);
        Close();
    }
}
