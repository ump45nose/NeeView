// Copyright (c) NeeLaboratory. 原 PageViewRecorder 的 TSV 与停留边界，MIT。
using System.Globalization;
using System.Text;
using System.Threading.Channels;
namespace NeeView;

/// <summary>状态变更在短锁内拍照；仅后台单读取者持有文件，界面不等待磁盘。</summary>
public sealed class PageViewRecorder : IAsyncDisposable
{
    private sealed record Work(string? Path, string? Row = null, TaskCompletionSource? Completion = null);
    private sealed record ViewedPage(Page Source, string Type, string Entry);
    private readonly object _sync = new();
    private readonly Channel<Work> _queue = Channel.CreateBounded<Work>(new BoundedChannelOptions(512)
        { FullMode = BoundedChannelFullMode.Wait, SingleReader = true, SingleWriter = false });
    private readonly Func<DateTime> _clock;
    private readonly Action<Exception>? _error;
    private readonly Task _writer;
    private BookOperation? _operation;
    private Book? _book;
    private ViewedPage[] _pages = [];
    private string? _path, _bookPath, _bookName;
    private DateTime _bookStarted, _pagesStarted;
    private bool _disposed;
    private readonly TaskCompletionSource _closed = new(TaskCreationOptions.RunContinuationsAsynchronously);
    /// <summary>时钟用于停留回归；错误回调即使失败也不能打断阅读或终止文件队列。</summary>
    public PageViewRecorder(Func<DateTime>? clock = null, Action<Exception>? error = null)
    { _clock = clock ?? (() => DateTime.Now); _error = error; _writer = Task.Run(WriteLoopAsync); }
    /// <summary>观察唯一阅读控制器；关闭后不重新附着或持有页面。</summary>
    public void Attach(BookOperation operation)
    {
        lock (_sync)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_operation is not null) _operation.Changed -= Changed;
            _operation = operation; operation.Changed += Changed; Synchronize();
        }
    }
    private void Changed(object? sender, EventArgs e) => Synchronize();
    private void Synchronize()
    {
        lock (_sync)
        {
            if (_disposed || _operation is not { } op) return;
            Configure(Config.Current.PageViewRecorder);
            if (_path is null || op.IsLoading) return;
            if (!ReferenceEquals(_book, op.Book)) { RecordPages([]); RecordBook(op.Book); _book = op.Book; }
            var pages = op.Book?.CurrentPages.ToArray() ?? [];
            if (!_pages.Select(p => p.Source).SequenceEqual(pages)) RecordPages(pages);
        }
    }
    /// <summary>切换前把旧停留记录交给旧文件；无效路径只回报，不传播到阅读事件。</summary>
    public void Configure(PageViewRecorderConfig config)
    {
        lock (_sync)
        {
            if (_disposed) return;
            string? path = null;
            try
            {
                if (config.IsSavePageViewRecord && !string.IsNullOrWhiteSpace(config.PageViewRecordFilePath))
                    path = Path.GetFullPath(config.PageViewRecordFilePath);
            }
            catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException) { Report(ex); }
            if (path == _path) return;
            var now = _clock(); FlushPages(now); FlushBook(now);
            _path = path; _book = null; _bookPath = null; _bookName = null; _pages = [];
            Enqueue(new(path));
        }
    }
    /// <summary>记录旧书并拍新书路径；不由后台线程访问活动书籍。</summary>
    public void RecordBook(Book? book)
    {
        lock (_sync)
        {
            if (_disposed) return;
            var now = _clock(); FlushBook(now); _bookStarted = now; _bookPath = book?.Path;
            _bookName = book is null ? null : Path.GetFileName(book.Path.TrimEnd('/'));
        }
    }
    /// <summary>页框选择变化记录停留；不可变名称与类型不随异步来源更新改变。</summary>
    public void RecordPages(IEnumerable<Page>? pages)
    {
        lock (_sync)
        {
            if (_disposed) return;
            var now = _clock(); FlushPages(now); _pagesStarted = now;
            _pages = pages?.Select(p => new ViewedPage(p, p.PageType.ToString(), p.EntryName)).ToArray() ?? [];
        }
    }
    private void FlushBook(DateTime now, List<Work>? final = null)
    { if (_path is not null && _bookPath is not null) Submit(new(_path, Row(_bookStarted, "Book", now - _bookStarted, _bookPath, _bookName ?? "")), final); }
    private void FlushPages(DateTime now, List<Work>? final = null)
    {
        if (_path is null) return;
        foreach (var page in _pages) Submit(new(_path, Row(_pagesStarted, page.Type, now - _pagesStarted, (_bookPath ?? "").TrimEnd('/'), page.Entry)), final);
    }
    private void Submit(Work work, List<Work>? final) { if (final is null) Enqueue(work); else final.Add(work); }
    private static string Row(DateTime at, string type, TimeSpan duration, string book, string entry) => string.Join('\t',
        at.ToString("O", CultureInfo.InvariantCulture), type, duration.TotalSeconds.ToString("#0.0000000", CultureInfo.InvariantCulture), book, entry) + Environment.NewLine;
    private void Enqueue(Work work)
    { if (!_queue.Writer.TryWrite(work)) Report(new IOException("页面记录队列已满；本条记录未写入，请降低记录频率或检查磁盘。")); }
    private void Report(Exception error) { try { _error?.Invoke(error); } catch { /* 诊断订阅者不能打断阅读或单写入者。 */ } }
    /// <summary>等待此前入队行完成；关闭并发时等待相同排空任务，不向已关闭队列写入。</summary>
    public async Task FlushAsync()
    {
        Task? queued = null; TaskCompletionSource? completion = null;
        lock (_sync)
        {
            if (!_disposed)
            {
                completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
                queued = _queue.Writer.WriteAsync(new(_path, Completion: completion)).AsTask();
            }
        }
        if (queued is null) { await _writer; return; }
        try { await queued; await completion!.Task; }
        catch (ChannelClosedException) { await _writer; }
    }
    private async Task WriteLoopAsync()
    {
        StreamWriter? stream = null; string? path = null;
        try
        {
            await foreach (var work in _queue.Reader.ReadAllAsync())
            {
                try
                {
                    if (path != work.Path)
                    { if (stream is not null) await stream.DisposeAsync(); stream = null; path = work.Path; }
                    if (work.Row is not null && path is not null)
                    {
                        // 原Append不隐式创建父目录；仅这个消费者持有写句柄。
                        stream ??= new(new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.Read, 4096, FileOptions.Asynchronous), new UTF8Encoding(false));
                        await stream.WriteAsync(work.Row); await stream.FlushAsync();
                    }
                    if (work.Completion is not null && stream is not null) await stream.FlushAsync();
                }
                catch (Exception ex)
                { Report(ex); if (stream is not null) { try { await stream.DisposeAsync(); } catch (Exception closeError) { Report(closeError); } stream = null; } }
                finally { work.Completion?.TrySetResult(); }
            }
        }
        finally { if (stream is not null) { try { await stream.DisposeAsync(); } catch (Exception ex) { Report(ex); } } }
    }
    /// <summary>仅一次补当前行、退订并关闭队列；重复/并发关闭等待同一后台排空。</summary>
    public async ValueTask DisposeAsync()
    {
        List<Work>? final = null;
        lock (_sync)
        {
            if (!_disposed)
            {
                _disposed = true;
                if (_operation is not null) _operation.Changed -= Changed;
                // 终止快照不使用TryWrite：队列满时异步等待容量，不能丢最后停留行。
                final = []; var now = _clock(); FlushPages(now, final); FlushBook(now, final);
                _pages = []; _book = null; _operation = null;
            }
        }
        if (final is null) { await _closed.Task; return; }
        try
        {
            foreach (var work in final) await _queue.Writer.WriteAsync(work);
            _queue.Writer.TryComplete(); await _writer; _closed.TrySetResult();
        }
        catch (Exception error) { _queue.Writer.TryComplete(error); _closed.TrySetException(error); throw; }
    }
}
