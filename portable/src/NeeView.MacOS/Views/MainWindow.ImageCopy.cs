namespace NeeView.MacOS.Views;

public sealed partial class MainWindow
{
    private IImageClipboard? _imageClipboard;
    private CancellationTokenSource? _imageCopyCancellation;
    private Task _imageCopyAction = Task.CompletedTask;
    private Book? _imageCopyBook;
    private Page? _imageCopyPage;
    /// <summary>唯一启动装配接入图像剪贴板，表现代码不依赖原生后端。</summary>
    public void AttachImageClipboard(IImageClipboard clipboard) { _imageClipboard = clipboard; RefreshHistoryCommandStates(); }
    private bool CanCopyImage => !_preparing && !_closedPrepared && _imageClipboard is not null && _imageCopyAction.IsCompleted && Viewer.CanCopyImage;
    /// <summary>原CopyImage的可等待宿主入口；单槽防重入，关闭等待真实原生结果。</summary>
    private Task CopyImageAsync()
    {
        if (!CanCopyImage) return Task.CompletedTask;
        return _imageCopyAction = RunImageCopyAsync();
    }
    private async Task RunImageCopyAsync()
    {
        await Task.Yield(); using var cancellation = new CancellationTokenSource(); _imageCopyCancellation = cancellation;
        try
        {
            if (_preparing || _closedPrepared) return;
            _imageCopyBook = _model?.Operation.Book; _imageCopyPage = Viewer.CopyImagePage;
            var png = await Viewer.CaptureCopyImageAsync(cancellation.Token);
            cancellation.Token.ThrowIfCancellationRequested();
            if (_preparing || _closedPrepared) return;
            await _imageClipboard!.WritePngAsync(png, cancellation.Token);
        }
        catch (OperationCanceledException) { }
        catch (Exception error) { ShowError("复制图像失败：" + error.Message); }
        finally { if (ReferenceEquals(_imageCopyCancellation, cancellation)) { _imageCopyCancellation = null; _imageCopyBook = null; _imageCopyPage = null; } }
    }
    /// <summary>系统提交仍在排队时，切书/改选也取消旧图；重复刷新同一图像不取消。</summary>
    private void ImageCopy_ReadingChanged(object? sender, EventArgs args)
    {
        if (_imageCopyCancellation is not null && (!ReferenceEquals(_imageCopyBook, _model?.Operation.Book)
            || !ReferenceEquals(_imageCopyPage, Viewer.CopyImagePage))) _imageCopyCancellation.Cancel();
        if (!_preparing && !_closedPrepared) RefreshHistoryCommandStates();
    }
}
