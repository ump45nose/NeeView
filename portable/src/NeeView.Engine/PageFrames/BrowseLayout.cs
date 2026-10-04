namespace NeeView;

/// <summary>Mac全景展示方式；分页仍由原IsPanorama开关及PageFrame控制。</summary>
public enum BrowseLayoutMode { Paged, Continuous, Masonry }

/// <summary>无界面依赖的图片位置；单位为DIP，索引对应原排序后的Page。</summary>
public readonly record struct BrowseItemRect(double X, double Y, double Width, double Height)
{
    public double Bottom => Y + Height;
    public bool Contains(double x, double y) => x >= X && x < X + Width && y >= Y && y < Bottom;
}

/// <summary>连续/最短列瀑布布局及按列二分可见查询，不创建控件或读取图片。</summary>
public sealed class BrowseLayout
{
    private readonly List<int>[] _columns;
    public IReadOnlyList<BrowseItemRect> Items { get; }
    public double Height { get; }
    public double Width { get; }
    public int ColumnCount => _columns.Length;

    /// <summary>按原内容序列分配，等高列按阅读方向选择；未知尺寸采用原页面占位比例。</summary>
    /// <param name="sizes">仅页面尺寸元数据，不包含像素。</param>
    /// <param name="width">可用视口宽度。</param>
    /// <param name="mode">连续模式单列，瀑布模式按目标列宽自动分列。</param>
    /// <param name="columnWidth">目标列宽，缩放只改变此值。</param>
    /// <param name="rightToLeft">等高时从右侧开始放置；导航仍沿原内容序列。</param>
    /// <param name="continuousScale">连续模式相对视口的单列缩放，瀑布模式忽略此值。</param>
    public BrowseLayout(IReadOnlyList<Size> sizes, double width, BrowseLayoutMode mode, double columnWidth = 320, bool rightToLeft = false, double continuousScale = 1)
    {
        const double gap = 8;
        width = double.IsFinite(width) ? Math.Max(32, width) : 320;
        columnWidth = double.IsFinite(columnWidth) ? Math.Clamp(columnWidth, 96, 1600) : 320;
        int count = mode == BrowseLayoutMode.Masonry ? Math.Clamp((int)Math.Round((width - gap) / (columnWidth + gap)), 1, 32) : 1;
        double contentWidth = mode == BrowseLayoutMode.Continuous ? width * (double.IsFinite(continuousScale) ? Math.Clamp(continuousScale, .1, 8) : 1) : width;
        Width = Math.Max(width, contentWidth);
        _columns = Enumerable.Range(0, count).Select(_ => new List<int>()).ToArray();
        var heights = new double[count]; Array.Fill(heights, gap);
        double cellWidth = Math.Max(1, (contentWidth - gap * (count + 1)) / count);
        var items = new BrowseItemRect[sizes.Count];
        for (int i = 0; i < sizes.Count; i++)
        {
            int column = rightToLeft ? count - 1 : 0;
            for (int k = 1; k < count; k++)
            {
                int candidate = rightToLeft ? count - 1 - k : k;
                if (heights[candidate] < heights[column]) column = candidate;
            }
            var size = sizes[i];
            double aspect = size.Width > 0 && size.Height > 0 && double.IsFinite(size.Width + size.Height) ? size.Height / size.Width : 1.5;
            items[i] = new(Math.Max(0, (width - contentWidth) / 2) + gap + column * (cellWidth + gap), heights[column], cellWidth, Math.Clamp(cellWidth * aspect, 1, 10_000_000));
            heights[column] = items[i].Bottom + gap; _columns[column].Add(i);
        }
        Items = items; Height = sizes.Count == 0 ? 0 : heights.Max();
    }

    /// <summary>每列二分到首个相交项，再顺序读取可见窗口；代价与可见项而非全书像素有关。</summary>
    /// <param name="top">视口顶部或预取窗口顶部。</param>
    /// <param name="bottom">视口/预取窗口底部。</param>
    /// <returns>按原内容顺序排列的相交索引。</returns>
    public int[] Query(double top, double bottom)
    {
        var result = new List<int>();
        foreach (var column in _columns)
        {
            int low = 0, high = column.Count;
            while (low < high) { int middle = (low + high) / 2; if (Items[column[middle]].Bottom <= top) low = middle + 1; else high = middle; }
            for (int i = low; i < column.Count && Items[column[i]].Y < bottom; i++) result.Add(column[i]);
        }
        result.Sort(); return result.ToArray();
    }
    /// <summary>命中仅查询对应高度窗口，不扫描全部页面。</summary>
    public int HitTest(double x, double y) => Query(y, y + 1).FirstOrDefault(i => Items[i].Contains(x, y), -1);
}
