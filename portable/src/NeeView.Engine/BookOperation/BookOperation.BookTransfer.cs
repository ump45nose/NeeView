// Copyright (c) NeeLaboratory. 原 BookControl/DestinationFolder/FileIO 整书传输，基线 c5c398d89。
namespace NeeView;

public sealed partial class BookOperation
{
    private int _bookTransferBusy;
    private readonly object _bookTransferSync = new();
    private CancellationTokenSource? _bookTransferPreparation;
    /// <summary>新打开/窗口关闭取消未授权的规划与确认；已提交的实体操作不受影响。</summary>
    public void CancelBookTransferPreparation() { lock (_bookTransferSync) _bookTransferPreparation?.Cancel(); }
    private bool _bookTransferCommitting;
    private CancellationTokenSource _bookTransferClosing = new();
    private TaskCompletionSource? _bookTransferCompletion;
    public bool IsTransferringBook => Volatile.Read(ref _bookTransferBusy) != 0;
    /// <summary>宿主只确认完整目标覆盖；目录覆盖明确包括全部已有内容。</summary>
    public Func<BookTransferPlan, Task<bool>>? ConfirmBookOverwriteAsync { get; set; }
    public bool CanCopyBookToFolder => !IsTransferringBook && CanTransferBookCore(false);
    public bool CanMoveBookToFolder => !IsTransferringBook && CanTransferBookCore(true);
    private bool CanTransferBookCore(bool move) => (!move || Config.Current.System.IsFileWriteAccessEnabled)
        && _fileBackend is IBookTransferBackend && !IsRenamingBook && !IsDeletingFile && !IsUsingClipboard
        && !_disposed && !_closing && !IsLoading && _destinationMoves?.IsBusy != true
        && Book is { IsIndexing: false } book && (move ? book.Path == book.Source.RootArchivePath : CanCopyEntry(book.Source.CreateBookEntry())) && !saveData.IsTemporaryPath(book.Path)
        && System.IO.Path.GetDirectoryName(book.Path) is not null && book.Path != "/Volumes" && System.IO.Path.GetDirectoryName(book.Path) != "/Volumes"
        && saveData.DirectoryPath != book.Path && !saveData.DirectoryPath.StartsWith(book.Path.TrimEnd('/') + "/", StringComparison.Ordinal);

    /// <summary>原指定目录命令1-based Index；Index=0的菜单由宿主呈现，不自行选择首项目。</summary>
    /// <param name="command">CopyBookToFolderAs/MoveBookToFolderAs原标识。</param><returns>已配置目标的完整动作任务。</returns>
    public Task TransferBookCommandAsync(string command)
    {
        var index = GetDestinationParameter(command).Index; var folders = Config.Current.System.DestinationFolderCollection;
        return index > 0 && folders.IsValidIndex(index - 1) ? TransferBookToFolderAsync(folders[index - 1], command == "MoveBookToFolderAs") : Task.CompletedTask;
    }

    /// <summary>复制按原Book.Path条目和归档策略保留阅读；移动仅真实根实体，关闭书籍且不跳目标/邻书。</summary>
    /// <param name="folder">原手动目标集合中的目录。</param><param name="move">true固定移动，false固定复制，不跟随面板模式。</param>
    /// <param name="token">确认和提交前取消；已落盘结果继续协调JSON。</param><returns>实体传输、状态联动及必要失败恢复完成的任务。</returns>
    public async Task TransferBookToFolderAsync(DestinationFolder folder, bool move, CancellationToken token = default)
    {
        if (!(move ? CanMoveBookToFolder : CanCopyBookToFolder) || Interlocked.CompareExchange(ref _bookTransferBusy, 1, 0) != 0) return;
        var completion = _bookTransferCompletion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        using var prompt = CancellationTokenSource.CreateLinkedTokenSource(token, _bookTransferClosing.Token);
        lock (_bookTransferSync) _bookTransferPreparation = prompt;
        Book? requested = null; BookMemento? memory = null; BookRenamePlan? rename = null;
        RealizedFilePathList? realized = null;
        string? search = null, failure = null, place = null; long generation = 0, actionGeneration = 0;
        bool released = false, committed = false, prepared = false, wasLocked = false;
        try
        {
            Notify(); await _gate.WaitAsync(prompt.Token);
            try { if (!CanTransferBookCore(move) || !folder.IsValid()) return; requested = Book!; generation = _generation; wasLocked = IsBookLocked; }
            finally { _gate.Release(); }
            var sourcePath = requested.Path;
            if (requested.Path != requested.Source.RootArchivePath)
            {
                // 原DestinationFolder.CopyAsync先解析Book.Path，再LimitedRealization；不以当前页代替书籍。
                realized = await ArchiveEntryUtility.RealizeArchiveEntry([requested.Source.CreateBookEntry()],
                    Config.Current.System.ArchiveCopyPolicy.LimitedRealization(), _entryRealizer, prompt.Token);
                if (realized.Paths.Count == 0) { failure = realized.CapabilityWarning; return; }
                sourcePath = realized.Paths.Single();
            }
            await ReadDeleteBookTargetAsync(sourcePath, prompt.Token);
            var backend = (IBookTransferBackend)_fileBackend!;
            var plan = await backend.PlanBookTransferAsync(sourcePath, folder.Path, prompt.Token);
            await ProtectBookTransferDestinationAsync(plan.Destination, prompt.Token);
            if (plan.DestinationHash is not null && (ConfirmBookOverwriteAsync is null || !await ConfirmBookOverwriteAsync(plan).WaitAsync(prompt.Token))) return;
            await _gate.WaitAsync(prompt.Token);
            try
            {
                if (!CanTransferBookCore(move) || generation != _generation || !ReferenceEquals(requested, Book)) return;
                await ReadDeleteBookTargetAsync(sourcePath, prompt.Token);
                await ProtectBookTransferDestinationAsync(plan.Destination, prompt.Token);
                _saving?.Cancel(); memory = requested.CreateMemento(); search = requested.Pages.SearchKeyword; place = _bookshelf?.Place;
                await saveData.SaveAsync(requested, prompt.Token, keepHistoryOrder: true);
                if (move)
                {
                    rename = new(plan.Target, plan.Destination, false, plan.DestinationHash is not null, IsMove: true,
                        ContentHash: plan.ContentHash, PreviousDestinationHash: plan.DestinationHash);
                    await saveData.PrepareBookRenameAsync(rename, prompt.Token); prepared = true;
                }
                prompt.Token.ThrowIfCancellationRequested(); _bookTransferCommitting = true;
                lock (_bookTransferSync) _bookTransferPreparation = null;
                if (move)
                {
                    _opening?.Cancel(); actionGeneration = Interlocked.Increment(ref _generation);
                    foreach (var page in requested.Pages.SourcePages) _fileImages?.InvalidatePage(page);
                    _fileImages?.InvalidateCovers(); await requested.DisposeAsync(); released = true;
                    Book = null; Frame = null; Context = null; Position = PagePosition.Zero; _fileSelection = null; IsBookLocked = false;
                    PageSelector.Synchronize(null, 0); RefreshMarkers(); Notify();
                }
                else actionGeneration = generation;
                // 关闭只取消准备阶段，已授权操作仍等待后端提交点和真实结果。
                var result = await backend.TransferBookAsync(plan, move, token); committed = true;
                if (move)
                {
                    PageHistory.RenameRecursive(result.Source, result.Destination); BookHistory.RenameRecursive(result.Source, result.Destination);
                    try { await saveData.RenameBookPathsAsync(rename!); }
                    catch (Exception ex) { failure = "书籍已移动，路径联动保存未完成，请重试保存：" + ex.Message; ScheduleSave(); }
                }
                _fileImages?.InvalidateCovers();
                try { await _fileBackend!.ReleaseAsync(result); }
                catch (Exception ex) { failure = "书籍传输已成功，恢复材料清理未完成：" + ex.Message; }
            }
            finally { _gate.Release(); }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { failure = (committed ? "书籍传输已成功，但协调失败：" : "书籍传输失败：") + ex.Message; }
        finally
        {
            try
            {
                if (prepared && !committed)
                {
                    // 实体返回异常不意味着文件已回滚；仅两端均符合原快照时撤销路径记录。
                    if (await ((IBookTransferBackend)_fileBackend!).WasBookMovedAsync(rename!, CancellationToken.None) == false) saveData.CancelBookRename();
                    else failure = "书籍传输状态尚未确认，路径恢复记录已保留，请检查恢复提示。";
                }
                if (released && !committed && memory is not null && !_disposed && !_closing && actionGeneration == _generation)
                {
                    var expected = actionGeneration + 1;
                    await OpenCoreAsync(memory.Path, CancellationToken.None, entryName: string.IsNullOrEmpty(memory.Page) ? null : memory.Page,
                        keepHistoryOrder: true, startupMemento: memory, pageSearchKeyword: search);
                    if (expected == _generation) { actionGeneration = expected; IsBookLocked = wasLocked; }
                    if (Book?.Path != memory.Path) failure = "传输失败后的原书恢复失败：" + Error;
                }
                if (committed && !_disposed && !_closing && actionGeneration == _generation && _bookshelf is { } shelf && shelf.Place == place)
                { await shelf.RefreshAsync(); if (shelf.Error is { } error) failure = "书籍传输已成功，书架刷新失败：" + error; }
            }
            catch (Exception ex) { failure = "书籍传输后的恢复失败：" + ex.Message; }
            finally
            {
                try { if (realized is not null) await realized.DisposeAsync(); }
                catch (Exception ex) { failure = "书籍复制临时材料清理失败：" + ex.Message; }
                lock (_bookTransferSync) _bookTransferPreparation = null;
                _bookTransferCommitting = false; Interlocked.Exchange(ref _bookTransferBusy, 0);
                if (failure is not null && (released ? actionGeneration == _generation : generation == _generation)) Error = failure;
                try { Notify(); } finally { completion.TrySetResult(); }
            }
        }
    }

    /// <summary>目标覆盖不允许触及Profile、应用临时数据或卷根；别名按实际路径核对。</summary>
    private async Task ProtectBookTransferDestinationAsync(string path, CancellationToken token)
    {
        var physical = await archives.GetPhysicalPathAsync(path, token); var profile = await archives.GetPhysicalPathAsync(saveData.DirectoryPath, token);
        static bool Within(string item, string root) => item == root || item.StartsWith(root.TrimEnd('/') + "/", StringComparison.Ordinal);
        if (System.IO.Path.GetDirectoryName(physical) is null || physical == "/Volumes" || System.IO.Path.GetDirectoryName(physical) == "/Volumes"
            || Within(profile, physical) || Within(physical, profile)) throw new NotSupportedException("目标不能覆盖卷根或应用Profile。");
        if (saveData.TemporaryDirectoryPath is { } temporary)
        { var root = await archives.GetPhysicalPathAsync(temporary, token); if (Within(root, physical) || Within(physical, root)) throw new NotSupportedException("目标不能位于应用临时数据中。"); }
    }
}
