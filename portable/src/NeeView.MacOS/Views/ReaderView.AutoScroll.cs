using Avalonia;
using Avalonia.Input;
using Avalonia.Threading;

namespace NeeView.MacOS.Views;

public sealed partial class ReaderView
{
    public event EventHandler? AutoScrollChanged;
    private ReaderAutoScrollPresenter? _autoScroll;
    private Book? _autoScrollBook;
    private Cursor? _autoScrollCursor, _previousAutoScrollCursor;
    /// <summary>当前是否处于指针自动滚动模式。</summary>
    public bool IsAutoScrollMode => _autoScroll?.IsActive == true;
    /// <summary>启停自动滚动，并以当前指针位置作为速度中心。</summary>
    public void SetAutoScrollMode(bool enabled)
    {
        _autoScroll ??= new ReaderAutoScrollPresenter(delta => Pan(delta));
        if (enabled) BeginAutoScroll(_pointer ?? new Point(Bounds.Width / 2, Bounds.Height / 2), false); else StopAutoScroll();
    }
    /// <summary>宿主用于长按中键进入自动滚动；释放最后一个按钮时结束。</summary>
    public void BeginAutoScroll(Point origin, bool longDownMode)
    {
        CancelLongPress(); CancelMouseSequence(); _pendingClick = null; _pressed = null;
        (_autoScroll ??= new ReaderAutoScrollPresenter(delta => Pan(delta))).Start(origin, longDownMode);
        _autoScrollBook = _operation?.Book;
        _autoScrollCursor ??= new Cursor(StandardCursorType.SizeAll);
        if (!ReferenceEquals(Cursor, _autoScrollCursor)) _previousAutoScrollCursor = Cursor;
        Cursor = _autoScrollCursor; AutoScrollChanged?.Invoke(this, EventArgs.Empty);
    }
    public void AutoScrollButtonPressed() { if (Config.Current.Mouse.IsStopAutoScrollUponInteraction && _autoScroll?.IsLongDownMode != true) StopAutoScroll(); }
    public void AutoScrollButtonReleased(bool anyButtonHeld) { if (_autoScroll?.IsLongDownMode == true && !anyButtonHeld) StopAutoScroll(); }
    public bool HandleAutoScrollEscape() { if (!IsAutoScrollMode) return false; StopAutoScroll(); return true; }
    /// <summary>停止自动滚动并释放其时钟。</summary>
    public void StopAutoScroll()
    {
        var active = IsAutoScrollMode; _autoScroll?.Stop(); _autoScrollBook = null;
        if (ReferenceEquals(Cursor, _autoScrollCursor)) Cursor = _previousAutoScrollCursor;
        _previousAutoScrollCursor = null;
        if (active) AutoScrollChanged?.Invoke(this, EventArgs.Empty);
    }
    /// <summary>切书拒绝旧指针速度；原页范围更新仅按停止交互配置退出。</summary>
    private void SynchronizeAutoScroll()
    { if (IsAutoScrollMode && !ReferenceEquals(_autoScrollBook, _operation?.Book)) StopAutoScroll(); }
    private void DisposeAutoScroll() { DisposeLongPress(); StopAutoScroll(); _autoScroll?.Dispose(); _autoScroll = null; _autoScrollCursor?.Dispose(); _autoScrollCursor = null; }
}
