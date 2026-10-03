// Copyright (c) NeeLaboratory. 适配原 PageFrameBox.ScrollToNext 的判断顺序。
namespace NeeView.PageFrames;

/// <summary>原换行限流与边界控制；时钟可注入，展示端只应用返回向量。</summary>
public sealed class PageFrameScrollControl(Func<long>? milliseconds = null)
{
    private readonly Func<long> _milliseconds = milliseconds ?? (() => Environment.TickCount64);
    private long _lastScroll = (milliseconds ?? (() => Environment.TickCount64))();
    /// <summary>计算一次滚动；返回 null 表示原换行停顿阻止本次动作。</summary>
    public ScrollResult? ScrollToNext(PageFrameContext context, Rect content, Rect viewport, int direction, ScrollPageCommandParameter parameter)
        => ScrollToNext(context, content, viewport, direction, parameter, parameter.LineBreakStopMode, parameter.EndMargin);
    /// <summary>纯NType使用同一原限流链，终端返回零向量而不导航。</summary>
    public ScrollResult? ScrollToNext(PageFrameContext context, Rect content, Rect viewport, int direction, IScrollNTypeParameter parameter,
        LineBreakStopMode stopMode = LineBreakStopMode.Line, double endMargin = 0)
    {
        var now = _milliseconds();
        var limited = now - _lastScroll < (int)(parameter.LineBreakStopTime * 1000);
        _lastScroll = now;
        var result = new NScroll(context, content, viewport).ScrollN(direction, parameter, endMargin);
        // 保留原先重置计时、再检查换行/终止的顺序，连续滚轮不会跳过停顿。
        return result.IsLineBreak && limited && (result.IsTerminated || stopMode == LineBreakStopMode.Line) ? null : result;
    }
}
