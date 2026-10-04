namespace NeeView;

/// <summary>Mac全景展示方式；分页仍由原IsPanorama开关及PageFrame控制。</summary>
public enum BrowseLayoutMode { Paged, Continuous, Masonry }

/// <summary>无界面依赖的图片位置；单位为DIP，索引对应原排序后的Page。</summary>
public readonly record struct BrowseItemRect(double X, double Y, double Width, double Height)
{
    public double Bottom => Y + Height;
    public bool Contains(double x, double y) => x >= X && x < X + Width && y >= Y && y < Bottom;
}

/// <summary>不可变的分段布局快照；尺寸补齐复用未变前缀，按列查询不扫描全书。</summary>
public sealed class BrowseLayout
{
    /// <summary>每段保存列累计高度检查点；改变首项时最坏仍需重算整个尾部。</summary>
    public const int CheckpointSize = 256;
    private const double Gap = 8;
    private readonly Block[] _blocks;
    private readonly double _cellWidth, _originX;
    private readonly bool _rightToLeft;
    private readonly int _count;
    public IReadOnlyList<BrowseItemRect> Items { get; }
    public double Height { get; }
    public double Width { get; }
    public int ColumnCount { get; }
    public int BlockCount => _blocks.Length;
    /// <summary>本次快照实际计算的条目数；诊断不包含图片或路径。</summary>
    public int RecomputedItemCount { get; }
    public int ReusedBlockCount { get; }

    /// <summary>按原内容序列分配，等高列按阅读方向选择；输入和检查点均由快照独占。</summary>
    /// <param name="sizes">页面尺寸元数据，不包含像素。</param>
    /// <param name="width">可用视口宽度。</param>
    /// <param name="mode">连续模式单列，瀑布按目标列宽自动分列。</param>
    /// <param name="columnWidth">目标列宽。</param>
    /// <param name="rightToLeft">等高时从右侧开始投放。</param>
    /// <param name="continuousScale">连续模式相对视口的缩放。</param>
    /// <param name="token">每段及段内检查取消，旧计算不继续占用后台槽。</param>
    public BrowseLayout(IReadOnlyList<Size> sizes, double width, BrowseLayoutMode mode, double columnWidth = 320,
        bool rightToLeft = false, double continuousScale = 1, CancellationToken token = default)
    {
        token.ThrowIfCancellationRequested();
        width = double.IsFinite(width) ? Math.Max(32, width) : 320;
        columnWidth = double.IsFinite(columnWidth) ? Math.Clamp(columnWidth, 96, 1600) : 320;
        ColumnCount = mode == BrowseLayoutMode.Masonry ? Math.Clamp((int)Math.Round((width - Gap) / (columnWidth + Gap)), 1, 32) : 1;
        double contentWidth = mode == BrowseLayoutMode.Continuous ? width * (double.IsFinite(continuousScale) ? Math.Clamp(continuousScale, .1, 8) : 1) : width;
        Width = Math.Max(width, contentWidth); _rightToLeft = rightToLeft;
        _cellWidth = Math.Max(1, (contentWidth - Gap * (ColumnCount + 1)) / ColumnCount);
        _originX = Math.Max(0, (width - contentWidth) / 2) + Gap;
        _count = sizes.Count; _blocks = new Block[(_count + CheckpointSize - 1) / CheckpointSize];
        var heights = new double[ColumnCount]; Array.Fill(heights, Gap);
        for (int b = 0; b < _blocks.Length; b++)
        {
            token.ThrowIfCancellationRequested();
            int start = b * CheckpointSize;
            var part = new Size[Math.Min(CheckpointSize, _count - start)];
            for (int i = 0; i < part.Length; i++) part[i] = sizes[start + i];
            _blocks[b] = BuildBlock(part, heights, token);
        }
        Items = new ItemList(this); Height = _count == 0 ? 0 : heights.Max(); RecomputedItemCount = _count;
    }

    /// <summary>只补齐实际变化的尺寸，从最早受影响检查点重算，累计高度收敛后复用尾部。</summary>
    /// <param name="changes">当前原页面顺序中的索引和尺寸；调用方在计算期间不得修改字典。</param>
    /// <param name="token">后台重排取消。</param>
    /// <returns>独立快照；无变化返回原快照，旧快照一直可安全查询。</returns>
    public BrowseLayout WithSizes(IReadOnlyDictionary<int, Size> changes, CancellationToken token = default)
    {
        token.ThrowIfCancellationRequested();
        var actual = new Dictionary<int, Size>();
        foreach (var pair in changes)
        {
            token.ThrowIfCancellationRequested();
            if (pair.Key < 0 || pair.Key >= _count) throw new ArgumentOutOfRangeException(nameof(changes));
            if (_blocks[pair.Key / CheckpointSize].Sizes[pair.Key % CheckpointSize] != pair.Value) actual.Add(pair.Key, pair.Value);
        }
        return actual.Count == 0 ? this : new(this, actual, token);
    }

    /// <summary>读取本快照实际使用的尺寸，检测其他消费者/已取消探测补齐的元数据。</summary>
    /// <param name="index">当前原页面顺序索引。</param>
    /// <returns>独立于可变PageContent的尺寸值。</returns>
    public Size GetPageSize(int index) => index >= 0 && index < _count
        ? _blocks[index / CheckpointSize].Sizes[index % CheckpointSize] : throw new ArgumentOutOfRangeException(nameof(index));

    /// <summary>复制轻量段引用；段数据不修改旧快照，不复制整个未变前缀的逐页矩形。</summary>
    /// <param name="previous">当前已发布的只读快照。</param>
    /// <param name="changes">已验证且确实变化的尺寸。</param>
    /// <param name="token">段级及段内取消令牌。</param>
    private BrowseLayout(BrowseLayout previous, Dictionary<int, Size> changes, CancellationToken token)
    {
        _count = previous._count; _cellWidth = previous._cellWidth; _originX = previous._originX; _rightToLeft = previous._rightToLeft;
        Width = previous.Width; ColumnCount = previous.ColumnCount; _blocks = (Block[])previous._blocks.Clone();
        int first = changes.Keys.Min() / CheckpointSize, last = changes.Keys.Max() / CheckpointSize;
        var heights = first == 0 ? Enumerable.Repeat(Gap, ColumnCount).ToArray() : (double[])_blocks[first - 1].EndHeights.Clone();
        int rebuilt = 0, computed = 0;
        for (int b = first; b < _blocks.Length; b++)
        {
            token.ThrowIfCancellationRequested();
            var old = _blocks[b]; var sizes = (Size[])old.Sizes.Clone();
            for (int i = 0; i < sizes.Length; i++) if (changes.TryGetValue(b * CheckpointSize + i, out var size)) sizes[i] = size;
            _blocks[b] = BuildBlock(sizes, heights, token); rebuilt++; computed += sizes.Length;
            // 最短列投放可能改变后续所有列；只有精确收敛且后面无尺寸变化才停止。
            if (b >= last && heights.AsSpan().SequenceEqual(old.EndHeights)) break;
        }
        Items = new ItemList(this); Height = _count == 0 ? 0 : _blocks[^1].EndHeights.Max();
        RecomputedItemCount = computed; ReusedBlockCount = _blocks.Length - rebuilt;
    }

    /// <summary>在私有数组上沿旧最短列/阅读方向算法计算一个检查点段。</summary>
    /// <param name="sizes">归新段独占的尺寸数组。</param>
    /// <param name="heights">本次计算独占的累计高度，原地推进到段末。</param>
    /// <param name="token">每32项检查取消。</param>
    /// <returns>包含独立段末检查点的不可变私有段。</returns>
    private Block BuildBlock(Size[] sizes, double[] heights, CancellationToken token)
    {
        var rects = new BrowseItemRect[sizes.Length];
        var columns = Enumerable.Range(0, ColumnCount).Select(_ => new List<int>()).ToArray();
        for (int i = 0; i < sizes.Length; i++)
        {
            if ((i & 31) == 0) token.ThrowIfCancellationRequested();
            int column = _rightToLeft ? ColumnCount - 1 : 0;
            for (int k = 1; k < ColumnCount; k++)
            {
                int candidate = _rightToLeft ? ColumnCount - 1 - k : k;
                if (heights[candidate] < heights[column]) column = candidate;
            }
            var size = sizes[i];
            double aspect = size.Width > 0 && size.Height > 0 && double.IsFinite(size.Width + size.Height) ? size.Height / size.Width : 1.5;
            rects[i] = new(_originX + column * (_cellWidth + Gap), heights[column], _cellWidth, Math.Clamp(_cellWidth * aspect, 1, 10_000_000));
            heights[column] = rects[i].Bottom + Gap; columns[column].Add(i);
        }
        return new(sizes, rects, columns.Select(c => c.ToArray()).ToArray(), (double[])heights.Clone());
    }

    /// <summary>按列二分检查点和段内首个相交项；只遍历可见段及条目。</summary>
    /// <param name="top">视口或预取窗口顶部。</param>
    /// <param name="bottom">视口或预取窗口底部。</param>
    /// <returns>按原内容顺序排列的相交索引。</returns>
    public int[] Query(double top, double bottom)
    {
        if (bottom <= top) return [];
        var result = new List<int>();
        for (int c = 0; c < ColumnCount; c++)
        {
            int low = 0, high = _blocks.Length;
            while (low < high) { int middle = (low + high) / 2; if (_blocks[middle].EndHeights[c] - Gap <= top) low = middle + 1; else high = middle; }
            bool done = false;
            for (int b = low; b < _blocks.Length && !done; b++)
            {
                var block = _blocks[b]; var indices = block.Columns[c]; int left = 0, right = indices.Length;
                while (left < right) { int middle = (left + right) / 2; if (block.Rects[indices[middle]].Bottom <= top) left = middle + 1; else right = middle; }
                for (int i = left; i < indices.Length; i++)
                {
                    if (block.Rects[indices[i]].Y >= bottom) { done = true; break; }
                    result.Add(b * CheckpointSize + indices[i]);
                }
            }
        }
        result.Sort(); return result.ToArray();
    }
    /// <summary>命中仅查询对应高度窗口，不扫描全部页面。</summary>
    public int HitTest(double x, double y) => Query(y, y + 1).FirstOrDefault(i => Items[i].Contains(x, y), -1);

    private sealed record Block(Size[] Sizes, BrowseItemRect[] Rects, int[][] Columns, double[] EndHeights);
    /// <summary>只读索引适配，逐页访问O(1)，外部不能修改共享段。</summary>
    private sealed class ItemList(BrowseLayout layout) : IReadOnlyList<BrowseItemRect>
    {
        public int Count => layout._count;
        public BrowseItemRect this[int index] => index >= 0 && index < Count
            ? layout._blocks[index / CheckpointSize].Rects[index % CheckpointSize] : throw new ArgumentOutOfRangeException(nameof(index));
        public IEnumerator<BrowseItemRect> GetEnumerator() { foreach (var block in layout._blocks) foreach (var item in block.Rects) yield return item; }
        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
    }
}
