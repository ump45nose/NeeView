using Avalonia;
namespace NeeView.MacOS.Views;
public sealed partial class ReaderView
{
    /// <summary>原PageFrameBox.AutoScroll：向阅读方向终端及底部线性滚动整个周期，100ms以下不启动。</summary>
    public void BeginSlideShowScroll(TimeSpan duration)
    {
        if (_disposed || IsBrowsing || _frame is null || duration <= TimeSpan.FromMilliseconds(100)) return;
        var from = _pan + _motion.GetPanOffset();
        var horizontal = _operation?.Book?.Setting.BookReadOrder == PageReadOrder.LeftToRight
            ? LimitedHorizontalAlignment.Right : LimitedHorizontalAlignment.Left;
        var delta = new DragArea(new(0, 0, Bounds.Width, Bounds.Height), GetContentRect())
            .SnapAlignment(horizontal, LimitedVerticalAlignment.Bottom, false);
        _pan += new Avalonia.Vector(delta.X, delta.Y); AnimatePan(from, duration, true);
    }
    /// <summary>鼠标及停止保留当前显示位置，只取消内容平移，不丢分页退出帧。</summary>
    public void CancelSlideShowScroll()
    {
        if (_disposed) return;
        _pan += _motion.CancelPan();
        if (!_motion.IsPageActive && !_awaitingTransition) _motionTimer.Stop();
        InvalidateVisual();
    }
}
