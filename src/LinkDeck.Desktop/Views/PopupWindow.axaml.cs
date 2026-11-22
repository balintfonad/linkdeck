using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using LinkDeck.Core.Abstractions;
using LinkDeck.Core.Import;
using LinkDeck.Core.Models;
using LinkDeck.Desktop.Services;
using LinkDeck.Desktop.ViewModels;

namespace LinkDeck.Desktop.Views;

public partial class PopupWindow : Window
{
    private PopupViewModel? _vm;
    private ContextMenu? _openMenu;
    private bool _dragging;
    private bool _resizing;
    private Size _chrome;
    private (int Columns, int Rows) _liveGrid;
    private WindowEdge _resizeEdge;
    private PixelPoint? _userPosition;

    public PopupWindow()
    {
        InitializeComponent();
        AddHandler(KeyDownEvent, OnPreviewKeyDown, RoutingStrategies.Tunnel);
        AddHandler(DragDrop.DragOverEvent, OnDragOver);
        AddHandler(DragDrop.DropEvent, OnDrop);
        AddHandler(PointerPressedEvent, OnPreviewPointerPressed, RoutingStrategies.Tunnel, handledEventsToo: true);
        AddHandler(ContextRequestedEvent, OnContextRequested);
        PointerPressed += OnBackgroundPointerPressed;
        PositionChanged += (_, e) =>
        {
            if (_dragging)
            {
                _userPosition = e.Point;
            }
        };
        WireMainMenu();
        Opened += (_, _) => ApplyBlur(); // the OS effect needs the native window
        DragHandle.PointerPressed += (_, e) =>
        {
            if (_vm is { IsCycling: false } && e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            {
                e.Handled = true;
                _dragging = true;
                BeginMoveDrag(e);
            }
        };
        foreach (var edge in new[] { EdgeTop, EdgeBottom, EdgeLeft, EdgeRight, EdgeTopLeft, EdgeTopRight, EdgeBottomLeft, ResizeGrip })
        {
            edge.PointerPressed += OnEdgePointerPressed;
        }

        Tiles.SizeChanged += (_, e) => _vm?.UpdateColumns(e.NewSize.Width);
        SizeChanged += (_, _) =>
        {
            if (_resizing)
            {
                UpdateSizeReadout();
            }
        };
        Deactivated += (_, _) => Dispatcher.UIThread.Post(RaiseFocusLostUnlessMenuOpen, DispatcherPriority.Background);
    }

    /// <summary>Esc pressed while the popup has keyboard focus.</summary>
    public event EventHandler? EscapeRequested;

    public event EventHandler? FocusLost;

    /// <summary>The popup was clicked while cycling (acts like Space: keep it open).</summary>
    public event EventHandler? ClickedWhileCycling;

    public event EventHandler<DropData>? DataDropped;

    /// <summary>OS window effects (light blur); set by the app.</summary>
    public IWindowEffects? WindowEffects { get; set; }

    /// <summary>Global cursor position provider (set by the controller) for re-centring.</summary>
    public Func<(int X, int Y)?>? CursorPosition { get; set; }

    /// <summary>Decodes Start-menu and other shell drags (set by the app).</summary>
    public IShellIdListResolver? ShellItems { get; set; }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (_vm is not null)
        {
            _vm.PropertyChanged -= OnVmPropertyChanged;
        }

        _vm = DataContext as PopupViewModel;
        if (_vm is not null)
        {
            _vm.PropertyChanged += OnVmPropertyChanged;
            ApplyGrid();
            ApplyBlur();
        }
    }

    /// <summary>Centres the popup on the screen containing <paramref name="cursor"/> (physical pixels).</summary>
    public void PlaceOnScreen((int X, int Y)? cursor)
    {
        _dragging = false;
        if (_userPosition is { } saved && Screens.All.Any(s => s.WorkingArea.Contains(saved)))
        {
            Position = saved; // the user moved the popup: keep it where they put it
            return;
        }

        var screen = (cursor is { } c ? Screens.ScreenFromPoint(new PixelPoint(c.X, c.Y)) : null)
            ?? Screens.ScreenFromWindow(this)
            ?? Screens.Primary;
        if (screen is null)
        {
            return;
        }

        var area = screen.WorkingArea;
        var scale = screen.Scaling;
        var width = (int)((Bounds.Width > 0 ? Bounds.Width : 780) * scale);
        var height = (int)(Math.Max(Bounds.Height, 200) * scale);
        Position = new PixelPoint(area.X + ((area.Width - width) / 2), area.Y + Math.Max(0, (area.Height - height) / 3));
    }

    public void FocusSearch()
    {
        SearchBox.Focus(NavigationMethod.Pointer);
        SearchBox.CaretIndex = SearchBox.Text?.Length ?? 0;
    }

    private void OnVmPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(PopupViewModel.PopupColumns) or nameof(PopupViewModel.PopupRows))
        {
            ApplyGrid();
        }
        else if (e.PropertyName == nameof(PopupViewModel.Blur))
        {
            ApplyBlur();
        }

        if (e.PropertyName == nameof(PopupViewModel.SelectedIndex) && _vm is not null && _vm.SelectedIndex >= 0)
        {
            var index = _vm.SelectedIndex;
            Dispatcher.UIThread.Post(() => Tiles.ContainerFromIndex(index)?.BringIntoView(), DispatcherPriority.Background);
        }
    }

    private void OnPreviewKeyDown(object? sender, KeyEventArgs e)
    {
        if (_vm is null)
        {
            return;
        }

        var ctrl = e.KeyModifiers.HasFlag(KeyModifiers.Control);
        var shift = e.KeyModifiers.HasFlag(KeyModifiers.Shift);
        switch (e.Key)
        {
            case Key.Escape:
                EscapeRequested?.Invoke(this, EventArgs.Empty);
                break;
            case Key.Enter when shift:
                _vm.CopySelected();
                break;
            case Key.Enter:
                _vm.LaunchSelected();
                break;
            case Key.Left when ctrl && shift:
                _vm.MoveSelected(-1);
                break;
            case Key.Right when ctrl && shift:
                _vm.MoveSelected(+1);
                break;
            case Key.Up when ctrl && shift:
                _vm.MoveSelected(-_vm.Columns);
                break;
            case Key.Down when ctrl && shift:
                _vm.MoveSelected(+_vm.Columns);
                break;
            case Key.Left:
                _vm.Move(-1, 0);
                break;
            case Key.Right:
                _vm.Move(+1, 0);
                break;
            case Key.Up:
                _vm.Move(0, -1);
                break;
            case Key.Down:
                _vm.Move(0, +1);
                break;
            case Key.Tab:
                // Tab / Shift+Tab switch groups (Ctrl+Tab too). Focus stays in the search box.
                _vm.NextTab(shift ? -1 : +1);
                break;
            case Key.Delete when string.IsNullOrEmpty(_vm.SearchText):
                _vm.RemoveSelected();
                break;
            case Key.N when ctrl:
                _vm.QuickAdd();
                break;
            case >= Key.D1 and <= Key.D9 when ctrl:
                _vm.LaunchNth(e.Key - Key.D0);
                break;
            case >= Key.NumPad1 and <= Key.NumPad9 when ctrl:
                _vm.LaunchNth(e.Key - Key.NumPad0);
                break;
            default:
                return;
        }

        e.Handled = true;
    }

    /// <summary>The menu button at the top left: Settings, Keep open, Add item, New group.</summary>
    private void WireMainMenu()
    {
        MenuSettings.Click += (_, _) => _vm?.OpenSettings();
        MenuAddItem.Click += (_, _) => _vm?.QuickAdd();
        MenuNewGroup.Click += (_, _) => _vm?.NewGroup();
        MenuCenter.Click += (_, _) =>
        {
            ResetPosition();
            PlaceOnScreen(CursorPosition?.Invoke());
        };
        MenuKeepOpen.Click += (_, _) =>
        {
            _vm?.ToggleKeepOpen();
            MenuKeepOpen.IsChecked = _vm?.KeepOpen ?? false;
        };
        MenuManualOrder.Click += (_, _) =>
        {
            _vm?.ToggleManualOrder();
            MenuManualOrder.IsChecked = _vm?.IsManualOrder ?? false;
        };
        if (MainMenuButton.Flyout is { } flyout)
        {
            flyout.Opened += (_, _) =>
            {
                MenuKeepOpen.IsChecked = _vm?.KeepOpen ?? false;
                MenuManualOrder.IsChecked = _vm?.IsManualOrder ?? false;
            };
            flyout.Closed += (_, _) => Dispatcher.UIThread.Post(RaiseFocusLostUnlessMenuOpen, DispatcherPriority.Background);
        }

        // Drag a tile onto another one to arrange (manual order).
        Tiles.AddHandler(PointerPressedEvent, OnTilePointerPressed, RoutingStrategies.Tunnel, handledEventsToo: true);
        Tiles.AddHandler(PointerMovedEvent, OnTilePointerMoved, RoutingStrategies.Tunnel, handledEventsToo: true);
        Tiles.AddHandler(PointerReleasedEvent, (_, _) => _tileDragStart = null, RoutingStrategies.Tunnel, handledEventsToo: true);
    }

    private static readonly DataFormat<string> TileFormat = DataFormat.CreateStringApplicationFormat("linkdeck-tile");
    private Point? _tileDragStart;
    private PointerPressedEventArgs? _tilePress;
    private ItemTileViewModel? _tileDragSource;

    private void OnTilePointerPressed(object? sender, PointerPressedEventArgs e)
    {
        _tileDragStart = null;
        if (_vm is not { CanReorder: true } || !e.GetCurrentPoint(Tiles).Properties.IsLeftButtonPressed)
        {
            return;
        }

        _tileDragSource = (e.Source as Control)?.DataContext as ItemTileViewModel;
        _tileDragStart = _tileDragSource is null ? null : e.GetPosition(Tiles);
        _tilePress = _tileDragStart is null ? null : e;
    }

    private async void OnTilePointerMoved(object? sender, PointerEventArgs e)
    {
        if (_tileDragStart is not { } start || _tileDragSource is null || _tilePress is not { } press)
        {
            return;
        }

        var delta = e.GetPosition(Tiles) - start;
        if (Math.Abs(delta.X) + Math.Abs(delta.Y) < 5)
        {
            return;
        }

        _tileDragStart = null;
        var source = _tileDragSource;
        source.IsDragging = true; // picked up: faded with a dashed outline
        var data = new DataTransfer();
        data.Add(DataTransferItem.Create(TileFormat, source.Item.Id));
        try
        {
            await DragDrop.DoDragDropAsync(press, data, DragDropEffects.Move);
        }
        finally
        {
            source.IsDragging = false;
            ClearDropMarkers();
        }
    }

    /// <summary>The tile under the pointer and whether the dragged tile would land after it (right half) or before it.</summary>
    private static (ItemTileViewModel Tile, bool After)? DropTargetAt(DragEventArgs e)
    {
        var button = (e.Source as Visual)?.FindAncestorOfType<Button>(includeSelf: true);
        return button?.DataContext is ItemTileViewModel tile
            ? (tile, e.GetPosition(button).X > button.Bounds.Width / 2)
            : null;
    }

    /// <summary>Shows the insertion bar on the side of the target tile where the dragged tile would land.</summary>
    private void ShowDropMarker(DragEventArgs e, string draggedId)
    {
        ClearDropMarkers();
        if (DropTargetAt(e) is { } target && target.Tile.Item.Id != draggedId)
        {
            target.Tile.DropBefore = !target.After;
            target.Tile.DropAfter = target.After;
        }
    }

    private void ClearDropMarkers()
    {
        foreach (var tile in _vm?.Items ?? [])
        {
            tile.DropBefore = false;
            tile.DropAfter = false;
        }
    }

    /// <summary>A tile dropped on a tile: before it when dropped on its left half, after it on its right half.</summary>
    private bool TryDropTile(DragEventArgs e)
    {
        if (_vm is null || e.DataTransfer.TryGetValue(TileFormat) is not { } id)
        {
            return false;
        }

        ClearDropMarkers();
        if (DropTargetAt(e) is { } target && _vm.Items.FirstOrDefault(t => t.Item.Id == id) is { } moved)
        {
            _vm.Reorder(moved.Item, target.Tile.Item, target.After);
        }

        return true;
    }

    /// <summary>Right-click menus, built on demand so "Move to" always lists the current groups.</summary>
    private void OnContextRequested(object? sender, ContextRequestedEventArgs e)
    {
        if (_vm is null || _vm.IsCycling || e.Source is not Control source)
        {
            return;
        }

        var items = source.DataContext switch
        {
            ItemTileViewModel tile => TileMenu(tile),
            TabViewModel tab => TabMenu(tab),
            _ => null,
        };
        if (items is null)
        {
            return;
        }

        e.Handled = true;
        var menu = new ContextMenu { ItemsSource = items };
        menu.Closed += (_, _) =>
        {
            _openMenu = null;
            Dispatcher.UIThread.Post(RaiseFocusLostUnlessMenuOpen, DispatcherPriority.Background);
        };
        _openMenu = menu;
        menu.Open(source);
    }

    private List<Control> TileMenu(ItemTileViewModel tile)
    {
        var vm = _vm!;
        var move = new MenuItem { Header = "Move to" };
        var targets = vm.MoveTargetsFor(tile.Item);
        move.ItemsSource = targets.Count == 0
            ? new List<Control> { new MenuItem { Header = "(no other group — use + Group)", IsEnabled = false } }
            : targets.Select(g => (Control)Item(g.Name, () => vm.MoveItem(tile.Item, g.Id))).ToList();

        var copy = new MenuItem { Header = "Copy to" };
        copy.ItemsSource = vm.CopyTargets.Select(g => (Control)Item(g.Name, () => vm.CopyItemTo(tile.Item, g.Id))).ToList();

        return
        [
            Item("Open", () => tile.OpenCommand.Execute(null)),
            Item("Copy address / path", () => tile.CopyCommand.Execute(null)),
            Item("Edit…", () => tile.EditCommand.Execute(null)),
            Item("Color…", () => tile.PickColorCommand.Execute(null)),
            move,
            copy,
            new Separator(),
            Item("Remove", () => tile.RemoveCommand.Execute(null)),
        ];
    }

    private List<Control> TabMenu(TabViewModel tab)
    {
        var vm = _vm!;
        var items = new List<Control> { Item("New group…", vm.NewGroup) };
        if (tab.GroupId is { } id)
        {
            items.Add(Item("Rename group…", () => vm.RenameGroup(id)));
            items.Add(Item("Delete group…", () => vm.DeleteGroup(id)));
        }

        return items;
    }

    private static MenuItem Item(string header, Action action)
    {
        var item = new MenuItem { Header = header };
        item.Click += (_, _) => action();
        return item;
    }

    /// <summary>Dragging an empty part of the popup (frame, tab row, footer) moves the window.</summary>
    private void OnBackgroundPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (_vm is { IsCycling: true } || !e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            return;
        }

        var source = e.Source as Visual;
        if (source is null || source.FindAncestorOfType<Button>(includeSelf: true) is not null
            || source.FindAncestorOfType<TextBox>(includeSelf: true) is not null
            || source.FindAncestorOfType<Avalonia.Controls.Primitives.TabStripItem>(includeSelf: true) is not null
            || source.FindAncestorOfType<Avalonia.Controls.Primitives.ScrollBar>(includeSelf: true) is not null)
        {
            return;
        }

        _dragging = true;
        BeginMoveDrag(e);
    }

    /// <summary>
    /// Sizes the tile area to whole tiles: columns × rows from the settings (rows 0 = fit the content,
    /// scrolling after 4 rows). The window then sizes itself around it, like a terminal around its character grid.
    /// </summary>
    private void ApplyGrid()
    {
        if (_vm is null)
        {
            return;
        }

        Scroller.Width = (_vm.PopupColumns * PopupViewModel.TileWidth) + 4;
        if (_vm.PopupRows > 0)
        {
            Scroller.Height = _vm.PopupRows * PopupViewModel.TileHeight;
            Scroller.MaxHeight = double.PositiveInfinity;
        }
        else
        {
            Scroller.Height = double.NaN;
            Scroller.MaxHeight = 4 * PopupViewModel.TileHeight;
        }

        SizeToContent = SizeToContent.WidthAndHeight;
    }

    /// <summary>Starts an edge/corner resize; when the user lets go, the size snaps to whole tiles and is saved.</summary>
    private void OnEdgePointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (_vm is null || _vm.IsCycling || sender is not Control { Tag: string tag }
            || !Enum.TryParse<WindowEdge>(tag, out var edge) || !e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            return;
        }

        e.Handled = true;

        // Everything around the tile area is "chrome"; whatever is left while dragging becomes tiles.
        _chrome = new Size(ClientSize.Width - Scroller.Bounds.Width, ClientSize.Height - Scroller.Bounds.Height);
        _liveGrid = (_vm.PopupColumns, Math.Max(1, (int)Math.Round(Scroller.Bounds.Height / PopupViewModel.TileHeight)));
        SizeToContent = SizeToContent.Manual;
        Scroller.Width = double.NaN;
        Scroller.Height = double.NaN;
        Scroller.MaxHeight = double.PositiveInfinity;
        _resizing = true;
        UpdateSizeReadout();
        SizeReadout.IsVisible = true;

        _resizeEdge = edge;
        // On Windows this runs the system sizing loop and returns when the mouse button is released.
        BeginResizeDrag(edge, e);
        FinishResize();
    }

    private void UpdateSizeReadout()
    {
        _liveGrid = GridFor(ClientSize, _chrome);
        SizeReadoutText.Text = $"{_liveGrid.Columns} × {_liveGrid.Rows}";
    }

    /// <summary>
    /// Whole tiles that fit a window of <paramref name="client"/> size around <paramref name="chrome"/>, rounded to the
    /// nearest tile (not down): past half a tile you get the bigger size, so enlarging never springs back.
    /// </summary>
    internal static (int Columns, int Rows) GridFor(Size client, Size chrome)
    {
        var columns = (int)Math.Round((client.Width - chrome.Width - 4) / PopupViewModel.TileWidth, MidpointRounding.AwayFromZero);
        var rows = (int)Math.Round((client.Height - chrome.Height) / PopupViewModel.TileHeight, MidpointRounding.AwayFromZero);
        return (Math.Clamp(columns, AppSettings.MinPopupColumns, AppSettings.MaxPopupColumns), Math.Clamp(rows, 1, AppSettings.MaxPopupRows));
    }

    /// <summary>Snaps to the grid shown in the readout and saves it.</summary>
    internal void FinishResize()
    {
        if (!_resizing)
        {
            return;
        }

        _resizing = false;
        SizeReadout.IsVisible = false;
        // Keep the edge opposite to the one dragged in place while snapping (dragging the left edge must not move the right one).
        var before = new PixelRect(Position, PixelSize.FromSize(ClientSize, RenderScaling));
        _vm?.SaveGrid(_liveGrid.Columns, _liveGrid.Rows);
        ApplyGrid(); // snap even when the saved grid did not change
        Dispatcher.UIThread.Post(() => KeepOppositeEdge(before, _resizeEdge), DispatcherPriority.Loaded);
    }

    private void KeepOppositeEdge(PixelRect before, WindowEdge edge)
    {
        var after = PixelSize.FromSize(ClientSize, RenderScaling);
        var x = edge is WindowEdge.West or WindowEdge.NorthWest or WindowEdge.SouthWest ? before.Right - after.Width : Position.X;
        var y = edge is WindowEdge.North or WindowEdge.NorthWest or WindowEdge.NorthEast ? before.Bottom - after.Height : Position.Y;
        if (x != Position.X || y != Position.Y)
        {
            Position = new PixelPoint(x, y);
        }
    }

    /// <summary>Light = Windows blur, Strong = acrylic; both fall back to plain transparency.</summary>
    private void ApplyBlur()
    {
        var mode = _vm?.Blur ?? BlurMode.None;
        TransparencyLevelHint = mode switch
        {
            BlurMode.Strong => [WindowTransparencyLevel.AcrylicBlur, WindowTransparencyLevel.Blur, WindowTransparencyLevel.Transparent],
            // Light: plain transparency here, plus the OS blur-behind below (the toolkit's "blur" does nothing on Windows 11).
            BlurMode.Light => [WindowTransparencyLevel.Transparent],
            _ => [WindowTransparencyLevel.Transparent],
        };

        // A blurred window is rectangular: let the surface fill it instead of floating inside a transparent margin.
        Root.Margin = mode == BlurMode.None ? new Thickness(8) : new Thickness(0);

        if (WindowEffects is not null && TryGetPlatformHandle() is { } handle)
        {
            WindowEffects.SetLightBlur(handle.Handle, mode == BlurMode.Light);
        }
    }

    /// <summary>Forgets a position the user dragged the popup to; it is centred again next time.</summary>
    public void ResetPosition() => _userPosition = null;

    private void OnPreviewPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (_vm is { IsCycling: true })
        {
            e.Handled = true; // do not launch the tile under the pointer
            ClickedWhileCycling?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>Opening a tile's context menu briefly deactivates the window on Windows; that must not close the popup.</summary>
    private void RaiseFocusLostUnlessMenuOpen()
    {
        if (IsActive || _openMenu is not null || MainMenuButton.Flyout is { IsOpen: true })
        {
            return;
        }

        FocusLost?.Invoke(this, EventArgs.Empty);
    }

    private void OnDragOver(object? sender, DragEventArgs e)
    {
        if (e.DataTransfer.Contains(TileFormat))
        {
            e.DragEffects = DragDropEffects.Move;
            ShowDropMarker(e, e.DataTransfer.TryGetValue(TileFormat) ?? "");
        }
        else
        {
            e.DragEffects = DropDataReader.CanRead(e.DataTransfer) ? DragDropEffects.Copy | DragDropEffects.Link : DragDropEffects.None;
        }

        e.Handled = true;
    }

    private void OnDrop(object? sender, DragEventArgs e)
    {
        e.Handled = true;
        if (TryDropTile(e))
        {
            return;
        }

        DataDropped?.Invoke(this, DropDataReader.Read(e.DataTransfer, ShellItems));
    }
}
