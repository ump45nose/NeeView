// Copyright (c) NeeLaboratory. 原 BookPageActionControl/PageFileIO 删除分组与确认规则，基线c5c398d89。
namespace NeeView;

public sealed partial class BookOperation
{
    private IPlatformService? _filePlatform;
    private int _deleteBusy;
    /// <summary>原确认宿主，归档不可逆删除始终确认；没有宿主则拒绝。</summary>
    public Func<string, Task<bool>>? ConfirmDeleteAsync { get; set; }
    public bool IsDeletingFile => Volatile.Read(ref _deleteBusy) != 0;
    public bool CanDeleteFile => !IsDeletingFile && FileActionPage is { } page && CanDeletePagesCore([page]);
    /// <summary>原页面列表多选资格，不改变主页Delete的单页范围。</summary>
    public bool CanDeletePages(IReadOnlyList<Page> pages) => !IsDeletingFile && CanDeletePagesCore(pages);
    private bool CanDeletePagesCore(IReadOnlyList<Page> pages) => Config.Current.System.IsFileWriteAccessEnabled && _filePlatform is not null
        && !IsRenamingBook && !IsTransferringBook && !IsUsingClipboard && !_disposed && !_closing && !IsLoading && Book?.IsIndexing == false && _destinationMoves?.IsBusy != true
        && pages.Count > 0 && pages.All(p => Book.Pages.Contains(p)) && GetDeleteEntryType(pages) != DeleteEntryType.Various
        && pages.GroupBy(p => p.ArchiveEntry.Archive).All(g => g.Key.CanDelete(g.Select(p => p.ArchiveEntry).ToArray()));
    /// <summary>原类型判定按代理来源，列表引用不能被当作实体文件。</summary>
    public static DeleteEntryType GetDeleteEntryType(IReadOnlyList<Page> pages)
    {
        var types = pages.Select(p => p.ArchiveEntry is PlaylistArchiveEntry ? DeleteEntryType.PlaylistEntry
            : p.ArchiveEntry.FilePath is not null ? DeleteEntryType.File : DeleteEntryType.ArchiveEntry).Distinct().ToArray();
        return types.Length == 0 ? DeleteEntryType.None : types.Length == 1 ? types[0] : DeleteEntryType.Various;
    }
    /// <summary>装配真实废纸篓和唯一像素工厂，Engine不持有窗口。</summary>
    public void AttachFileDeletion(IPlatformService platform, BitmapFactory images) { _filePlatform = platform; _fileImages = images; }
    /// <summary>原主页Delete只处理当前主页面，瀑布使用显式选择。</summary>
    public Task DeleteFileAsync(CancellationToken token = default) => FileActionPage is { } page
        ? DeletePagesCoreAsync([page], true, token) : Task.CompletedTask;
    /// <summary>页面列表显式选区删除，按来源分组，失败只移除真实成功项。</summary>
    public Task DeletePagesAsync(IReadOnlyList<Page> pages, CancellationToken token = default) => DeletePagesCoreAsync(pages.Distinct().ToArray(), false, token);
    private async Task DeletePagesCoreAsync(IReadOnlyList<Page> pages, bool mainPage, CancellationToken token)
    {
        if (!CanDeletePages(pages) || Interlocked.CompareExchange(ref _deleteBusy, 1, 0) != 0) return;
        var completion = _bookDeleteCompletion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        long generation = _generation;
        try
        {
            using var prompt = CancellationTokenSource.CreateLinkedTokenSource(token, _bookDeleteClosing.Token);
            Notify(); Book requestedBook; PagePosition position; BrowseLayoutMode mode;
            await _gate.WaitAsync(prompt.Token);
            try
            {
                if (!CanDeletePagesCore(pages)) return;
                requestedBook = Book!; generation = _generation; position = Position; mode = BrowseMode;
            }
            finally { _gate.Release(); }
            var snapshots = await ReadDeletePageTargetsAsync(pages, prompt.Token);
            var type = GetDeleteEntryType(pages);
            var message = string.Join("\n", pages.Select(p => p.ArchiveEntry.SystemPath));
            if (type == DeleteEntryType.ArchiveEntry) message = "归档条目将永久移除，不能通过废纸篓恢复。\n" + message;
            if (type == DeleteEntryType.PlaylistEntry) message = "仅移除播放列表登记，保留引用文件。\n" + message;
            if (pages.Any(p => p.ArchiveEntry.IsDirectory)) message = "选中目录包含其中全部内容。\n" + message;
            if ((Config.Current.System.IsRemoveConfirmed || type == DeleteEntryType.ArchiveEntry)
                && (ConfirmDeleteAsync is null || !await ConfirmDeleteAsync(message).WaitAsync(prompt.Token))) return;
            await _gate.WaitAsync(prompt.Token);
            try
            {
                if (!CanDeletePagesCore(pages) || generation != _generation || !ReferenceEquals(requestedBook, Book)
                    || mainPage && (!ReferenceEquals(pages[0], FileActionPage) || position != Position || mode != BrowseMode)) return;
                if (!snapshots.SequenceEqual(await ReadDeletePageTargetsAsync(pages, prompt.Token))) throw new IOException("删除目标在确认期间已改变，请重新确认。");
                Error = null; var removed = new List<ArchiveEntry>(); string? failure = null;
                foreach (var group in pages.GroupBy(p => p.ArchiveEntry.Archive))
                {
                    try
                    {
                        // gate内已核对并授权。关闭等待完成信号，不能在系统提交后隐瞒实际结果；调用方仍可取消未提交操作。
                        var result = await group.Key.DeleteAsync(group.Select(p => p.ArchiveEntry).ToArray(), _filePlatform!, token);
                        removed.AddRange(result.Removed); if (result.Error is not null) { failure = result.Error; break; }
                    }
                    catch (OperationCanceledException) { break; }
                    catch (Exception ex) { failure = ex.Message; break; }
                }
                if (failure is not null) Error = "删除失败：" + failure;
                if (removed.Count == 0) return;
                _saving?.Cancel(); var book = requestedBook; var current = book.CurrentPage; int previous = current?.Index ?? 0;
                bool IsRemoved(Page p) => removed.Any(e => ReferenceEquals(e.Archive, p.ArchiveEntry.Archive)
                    && (e.Id == p.EntryIndex || p.ArchiveEntry is not PlaylistArchiveEntry && e.FilePath is not null && e.FilePath == p.ArchiveEntry.FilePath
                        || p.ArchiveEntry is not PlaylistArchiveEntry && e.IsDirectory && !e.IsShortcut && p.ArchiveEntry.SystemPath.StartsWith(e.SystemPath.TrimEnd('/') + "/", StringComparison.Ordinal)));
                var deleted = book.Pages.SourcePages.Where(IsRemoved).ToArray();
                var next = current is not null && !IsRemoved(current) ? current
                    : book.Pages.Skip(previous + 1).FirstOrDefault(p => !IsRemoved(p)) ?? book.Pages.Take(previous).LastOrDefault(p => !IsRemoved(p));
                book.Pages.SetSourcePages(book.Pages.SourcePages.Where(p => !IsRemoved(p))); await book.SortAsync(CancellationToken.None); _fileSelection = null;
                if (book.Pages.Count == 0) book.CurrentPage = book.Pages.SourcePages.FirstOrDefault(p => p.EntryIndex > (current?.EntryIndex ?? -1)) ?? book.Pages.SourcePages.LastOrDefault();
                Position = new(next is not null && book.Pages.Contains(next) ? next.Index : 0, next == current ? position.Part : 0);
                foreach (var page in deleted) _fileImages?.InvalidatePage(page); _fileImages?.InvalidateCovers();
                await ProbeAroundAsync(book, Position.Index, CancellationToken.None); RebuildFrame(1); RecordPageHistory();
                try { await saveData.SaveAsync(book, keepHistoryOrder: _keepHistoryOrder); }
                catch (Exception ex) { Error = (type == DeleteEntryType.File ? "文件已移至废纸篓" : "条目已删除") + "，阅读状态保存失败，请重试保存：" + ex.Message; ScheduleSave(); }
            }
            finally { _gate.Release(); }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { if (generation == _generation) Error = "删除失败：" + ex.Message; }
        finally { Interlocked.Exchange(ref _deleteBusy, 0); try { Notify(); } finally { completion.TrySetResult(); } }
    }
    /// <summary>确认前后核对真实实体；目录采用现有Profile/卷根保护，链接只删除其自身。</summary>
    private async Task<IReadOnlyList<FolderItem>> ReadDeletePageTargetsAsync(IReadOnlyList<Page> pages, CancellationToken token)
    {
        var results = new List<FolderItem>();
        foreach (var page in pages.Where(p => p.ArchiveEntry is not PlaylistArchiveEntry && p.ArchiveEntry.FilePath is not null))
        {
            var entry = page.ArchiveEntry; var path = entry.FilePath!;
            var info = await archives.GetFileMetadataAsync(path, token) ?? throw new FileNotFoundException("删除目标已不存在。", path);
            if (info.IsSymbolicLink != entry.IsShortcut) throw new IOException("目标链接类型已改变，请重新加载。");
            if (info.IsDirectory && !info.IsSymbolicLink) info = await ReadDeleteBookTargetAsync(path, token);
            results.Add(info);
        }
        return results;
    }
}
