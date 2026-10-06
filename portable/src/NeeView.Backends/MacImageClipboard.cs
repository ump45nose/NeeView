using AppKit;
using Foundation;
using NeeView;
namespace NeeView.Backends;

/// <summary>原位图复制的NSPasteboard适配；默认系统剪贴板，验收使用独立命名剪贴板。</summary>
public sealed class MacImageClipboard(string? pasteboardName = null) : IImageClipboard
{
    /// <summary>在主线程准备原生图像数据，提交前不清空剪贴板；晚取消不掩盖真实写入。</summary>
    /// <param name="png">调用完成前保持不变的独立PNG快照。</param><param name="token">排队和提交前取消。</param>
    /// <returns>系统接受实际图像；拒绝时通过异常返回。</returns>
    public async Task WritePngAsync(byte[] png, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(png); token.ThrowIfCancellationRequested(); ImageClipboardCodec.ValidatePng(png);
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        int phase = 0;
        using var cancellation = token.Register(() =>
        { if (Interlocked.CompareExchange(ref phase, 2, 0) == 0) completion.TrySetCanceled(token); });
        NSApplication.SharedApplication.BeginInvokeOnMainThread(() =>
        {
            if (Interlocked.CompareExchange(ref phase, 1, 0) != 0) return;
            try
            {
                token.ThrowIfCancellationRequested();
                using var data = NSData.FromArray(png); using var item = new NSPasteboardItem();
                if (!item.SetDataForType(data, ImageClipboardCodec.PngType)) throw new IOException("系统拒绝PNG剪贴板数据。");
                var board = pasteboardName is null ? NSPasteboard.GeneralPasteboard : NSPasteboard.FromName(pasteboardName);
                try
                {
                    token.ThrowIfCancellationRequested();
                    // ClearContents 是系统提交点；清空与写入在同一主线程回调完成，此后晚取消不能拆开提交。
                    board.ClearContents();
                    if (!board.WriteObjects([item])) throw new IOException("复制图像到系统剪贴板失败。");
                }
                finally { if (pasteboardName is not null) board.Dispose(); }
                completion.TrySetResult();
            }
            catch (OperationCanceledException) { completion.TrySetCanceled(token); }
            catch (Exception error) { completion.TrySetException(error); }
        });
        await completion.Task.ConfigureAwait(false);
    }
}
