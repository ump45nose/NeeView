using AppKit;
using Foundation;
using ImageMagick;
using NeeView.Backends;
namespace NeeView.Backends.MacOS.Tests;

/// <summary>实际NSPasteboard图像协议；命名剪贴板隔离，不触碰用户GeneralPasteboard。</summary>
public sealed class ImageClipboardNativeTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;
    private static readonly byte[] Png = CreatePng();
    private static byte[] CreatePng() { using var image = new MagickImage(MagickColors.Red, 1, 1); return image.ToByteArray(MagickFormat.Png); }
    [Fact]
    public async Task ActualPngBytesAndSingleImageTypeRoundTripOnIsolatedBoard()
    {
        var name = "net.neeview.tests.image." + Guid.NewGuid().ToString("N");
        try
        {
            await new MacImageClipboard(name).WritePngAsync(Png, Token);
            await OnMain(() =>
            {
                using var board = NSPasteboard.FromName(name); using var data = board.GetDataForType(ImageClipboardCodec.PngType);
                Assert.Equal(Png, data!.ToArray()); Assert.Single(board.PasteboardItems!); Assert.Contains(ImageClipboardCodec.PngType, board.Types!);
                using var bitmap = new NSBitmapImageRep(data); Assert.Equal(1, bitmap.PixelsWide); Assert.Equal(1, bitmap.PixelsHigh);
                Assert.DoesNotContain("public.file-url", board.Types!); Assert.DoesNotContain(FileClipboardCodec.QueryPathsType, board.Types!);
            });
        }
        finally { await Clear(name); }
    }
    [Fact]
    public async Task InvalidAndAlreadyCanceledRequestsKeepExistingBoardContents()
    {
        var name = "net.neeview.tests.image." + Guid.NewGuid().ToString("N");
        try
        {
            var clipboard = new MacImageClipboard(name); await clipboard.WritePngAsync(Png, Token);
            using var canceled = new CancellationTokenSource(); canceled.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => clipboard.WritePngAsync(Png, canceled.Token));
            await Assert.ThrowsAsync<ArgumentException>(() => clipboard.WritePngAsync([1, 2, 3], Token));
            await OnMain(() => { using var board = NSPasteboard.FromName(name); using var data = board.GetDataForType(ImageClipboardCodec.PngType); Assert.Equal(Png, data!.ToArray()); });
        }
        finally { await Clear(name); }
    }
    [Fact]
    public async Task CanceledQueuedCallbackCannotClearOrPublishAfterCompletion()
    {
        var name = "net.neeview.tests.image." + Guid.NewGuid().ToString("N");
        using var release = new ManualResetEventSlim();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        try
        {
            var clipboard = new MacImageClipboard(name); await clipboard.WritePngAsync(Png, Token);
            NSApplication.SharedApplication.BeginInvokeOnMainThread(() => { entered.TrySetResult(); release.Wait(TimeSpan.FromSeconds(10)); });
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(10), Token);
            using var canceled = new CancellationTokenSource();
            var pending = clipboard.WritePngAsync(Png, canceled.Token); canceled.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending); release.Set();
            // 此读取排在旧写回调后，验证取消完成后不会晚到清空/替换已有图像。
            await OnMain(() => { using var board = NSPasteboard.FromName(name); using var data = board.GetDataForType(ImageClipboardCodec.PngType); Assert.Equal(Png, data!.ToArray()); });
        }
        finally { release.Set(); await Clear(name); }
    }
    private static Task Clear(string name) => OnMain(() => { using var board = NSPasteboard.FromName(name); board.ClearContents(); board.ReleaseGlobally(); });
    private static Task OnMain(Action action)
    {
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        NSApplication.SharedApplication.BeginInvokeOnMainThread(() => { try { action(); completion.TrySetResult(); } catch (Exception ex) { completion.TrySetException(ex); } });
        return completion.Task.WaitAsync(TimeSpan.FromSeconds(10), Token);
    }
}
