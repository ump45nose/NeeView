using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Threading;
using Avalonia.VisualTree;
using NeeView.MacOS.ViewModels;
namespace NeeView.MacOS.Views;

/// <summary>从原 AutoHideBehavior/DelayValue 迁入的延迟决策；不持有控件或线程资源。</summary>
internal sealed class AutoHideVisibility
{
    public bool Visible { get; private set; } = true;
    private bool? _pending;
    private double _deadline;
    /// <summary>原VisibleOnce立即改变实际显示，再沿既有焦点/悬停和延迟规则收起。</summary>
    public void SetVisibleOnce(bool value) { Visible = value; _pending = null; }
    /// <summary>立即锁定优先；重复请求只缩短截止时间，键盘延长隐藏才使用 force。</summary>
    /// <param name="now">单调时钟，秒。</param>
    /// <param name="enabled">该区域当前是否具备原自动隐藏资格。</param>
    /// <param name="locked">焦点、弹出层、拖动或 ShowHiddenPanels 锁。</param>
    /// <param name="hover">实际区域或有效边缘命中。</param>
    /// <param name="reset">资格改变时立即应用隐藏，保留原 OnIsEnabledChanged 的 Now 语义。</param>
    /// <param name="force">按键活动重新发出延迟隐藏。</param>
    /// <returns>最终可见性；迟到 tick 不延长已确定截止时间。</returns>
    public bool Update(double now, bool enabled, bool locked, bool hover, AutoHideConfig config, bool reset = false, bool force = false)
    {
        bool immediate = !enabled || locked;
        bool target = immediate || hover;
        double delay = target ? immediate ? 0 : Math.Max(0, config.AutoHideDelayVisibleTime) : reset ? 0 : Math.Max(.001, config.AutoHideDelayTime);
        if (target == Visible) _pending = null;
        else
        {
            var deadline = now + delay;
            if (_pending != target || force) { _pending = target; _deadline = deadline; }
            else _deadline = Math.Min(_deadline, deadline);
            if (now >= _deadline) { Visible = target; _pending = null; }
        }
        return Visible;
    }
}

/// <summary>原五区自动隐藏的 Avalonia 表现适配；独立于正文、布局数据和主题。</summary>
public sealed class AutoHidePresenter : IDisposable
{
    private readonly Window _window;
    private readonly ReaderWorkspaceViewModel _model;
    private readonly Grid _root;
    private readonly Border _menu, _left, _right, _status, _filmLayer;
    private readonly Control _leftRail, _rightRail, _slider;
    private readonly ThumbnailView _film;
    private readonly StackPanel _bottom;
    private readonly Menu _menuBar;
    private readonly AutoHideVisibility[] _states = Enumerable.Range(0, 5).Select(_ => new AutoHideVisibility()).ToArray();
    private readonly bool[] _enabled = new bool[5];
    private readonly DispatcherTimer _timer;
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private Point? _pointer;
    private Control? _logicalFocus;
    private bool _locked, _lockedOld, _disposed;
    public bool IsVisibleLocked => _locked;

    /// <summary>绑定正式五个区域及同一胶片条；窗口关闭后必须 Dispose 停止时钟和输入订阅。</summary>
    public AutoHidePresenter(Window window, ReaderWorkspaceViewModel model)
    {
        _window = window; _model = model; _root = window.FindControl<Grid>("Root")!;
        _menu = window.FindControl<Border>("DockMenuSocket")!; _status = window.FindControl<Border>("DockStatusArea")!;
        _left = window.FindControl<Border>("LeftPanel")!; _right = window.FindControl<Border>("RightPanel")!;
        _leftRail = window.FindControl<Border>("LeftRail")!; _rightRail = window.FindControl<Border>("RightRail")!;
        _slider = window.FindControl<Grid>("DockPageSliderSocket")!; _menuBar = window.FindControl<Menu>("MenuBar")!;
        _film = window.FindControl<ThumbnailView>("DockFilmStripSocket")!;
        _filmLayer = window.FindControl<Border>("LayerFilmStripSocket")!; _bottom = (StackPanel)_status.Child!;
        window.AddHandler(InputElement.PointerMovedEvent, PointerMoved, RoutingStrategies.Tunnel, handledEventsToo: true);
        window.AddHandler(InputElement.PointerPressedEvent, PointerPressed, RoutingStrategies.Tunnel, handledEventsToo: true);
        window.AddHandler(InputElement.PointerWheelChangedEvent, PointerWheel, RoutingStrategies.Tunnel, handledEventsToo: true);
        window.PointerExited += PointerExited; window.PropertyChanged += WindowChanged;
        _timer = new(TimeSpan.FromMilliseconds(40), DispatcherPriority.Background, (_, _) => Update());
        Refresh(); if (window.IsVisible) _timer.Start(); window.Opened += Opened;
    }

    /// <summary>窗口/配置改变时按原 MainWindowController/MainWindowModel 重算资格与覆盖插槽。</summary>
    public void Refresh()
    {
        if (_disposed || _window.WindowState == WindowState.Minimized) return;
        var config = Config.Current;
        _model.SetAutoHideMode(_window.WindowState switch
        {
            WindowState.FullScreen => config.Window.IsAutoHideInFullScreen,
            WindowState.Maximized => config.Window.IsAutoHideInMaximized,
            _ => config.Window.IsAutoHideInNormal
        });
        bool[] enabled = [_model.CanHideMenu, _model.LeftAutoHide, _model.RightAutoHide, _model.CanHideSlider, _model.CanHideFilmStrip];
        _window.Topmost = config.Window.IsTopmost;
        Grid.SetRowSpan(_menu, enabled[0] ? 3 : 1); _menu.VerticalAlignment = enabled[0] ? VerticalAlignment.Top : VerticalAlignment.Stretch;
        Grid.SetRow(_status, enabled[3] ? 0 : 2); Grid.SetRowSpan(_status, enabled[3] ? 3 : 1);
        _status.VerticalAlignment = enabled[3] ? VerticalAlignment.Bottom : VerticalAlignment.Stretch;
        // 原滑条隐藏时胶片条随整个底组；仅胶片条隐藏时移到独立覆盖插槽。
        if (enabled[4] && _film.Parent == _bottom) { _bottom.Children.Remove(_film); _filmLayer.Child = _film; }
        else if (!enabled[4] && _film.Parent == _filmLayer) { _filmLayer.Child = null; _bottom.Children.Insert(0, _film); }
        _filmLayer.Margin = new Thickness(0, 0, 0, _status.Bounds.Height);
        bool[] reset = enabled.Select((value, index) => value != _enabled[index]).ToArray();
        enabled.CopyTo(_enabled, 0); Update(reset);
        // 原 MainWindowViewModel 的 SidePanelMargin：只移动面板内容，不改变正文区域。
        var margin = new Thickness(0, enabled[0] ? SafeMargin(config.Panels.ConflictTopMargin) : 0,
            0, enabled[3] ? SafeMargin(config.Panels.ConflictBottomMargin) : 0);
        foreach (var name in new[] { "LeftDockHost", "RightDockHost", "LeftRailItems", "RightRailItems" })
            _window.FindControl<Control>(name)!.Margin = margin;
    }

    /// <summary>原边角内容余量只用于排布；损坏非有限值不传入布局系统。</summary>
    private static double SafeMargin(double value) => double.IsFinite(value) ? Math.Max(0, value) : 0;

    /// <summary>按原 EnterVisibleLocked 保留连续 ShowHiddenPanels 的切换记忆。</summary>
    public void ShowHiddenPanels() { _locked = !_lockedOld; _lockedOld = _locked; Update(); }
    /// <summary>原Menu/StatusVisibleAtOnce只影响对应区域，不锁定侧栏或创建持久状态。</summary>
    /// <param name="addressBar">true是顶部菜单/地址区，false是底部状态/滑条区。</param><param name="visible">原Toggle参数求得的当次实际状态。</param>
    public void SetChromeVisibleOnce(bool addressBar, bool visible)
    {
        if (_disposed) return;
        int index = addressBar ? 0 : 3;
        if (!_enabled[index]) return;
        _states[index].SetVisibleOnce(visible); Update();
    }
    /// <summary>按原 LeaveVisibleLocked 在后续键盘/指针/滚轮动作解除一次显示锁。</summary>
    public void LeaveVisibleLocked()
    {
        if (_locked) { _lockedOld = true; _locked = false; } else _lockedOld = false;
    }
    /// <summary>单次评估五个区域，复杂图像绘制与来源任务不参与；最小化期间冻结显示避免丢焦点。</summary>
    internal void Update(bool[]? reset = null, Control? keySource = null)
    {
        if (_disposed || _window.WindowState == WindowState.Minimized) return;
        _capture = _pressedPointer?.Captured as Control;
        var focused = _window.FocusManager?.GetFocusedElement() as Control;
        if (focused is not null && TopLevel.GetTopLevel(focused) == _window) _logicalFocus = focused;
        var config = Config.Current.AutoHide;
        var popups = _window.OpenedPopups.Select(p => p.PlacementTarget).OfType<Control>().ToArray();
        bool dialog = WindowInteraction.HasDialog(_window);
        Control[] regions = [_menu, _left, _right, _status, _film];
        bool[] shown = new bool[5];
        for (int i = 0; i < shown.Length; i++)
        {
            var region = regions[i];
            bool locked = _locked || dialog || (i is 1 or 2 && _model.IsPanelDragging) ||
                popups.Any(target => Contains(region, target)) || (i == 0 && _menuBar.IsOpen) || FocusLocks(region, focused);
            bool hover = region.IsVisible && region.IsPointerOver;
            if (i == 1) hover |= _leftRail.IsVisible && _leftRail.IsPointerOver;
            if (i == 2) hover |= _rightRail.IsVisible && _rightRail.IsPointerOver;
            // 捕获可越出控件：滑条、分隔条和标题拖动期间保持该区显示。
            locked |= _capture is { } captured && Contains(region, captured);
            hover |= HitEdge(i, config);
            bool force = keySource is not null && Contains(region, keySource) && config.IsAutoHideKeyDownDelay && !locked && !hover;
            shown[i] = _states[i].Update(_clock.Elapsed.TotalSeconds, _enabled[i], locked, hover, config, reset?[i] == true, force);
        }
        // 滑条为隐藏宿主时，胶片条与它一起出现；胶片条开关仍由表现模型决定。
        if (_enabled[3]) shown[4] = shown[3];
        _status.IsVisible = !_enabled[3] || shown[3];
        _model.SetChromeVisibility(shown[0], shown[1], shown[2], shown[3], shown[4]);
        if (_enabled[4]) _filmLayer.Margin = new Thickness(0, 0, 0, _status.Bounds.Height);
    }

    /// <summary>保留原左右边缘与上下冲突处理；真实区域悬停的优先级高于边缘冲突。</summary>
    private bool HitEdge(int index, AutoHideConfig config)
    {
        if (_pointer is not { } point || !new Avalonia.Rect(_root.Bounds.Size).Contains(point)) return false;
        var sideHovered = (_left.IsVisible && _left.IsPointerOver) || (_right.IsVisible && _right.IsPointerOver) ||
            (_leftRail.IsVisible && _leftRail.IsPointerOver) || (_rightRail.IsVisible && _rightRail.IsPointerOver);
        if (index == 0 && sideHovered && (config.AutoHideConflictTopMargin == AutoHideConflictMode.Deny ||
            config.AutoHideConflictTopMargin == AutoHideConflictMode.AllowPixel && point.Y > 1.5)) return false;
        if (index == 3 && sideHovered && (config.AutoHideConflictBottomMargin == AutoHideConflictMode.Deny ||
            config.AutoHideConflictBottomMargin == AutoHideConflictMode.AllowPixel && point.Y < _root.Bounds.Height - 1.5)) return false;
        return index switch
        {
            0 => point.Y < config.AutoHideHitTestVerticalMargin,
            1 => point.X < config.AutoHideHitTestHorizontalMargin,
            2 => point.X > _root.Bounds.Width - config.AutoHideHitTestHorizontalMargin - 1,
            3 => point.Y > _root.Bounds.Height - config.AutoHideHitTestVerticalMargin - 1,
            4 => point.Y > _root.Bounds.Height - _status.Bounds.Height - config.AutoHideHitTestVerticalMargin - 1,
            _ => false
        };
    }
    /// <summary>Mac 使用窗口焦点记忆对应原逻辑焦点；焦点已移到查看器时解除，弹出层另按宿主锁定。</summary>
    private bool FocusLocks(Control region, Control? focused)
    {
        var mode = Config.Current.AutoHide.AutoHideFocusLockMode;
        var target = mode is AutoHideFocusLockMode.LogicalFocusLock or AutoHideFocusLockMode.LogicalTextBoxFocusLock ? _logicalFocus : focused;
        return mode != AutoHideFocusLockMode.None && target is not null && Contains(region, target) &&
            (mode is AutoHideFocusLockMode.LogicalFocusLock or AutoHideFocusLockMode.FocusLock || target is TextBox);
    }
    /// <summary>按实际视觉归属查询；不遍历所有页面或创建 WPF 控件模拟层。</summary>
    private static bool Contains(Control root, Control target) => ReferenceEquals(root, target) || target.GetVisualAncestors().Contains(root);
    private Control? _capture;
    private IPointer? _pressedPointer;
    /// <summary>实际鼠标移动/捕获更新边缘需求；移动本身不会解除 ShowHiddenPanels 锁。</summary>
    private void PointerMoved(object? sender, PointerEventArgs e)
    { _pointer = e.GetPosition(_root); _capture = _pressedPointer?.Captured as Control; Update(); }
    /// <summary>离开窗口撤销边缘命中；原焦点/菜单和拖动锁仍有效。</summary>
    private void PointerExited(object? sender, PointerEventArgs e) { _pointer = null; Update(); }
    /// <summary>指针按下解除一次锁；下一 tick 观察模板建立的捕获。</summary>
    private void PointerPressed(object? sender, PointerPressedEventArgs e) { _pressedPointer = e.Pointer; LeaveVisibleLocked(); Update(); }
    /// <summary>原滚轮也是解除一次显示锁的操作。</summary>
    private void PointerWheel(object? sender, PointerWheelEventArgs e) { LeaveVisibleLocked(); Update(); }
    /// <summary>按键延长所属区域的待隐藏时间；输入作用域仍由窗口和控件负责。</summary>
    public void HandleKey(Control? source) { LeaveVisibleLocked(); Update(keySource: source); }
    /// <summary>正式打开后开始表现计时；设计器与未显示窗口不运行后台循环。</summary>
    private void Opened(object? sender, EventArgs e) { Refresh(); _timer.Start(); }
    /// <summary>全屏/最大化及恢复立即重算资格；最小化不变更控件可见性。</summary>
    private void WindowChanged(object? sender, AvaloniaPropertyChangedEventArgs e) { if (e.Property == Window.WindowStateProperty) Refresh(); }
    /// <summary>关闭释放全部订阅和计时器，不把窗口引用留到下一次重新打开。</summary>
    public void Dispose()
    {
        if (_disposed) return; _disposed = true; _timer.Stop();
        _window.RemoveHandler(InputElement.PointerMovedEvent, PointerMoved); _window.RemoveHandler(InputElement.PointerPressedEvent, PointerPressed);
        _window.RemoveHandler(InputElement.PointerWheelChangedEvent, PointerWheel);
        _window.PointerExited -= PointerExited; _window.PropertyChanged -= WindowChanged; _window.Opened -= Opened;
    }
}
