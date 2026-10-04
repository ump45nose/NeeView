// Copyright (c) NeeLaboratory. 原BookPageActionControl/ClipboardUtility及ContentDropReceiver加载语义，基线c5c398d89。
namespace NeeView;

public sealed partial class BookOperation
{
    private IFileClipboard? _fileClipboard;
    private IArchiveEntryRealizer? _entryRealizer;
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
    /// <summary>启动层注入进程级实体化能力；关窗和切书不释放成功发布的剪贴板材料。</summary>
    /// <param name="realizer">与原归档读取关系相连的临时实体后端。</param>
    public void AttachArchiveEntryRealizer(IArchiveEntryRealizer realizer) => _entryRealizer = realizer;
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
    /// <summary>复用原CollectPages；普通文件及已接入归档文件可以复制，链接/目录提取仍待迁。</summary>
    /// <param name="policy">原三种页组收集策略。</param><returns>整组来源及当前状态均支持复制时为true。</returns>
    public bool CanCopyFiles(MultiPagePolicy policy) => Enum.IsDefined(policy) && CanUseClipboard && !IsLoading && Book?.IsIndexing == false
        && CollectFileActionPages(policy) is { Count: > 0 } pages && pages.All(CanRealizeFile);
    /// <summary>按原Page来源和装配能力判断实体化，不将归档逻辑路径当作普通文件。</summary>
    /// <param name="page">原当前页组成员。</param><returns>普通实体或已装配提取能力的归档文件时为true。</returns>
    private bool CanRealizeFile(Page page) => page.ArchiveEntry.CanRealize() && (page.ArchiveEntry.FilePath is not null || _entryRealizer is not null);
    /// <summary>复制原页组或整个实体书籍到剪贴板；归档可生成临时实体，不修改源文件或移动历史。</summary>
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
            var entries = book ? [] : CollectFileActionPages(policy).Select(p => p.ArchiveEntry).ToArray();
            var query = book ? [requestedBook.Path] : entries.Select(entry => entry.SystemPath).ToArray();
            var archivePolicy = Config.Current.System.ArchiveCopyPolicy;
            var textPolicy = Config.Current.System.TextCopyPolicy;
            Notify(); await _gate.WaitAsync(pending.Token);
            try
            {
                if (_disposed || _closing || IsLoading || generation != _generation || !ReferenceEquals(requestedBook, Book)) return;
                FileClipboardCodec.ValidatePaths(query);
                // 整组选区的普通实体/根归档先确认；虚拟路径不能交给文件系统存在性检查。
                foreach (var path in book ? query : entries.Select(entry => entry.FilePath ?? entry.Archive.RootArchivePath).Distinct(StringComparer.Ordinal))
                {
                    var info = await archives.GetFileMetadataAsync(path, pending.Token);
                    if (info is null) throw new FileNotFoundException("要复制的实体已不存在。", path);
                    if (info.IsSymbolicLink) throw new NotSupportedException("链接的剪贴板复制尚未迁移。");
                }
                RealizedFilePathList? realized = book ? new() : await ArchiveEntryUtility.RealizeArchiveEntry(entries, archivePolicy, _entryRealizer, pending.Token);
                try
                {
                    if (book) realized.Add(requestedBook.Path);
                    if (_closing || generation != _generation || !ReferenceEquals(requestedBook, Book)) return;
                    // c5c398d89 的 OriginalPath 分支再次调用同一提取策略；保留其实际输出，不能按名称另解。
                    string? text = textPolicy switch
                    {
                        TextCopyPolicy.None => null,
                        TextCopyPolicy.CopyFilePath or TextCopyPolicy.OriginalPath => realized.Paths.Count == 0 ? null : string.Join(Environment.NewLine, realized.Paths),
                        _ => throw new NotSupportedException("未知的原文本复制策略。")
                    };
                    await _fileClipboard!.WriteAsync(new(realized.Paths.ToArray(), query, text), pending.Token);
                    // 提交后转移所有权，不受晚取消或切书影响；普通文件复制也释放旧归档剪贴板材料。
                    if (_entryRealizer is not null) { await _entryRealizer.RetainClipboardAsync(realized); realized = null; }
                }
                finally { if (realized is not null) await realized.DisposeAsync(); }
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
