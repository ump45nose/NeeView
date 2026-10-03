// Copyright (c) NeeLaboratory. 原 DragArea 越界算法迁入，移除 WPF 控件辅助方法。
namespace NeeView;

/// <summary>原内容与视口的四边越界值，供 NScroll 计算移动距离。</summary>
public sealed class DragArea
{
    public Rect ViewRect { get; }
    public Rect ContentRect { get; }
    public Rect Over { get; }
    /// <summary>接收实际显示矩形；左/上越界为负，右/下越界为正。</summary>
    public DragArea(Rect viewRect, Rect contentRect)
    {
        ViewRect = viewRect; ContentRect = contentRect;
        var left = contentRect.Left < viewRect.Left ? contentRect.Left - viewRect.Left : 0;
        var right = contentRect.Right > viewRect.Right ? contentRect.Right - viewRect.Right : 0;
        var top = contentRect.Top < viewRect.Top ? contentRect.Top - viewRect.Top : 0;
        var bottom = contentRect.Bottom > viewRect.Bottom ? contentRect.Bottom - viewRect.Bottom : 0;
        Over = new(left, top, right - left, bottom - top);
    }
    /// <summary>原 SnapView：小图居中或约束在视口内，大图不允许整张移出边界。</summary>
    /// <param name="centered">小于视口时是否强制居中。</param>
    /// <returns>内容中心需要增加的位移，保持原内容移动方向。</returns>
    public Vector SnapView(bool centered)
    {
        const double margin = 1;
        var x = ContentRect.X + ContentRect.Width * .5; var y = ContentRect.Y + ContentRect.Height * .5;
        var originalX = x; var originalY = y;
        if (ContentRect.Width <= ViewRect.Width + margin)
        {
            if (centered) x = ViewRect.X + ViewRect.Width * .5;
            else if (ContentRect.Left < ViewRect.Left) x = ViewRect.Left + ContentRect.Width * .5;
            else if (ContentRect.Right > ViewRect.Right) x = ViewRect.Right - ContentRect.Width * .5;
        }
        else
        {
            if (ContentRect.Left > ViewRect.Left + margin) x = ViewRect.Left + ContentRect.Width * .5;
            else if (ContentRect.Right < ViewRect.Right - margin) x = ViewRect.Right - ContentRect.Width * .5;
        }
        if (ContentRect.Height <= ViewRect.Height + margin)
        {
            if (centered) y = ViewRect.Y + ViewRect.Height * .5;
            else if (ContentRect.Top < ViewRect.Top) y = ViewRect.Top + ContentRect.Height * .5;
            else if (ContentRect.Bottom > ViewRect.Bottom) y = ViewRect.Bottom - ContentRect.Height * .5;
        }
        else
        {
            if (ContentRect.Top > ViewRect.Top + margin) y = ViewRect.Top + ContentRect.Height * .5;
            else if (ContentRect.Bottom < ViewRect.Bottom - margin) y = ViewRect.Bottom - ContentRect.Height * .5;
        }
        return new(x - originalX, y - originalY);
    }
    /// <summary>原预置滚动：小图在非snap时仅校正越界，大图或snap时强制对齐指定边。</summary>
    public Vector SnapAlignment(LimitedHorizontalAlignment horizontal, LimitedVerticalAlignment vertical, bool snap)
    {
        var correction = SnapView(false);
        var x = snap || ContentRect.Width > ViewRect.Width ? horizontal switch
        { LimitedHorizontalAlignment.Left => ViewRect.Left + ContentRect.Width * .5, LimitedHorizontalAlignment.Right => ViewRect.Right - ContentRect.Width * .5, _ => ViewRect.Left + ViewRect.Width * .5 } : ContentRect.X + ContentRect.Width * .5 + correction.X;
        var y = snap || ContentRect.Height > ViewRect.Height ? vertical switch
        { LimitedVerticalAlignment.Top => ViewRect.Top + ContentRect.Height * .5, LimitedVerticalAlignment.Bottom => ViewRect.Bottom - ContentRect.Height * .5, _ => ViewRect.Top + ViewRect.Height * .5 } : ContentRect.Y + ContentRect.Height * .5 + correction.Y;
        return new(x - ContentRect.X - ContentRect.Width * .5, y - ContentRect.Y - ContentRect.Height * .5);
    }
}
