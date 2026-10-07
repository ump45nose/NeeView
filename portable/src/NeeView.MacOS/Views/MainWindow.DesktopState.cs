using NeeView.Windows;
namespace NeeView.MacOS.Views;
public sealed partial class MainWindow
{
    private bool _restoringWindow = true;
    private void RestoreMainWindowPlacement()
    {
        var config = Config.Current.Window;
        if (config.WindowPlacement is { } placement && placement.IsValid())
        {
            var point = new Avalonia.PixelPoint(placement.Left,placement.Top);
            var screen = Screens.ScreenFromPoint(point) ?? Screens.Primary;
            var value = FloatingPanelWindow.CalculatePlacement(placement, point, new(ClientSize.Width,ClientSize.Height), new(MinWidth,MinHeight),screen?.WorkingArea,screen?.Scaling ?? 1,RenderScaling);
            Position = value.Position; Width = value.Size.Width; Height = value.Size.Height;
            WindowDisplayState.Set(this,placement.WindowStateEx,config.LastState);
        }
        _restoringWindow = false;
    }
    private void StoreMainWindowPlacement()
    {
        if (_restoringWindow || !IsVisible) return;
        var config = Config.Current.Window;
        config.State = WindowDisplayState.Get(this);
        if (config.State is WindowStateEx.Normal or WindowStateEx.Maximized) config.LastState = config.State;
        config.WindowPlacement = WindowDisplayState.Capture(this,config.WindowPlacement ?? WindowPlacement.None);
    }
    /// <summary>跨屏命令作用于实际查看器宿主，浮动后仍只操作同一个阅读器。</summary>
    public bool IsFullDesktop => WindowDisplayState.Get(ViewerHostWindow) == WindowStateEx.FullDesktop;
    public void SetFullDesktop(bool enabled) => WindowDisplayState.Set(ViewerHostWindow,
        enabled ? WindowStateEx.FullDesktop : WindowStateEx.Normal);
}
