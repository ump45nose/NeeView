// Copyright (c) NeeLaboratory. 原 PageFrameContainerLayout.Layout 方向与定位分支迁入。
namespace NeeView.PageFrames;
/// <summary>分页相邻容器定位的数值部分；表现层负责时间插值，不包含控件。</summary>
public static class PageFrameContainerLayout
{
    /// <summary>按原上/下或阅读顺序放置前后帧；Fade与参考帧同起点。</summary>
    /// <param name="reference">参考帧的容器矩形。</param><param name="size">目标帧尺寸。</param>
    /// <param name="context">原帧方向、阅读顺序与间距。</param><param name="direction">前进1，后退-1。</param>
    public static Rect Layout(Rect reference, Size size, PageFrameContext context, int direction)
    {
        if (!context.IsPanorama && context.PageChangeType == PageMoveType.Fade) return new(reference.X, reference.Y, size.Width, size.Height);
        if (context.FrameOrientation == PageFrameOrientation.Horizontal)
        {
            bool previous = (context.ReadOrder == PageReadOrder.LeftToRight ? direction : -direction) < 0;
            return new(previous ? reference.X - size.Width - context.FrameMargin : reference.Right + context.FrameMargin,
                -size.Height * .5, size.Width, size.Height);
        }
        return new(-size.Width * .5, direction < 0 ? reference.Y - size.Height - context.FrameMargin : reference.Bottom + context.FrameMargin, size.Width, size.Height);
    }
}
