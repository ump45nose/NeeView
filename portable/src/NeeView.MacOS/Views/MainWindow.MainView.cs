using Avalonia.Controls;
namespace NeeView.MacOS.Views;
public sealed partial class MainWindow
{
    /// <summary>原 StretchWindow 改变实际窗口尺寸，再强制内容适配；中央浮动时作用于同一查看器的当前宿主。</summary>
    /// <param name="automatic">自动浮动模式使用保留参考视口，不随每次内容变化累计收缩。</param>
    private async Task StretchHostWindowAsync(bool automatic)
    {
        if (_preparing || _closedPrepared || _model?.Operation.IsFrameReading != true) return;
        var host = ViewerHostWindow;
        if (host.WindowState != WindowState.Normal) return;
        var config = Config.Current.MainView;
        if (automatic && (!config.IsFloating || !config.IsAutoStretch)) return;
        var rect = Viewer.GetContentRect();
        var size = automatic ? Viewer.GetReferenceStretchSize(config.ReferenceSize) : new NeeView.Size(rect.Width, rect.Height);
        if (!double.IsFinite(size.Width) || !double.IsFinite(size.Height) || size.Width <= 0 || size.Height <= 0) return;
        // 原 SetWindowSize 保留查看器之外的菜单、侧栏和底栏客户端大小。
        var width = size.Width + Math.Max(0, host.ClientSize.Width - Viewer.Bounds.Width);
        var height = size.Height + Math.Max(0, host.ClientSize.Height - Viewer.Bounds.Height);
        if (Math.Abs(host.Width - width) < .5 && Math.Abs(host.Height - height) < .5) return;
        var floating = host as MainViewWindow;
        if (floating is not null) floating.IsReferenceSizeLocked = automatic;
        try
        {
            host.Width = Math.Max(host.MinWidth, width); host.Height = Math.Max(host.MinHeight, height);
            host.UpdateLayout(); await Viewer.StretchAsync(); _mainView?.Store();
        }
        finally { if (floating is not null) floating.IsReferenceSizeLocked = false; }
    }
}
