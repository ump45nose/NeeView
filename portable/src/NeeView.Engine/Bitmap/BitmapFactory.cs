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
    private readonly record struct Key(object Source, int Id, long Length, DateTime Version, int Width, int Height, bool Thumbnail, string? CoverSelection = null, long CoverRevision = 0);
    private readonly object _sync = new();
    private readonly Dictionary<Key, Entry> _cache = [];
    private readonly HashSet<Entry> _retired = [];
    private readonly Dictionary<Key, Pending> _pending = [];
    private readonly SemaphoreSlim _decodeSlots = new(2);
    private readonly SemaphoreSlim _backgroundSlot = new(1);
    private readonly CancellationTokenSource _lifetime = new();
    private bool _disposed;
    private long _clock;
    private long _coverRevision;
    public long Budget { get; set; } = 512L * 1024 * 1024;
    public long ThumbnailBudget { get; set; } = 64L * 1024 * 1024;
    public long ByteCount { get { lock (_sync) return _cache.Values.Concat(_retired).Sum(e => e.Image.ByteCount + e.DisplayBytes); } }

    /// <summary>合并相同规格读取，取消当前需求不影响其他共享需求。</summary>
    public Task<BitmapLease> GetAsync(Page page, DecodeRequest request, CancellationToken token, bool background = false)
    {
        var entry = page.ArchiveEntry;
        var key = new Key(entry.Archive, entry.Id, entry.Length, entry.LastWriteTime, request.TargetWidth, request.TargetHeight, request.IsThumbnail);
        return GetCoreAsync(key, request, token, background, cancellation =>
        {
            if (!page.IsImage && !page.PageType.IsFolder()) throw new NotSupportedException("这个文件类型的查看器尚未迁移。");
            return page.PageType.IsFolder() ? ArchivePageUtility.GetSelectedPageAsync(page, cancellation) : Task.FromResult(ArchivePageCover.Borrow(entry));
        });
    }
    /// <summary>列表封面使用稳定原路径/版本/选择规格，复用同一解码槽和缩略预算，不缓存来源对象。</summary>
    /// <param name="path">原来源定位。</param><param name="archives">已有来源替换点。</param><param name="folders">原封面配置。</param>
    /// <param name="request">设备像素缩略规格。</param><param name="token">当前可见消费者取消。</param>
    public async Task<BitmapLease> GetCoverAsync(string path, IArchiveFactory archives, FolderConfigCollection folders, DecodeRequest request, CancellationToken token)
    {
        var revision = Interlocked.Read(ref _coverRevision);
        var metadata = await archives.GetFileMetadataAsync(path, token);
        if (metadata is null)
        {
            // 包内定位的真实版本属于根归档；这个短期来源只用于解析，不进入缓存键/长期字典。
            await using var source = await archives.OpenAsync(path, token);
            metadata = await archives.GetFileMetadataAsync(source.RootArchivePath, token);
        }
        var selection = $"{folders.GetThumbnailTarget(path)}\n{Config.Current.Book.BookThumbnailRegex}\n{Config.Current.Book.BookThumbnailDepth}";
        var key = new Key(path, 0, metadata?.Length ?? -1, metadata?.LastWriteTime ?? default, request.TargetWidth, request.TargetHeight, true, selection, revision);
        return await GetCoreAsync(key, request with { IsThumbnail = true }, token, true,
            cancellation => ArchivePageUtility.GetSelectedPageAsync(path, archives, folders, cancellation));
    }
    /// <summary>显式刷新重探封面；仍显示的旧租约不提前释放，过期后台结果不进入新缓存。</summary>
    public void InvalidateCovers()
    {
        lock (_sync)
        {
            ++_coverRevision;
            foreach (var pair in _cache.Where(e => e.Key.Source is string).ToArray())
            { _cache.Remove(pair.Key); pair.Value.Cached = false; if (pair.Value.References == 0 && pair.Value.WaitingConsumers == 0) pair.Value.Image.Dispose(); else _retired.Add(pair.Value); }
            foreach (var pair in _pending.Where(e => e.Key.Source is string).ToArray())
            { RemovePending(pair.Key, pair.Value); pair.Value.Cancellation.Cancel(); }
        }
    }
    /// <summary>正文和路径封面合并同规格需求，共享取消/晚到/租约保护。</summary>
    private async Task<BitmapLease> GetCoreAsync(Key key, DecodeRequest request, CancellationToken token, bool background, Func<CancellationToken, Task<ArchivePageCover>> resolve)
    {
        token.ThrowIfCancellationRequested();
        Pending work;
        lock (_sync)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (key.Source is string && key.CoverRevision != _coverRevision) throw new OperationCanceledException("封面已刷新。");
            if (_cache.TryGetValue(key, out var cached)) return Rent(cached);
            if (!_pending.TryGetValue(key, out work!))
            {
                if (_pending.Count >= 512) throw new InvalidOperationException("图像加载队列已满，请稍后重试。");
                work = new Pending { Cancellation = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token) }; _pending[key] = work;
                // 任务返回的像素在等待者取得租约前也受保护，其他请求的 Trim 不能回收。
                var pending = work;
                work.Task = Task.Run(() => LoadAsync(key, request, background, pending, resolve));
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
                    if (!result.Cached && result.References == 0 && result.WaitingConsumers == 0) { _retired.Remove(result); result.Image.Dispose(); }
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
    private async Task<Entry> LoadAsync(Key key, DecodeRequest request, bool background, Pending pending, Func<CancellationToken, Task<ArchivePageCover>> resolve)
    {
        bool backgroundHeld = false, decodeHeld = false;
        try
        {
            if (background) { await _backgroundSlot.WaitAsync(pending.Cancellation.Token); backgroundHeld = true; }
            await _decodeSlots.WaitAsync(pending.Cancellation.Token); decodeHeld = true;
            await using var cover = await resolve(pending.Cancellation.Token);
            var entry = cover.Entry ?? throw new EmptyArchivePageException();
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
                if (!entry.Cached && entry.References == 0 && entry.WaitingConsumers == 0) { _retired.Remove(entry); entry.Image.Dispose(); }
                Trim();
            }
        });
    }
    /// <summary>只回收没有显示租约的旧像素，当前显示资源不会提前释放。</summary>
    private void Trim()
    {
        var main = _cache.Values.Concat(_retired).Where(e => !e.IsThumbnail).Sum(e => e.Image.ByteCount + e.DisplayBytes);
        var thumbnails = _cache.Values.Concat(_retired).Where(e => e.IsThumbnail).Sum(e => e.Image.ByteCount + e.DisplayBytes);
        // 未超预算时无需分配/排序完整LRU；保留原回收顺序和租约保护。
        if (main <= Budget && thumbnails <= ThumbnailBudget) return;
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
            foreach (var entry in _cache.Values) { entry.Cached = false; if (entry.References == 0 && entry.WaitingConsumers == 0) entry.Image.Dispose(); else _retired.Add(entry); }
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
