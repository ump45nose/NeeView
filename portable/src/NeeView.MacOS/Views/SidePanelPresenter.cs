using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.VisualTree;
using NeeView.MacOS.ViewModels;
using NeeView.Runtime.LayoutPanel;
using NeeView.Windows;
namespace NeeView.MacOS.Views;

/// <summary>原侧栏停靠的 Avalonia 表现适配；只操作原布局数据，不依赖阅读或文件后端。</summary>
public sealed class SidePanelPresenter : IDisposable
{
    private readonly Grid _root;
    private readonly Window _window;
    private readonly Canvas _preview;
    private readonly ReaderWorkspaceViewModel _model;
    private readonly Dictionary<string, Control> _contents;
    private readonly Dictionary<string, Grid> _hosts;
    private readonly Dictionary<string, StackPanel> _rails;
    private readonly Dictionary<Control, string> _dragSources = [];
    private readonly Dictionary<string, Border> _containers = [];
    private readonly List<Grid> _contentGrids = [];
    private readonly List<(Grid Grid, LayoutPanelCollection Group)> _groups = [];
    private string _signature = "";
    private string? _pressedPanel;
    private Point _pressedPoint;
    private bool _dragging;
    private bool _pressedSelect;
    private IPointer? _pointer;
    private sealed record Target(string Side, int Index, string? Panel, PanelDock Edge);
    private Target? _target;
    private readonly Dictionary<string, FloatingPanelWindow> _windows = [];
    private Control? _dragRoot;
    private bool _disposing, _preparing, _refreshing;
    public IReadOnlyCollection<FloatingPanelWindow> FloatingWindows => _windows.Values;
    /// <summary>浮窗输入进入同一命令路由，由实际窗口焦点确定作用域。</summary>
    public event EventHandler<KeyEventArgs>? FloatingKeyDown;

    /// <summary>接收唯一窗口的布局插槽及已定义内容；控件内容只创建一次。</summary>
    public SidePanelPresenter(Window window, ReaderWorkspaceViewModel model)
    {
        _window = window; _model = model; _root = window.FindControl<Grid>("SidePanelFrame")!;
        _preview = window.FindControl<Canvas>("PanelDropPreview")!;
        _hosts = new() { ["Left"] = window.FindControl<Grid>("LeftDockHost")!, ["Right"] = window.FindControl<Grid>("RightDockHost")! };
        _rails = new() { ["Left"] = window.FindControl<StackPanel>("LeftRailItems")!, ["Right"] = window.FindControl<StackPanel>("RightRailItems")! };
        var store = window.FindControl<Grid>("PanelContentStore")!;
        _contents = store.Children.Cast<Control>().ToDictionary(c => (string)c.Tag!, c => c);
        foreach (var content in _contents.Values) content.DataContext = model;
        store.Children.Clear();
        AttachDrag(_root);
        _window.Opened += OwnerOpened;
        Refresh();
    }

    /// <summary>读取原组顺序与方向生成控件；悬停刷新不重建布局。</summary>
    public void Refresh()
    {
        if (_disposing || _refreshing) return;
        var signature = string.Join('|', _model.Layout.Docks.Select(d => d.Key + ":" + string.Join(';', d.Value.Items.Select(g => g.Orientation + ":" + string.Join(',', g.Select(p => p.Key)))) + ":" + d.Value.SelectedItem?.FirstOrDefault()?.Key)) + "|Windows:" + string.Join(',', _model.Layout.Windows);
        if (_signature == signature) return; _signature = signature;
        _refreshing = true;
        try
        {
            // 先解除旧父容器，跨栏重排复用同一内容控件及选择状态。
            foreach (var grid in _contentGrids) grid.Children.Clear(); _contentGrids.Clear();
            foreach (var host in _hosts.Values) { host.Children.Clear(); host.RowDefinitions.Clear(); host.ColumnDefinitions.Clear(); }
            foreach (var rail in _rails.Values) rail.Children.Clear();
            _dragSources.Clear(); _containers.Clear(); _groups.Clear();
            foreach (var key in _windows.Keys.Where(k => !_model.Layout.Windows.Contains(k)).ToArray()) CloseWindowHost(key);
            foreach (var (side, dock) in _model.Layout.Docks)
            {
                foreach (var group in dock.Items)
                {
                    var key = group[0].Key;
                    var button = new Button { Content = Icon(key), Tag = key, Classes = { "panel-icon" } };
                    ToolTip.SetTip(button, string.Join(" / ", group.Select(p => Title(p.Key))) + "（拖动可重排或组合）");
                    button.ContextMenu = CreatePanelMenu(key);
                    _dragSources[button] = key; _rails[side].Children.Add(button);
                }
                if (dock.SelectedItem is not { } selected || selected.Any(p => _model.Layout.Windows.Contains(p.Key))) continue;
                var host = _hosts[side]; var horizontal = selected.Orientation == PanelOrientation.Horizontal;
                for (int i = 0; i < selected.Count; i++)
                {
                    var panel = selected[i]; int slot = i * 2;
                    if (horizontal) host.ColumnDefinitions.Add(new(panel.Weight, GridUnitType.Star));
                    else host.RowDefinitions.Add(new(panel.Weight, GridUnitType.Star));
                    var header = new Border { Child = new TextBlock { Text = Title(panel.Key), Classes = { "panel-title" } }, Background = Brushes.Transparent, Cursor = new Cursor(StandardCursorType.SizeAll) };
                    ToolTip.SetTip(header, "拖动标题可组合面板；拖到图标栏可拆出或跨栏移动");
                    header.ContextMenu = CreatePanelMenu(panel.Key, true);
                    _dragSources[header] = panel.Key;
                    var body = new Grid { RowDefinitions = new("Auto,*") };
                    body.Children.Add(header); var content = _contents[panel.Key]; Grid.SetRow(content, 1); body.Children.Add(content); _contentGrids.Add(body);
                    var border = new Border { Child = body, ClipToBounds = true, MinHeight = horizontal ? 0 : 64, MinWidth = horizontal ? 64 : 0 };
                    _containers[panel.Key] = border;
                    if (horizontal) Grid.SetColumn(border, slot); else Grid.SetRow(border, slot); host.Children.Add(border);
                    if (i == selected.Count - 1) continue;
                    if (horizontal) host.ColumnDefinitions.Add(new(new GridLength(4))); else host.RowDefinitions.Add(new(new GridLength(4)));
                    var splitter = new GridSplitter { ResizeDirection = horizontal ? GridResizeDirection.Columns : GridResizeDirection.Rows, ResizeBehavior = GridResizeBehavior.PreviousAndNext };
                    if (horizontal) Grid.SetColumn(splitter, slot + 1); else Grid.SetRow(splitter, slot + 1);
                    splitter.PointerReleased += (_, _) => SaveWeights(); host.Children.Add(splitter);
                }
                _groups.Add((host, selected));
            }
            foreach (var key in _model.Layout.Windows)
            {
                if (!_windows.TryGetValue(key, out var floating))
                {
                    floating = new(key, Title(key)) { DataContext = _model };
                    floating.ContentHost.Child = _contents[key];
                    _windows.Add(key, floating); AttachDrag(floating.DragSurface);
                    floating.Closed += FloatingClosed; floating.Closing += FloatingClosing; floating.PositionChanged += FloatingPositionChanged;
                    floating.SizeChanged += FloatingSizeChanged;
                    floating.PropertyChanged += FloatingPropertyChanged;
                    floating.AddHandler(InputElement.KeyDownEvent, FloatKeyDown, RoutingStrategies.Tunnel);
                }
                floating.PanelHeader.ContextMenu = CreatePanelMenu(key);
                _dragSources[floating.PanelHeader] = key;
                if (_window.IsVisible && !floating.IsVisible)
                { floating.RestorePlacement(_model.Layout.Panels[key].WindowPlacement, _window); floating.Show(_window); Snap(floating); }
            }
        }
        finally { _refreshing = false; }
        foreach (var floating in _windows.Values) Snap(floating);
    }

    /// <summary>原标题浮动取当前容器屏幕位置加32像素，图标浮动沿用保存位置。</summary>
    public void OpenWindow(string key, bool fromContainer = false)
    {
        if (_preparing || _disposing || !_model.Layout.Panels.ContainsKey(key)) return;
        SaveWeights(); WindowPlacement? placement = null;
        if (fromContainer && _containers.TryGetValue(key, out var container))
        {
            var point = container.PointToScreen(default); var scale = _window.RenderScaling;
            placement = new(WindowStateEx.Normal, point.X + 32, point.Y + 32, (int)(container.Bounds.Width * scale), (int)(container.Bounds.Height * scale));
        }
        _model.Layout.OpenWindow(key, placement);
        if (_windows.TryGetValue(key, out var floating) && floating.IsVisible) floating.Activate();
    }
    /// <summary>原浮窗/图标右键入口；停靠选择原位置，关闭保留浮动位置。</summary>
    private ContextMenu CreatePanelMenu(string key, bool fromContainer = false)
    {
        var floating = new MenuItem { Header = "浮动", IsEnabled = !_model.Layout.Windows.Contains(key) };
        var docking = new MenuItem { Header = "停靠", IsEnabled = _model.Layout.IsFloating(key) };
        var close = new MenuItem { Header = "关闭" };
        floating.Click += (_, _) => OpenWindow(key, fromContainer);
        docking.Click += (_, _) => { if (!_preparing) Dock(key); };
        close.Click += (_, _) => { if (!_preparing) { SaveWeights(); _model.Layout.Close(key, true); } };
        return new() { ItemsSource = new Control[] { floating, docking, new Separator(), close } };
    }
    /// <summary>停靠先快照浮窗，关闭控件宿主后原模型清除浮动位置。</summary>
    public void Dock(string key)
    { if (_preparing || _disposing) return; SaveWeights(); _model.Layout.OpenDock(key); }
    /// <summary>主窗显示之前不创建系统浮窗；打开后恢复原Windows集合。</summary>
    private void OwnerOpened(object? sender, EventArgs e) { _signature = ""; Refresh(); }
    private void FloatKeyDown(object? sender, KeyEventArgs e) => FloatingKeyDown?.Invoke(sender, e);
    /// <summary>实际关闭删除打开集合，位置保留；模型迁移/主关闭不误删保存状态。</summary>
    private void FloatingClosed(object? sender, EventArgs e)
    {
        if (sender is not FloatingPanelWindow floating || !_windows.ContainsKey(floating.PanelKey)) return;
        Snap(floating); RemoveWindowHost(floating); _windows.Remove(floating.PanelKey);
        if (!_disposing && !_refreshing) _model.Layout.Close(floating.PanelKey);
    }
    private void FloatingClosing(object? sender, WindowClosingEventArgs e)
    { if (_preparing) { e.Cancel = true; return; } if (sender is FloatingPanelWindow floating) Snap(floating); }
    private void FloatingPositionChanged(object? sender, PixelPointEventArgs e) { if (sender is FloatingPanelWindow floating) Snap(floating); }
    private void FloatingSizeChanged(object? sender, SizeChangedEventArgs e) { if (sender is FloatingPanelWindow floating) Snap(floating); }
    private void FloatingPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    { if (e.Property == Window.WindowStateProperty && sender is FloatingPanelWindow floating) Snap(floating); }
    private void Snap(FloatingPanelWindow floating)
    {
        if (!floating.IsVisible || _disposing || _refreshing) return;
        _model.Layout.SetWindowPlacement(floating.PanelKey, floating.Snap(_model.Layout.Panels[floating.PanelKey].WindowPlacement));
    }
    private void CloseWindowHost(string key)
    { var floating = _windows[key]; RemoveWindowHost(floating); _windows.Remove(key); floating.Close(); }
    /// <summary>先解除内容父级与事件，再关闭原生宿主，唯一面板可以安全重新停靠。</summary>
    private void RemoveWindowHost(FloatingPanelWindow floating)
    {
        if (ReferenceEquals(_dragRoot, floating.DragSurface)) CancelDrag();
        floating.Closed -= FloatingClosed; floating.Closing -= FloatingClosing; floating.PositionChanged -= FloatingPositionChanged; floating.SizeChanged -= FloatingSizeChanged;
        floating.PropertyChanged -= FloatingPropertyChanged;
        floating.RemoveHandler(InputElement.KeyDownEvent, FloatKeyDown); DetachDrag(floating.DragSurface); floating.ContentHost.Child = null;
    }
    private void AttachDrag(Control root)
    {
        root.AddHandler(InputElement.PointerPressedEvent, Pressed, RoutingStrategies.Tunnel);
        root.AddHandler(InputElement.PointerMovedEvent, Moved, RoutingStrategies.Tunnel);
        root.AddHandler(InputElement.PointerReleasedEvent, Released, RoutingStrategies.Tunnel); root.PointerCaptureLost += CaptureLost;
    }
    private void DetachDrag(Control root)
    {
        root.RemoveHandler(InputElement.PointerPressedEvent, Pressed); root.RemoveHandler(InputElement.PointerMovedEvent, Moved);
        root.RemoveHandler(InputElement.PointerReleasedEvent, Released); root.PointerCaptureLost -= CaptureLost;
    }

    /// <summary>分隔拖动后保存实际比例，使用原 GridLength 星号权重。</summary>
    public void SaveWeights()
    {
        foreach (var floating in _windows.Values) Snap(floating);
        foreach (var (grid, group) in _groups)
        {
            var lengths = group.Orientation == PanelOrientation.Horizontal ? grid.ColumnDefinitions.Where((_, i) => i % 2 == 0).Select(d => d.ActualWidth).ToArray() : grid.RowDefinitions.Where((_, i) => i % 2 == 0).Select(d => d.ActualHeight).ToArray();
            var total = lengths.Sum();
            if (lengths.Length != group.Count || total <= 0) continue;
            for (int i = 0; i < lengths.Length; i++) group[i].Weight = Math.Max(.001, lengths[i] / total);
        }
        Config.Current.Panels.Layout = _model.Layout.CreateMemento();
    }

    /// <summary>仅图标或标题可起拖，内容选择、滚动和文本编辑继续正常处理。</summary>
    private void Pressed(object? sender, PointerPressedEventArgs e)
    {
        if (_preparing || sender is not Control root || !e.GetCurrentPoint(root).Properties.IsLeftButtonPressed || e.Source is not Visual source) return;
        var control = source.GetSelfAndVisualAncestors().OfType<Control>().FirstOrDefault(_dragSources.ContainsKey);
        if (control is null) return;
        SaveWeights(); _pressedPanel = _dragSources[control]; _pressedSelect = control is Button; _dragRoot = root; _pressedPoint = e.GetPosition(root); _pointer = e.Pointer;
        e.Pointer.Capture(root); e.Handled = true;
    }

    /// <summary>超过拖动阈值后锁定自动隐藏，两种目标分别预览图标插入线与内容分割区。</summary>
    private void Moved(object? sender, PointerEventArgs e)
    {
        if (_pressedPanel is null || _dragRoot is null) return;
        var point = e.GetPosition(_dragRoot);
        var dx = point.X - _pressedPoint.X; var dy = point.Y - _pressedPoint.Y;
        if (!_dragging && dx * dx + dy * dy < 25) return;
        if (!_dragging) { _dragging = true; _model.SetPanelDragging(true); }
        // PointToScreen/PointToClient统一物理屏幕坐标，跨窗和不同缩放不能TranslatePoint。
        var mainPoint = ReferenceEquals(_dragRoot, _root) ? point : _root.PointToClient(_dragRoot.PointToScreen(point));
        _target = HitTarget(mainPoint); DrawPreview(); e.Handled = true;
    }

    /// <summary>释放时一次性提交；未拖动仍切换图标，空白落点取消布局改变。</summary>
    private void Released(object? sender, PointerReleasedEventArgs e)
    {
        if (_pressedPanel is not { } key) return;
        var target = _target; bool dragged = _dragging, select = _pressedSelect; EndDrag(); e.Handled = true;
        if (!dragged) { if (select) _model.SelectPanel(key); return; }
        if (target is null) return;
        if (target.Panel is { } panel) _model.Layout.CombinePanel(key, panel, target.Edge);
        else _model.Layout.MovePanel(key, target.Side, target.Index);
        // 拖放选择目标组并展开该栏，取消操作才恢复原显隐。
        if (target.Side == "Left") Config.Current.Panels.IsLeftVisible = true; else Config.Current.Panels.IsRightVisible = true;
        _model.RefreshPanels();
    }

    /// <summary>命中图标栏时给出移除前插入位置；命中内容沿用原方向分半算法。</summary>
    private Target? HitTarget(Point point)
    {
        foreach (var (side, rail) in _rails)
        {
            var origin = rail.TranslatePoint(default, _root);
            if (origin is null || point.X < origin.Value.X || point.X > origin.Value.X + 41 || point.Y < 0 || point.Y > _root.Bounds.Height) continue;
            var dock = _model.Layout.Docks[side];
            var index = rail.Children.OfType<Control>().Count(c => point.Y > (c.TranslatePoint(default, _root)?.Y ?? 0) + c.Bounds.Height * .5);
            return new(side, Math.Clamp(index, 0, dock.Items.Count), null, PanelDock.Bottom);
        }
        foreach (var (key, container) in _containers)
        {
            if (key == _pressedPanel || _model.Layout.Find(key) is not { } found) continue;
            var origin = container.TranslatePoint(default, _root);
            if (origin is null || !new Avalonia.Rect(origin.Value, container.Bounds.Size).Contains(point)) continue;
            var local = point - origin.Value;
            return new(found.Side, 0, key, LayoutPanelManager.GetLayoutDockFromPos(local.X, local.Y, container.Bounds.Width, container.Bounds.Height, found.Group));
        }
        return null;
    }

    /// <summary>预览不修改布局；取消时清除预览并释放捕获。</summary>
    private void DrawPreview()
    {
        _preview.Children.Clear(); if (_target is not { } target) return;
        Point origin; double width, height;
        if (target.Panel is { } key)
        {
            var container = _containers[key]; origin = container.TranslatePoint(default, _root) ?? default;
            width = container.Bounds.Width; height = container.Bounds.Height;
            if (target.Edge is PanelDock.Top or PanelDock.Bottom) { height /= 2; if (target.Edge == PanelDock.Bottom) origin = origin.WithY(origin.Y + height); }
            else { width /= 2; if (target.Edge == PanelDock.Right) origin = origin.WithX(origin.X + width); }
        }
        else
        {
            var rail = _rails[target.Side]; origin = rail.TranslatePoint(default, _root) ?? default;
            origin = origin.WithY(origin.Y + target.Index * 40); width = 41; height = 3;
        }
        var indicator = new Border { Width = width, Height = height, Classes = { "panel-drop-preview" } };
        Canvas.SetLeft(indicator, origin.X); Canvas.SetTop(indicator, origin.Y); _preview.Children.Add(indicator);
    }

    /// <summary>Escape 或捕获丢失取消拖动，布局只在有效释放时提交。</summary>
    public bool CancelDrag() { if (_pressedPanel is null) return false; EndDrag(); return true; }
    /// <summary>窗口失去捕获时恢复自动隐藏锁，保留原布局。</summary>
    private void CaptureLost(object? sender, PointerCaptureLostEventArgs e) { if (_pressedPanel is not null) EndDrag(); }
    /// <summary>先清状态再释放指针，防止捕获丢失回调重入。</summary>
    private void EndDrag()
    {
        _pressedPanel = null; _target = null; _dragging = false; _dragRoot = null; _preview.Children.Clear();
        var pointer = _pointer; _pointer = null; pointer?.Capture(null); _model.SetPanelDragging(false);
    }
    /// <summary>窗口关闭解除输入订阅，布局生命周期结束。</summary>
    public void Dispose()
    {
        if (_disposing) return; _disposing = true; CancelDrag(); DetachDrag(_root); _window.Opened -= OwnerOpened;
        foreach (var key in _windows.Keys.ToArray()) CloseWindowHost(key);
        foreach (var grid in _contentGrids) grid.Children.Clear(); _contentGrids.Clear(); FloatingKeyDown = null;
    }
    /// <summary>正常退出先冻结浮窗并保存位置；保存失败恢复同一内容供重试。</summary>
    public void PrepareClose() { CancelDrag(); SaveWeights(); _preparing = true; foreach (var floating in _windows.Values) floating.IsEnabled = false; }
    public void CancelClose() { _preparing = false; foreach (var floating in _windows.Values) floating.IsEnabled = true; }

    /// <summary>原面板名称及未迁入阶段，组合后仍可识别每个面板。</summary>
    private static string Title(string key) => key switch
    {
        "FolderPanel" => "文件夹", "PageListPanel" => "页面列表", "HistoryPanel" => "历史", "FileInformationPanel" => "信息", "NavigatePanel" => "导航器",
        "BookmarkPanel" => "书签", "ImageEffectPanel" => "图像效果（P5）", "PlaylistPanel" => "播放列表", "DestinationFolderPanel" => "目标文件夹（P4）", _ => key
    };
    /// <summary>使用原图标资源，主题替换不影响停靠操作。</summary>
    private PathIcon Icon(string key)
    {
        var resource = key switch { "FolderPanel" => "g_folder_24px", "PageListPanel" => "g_photo_library_24px", "HistoryPanel" => "g_history_24px", "FileInformationPanel" => "g_info_24px", "NavigatePanel" => "g_desktop_windows_24px", "BookmarkPanel" => "g_star_24px", "ImageEffectPanel" => "g_toy_24px", "PlaylistPanel" => "g_playlist_24px", _ => "g_recursive_folder_24px" };
        return new() { Data = _root.FindResource(resource) as Geometry };
    }
}
