// Copyright (c) NeeLaboratory. 原 BookshelfItemAccessor.Name / FolderItem.RenameAsync 的平台适配，MIT。
namespace NeeView;

public sealed partial class BookOperation
{
    /// <summary>改名书架实体，复用原忙碌/退出等待、来源释放和 JSON 路径恢复链；书签只改节点名称。</summary>
    /// <param name="item">调用时捕获的原书架项。</param><param name="name">同一父目录内的新名称。</param>
    /// <param name="token">准备可取消，实体成功后的状态联动不再取消。</param>
    /// <returns>实际成功路径的元数据；失败抛给脚本，取消不改变访问器。</returns>
    public async Task<FolderItem> RenameFolderItemAsync(FolderItem item, string name, CancellationToken token = default)
    {
        token.ThrowIfCancellationRequested();
        if (item.Bookmark is { } node)
        {
            await saveData.RenameBookmarkAsync(node, name, token);
            return item with { Name = node.DisplayName };
        }
        if (_fileBackend is not IBookRenameBackend backend) throw new NotSupportedException("当前来源不支持实体改名。");
        if (!Config.Current.System.IsFileWriteAccessEnabled) throw new UnauthorizedAccessException("文件写入权限未开启。");
        if (_disposed || _closing || IsLoading || IsTransferringBook || IsDeletingFile || IsUsingClipboard || _destinationMoves?.IsBusy == true
            || Interlocked.CompareExchange(ref _renameBusy, 1, 0) != 0) throw new InvalidOperationException("文件操作或加载正在进行。");
        var completion = _renameCompletion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        using var pending = CancellationTokenSource.CreateLinkedTokenSource(token, _renameClosing.Token);
        BookRenamePlan? plan = null; BookMemento? memory = null; string? search = null;
        bool prepared = false, renamed = false, released = false; long generation = 0;
        Exception? failure = null; FolderItem result = item;
        try
        {
            Notify(); await _gate.WaitAsync(pending.Token);
            try
            {
                if (_disposed || _closing || IsLoading || IsTransferringBook || IsDeletingFile || IsUsingClipboard || _destinationMoves?.IsBusy == true)
                    throw new InvalidOperationException("文件操作或加载正在进行。");
                await ProtectBookTransferDestinationAsync(item.Path, pending.Token);
                var target = await backend.GetRenameTargetAsync(item.Path, pending.Token);
                plan = await backend.PlanRenameAsync(target, name, pending.Token);
                if (plan.Destination == target.Path) return item;
                await ProtectBookTransferDestinationAsync(plan.Destination, pending.Token);
                // 原脚本 Name setter 不打开交互确认；后端生成不覆盖的真实落点。
                var book = Book;
                bool affectsSource = book is not null && (BookMementoTools.RenamePath(book.Path, target.Path, plan.Destination) != book.Path
                    || book.Pages.SourcePages.Any(p => p.ArchiveEntry.FilePath == target.Path));
                if (book is not null) await saveData.SaveAsync(book, pending.Token, keepHistoryOrder: true);
                await saveData.PrepareBookRenameAsync(plan, pending.Token); prepared = true;
                pending.Token.ThrowIfCancellationRequested(); _renameCommitting = true; _saving?.Cancel();
                if (affectsSource)
                {
                    memory = book!.CreateMemento(); search = book.Pages.SearchKeyword;
                    _opening?.Cancel(); generation = Interlocked.Increment(ref _generation);
                    foreach (var page in book.Pages.SourcePages) _fileImages?.InvalidatePage(page);
                    await book.DisposeAsync(); released = true;
                    Book = null; Frame = null; Context = null; _fileSelection = null; Notify();
                }
                try { await backend.RenameAsync(plan, pending.Token); renamed = true; }
                catch
                {
                    // 系统可能已改名才报告失败；可靠确认成功后继续状态提交，模糊情况保留marker。
                    renamed = await backend.WasRenamedAsync(plan, CancellationToken.None) == true;
                    if (!renamed) throw;
                }
                PageHistory.RenameRecursive(target.Path, plan.Destination); BookHistory.RenameRecursive(target.Path, plan.Destination);
                await saveData.RenameBookPathsAsync(plan); _fileImages?.InvalidateCovers();
            }
            finally { _gate.Release(); }
            result = item with { Path = plan.Destination, Name = System.IO.Path.GetFileName(plan.Destination) };
            try { result = await archives.GetFileMetadataAsync(plan.Destination, CancellationToken.None) ?? result; }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or TimeoutException) { /* 保留原元数据及实际成功地址。 */ }
        }
        catch (Exception ex) { failure = ex; }
        finally
        {
            try
            {
                // 后端异常可能发生在提交后；仅确证未改名才删除恢复记录。
                if (prepared && !renamed && plan is not null && await backend.WasRenamedAsync(plan, CancellationToken.None) == false) saveData.CancelBookRename();
                if (released && memory is not null && plan is not null && !_disposed && !_closing && generation == _generation)
                {
                    var originalPath = memory.Path;
                    if (renamed)
                    {
                        memory.Path = BookMementoTools.RenamePath(memory.Path, plan.Target.Path, plan.Destination);
                        if (!string.IsNullOrEmpty(memory.Page) && System.IO.Path.Combine(originalPath, memory.Page) == plan.Target.Path)
                            memory.Page = System.IO.Path.GetFileName(plan.Destination);
                    }
                    await OpenCoreAsync(memory.Path, CancellationToken.None, entryName: string.IsNullOrEmpty(memory.Page) ? null : memory.Page,
                        keepHistoryOrder: true, startupMemento: memory, pageSearchKeyword: search, renamed: renamed);
                }
                if (renamed && !_disposed && !_closing && _bookshelf is { } shelf && plan is not null && shelf.Place is { } place)
                    await shelf.SetPlaceAsync(BookMementoTools.RenamePath(place, plan.Target.Path, plan.Destination), selectedPath: plan.Destination, force: true);
            }
            catch (Exception ex) { failure = failure is null ? ex : new AggregateException(failure, ex); }
            finally
            {
                _renameCommitting = false; Interlocked.Exchange(ref _renameBusy, 0);
                try { Notify(); } finally { completion.TrySetResult(); }
            }
        }
        if (failure is not null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
        return result;
    }
}
