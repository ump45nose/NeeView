// Copyright (c) NeeLaboratory. 原 MainViewManager 的唯一控件宿主/替代面板关系，MIT。
using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using NeeView.MacOS.ViewModels;
using NeeView.Windows;
namespace NeeView.MacOS.Views;

/// <summary>中央查看器停靠/浮动的表现所有者；同一内容、图像工厂、选区与播放器保持。</summary>
public sealed class MainViewPresenter : IDisposable
{
    private readonly MainWindow _owner;
    private readonly ReaderWorkspaceViewModel _model;
    private readonly SidePanelPresenter _panels;
    private readonly Border _dock, _alternative = new();
    private readonly Grid _content;
    private MainViewConfig _config;
    private bool _updating, _preparing, _disposed;
    private Book? _book;
    private PagePosition _position;
    private readonly DispatcherTimer _stretchTimer = new() { Interval = TimeSpan.FromMilliseconds(100) };
    public MainViewWindow? Window { get; private set; }
    public event EventHandler<KeyEventArgs>? KeyDown;
    public event EventHandler<DragEventArgs>? Drop;
    public event EventHandler? Changed;
    public event EventHandler? AutoStretchRequested;
    public event Action<SlideShowTimerResetGesture, bool>? Input;

    /// <summary>从原 XAML 接收唯一查看器内容，不重新 Attach 阅读服务。</summary>
    public MainViewPresenter(MainWindow owner, ReaderWorkspaceViewModel model, SidePanelPresenter panels)
    {
        _owner = owner; _model = model; _panels = panels; _config = Config.Current.MainView;
        _dock = owner.FindControl<Border>("MainViewDockSocket")!; _content = owner.FindControl<Grid>("MainViewContent")!;
        _config.PropertyChanged += ConfigurationChanged; _owner.Opened += OwnerOpened;
        _model.Refreshed += ReadingChanged; owner.Viewer.DisplayCompleted += DisplayCompleted;
        _stretchTimer.Tick += (_, _) => { _stretchTimer.Stop(); if (!_preparing && Window is not null) AutoStretchRequested?.Invoke(this, EventArgs.Empty); };
        Refresh();
    }
    /// <summary>设置重载只更换配置订阅，返回原宿主后再按真实新配置恢复。</summary>
    public void Refresh()
    {
        if (_disposed || _preparing || _updating) return;
        if (!ReferenceEquals(_config, Config.Current.MainView))
        {
            Dock(); _config.PropertyChanged -= ConfigurationChanged; _config = Config.Current.MainView; _config.PropertyChanged += ConfigurationChanged;
        }
        _updating = true;
        try
        {
            if (_config.IsFloating && _owner.IsVisible) Float();
            else if (!_config.IsFloating) Dock();
            if (Window is { } window)
            {
                window.Topmost = _config.IsTopmost;
                if (WindowDisplayState.Get(window) != WindowStateEx.FullDesktop) window.WindowDecorations = _config.IsHideTitleBar ? WindowDecorations.None : WindowDecorations.Full;
                window.Title = _owner.Title;
                _panels.SetAlternativeHost(_config.AlternativeContent == AlternativeContent.PageList ? _alternative : null);
            }
        }
        finally { _updating = false; }
        Changed?.Invoke(this, EventArgs.Empty);
    }
    /// <summary>脚本/命令使用原 IsFloating；已有窗口显式打开会恢复并获得焦点。</summary>
    public void SetFloating(bool value, bool focus = false)
    {
        if (_preparing || _disposed) return;
        _config.IsFloating = value; Refresh();
        if (focus && Window is { } window) { if (window.WindowState == WindowState.Minimized) window.WindowState = WindowState.Normal; window.Activate(); _owner.Viewer.Focus(); }
    }
    private void Float()
    {
        if (Window is not null) return;
        _owner.Viewer.CancelMouseSequence(); _owner.Viewer.SetLoupe(false); _owner.Viewer.StopAutoScroll();
        if (!_config.WindowPlacement.IsValid())
        {
            var point = _content.PointToScreen(default); var size = _content.Bounds.Size;
            _config.WindowPlacement = new(WindowStateEx.Normal, point.X + 32, point.Y + 32,
                Math.Max(160, (int)Math.Round(size.Width * _owner.RenderScaling)), Math.Max(120, (int)Math.Round(size.Height * _owner.RenderScaling)));
        }
        if (_config.ReferenceSize.Width <= 0 || _config.ReferenceSize.Height <= 0)
            _config.ReferenceSize = new(Math.Max(1, _content.Bounds.Width), Math.Max(1, _content.Bounds.Height));
        _dock.Child = null;
        var window = new MainViewWindow(_config) { DataContext = _model, Title = _owner.Title };
        Window = window; window.ContentHost.Child = _content; _dock.Child = _alternative;
        window.AddHandler(InputElement.KeyDownEvent, FloatingKeyDown, RoutingStrategies.Tunnel);
        window.AddHandler(DragDrop.DropEvent, FloatingDrop); DragDrop.SetAllowDrop(window, true);
        window.AddHandler(InputElement.PointerPressedEvent, FloatingPressed, RoutingStrategies.Tunnel, true);
        window.AddHandler(InputElement.PointerWheelChangedEvent, FloatingWheel, RoutingStrategies.Tunnel, true);
        window.AddHandler(InputElement.PointerMovedEvent, FloatingMoved, RoutingStrategies.Tunnel, true);
        window.Deactivated += Deactivated; window.Closed += Closed;
        window.RestorePlacement(_owner); window.Show(_owner); window.Store();
        if (_config.IsAutoHide && _model.Operation.Book is null) window.WindowState = WindowState.Minimized;
        if (_config.IsAutoStretch) _stretchTimer.Start();
    }
    private void Dock(bool closeWindow = true)
    {
        if (Window is not { } window) return;
        _stretchTimer.Stop(); _owner.Viewer.CancelMouseSequence(); _owner.Viewer.SetLoupe(false); _owner.Viewer.StopAutoScroll();
        Window = null; window.Deactivated -= Deactivated; window.Closed -= Closed;
        window.RemoveHandler(InputElement.KeyDownEvent, FloatingKeyDown); window.RemoveHandler(DragDrop.DropEvent, FloatingDrop);
        window.RemoveHandler(InputElement.PointerPressedEvent, FloatingPressed); window.RemoveHandler(InputElement.PointerWheelChangedEvent, FloatingWheel);
        window.RemoveHandler(InputElement.PointerMovedEvent, FloatingMoved);
        window.CloseHost(closeWindow); _panels.SetAlternativeHost(null); _dock.Child = _content;
    }
    private void OwnerOpened(object? sender, EventArgs e) => Refresh();
    private void ConfigurationChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (_disposed || _updating) return;
        Dispatcher.UIThread.Post(() =>
        {
            if (_disposed) return;
            Refresh();
            if (e.PropertyName == nameof(MainViewConfig.IsAutoStretch) && _config.IsAutoStretch && Window is not null)
            { _stretchTimer.Stop(); _stretchTimer.Start(); }
        });
    }
    private void Closed(object? sender, EventArgs e)
    {
        if (_disposed || _updating) return;
        _config.IsFloating = false; Dock(false); Changed?.Invoke(this, EventArgs.Empty);
    }
    private void Deactivated(object? sender, EventArgs e) { _owner.Viewer.SetLoupe(false); _owner.Viewer.StopAutoScroll(); }
    private void FloatingKeyDown(object? sender, KeyEventArgs e) => KeyDown?.Invoke(sender, e);
    private void FloatingDrop(object? sender, DragEventArgs e) => Drop?.Invoke(sender, e);
    private void FloatingPressed(object? sender, PointerPressedEventArgs e) => Input?.Invoke(SlideShowTimerResetGesture.InputAction, true);
    private void FloatingWheel(object? sender, PointerWheelEventArgs e) => Input?.Invoke(SlideShowTimerResetGesture.InputAction, true);
    private void FloatingMoved(object? sender, PointerEventArgs e) => Input?.Invoke(SlideShowTimerResetGesture.MouseMove, false);
    private void ReadingChanged(object? sender, EventArgs e)
    {
        if (!Dispatcher.UIThread.CheckAccess()) { Dispatcher.UIThread.Post(() => ReadingChanged(sender, e)); return; }
        if (_preparing || _disposed) return;
        Refresh(); var book = _model.Operation.Book; var position = _model.Operation.Position;
        if (ReferenceEquals(book, _book) && position == _position) return;
        _book = book; _position = position;
        if (Window is not { } window) return;
        if (book is null && _config.IsAutoHide) window.WindowState = WindowState.Minimized;
        else if (book is not null && _config.IsAutoShow && window.WindowState == WindowState.Minimized)
        { window.WindowState = WindowState.Normal; if (_config.IsFrontAsPossible) window.Activate(); }
    }
    private void DisplayCompleted(object? sender, EventArgs e)
    { if (!_disposed && !_preparing && Window is not null && _config.IsAutoStretch) { _stretchTimer.Stop(); _stretchTimer.Start(); } }
    /// <summary>冻结并保存原浮动状态；保存失败恢复同一窗口，成功退出才销毁宿主。</summary>
    public void PrepareClose() { _preparing = true; _stretchTimer.Stop(); if (Window is { } window) { window.Store(); window.IsPreparingClose = true; window.IsEnabled = false; } }
    public void CancelClose() { _preparing = false; if (Window is { } window) { window.IsPreparingClose = false; window.IsEnabled = true; } }
    public void Store() => Window?.Store();
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true; _stretchTimer.Stop(); _config.PropertyChanged -= ConfigurationChanged; _owner.Opened -= OwnerOpened;
        _model.Refreshed -= ReadingChanged; _owner.Viewer.DisplayCompleted -= DisplayCompleted; Dock();
        KeyDown = null; Drop = null; Input = null; Changed = null; AutoStretchRequested = null;
    }
}
