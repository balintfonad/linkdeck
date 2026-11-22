using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;
using LinkDeck.Core.Config;
using LinkDeck.Desktop.Services;

namespace LinkDeck.Desktop.Views;

/// <summary>A small non-activating notification at the bottom-right of the primary screen.</summary>
public partial class ToastWindow : Window
{
    public ToastWindow()
    {
        InitializeComponent();
        PointerPressed += (_, _) => Hide();
    }

    public void ShowMessage(NoticeLevel level, string message)
    {
        Message.Text = message;
        Accent.Background = level switch
        {
            NoticeLevel.Error => new SolidColorBrush(Color.Parse("#DC2626")),
            NoticeLevel.Warning => new SolidColorBrush(Color.Parse("#D97706")),
            _ => new SolidColorBrush(Color.Parse("#3B82F6")),
        };
        if (!IsVisible)
        {
            Show();
        }

        Dispatcher.UIThread.Post(PlaceBottomRight, DispatcherPriority.Loaded);
    }

    private void PlaceBottomRight()
    {
        var screen = Screens.Primary;
        if (screen is null)
        {
            return;
        }

        var area = screen.WorkingArea;
        var scale = screen.Scaling;
        Position = new PixelPoint(
            area.Right - (int)(Bounds.Width * scale) - 12,
            area.Bottom - (int)(Bounds.Height * scale) - 12);
    }
}

/// <summary><see cref="INotifier"/> that shows one toast at a time; each new message replaces the last.</summary>
public sealed class ToastNotifier : INotifier
{
    private static readonly TimeSpan Duration = TimeSpan.FromSeconds(6);
    private ToastWindow? _window;
    private DispatcherTimer? _timer;

    public void Show(NoticeLevel level, string message) =>
        Dispatcher.UIThread.Post(() =>
        {
            _window ??= new ToastWindow();
            _window.ShowMessage(level, message);
            _timer?.Stop();
            _timer = new DispatcherTimer { Interval = level == NoticeLevel.Error ? Duration * 1.5 : Duration };
            _timer.Tick += (_, _) =>
            {
                _timer?.Stop();
                _window?.Hide();
            };
            _timer.Start();
        });
}
