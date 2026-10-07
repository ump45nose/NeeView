using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using NeeView.Windows;
namespace NeeView.MacOS.Views;

/// <summary>原单面板浮动宿主；控件所有权和输入由侧栏表现管理，不持有业务后端。</summary>
public sealed partial class FloatingPanelWindow : Window
{
    private WindowPlacement? _pendingPlacement;
    private Window? _placementOwner;
    private Size _defaultSize;
    public string PanelKey { get; }
    public Grid DragSurface => this.FindControl<Grid>("PartDragSurface")!;
    public Border PanelHeader => this.FindControl<Border>("PartPanelHeader")!;
    public Border ContentHost => this.FindControl<Border>("PartContentHost")!;
    public FloatingPanelWindow() : this("", "面板") { }
    public FloatingPanelWindow(string key, string title)
    {
        AvaloniaXamlLoader.Load(this); PanelKey = key; Title = title;
        this.FindControl<TextBlock>("PanelTitle")!.Text = title;
        ToolTip.SetTip(PanelHeader, "拖回主窗口的图标栏或内容区；右键可停靠或关闭");
    }
    /// <summary>恢复原客户端物理尺寸；屏幕坐标和绘制像素在 macOS 上使用不同缩放。</summary>
    /// <param name="placement">原五段位置，宽高为客户端设备像素。</param>
    /// <param name="owner">未显示前提供位置和临时绘制缩放；显示后使用浮窗实际缩放。</param>
    public void RestorePlacement(WindowPlacement placement, Window owner)
    {
        _pendingPlacement = placement; _placementOwner = owner; _defaultSize = new(Width, Height);
        ApplyPlacement(placement, owner, owner.RenderScaling);
        // 不恢复最小化/Windows FullDesktop，保证原面板可访问；最大化单独恢复。
        WindowState = placement.WindowStateEx == WindowStateEx.Maximized ? WindowState.Maximized : WindowState.Normal;
    }
    /// <summary>原生窗口已选择实际显示器后重新换算；恢复期间的尺寸事件不得覆盖原保存值。</summary>
    protected override void OnOpened(EventArgs e)
    {
        if (_pendingPlacement is { } placement && _placementOwner is { } owner)
            ApplyPlacement(placement, owner, RenderScaling);
        _pendingPlacement = null; _placementOwner = null;
        base.OnOpened(e);
    }
    private void ApplyPlacement(WindowPlacement placement, Window owner, double renderScaling)
    {
        var desired = placement.IsValid() ? new PixelPoint(placement.Left, placement.Top) : new PixelPoint(owner.Position.X + 32, owner.Position.Y + 32);
        var screen = Screens.ScreenFromPoint(desired) ?? Screens.ScreenFromWindow(owner) ?? Screens.Primary;
        var restored = CalculatePlacement(placement, desired, _defaultSize, new(MinWidth, MinHeight), screen?.WorkingArea, screen?.Scaling ?? 1, renderScaling);
        Width = restored.Size.Width; Height = restored.Size.Height; Position = restored.Position;
    }
    /// <summary>将设备像素尺寸转为 DIP，工作区/位置使用桌面坐标缩放；两者不能混用。</summary>
    /// <param name="placement">原客户端设备像素尺寸。</param><param name="desired">桌面坐标位置。</param>
    /// <param name="defaults">未保存位置时的 DIP 尺寸。</param><param name="minimum">DIP 最小尺寸。</param>
    /// <param name="area">桌面坐标工作区。</param><param name="desktopScaling">桌面坐标每 DIP 的单位数。</param>
    /// <param name="renderScaling">客户端设备像素每 DIP 的单位数。</param>
    /// <returns>可访问的桌面位置与 DIP 客户端尺寸。</returns>
    internal static (PixelPoint Position, Size Size) CalculatePlacement(WindowPlacement placement, PixelPoint desired, Size defaults, Size minimum, PixelRect? area, double desktopScaling, double renderScaling)
    {
        if (!double.IsFinite(renderScaling) || renderScaling <= 0) renderScaling = 1;
        if (!double.IsFinite(desktopScaling) || desktopScaling <= 0) desktopScaling = 1;
        var width = placement.Width > 0 ? placement.Width / renderScaling : defaults.Width;
        var height = placement.Height > 0 ? placement.Height / renderScaling : defaults.Height;
        if (area is { Width: > 0, Height: > 0 } work)
        {
            width = Math.Clamp(width, Math.Min(minimum.Width, work.Width / desktopScaling), work.Width / desktopScaling);
            height = Math.Clamp(height, Math.Min(minimum.Height, work.Height / desktopScaling), work.Height / desktopScaling);
            desired = new((int)Math.Clamp((long)desired.X, work.X, (long)work.Right - (long)Math.Ceiling(width * desktopScaling)),
                (int)Math.Clamp((long)desired.Y, work.Y, (long)work.Bottom - (long)Math.Ceiling(height * desktopScaling)));
        }
        return (desired, new(width, height));
    }
    /// <summary>普通窗口客户端尺寸转换为原物理像素；最大化/最小化时沿用正常位置。</summary>
    public WindowPlacement Snap(WindowPlacement previous)
    {
        if (_pendingPlacement is not null) return previous;
        if (WindowState != WindowState.Normal && previous.IsValid()) return previous with { WindowStateEx = WindowState == WindowState.Maximized ? WindowStateEx.Maximized : previous.WindowStateEx };
        return new(WindowStateEx.Normal, Position.X, Position.Y,
            Math.Max(1, (int)Math.Round(ClientSize.Width * RenderScaling)), Math.Max(1, (int)Math.Round(ClientSize.Height * RenderScaling)));
    }
}

/// <summary>浮动面板属于非模态宿主，不应锁住主查看器和五区自动隐藏。</summary>
internal static class WindowInteraction
{
    public static bool HasDialog(Window owner) => owner.OwnedWindows.Any(w => w.IsVisible && w is not FloatingPanelWindow and not MainViewWindow);
}
