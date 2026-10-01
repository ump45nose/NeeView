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
    public ThumbnailView(PageDescriptor page, IReaderSession session, IImageRequestScheduler scheduler)
    { _page = page; _session = session; _scheduler = scheduler; Width = 48; Height = 64; }
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
        finally { lease?.Dispose(); }
    }
    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    { _cancellation?.Cancel(); _bitmap?.Dispose(); _lease?.Dispose(); _bitmap = null; _lease = null; base.OnDetachedFromVisualTree(e); }
    public override void Render(DrawingContext context)
    { base.Render(context); if (_bitmap is not null) context.DrawImage(_bitmap, new Rect(Bounds.Size)); }
}
