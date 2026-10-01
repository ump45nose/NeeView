using NeeView.Core;

namespace NeeView.Desktop;

/// <summary>独立后台几何协调器；串行使用增量策略，新需求取消旧需求，无窗口/绘制引用。</summary>
public sealed class ReaderLayoutCoordinator : IAsyncDisposable
{
    private readonly SemaphoreSlim _serial = new(1);
    private readonly MasonryLayout _masonry = new();
    private readonly ContinuousLayout _continuous = new();
    private readonly object _state = new();
    private readonly HashSet<Task> _running = [];
    private CancellationTokenSource? _pending;
    private bool _disposed;
    /// <summary>输入不可变页面/视口快照，后台输出几何；取消和生命周期由调用者观察。</summary>
    /// <param name="input">请求的页面、选项和视口几何，调用方不得在计算期间修改页面集合。</param>
    /// <returns>完成的布局；新需求取代本请求时抛出取消异常。</returns>
    public Task<LayoutSnapshot> CalculateAsync(LayoutInput input)
    {
        CancellationTokenSource? old; Task<LayoutSnapshot> task;
        lock (_state)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            var cancellation = CancellationTokenSource.CreateLinkedTokenSource(input.Cancellation); old = _pending; _pending = cancellation;
            task = CalculateCoreAsync(input, cancellation); _running.Add(task); _ = ObserveAsync(task);
        }
        try { old?.Cancel(); } catch (ObjectDisposedException) { }
        return task;
    }
    /// <summary>计算缓存只被单一后台工作使用，待执行请求取消后不进入线程池重算。</summary>
    private async Task<LayoutSnapshot> CalculateCoreAsync(LayoutInput input, CancellationTokenSource cancellation)
    {
        var acquired = false;
        try
        {
            await _serial.WaitAsync(cancellation.Token); acquired = true;
            return await Task.Run(() => (input.Options.Mode == ReaderMode.Masonry ? (ILayoutStrategy)_masonry : _continuous)
                .Calculate(input with { Cancellation = cancellation.Token }), cancellation.Token);
        }
        finally
        {
            if (acquired) _serial.Release();
            Interlocked.CompareExchange(ref _pending, null, cancellation); cancellation.Dispose();
        }
    }
    /// <summary>取消不再需要的几何；允许已退休取消源与本次取消并发完成。</summary>
    public void CancelPending()
    { try { _pending?.Cancel(); } catch (ObjectDisposedException) { } }
    /// <summary>跟踪全部已注册计算，关闭与新请求注册在同一互斥内裁决。</summary>
    private async Task ObserveAsync(Task task)
    { try { await task; } catch (Exception) { /* 异常由原请求调用者处理，此处只维护生命周期。 */ } finally { lock (_state) _running.Remove(task); } }
    /// <summary>停止新请求并等待活跃计算归还策略访问权。</summary>
    public async ValueTask DisposeAsync()
    {
        Task[] running;
        lock (_state) { if (_disposed) return; _disposed = true; running = _running.ToArray(); }
        CancelPending();
        try { await Task.WhenAll(running); } catch (OperationCanceledException) { }
    }
}
