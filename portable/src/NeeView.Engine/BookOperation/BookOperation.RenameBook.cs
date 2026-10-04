// Copyright (c) NeeLaboratory. 原 BookControl.RenameBook/FileIO.RenameAsync/RestoreBook 链路，基线 c5c398d89。
namespace NeeView;

public sealed partial class BookOperation
{
    private int _renameBusy;
    private bool _renameCommitting;
    private CancellationTokenSource _renameClosing = new();
    private TaskCompletionSource? _renameCompletion;
    public bool IsRenamingBook => Volatile.Read(ref _renameBusy) != 0;
    /// <summary>宿主只收集名称、扩展名/编号确认，不操作文件或原状态。</summary>
    public Func<BookRenameTarget, Task<string?>>? AskBookNameAsync { get; set; }
    public Func<BookRenamePlan, Task<bool>>? ConfirmBookRenameAsync { get; set; }
    /// <summary>沿原失败重试/取消提示；等待用户时释放导航锁，返回后核对最新打开请求。</summary>
    public Func<string, Task<bool>>? RetryBookRenameAsync { get; set; }
    public bool CanRenameBook => !IsRenamingBook && CanRenameBookCore();
    private bool CanRenameBookCore() => Config.Current.System.IsFileWriteAccessEnabled && _fileBackend is IBookRenameBackend
        && !_disposed && !_closing && !IsLoading && Book is { IsIndexing: false } book
        && book.Path == book.Source.RootArchivePath && System.IO.Path.GetDirectoryName(book.Path) is not null
        && saveData.DirectoryPath != book.Path && !saveData.DirectoryPath.StartsWith(book.Path + "/", StringComparison.Ordinal)
        && !IsDeletingFile && _destinationMoves?.IsBusy != true;

    /// <summary>原当前书籍目录或归档改名；单图打开仍改所在书籍目录，不改当前图片文件名。</summary>
    /// <param name="token">实体提交前可取消；提交后恢复新路径并落盘，不进入分类撤销栈。</param>
    /// <returns>用户采集、实体改名和路径联动/恢复结束的任务；错误通过Error回报。</returns>
    public async Task RenameBookAsync(CancellationToken token = default)
    {
        if (!CanRenameBook || Interlocked.CompareExchange(ref _renameBusy, 1, 0) != 0) return;
        var completion = _renameCompletion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        Book? requestedBook = null; BookMemento? memory = null; BookRenamePlan? plan = null; string? search = null;
        long generation = 0, restoreGeneration = 0; bool released = false, renamed = false, prepared = false;
        string? failure = null;
        try
        {
            using var prompt = CancellationTokenSource.CreateLinkedTokenSource(token, _renameClosing.Token);
            Notify();
            var backend = (IBookRenameBackend)_fileBackend!;
            await _gate.WaitAsync(prompt.Token);
            try
            {
                if (!CanRenameBookCore()) return;
                requestedBook = Book!; generation = _generation;
            }
            finally { _gate.Release(); }
            var target = await backend.GetRenameTargetAsync(requestedBook.Path, prompt.Token);
            if (AskBookNameAsync is null || await AskBookNameAsync(target).WaitAsync(prompt.Token) is not { } name) return;
            plan = await backend.PlanRenameAsync(target, name, prompt.Token);
            if (plan.Destination == target.Path) return;
            if ((plan.ExtensionChanged || plan.Conflict) && (ConfirmBookRenameAsync is null || !await ConfirmBookRenameAsync(plan).WaitAsync(prompt.Token))) return;
            // 输入及确认不占导航锁，允许用户切书；仅当前书籍和打开代次未变时授权实体操作。
            await _gate.WaitAsync(prompt.Token);
            try
            {
                if (!CanRenameBookCore() || generation != _generation || !ReferenceEquals(requestedBook, Book)) return;
                _renameCommitting = true; _saving?.Cancel();
                memory = requestedBook.CreateMemento(); search = requestedBook.Pages.SearchKeyword;
                await saveData.SaveAsync(requestedBook, prompt.Token, keepHistoryOrder: true);
                await saveData.PrepareBookRenameAsync(plan, prompt.Token); prepared = true;
                // 关闭可取消准备阶段的锁/探测/保存；实体操作授权后仍等待真实结果。
                prompt.Token.ThrowIfCancellationRequested();
                _opening?.Cancel(); restoreGeneration = Interlocked.Increment(ref _generation);
                foreach (var page in requestedBook.Pages.SourcePages) _fileImages?.InvalidatePage(page);
                _fileImages?.InvalidateCovers();
                await requestedBook.DisposeAsync(); released = true;
                Book = null; Frame = null; Context = null; _fileSelection = null; Notify();
                while (true)
                {
                    try { await backend.RenameAsync(plan, token); renamed = true; break; }
                    catch (OperationCanceledException) { throw; }
                    catch (Exception ex)
                    {
                        bool retry = false;
                        _gate.Release();
                        try { if (RetryBookRenameAsync is { } askRetry) retry = await askRetry(ex.Message).WaitAsync(prompt.Token); }
                        finally { await _gate.WaitAsync(); }
                        if (!retry || _disposed || _closing || restoreGeneration != _generation) throw;
                    }
                }
                PageHistory.RenameRecursive(plan.Target.Path, plan.Destination); BookHistory.RenameRecursive(plan.Target.Path, plan.Destination);
                try { await saveData.RenameBookPathsAsync(plan); }
                catch (Exception ex) { failure = "书籍已重命名，路径联动保存未完成，请重试保存：" + ex.Message; }
            }
            finally { _gate.Release(); }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { failure = (renamed ? "书籍已重命名，但恢复失败：" : "书籍重命名失败：") + ex.Message; }
        finally
        {
            try
            {
                if (prepared && !renamed) saveData.CancelBookRename();
                if (released && memory is not null && plan is not null && !_disposed && !_closing && restoreGeneration == _generation)
                {
                    // 来源已释放，不回用旧Archive；原Resume恢复设置与条目，Part仍按原首半页恢复。
                    memory.Path = renamed ? plan.Destination : plan.Target.Path;
                    await OpenCoreAsync(memory.Path, CancellationToken.None, entryName: string.IsNullOrEmpty(memory.Page) ? null : memory.Page,
                        keepHistoryOrder: true, startupMemento: memory, pageSearchKeyword: search);
                    if (Book?.Path != memory.Path) failure = (failure is null ? "书籍来源已关闭，重新打开失败：" : failure + "\n重新打开失败：") + Error;
                    else if (_bookshelf?.Place is { } place)
                    {
                        var newPlace = renamed ? BookMementoTools.RenamePath(place, plan.Target.Path, plan.Destination) : place;
                        await _bookshelf.SetPlaceAsync(newPlace, selectedPath: memory.Path, force: true);
                    }
                }
            }
            catch (Exception ex) { failure = (failure is null ? "书籍重命名恢复失败：" : failure + "\n") + ex.Message; }
            finally
            {
                _renameCommitting = false; Interlocked.Exchange(ref _renameBusy, 0);
                if (failure is not null) Error = failure;
                try { Notify(); } finally { completion.TrySetResult(); }
            }
        }
    }
}
