namespace NeeView.Engine.Tests;

/// <summary>检查点优化与完整计算对照，旧快照和可见查询不得受到新尺寸影响。</summary>
public sealed class BrowseCheckpointTests
{
    [Theory]
    [InlineData(BrowseLayoutMode.Continuous, false)]
    [InlineData(BrowseLayoutMode.Continuous, true)]
    [InlineData(BrowseLayoutMode.Masonry, false)]
    [InlineData(BrowseLayoutMode.Masonry, true)]
    public void RepeatedDimensionUpdatesMatchFullLayoutAndPreserveOldSnapshots(BrowseLayoutMode mode, bool rtl)
    {
        var random = new Random(314);
        var sizes = Enumerable.Range(0, 10_000).Select(i => new Size(100 + i % 700, 20 + i % 3000)).ToArray();
        var layout = new BrowseLayout(sizes, 1200, mode, rightToLeft: rtl);
        for (int step = 0; step < 12; step++)
        {
            var before = layout.Items.ToArray();
            var changes = new Dictionary<int, Size>();
            for (int i = 0; i < 4; i++) { int index = random.Next(sizes.Length); sizes[index] = new(random.Next(10, 2000), random.Next(10, 20000)); changes[index] = sizes[index]; }
            var next = layout.WithSizes(changes, TestContext.Current.CancellationToken);
            var full = new BrowseLayout(sizes, 1200, mode, rightToLeft: rtl);
            Assert.Equal(full.Items, next.Items); Assert.Equal(full.Height, next.Height); Assert.Equal(before, layout.Items);
            for (int i = 0; i < 8; i++)
            {
                double top = random.NextDouble() * next.Height, bottom = top + 700;
                var expected = next.Items.Select((rect, index) => (rect, index)).Where(e => e.rect.Bottom > top && e.rect.Y < bottom).Select(e => e.index);
                Assert.Equal(expected, next.Query(top, bottom));
            }
            layout = next;
        }
    }

    [Fact]
    public void LatePageChangesReusePrefixAndEqualGeometryConvergesAtCheckpoint()
    {
        var sizes = Enumerable.Repeat(new Size(400, 600), 10_000).ToArray();
        var layout = new BrowseLayout(sizes, 1200, BrowseLayoutMode.Masonry);
        var next = layout.WithSizes(new Dictionary<int, Size> { [9000] = new(400, 2000), [9800] = new(400, 1200) }, TestContext.Current.CancellationToken);
        Assert.InRange(next.RecomputedItemCount, 1, 1040); Assert.True(next.ReusedBlockCount >= 35);
        Assert.Equal(layout.Items.Take(8960), next.Items.Take(8960));
        // 同比例新尺寸不会改变最短列/累计高度，后续段应全部共享。
        var converged = layout.WithSizes(new Dictionary<int, Size> { [300] = new(800, 1200) }, TestContext.Current.CancellationToken);
        Assert.Equal(256, converged.RecomputedItemCount); Assert.Equal(layout.BlockCount - 1, converged.ReusedBlockCount);
        Assert.Equal(layout.Items, converged.Items); Assert.Equal(new Size(800, 1200), converged.GetPageSize(300));
        Assert.Equal(new Size(400, 600), layout.GetPageSize(300));
        Assert.Same(layout, layout.WithSizes(new Dictionary<int, Size> { [300] = new(400, 600) }, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task CancelledAndConcurrentReflowCannotModifyPublishedLayout()
    {
        var sizes = Enumerable.Repeat(new Size(400, 600), 100_000).ToArray();
        var layout = new BrowseLayout(sizes, 1200, BrowseLayoutMode.Masonry);
        var expected = layout.Query(layout.Height * .5, layout.Height * .5 + 700);
        using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
        Assert.Throws<OperationCanceledException>(() => layout.WithSizes(new Dictionary<int, Size> { [0] = new(1, 1000) }, cancelled.Token));
        Assert.Throws<OperationCanceledException>(() => new BrowseLayout(sizes, 1200, BrowseLayoutMode.Masonry, token: cancelled.Token));
        using var during = new CancellationTokenSource();
        Assert.Throws<OperationCanceledException>(() => new BrowseLayout(new CancellingSizes(sizes, during), 1200, BrowseLayoutMode.Masonry, token: during.Token));
        var update = Task.Run(() => layout.WithSizes(new Dictionary<int, Size> { [0] = new(1, 1000) }, TestContext.Current.CancellationToken), TestContext.Current.CancellationToken);
        for (int i = 0; i < 100; i++) Assert.Equal(expected, layout.Query(layout.Height * .5, layout.Height * .5 + 700));
        var next = await update; Assert.NotEqual(layout.Items[0], next.Items[0]); Assert.Equal(expected, layout.Query(layout.Height * .5, layout.Height * .5 + 700));
        Assert.Throws<ArgumentOutOfRangeException>(() => layout.WithSizes(new Dictionary<int, Size> { [-1] = new(1, 1) }, TestContext.Current.CancellationToken));
    }

    [Fact]
    public void SparseColumnsEmptyLayoutsAndCheckpointBoundariesKeepQuerySemantics()
    {
        foreach (int count in new[] { 0, 1, 255, 256, 257, 513 })
        {
            var layout = new BrowseLayout(Enumerable.Range(0, count).Select(i => new Size(100, i % 3 == 0 ? 100000 : 1)).ToArray(), 3000, BrowseLayoutMode.Masonry, 96);
            foreach (double top in new[] { -20d, 0, 8, 9, layout.Height / 2, layout.Height }.Concat(layout.Items.Select(r => r.Bottom).TakeLast(5)))
            {
                double bottom = top + 30;
                var expected = layout.Items.Select((r, i) => (r, i)).Where(e => e.r.Bottom > top && e.r.Y < bottom).Select(e => e.i);
                Assert.Equal(expected, layout.Query(top, bottom));
            }
            Assert.Empty(layout.Query(100, 100));
            if (count > 0) Assert.DoesNotContain(count - 1, layout.Query(-20, layout.Items[count - 1].Y));
        }
    }

    /// <summary>确定性地在复制尺寸期间取消，不用睡眠猜测线程执行时序。</summary>
    private sealed class CancellingSizes(Size[] sizes, CancellationTokenSource cancellation) : IReadOnlyList<Size>
    {
        public int Count => sizes.Length;
        public Size this[int index] { get { if (index == 384) cancellation.Cancel(); return sizes[index]; } }
        public IEnumerator<Size> GetEnumerator() => ((IEnumerable<Size>)sizes).GetEnumerator();
        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
    }
}
