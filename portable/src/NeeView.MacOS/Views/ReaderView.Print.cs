using Avalonia;
using Avalonia.Threading;
using Avalonia.Media.Imaging;
using Avalonia.Media;
namespace NeeView.MacOS.Views;
public sealed partial class ReaderView
{
    /// <summary>原打印快照复用同一帧/显示租约和效果导出；不截取窗口或加入面板。</summary>
    public async Task<PrintImage> CapturePrintAsync(PrintParameters parameters,CancellationToken token)
    {
        Dispatcher.UIThread.VerifyAccess(); token.ThrowIfCancellationRequested();
        if (_frame is null || _operation?.Book is null || IsBrowsing) throw new NotSupportedException("打印请先切回分页或原帧全景。");
        if(parameters.Mode==PrintMode.RawImage)
        {
            // 首元素的动画探测与捕获后复核也使用解码规格，原尺寸标记须保持到整个捕获结束。
            _exportOriginalSize=true;
            try
            {
                await RefreshAsync(token); token.ThrowIfCancellationRequested();
                var page=GetCopyImagePage()??throw new NotSupportedException("当前首元素不提供原图打印。");
                var png=await CaptureCopyImageAsync(token);var size=_images[page].Bitmap.PixelSize;
                ValidateExportSize(size.Width, size.Height);
                if (parameters.IsBackground)
                {
                    if (_background is not null) await _background.Pending.WaitAsync(token);
                    using var raw = new Bitmap(new MemoryStream(png, false));
                    using var composed = new RenderTargetBitmap(size, new(96,96));
                    using (var context = composed.CreateDrawingContext())
                    {
                        _background?.Render(context, CurrentContentColor, new(size.Width,size.Height));
                        context.DrawImage(raw, new Avalonia.Rect(0,0,size.Width,size.Height));
                    }
                    png = await ImageCopyEncoder.EncodeAsync(composed, token);
                }
                return new(png,size.Width,size.Height,size.Width,size.Height){BackgroundPng=await CapturePrintBackgroundAsync(parameters,token)};
            }
            finally { _exportOriginalSize=false; }
        }
        using var stream=new ImageCopyEncoder.LimitedPngStream();
        await ExportViewAsync(_operation.Frame!,new ExportImageParameter{Mode=ExportImageMode.View,FileFormat=BitmapImageFormat.Png,
            HasBackground=parameters.IsBackground,IsDotKeep=parameters.IsDotScale},stream,token);
        var sizeFrame=_frame.StretchedSize;
        var rect=new Avalonia.Rect(-sizeFrame.Width/2,-sizeFrame.Height/2,sizeFrame.Width,sizeFrame.Height).TransformToAABB(_transform.GetMatrix(false));
        return new(stream.ToArray(),rect.Width,rect.Height,Bounds.Width,Bounds.Height,rect.X,rect.Y){BackgroundPng=await CapturePrintBackgroundAsync(parameters,token)};
    }
    private async Task<byte[]?> CapturePrintBackgroundAsync(PrintParameters parameters,CancellationToken token)
    {
        if(!parameters.IsBackground||_background is null)return null;
        await _background.Pending.WaitAsync(token);token.ThrowIfCancellationRequested();
        var ratio=Math.Min(1,2048/Math.Max(1,Math.Max(Bounds.Width,Bounds.Height)));
        var size=new PixelSize(Math.Max(1,(int)Math.Ceiling(Bounds.Width*ratio)),Math.Max(1,(int)Math.Ceiling(Bounds.Height*ratio)));
        using var background=new RenderTargetBitmap(size,new(96,96));
        using(var context=background.CreateDrawingContext())_background.Render(context,CurrentContentColor,new(size.Width,size.Height));
        return await ImageCopyEncoder.EncodeAsync(background,token);
    }
}

public sealed partial class ReaderView
{
    /// <summary>暂停当前页框全部媒体；退出仅恢复仍由同一查看器持有的播放器。</summary>
    internal IDisposable SuspendPrintMedia()
    {
        var states = _animations.Values.Select(a => (Player: (IMediaPlayer)a.Player, Enabled: a.Player.IsEnabled))
            .Concat(_videos.Values.Select(v => (Player: (IMediaPlayer)v.Player, Enabled: v.Player.IsEnabled))).ToArray();
        foreach (var state in states) state.Player.IsEnabled = false;
        return new PrintMediaLease(() =>
        {
            var live = _animations.Values.Select(a => (IMediaPlayer)a.Player).Concat(_videos.Values.Select(v => (IMediaPlayer)v.Player)).ToHashSet();
            foreach (var state in states) if (live.Contains(state.Player)) state.Player.IsEnabled = state.Enabled;
        });
    }
    private sealed class PrintMediaLease(Action release) : IDisposable
    {
        private Action? _release = release;
        public void Dispose() => Interlocked.Exchange(ref _release, null)?.Invoke();
    }
}
