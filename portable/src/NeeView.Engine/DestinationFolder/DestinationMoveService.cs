// Copyright (c) NeeLaboratory. 原 DestinationMoveService 忙碌锁、双栈和成功后提交，基线 c5c398d89。
namespace NeeView;

/// <summary>进程共享移动历史；由启动装配注入，替换原全局 Shell/Toast/窗口调用。</summary>
public sealed class DestinationMoveService(IFileOperationBackend backend)
{
    private readonly object _syncRoot = new();
    private readonly List<FileTransferResult> _undoHistory = [], _redoHistory = [];
    private int _isBusy;
    public bool IsBusy => Volatile.Read(ref _isBusy) != 0;
    public bool CanUndo { get { lock (_syncRoot) return !IsBusy && _undoHistory.Count > 0; } }
    public bool CanRedo { get { lock (_syncRoot) return !IsBusy && _redoHistory.Count > 0; } }
    public int UndoCount { get { lock (_syncRoot) return _undoHistory.Count; } }
    public int RedoCount { get { lock (_syncRoot) return _redoHistory.Count; } }
    public string? Error { get; private set; }
    public event EventHandler? StateChanged;
    /// <summary>覆盖确认由表现层提供，默认拒绝；在执行锁内等待，不占用 UI 线程。</summary>
    public Func<string, Task<bool>>? ConfirmOverwriteAsync { get; set; }
    /// <summary>目录覆盖必须独立确认整个树；不能复用只有图片路径的确认文案。</summary>
    public Func<BookTransferPlan, Task<bool>>? ConfirmDirectoryOverwriteAsync { get; set; }

    /// <summary>处理一个原主图片；复制不进入历史，失败/取消不改变栈。</summary>
    public async Task<FileTransferResult?> TransferAsync(string source, string destination, bool move, CancellationToken token = default)
        => (await TransferManyAsync([new(source, destination, move)], token)).FirstOrDefault();

    /// <summary>同一原多页动作独占忙碌锁；按选定顺序逐项执行，仅真实成功项入历史并回报。</summary>
    /// <param name="requests">已捕获的实体目标；目录复制带完整快照，不得用于分类移动。</param>
    /// <param name="token">取消阻止后续项；提交过的项仍返回给阅读控制协调。</param>
    /// <returns>按实际成功顺序排列的结果；失败或取消不会丢失先前已提交项。</returns>
    public Task<IReadOnlyList<FileTransferResult>> TransferManyAsync(IReadOnlyList<FileTransferRequest> requests, CancellationToken token = default)
        => TransferManyAsync(_ => Task.FromResult(requests), token);

    /// <summary>归档实体化准备也独占原忙碌锁；准备失败不能执行部分路径或改变历史。</summary>
    /// <param name="prepare">捕获整组并生成真实请求，执行期间不允许另一文件动作插入。</param>
    /// <param name="token">准备及实体提交前取消。</param>
    /// <param name="validateDirectoryCopy">目录确认后再次复核应用路径保护；不承担平台传输或选页。</param><returns>真实成功项的原有顺序。</returns>
    public async Task<IReadOnlyList<FileTransferResult>> TransferManyAsync(Func<CancellationToken, Task<IReadOnlyList<FileTransferRequest>>> prepare, CancellationToken token = default,
        Func<BookTransferPlan, CancellationToken, Task>? validateDirectoryCopy = null)
    {
        var results = new List<FileTransferResult>();
        await RunAsync(async () =>
        {
            var requests = await prepare(token);
            foreach (var request in requests)
            {
                token.ThrowIfCancellationRequested();
                FileTransferResult result;
                if (request.DirectoryCopyPlan is { } plan)
                {
                    if (request.Move || !plan.Target.IsDirectory || plan.Target.Path != request.Source || plan.Destination != request.Destination
                        || backend is not IBookTransferBackend directoryBackend)
                        throw new NotSupportedException("目录仅支持已核对的固定复制，不能进入分类移动历史。");
                    if (plan.DestinationHash is not null && (ConfirmDirectoryOverwriteAsync is null || !await ConfirmDirectoryOverwriteAsync(plan).WaitAsync(token))) break;
                    if (validateDirectoryCopy is not null) await validateDirectoryCopy(plan, token);
                    token.ThrowIfCancellationRequested();
                    result = await directoryBackend.TransferBookAsync(plan, false, token);
                }
                else
                {
                    bool overwrite = await backend.FileExistsAsync(request.Destination, token);
                    if (overwrite && (ConfirmOverwriteAsync is null || !await ConfirmOverwriteAsync(request.Destination).WaitAsync(token))) break;
                    token.ThrowIfCancellationRequested();
                    result = await backend.TransferAsync(request with { Overwrite = overwrite }, token);
                }
                results.Add(result);
                if (request.Move)
                {
                    List<FileTransferResult> retired;
                    lock (_syncRoot) { retired = _redoHistory.ToList(); _redoHistory.Clear(); _undoHistory.Add(result); retired.AddRange(Trim()); }
                    foreach (var old in retired) await ReleaseSafelyAsync(old);
                }
                else await ReleaseSafelyAsync(result);
            }
            return results.LastOrDefault();
        });
        return results;
    }

    /// <summary>反向重放栈顶，实际成功后才弹栈；覆盖副本一并恢复。</summary>
    public Task<FileTransferResult?> ReplayAsync(bool undo, CancellationToken token = default) => RunAsync(async () =>
    {
        FileTransferResult? record;
        lock (_syncRoot) record = (undo ? _undoHistory : _redoHistory).LastOrDefault();
        if (record is null) return null;
        bool overwrite = await backend.FileExistsAsync(record.Source, token);
        if (overwrite && (ConfirmOverwriteAsync is null || !await ConfirmOverwriteAsync(record.Source))) return null;
        token.ThrowIfCancellationRequested();
        var inverse = await backend.TransferAsync(new(record.Destination, record.Source, true, overwrite, record.Backup, record.ContentHash, record.BackupHash), token);
        List<FileTransferResult> retired;
        lock (_syncRoot)
        {
            var from = undo ? _undoHistory : _redoHistory; var to = undo ? _redoHistory : _undoHistory;
            from.RemoveAt(from.Count - 1); to.Add(inverse); retired = Trim();
        }
        await ReleaseSafelyAsync(record);
        foreach (var old in retired) await ReleaseSafelyAsync(old);
        return inverse;
    });

    /// <summary>缩容清理最旧的两栈项；不把容量变更当作一次移动。</summary>
    public async Task ApplyHistoryCapacityAsync()
    {
        await RunAsync(async () =>
        {
            List<FileTransferResult> retired; lock (_syncRoot) retired = Trim();
            foreach (var old in retired) await ReleaseSafelyAsync(old);
            return null;
        });
    }
    private List<FileTransferResult> Trim()
    {
        var retired = new List<FileTransferResult>(); int capacity = Config.Current.Panels.DestinationMoveHistoryCapacity;
        foreach (var stack in new[] { _undoHistory, _redoHistory })
        { int excess = Math.Max(0, stack.Count - capacity); retired.AddRange(stack.Take(excess)); stack.RemoveRange(0, excess); }
        return retired;
    }
    private async Task<FileTransferResult?> RunAsync(Func<Task<FileTransferResult?>> action)
    {
        if (Interlocked.CompareExchange(ref _isBusy, 1, 0) != 0) return null;
        Error = null; StateChanged?.Invoke(this, EventArgs.Empty);
        try { return await action(); }
        catch (OperationCanceledException) { return null; }
        catch (Exception ex) { Error = ex.Message; return null; }
        finally { Interlocked.Exchange(ref _isBusy, 0); StateChanged?.Invoke(this, EventArgs.Empty); }
    }
    private async Task ReleaseSafelyAsync(FileTransferResult record)
    { try { await backend.ReleaseAsync(record); } catch (Exception ex) { Error = "文件操作成功，恢复材料清理失败：" + ex.Message; } }
}
