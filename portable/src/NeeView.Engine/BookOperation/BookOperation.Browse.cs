namespace NeeView;

public sealed partial class BookOperation
{
    private readonly SemaphoreSlim _probeSlots = new(2);
    public BrowseLayoutMode BrowseMode => Config.Current.Book.IsPanorama
        ? Config.Current.Book.MacPanoramaLayout == BrowseLayoutMode.Masonry ? BrowseLayoutMode.Masonry : BrowseLayoutMode.Continuous
        : BrowseLayoutMode.Paged;
    public double BrowseScale => BrowseMode == BrowseLayoutMode.Continuous ? Config.Current.Book.MacContinuousScale : Config.Current.Book.MacGalleryColumnWidth;

    /// <summary>切换表现布局并原子保存原Config，不排序页面或重新加载书籍；失败恢复旧模式。</summary>
    /// <param name="mode">分页、纵向连续或瀑布展示；非法枚举值拒绝提交。</param>
    /// <returns>配置保存和当前帧重建完成；失败时恢复旧模式并传播异常。</returns>
    public async Task SetBrowseModeAsync(BrowseLayoutMode mode)
    {
        if (!Enum.IsDefined(mode)) throw new ArgumentOutOfRangeException(nameof(mode));
        await _gate.WaitAsync();
        try
        {
            ObjectDisposedException.ThrowIf(_disposed || _closing, this);
            var bookConfig = Config.Current.Book; var enabled = bookConfig.IsPanorama; var layout = bookConfig.MacPanoramaLayout;
            try
            {
                bookConfig.IsPanorama = mode != BrowseLayoutMode.Paged;
                if (mode != BrowseLayoutMode.Paged) bookConfig.MacPanoramaLayout = mode;
                RebuildFrame(MoveDirection);
                await saveData.SaveAsync(null);
            }
            catch { bookConfig.IsPanorama = enabled; bookConfig.MacPanoramaLayout = layout; RebuildFrame(MoveDirection); throw; }
            Notify();
        }
        finally { _gate.Release(); }
    }

    /// <summary>列宽缩放在原导航锁中提交，沿用阅读防抖保存；表现端不直接写配置。</summary>
    /// <param name="factor">相对缩放倍数，有限正值；原分页变换不受影响。</param>
    /// <returns>锁内缩放提交完成，持久化由原阅读保存链执行。</returns>
    public async Task ScaleBrowseColumnsAsync(double factor)
    {
        if (!double.IsFinite(factor) || factor <= 0) return;
        factor = Math.Clamp(factor, .01, 100);
        await _gate.WaitAsync();
        try
        {
            if (_disposed || _closing || BrowseMode == BrowseLayoutMode.Paged) return;
            if (BrowseMode == BrowseLayoutMode.Continuous) Config.Current.Book.MacContinuousScale *= factor;
            else Config.Current.Book.MacGalleryColumnWidth *= factor;
            ScheduleSave(); Notify();
        }
        finally { _gate.Release(); }
    }
    /// <summary>只探测可见需求的原Page；有界后台读取，不创建新身份或全书预解码。</summary>
    /// <param name="book">发出需求的书籍，切书后拒绝旧需求。</param>
    /// <param name="page">原排序集合中的页面。</param>
    /// <param name="token">可见区域离开/关闭取消。</param>
    /// <returns>尺寸探测完成；损坏页登记错误占位，取消继续向调用方传播。</returns>
    public async Task EnsurePageInfoAsync(Book book, Page page, CancellationToken token)
    {
        if (page.Content.HasSize || !ReferenceEquals(Book, book) || _disposed || _closing) return;
        await _probeSlots.WaitAsync(token);
        try
        {
            if (page.Content.HasSize || !ReferenceEquals(Book, book) || _disposed || _closing) return;
            if (!page.IsImage) { page.Content.HasSize = true; return; }
            await using var stream = await page.ArchiveEntry.Archive.OpenEntryAsync(page.ArchiveEntry, token);
            var info = await decoder.ProbeAsync(stream, token); token.ThrowIfCancellationRequested();
            page.Content.PageDataSource = new(info.Size); page.Content.HasSize = true;
        }
        catch (Exception ex) when (ex is not OperationCanceledException) { page.Content.Error = ex.Message; page.Content.HasSize = true; }
        finally { _probeSlots.Release(); }
    }

    /// <summary>浏览滚动回报原页面锚点；不重复探测邻页，不创建另一本书或第二套位置保存。</summary>
    /// <param name="book">可见布局所属书籍，切书后拒绝回报。</param>
    /// <param name="page">原Page对象；排序后在锁内核对实际索引。</param>
    /// <returns>有效锚点在原导航锁内提交完成，过期回报直接忽略。</returns>
    public async Task ReportBrowsePositionAsync(Book book, Page page)
    {
        await _gate.WaitAsync();
        try
        {
            if (_disposed || _closing || IsLoading || BrowseMode == BrowseLayoutMode.Paged || !ReferenceEquals(Book, book)
                || page.Index < 0 || page.Index >= book.Pages.Count || !ReferenceEquals(book.Pages[page.Index], page) || Position.Index == page.Index) return;
            Position = new(page.Index, 0); RebuildFrame(1); RecordPageHistory(); ScheduleSave(); Notify();
        }
        finally { _gate.Release(); }
    }
}
