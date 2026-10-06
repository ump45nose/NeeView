// Copyright (c) NeeLaboratory. 原BookPageActionControl/BookControl/ExternalAppUtility的外部打开链。
namespace NeeView;

public sealed partial class BookOperation
{
    private IPlatformService? _externalPlatform;
    private string _externalExecutablePath = "";
    private int _externalBusy;
    private readonly object _externalSync = new();
    private CancellationTokenSource _externalClosing = new();
    private CancellationTokenSource? _externalPending;
    private TaskCompletionSource? _externalCompletion;
    public bool IsOpeningExternalApplication => Volatile.Read(ref _externalBusy) != 0;
    /// <summary>唯一启动层装配平台与实际入口地址；不由视图创建具体后端。</summary>
    public void AttachExternalApplications(IPlatformService platform, string executablePath) { _externalPlatform = platform; _externalExecutablePath = executablePath; }
    /// <summary>切书/关窗只取消尚未提交准备，系统提交后保持材料所有权。</summary>
    public void CancelExternalApplicationPreparation() { lock (_externalSync) _externalPending?.Cancel(); }
    /// <summary>原页组选择或Book.Path条目；与实体写权限无关，瀑布要求明确操作对象。</summary>
    public bool CanOpenExternalApplication(MultiPagePolicy policy, bool book = false) => Enum.IsDefined(policy) && _externalPlatform is not null && !_disposed && !_closing && !IsLoading && !IsOpeningExternalApplication
        && !IsUsingClipboard && !IsRenamingBook && !IsTransferringBook && !IsDeletingFile && _destinationMoves?.IsBusy != true && Book?.IsIndexing == false
        && (book ? !Book.Source.IsDisposed : CollectFileActionPages(policy).Count > 0);
    /// <summary>读取唯一差分JSON的一开始应用索引；零保留原选择菜单语义。</summary>
    public int GetExternalApplicationIndex(string name) => name switch
    {
        "OpenExternalAppAs" => saveData.GetCommandParameter<OpenExternalAppAsCommandParameter>(name).Index,
        "OpenBookExternalAppAs" => saveData.GetCommandParameter<OpenBookExternalAppAsCommandParameter>(name).Index,
        "OpenExternalApp" => 0,
        _ => throw new ArgumentException("未知的外部应用命令。", nameof(name))
    };
    /// <summary>原页面多页参数与整书固定Once，不能统一丢弃页组顺序。</summary>
    public MultiPagePolicy GetExternalApplicationPolicy(string name) => name switch
    {
        "OpenExternalApp" => saveData.GetCommandParameter<OpenExternalAppCommandParameter>(name).MultiPagePolicy,
        "OpenExternalAppAs" => saveData.GetCommandParameter<OpenExternalAppAsCommandParameter>(name).MultiPagePolicy,
        "OpenBookExternalAppAs" => MultiPagePolicy.Once,
        _ => throw new ArgumentException("未知的外部应用命令。", nameof(name))
    };
    /// <summary>三原命令共用实际业务；selectedIndex来自菜单，仍是一开始编号。</summary>
    public Task OpenExternalApplicationCommandAsync(string name, int? selectedIndex = null, CancellationToken token = default)
    {
        var policy = GetExternalApplicationPolicy(name);
        if (name == "OpenExternalApp") return OpenExternalApplicationAsync(saveData.GetCommandParameter<OpenExternalAppCommandParameter>(name), policy, token: token);
        var index = (selectedIndex ?? GetExternalApplicationIndex(name)) - 1;
        if (index < 0) return Task.CompletedTask; // Index0由唯一宿主呈现选择菜单，不自动取第一个应用。
        var apps = Config.Current.System.ExternalAppCollection;
        if (!apps.IsValidIndex(index)) throw new ArgumentOutOfRangeException(nameof(selectedIndex), "原外部应用索引已不存在。");
        return OpenExternalApplicationAsync(apps[index], policy, name == "OpenBookExternalAppAs", token);
    }
    /// <summary>捕获配置与原页组，串行实体化/提交；失败保持阅读、索引与移动历史。</summary>
    /// <param name="options">原配置应用或直接命令参数。</param><param name="policy">当前页组收集顺序。</param><param name="book">整书沿原系统归档复制策略。</param><param name="token">准备/提交前取消。</param>
    public async Task OpenExternalApplicationAsync(IExternalApp options, MultiPagePolicy policy = MultiPagePolicy.Once, bool book = false, CancellationToken token = default)
    {
        CancellationTokenSource pending; TaskCompletionSource completion;
        lock (_externalSync)
        {
            if (!CanOpenExternalApplication(policy, book)) return;
            pending = CancellationTokenSource.CreateLinkedTokenSource(token, _externalClosing.Token);
            completion = new(TaskCreationOptions.RunContinuationsAsynchronously); _externalPending = pending; _externalCompletion = completion;
            Volatile.Write(ref _externalBusy, 1);
        }
        var generation = _generation; var sourceBook = Book!;
        var captured = new ExternalApp { Command = options.Command, Parameter = options.Parameter, ArchivePolicy = options.ArchivePolicy, WorkingDirectory = options.WorkingDirectory };
        var entries = book ? [sourceBook.Source.CreateBookEntry()] : CollectFileActionPages(policy).Select(page => page.ArchiveEntry).ToArray();
        var archivePolicy = book ? Config.Current.System.ArchiveCopyPolicy.LimitedRealization() : captured.ArchivePolicy;
        RealizedFilePathList? files = null; bool entered = false, submitted = false;
        try
        {
            Notify(); await _gate.WaitAsync(pending.Token); entered = true;
            if (_closing || generation != _generation || !ReferenceEquals(sourceBook, Book)) return;
            files = await ArchiveEntryUtility.RealizeArchiveEntry(entries, archivePolicy, _entryRealizer, pending.Token);
            var requests = files.Paths.Select(path => ExternalAppUtility.CreateLaunchRequest(captured, path, _externalExecutablePath)).ToArray();
            pending.Token.ThrowIfCancellationRequested();
            if (generation != _generation || !ReferenceEquals(sourceBook, Book)) return;
            if (ExternalAppUtility.RequiresSave(captured)) await SaveAsync();
            foreach (var request in requests)
            {
                pending.Token.ThrowIfCancellationRequested();
                await _externalPlatform!.OpenExternalApplicationAsync(request, pending.Token);
                submitted = true;
            }
            if (!_closing && generation == _generation) Error = files.CapabilityWarning;
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { if (!_closing && generation == _generation) Error = "外部应用打开失败：" + ex.Message; }
        finally
        {
            try
            {
                // 即使后续项失败，首个外部程序仍可能读取材料；成功提交的批次驻留到进程退出。
                if (files is not null)
                {
                    if (submitted && files.HasTemporaryFiles && _entryRealizer is not null) await _entryRealizer.RetainExternalAsync(files);
                    else await files.DisposeAsync();
                }
            }
            finally
            {
                if (entered) _gate.Release();
                lock (_externalSync) { _externalPending = null; pending.Dispose(); Volatile.Write(ref _externalBusy, 0); }
                try { Notify(); } finally { completion.TrySetResult(); }
            }
        }
    }
}
