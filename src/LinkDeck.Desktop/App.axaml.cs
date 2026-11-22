using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Platform;
using Avalonia.Styling;
using Avalonia.Threading;
using LinkDeck.Core.Config;
using LinkDeck.Core.Icons;
using LinkDeck.Core.Import;
using LinkDeck.Core.Logging;
using LinkDeck.Core.Models;
using LinkDeck.Desktop.Services;
using LinkDeck.Desktop.ViewModels;
using LinkDeck.Desktop.Views;
using LinkDeck.Platform.Windows;

namespace LinkDeck.Desktop;

/// <summary>Composition root: builds services, the hidden popup, the tray icon and the hotkey.</summary>
[System.Diagnostics.CodeAnalysis.SuppressMessage("Reliability", "CA1001", Justification = "Disposed in Shutdown when the application lifetime exits.")]
public partial class App : Application
{
    private ILog _log = NullLog.Instance;
    private ConfigStore? _store;
    private DeckService? _deck;
    private HotkeyService? _hotkey;
    private PopupController? _controller;
    private IconCache? _icons;
    private TrayIcon? _tray;
    private NativeMenuItem? _pauseItem;
    private SettingsWindow? _settings;
    private ProcessRunner _runner = new();
    private Autostart? _autostart;
    private ShortcutResolver _shortcuts = new();
    private readonly ShellIdListResolver _shellItems = new();
    private readonly ForegroundActivator _activator = new();
    private AppSettings? _appliedSettings;

    /// <summary>Set by Program for the single-instance "show" signal.</summary>
    internal static SingleInstance? Instance { get; set; }

    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.ShutdownMode = ShutdownMode.OnExplicitShutdown;
            desktop.Exit += (_, _) => Shutdown();
            Start(desktop);
        }

        base.OnFrameworkInitializationCompleted();
    }

    private void Start(IClassicDesktopStyleApplicationLifetime desktop)
    {
        var paths = AppPaths.Resolve(
            AppContext.BaseDirectory,
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            FolderPermissions.IsPrivateFolder);
        _log = new FileLog(paths.LogsDirectory);
        _log.Info($"LinkDeck starting. Portable: {paths.IsPortable}.");

        var notifier = new ToastNotifier();
        _store = new ConfigStore(paths.ConfigPath);
        var usage = new UsageStore(paths.StatePath);
        var launcher = new WindowsLauncher(_runner);
        launcher.CleanupTemporaryFiles();

        var trust = new TrustStore(paths.TrustPath, new DpapiProtector());
        _deck = new DeckService(_store, usage, launcher, notifier, _log, a => Dispatcher.UIThread.Post(a), trust: trust);
        if (paths.PortableConfigIgnored)
        {
            _deck.Report(new ConfigNotice(NoticeLevel.Warning,
                $"Portable mode is off: other accounts can change files in {AppContext.BaseDirectory}, so its config.json was ignored. "
                + "Move LinkDeck to a folder only you can write to, or remove write access for other users."));
        }

        _deck.Load();
        _deck.ConfigChanged += (_, _) => ApplySettings(_deck.Config.Settings);

        _icons = new IconCache(new ShellIconProvider(), new FaviconFetcher(paths.IconsDirectory), _log);
        var popupVm = new PopupViewModel(_deck, _icons);
        var popup = new PopupWindow { DataContext = popupVm, ShellItems = _shellItems, WindowEffects = new WindowEffects() };

        _hotkey = new HotkeyService(_log, a => Dispatcher.UIThread.Post(a), new KeyStateProbe());
        _controller = new PopupController(popup, popupVm, _deck, _hotkey, _activator, new DropParser(_shortcuts), _icons, _log);
        WindowPlacement.Cursor = () => _activator.GetCursorPosition();
        _controller.AppCatalog = new AppCatalog(_shortcuts);
        _controller.Dialogs = new DialogService(w => { if (w.TryGetPlatformHandle() is { } h) { _activator.Activate(h.Handle); } });
        _controller.EditRequested += (_, item) => OpenSettings(item.Id, fromLauncher: true);
        _controller.SettingsRequested += (_, _) => OpenSettings(fromLauncher: true);
        _controller.Prewarm();

        _autostart = new Autostart(Environment.ProcessPath ?? Path.Combine(AppContext.BaseDirectory, "LinkDeck.exe"));
        ApplySettings(_deck.Config.Settings);
        _hotkey.Start(_deck.Config.Settings.Hotkey);

        CreateTray();
        _store.StartWatching();
        Instance?.Listen(() => Dispatcher.UIThread.Post(() => _controller.ShowInteractive()));

        if (desktop.Args?.Contains("--settings") == true)
        {
            OpenSettings();
        }

        _log.Info("LinkDeck ready.");
    }

    private void ApplySettings(AppSettings settings)
    {
        if (_appliedSettings == settings)
        {
            return;
        }

        RequestedThemeVariant = settings.Theme switch
        {
            ThemeMode.Light => ThemeVariant.Light,
            ThemeMode.Dark => ThemeVariant.Dark,
            _ => ThemeVariant.Default,
        };

        if (_appliedSettings is not null && _appliedSettings.Hotkey != settings.Hotkey)
        {
            _hotkey?.UpdateHotkey(settings.Hotkey);
        }

        if (_deck is { IsReadOnly: false } && _autostart is not null)
        {
            try
            {
                if (_autostart.IsEnabled != settings.StartWithWindows)
                {
                    _autostart.SetEnabled(settings.StartWithWindows);
                }
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException or IOException or System.Security.SecurityException)
            {
                _log.Warn("Could not update the start-with-Windows setting.", ex);
            }
        }

        _appliedSettings = settings;
    }

    private void CreateTray()
    {
        using var iconStream = AssetLoader.Open(new Uri("avares://LinkDeck/Assets/linkdeck.ico"));
        var addItem = new NativeMenuItem("Add item…");
        addItem.Click += async (_, _) => await _controller!.QuickAddAsync([null]);
        var settingsItem = new NativeMenuItem("Settings");
        settingsItem.Click += (_, _) => OpenSettings();
        var openConfig = new NativeMenuItem("Open config file");
        openConfig.Click += (_, _) => _runner.Start(new System.Diagnostics.ProcessStartInfo(_deck!.ConfigPath) { UseShellExecute = true });
        _pauseItem = new NativeMenuItem("Pause hotkey") { ToggleType = MenuItemToggleType.CheckBox };
        _pauseItem.Click += (_, _) => TogglePause();
        var exit = new NativeMenuItem("Exit");
        exit.Click += (_, _) => (ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.Shutdown();

        _tray = new TrayIcon
        {
            Icon = new WindowIcon(iconStream),
            ToolTipText = "LinkDeck",
            Menu = [addItem, settingsItem, openConfig, _pauseItem, new NativeMenuItemSeparator(), exit],
            IsVisible = true,
        };
        _tray.Clicked += (_, _) => _controller?.ShowInteractive();
        TrayIcon.SetIcons(this, [_tray]);
    }

    private void TogglePause()
    {
        if (_hotkey is null || _pauseItem is null)
        {
            return;
        }

        _hotkey.IsPaused = !_hotkey.IsPaused;
        _pauseItem.IsChecked = _hotkey.IsPaused;
        _tray!.ToolTipText = _hotkey.IsPaused ? "LinkDeck (hotkey paused)" : "LinkDeck";
    }

    /// <param name="fromLauncher">Opened from the launcher: closing Settings brings the launcher back.</param>
    private void OpenSettings(string? itemId = null, bool fromLauncher = false)
    {
        if (_deck is null)
        {
            return;
        }

        if (_settings is not null)
        {
            if (itemId is not null)
            {
                (_settings.DataContext as SettingsViewModel)?.SelectItem(itemId);
            }

            _settings.Activate();
            return;
        }

        var vm = new SettingsViewModel(_deck, _runner, _icons);
        if (itemId is not null)
        {
            vm.SelectItem(itemId);
        }

        _settings = new SettingsWindow
        {
            DataContext = vm,
            DropParser = new DropParser(_shortcuts),
            ShellItems = _shellItems,
            Notifier = new ToastNotifier(),
        };
        var returnToLauncher = fromLauncher;
        _settings.ReturnToLauncherRequested += (_, _) => returnToLauncher = true;
        _settings.Closed += (_, _) =>
        {
            _settings = null;
            if (returnToLauncher)
            {
                Dispatcher.UIThread.Post(() => _controller?.ShowInteractive());
            }
        };
        WindowPlacement.CenterOnActiveScreen(_settings);
        _settings.Show();
        _settings.Activate();
    }

    private void Shutdown()
    {
        _log.Info("LinkDeck exiting.");
        _hotkey?.Dispose();
        _store?.Dispose();
        _icons?.Dispose();
        if (_tray is not null)
        {
            _tray.IsVisible = false;
            _tray.Dispose();
        }
    }
}
