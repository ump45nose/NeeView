// Copyright (c) NeeLaboratory. 原 WindowStateManager 的 Avalonia 窗口状态替换点，MIT。
using Avalonia;
using Avalonia.Controls;
using NeeView.Windows;
using System.Runtime.CompilerServices;
namespace NeeView.MacOS.Views;

/// <summary>宿主级状态与普通窗口快照；不模拟控件，不持有阅读业务。</summary>
internal sealed class WindowDisplayState
{
    private static readonly ConditionalWeakTable<Window, WindowDisplayState> States = new();
    private readonly Window _window;
    private bool _desktop, _changing;
    private WindowState _resume = WindowState.Normal;
    private PixelPoint _position;
    private Avalonia.Size _size;
    private WindowDecorations _decorations;
    private Screens? _screens;
    public event EventHandler? Changed;
    private WindowDisplayState(Window window)
    { _window = window; window.Closed += Closed; window.PropertyChanged += PropertyChanged; }
    internal static WindowDisplayState For(Window window) => States.GetValue(window, w => new(w));
    internal static WindowStateEx Get(Window window) => For(window).State;
    private WindowStateEx State => _window.WindowState == WindowState.Minimized ? WindowStateEx.Minimized : _desktop ? WindowStateEx.FullDesktop
        : Enum.Parse<WindowStateEx>(_window.WindowState.ToString());
    /// <summary>None不操作；退出跨屏先还原实际装饰/几何，再应用目标状态。</summary>
    internal static void Set(Window window, WindowStateEx state, WindowStateEx? resumeState = null) => For(window).SetState(state, resumeState);
    private void SetState(WindowStateEx state, WindowStateEx? resumeState)
    {
        if (state == WindowStateEx.None) return;
        if (resumeState is { } restoredState) _resume = restoredState == WindowStateEx.Maximized ? WindowState.Maximized : WindowState.Normal;
        if (state == WindowStateEx.FullDesktop)
        {
            if (!_desktop)
            {
                _resume = _window.WindowState is WindowState.Maximized ? WindowState.Maximized : WindowState.Normal;
                if (resumeState is { } persisted) _resume = persisted == WindowStateEx.Maximized ? WindowState.Maximized : WindowState.Normal;
                _position = _window.Position; _size = _window.ClientSize; _decorations = _window.WindowDecorations;
                _screens = _window.Screens;
                _desktop = true; _screens.Changed += ScreensChanged;
            }
            _changing = true;
            try { _window.WindowState = WindowState.Normal; _window.WindowDecorations = WindowDecorations.None; ResizeDesktop(); }
            finally { _changing = false; }
        }
        else if (state == WindowStateEx.Minimized) _window.WindowState = WindowState.Minimized;
        else
        {
            var resume = _desktop || _window.WindowState == WindowState.FullScreen ? _resume : WindowState.Normal;
            if (_desktop) Restore();
            if (state == WindowStateEx.Normal) _window.WindowState = resume;
            else
            {
                if (state == WindowStateEx.FullScreen && _window.WindowState != WindowState.FullScreen)
                {
                    _resume = _window.WindowState == WindowState.Maximized ? WindowState.Maximized : WindowState.Normal;
                    if (resumeState is { } persisted) _resume = persisted == WindowStateEx.Maximized ? WindowState.Maximized : WindowState.Normal;
                }
                _window.WindowState = Enum.Parse<WindowState>(state.ToString());
            }
        }
        Changed?.Invoke(this, EventArgs.Empty);
    }
    private void Restore()
    {
        _changing = true;
        try
        {
            _desktop = false; DetachScreens();
            _window.WindowState = WindowState.Normal; _window.WindowDecorations = _decorations;
            _window.Position = _position;
            _window.Width = Math.Max(_window.MinWidth, _size.Width); _window.Height = Math.Max(_window.MinHeight, _size.Height);
        }
        finally { _changing = false; }
    }
    /// <summary>持久化使用普通窗口设备尺寸，不能把跨屏大小覆盖恢复位置。</summary>
    internal static WindowPlacement Capture(Window window, WindowPlacement previous)
    {
        var adapter = For(window); var state = adapter.State;
        if (adapter._desktop) return new(WindowStateEx.FullDesktop, adapter._position.X, adapter._position.Y,
            Math.Max(1, (int)Math.Round(adapter._size.Width * window.RenderScaling)), Math.Max(1, (int)Math.Round(adapter._size.Height * window.RenderScaling)));
        if (window.WindowState != WindowState.Normal && previous.IsValid()) return previous with { WindowStateEx = state == WindowStateEx.Minimized ? previous.WindowStateEx : state };
        return new(state, window.Position.X, window.Position.Y,
            Math.Max(1, (int)Math.Round(window.ClientSize.Width * window.RenderScaling)), Math.Max(1, (int)Math.Round(window.ClientSize.Height * window.RenderScaling)));
    }
    private void ResizeDesktop()
    {
        var bounds = DesktopBounds(_window);
        // macOS桌面坐标是点，Screens.Scaling和RenderScaling并非同一个单位。
        var scale = _window.Screens.ScreenFromWindow(_window)?.Scaling ?? 1;
        var size = DipSize(bounds.Size, scale);
        _window.Position = bounds.Position; _window.Width = Math.Max(_window.MinWidth, size.Width); _window.Height = Math.Max(_window.MinHeight, size.Height);
    }
    private void ScreensChanged(object? sender, EventArgs e) { if (_desktop && _window.WindowState != WindowState.Minimized) ResizeDesktop(); }
    private void PropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (_changing || e.Property != Window.WindowStateProperty) return;
        if (_desktop && _window.WindowState is WindowState.Maximized or WindowState.FullScreen)
        { var requested = _window.WindowState; Restore(); _window.WindowState = requested; }
        else if (_desktop && _window.WindowState == WindowState.Normal) ResizeDesktop();
        Changed?.Invoke(this, EventArgs.Empty);
    }
    private void Closed(object? sender, EventArgs e)
    { DetachScreens(); _window.PropertyChanged -= PropertyChanged; _window.Closed -= Closed; Changed = null; States.Remove(_window); }
    /// <summary>仅退订进入跨屏时取得的后端；未显示或已关闭的窗口不能重新读取Screens。</summary>
    private void DetachScreens()
    { if (_screens is not null) _screens.Changed -= ScreensChanged; _screens = null; }
    internal static PixelRect DesktopBounds(Window window)
    {
        var screens = window.Screens.All;
        if (screens.Count == 0) return new(window.Position, new PixelSize(Math.Max(1,(int)window.ClientSize.Width), Math.Max(1,(int)window.ClientSize.Height)));
        var result = screens[0].Bounds; foreach (var screen in screens.Skip(1)) result = Union(result, screen.Bounds); return result;
    }
    internal static PixelRect Union(PixelRect a, PixelRect b)
    { var left = Math.Min(a.X,b.X); var top = Math.Min(a.Y,b.Y); return new(left,top,Math.Max(a.Right,b.Right)-left,Math.Max(a.Bottom,b.Bottom)-top); }
    internal static Avalonia.Size DipSize(PixelSize pixels,double scaling) => new(pixels.Width/(scaling>0?scaling:1),pixels.Height/(scaling>0?scaling:1));
}
