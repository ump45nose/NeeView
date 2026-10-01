namespace NeeView.Core;

public readonly record struct RectD(double X, double Y, double Width, double Height)
{
    public double Bottom => Y + Height;
    public bool Intersects(double top, double bottom) => Bottom >= top && Y <= bottom;
}
public sealed record LayoutItem(PageDescriptor Page, int Part, RectD Bounds, bool Divided = false);
public sealed record LayoutInput(IReadOnlyList<PageDescriptor> Pages, ReaderOptions Options,
    double Width, double Height, ReadingAnchor? Anchor, double DeviceScale = 1, CancellationToken Cancellation = default);
public interface ILayoutStrategy { LayoutSnapshot Calculate(LayoutInput input); }

/// <summary>布局快照按列索引查询，避免每帧遍历万条目。</summary>
public sealed class LayoutSnapshot
{
    private readonly IReadOnlyList<LayoutItem[]> _columns;
    public IReadOnlyList<LayoutItem> Items { get; }
    public double Height { get; }
    public LayoutSnapshot(IReadOnlyList<LayoutItem> items, double height)
    {
        Items = items; Height = height;
        _columns = items.GroupBy(i => i.Bounds.X).Select(c => c.OrderBy(i => i.Bounds.Y).ToArray()).ToArray();
    }
    /// <summary>输入滚动范围，按列二分查找可见项；返回几何，不持有图像。</summary>
    public IEnumerable<LayoutItem> Visible(double top, double bottom)
    {
        foreach (var column in _columns)
        {
            var low = 0; var high = column.Length;
            while (low < high)
            {
                var middle = (low + high) / 2;
                if (column[middle].Bounds.Bottom < top) low = middle + 1; else high = middle;
            }
            for (var i = low; i < column.Length && column[i].Bounds.Y <= bottom; i++) yield return column[i];
        }
    }
    /// <summary>按内容身份和页内比例恢复滚动坐标。</summary>
    public double RestoreY(ReadingAnchor? anchor)
    {
        var item = Items.FirstOrDefault(i => i.Page.Id == anchor?.Content && i.Part == anchor.Part);
        return item is null ? 0 : item.Bounds.Y + Math.Clamp(anchor!.RelativeY, 0, 1) * item.Bounds.Height;
    }
}

public static class ReadingRules
{
    /// <summary>用户旋转改变布局尺寸；EXIF 方向已经由解码器探测处理。</summary>
    public static PixelSize Size(PageDescriptor page, ReaderOptions options)
    {
        var size = page.Size ?? new PixelSize(900, 1200);
        return Math.Abs(options.Rotation) % 180 == 90 ? new(size.Height, size.Width) : size;
    }
    /// <summary>从锚点生成分页 frame；参照原 PageFrameFactory 的宽页与首尾规则。</summary>
    public static IReadOnlyList<(PageDescriptor Page, int Part, bool Divided)> Frame(
        IReadOnlyList<PageDescriptor> pages, ReadingAnchor? anchor, ReaderOptions options)
    {
        if (pages.Count == 0) return [];
        var index = Math.Max(0, pages.ToList().FindIndex(p => p.Id == anchor?.Content));
        var page = pages[index];
        if (!options.DoublePage)
            return [(page, options.DivideWide && Size(page, options).IsLandscape ? Math.Clamp(anchor?.Part ?? 0, 0, 1) : 0,
                options.DivideWide && Size(page, options).IsLandscape)];
        // 宽图和首末页不能与邻页配对；分割只在单页模式有效。
        if ((options.WidePage && Size(page, options).IsLandscape) || (options.SingleFirst && index == 0)
            || index + 1 >= pages.Count || (options.SingleLast && index == pages.Count - 1)) return [(page, 0, false)];
        var second = pages[index + 1];
        if ((options.WidePage && Size(second, options).IsLandscape) || (options.SingleLast && index + 1 == pages.Count - 1))
            return [(page, 0, false)];
        return [(page, 0, false), (second, 0, false)];
    }
    /// <summary>输入方向和是否单资源步进，返回新的内容锚点；边界不循环。</summary>
    public static ReadingAnchor? Navigate(IReadOnlyList<PageDescriptor> pages, ReadingAnchor? anchor,
        ReaderOptions options, int direction, bool onePage = false)
    {
        if (pages.Count == 0) return null;
        var index = Math.Max(0, pages.ToList().FindIndex(p => p.Id == anchor?.Content));
        if (!onePage && options.Mode == ReaderMode.Paged && !options.DoublePage && options.DivideWide && Size(pages[index], options).IsLandscape)
        {
            var nextPart = (anchor?.Part ?? 0) + direction;
            if (nextPart is >= 0 and <= 1) return new(pages[index].Id, nextPart);
        }
        if (direction > 0)
            index += !onePage && options.Mode == ReaderMode.Paged ? Frame(pages, anchor, options).Count : 1;
        else if (!onePage && options.Mode == ReaderMode.Paged && options.DoublePage)
        {
            // 重建前向 frame 边界，保证上一 frame 与下一 frame 可逆。
            var previous = 0;
            for (var cursor = 0; cursor < index;)
            {
                previous = cursor;
                cursor += Frame(pages, new(pages[cursor].Id), options).Count;
                if (cursor >= index) break;
            }
            index = previous;
        }
        else index--;
        if (index < 0 || index >= pages.Count) return anchor ?? new(pages[Math.Clamp(index, 0, pages.Count - 1)].Id);
        var part = !onePage && options.Mode == ReaderMode.Paged && direction < 0 && !options.DoublePage && options.DivideWide && Size(pages[index], options).IsLandscape ? 1 : 0;
        return new(pages[index].Id, part);
    }
}
public sealed class PagedLayout : ILayoutStrategy
{
    /// <summary>输入视口/设备比例，输出当前 frame；100% 使用设备像素比例。</summary>
    public LayoutSnapshot Calculate(LayoutInput input)
    {
        var frame = ReadingRules.Frame(input.Pages, input.Anchor, input.Options);
        var sizes = frame.Select(p => ReadingRules.Size(p.Page, input.Options)).ToArray();
        var widths = sizes.Select((s, i) => (double)s.Width / (frame[i].Divided ? 2 : 1)).ToArray();
        var totalWidth = widths.Sum(); var height = sizes.Select(s => (double)s.Height).DefaultIfEmpty(1).Max();
        var scale = input.Options.Scale switch
        {
            ScaleMode.ActualPixels => 1 / Math.Max(0.1, input.DeviceScale),
            ScaleMode.FitWidth => input.Width / Math.Max(1, totalWidth),
            _ => Math.Min(input.Width / Math.Max(1, totalWidth), input.Height / height)
        } * input.Options.Zoom;
        var order = Enumerable.Range(0, frame.Count);
        if (input.Options.Direction == ReadDirection.RightToLeft) order = order.Reverse();
        var x = Math.Max(0, (input.Width - totalWidth * scale) / 2);
        var items = new List<LayoutItem>();
        foreach (var i in order)
        {
            var h = sizes[i].Height * scale; var w = widths[i] * scale;
            items.Add(new(frame[i].Page, frame[i].Part, new(x, Math.Max(0, (input.Height - h) / 2), w, h), frame[i].Divided));
            x += w;
        }
        return new(items, Math.Max(input.Height, height * scale));
    }
}
public sealed class ContinuousLayout : ILayoutStrategy
{
    /// <summary>输入页面尺寸，输出累计高度；尺寸未知时使用稳定占位比。</summary>
    public LayoutSnapshot Calculate(LayoutInput input)
    {
        var y = 0d; var items = new List<LayoutItem>();
        var width = Math.Max(1, input.Width * input.Options.Zoom);
        foreach (var page in input.Pages)
        {
            input.Cancellation.ThrowIfCancellationRequested();
            var height = width / ReadingRules.Size(page, input.Options).Aspect;
            items.Add(new(page, 0, new(Math.Max(0, (input.Width - width) / 2), y, width, height)));
            y += height + input.Options.Gap;
        }
        return new(items, y);
    }
}
public sealed class MasonryLayout : ILayoutStrategy
{
    private const int SegmentSize = 256;
    private sealed record Previous(LayoutInput Input, LayoutSnapshot Layout, Dictionary<int, double[]> Checkpoints);
    private Previous? _previous;
    /// <summary>输入排序页面，按最短列投放；等高时按阅读方向选择。</summary>
    public LayoutSnapshot Calculate(LayoutInput input)
    {
        input.Cancellation.ThrowIfCancellationRequested();
        var gap = input.Options.Gap;
        var count = Math.Max(1, (int)((input.Width + gap) / (Math.Max(80, input.Options.ColumnWidth * input.Options.Zoom) + gap)));
        var width = Math.Max(1, (input.Width - gap * (count - 1)) / count);
        var heights = new double[count]; var items = new List<LayoutItem>();
        var checkpoints = new Dictionary<int, double[]> { [0] = new double[count] }; var start = 0;
        if (_previous is { } previous && previous.Input.Width == input.Width && previous.Input.Options == input.Options)
        {
            // 检查顺序/元数据相同的前缀，尺寸晚到只从之前的分段检查点重新投放。
            var prefix = 0;
            while (prefix < Math.Min(input.Pages.Count, previous.Input.Pages.Count) && input.Pages[prefix] == previous.Input.Pages[prefix])
            { input.Cancellation.ThrowIfCancellationRequested(); prefix++; }
            if (prefix == input.Pages.Count && prefix == previous.Input.Pages.Count) return previous.Layout;
            start = prefix / SegmentSize * SegmentSize;
            if (previous.Checkpoints.TryGetValue(start, out var saved))
            {
                heights = (double[])saved.Clone(); items.AddRange(previous.Layout.Items.Take(start));
                checkpoints = previous.Checkpoints.Where(pair => pair.Key <= start).ToDictionary(pair => pair.Key, pair => pair.Value);
            }
            else start = 0;
        }
        for (var index = start; index < input.Pages.Count; index++)
        {
            input.Cancellation.ThrowIfCancellationRequested(); var page = input.Pages[index];
            var column = input.Options.Direction == ReadDirection.RightToLeft ? count - 1 : 0;
            var order = input.Options.Direction == ReadDirection.RightToLeft ? Enumerable.Range(0, count).Reverse() : Enumerable.Range(0, count);
            foreach (var candidate in order) if (heights[candidate] < heights[column]) column = candidate;
            var height = width / ReadingRules.Size(page, input.Options).Aspect;
            items.Add(new(page, 0, new(column * (width + gap), heights[column], width, height)));
            heights[column] += height + gap;
            if ((index + 1) % SegmentSize == 0) checkpoints[index + 1] = (double[])heights.Clone();
        }
        var result = new LayoutSnapshot(items, heights.Max());
        // 只有成功计算才能替换缓存；取消不会留下半份高度状态。不得并发调用同一策略实例。
        _previous = new(input with { Pages = input.Pages.ToArray(), Cancellation = default, Anchor = null }, result, checkpoints); return result;
    }
}
