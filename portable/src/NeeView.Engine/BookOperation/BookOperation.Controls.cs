// Copyright (c) NeeLaboratory. MIT；原BookHub锁定/Unload及BookPageTerminator流程的异步平台适配。
namespace NeeView;
public sealed partial class BookOperation
{
    private int _pageTerminating;
    public bool IsBookLocked { get; private set; }
    public bool CanUnload => !_disposed && !_closing && (Book is not null || IsLoading);
    /// <summary>宿主仅提供原三种选择，业务在返回后再次核对书籍/位置/代次。</summary>
    public Func<int, CancellationToken, Task<PageEndAction>>? PageEndDialogAsync { get; set; }
    public void SetBookLock(bool value) { if (_disposed || _closing) return; IsBookLocked = value; Notify(); }
    /// <summary>关闭当前来源并解锁，服务继续可用；保存失败保留书籍供重试。</summary>
    public async Task UnloadAsync(CancellationToken token = default)
    {
        if (_disposed || _closing) return;
        IsBookLocked = false; var generation = Interlocked.Increment(ref _generation); _opening?.Cancel(); _saving?.Cancel();
        await _gate.WaitAsync(token);
        try
        {
            token.ThrowIfCancellationRequested();
            if (_disposed || _closing || generation != _generation) return;
            await saveData.SaveAsync(Book, Position.Part, token, _keepHistoryOrder, clearLastBook: true);
            var old = Book; Book = null; Frame = null; Context = null; Position = PagePosition.Zero; Error = null;
            PageSelector.Synchronize(null, 0); RefreshMarkers(); RecordPageHistory();
            if (old is not null) await old.DisposeAsync();
        }
        finally { _gate.Release(); if (generation == _generation) { IsLoading = false; Notify(); } }
    }
    /// <summary>页框方向是帧之间的滚动轴，双页仍按原书籍方向横排；不替换阅读方向。</summary>
    public Task SetOrientationAsync(PageFrameOrientation orientation) => ApplyGlobalReadingAsync(() => Config.Current.Book.Orientation = orientation);
    public Task SetPageEndActionAsync(PageEndAction action) => ApplyGlobalReadingAsync(() => Config.Current.Book.PageEndAction = action);
    private async Task ApplyGlobalReadingAsync(Action change)
    {
        await _gate.WaitAsync();
        try { if (_disposed || _closing || IsLoading) return; change(); RebuildFrame(MoveDirection); ScheduleSave(); Notify(); }
        finally { _gate.Release(); }
    }
    /// <summary>原None提示、Loop首尾、NextBook重置策略及Dialog；重复终止不重入。</summary>
    private async Task HandlePageEndAsync(Book book, long generation, PagePosition position, int direction)
    {
        if (Interlocked.CompareExchange(ref _pageTerminating, 1, 0) != 0) return;
        try
        {
            if (!IsCurrent()) return;
            var action = Config.Current.Book.PageEndAction;
            var notify = action != PageEndAction.Dialog;
            if (action == PageEndAction.Dialog)
                action = PageEndDialogAsync is { } dialog ? await dialog(direction, CancellationToken.None) : PageEndAction.None;
            if (!IsCurrent()) return;
            switch (action)
            {
                case PageEndAction.Loop:
                    await JumpCoreAsync(direction < 0 ? book.Pages.Count - 1 : 0, direction < 0, true, book, expectedPosition: position);
                    if (Config.Current.Book.IsNotifyPageLoop && ReferenceEquals(book, Book)) { Error = "页面已循环。"; Notify(); }
                    break;
                case PageEndAction.NextBook:
                    await MoveBookAsync(direction, true, book, generation, position); break;
                case PageEndAction.SeamlessLoop: break; // 循环由原PageFrameFactory完成。
                default: if (notify) { Error = direction < 0 ? "已到首页。" : "已到末页。"; Notify(); } break;
            }
        }
        finally { Interlocked.Exchange(ref _pageTerminating, 0); }
        bool IsCurrent() => !_disposed && !_closing && !IsLoading && _generation == generation && ReferenceEquals(book, Book) && Position == position;
    }
}
