using System.Diagnostics;

namespace NeeView.Application;

/// <summary>优先队列、重复请求合并和按实际字节回收的像素缓存。</summary>
public sealed class ImageScheduler : IImageRequestScheduler
{
    private sealed class Entry(DecodedImageLease image) { public DecodedImageLease Image = image; public int Leases; public long Used; }
    private sealed class Work(ImageCacheKey key, IContentSource source, DecodeRequest request, ImagePriority priority)
    {
        public ImageCacheKey Key = key;
        public IContentSource Source = source;
        public DecodeRequest Request = request;
        public ImagePriority Priority = priority;
        public TaskCompletionSource<Entry> Completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public CancellationTokenSource Cancellation = new();
        public int Waiters;
        public bool Claimed;
    }
    private readonly object _gate = new();
    private readonly IImageDecoder _decoder;
    private readonly Dictionary<ImageCacheKey, Entry> _cache = [];
    private readonly Dictionary<ImageCacheKey, Work> _pending = [];
    private readonly PriorityQueue<Work, (int Priority, long Sequence)> _queue = new();
    private readonly SemaphoreSlim _signal = new(0);
    private readonly SemaphoreSlim _background = new(1);
    private readonly CancellationTokenSource _shutdown = new();
    private readonly Task[] _workers;
    private readonly long _budget;
    private readonly long _thumbnailBudget;
    private long _sequence;
    private bool _disposed;
    public long CachedBytes { get { lock (_gate) return _cache.Values.Sum(e => e.Image.ByteCount); } }
    public ImageScheduler(IImageDecoder decoder, long budget = 512L * 1024 * 1024, long thumbnailBudget = 64L * 1024 * 1024)
    {
        _decoder = decoder; _budget = budget; _thumbnailBudget = thumbnailBudget;
        _workers = [Task.Run(() => WorkerAsync(true)), Task.Run(() => WorkerAsync(false))];
    }
    /// <summary>提交需求，调用者独立取消；最后一个等待者取消后中止排队/读取。</summary>
    public async Task<DecodedImageLease> RequestAsync(IContentSource source, DecodeRequest request, ImagePriority priority, CancellationToken token)
    {
        var key = new ImageCacheKey(request.Page.Id, request.Page.Version, request.TargetWidth, request.TargetHeight, request.Thumbnail);
        Work work;
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_cache.TryGetValue(key, out var cached)) return Lease(key, cached);
            if (!_pending.TryGetValue(key, out work!))
            {
                // 可见需求不会无限积累；调用方随视口取消旧需求。
                if (_pending.Count >= 512) throw new ReaderException(FailureKind.Unavailable, "图像请求队列已满。");
                work = new(key, source, request, priority); _pending.Add(key, work);
                _queue.Enqueue(work, ((int)priority, _sequence++)); _signal.Release();
            }
            else if (priority < work.Priority)
            {
                work.Priority = priority;
                _queue.Enqueue(work, ((int)priority, _sequence++)); _signal.Release();
            }
            work.Waiters++;
        }
        try
        {
            var entry = await work.Completion.Task.WaitAsync(token);
            lock (_gate) { ObjectDisposedException.ThrowIf(_disposed, this); return Lease(key, entry); }
        }
        finally
        {
            lock (_gate)
            {
                work.Waiters--;
                if (work.Waiters == 0 && !work.Completion.Task.IsCompleted) work.Cancellation.Cancel();
                if (work.Waiters == 0 && work.Completion.Task.IsCompleted) _pending.Remove(key);
                Trim();
            }
        }
    }
    /// <summary>增加引用，归还回调触发预算回收。</summary>
    private DecodedImageLease Lease(ImageCacheKey key, Entry entry)
    {
        entry.Leases++; entry.Used = ++_sequence;
        return entry.Image.Share(() => { lock (_gate) { entry.Leases--; Trim(); } });
    }
    /// <summary>只回收无人显示且没有等待者的缓存项。</summary>
    private void Trim()
    {
        foreach (var thumbnail in new[] { false, true })
        {
            var bytes = _cache.Where(e => e.Key.Thumbnail == thumbnail).Sum(e => e.Value.Image.ByteCount);
            var budget = thumbnail ? _thumbnailBudget : _budget;
            foreach (var pair in _cache.Where(e => e.Key.Thumbnail == thumbnail && e.Value.Leases == 0
                && (!_pending.TryGetValue(e.Key, out var pending) || pending.Waiters == 0)).OrderBy(e => e.Value.Used).ToArray())
            {
                if (bytes <= budget) break;
                _cache.Remove(pair.Key); bytes -= pair.Value.Image.ByteCount; pair.Value.Image.Dispose();
            }
        }
    }
    /// <summary>按优先级执行；后台仅占一个槽，保留另一槽给当前页。</summary>
    private async Task WorkerAsync(bool allowBackground)
    {
        try
        {
            while (true)
            {
                await _signal.WaitAsync(_shutdown.Token);
                Work? work = null;
                lock (_gate)
                {
                    var skipped = new List<(Work Work, (int Priority, long Sequence) Priority)>();
                    while (_queue.TryDequeue(out var candidate, out var priority))
                    {
                        if (candidate.Claimed || candidate.Completion.Task.IsCompleted) continue;
                        if (!allowBackground && candidate.Priority >= ImagePriority.Prefetch) { skipped.Add((candidate, priority)); continue; }
                        work = candidate; candidate.Claimed = true; break;
                    }
                    foreach (var skippedWork in skipped) _queue.Enqueue(skippedWork.Work, skippedWork.Priority);
                }
                if (work is null) continue;
                var background = work.Priority >= ImagePriority.Prefetch;
                var acquired = false; var watch = Stopwatch.StartNew();
                try
                {
                    if (background) { await _background.WaitAsync(work.Cancellation.Token); acquired = true; }
                    work.Cancellation.Token.ThrowIfCancellationRequested();
                    await using var stream = await work.Source.OpenReadAsync(work.Request.Page, work.Cancellation.Token);
                    var image = await _decoder.DecodeAsync(stream, work.Request, work.Cancellation.Token);
                    lock (_gate)
                    {
                        if (_disposed || work.Cancellation.IsCancellationRequested) { image.Dispose(); work.Completion.TrySetCanceled(); }
                        else { var entry = new Entry(image) { Used = ++_sequence }; _cache[work.Key] = entry; work.Completion.TrySetResult(entry); }
                    }
                    Trace.WriteLine($"decode {watch.ElapsedMilliseconds}ms cache={CachedBytes} key={work.Key.Content.Value}");
                }
                catch (OperationCanceledException) { work.Completion.TrySetCanceled(); }
                catch (Exception error) { work.Completion.TrySetException(ReaderException.From(error)); }
                finally
                {
                    if (acquired) _background.Release();
                    lock (_gate)
                    {
                        if (work.Waiters == 0) _pending.Remove(work.Key);
                        Trim();
                        if (_queue.Count > 0) _signal.Release();
                    }
                }
            }
        }
        catch (OperationCanceledException) { }
    }
    /// <summary>取消排队和进行中的工作，原生完成后释放所有缓存。</summary>
    public async ValueTask DisposeAsync()
    {
        lock (_gate)
        {
            if (_disposed) return; _disposed = true;
            foreach (var work in _pending.Values) { work.Cancellation.Cancel(); work.Completion.TrySetCanceled(); }
            _pending.Clear(); _shutdown.Cancel();
        }
        await Task.WhenAll(_workers);
        lock (_gate) { foreach (var entry in _cache.Values) entry.Image.Dispose(); _cache.Clear(); }
        _shutdown.Dispose();
    }
}
