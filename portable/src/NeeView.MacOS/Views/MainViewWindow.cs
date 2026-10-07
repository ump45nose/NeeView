// Copyright (c) NeeLaboratory. 原 MainViewWindow 的 Avalonia 宿主适配，MIT。
using Avalonia;
using Avalonia.Controls;
using NeeView.Windows;
namespace NeeView.MacOS.Views;

/// <summary>只承载唯一中央查看器；关闭默认最小化，真正停靠由 MainViewPresenter 负责。</summary>
public sealed class MainViewWindow : Window
{
    private readonly MainViewConfig _config;
    private bool _closingHost, _restoring;
    private WindowPlacement? _pending;
    private Window? _owner;
    public Border ContentHost { get; } = new();
    public bool IsPreparingClose { get; set; }
    internal bool IsReferenceSizeLocked { get; set; }
    /// <summary>接收原配置，窗口不创建阅读器或后端。</summary>
    public MainViewWindow(MainViewConfig config)
    {
        _config = config; Content = ContentHost; Width = 800; Height = 600; MinWidth = 160; MinHeight = 120;
        ShowActivated = false; WindowStartupLocation = WindowStartupLocation.Manual;
        PositionChanged += PositionUpdated; SizeChanged += SizeUpdated;
        PropertyChanged += StateUpdated;
        Closing += (_, e) =>
        {
            if (_closingHost) return;
            if (IsPreparingClose) { e.Cancel = true; return; }
            Store();
            if (!_config.IsFloatingEndWhenClosed) { e.Cancel = true; WindowState = WindowState.Minimized; }
        };
    }
    /// <summary>设备尺寸沿原五段位置；显示后用实际 RenderScaling 重新计算，不覆盖待恢复快照。</summary>
    public void RestorePlacement(Window owner)
    {
        _owner = owner; _pending = _config.WindowPlacement; _restoring = true;
        ApplyPlacement(owner.RenderScaling);
        WindowState = _pending.WindowStateEx switch
        { WindowStateEx.Maximized => WindowState.Maximized, WindowStateEx.FullScreen => WindowState.FullScreen, _ => WindowState.Normal };
    }
    protected override void OnOpened(EventArgs e)
    {
        ApplyPlacement(RenderScaling); _pending = null; _restoring = false;
        base.OnOpened(e);
    }
    private void ApplyPlacement(double renderScaling)
    {
        if (_pending is not { } placement || _owner is not { } owner) return;
        var point = placement.IsValid() ? new PixelPoint(placement.Left, placement.Top) : new PixelPoint(owner.Position.X + 32, owner.Position.Y + 32);
        var screen = Screens.ScreenFromPoint(point) ?? Screens.ScreenFromWindow(owner) ?? Screens.Primary;
        var result = FloatingPanelWindow.CalculatePlacement(placement, point, new(800, 600), new(MinWidth, MinHeight),
            screen?.WorkingArea, screen?.Scaling ?? 1, renderScaling);
        Width = result.Size.Width; Height = result.Size.Height; Position = result.Position;
    }
    /// <summary>最小化/全屏保留普通位置；LastState 保存全屏返回状态，仍使用原 JSON 字段。</summary>
    public void Store()
    {
        if (_restoring || _closingHost || !IsVisible) return;
        var previous = _config.WindowPlacement;
        var state = WindowState switch { WindowState.Maximized => WindowStateEx.Maximized, WindowState.FullScreen => WindowStateEx.FullScreen, _ => WindowStateEx.Normal };
        if (WindowState is WindowState.Normal or WindowState.Maximized) _config.LastState = state;
        if (WindowState != WindowState.Normal && previous.IsValid())
            _config.WindowPlacement = previous with { WindowStateEx = state };
        else _config.WindowPlacement = new(state, Position.X, Position.Y,
            Math.Max(1, (int)Math.Round(ClientSize.Width * RenderScaling)), Math.Max(1, (int)Math.Round(ClientSize.Height * RenderScaling)));
    }
    /// <summary>先清空内容再允许关闭宿主，不销毁唯一 ReaderView。</summary>
    internal void CloseHost(bool closeWindow = true)
    { Store(); _closingHost = true; ContentHost.Child = null; DetachStateEvents(); if (closeWindow) Close(); _owner = null; }
    private void PositionUpdated(object? sender, PixelPointEventArgs e) => Store();
    private void SizeUpdated(object? sender, SizeChangedEventArgs e)
    {
        // 对应原 PageFrameProfile.ReferenceSizeLocker：用户调整重设参考，自动贴合保留参考。
        if (!_restoring && !_closingHost && !IsPreparingClose && !IsReferenceSizeLocked && WindowState == WindowState.Normal
            && e.NewSize.Width > 0 && e.NewSize.Height > 0)
            _config.ReferenceSize = new(e.NewSize.Width, e.NewSize.Height);
        Store();
    }
    private void StateUpdated(object? sender, AvaloniaPropertyChangedEventArgs e)
    { if (e.Property == WindowStateProperty) Store(); }
    private void DetachStateEvents()
    { PositionChanged -= PositionUpdated; SizeChanged -= SizeUpdated; PropertyChanged -= StateUpdated; }
    protected override void OnClosed(EventArgs e) { DetachStateEvents(); base.OnClosed(e); }
}
