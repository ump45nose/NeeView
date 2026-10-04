// Copyright (c) NeeLaboratory. 原BookControl.DeleteBook/NextFolderListBookLoader，基线c5c398d89。
namespace NeeView;

public sealed partial class BookOperation
{
    private CancellationTokenSource _bookDeleteClosing = new();
    private TaskCompletionSource? _bookDeleteCompletion;
    private bool _bookDeleteCommitting;
    private string? _deletedLastBookPath;
    /// <summary>宿主只采集整书确认；目录意味着整个书籍目录，不是当前图片。</summary>
    public Func<string, Task<bool>>? ConfirmDeleteBookAsync { get; set; }
    public bool CanDeleteBook => !IsDeletingFile && CanDeleteBookCore();
    /// <summary>保留原实体书/写权限资格，拒绝应用临时书、卷根及包含Profile的目录。</summary>
    /// <returns>廉价定位资格；提交前仍须由后端复核实体与实际路径。</returns>
    private bool CanDeleteBookCore() => Config.Current.System.IsFileWriteAccessEnabled && _filePlatform is not null
        && !IsRenamingBook && !IsTransferringBook && !IsUsingClipboard && !_disposed && !_closing && !IsLoading && _destinationMoves?.IsBusy != true
        && Book is { IsIndexing: false } book && book.Path == book.Source.RootArchivePath && !saveData.IsTemporaryPath(book.Path)
        && System.IO.Path.GetDirectoryName(book.Path) is not null && book.Path != "/Volumes"
        && System.IO.Path.GetDirectoryName(book.Path) != "/Volumes"
        && saveData.DirectoryPath != book.Path && !saveData.DirectoryPath.StartsWith(book.Path.TrimEnd('/') + "/", StringComparison.Ordinal);

    /// <summary>原DeleteBook移走整本真实目录/根文件；成功后沿原书架邻项打开，不删除历史或书签记录。</summary>
    /// <param name="token">确认/保存和系统提交前可取消；真实成功后继续协调状态。</param>
    /// <returns>确认、来源释放、废纸篓结果及恢复/下一书加载的完成任务。</returns>
    public async Task DeleteBookAsync(CancellationToken token = default)
    {
        if (!CanDeleteBook || Interlocked.CompareExchange(ref _deleteBusy, 1, 0) != 0) return;
        var completion = _bookDeleteCompletion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        Book? requested = null; BookMemento? memory = null; string? search = null, failure = null, nextPath = null, place = null;
        long generation = 0, actionGeneration = 0; bool released = false, trashed = false, wasLocked = false;
        try
        {
            using var prompt = CancellationTokenSource.CreateLinkedTokenSource(token, _bookDeleteClosing.Token);
            Notify(); await _gate.WaitAsync(prompt.Token);
            try { if (!CanDeleteBookCore()) return; requested = Book!; generation = _generation; wasLocked = IsBookLocked; }
            finally { _gate.Release(); }
            var target = await ReadDeleteBookTargetAsync(requested.Path, prompt.Token);
            if (Config.Current.System.IsRemoveConfirmed && (ConfirmDeleteBookAsync is null || !await ConfirmDeleteBookAsync(target.Path).WaitAsync(prompt.Token))) return;
            await _gate.WaitAsync(prompt.Token);
            try
            {
                if (!CanDeleteBookCore() || generation != _generation || !ReferenceEquals(requested, Book)) return;
                var current = await ReadDeleteBookTargetAsync(target.Path, prompt.Token);
                if (target != current) throw new IOException("书籍在确认期间已改变，请重新确认。");
                memory = requested.CreateMemento(); search = requested.Pages.SearchKeyword;
                place = _bookshelf?.Place;
                // 原邻项在实体操作前捕获：下一项优先，末项退前；未在列表时回退选择。
                if (Config.Current.Bookshelf.IsOpenNextBookWhenRemove && _bookshelf is { } shelf)
                { nextPath = shelf.GetNextItem(requested.Path, true)?.Path; }
                if (nextPath == requested.Path || nextPath?.StartsWith(requested.Path.TrimEnd('/') + "/", StringComparison.Ordinal) == true) nextPath = null;
                _saving?.Cancel(); await saveData.SaveAsync(requested, prompt.Token, keepHistoryOrder: true);
                prompt.Token.ThrowIfCancellationRequested();
                _bookDeleteCommitting = true; _opening?.Cancel(); actionGeneration = Interlocked.Increment(ref _generation);
                foreach (var page in requested.Pages.SourcePages) _fileImages?.InvalidatePage(page);
                _fileImages?.InvalidateCovers();
                await requested.DisposeAsync(); released = true;
                Book = null; Frame = null; Context = null; Position = PagePosition.Zero; _fileSelection = null; IsBookLocked = false;
                PageSelector.Synchronize(null, 0); RefreshMarkers(); Notify();
                // 关闭只取消准备，授权后的系统调用必须等真实结果；用户取消仍由平台提交点裁决。
                await _filePlatform!.TrashAsync(target.Path, token); trashed = true; _deletedLastBookPath = target.Path;
                try { await SaveCurrentReadingAsync(); }
                catch (Exception ex) { failure = "书籍已移至废纸篓，启动状态保存失败，请重试保存：" + ex.Message; ScheduleSave(); }
            }
            finally { _gate.Release(); }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { failure = (trashed ? "书籍已移至废纸篓，但后续协调失败：" : "书籍移至废纸篓失败：") + ex.Message; }
        finally
        {
            try
            {
                if (released && memory is not null && !_disposed && !_closing && actionGeneration == _generation)
                {
                    if (!trashed)
                    {
                        // 不复用已关闭Archive；原恢复设置/条目/搜索，取消和失败不擅自导航下一书。
                        var expectedGeneration = actionGeneration + 1;
                        await OpenCoreAsync(memory.Path, CancellationToken.None, entryName: string.IsNullOrEmpty(memory.Page) ? null : memory.Page,
                            keepHistoryOrder: true, startupMemento: memory, pageSearchKeyword: search);
                        if (expectedGeneration == _generation) { actionGeneration = expectedGeneration; IsBookLocked = wasLocked; }
                        if (Book?.Path != memory.Path) failure = (failure is null ? "重新打开失败：" : failure + "\n重新打开失败：") + Error;
                    }
                    else if (_bookshelf is { } shelf && shelf.Place == place)
                    {
                        await shelf.RefreshAsync();
                        if (shelf.Error is { } error) failure = (failure is null ? "书籍已移至废纸篓，书架刷新失败：" : failure + "\n书架刷新失败：") + error;
                        else if (!_disposed && !_closing && actionGeneration == _generation && Config.Current.Bookshelf.IsOpenNextBookWhenRemove
                            && nextPath is not null && shelf.Items.Any(item => item.Path == nextPath))
                        {
                            var expectedGeneration = actionGeneration + 1;
                            await OpenCoreAsync(nextPath, CancellationToken.None);
                            if (expectedGeneration == _generation) actionGeneration = expectedGeneration;
                            if (Book?.Path == nextPath) shelf.Select(shelf.Items.FirstOrDefault(item => item.Path == nextPath));
                            else failure = (failure is null ? "书籍已移至废纸篓，下一书打开失败：" : failure + "\n下一书打开失败：") + Error;
                        }
                    }
                }
            }
            catch (Exception ex) { failure = (failure is null ? "书籍删除后的恢复失败：" : failure + "\n") + ex.Message; }
            finally
            {
                _bookDeleteCommitting = false; Interlocked.Exchange(ref _deleteBusy, 0);
                // 新打开请求优先；旧删除结果不能覆盖新书错误。
                if (failure is not null && (released ? Book?.Path == memory?.Path || actionGeneration == _generation : generation == _generation)) Error = failure;
                try { Notify(); } finally { completion.TrySetResult(); }
            }
        }
    }

    /// <summary>确认前后均核对实体元数据；链接和根地址不交给普通整书删除。</summary>
    /// <param name="path">捕获的当前书籍根地址。</param><param name="token">准备/确认阶段取消。</param>
    /// <returns>通过实际路径保护的实体快照；不提供跨进程原子身份保证。</returns>
    private async Task<FolderItem> ReadDeleteBookTargetAsync(string path, CancellationToken token)
    {
        var target = await archives.GetFileMetadataAsync(path, token) ?? throw new FileNotFoundException("书籍实体已不存在。", path);
        if (target.IsSymbolicLink) throw new NotSupportedException("链接的整书删除尚未迁移。");
        var physical = await archives.GetPhysicalPathAsync(path, token);
        var profile = await archives.GetPhysicalPathAsync(saveData.DirectoryPath, token);
        if (System.IO.Path.GetDirectoryName(physical) is null || physical == "/Volumes" || System.IO.Path.GetDirectoryName(physical) == "/Volumes")
            throw new NotSupportedException("不能删除卷根目录。");
        if (profile == physical || profile.StartsWith(physical.TrimEnd('/') + "/", StringComparison.Ordinal))
            throw new NotSupportedException("不能删除 Profile 目录或包含它的目录。");
        if (saveData.TemporaryDirectoryPath is { } temporary)
        {
            var temporaryRoot = await archives.GetPhysicalPathAsync(temporary, token);
            if (physical == temporaryRoot || physical.StartsWith(temporaryRoot.TrimEnd('/') + "/", StringComparison.Ordinal))
                throw new NotSupportedException("应用临时书不能作为实体书删除。");
        }
        return target;
    }
    /// <summary>在原保存事务重试已删除书的LastBook清除；新书保存仍以新书快照为准。</summary>
    /// <param name="token">原保存取消。</param><param name="limits">设置表单的历史限制副本。</param>
    /// <returns>启动目标清除及既有重命名恢复记录刷新的完成任务。</returns>
    private async Task SaveCurrentReadingAsync(CancellationToken token = default, (int Size, TimeSpan Span)? limits = null)
    {
        var book = Book; var deleted = _deletedLastBookPath;
        await saveData.SaveAsync(book, token, _keepHistoryOrder, limits, clearLastBook: book is null && deleted is not null && saveData.LastBookPath == deleted);
        if (_deletedLastBookPath == deleted) _deletedLastBookPath = null;
        await saveData.FlushBookRenameAsync();
    }
}
