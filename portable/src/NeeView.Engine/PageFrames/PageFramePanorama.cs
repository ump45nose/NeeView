// Copyright (c) NeeLaboratory. MIT；原PageFrameContainerLayout与PageFrameBox.CreatePanoramaContentRect的数值适配。
namespace NeeView.PageFrames;

/// <summary>可见邻近帧的容器位置；PageFrameFactory仍是唯一双页/宽页/分割生成器。</summary>
public sealed record PanoramaFrame(PageFrame Frame, Rect Bounds);
/// <summary>原全景容器的有界数值窗口；不拥有Page、像素或控件。</summary>
public sealed class PageFramePanorama
{
    public IReadOnlyList<PanoramaFrame> Frames { get; }
    public Rect ContentRect { get; }
    /// <summary>沿当前原帧向前后扩展一视口，保留水平阅读方向、垂直顺序与FrameSpace。</summary>
    /// <param name="pages">当前原排序后的Page集合。</param><param name="selected">当前原帧，含Part/dummy。</param>
    /// <param name="context">原阅读与拉伸上下文。</param><param name="viewport">相对当前帧中心的可见矩形。</param>
    /// <param name="scale">表现端手工/基准倍率。</param><param name="angle">表现端手工角度。</param>
    /// <param name="indexing">真实页尾尚未知时保留尾部滚动余量。</param>
    public PageFramePanorama(IReadOnlyList<Page> pages, PageFrame selected, PageFrameContext context, Rect viewport,
        double scale = 1, double angle = 0, bool indexing = false)
    {
        var factory = new PageFrameFactory(context, new BookContext(pages), new ContentSizeCalculator(context));
        var size = SizeOf(selected); var origin = new Rect(-size.Width / 2, -size.Height / 2, size.Width, size.Height);
        var frames = new List<PanoramaFrame> { new(selected, origin) };
        Extend(-1); Extend(1); Frames = frames;
        var left = frames.Min(f => f.Bounds.Left); var top = frames.Min(f => f.Bounds.Top);
        var right = frames.Max(f => f.Bounds.Right); var bottom = frames.Max(f => f.Bounds.Bottom);
        // 原CreatePanoramaContentRect在未到首尾时各增加一个视口，不把窗口边界当作书籍边界。
        bool first = !context.IsLoopPage && frames.Any(f => f.Frame.FrameRange.Min.Index <= 0 && f.Frame.FrameRange.Min.Part == 0);
        bool last = !indexing && !context.IsLoopPage && frames.Any(f => f.Frame.FrameRange.Max.Index >= pages.Count - 1 && f.Frame.FrameRange.Max.Part == 1);
        if (context.FrameOrientation == PageFrameOrientation.Horizontal)
        {
            if (!first) { if (context.ReadOrder == PageReadOrder.LeftToRight) left -= viewport.Width; else right += viewport.Width; }
            if (!last) { if (context.ReadOrder == PageReadOrder.LeftToRight) right += viewport.Width; else left -= viewport.Width; }
        }
        else { if (!first) top -= viewport.Height; if (!last) bottom += viewport.Height; }
        ContentRect = new(left, top, right - left, bottom - top);

        Size SizeOf(PageFrame frame)
        {
            var rotated = GeometryMath.RotateSize(frame.Size, angle);
            return new(Math.Max(1, rotated.Width * scale), Math.Max(1, rotated.Height * scale));
        }
        void Extend(int direction)
        {
            var current = selected; var bounds = origin;
            // 最多65个轻量帧，不按书籍页数生成显示对象；离开窗口即由调用方替换。
            for (int count = 0; count < 32; count++)
            {
                var position = current.FrameRange.Next(direction);
                if (!context.IsLoopPage && (position.Index < 0 || position.Index >= pages.Count)) break;
                var next = factory.CreatePageFrame(position, direction); if (next is null || next.FrameRange == current.FrameRange) break;
                var nextBounds = PageFrameContainerLayout.Layout(bounds, SizeOf(next), context, direction);
                frames.Add(new(next, nextBounds)); current = next; bounds = nextBounds;
                if (context.FrameOrientation == PageFrameOrientation.Vertical)
                { if (direction < 0 ? bounds.Bottom < viewport.Top - viewport.Height : bounds.Top > viewport.Bottom + viewport.Height) break; }
                else
                {
                    bool leftward = (context.ReadOrder == PageReadOrder.LeftToRight ? direction : -direction) < 0;
                    if (leftward ? bounds.Right < viewport.Left - viewport.Width : bounds.Left > viewport.Right + viewport.Width) break;
                }
            }
        }
    }
    /// <summary>选择距视口中心最近的真实帧；滚动锚点仍使用原PagePosition，不按文件名重新定位。</summary>
    public PanoramaFrame? FindNearest(double x, double y) => Frames.MinBy(f => Distance(f.Bounds, x, y));
    private static double Distance(Rect r, double x, double y)
    {
        var dx = Math.Max(Math.Max(r.Left - x, 0), x - r.Right); var dy = Math.Max(Math.Max(r.Top - y, 0), y - r.Bottom);
        return dx * dx + dy * dy;
    }
}
