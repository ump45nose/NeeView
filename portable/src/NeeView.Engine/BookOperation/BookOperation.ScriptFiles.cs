// Copyright (c) NeeLaboratory. 原CommandHost/ExternalApp/DestinationFolder文件入口的Mac适配，MIT。
using System.Runtime.ExceptionServices;
namespace NeeView;
public sealed partial class BookOperation
{
    /// <summary>原nv.DeleteFile复用原删除/废纸篓服务，拒绝权限关闭及应用数据；不会回退到永久删除。</summary>
    /// <param name="path">真实路径或当前页面的逻辑路径。</param><param name="token">确认和系统提交前取消。</param>
    public async Task DeleteScriptPathAsync(string path, CancellationToken token = default)
    {
        if (!Config.Current.System.IsFileWriteAccessEnabled) throw new UnauthorizedAccessException("文件写入权限已关闭。");
        // 与传输入口使用相同完整路径规则，尾部分隔符不能绕过当前书籍/页面的专属协调。
        path = System.IO.Path.TrimEndingDirectorySeparator(System.IO.Path.GetFullPath(path));
        var platform = _filePlatform ?? throw new NotSupportedException("废纸篓后端未装配。");
        var book = Book;
        if (book?.Path == path && path == book.Source.RootArchivePath)
        {
            if (!CanDeleteBook) throw new InvalidOperationException("当前不能删除书籍。");
            Error = null; await DeleteBookAsync(token);
            if (Error is not null) throw new IOException(Error); token.ThrowIfCancellationRequested(); return;
        }
        var page = book?.Pages.FirstOrDefault(p => p.EntryFullName == path || p.ArchiveEntry.FilePath == path);
        if (page is not null)
        {
            if (!CanDeletePages([page])) throw new InvalidOperationException("当前不能删除该页面。");
            Error = null; await DeletePagesAsync([page], token);
            if (Error is not null) throw new IOException(Error); token.ThrowIfCancellationRequested(); return;
        }
        if (book is not null && IsInsideBook(book.Path, path)) throw new NotSupportedException("删除当前书籍的父目录前，请先关闭书籍。");
        if (Interlocked.CompareExchange(ref _deleteBusy, 1, 0) != 0) throw new InvalidOperationException("删除正在执行。");
        var completion = _bookDeleteCompletion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        using var prompt = CancellationTokenSource.CreateLinkedTokenSource(token, _bookDeleteClosing.Token);
        BookMemento? refresh = null; string? search = null; var generation = _generation;
        try
        {
            var before = await ReadDeleteBookTargetAsync(path, prompt.Token, allowLink: true);
            await ProtectBookTransferDestinationAsync(path, prompt.Token);
            if (Config.Current.System.IsRemoveConfirmed && (ConfirmDeleteAsync is null || !await ConfirmDeleteAsync(path).WaitAsync(prompt.Token))) return;
            await _gate.WaitAsync(prompt.Token);
            try
            {
                ObjectDisposedException.ThrowIf(_disposed || _closing, this);
                if (IsTransferringBook || IsRenamingBook || IsUsingClipboard || _destinationMoves?.IsBusy == true) throw new InvalidOperationException("文件操作正在进行。");
                if (before != await ReadDeleteBookTargetAsync(path, prompt.Token, allowLink: true)) throw new IOException("删除目标在确认期间已改变。");
                prompt.Token.ThrowIfCancellationRequested(); _bookDeleteCommitting = true;
                await platform.TrashAsync(path, token);
                _fileImages?.InvalidateCovers();
                if (book?.Source.IsDirectory == true && ReferenceEquals(book, Book) && IsInsideBook(path, book.Path))
                { refresh = book.CreateMemento(); search = book.Pages.SearchKeyword; }
            }
            finally { _gate.Release(); }
        }
        finally
        {
            _bookDeleteCommitting = false; Interlocked.Exchange(ref _deleteBusy, 0);
            try { Notify(); } finally { completion.TrySetResult(); }
        }
        if (refresh is not null && generation == _generation && !_closing && !_disposed)
            await OpenCoreAsync(refresh.Path, CancellationToken.None, keepHistoryOrder: true, startupMemento: refresh, pageSearchKeyword: search, expectedGeneration: generation);
    }
    /// <summary>按脚本显式Page数组复制，不以当前帧替代调用方选区。</summary>
    public Task TransferScriptPagesAsync(IReadOnlyList<Page> pages, string destination, bool move, CancellationToken token = default)
        => TransferScriptCoreAsync(pages.ToArray(), null, destination, move, true, token);
    /// <summary>原Copy/Move和nv.CopyFile/MoveFile共用文件服务；destinationIsFolder区分原两个入口。</summary>
    public Task TransferScriptPathsAsync(IReadOnlyList<string> paths, string destination, bool move, bool destinationIsFolder, CancellationToken token = default)
        => TransferScriptCoreAsync(null, paths.ToArray(), destination, move, destinationIsFolder, token);
    private async Task TransferScriptCoreAsync(Page[]? pages, string[]? paths, string destination, bool move, bool folder, CancellationToken token)
    {
        if (_destinationMoves is null || _fileBackend is null) throw new NotSupportedException("文件后端未装配。");
        if (move && !Config.Current.System.IsFileWriteAccessEnabled) throw new UnauthorizedAccessException("文件写入权限已关闭。");
        destination = System.IO.Path.GetFullPath(destination);
        // 正在浏览的根书籍必须复用原释放/定位联动，不绕过整书事务直接移动目录项。
        if (paths is { Length: 1 } && Book is { } current && System.IO.Path.GetFullPath(paths[0]) == current.Path && current.Path == current.Source.RootArchivePath)
        {
            if (!(move ? CanMoveBookToFolder : CanCopyBookToFolder)) throw new InvalidOperationException("当前不能传输书籍。");
            await TransferBookToFolderAsync(new("", folder ? destination : System.IO.Path.GetDirectoryName(destination)!), move, token, folder ? null : destination);
            if (Error is not null) throw new IOException(Error); token.ThrowIfCancellationRequested(); return;
        }
        var generation = _generation; var book = Book; var owned = new List<Archive>();
        BookMemento? refresh = null; string? refreshSearch = null; ExceptionDispatchInfo? failure = null;
        var committed = new List<FileTransferResult>();
        RealizedFilePathList? realized = null; var additionalMaterials = new List<RealizedFilePathList>();
        await _gate.WaitAsync(token);
        try
        {
            ObjectDisposedException.ThrowIf(_closing || _disposed, this);
            if (IsUsingClipboard || IsDeletingFile || IsRenamingBook || IsTransferringBook || _destinationMoves.IsBusy) throw new InvalidOperationException("文件操作正在进行。");
            if (generation != _generation || !ReferenceEquals(book, Book)) throw new OperationCanceledException("书籍已改变。", token);
            var inputs = new List<string>();
            if (pages is not null)
            {
                if (pages.Any(p => p.ArchiveEntry.Archive.IsDisposed)) throw new ObjectDisposedException(nameof(Archive));
                if (move) inputs.AddRange(pages.Select(p => p.ArchiveEntry.TargetArchiveEntry.FilePath).OfType<string>());
                else
                {
                    realized = await ArchiveEntryUtility.RealizeArchiveEntry(pages.Select(p => p.ArchiveEntry),
                        Config.Current.System.ArchiveCopyPolicy.LimitedRealization(), _entryRealizer, token);
                    inputs.AddRange(realized.Paths);
                }
            }
            else
            {
                foreach (var path in paths ?? [])
                {
                    token.ThrowIfCancellationRequested();
                    var metadata = await archives.GetFileMetadataAsync(path, token);
                    if (metadata is not null) { inputs.Add(path); continue; }
                    if (move) throw new FileNotFoundException("移动来源不存在。", path);
                    var source = await archives.OpenAsync(path, token); owned.Add(source);
                    var entry = source.RequestedEntryName is { } name
                        ? (await source.GetEntriesAsync(token)).First(e => e.EntryName == name) : source.CreateBookEntry();
                    // 每个独立路径先实体化；最终所有租约按请求释放，不驻留到脚本结束以后。
                    var material = await ArchiveEntryUtility.RealizeArchiveEntry([entry], ArchivePolicy.SendExtractFile, _entryRealizer, token);
                    if (realized is null) realized = material;
                    else { additionalMaterials.Add(material); }
                    inputs.AddRange(material.Paths);
                }
            }
            if (!folder && inputs.Count > 1) throw new ArgumentException("单个文件落点不能接收多个来源。");
            await ProtectBookTransferDestinationAsync(destination, token);
            // 按原调用顺序逐项执行；目录和文件不能被分成两组而改变调用语义。
            foreach (var input in inputs.Distinct(StringComparer.Ordinal))
            {
                token.ThrowIfCancellationRequested();
                var metadata = await archives.GetFileMetadataAsync(input, token) ?? throw new FileNotFoundException("文件来源不存在。", input);
                if (move) await ProtectBookTransferDestinationAsync(input, token);
                var target = folder ? System.IO.Path.Combine(destination, System.IO.Path.GetFileName(input)) : destination;
                FileTransferResult? result;
                if (metadata.IsDirectory && !metadata.IsSymbolicLink && move)
                {
                    if (_fileBackend is not IBookTransferBackend directories) throw new NotSupportedException("目录传输后端未装配。");
                    var plan = await directories.PlanPathTransferAsync(input, target, token);
                    if (plan.DestinationHash is not null && (_destinationMoves.ConfirmDirectoryOverwriteAsync is null
                        || !await _destinationMoves.ConfirmDirectoryOverwriteAsync(plan).WaitAsync(token))) break;
                    token.ThrowIfCancellationRequested();
                    await ProtectBookTransferDestinationAsync(plan.Destination, token);
                    var rename = new BookRenamePlan(plan.Target, plan.Destination, false, plan.DestinationHash is not null,
                        IsMove: true, ContentHash: plan.ContentHash, PreviousDestinationHash: plan.DestinationHash);
                    await saveData.PrepareBookRenameAsync(rename, token);
                    try { result = await directories.TransferBookAsync(plan, true, token); }
                    catch
                    {
                        if (await directories.WasBookMovedAsync(rename, CancellationToken.None) == false) saveData.CancelBookRename();
                        throw;
                    }
                    committed.Add(result); // 落盘后立即记录，即使后续JSON/清理失败也必须协调现有来源。
                    if (book?.Source.IsDirectory == true && (IsInsideBook(result.Source, book.Path) || IsInsideBook(result.Destination, book.Path)))
                    { refresh = book.CreateMemento(); refreshSearch = book.Pages.SearchKeyword; }
                    PageHistory.RenameRecursive(result.Source, result.Destination); BookHistory.RenameRecursive(result.Source, result.Destination);
                    await saveData.RenameBookPathsAsync(rename); await _fileBackend.ReleaseAsync(result);
                }
                else
                {
                    BookTransferPlan? plan = null;
                    if (metadata.IsDirectory && !metadata.IsSymbolicLink)
                    {
                        if (_fileBackend is not IBookTransferBackend directories) throw new NotSupportedException("目录传输后端未装配。");
                        plan = await directories.PlanPathTransferAsync(input, target, token);
                    }
                    var results = await _destinationMoves.TransferManyAsync(_ => Task.FromResult<IReadOnlyList<FileTransferRequest>>([new(input, target, move,
                        PreserveSourceLink: metadata.IsSymbolicLink, DirectoryCopyPlan: plan)]), token,
                        async (copy, ct) => await ProtectBookTransferDestinationAsync(copy.Destination, ct));
                    committed.AddRange(results); result = results.FirstOrDefault();
                    if (_destinationMoves.Error is not null) throw new IOException(_destinationMoves.Error);
                    if (result is null) { token.ThrowIfCancellationRequested(); break; }
                }
            }
            bool changed = false;
            if (book is not null && ReferenceEquals(book, Book))
            {
                var anchor = book.CurrentPage;
                foreach (var result in committed)
                {
                    var page = book.Pages.SourcePages.FirstOrDefault(p => p.ArchiveEntry.FilePath is { } path && System.IO.Path.GetFullPath(path) == result.Source);
                    if (page is not null && !page.ArchiveEntry.IsDirectory) changed |= await ApplyTransferredPageAsync(book, page, result, !move, anchor);
                    if (book.Source.IsDirectory && (page?.ArchiveEntry.IsDirectory == true && IsInsideBook(result.Source, book.Path) || IsInsideBook(result.Destination, book.Path))) { refresh = book.CreateMemento(); refreshSearch = book.Pages.SearchKeyword; }
                }
                if (changed && anchor is not null && book.Pages.Contains(anchor)) Position = new(anchor.Index, Position.Part);
                if (changed) { await ProbeAroundAsync(book, Position.Index, CancellationToken.None); RebuildFrame(1); RecordPageHistory(); }
            }
            if (committed.Count > 0) { _fileImages?.InvalidateCovers(); await saveData.SaveAsync(book, keepHistoryOrder: _keepHistoryOrder); }
            Error = null; Notify();
            token.ThrowIfCancellationRequested();
        }
        catch (Exception ex) { failure = ExceptionDispatchInfo.Capture(ex); }
        finally
        {
            // 失败或取消也不能丢失已经落盘的结果；重读目录保留原Memento语义。
            if (committed.Count > 0 && book?.Source.IsDirectory == true && ReferenceEquals(book, Book))
            {
                _fileImages?.InvalidateCovers();
                foreach (var page in book.Pages.SourcePages.Where(p => committed.Any(r => r.Move && IsInsideBook(p.ArchiveEntry.SystemPath, r.Source) || IsInsideBook(p.ArchiveEntry.SystemPath, r.Destination)))) _fileImages?.InvalidatePage(page);
                if (failure is not null && committed.Any(r => IsInsideBook(r.Source, book.Path) || IsInsideBook(r.Destination, book.Path)))
                { refresh = book.CreateMemento(); refreshSearch = book.Pages.SearchKeyword; }
            }
            try
            {
                foreach (var resource in additionalMaterials.Cast<IAsyncDisposable>().Concat(owned).Prepend(realized).OfType<IAsyncDisposable>())
                {
                    try { await resource.DisposeAsync(); }
                    catch (Exception ex) { failure ??= ExceptionDispatchInfo.Capture(ex); }
                }
            }
            finally { _gate.Release(); }
        }
        if (refresh is not null && generation == _generation && !_closing && !_disposed && ReferenceEquals(book, Book))
            await OpenCoreAsync(refresh.Path, CancellationToken.None, keepHistoryOrder: true, startupMemento: refresh, pageSearchKeyword: refreshSearch, expectedGeneration: generation);
        failure?.Throw();
    }
}
