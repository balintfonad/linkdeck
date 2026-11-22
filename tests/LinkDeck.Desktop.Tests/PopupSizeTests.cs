using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using LinkDeck.Core.Abstractions;
using LinkDeck.Core.Models;
using LinkDeck.Desktop.ViewModels;
using LinkDeck.Desktop.Views;

namespace LinkDeck.Desktop.Tests;

public class PopupSizeTests : IDisposable
{
    private readonly DeckFixture _f = new(DeckFixture.Sample(SortMode.Manual));

    public void Dispose() => _f.Dispose();

    private void SetSettings(Func<AppSettings, AppSettings> change) =>
        _f.Deck.TrySave(_f.Deck.Config with { Settings = change(_f.Deck.Config.Settings) });

    [Fact]
    public void Columns_follow_the_tile_area_width_and_drive_arrow_keys()
    {
        var vm = new PopupViewModel(_f.Deck, null);

        vm.UpdateColumns(250);
        Assert.Equal(2, vm.Columns);
        vm.UpdateColumns(1220);
        Assert.Equal(10, vm.Columns);

        vm.UpdateColumns(250);
        vm.Move(0, 1); // one row down = two tiles on
        Assert.Equal(2, vm.SelectedIndex);
    }

    [Fact]
    public void Resizing_saves_whole_tiles()
    {
        var vm = new PopupViewModel(_f.Deck, null);

        vm.SaveGrid(9, 4);

        Assert.Equal((9, 4), (_f.Deck.Config.Settings.PopupColumns, _f.Deck.Config.Settings.PopupRows));
        Assert.Equal((9, 4), (vm.PopupColumns, vm.PopupRows));

        vm.SaveGrid(1, 0); // clamped
        Assert.Equal((2, 1), (_f.Deck.Config.Settings.PopupColumns, _f.Deck.Config.Settings.PopupRows));
    }

    [AvaloniaFact]
    public void Tile_area_is_exactly_columns_by_rows()
    {
        SetSettings(s => s with { PopupColumns = 8, PopupRows = 3 });
        var window = new PopupWindow { DataContext = new PopupViewModel(_f.Deck, null) };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        Assert.Equal((8 * PopupViewModel.TileWidth) + 4, window.Scroller.Width);
        Assert.Equal(3 * PopupViewModel.TileHeight, window.Scroller.Height);
        Assert.Equal(SizeToContent.WidthAndHeight, window.SizeToContent);
        window.Close();
    }

    [AvaloniaFact]
    public void Auto_rows_fit_the_content_up_to_four_rows()
    {
        var window = new PopupWindow { DataContext = new PopupViewModel(_f.Deck, null) };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        Assert.True(double.IsNaN(window.Scroller.Height));
        Assert.Equal(4 * PopupViewModel.TileHeight, window.Scroller.MaxHeight);
        window.Close();
    }

    [AvaloniaFact]
    public void Tiles_are_laid_out_from_the_top_left()
    {
        SetSettings(s => s with { PopupColumns = 8, PopupRows = 4 });
        var vm = new PopupViewModel(_f.Deck, null);
        var window = new PopupWindow { DataContext = vm };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        var panel = window.Tiles.ItemsPanelRoot!;
        Assert.IsType<WrapPanel>(panel);
        var first = window.Tiles.ContainerFromIndex(0)!;
        Assert.Equal(0, first.Bounds.X);
        Assert.Equal(0, first.Bounds.Y);
        window.Close();
    }

    [AvaloniaTheory]
    [InlineData(BlurMode.Light, "Transparent", 0)]
    [InlineData(BlurMode.Strong, "AcrylicBlur", 0)]
    [InlineData(BlurMode.None, "Transparent", 8)]
    public void Blur_mode_picks_the_window_effect(BlurMode mode, string first, double margin)
    {
        SetSettings(s => s with { Blur = mode });
        var window = new PopupWindow { DataContext = new PopupViewModel(_f.Deck, null) };

        Assert.Equal(first, window.TransparencyLevelHint[0].ToString());
        Assert.Equal(margin, window.Root.Margin.Left);
    }

    [AvaloniaFact]
    public void Every_edge_and_corner_can_resize()
    {
        var window = new PopupWindow { DataContext = new PopupViewModel(_f.Deck, null) };

        var edges = new Control[] { window.EdgeTop, window.EdgeBottom, window.EdgeLeft, window.EdgeRight, window.EdgeTopLeft, window.EdgeTopRight, window.EdgeBottomLeft, window.ResizeGrip };
        Assert.All(edges, e => Assert.True(Enum.TryParse<WindowEdge>((string)e.Tag!, out _)));
        Assert.True(window.DragHandle.IsVisible);
        Assert.False(window.SizeReadout.IsVisible);
    }

    [Fact]
    public void Settings_save_columns_rows_auto_and_blur()
    {
        var vm = new SettingsViewModel(_f.Deck, new NoRunner());
        Assert.True(vm.AutoHeight);
        Assert.Equal((int)BlurMode.None, vm.BlurIndex);

        vm.PopupColumns = 9;
        vm.AutoHeight = false;
        vm.PopupRows = 4;
        vm.BlurIndex = (int)BlurMode.Strong;
        Assert.True(vm.Save());

        var s = _f.Deck.Config.Settings;
        Assert.Equal((9, 4, BlurMode.Strong), (s.PopupColumns, s.PopupRows, s.Blur));

        vm.AutoHeight = true;
        Assert.True(vm.Save());
        Assert.Equal(0, _f.Deck.Config.Settings.PopupRows);
    }

    private sealed class NoRunner : IProcessRunner
    {
        public void Start(System.Diagnostics.ProcessStartInfo startInfo)
        {
        }
    }
}

public class LightBlurTests : IDisposable
{
    private readonly DeckFixture _f = new(DeckFixture.Sample());

    public void Dispose() => _f.Dispose();

    private sealed class RecordingEffects : IWindowEffects
    {
        public List<bool> Calls { get; } = [];

        public void SetLightBlur(IntPtr windowHandle, bool enabled) => Calls.Add(enabled);
    }

    [AvaloniaFact]
    public void Light_blur_uses_the_os_effect_and_other_modes_turn_it_off()
    {
        _f.Deck.TrySave(_f.Deck.Config with { Settings = _f.Deck.Config.Settings with { Blur = BlurMode.Light } });
        var effects = new RecordingEffects();
        var window = new PopupWindow { DataContext = new PopupViewModel(_f.Deck, null), WindowEffects = effects };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        Assert.True(effects.Calls[^1]); // soft (light) blur chosen

        _f.Deck.TrySave(_f.Deck.Config with { Settings = _f.Deck.Config.Settings with { Blur = BlurMode.Strong } });
        Assert.False(effects.Calls[^1]);
        window.Close();
    }
}

public class ResizeSnapTests
{
    private static readonly Avalonia.Size Chrome = new(50, 120);

    private static Avalonia.Size ClientFor(double columns, double rows) =>
        new(Chrome.Width + 4 + (columns * PopupViewModel.TileWidth), Chrome.Height + (rows * PopupViewModel.TileHeight));

    [Theory]
    [InlineData(6.0, 3.0, 6, 3)]   // exact
    [InlineData(6.6, 3.6, 7, 4)]   // more than half a tile bigger: keep the bigger size (used to spring back)
    [InlineData(6.4, 3.4, 6, 3)]   // less than half: snap back
    [InlineData(6.5, 3.5, 7, 4)]   // exactly half: bigger
    [InlineData(0.5, 0.2, 2, 1)]   // minimums
    public void Snaps_to_the_nearest_whole_tile(double columns, double rows, int expectedColumns, int expectedRows) =>
        Assert.Equal((expectedColumns, expectedRows), PopupWindow.GridFor(ClientFor(columns, rows), Chrome));
}
