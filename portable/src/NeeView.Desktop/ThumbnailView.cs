using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using NeeView.Application;
using NeeView.Core;

namespace NeeView.Desktop;

/// <summary>列表虚拟化缩略图；离开可视树后归还租约。</summary>
public sealed class ThumbnailView : Control
{
    private readonly PageDescriptor _page;
    private readonly IReaderSession _session;
    private readonly IImageRequestScheduler _scheduler;
    private CancellationTokenSource? _cancellation;
    private DecodedImageLease? _lease;
    private WriteableBitmap? _bitmap;
    /// <summary>输入条目及应用接口，建立无持久化依赖的缩略图控件。</summary>
    public ThumbnailView(PageDescriptor page, IReaderSession session, IImageRequestScheduler scheduler)
    { _page = page; _session = session; _scheduler = scheduler; Width = 48; Height = 64; }
    /// <summary>控件可见后提交需求，取消源由加载任务最终释放。</summary>
    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    { base.OnAttachedToVisualTree(e); _cancellation = new(); _ = LoadAsync(_cancellation); }
    /// <summary>只有附着且代次匹配时显示缩略图。</summary>
    private async Task LoadAsync(CancellationTokenSource cancellation)
    {
        DecodedImageLease? lease = null; var generation = _session.Snapshot.Generation;
        try
        {
            if (_session.Source is not { } source) return;
            lease = await _scheduler.RequestAsync(source, new(_page, 96, 128, true), ImagePriority.Thumbnail, cancellation.Token);
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                if (cancellation.IsCancellationRequested || generation != _session.Snapshot.Generation) return;
                _bitmap = new(new(lease.Size.Width, lease.Size.Height), new(96, 96), PixelFormat.Bgra8888, AlphaFormat.Premul);
                using var frame = _bitmap.Lock(); var bytes = lease.Pixels.ToArray();
                for (var row = 0; row < lease.Size.Height; row++) Marshal.Copy(bytes, row * lease.Stride, frame.Address + row * frame.RowBytes, lease.Stride);
                _lease = lease; lease = null; InvalidateVisual();
            });
        }
        catch (OperationCanceledException) { }
        catch (ReaderException) { }
        catch (Exception error) { System.Diagnostics.Trace.WriteLine("thumbnail: " + error.Message); }
        finally
        {
            lease?.Dispose();
            await Dispatcher.UIThread.InvokeAsync(() => { if (_cancellation == cancellation) _cancellation = null; });
            cancellation.Dispose();
        }
    }
    /// <summary>控件移出虚拟化列表时取消需求，并立即归还显示资源。</summary>
    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    { _cancellation?.Cancel(); _cancellation = null; _bitmap?.Dispose(); _lease?.Dispose(); _bitmap = null; _lease = null; base.OnDetachedFromVisualTree(e); }
    /// <summary>输入绘制上下文，只绘制当前有效位图；后台需求不进入绘制线程。</summary>
    public override void Render(DrawingContext context)
    { base.Render(context); if (_bitmap is not null) context.DrawImage(_bitmap, new Rect(Bounds.Size)); }
}
