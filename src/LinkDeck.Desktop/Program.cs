using Avalonia;
using LinkDeck.Desktop.Services;
using Velopack;

namespace LinkDeck.Desktop;

internal static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        // Velopack install/update hooks must run first; it may exit the process.
        VelopackApp.Build().Run();

        using var instance = new SingleInstance("LinkDeck");
        if (!instance.IsFirstInstance)
        {
            LinkDeck.Platform.Windows.ForegroundActivator.AllowAnyProcessToActivate();
            instance.SignalFirstInstance();
            return 0;
        }

        App.Instance = instance;
        return BuildAvaloniaApp().StartWithClassicDesktopLifetime(args, Avalonia.Controls.ShutdownMode.OnExplicitShutdown);
    }

    // Used by the visual designer too.
    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .LogToTrace();
}
