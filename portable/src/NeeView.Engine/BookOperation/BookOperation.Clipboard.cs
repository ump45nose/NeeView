// Copyright (c) NeeLaboratory. 原BookPageActionControl/ClipboardUtility及ContentDropReceiver加载语义，基线c5c398d89。
namespace NeeView;

public sealed partial class BookOperation
{
    private IFileClipboard? _fileClipboard;
    private int _clipboardBusy;
    private CancellationTokenSource _clipboardClosing = new();
    private TaskCompletionSource? _clipboardCompletion;
    private readonly object _clipboardSync = new();
    private CancellationTokenSource? _clipboardPending;
    private bool _clipboardIsCopy;
    public bool IsUsingClipboard => Volatile.Read(ref _clipboardBusy) != 0;
    private bool CanUseClipboard => _fileClipboard is not null && !_disposed && !_closing && !IsUsingClipboard && !IsRenamingBook && !IsDeletingFile && _destinationMoves?.IsBusy != true;
    public bool CanPasteFiles => CanUseClipboard && !IsLoading && _fileClipboard!.HasFileContent;
    public bool CanCopyBook => CanUseClipboard && !IsLoading && Book is { IsIndexing: false } book && book.Path == book.Source.RootArchivePath;
    /// <summary>启动层注入系统协议；视图只转交稳定命令。</summary>
    /// <param name="clipboard">窗口使用的文件协议后端。</param>
    public void AttachFileClipboard(IFileClipboard clipboard) => _fileClipboard = clipboard;
    /// <summary>宿主关闭前取消当前准备；已提交的原生写入仍等待真实结果，不永久禁用后续复制。</summary>
    public void CancelClipboardPreparation()
    { lock (_clipboardSync) _clipboardPending?.Cancel(); }
    /// <summary>新打开取消尚未提交的复制，不取消Paste自身的打开流程。</summary>
    private void CancelCopyPreparation()
    { lock (_clipboardSync) if (_clipboardIsCopy) _clipboardPending?.Cancel(); }
    /// <summary>在关闭/取消读取等待任务前，原子登记当前调用的资源和完成信号。</summary>
    /// <param name="copy">区分复制准备与Paste自身的加载。</param><param name="token">调用方取消令牌。</param>
    /// <param name="pending">仅本次调用持有的linked CTS。</param><param name="completion">关闭等待的完成信号。</param>
    /// <returns>当前能力和关闭状态允许登记时为true。</returns>
    private bool BeginClipboard(bool copy, CancellationToken token, out CancellationTokenSource pending, out TaskCompletionSource completion)
    {
        lock (_clipboardSync)
        {
            pending = null!; completion = null!;
            if (!CanUseClipboard) return false;
            pending = CancellationTokenSource.CreateLinkedTokenSource(token, _clipboardClosing.Token);
            completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
            _clipboardPending = pending; _clipboardIsCopy = copy; _clipboardCompletion = completion;
            Volatile.Write(ref _clipboardBusy, 1); return true;
        }
    }
    /// <summary>释放当前调用，再回报能力改变；任何通知异常也不能悬挂关闭等待。</summary>
    /// <param name="pending">本次调用所有的令牌资源。</param><param name="completion">无论成功、失败或取消都完成的信号。</param>
    private void EndClipboard(CancellationTokenSource pending, TaskCompletionSource completion)
    {
        lock (_clipboardSync) { _clipboardPending = null; pending.Dispose(); Volatile.Write(ref _clipboardBusy, 0); }
        try { Notify(); } finally { completion.TrySetResult(); }
    }
    /// <summary>从唯一原差分配置读取参数，不展开默认命令。</summary>
    /// <returns>原CopyFile当前页组参数。</returns>
    public CopyFileCommandParameter GetCopyFileParameter() => saveData.GetCommandParameter<CopyFileCommandParameter>("CopyFile");
    /// <summary>原CollectPages策略复用；本批只支持真实目录项，归档实体化/链接明确待迁。</summary>
    /// <param name="policy">原三种页组收集策略。</param><returns>整组来源及当前状态均支持复制时为true。</returns>
    public bool CanCopyFiles(MultiPagePolicy policy) => Enum.IsDefined(policy) && CanUseClipboard && !IsLoading && Book?.IsIndexing == false
        && CollectFileActionPages(policy) is { Count: > 0 } pages && pages.All(IsClipboardFile);
    /// <summary>按原Page来源能力判断实体项，不将归档逻辑路径当作普通文件。</summary>
    /// <param name="page">原当前页组成员。</param><returns>本批支持的普通目录实体时为true。</returns>
    private static bool IsClipboardFile(Page page) => page.ArchiveEntry is { FilePath: not null, IsShortcut: false } entry && entry.Archive.IsDirectory;
    /// <summary>复制当前真实页组或整个实体书籍，不实际复制/移动文件，也不写分类历史。</summary>
    /// <param name="book">true复制原书籍目录/根归档；false按原MultiPagePolicy选页。</param>
    /// <param name="token">准备及系统剪贴板提交前可取消。</param><returns>提交结束任务，失败通过Error回报。</returns>
    public async Task CopyFilesAsync(bool book = false, CancellationToken token = default)
    {
        var policy = book ? MultiPagePolicy.Once : GetCopyFileParameter().MultiPagePolicy;
        if (!(book ? CanCopyBook : CanCopyFiles(policy)) || !BeginClipboard(true, token, out var pending, out var completion)) return;
        var generation = _generation;
        try
        {
            var requestedBook = Book!;
            var paths = book ? [requestedBook.Path] : CollectFileActionPages(policy).Select(p => p.ArchiveEntry.FilePath!).ToArray();
            Notify(); await _gate.WaitAsync(pending.Token);
            try
            {
                if (_disposed || _closing || IsLoading || generation != _generation || !ReferenceEquals(requestedBook, Book)) return;
                FileClipboardCodec.ValidatePaths(paths);
                foreach (var path in paths)
                {
                    var info = await archives.GetFileMetadataAsync(path, pending.Token);
                    if (info is null) throw new FileNotFoundException("要复制的实体已不存在。", path);
                    if (info.IsSymbolicLink) throw new NotSupportedException("链接的剪贴板复制尚未迁移。");
                }
                if (_closing || generation != _generation || !ReferenceEquals(requestedBook, Book)) return;
                string? text = Config.Current.System.TextCopyPolicy switch
                {
                    TextCopyPolicy.None => null,
                    TextCopyPolicy.CopyFilePath or TextCopyPolicy.OriginalPath => string.Join(Environment.NewLine, paths),
                    _ => throw new NotSupportedException("未知的原文本复制策略。")
                };
                await _fileClipboard!.WriteAsync(new(paths, paths.ToArray(), text), pending.Token);
                if (!_closing && generation == _generation) Error = null;
            }
            finally { _gate.Release(); }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { if (!_closing && generation == _generation) Error = "文件复制到剪贴板失败：" + ex.Message; }
        finally { EndClipboard(pending, completion); }
    }
    /// <summary>原Paste加载文件而非粘入目录；QueryPath优先，当前仅单个来源，多个不丢项。</summary>
    /// <param name="token">剪贴板读取和来源打开的取消令牌。</param><returns>原打开链路完成的任务。</returns>
    public async Task PasteFilesAsync(CancellationToken token = default)
    {
        if (!CanPasteFiles || !BeginClipboard(false, token, out var pending, out var completion)) return;
        var generation = _generation;
        try
        {
            Notify();
            var content = await _fileClipboard!.ReadAsync(pending.Token);
            if (_disposed || _closing || generation != _generation) return;
            var paths = FileClipboardCodec.ValidatePaths(content.QueryPaths.Count > 0 ? content.QueryPaths : content.Files);
            if (paths.Length == 0) return;
            if (paths.Length != 1) throw new NotSupportedException("多文件粘贴需要原临时播放列表来源，本批尚未迁入；本次未打开任何项目。");
            if (_disposed || _closing || generation != _generation) return;
            await OpenCoreAsync(paths[0], pending.Token);
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { if (!_closing && generation == _generation) Error = "剪贴板加载失败：" + ex.Message; }
        finally { EndClipboard(pending, completion); }
    }
}
