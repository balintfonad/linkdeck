using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using LinkDeck.Core.Abstractions;
using LinkDeck.Desktop.ViewModels;
using LinkDeck.Desktop.Views;

namespace LinkDeck.Desktop.Tests;

public class SettingsBackTests : IDisposable
{
    private readonly DeckFixture _f = new(DeckFixture.Sample());

    public void Dispose() => _f.Dispose();

    private (SettingsWindow Window, SettingsViewModel Vm, Func<bool> Returned, Func<bool> Closed) Open()
    {
        var vm = new SettingsViewModel(_f.Deck, new NoRunner());
        var window = new SettingsWindow { DataContext = vm };
        var returned = false;
        var closed = false;
        window.ReturnToLauncherRequested += (_, _) => returned = true;
        window.Closed += (_, _) => closed = true;
        window.Show();
        Dispatcher.UIThread.RunJobs();
        return (window, vm, () => returned, () => closed);
    }

    [AvaloniaFact]
    public void Back_saves_changes_closes_and_returns_to_the_launcher()
    {
        var (window, vm, returned, closed) = Open();
        vm.SelectedItem!.Name = "Jira Cloud";

        window.GoBack();
        Dispatcher.UIThread.RunJobs();

        Assert.True(closed());
        Assert.True(returned());
        Assert.Equal("Jira Cloud", _f.Deck.Config.AllItems.First().Name);
    }

    [AvaloniaFact]
    public void Back_with_invalid_input_stays_open_and_explains()
    {
        var (window, vm, returned, closed) = Open();
        vm.SelectedItem!.Target = "";

        window.GoBack();
        Dispatcher.UIThread.RunJobs();

        Assert.False(closed());
        Assert.False(returned());
        Assert.True(vm.HasStatus);
        window.AskSaveChanges = () => Task.FromResult(false);
        window.Close();
    }

    [AvaloniaFact]
    public void Escape_goes_back()
    {
        var (window, _, returned, closed) = Open();

        window.KeyPressQwerty(PhysicalKey.Escape, RawInputModifiers.None);
        Dispatcher.UIThread.RunJobs();

        Assert.True(closed());
        Assert.True(returned());
    }

    [AvaloniaFact]
    public void Closing_without_changes_does_not_ask_and_does_not_return()
    {
        var (window, _, returned, closed) = Open();

        window.Close();
        Dispatcher.UIThread.RunJobs();

        Assert.True(closed());
        Assert.False(returned());
    }

    [AvaloniaTheory]
    [InlineData(true, "Changed")]
    [InlineData(false, "Jira")]
    public void Closing_with_unsaved_changes_asks_save_or_dont_save(bool save, string expectedName)
    {
        var (window, vm, _, closed) = Open();
        var asked = 0;
        window.AskSaveChanges = () =>
        {
            asked++;
            return Task.FromResult(save);
        };
        vm.SelectedItem!.Name = "Changed";

        window.Close();
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(1, asked);
        Assert.True(closed());
        Assert.Equal(expectedName, _f.Deck.Config.AllItems.First().Name);
    }

    private sealed class NoRunner : IProcessRunner
    {
        public void Start(System.Diagnostics.ProcessStartInfo startInfo)
        {
        }
    }
}

public class SettingsLayoutTests : IDisposable
{
    private readonly DeckFixture _f = new(DeckFixture.Sample());

    public void Dispose() => _f.Dispose();

    [AvaloniaFact]
    public void General_tab_scrolls_so_nothing_is_cut_off()
    {
        var window = new SettingsWindow { DataContext = new SettingsViewModel(_f.Deck, new NoRunnerForLayout()) };

        var general = Avalonia.LogicalTree.LogicalExtensions.GetLogicalDescendants(window)
            .OfType<Avalonia.Controls.TabItem>().Single(t => (string?)t.Header == "General");

        Assert.IsType<Avalonia.Controls.ScrollViewer>(general.Content);
    }

    private sealed class NoRunnerForLayout : IProcessRunner
    {
        public void Start(System.Diagnostics.ProcessStartInfo startInfo)
        {
        }
    }
}
