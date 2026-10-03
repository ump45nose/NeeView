using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using NeeView.Windows;
namespace NeeView.MacOS.Views;

/// <summary>原单面板浮动宿主；控件所有权和输入由侧栏表现管理，不持有业务后端。</summary>
public sealed partial class FloatingPanelWindow : Window
{
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
    /// <summary>按原屏幕像素恢复，在当前显示器工作区内限制尺寸及位置，避免断开显示器后丢失窗口。</summary>
    public void RestorePlacement(WindowPlacement placement, Window owner)
    {
        var desired = placement.IsValid() ? new PixelPoint(placement.Left, placement.Top) : new PixelPoint(owner.Position.X + 32, owner.Position.Y + 32);
        var screen = Screens.ScreenFromPoint(desired) ?? Screens.ScreenFromWindow(owner) ?? Screens.Primary;
        double scaling = screen?.Scaling ?? owner.RenderScaling;
        if (!double.IsFinite(scaling) || scaling <= 0) scaling = 1;
        var width = placement.IsValid() && placement.Width > 0 ? placement.Width / scaling : Width;
        var height = placement.IsValid() && placement.Height > 0 ? placement.Height / scaling : Height;
        if (screen is { } display)
        {
            var area = display.WorkingArea;
            width = Math.Clamp(width, Math.Min(MinWidth, area.Width / scaling), area.Width / scaling);
            height = Math.Clamp(height, Math.Min(MinHeight, area.Height / scaling), area.Height / scaling);
            desired = new((int)Math.Clamp((long)desired.X, area.X, (long)area.Right - (int)(width * scaling)),
                (int)Math.Clamp((long)desired.Y, area.Y, (long)area.Bottom - (int)(height * scaling)));
        }
        Width = width; Height = height; Position = desired;
        // 不恢复最小化/Windows FullDesktop，保证原面板可访问；最大化单独恢复。
        WindowState = placement.WindowStateEx == WindowStateEx.Maximized ? WindowState.Maximized : WindowState.Normal;
    }
    /// <summary>普通窗口客户端尺寸转换为原物理像素；最大化/最小化时沿用正常位置。</summary>
    public WindowPlacement Snap(WindowPlacement previous)
    {
        if (WindowState != WindowState.Normal && previous.IsValid()) return previous with { WindowStateEx = WindowState == WindowState.Maximized ? WindowStateEx.Maximized : previous.WindowStateEx };
        return new(WindowStateEx.Normal, Position.X, Position.Y,
            Math.Max(1, (int)Math.Round(ClientSize.Width * RenderScaling)), Math.Max(1, (int)Math.Round(ClientSize.Height * RenderScaling)));
    }
}

/// <summary>浮动面板属于非模态宿主，不应锁住主查看器和五区自动隐藏。</summary>
internal static class WindowInteraction
{
    public static bool HasDialog(Window owner) => owner.OwnedWindows.Any(w => w.IsVisible && w is not FloatingPanelWindow);
}
