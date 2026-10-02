// Copyright (c) NeeLaboratory. 原源码关系与业务规则沿用仓库 MIT；P1子集适配见 docs/source-migration.json。
using NeeView.PageFrames;
namespace NeeView;

/// <summary>原 BookOperation 的 P1 编排边界；展示与文件后端从构造参数接入。</summary>
public sealed class BookOperation(IArchiveFactory archives, IImageDecoder decoder, SaveData saveData) : IAsyncDisposable
{
    private readonly SemaphoreSlim _gate = new(1);
    private CancellationTokenSource? _opening;
    private long _generation;
    private bool _disposed;
    private bool _closing;
    private readonly object _closeSync = new();
    private Task? _closeTask;
    private CancellationTokenSource? _saving;
    public Book? Book { get; private set; }
    public PageFrame? Frame { get; private set; }
    public PagePosition Position { get; private set; } = PagePosition.Zero;
    public PageFrameContext? Context { get; private set; }
    public bool IsLoading { get; private set; }
    public string? Error { get; private set; }
    public event EventHandler? Changed;

    /// <summary>提供来源关联的目录导航信息，界面不访问文件系统。</summary>
    public Task<IReadOnlyList<FolderItem>> GetFoldersAsync(CancellationToken token) => Book is null
        ? Task.FromResult<IReadOnlyList<FolderItem>>([])
        : archives.ListFoldersAsync(Book.Source.IsDirectory ? Book.Path : System.IO.Path.GetDirectoryName(Book.Path)!, token);

    /// <summary>打开图片所在目录或来源；失败保持旧书，晚到来源只释放。</summary>
    public async Task OpenAsync(string path, CancellationToken token = default)
    {
        ObjectDisposedException.ThrowIf(_disposed || _closing, this);
        var generation = Interlocked.Increment(ref _generation);
        _opening?.Cancel();
        var opening = CancellationTokenSource.CreateLinkedTokenSource(token); _opening = opening;
        IsLoading = true; Error = null; Notify();
        Archive? source = null;
        try
        {
            source = await archives.OpenAsync(path, opening.Token);
            var entries = await source.GetEntriesAsync(opening.Token);
            var restored = saveData.Find(source.Path);
            var setting = Config.Current.BookSettingPolicy.Mix(Config.Current.BookSettingDefault, Config.Current.BookSetting, restored.Memento?.ToBookSetting(), false);
            var book = new Book(source, entries.Where(e => !e.IsDirectory && ImageFormats.IsImage(e.EntryName)).Select(e => new Page(e)).ToList(), setting);
            book.SortSeed = restored.Memento?.SortSeed ?? 0; book.Sort(opening.Token);
            var requested = ImageFormats.IsImage(path) ? System.IO.Path.GetFileName(path) : setting.Page;
            int index = book.Pages.FindIndex(e => e.EntryName == requested);
            index = Math.Max(0, index);
            if (!ImageFormats.IsImage(path) && Config.Current.BookSettingPolicy.Page == BookSettingPageSelectMode.RestoreOrDefaultReset && index >= book.Pages.Count - (setting.PageMode == PageMode.WidePage && !setting.IsSupportedSingleLastPage ? 2 : 1)) index = 0;
            await ProbeAroundAsync(book, index, opening.Token);
            await _gate.WaitAsync(opening.Token);
            try
            {
                opening.Token.ThrowIfCancellationRequested();
                if (_disposed || _closing || generation != _generation) return;
                await saveData.SaveAsync(Book, Position.Part, opening.Token);
                var old = Book; Book = book; source = null;
                Config.Current.BookSetting = setting;
                Context = new(setting, Config.Current);
                Position = new(index, ImageFormats.IsImage(path) ? 0 : Math.Clamp(restored.Part, 0, 1));
                RebuildFrame(1);
                if (old is not null) await old.DisposeAsync();
                ScheduleSave();
            }
            finally { _gate.Release(); }
        }
        catch (OperationCanceledException) when (opening.IsCancellationRequested) { }
        catch (Exception ex) { if (generation == _generation) Error = ex.Message; }
        finally
        {
            if (source is not null) await source.DisposeAsync();
            if (generation == _generation) { IsLoading = false; Notify(); }
            if (ReferenceEquals(_opening, opening)) _opening = null;
            opening.Dispose();
        }
    }

    /// <summary>保留 PageFrameBox 的帧步进与单页步进算法，I/O 前后检查书籍代次。</summary>
    public async Task MoveAsync(int direction, bool onePage = false)
    {
        await _gate.WaitAsync();
        try
        {
            if (_disposed || _closing || Book is null || Frame is null || IsLoading) return;
            var generation = _generation;
            var range = Frame.FrameRange;
            // 原 MoveToNextPage 在双页时从当前方向的 Top + 1 页开始；单页仍按帧移动。
            var position = onePage && Context!.FramePageSize == 2
                ? new PagePosition(range.Top(direction).Index + direction, direction > 0 ? 0 : 1)
                : range.Next(direction);
            if (position.Index < 0 || position.Index >= Book.Pages.Count) return;
            await ProbeAroundAsync(Book, position.Index, CancellationToken.None);
            if (generation != _generation || _disposed || _closing) return;
            Position = position; RebuildFrame(direction); ScheduleSave(); Notify();
        }
        finally { _gate.Release(); }
    }

    /// <summary>按条目索引定位，最后一页反向生成，保留原首尾页行为。</summary>
    public async Task JumpAsync(int index, bool backwards = false)
    {
        await _gate.WaitAsync();
        try
        {
            if (_disposed || _closing || Book is null || Book.Pages.Count == 0 || IsLoading) return;
            var generation = _generation; index = Math.Clamp(index, 0, Book.Pages.Count - 1);
            await ProbeAroundAsync(Book, index, CancellationToken.None);
            if (_disposed || _closing || generation != _generation) return;
            Position = new(index, backwards ? 1 : 0); RebuildFrame(backwards ? -1 : 1); ScheduleSave(); Notify();
        }
        finally { _gate.Release(); }
    }

    /// <summary>设置或排序变化后保持当前条目，再生成原页框。</summary>
    public async Task ApplySettingAsync(Action<BookSettingConfig> change)
    {
        await _gate.WaitAsync();
        try
        {
            if (_disposed || _closing || Book is null || IsLoading) return;
            var current = Book.CurrentPage; change(Book.Setting); Book.Sort(CancellationToken.None);
            Position = new(current?.Index ?? 0, Position.Part); RebuildFrame(1); ScheduleSave(); Notify();
        }
        finally { _gate.Release(); }
    }

    /// <summary>视口改变只重算尺寸，不扫描来源或改变阅读对象。</summary>
    public void SetViewport(Size size, double scale)
    {
        if (_disposed || Context is null || size.Width <= 0 || size.Height <= 0) return;
        Context.CanvasSize = size; Context.DeviceScale = Math.Max(1, scale); RebuildFrame(1);
    }
    /// <summary>立即保存，用于切书和正常退出。</summary>
    public Task SaveAsync() => saveData.SaveAsync(Book, Position.Part);

    /// <summary>使用完整迁入的 PageFrameFactory；半页位置在关闭分割时恢复整页。</summary>
    private void RebuildFrame(int direction)
    {
        if (Book is null || Context is null || Book.Pages.Count == 0) { Frame = null; return; }
        var page = Book.Pages[Math.Clamp(Position.Index, 0, Book.Pages.Count - 1)];
        if (!(Context.PageMode == PageMode.SinglePage && Context.IsSupportedDividePage && Maths.AspectRatioTools.IsLandscape(page.Content.PageDataSource.Size))) Position = new(page.Index, direction > 0 ? 0 : 1);
        Frame = new PageFrameFactory(Context, new BookContext(Book.Pages), new ContentSizeCalculator(Context)).CreatePageFrame(Position, direction);
        Book.CurrentPage = Frame?.Elements.FirstOrDefault(e => !e.IsDummy)?.Page;
    }
    /// <summary>只探测当前及生成双页所需邻页，损坏页保留占位。</summary>
    private async Task ProbeAroundAsync(Book book, int index, CancellationToken token)
    {
        foreach (var page in book.Pages.Skip(Math.Max(0, index - 1)).Take(4))
        {
            if (page.Content.HasSize) continue;
            try
            {
                await using var stream = await book.Source.OpenEntryAsync(page.ArchiveEntry, token);
                var info = await decoder.ProbeAsync(stream, token);
                page.Content.PageDataSource = new(info.Size);
            }
            catch (Exception ex) when (ex is not OperationCanceledException) { page.Content.Error = ex.Message; }
            page.Content.HasSize = true;
        }
    }
    /// <summary>一秒防抖，切书及退出仍由立即保存入口保证。</summary>
    private void ScheduleSave()
    {
        _saving?.Cancel(); var pending = new CancellationTokenSource(); _saving = pending;
        _ = SaveLaterAsync(pending);
    }
    /// <summary>观察后台保存错误，不产生未观察任务异常。</summary>
    private async Task SaveLaterAsync(CancellationTokenSource pending)
    {
        try { await Task.Delay(1000, pending.Token); await saveData.SaveAsync(Book, Position.Part, pending.Token); }
        catch (OperationCanceledException) { }
        catch (Exception ex) { Error = "保存失败：" + ex.Message; Notify(); }
        finally { if (ReferenceEquals(_saving, pending)) _saving = null; pending.Dispose(); }
    }
    /// <summary>发布业务变化；界面订阅者负责切换 UI 线程。</summary>
    private void Notify() => Changed?.Invoke(this, EventArgs.Empty);
    /// <summary>取消打开和防抖，完成状态保存后释放当前来源。</summary>
    public ValueTask DisposeAsync()
    {
        lock (_closeSync)
        {
            if (_disposed) return ValueTask.CompletedTask;
            return new ValueTask(_closeTask ??= CloseCoreAsync());
        }
    }
    /// <summary>序列化保存和释放；保存失败恢复操作能力，下一次退出仍会重试。</summary>
    private async Task CloseCoreAsync()
    {
        await Task.Yield();
        _closing = true; Interlocked.Increment(ref _generation);
        _opening?.Cancel(); _saving?.Cancel();
        await _gate.WaitAsync();
        try
        {
            await SaveAsync();
            if (Book is not null) await Book.DisposeAsync();
            Book = null; Frame = null; _disposed = true;
        }
        finally
        {
            _gate.Release(); _closing = false;
            lock (_closeSync) _closeTask = null;
        }
    }
}

/// <summary>P1 可打开格式；高级格式由迁移清单保留。</summary>
public static class ImageFormats
{
    private static readonly HashSet<string> Extensions = new(StringComparer.OrdinalIgnoreCase) { ".jpg", ".jpeg", ".png", ".webp", ".bmp", ".tif", ".tiff", ".gif" };
    /// <summary>按扩展名识别候选图片，损坏文件仍保留索引。</summary>
    public static bool IsImage(string path) => Extensions.Contains(System.IO.Path.GetExtension(path));
}
