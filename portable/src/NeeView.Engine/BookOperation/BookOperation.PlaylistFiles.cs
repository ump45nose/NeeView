// Copyright (c) NeeLaboratory. 原 PlaylistHub 文件管理，复用现有改名恢复/阅读生命周期。
namespace NeeView;

public sealed partial class BookOperation
{
    /// <summary>列表文件动作使用现有实体后端及忙碌标志；条目编辑不受此实体写权限影响。</summary>
    public bool CanManagePlaylistFile => Config.Current.System.IsFileWriteAccessEnabled && _fileBackend is IBookRenameBackend
        && !_disposed && !_closing && !IsLoading && !IsRenamingBook && !IsDeletingFile && !IsTransferringBook
        && !IsUsingClipboard && _destinationMoves?.IsBusy != true && Playlists.Current is not null;
    public bool CanDeletePlaylistFile => CanManagePlaylistFile && _filePlatform is not null && Playlists.CanDeleteFile;

    /// <summary>原列表名输入不含扩展名；保留原 TrimStart 和同名编号算法。</summary>
    /// <param name="expected">输入前捕获的列表引用。</param><param name="name">用户输入的新列表名称。</param>
    /// <param name="token">提交前取消，实体成功后必须完成联动。</param>
    public Task RenamePlaylistFileAsync(Playlist expected, string name, CancellationToken token = default)
        => ChangePlaylistFileAsync(expected, name, delete: false, token);

    /// <summary>确认后移至系统废纸篓；只删除列表文件，引用图片保持。</summary>
    /// <param name="expected">确认前捕获的列表引用。</param><param name="token">提交前取消。</param>
    public Task DeletePlaylistFileAsync(Playlist expected, CancellationToken token = default)
        => ChangePlaylistFileAsync(expected, null, delete: true, token);

    /// <summary>沿唯一阅读导航锁协调列表实体与当前打开的列表书；不另建文件事务。</summary>
    private async Task ChangePlaylistFileAsync(Playlist expected, string? name, bool delete, CancellationToken token)
    {
        if (!(delete ? CanDeletePlaylistFile : CanManagePlaylistFile) || Interlocked.CompareExchange(ref _renameBusy, 1, 0) != 0) return;
        var completion = _renameCompletion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        BookMemento? memory = null; string? search = null; long restoreGeneration = 0;
        BookRenamePlan? plan = null; bool prepared = false, committed = false, released = false;
        Exception? failure = null;
        try
        {
            using var preparation = CancellationTokenSource.CreateLinkedTokenSource(token, _renameClosing.Token);
            var backend = (IBookRenameBackend)_fileBackend!;
            var target = await backend.GetRenameTargetAsync(expected.Path, preparation.Token);
            if (!delete)
            {
                if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("请输入播放列表名称。");
                plan = await backend.PlanRenameAsync(target, name.TrimStart() + System.IO.Path.GetExtension(expected.Path), preparation.Token);
                if (plan.Destination == target.Path) return;
            }
            await _gate.WaitAsync(preparation.Token);
            try
            {
                if (_disposed || _closing || IsLoading || !ReferenceEquals(expected, Playlists.Current))
                    throw new InvalidOperationException("播放列表或阅读状态已改变，请重新操作。");
                if (target != await backend.GetRenameTargetAsync(expected.Path, preparation.Token))
                    throw new IOException("播放列表文件已改变，请重新操作。");
                _saving?.Cancel();
                if (Book is { } current && current.Path == expected.Path)
                {
                    memory = current.CreateMemento(); search = current.Pages.SearchKeyword;
                    await saveData.SaveAsync(current, preparation.Token, keepHistoryOrder: true);
                }
                if (plan is not null) { await saveData.PrepareBookRenameAsync(plan, preparation.Token); prepared = true; }
                preparation.Token.ThrowIfCancellationRequested(); _renameCommitting = true;
                if (memory is not null && Book is { } currentBook)
                {
                    _opening?.Cancel(); restoreGeneration = Interlocked.Increment(ref _generation);
                    foreach (var page in currentBook.Pages.SourcePages) _fileImages?.InvalidatePage(page);
                    _fileImages?.InvalidateCovers(); await currentBook.DisposeAsync(); released = true;
                    Book = null; Frame = null; Context = null; Position = PagePosition.Zero; _fileSelection = null;
                    PageSelector.Synchronize(null, 0); RefreshMarkers(); Notify();
                }
                if (delete)
                {
                    Exception? deletionError = null;
                    try { await Playlists.DeleteFileAsync(expected, _filePlatform!, token); }
                    catch(Exception error) { deletionError = error; }
                    finally { committed = !ReferenceEquals(Playlists.Current, expected); if (committed) _deletedLastBookPath = expected.Path; }
                    // 默认源损坏也不能留着已删除的启动目标；真实成功立即保存，失败沿原防抖/退出重试。
                    if (committed)
                    {
                        try { await SaveCurrentReadingAsync(); }
                        catch(Exception error) { ScheduleSave(); deletionError = deletionError is null ? error : new AggregateException(deletionError,error); }
                    }
                    if (deletionError is not null) throw deletionError;
                }
                else
                {
                    await Playlists.RenameFileAsync(expected, plan!, backend, token); committed = true;
                    PageHistory.RenameRecursive(plan!.Target.Path, plan.Destination); BookHistory.RenameRecursive(plan.Target.Path, plan.Destination);
                    await saveData.RenameBookPathsAsync(plan);
                }
            }
            finally { _gate.Release(); }
        }
        catch (Exception error) { failure = error; }
        finally
        {
            try
            {
                if (prepared && !committed) saveData.CancelBookRename();
                if (released && memory is not null && !_disposed && !_closing && restoreGeneration == _generation && (!delete || !committed))
                {
                    memory.Path = committed ? plan!.Destination : memory.Path;
                    await OpenCoreAsync(memory.Path, CancellationToken.None, entryName: string.IsNullOrEmpty(memory.Page) ? null : memory.Page,
                        keepHistoryOrder: true, startupMemento: memory, pageSearchKeyword: search, expectedGeneration: restoreGeneration);
                    if (Book?.Path != memory.Path && restoreGeneration + 1 == _generation) throw new IOException(Error ?? "播放列表书籍重新打开失败。");
                }
            }
            catch (Exception error) { failure = failure is null ? error : new AggregateException(failure, error); }
            finally { _renameCommitting = false; Interlocked.Exchange(ref _renameBusy, 0); completion.TrySetResult(); Notify(); }
        }
        if (failure is not null) throw new IOException(committed ? "文件操作已成功，后续状态协调未完成，请重试保存。" : "播放列表文件操作失败。", failure);
    }

    /// <summary>存在检查使用原归档工厂；失联和无权限不会被判断为无效登记。</summary>
    public Task<PlaylistInvalidPlan> PlanInvalidPlaylistItemsAsync(CancellationToken token = default) => Playlists.PlanInvalidAsync(archives.ExistsAsync, token);
    /// <summary>确认后复查原计划，最近删除批次仍可使用原 Restore 恢复。</summary>
    public Task<int> RemoveInvalidPlaylistItemsAsync(PlaylistInvalidPlan plan, CancellationToken token = default) => Playlists.RemoveInvalidAsync(plan, archives.ExistsAsync, token);
    /// <summary>先等待列表编辑落盘，再沿唯一来源打开为原播放列表书。</summary>
    public async Task OpenPlaylistAsBookAsync(Playlist expected)
    {
        await Playlists.PrepareOpenAsBookAsync(expected);
        if (!_disposed && !_closing && ReferenceEquals(expected, Playlists.Current)) await OpenCoreAsync(expected.Path, CancellationToken.None, expectedPlaylist: expected);
    }
}
