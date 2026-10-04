// Copyright (c) NeeLaboratory. 原 BookPageActionControl 当前主页删除与确认规则，基线 c5c398d89。
namespace NeeView;

public sealed partial class BookOperation
{
    private IPlatformService? _filePlatform;
    private int _deleteBusy;
    /// <summary>当前主页删除的确认宿主；默认确认开启而宿主缺失时拒绝执行。</summary>
    public Func<string, Task<bool>>? ConfirmDeleteAsync { get; set; }
    public bool IsDeletingFile => Volatile.Read(ref _deleteBusy) != 0;
    /// <summary>沿原写权限开关，只开放已完成索引的普通真实图片；归档、书籍页及链接继续占位。</summary>
    public bool CanDeleteFile => !IsDeletingFile && CanDeleteFileCore();
    private bool CanDeleteFileCore() => Config.Current.System.IsFileWriteAccessEnabled && _filePlatform is not null
        && !IsRenamingBook && !_disposed && !_closing && !IsLoading && Book?.IsIndexing == false && _destinationMoves?.IsBusy != true
        && FileActionPage is { IsImage: true, ArchiveEntry.FilePath: not null, ArchiveEntry.IsShortcut: false } page
        && page.ArchiveEntry.Archive.IsDirectory;

    /// <summary>启动装配或宿主接入系统废纸篓与同一图像工厂，业务不持有窗口对象。</summary>
    /// <param name="platform">实际系统操作替换点，必须在失败时传播错误。</param>
    /// <param name="images">唯一像素工厂，删除后回收该页及晚到需求。</param>
    public void AttachFileDeletion(IPlatformService platform, BitmapFactory images)
    { _filePlatform = platform; _fileImages = images; }

    /// <summary>原DeleteFile只处理当前主页，双页不扩大选区，瀑布使用显式选择。</summary>
    /// <param name="token">确认前和系统调用前可取消；系统成功后仍完成索引/阅读保存协调。</param>
    /// <returns>确认、系统结果及原阅读状态协调完成的任务；取消/失败不移除页面。</returns>
    public async Task DeleteFileAsync(CancellationToken token = default)
    {
        if (!CanDeleteFile || Interlocked.CompareExchange(ref _deleteBusy, 1, 0) != 0) return;
        try
        {
            Notify(); token.ThrowIfCancellationRequested();
            Book requestedBook; Page requestedPage; long generation; PagePosition position; BrowseLayoutMode mode;
            await _gate.WaitAsync(token);
            try
            {
                if (!CanDeleteFileCore()) return;
                requestedBook = Book!; requestedPage = FileActionPage!;
                generation = _generation; position = Position; mode = BrowseMode;
            }
            finally { _gate.Release(); }
            string path = requestedPage.ArchiveEntry.FilePath!;
            // 原确认可关闭；无不可逆回退，因此NAS不支持废纸篓时直接失败。
            // 确认不占导航锁，返回后必须重新核对捕获的书籍、页面和位置。
            if (Config.Current.System.IsRemoveConfirmed && (ConfirmDeleteAsync is null || !await ConfirmDeleteAsync(path))) return;
            await _gate.WaitAsync(token);
            try
            {
                if (!CanDeleteFileCore() || generation != _generation || !ReferenceEquals(requestedBook, Book)
                    || !ReferenceEquals(requestedPage, FileActionPage) || position != Position || mode != BrowseMode) return;
                Error = null;
                try { await _filePlatform!.TrashAsync(path, token); }
                catch (OperationCanceledException) { return; }
                catch (Exception ex) { Error = "移至废纸篓失败：" + ex.Message; return; }

                _saving?.Cancel();
                // 与原预移除+失败重载相比，按真实成功结果移除，失败无需破坏当前页/搜索。
                var book = requestedBook; int previous = requestedPage.Index;
                var next = book.Pages.Skip(previous + 1).FirstOrDefault() ?? book.Pages.Take(previous).LastOrDefault();
                book.Pages.SetSourcePages(book.Pages.SourcePages.Where(page => !ReferenceEquals(page, requestedPage)));
                book.Sort(CancellationToken.None); _fileSelection = null;
                if (book.Pages.Count == 0)
                    book.CurrentPage = book.Pages.SourcePages.FirstOrDefault(page => page.EntryIndex > requestedPage.EntryIndex) ?? book.Pages.SourcePages.LastOrDefault();
                Position = new(next is not null && book.Pages.Contains(next) ? next.Index : 0, 0);
                _fileImages?.InvalidatePage(requestedPage); _fileImages?.InvalidateCovers();
                await ProbeAroundAsync(book, Position.Index, CancellationToken.None); RebuildFrame(1); RecordPageHistory();
                try { await saveData.SaveAsync(book, keepHistoryOrder: _keepHistoryOrder); }
                catch (Exception ex) { Error = "文件已移至废纸篓，阅读状态保存失败，请重试保存：" + ex.Message; ScheduleSave(); }
            }
            finally { _gate.Release(); }
        }
        catch (OperationCanceledException) { }
        finally { Interlocked.Exchange(ref _deleteBusy, 0); Notify(); }
    }
}
