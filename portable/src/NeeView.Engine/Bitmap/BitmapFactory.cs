namespace NeeView;

/// <summary>原图像工厂的后端适配与字节缓存，显示资源由租约统一计入预算。</summary>
public sealed class BitmapFactory(IImageDecoder decoder) : IDisposable
{
    private sealed class Entry(DecodedImageLease image, bool thumbnail)
    {
        public DecodedImageLease Image { get; } = image;
        public bool IsThumbnail { get; } = thumbnail;
        public int References;
        public int WaitingConsumers;
        public long DisplayBytes;
        public long Used;
        public bool Cached = true;
    }
    private sealed class Pending
    {
        public Task<Entry> Task { get; set; } = null!;
        public int Consumers;
        public Entry? Result;
        public CancellationTokenSource Cancellation { get; set; } = null!;
        public bool Finished;
    }
    private readonly record struct Key(Archive Archive, int Id, long Length, DateTime Version, int Width, int Height, bool Thumbnail);
    private readonly object _sync = new();
    private readonly Dictionary<Key, Entry> _cache = [];
    private readonly Dictionary<Key, Pending> _pending = [];
    private readonly SemaphoreSlim _decodeSlots = new(2);
    private readonly SemaphoreSlim _backgroundSlot = new(1);
    private readonly CancellationTokenSource _lifetime = new();
    private bool _disposed;
    private long _clock;
    public long Budget { get; set; } = 512L * 1024 * 1024;
    public long ThumbnailBudget { get; set; } = 64L * 1024 * 1024;
    public long ByteCount { get { lock (_sync) return _cache.Values.Sum(e => e.Image.ByteCount + e.DisplayBytes); } }

    /// <summary>合并相同规格读取，取消当前需求不影响其他共享需求。</summary>
    public async Task<BitmapLease> GetAsync(Page page, DecodeRequest request, CancellationToken token, bool background = false)
    {
        var entry = page.ArchiveEntry;
        token.ThrowIfCancellationRequested();
        var key = new Key(entry.Archive, entry.Id, entry.Length, entry.LastWriteTime, request.TargetWidth, request.TargetHeight, request.IsThumbnail);
        Pending work;
        lock (_sync)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_cache.TryGetValue(key, out var cached)) return Rent(cached);
            if (!_pending.TryGetValue(key, out work!))
            {
                if (_pending.Count >= 512) throw new InvalidOperationException("图像加载队列已满，请稍后重试。");
                work = new Pending { Cancellation = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token) }; _pending[key] = work;
                // 任务返回的像素在等待者取得租约前也受保护，其他请求的 Trim 不能回收。
                var pending = work;
                work.Task = Task.Run(() => LoadAsync(key, page, request, background, pending));
                _ = ObserveAsync(work.Task);
            }
            work.Consumers++;
        }
        try
        {
            var result = await work.Task.WaitAsync(token);
            lock (_sync)
            {
                ObjectDisposedException.ThrowIf(_disposed, this);
                return Rent(result);
            }
        }
        finally
        {
            lock (_sync)
            {
                work.Consumers--;
                if (work.Consumers == 0 && work.Result is null && !work.Finished)
                {
                    // 无消费者时取消排队工作；原生晚到结果清理，同键重提创建新工作。
                    RemovePending(key, work); work.Cancellation.Cancel();
                }
                if (work.Result is { } result)
                {
                    result.WaitingConsumers--;
                    if (!result.Cached && result.References == 0 && result.WaitingConsumers == 0) result.Image.Dispose();
                }
                Trim();
            }
        }
    }
    /// <summary>即使所有等待者已取消，也观察共享后台任务的异常。</summary>
    private static async Task ObserveAsync(Task<Entry> task)
    {
        try { await task; } catch { /* 实际等待者收到错误；无人等待的任务仍需观察。 */ }
    }
    /// <summary>按需读取及解码；背景任务最多占一个解码槽。</summary>
    private async Task<Entry> LoadAsync(Key key, Page page, DecodeRequest request, bool background, Pending pending)
    {
        bool backgroundHeld = false, decodeHeld = false;
        try
        {
            if (background) { await _backgroundSlot.WaitAsync(pending.Cancellation.Token); backgroundHeld = true; }
            await _decodeSlots.WaitAsync(pending.Cancellation.Token); decodeHeld = true;
            if (!page.IsImage && !page.PageType.IsFolder()) throw new NotSupportedException("这个文件类型的查看器尚未迁移。");
            await using var cover = page.PageType.IsFolder() ? await ArchivePageUtility.GetSelectedPageAsync(page, pending.Cancellation.Token) : null;
            var entry = cover is not null ? cover.Entry ?? throw new EmptyArchivePageException() : page.ArchiveEntry;
            await using var stream = await entry.Archive.OpenEntryAsync(entry, pending.Cancellation.Token);
            var image = await decoder.DecodeAsync(stream, request, pending.Cancellation.Token);
            var result = new Entry(image, request.IsThumbnail);
            lock (_sync)
            {
                if (_disposed || pending.Cancellation.IsCancellationRequested) { image.Dispose(); throw new OperationCanceledException(); }
                result.Used = ++_clock; result.WaitingConsumers = pending.Consumers; pending.Result = result;
                _cache[key] = result; RemovePending(key, pending); Trim();
                // 当前新结果先保留；调用方取得租约后再按预算回收。
                return result;
            }
        }
        catch { lock (_sync) RemovePending(key, pending); throw; }
        finally
        {
            if (decodeHeld) _decodeSlots.Release(); if (backgroundHeld) _backgroundSlot.Release();
            lock (_sync) { pending.Finished = true; pending.Cancellation.Dispose(); }
        }
    }
    /// <summary>只撤销当前工作的登记，旧取消任务不能移除同键的新需求。</summary>
    private void RemovePending(Key key, Pending pending)
    {
        if (_pending.TryGetValue(key, out var current) && ReferenceEquals(current, pending)) _pending.Remove(key);
    }
    /// <summary>缓存中的像素共享，引用和显示字节由当前租约持有。</summary>
    private BitmapLease Rent(Entry entry)
    {
        entry.References++; entry.Used = ++_clock; Trim();
        return new(entry.Image, bytes => { lock (_sync) { entry.DisplayBytes += bytes; Trim(); } }, bytes =>
        {
            lock (_sync)
            {
                entry.DisplayBytes -= bytes; entry.References--;
                if (!entry.Cached && entry.References == 0 && entry.WaitingConsumers == 0) entry.Image.Dispose();
                Trim();
            }
        });
    }
    /// <summary>只回收没有显示租约的旧像素，当前显示资源不会提前释放。</summary>
    private void Trim()
    {
        var main = _cache.Values.Where(e => !e.IsThumbnail).Sum(e => e.Image.ByteCount + e.DisplayBytes);
        var thumbnails = _cache.Values.Where(e => e.IsThumbnail).Sum(e => e.Image.ByteCount + e.DisplayBytes);
        foreach (var pair in _cache.OrderBy(e => e.Value.Used).ToArray())
        {
            var entry = pair.Value;
            if ((entry.IsThumbnail ? thumbnails <= ThumbnailBudget : main <= Budget) || entry.References != 0 || entry.WaitingConsumers != 0) continue;
            if (entry.IsThumbnail) thumbnails -= entry.Image.ByteCount + entry.DisplayBytes; else main -= entry.Image.ByteCount + entry.DisplayBytes;
            _cache.Remove(pair.Key); entry.Cached = false; entry.Image.Dispose();
        }
    }
    /// <summary>取消队列并释放未被显示持有的资源；晚到解码自行清理。</summary>
    public void Dispose()
    {
        lock (_sync)
        {
            if (_disposed) return; _disposed = true; _lifetime.Cancel();
            foreach (var entry in _cache.Values) { entry.Cached = false; if (entry.References == 0 && entry.WaitingConsumers == 0) entry.Image.Dispose(); }
            _cache.Clear();
        }
    }
}

/// <summary>共享像素租约，Avalonia Bitmap 必须先释放再归还租约。</summary>
public sealed class BitmapLease(DecodedImageLease image, Action<long> addDisplay, Action<long> release) : IDisposable
{
    private long _displayBytes;
    private bool _disposed;
    public DecodedImageLease Image { get; } = image;
    /// <summary>登记实际显示缓冲字节，纳入工厂的同一预算。</summary>
    public void RegisterDisplayBytes(long bytes) { _displayBytes += bytes; addDisplay(bytes); }
    /// <summary>归还引用与显示字节；重复释放不影响其他租约。</summary>
    public void Dispose() { if (_disposed) return; _disposed = true; release(_displayBytes); }
}
