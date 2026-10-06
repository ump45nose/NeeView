using Avalonia.Media.Imaging;
using Avalonia.Threading;
namespace NeeView.MacOS.Views;

public sealed partial class ReaderView
{
    /// <summary>原ViewContents.First语义：选中原帧的首元素，不按显示方向反转，不跳过dummy/非图片。</summary>
    private Page? GetCopyImagePage()
    {
        if (_disposed || _operation?.IsLoading != false || _operation.Book is not { } book) return null;
        if (IsBrowsing) return _browse?.CopyImagePage;
        if (!ReferenceEquals(book, _displayBook) || _frame?.FrameRange != _operation.Frame?.FrameRange) return null;
        var first = _frame?.Elements.FirstOrDefault();
        // 原动画/归档/文件strategy没有BitmapSource；禁用不能冒充复制当前动画帧或封面。
        if (first is not { IsDummy: false, Page.IsImage: true } || _animations.ContainsKey(first.Page)
            || _pageErrors.ContainsKey(first.Page) || !_images.ContainsKey(first.Page)) return null;
        // 动画类型尚在探测/首帧准备时，不能把短暂静态fallback当作原静态strategy。
        if (Config.Current.Image.Standard.IsAnimationEnabled(first.Page.EntryName)
            && _animationAttempts.GetValueOrDefault(first.Page) != (GetRequest(first.Page), first.Page.ArchiveEntry.Length, first.Page.ArchiveEntry.LastWriteTime)) return null;
        return first.Page;
    }
    public bool CanCopyImage => GetCopyImagePage() is not null;
    internal Page? CopyImagePage => GetCopyImagePage();

    /// <summary>捕获并保留当前图像源，在后台编码；晚到/切书/选中项变化拒绝返回旧快照。</summary>
    /// <param name="token">编码前后和发布前的取消。</param><returns>PNG独立快照；没有有效图像或结果过期时抛出。</returns>
    public async Task<byte[]> CaptureCopyImageAsync(CancellationToken token)
    {
        Dispatcher.UIThread.VerifyAccess(); token.ThrowIfCancellationRequested();
        var page = GetCopyImagePage() ?? throw new InvalidOperationException("当前没有可复制的图像源。");
        var book = _operation!.Book;
        Bitmap bitmap; IDisposable retained;
        if (IsBrowsing) (bitmap, retained) = _browse!.RetainCopyImage(page);
        else { var display = _images[page].Retain(); bitmap = display.Bitmap; retained = display; }
        try
        {
            var png = await ImageCopyEncoder.EncodeAsync(bitmap, token);
            token.ThrowIfCancellationRequested();
            if (_disposed || !ReferenceEquals(book, _operation.Book) || !ReferenceEquals(page, GetCopyImagePage()))
                throw new OperationCanceledException("复制期间当前图像已改变。", token);
            return png;
        }
        finally { retained.Dispose(); }
    }
}
