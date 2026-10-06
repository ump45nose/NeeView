using Avalonia.Threading;
using NeeView;
namespace NeeView.MacOS.Views;

public sealed partial class ReaderView
{
    internal ThemeRgba CurrentContentColor => IsBrowsing ? _browse?.ContentColor ?? ThemeRgba.Parse("Black") :
        _operation?.Book?.CurrentPage is { } page && _images.TryGetValue(page, out var image) ? image.Color : ThemeRgba.Parse("Black");
    /// <summary>命令仅刷新图像表现，后台事件回到UI线程；失败进入既有查看器错误。</summary>
    private void ImagePresentationChanged(object? sender, EventArgs e) => Dispatcher.UIThread.Post(async () =>
    {
        if (_disposed) return;
        try { await RefreshAppearanceAsync(); }
        catch (Exception error) { if (!_disposed) { _loadError = error.Message; InvalidateVisual(); } }
    });
    /// <summary>背景不刷新阅读需求；像素保持规格变化才复用既有显示加载链。</summary>
    public async Task RefreshAppearanceAsync()
    {
        if (_disposed) return;
        _background?.Refresh();
        var dot = (Config.Current.ImageDotKeep.IsEnabled, Config.Current.ImageDotKeep.Threshold);
        if (_dotKeep != dot) { _dotKeep = dot; await RefreshAsync(); }
        InvalidateVisual();
    }
}
