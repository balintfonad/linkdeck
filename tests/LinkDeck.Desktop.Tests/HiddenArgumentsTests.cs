using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using LinkDeck.Core.Import;
using LinkDeck.Core.Models;
using LinkDeck.Desktop.ViewModels;
using LinkDeck.Desktop.Views;

namespace LinkDeck.Desktop.Tests;

/// <summary>Arguments imported from a dropped shortcut must be visible before saving and wherever the item is shown.</summary>
public class HiddenArgumentsTests : IDisposable
{
    private readonly DeckFixture _f = new(DeckFixture.Sample());

    public void Dispose() => _f.Dispose();

    private static ItemDraft ShortcutDraft() => new()
    {
        Type = ItemType.Path,
        Name = "Chrome",
        Target = @"C:\Program Files\Google\Chrome\Application\chrome.exe",
        Args = @"--load-extension=C:\Users\me\Downloads\ext",
        WorkingDir = @"C:\Users\me\Downloads",
    };

    [Fact]
    public void Quick_add_exposes_and_edits_arguments_from_a_dropped_shortcut()
    {
        var vm = new QuickAddViewModel(_f.Deck, ShortcutDraft());

        Assert.True(vm.IsPath);
        Assert.Equal(@"--load-extension=C:\Users\me\Downloads\ext", vm.Args);

        vm.Args = "";
        vm.WorkingDir = "";
        Assert.True(vm.Save());

        var saved = Assert.IsType<PathItem>(vm.SavedItem);
        Assert.Null(saved.Args);
        Assert.Null(saved.WorkingDir);
    }

    [AvaloniaFact]
    public void Quick_add_window_shows_the_arguments_and_working_directory()
    {
        var window = new QuickAddWindow { DataContext = new QuickAddViewModel(_f.Deck, ShortcutDraft()) };
        window.Show();

        var boxes = window.GetVisualDescendants().OfType<TextBox>().ToList();
        var args = boxes.Single(b => AutomationProperties.GetName(b) == "Arguments");
        var dir = boxes.Single(b => AutomationProperties.GetName(b) == "Working directory");
        Assert.True(args.IsEffectivelyVisible);
        Assert.Equal(@"--load-extension=C:\Users\me\Downloads\ext", args.Text);
        Assert.True(dir.IsEffectivelyVisible);
        window.Close();
    }

    [Fact]
    public void Footer_and_tile_show_the_arguments_too()
    {
        var item = new PathItem { Id = "c", Name = "Chrome", Path = @"C:\Apps\chrome.exe", Args = "--load-extension=x" };

        Assert.Equal(@"C:\Apps\chrome.exe --load-extension=x", item.DisplayTarget);
        Assert.Equal(@"C:\Apps\chrome.exe", item.Target); // copy (Shift+Enter) still copies just the path
        Assert.Equal(item.DisplayTarget, new ItemTileViewModel(item, null).Target);
    }
}
