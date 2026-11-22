using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using LinkDeck.Core.Import;
using LinkDeck.Desktop.ViewModels;

namespace LinkDeck.Desktop.Views;

public partial class QuickAddWindow : Window
{
    private readonly TaskCompletionSource<bool> _result = new();

    public QuickAddWindow()
    {
        InitializeComponent();
        AddHandler(KeyDownEvent, OnPreviewKeyDown, RoutingStrategies.Tunnel);
        Opened += (_, _) => TargetBox.Focus();
        AppPicker.PropertyChanged += async (_, e) =>
        {
            if (e.Property == Expander.IsExpandedProperty && AppPicker.IsExpanded && Vm is { } vm)
            {
                await vm.LoadAppsAsync();
            }
        };
        Closed += (_, _) => _result.TrySetResult(false);
    }

    /// <summary>Completes with true when an item was saved, false when cancelled.</summary>
    public Task<bool> Result => _result.Task;

    /// <summary>Turns a browsed path into a draft (resolves shortcuts, reads .rdp/.url). Set by the caller.</summary>
    public DropParser? DropParser { get; set; }

    private QuickAddViewModel? Vm => DataContext as QuickAddViewModel;

    private async void OnBrowseFile(object? sender, RoutedEventArgs e)
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Choose a file, program or shortcut",
            AllowMultiple = false,
        });
        Apply(files.Count > 0 ? files[0].TryGetLocalPath() : null);
    }

    private async void OnBrowseFolder(object? sender, RoutedEventArgs e)
    {
        var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "Choose a folder",
            AllowMultiple = false,
        });
        Apply(folders.Count > 0 ? folders[0].TryGetLocalPath() : null);
    }

    private void Apply(string? path)
    {
        if (string.IsNullOrEmpty(path) || Vm is not { } vm)
        {
            return;
        }

        var draft = (DropParser ?? new DropParser(null)).ParseFile(path);
        if (draft is not null)
        {
            vm.ApplyDraft(draft);
        }

        Activate();
    }

    private void OnPreviewKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            e.Handled = true;
            Cancel();
        }
        else if (e.Key == Key.Enter && e.KeyModifiers == KeyModifiers.None && e.Source is not ComboBox)
        {
            e.Handled = true;
            TrySave();
        }
    }

    private void OnSave(object? sender, RoutedEventArgs e) => TrySave();

    private void OnCancel(object? sender, RoutedEventArgs e) => Cancel();

    private void TrySave()
    {
        if (Vm?.Save() == true)
        {
            _result.TrySetResult(true);
            Close();
        }
    }

    private void Cancel()
    {
        _result.TrySetResult(false);
        Close();
    }
}
