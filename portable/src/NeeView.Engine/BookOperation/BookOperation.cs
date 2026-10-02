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
    private readonly SemaphoreSlim _historyGate = new(1);
    private bool _keepHistoryOrder;
    private FilmStrip? _filmStrip;
    private BookshelfFolderList? _bookshelf;
    public BookshelfFolderList Bookshelf => _bookshelf ??= new(archives);
    public PageSelector PageSelector { get; } = new();
    public FilmStrip FilmStrip => _filmStrip ??= new(PageSelector);
    public PageHistory PageHistory { get; } = new();
    public BookHubHistory BookHistory { get; } = new();
    public Book? Book { get; private set; }
    public PageFrame? Frame { get; private set; }
    public PagePosition Position { get; private set; } = PagePosition.Zero;
    public PageFrameContext? Context { get; private set; }
    /// <summary>保留最近生成帧的移动方向，供反向半页重算和显示原点使用；与书籍阅读方向分离。</summary>
    public int MoveDirection { get; private set; } = 1;
    public bool IsLoading { get; private set; }
    public string? Error { get; private set; }
    public event EventHandler? Changed;

    /// <summary>打开图片所在目录或来源；失败保持旧书，晚到来源只释放。</summary>
    public async Task OpenAsync(string path, CancellationToken token = default)
    { await OpenCoreAsync(path, token); }

    /// <summary>共享原加载链；历史重放可指定条目并保留访问顺序，返回是否实际提交新书。</summary>
    private async Task<bool> OpenCoreAsync(string path, CancellationToken token, string? entryName = null, bool keepHistoryOrder = false, bool replayPageHistory = false, bool replayBookHistory = false)
    {
        ObjectDisposedException.ThrowIf(_disposed || _closing, this);
        var generation = Interlocked.Increment(ref _generation);
        _opening?.Cancel();
        var opening = CancellationTokenSource.CreateLinkedTokenSource(token); _opening = opening;
        IsLoading = true; Error = null; Notify();
        Archive? source = null;
        bool committed = false;
        try
        {
            source = await archives.OpenAsync(path, opening.Token);
            var entries = await source.GetEntriesAsync(opening.Token);
            var restored = saveData.Find(source.Path);
            var setting = Config.Current.BookSettingPolicy.Mix(Config.Current.BookSettingDefault, Config.Current.BookSetting, restored.Memento?.ToBookSetting(), false);
            var book = new Book(source, entries.Where(e => !e.IsDirectory && ImageFormats.IsImage(e.EntryName)).Select(e => new Page(e)).ToList(), setting);
            book.SortSeed = restored.Memento?.SortSeed ?? 0; book.Sort(opening.Token);
            var requested = entryName ?? (ImageFormats.IsImage(path) ? System.IO.Path.GetFileName(path) : setting.Page);
            int index = book.Pages.FindIndex(e => e.EntryName == requested);
            if (entryName is not null && index < 0) throw new FileNotFoundException("历史页面已不存在：" + entryName);
            index = Math.Max(0, index);
            if (entryName is null && !keepHistoryOrder && !ImageFormats.IsImage(path) && Config.Current.BookSettingPolicy.Page == BookSettingPageSelectMode.RestoreOrDefaultReset && index >= book.Pages.Count - (setting.PageMode == PageMode.WidePage && !setting.IsSupportedSingleLastPage ? 2 : 1)) index = 0;
            await ProbeAroundAsync(book, index, opening.Token);
            await _gate.WaitAsync(opening.Token);
            try
            {
                opening.Token.ThrowIfCancellationRequested();
                if (_disposed || _closing || generation != _generation) return false;
                await saveData.SaveAsync(Book, Position.Part, opening.Token, _keepHistoryOrder);
                var old = Book; Book = book; source = null;
                _keepHistoryOrder = keepHistoryOrder;
                Config.Current.BookSetting = setting;
                Context = new(setting, Config.Current);
                Position = new(index, entryName is not null || ImageFormats.IsImage(path) ? 0 : Math.Clamp(restored.Part, 0, 1));
                RebuildFrame(1);
                if (old is not null) await old.DisposeAsync();
                if (!replayBookHistory) BookHistory.Add(book.Path);
                if (!replayPageHistory) RecordPageHistory();
                committed = true;
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
        return committed;
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
            // 新导航已经提交，旧打开/书架边界提示不能继续遮蔽当前页面状态。
            Error = null; Position = position; RebuildFrame(direction); RecordPageHistory(); ScheduleSave(); Notify();
        }
        finally { _gate.Release(); }
    }

    /// <summary>原 NextBook/PrevBook 按书架所选条目的前后项加载，失败不移动选择；普通模式在加载时不抢占。</summary>
    /// <param name="direction">前一本 -1、后一本 +1。</param>
    public async Task MoveBookAsync(int direction)
    {
        if (_disposed || _closing || (IsLoading && !Config.Current.Book.IsPrioritizeBookMove)) return;
        await _historyGate.WaitAsync();
        try
        {
            if (_disposed || _closing || Book is null || (IsLoading && !Config.Current.Book.IsPrioritizeBookMove)) return;
            var book = Book; var generation = _generation;
            // 不用当前页面推算兄弟书；有书架位置时遵循独立选择，无位置才按原 Sync 取得父目录。
            if (Bookshelf.Place is null && !await Bookshelf.SyncAsync(book)) { Error = Bookshelf.Error; Notify(); return; }
            if (_disposed || _closing || generation != _generation || !ReferenceEquals(book, Book)) return;
            if (Bookshelf.IsLoading) return;
            var item = Bookshelf.GetFolderItem(direction);
            if (item is null) { Error = direction < 0 ? "已到书架首项，无法打开上一本书籍。" : "已到书架末项，无法打开下一本书籍。"; Notify(); return; }
            if (await OpenCoreAsync(item.Path, CancellationToken.None)) Bookshelf.Select(item);
        }
        finally { _historyGate.Release(); }
    }

    /// <summary>沿用原 GetNextFolderIndex/GetPrevFolderIndex；仅文件名排序按条目目录分组跳页，不打开子书。</summary>
    /// <param name="direction">前一组 -1、后一组 +1。</param>
    public async Task MoveFolderPageAsync(int direction)
    {
        await _gate.WaitAsync();
        try
        {
            if (_disposed || _closing || IsLoading || Book is null || Frame is null) return;
            var generation = _generation;
            int start = new BookContext(Book.Pages).NormalizeIndex(Frame.FrameRange.Min.Index);
            int index = direction < 0 ? Book.Pages.GetPrevFolderIndex(start) : Book.Pages.GetNextFolderIndex(start);
            if (index < 0) return;
            await ProbeAroundAsync(Book, index, CancellationToken.None);
            if (_disposed || _closing || generation != _generation) return;
            // 原文件夹跳页无论前后均从目标目录首图正向生成，不复用上一帧的后半页。
            Error = null; Position = new(index, 0); RebuildFrame(1); RecordPageHistory(); ScheduleSave(); Notify();
        }
        finally { _gate.Release(); }
    }

    /// <summary>按条目索引定位，最后一页反向生成，保留原首尾页行为。</summary>
    public async Task JumpAsync(int index, bool backwards = false)
    { await JumpCoreAsync(index, backwards, true); }

    /// <summary>共用定位；历史重放不再追加自身，否则会截断原前进分支。</summary>
    private async Task<bool> JumpCoreAsync(int index, bool backwards, bool recordHistory)
    {
        await _gate.WaitAsync();
        try
        {
            if (_disposed || _closing || Book is null || Book.Pages.Count == 0 || IsLoading) return false;
            var generation = _generation; index = Math.Clamp(index, 0, Book.Pages.Count - 1);
            await ProbeAroundAsync(Book, index, CancellationToken.None);
            if (_disposed || _closing || generation != _generation) return false;
            // 明确定位成功后恢复页面状态；失败和过期请求仍保留各自的错误处理。
            Error = null; Position = new(index, backwards ? 1 : 0); RebuildFrame(backwards ? -1 : 1);
            if (recordHistory) RecordPageHistory(); ScheduleSave(); Notify(); return true;
        }
        finally { _gate.Release(); }
    }

    /// <summary>原 MoveToNextStep 从范围最小索引计算并静态对齐；越过尾端反向生成。</summary>
    /// <param name="delta">带方向的页数，不重复普通帧翻页。</param>
    public async Task MoveSizeAsync(int delta)
    {
        if (delta == 0) return;
        await _gate.WaitAsync();
        try
        {
            if (_disposed || _closing || IsLoading || Book is null || Frame is null) return;
            var generation = _generation;
            var accessor = new BookContext(Book.Pages);
            var requested = Frame.FrameRange.Min.Index + delta;
            // 原算法先归一到周期内再静态对齐，之后还原周期；奇数页书籍不可直接对原索引对齐。
            var index = Book.Pages.Count * accessor.NormalizeCycle(requested) + BookTools.WidwPageAlignment(accessor.NormalizeIndex(requested), Book.Setting);
            bool backwards = index >= Book.Pages.Count;
            if (!accessor.ContainsIndex(index))
            {
                int corrected = Math.Clamp(index, 0, Book.Pages.Count - 1);
                // 原 CorrectPosition：未到终点时允许定位首尾；已显示该端点时终止，不重建分割页。
                if ((backwards ? Frame.FrameRange.Max.Index : Frame.FrameRange.Min.Index) == corrected) return;
                index = corrected;
            }
            await ProbeAroundAsync(Book, index, CancellationToken.None);
            if (_disposed || _closing || generation != _generation) return;
            Error = null; Position = new(index, backwards ? 1 : 0); RebuildFrame(backwards ? -1 : 1);
            RecordPageHistory(); ScheduleSave(); Notify();
        }
        finally { _gate.Release(); }
    }

    /// <summary>重放原页面或书籍打开历史；加载失败/被新打开取代时保留游标供重试。</summary>
    /// <param name="direction">后退为 -1，前进为 1。</param>
    /// <param name="bookOnly">true 按打开顺序，false 按书籍路径和页面条目名。</param>
    public async Task NavigateHistoryAsync(int direction, bool bookOnly = false)
    {
        await _historyGate.WaitAsync();
        try
        {
            if (_disposed || _closing || IsLoading) return;
            if (bookOnly)
            {
                var path = BookHistory.GetTarget(direction); if (path is null) return;
                if (await OpenCoreAsync(path, CancellationToken.None, keepHistoryOrder: true, replayBookHistory: true) && BookHistory.GetTarget(direction) == path)
                    BookHistory.CommitMove(direction);
            }
            else
            {
                var target = PageHistory.GetTarget(direction); if (target is not { } unit || unit.IsEmpty()) return;
                bool success;
                if (Book?.Path == unit.BookAddress)
                {
                    int index = Book.Pages.FindIndex(p => p.EntryName == unit.PageName);
                    if (index < 0) { Error = "历史页面已不存在：" + unit.PageName; Notify(); return; }
                    success = await JumpCoreAsync(index, false, false);
                }
                else success = await OpenCoreAsync(unit.BookAddress, CancellationToken.None, unit.PageName, true, replayPageHistory: true);
                if (success && PageHistory.GetTarget(direction) == unit) PageHistory.CommitMove(direction);
            }
        }
        finally { _historyGate.Release(); }
    }

    /// <summary>悬停详情只探测该条目附近的元数据，来源/代次由既有操作锁保护。</summary>
    public async Task<Page?> GetPageInformationAsync(int index, CancellationToken token)
    {
        await _gate.WaitAsync(token);
        try
        {
            if (_disposed || _closing || IsLoading || Book is null || index < 0 || index >= Book.Pages.Count) return null;
            var book = Book; var generation = _generation; await ProbeAroundAsync(book, index, token);
            return generation == _generation && ReferenceEquals(book, Book) ? book.Pages[index] : null;
        }
        finally { _gate.Release(); }
    }

    /// <summary>原页面历史只登记当前真实显示帧中索引最小的页，空内容使用空记录。</summary>
    private void RecordPageHistory()
    {
        var page = Frame?.Elements.Where(e => !e.IsDummy).Select(e => e.Page).MinBy(p => p.Index);
        PageHistory.Add(page is null || Book is null ? PageHistoryUnit.Empty : new(Book.Path, page.EntryName));
    }

    /// <summary>设置或排序变化后保持当前条目，再生成原页框。</summary>
    public async Task ApplySettingAsync(Action<BookSettingConfig> change)
    {
        await _gate.WaitAsync();
        try
        {
            if (_disposed || _closing || Book is null || IsLoading) return;
            var current = Book.CurrentPage; change(Book.Setting); Book.Sort(CancellationToken.None);
            Position = new(current?.Index ?? 0, Position.Part); RebuildFrame(1); RecordPageHistory(); ScheduleSave(); Notify();
        }
        finally { _gate.Release(); }
    }

    /// <summary>视口改变只重算尺寸，不扫描来源或改变阅读对象。</summary>
    public void SetViewport(Size size, double scale)
    {
        if (_disposed || Context is null || size.Width <= 0 || size.Height <= 0) return;
        // 视口变化必须保留当前生成方向，否则反向分割页会被改成前半页。
        Context.CanvasSize = size; Context.DeviceScale = Math.Max(1, scale); RebuildFrame(MoveDirection, false);
    }
    /// <summary>立即保存，用于切书和正常退出。</summary>
    public Task SaveAsync() => saveData.SaveAsync(Book, Position.Part, keepHistoryOrder: _keepHistoryOrder);

    /// <summary>使用完整迁入的 PageFrameFactory；半页位置在关闭分割时恢复整页。</summary>
    private void RebuildFrame(int direction, bool synchronizeSelection = true)
    {
        // PageFrame.Direction 是书籍阅读方向；移动方向独立保留，供分割生成与展示原点使用。
        MoveDirection = direction;
        if (Book is null || Context is null || Book.Pages.Count == 0) { Frame = null; if (synchronizeSelection) PageSelector.Synchronize(Book, 0); return; }
        var page = Book.Pages[Math.Clamp(Position.Index, 0, Book.Pages.Count - 1)];
        if (!(Context.PageMode == PageMode.SinglePage && Context.IsSupportedDividePage && Maths.AspectRatioTools.IsLandscape(page.Content.PageDataSource.Size))) Position = new(page.Index, direction > 0 ? 0 : 1);
        Frame = new PageFrameFactory(Context, new BookContext(Book.Pages), new ContentSizeCalculator(Context)).CreatePageFrame(Position, direction);
        Book.CurrentPage = Frame?.Elements.FirstOrDefault(e => !e.IsDummy)?.Page;
        if (synchronizeSelection) PageSelector.Synchronize(Book, Math.Max(0, Frame?.FrameRange.Min.Index ?? 0));
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
        try { await Task.Delay(1000, pending.Token); await saveData.SaveAsync(Book, Position.Part, pending.Token, _keepHistoryOrder); }
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
            _bookshelf?.Dispose();
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

/// <summary>当前已接入的普通归档类型，书架候选与来源工厂使用同一能力清单。</summary>
public static class ArchiveFormats
{
    private static readonly HashSet<string> Extensions = new(StringComparer.OrdinalIgnoreCase) { ".zip", ".cbz", ".rar", ".cbr", ".7z" };
    /// <summary>按扩展名识别归档候选，损坏或加密归档仍由加载入口明确报错。</summary>
    public static bool IsArchive(string path) => Extensions.Contains(System.IO.Path.GetExtension(path));
}
