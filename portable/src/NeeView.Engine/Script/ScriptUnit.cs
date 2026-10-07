// Copyright (c) NeeLaboratory. 原ScriptUnit的独立取消与任务资源，MIT。
namespace NeeView;
/// <summary>先登记再开始的脚本调用；完成必释放CTS，快速完成不会遗留池条目。</summary>
public sealed class ScriptUnit
{
    private readonly object _gate = new();
    private CancellationTokenSource? _cancellation;
    public Task<object?> Task { get; private set; } = System.Threading.Tasks.Task.FromResult<object?>(null);
    internal ScriptUnit(CancellationToken token) => _cancellation = CancellationTokenSource.CreateLinkedTokenSource(token);
    internal void Start(Func<CancellationToken, object?> action)
    {
        var token = _cancellation!.Token;
        Task = System.Threading.Tasks.Task.Run(() =>
        { try { return action(token); } finally { lock (_gate) { _cancellation?.Dispose(); _cancellation = null; } } });
    }
    public void Cancel() { lock (_gate) _cancellation?.Cancel(); }
}
