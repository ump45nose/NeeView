using System.Threading.Channels;
using NeeView.Core;

namespace NeeView.Application;

/// <summary>窗口级阅读会话，所有状态写入经过单读者消息队列。</summary>
public sealed class ReaderSession : IReaderSession
{
    private readonly IContentSourceFactory _factory;
    private readonly IReaderStateStore _states;
    private readonly ISettingsStore _settings;
    private readonly Channel<Func<Task>> _messages = Channel.CreateUnbounded<Func<Task>>(new() { SingleReader = true });
    private readonly Task _pump;
    private CancellationTokenSource? _opening;
    private CancellationTokenSource? _saveDelay;
    private long _openVersion;
    private bool _disposed;
    public ReaderSnapshot Snapshot { get; private set; } = new(0, null, null, null, new(), false, null);
    public IContentSource? Source { get; private set; }
    public event Action<ReaderSnapshot>? Changed;
    public ReaderSession(IContentSourceFactory factory, IReaderStateStore states, ISettingsStore settings)
    {
        _factory = factory; _states = states; _settings = settings;
        _pump = Task.Run(async () => { await foreach (var message in _messages.Reader.ReadAllAsync()) await message(); });
    }
    /// <summary>排队状态修改；返回任务向调用方传播异常。</summary>
    private Task Enqueue(Func<Task> action)
    {
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        if (!_messages.Writer.TryWrite(async () =>
        {
            try { await action(); completion.TrySetResult(); }
            catch (Exception error) { completion.TrySetException(error); }
        })) completion.TrySetException(new ObjectDisposedException(nameof(ReaderSession)));
        return completion.Task;
    }
    /// <summary>发布只读快照；订阅者负责切换到 UI 线程。</summary>
    private void Publish(ReaderSnapshot snapshot)
    {
        Snapshot = snapshot;
        Changed?.Invoke(snapshot);
    }
    /// <summary>输入打开请求，取消旧打开并在新来源成功后原子替换。</summary>
    public Task OpenAsync(OpenRequest request, CancellationToken token = default)
    {
        var version = Interlocked.Increment(ref _openVersion);
        var cancellation = CancellationTokenSource.CreateLinkedTokenSource(token);
        Interlocked.Exchange(ref _opening, cancellation)?.Cancel();
        return Enqueue(async () =>
        {
            IContentSource? candidate = null;
            try
            {
                if (version != _openVersion) return;
                await SaveCurrentAsync();
                Publish(Snapshot with { Loading = true, Error = null });
                candidate = await _factory.OpenAsync(request, cancellation.Token);
                var index = await candidate.IndexAsync(cancellation.Token);
                var settings = await _settings.LoadAsync(cancellation.Token);
                var saved = await _states.GetAsync(index.Book, cancellation.Token);
                cancellation.Token.ThrowIfCancellationRequested();
                if (version != _openVersion) return;
                var options = OptionRestore.Mix(settings.Defaults, Snapshot.Options, saved?.Options, settings.RestorePolicies);
                index = index with { Pages = PageOrdering.Sort(index.Pages, options) };
                var anchor = index.RequestedContent is { } requested ? new ReadingAnchor(requested) : saved?.Anchor;
                if (anchor is null && saved?.LegacyPage is { } legacy)
                {
                    var page = index.Pages.FirstOrDefault(p => p.Name.Replace('\\', '/') == legacy.Replace('\\', '/')
                        || p.Locator.Entry?.Replace('\\', '/') == legacy.Replace('\\', '/'));
                    if (page is not null) anchor = new(page.Id);
                }
                if (!index.Pages.Any(p => p.Id == anchor?.Content)) anchor = index.Pages.Count > 0 ? new(index.Pages[0].Id) : null;
                var previous = Source; Source = candidate; candidate = null;
                Publish(new(Snapshot.Generation + 1, index, anchor, null, options, false, null));
                if (previous is not null) await previous.DisposeAsync();
                await _settings.SaveAsync(settings with { LastSource = index.Locator.Path });
            }
            catch (OperationCanceledException) { if (version == _openVersion) Publish(Snapshot with { Loading = false }); }
            catch (Exception error) { if (version == _openVersion) Publish(Snapshot with { Loading = false, Error = ReaderException.From(error).Message }); }
            finally
            {
                if (candidate is not null) await candidate.DisposeAsync();
                Interlocked.CompareExchange(ref _opening, null, cancellation);
                cancellation.Dispose();
            }
        });
    }
    /// <summary>按阅读 frame 或单资源导航，锚点保持内容身份。</summary>
    public Task NavigateAsync(int direction, bool onePage = false) => Enqueue(() =>
    {
        if (Snapshot.Index is { } index) Publish(Snapshot with { Anchor = ReadingRules.Navigate(index.Pages, Snapshot.Anchor, Snapshot.Options, direction, onePage) });
        ScheduleSave(); return Task.CompletedTask;
    });
    /// <summary>定位内容或更新显式选择；不以滚动锚点替代分类选择。</summary>
    public Task LocateAsync(ReadingAnchor anchor, bool select = false) => Enqueue(() =>
    {
        if (Snapshot.Index?.Pages.Any(p => p.Id == anchor.Content) == true)
            Publish(Snapshot with { Anchor = anchor, Selection = select ? anchor.Content : Snapshot.Selection });
        ScheduleSave(); return Task.CompletedTask;
    });
    /// <summary>修改模式与排序，保持锚点内容。</summary>
    public Task SetOptionsAsync(ReaderOptions options) => Enqueue(() =>
    {
        var index = Snapshot.Index is { } current ? current with { Pages = PageOrdering.Sort(current.Pages, options) } : null;
        Publish(Snapshot with { Options = options, Index = index }); ScheduleSave(); return Task.CompletedTask;
    });
    /// <summary>仅接受当前代次的尺寸探测结果。</summary>
    public Task ReportSizeAsync(ContentId content, PixelSize size, long generation) => Enqueue(() =>
    {
        if (Snapshot.Generation != generation || Snapshot.Index is not { } index) return Task.CompletedTask;
        var page = index.Pages.FirstOrDefault(p => p.Id == content);
        if (page is null || page.Size == size) return Task.CompletedTask;
        Publish(Snapshot with { Index = index with { Pages = index.Pages.Select(p => p.Id == content ? p with { Size = size } : p).ToArray() } });
        return Task.CompletedTask;
    });
    /// <summary>文件操作后重建索引；移除当前项时选择下一项，末尾退到上一项。</summary>
    public Task RefreshAsync(ContentId? prefer = null) => Enqueue(async () =>
    {
        if (Source is null) return;
        var old = Snapshot.Index?.Pages.ToList().FindIndex(p => p.Id == Snapshot.Anchor?.Content) ?? 0;
        var fresh = await Source.IndexAsync(CancellationToken.None);
        var sizes = Snapshot.Index?.Pages.ToDictionary(p => p.Id, p => p.Size) ?? [];
        fresh = fresh with { Pages = PageOrdering.Sort(fresh.Pages.Select(p => p with { Size = sizes.GetValueOrDefault(p.Id) }), Snapshot.Options) };
        var chosen = prefer ?? Snapshot.Anchor?.Content;
        var page = fresh.Pages.FirstOrDefault(p => p.Id == chosen) ?? fresh.Pages.ElementAtOrDefault(Math.Clamp(old, 0, Math.Max(0, fresh.Pages.Count - 1)));
        Publish(Snapshot with { Index = fresh, Anchor = page is null ? null : new(page.Id), Selection = null });
        await SaveCurrentAsync();
    });
    /// <summary>一秒防抖保存；保存消息仍在会话队列执行。</summary>
    private void ScheduleSave()
    {
        _saveDelay?.Cancel(); _saveDelay?.Dispose(); _saveDelay = new(); var token = _saveDelay.Token;
        _ = Task.Run(async () =>
        {
            try { await Task.Delay(1000, token); await Enqueue(SaveCurrentAsync); }
            catch (OperationCanceledException) { }
            catch (ObjectDisposedException) { }
        });
    }
    /// <summary>将当前书籍身份、锚点和设置立即持久化。</summary>
    private Task SaveCurrentAsync() => Snapshot.Index is { } index
        ? _states.SaveAsync(new(index.Book, index.Locator, Snapshot.Anchor, Snapshot.Options, DateTimeOffset.UtcNow)) : Task.CompletedTask;
    public Task FlushAsync() => Enqueue(SaveCurrentAsync);
    /// <summary>关闭来源和消息队列；持久化完成后才释放。</summary>
    public async ValueTask DisposeAsync()
    {
        if (_disposed) return; _disposed = true;
        try { _opening?.Cancel(); } catch (ObjectDisposedException) { }
        _saveDelay?.Cancel(); await FlushAsync();
        _messages.Writer.TryComplete(); await _pump;
        if (Source is not null) await Source.DisposeAsync();
        _saveDelay?.Dispose();
    }
}
