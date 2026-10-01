using NeeView.Core;
using NeeView.Desktop;
using Xunit;

namespace NeeView.Portable.Tests;

/// <summary>验证分段重算与完整布局等价，以及后台需求取消的生命周期。</summary>
public sealed class LayoutCoordinatorTests
{
    /// <summary>尺寸晚到、增删和排序改变后，检查点重算必须与首次完整投放一致。</summary>
    [Fact]
    public void IncrementalMasonryMatchesFreshGeometryAfterEdits()
    {
        var pages = Pages(2000); var strategy = new MasonryLayout();
        var input = new LayoutInput(pages, new() { Mode = ReaderMode.Masonry }, 1280, 800, null);
        _ = strategy.Calculate(input);
        pages[1750] = pages[1750] with { Size = new(600, 4000) };
        foreach (var sequence in new[] { pages, pages.Take(1800).ToArray(), pages.Reverse().ToArray() })
        {
            var changed = input with { Pages = sequence };
            var actual = strategy.Calculate(changed); var expected = new MasonryLayout().Calculate(changed);
            Assert.Equal(expected.Items, actual.Items); Assert.Equal(expected.Height, actual.Height);
        }
        using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        Assert.Throws<OperationCanceledException>(() => strategy.Calculate(input with { Cancellation = cancellation.Token }));
        Assert.Equal(new MasonryLayout().Calculate(input).Items, strategy.Calculate(input).Items);
    }
    /// <summary>快速窗口变化保留最新几何，退休计算可取消，关闭后拒绝新任务。</summary>
    [Fact]
    public async Task BackgroundLayoutUsesLatestViewportAndStops()
    {
        await using var coordinator = new ReaderLayoutCoordinator();
        var input = new LayoutInput(Pages(10000), new() { Mode = ReaderMode.Masonry }, 1280, 800, null);
        var old = coordinator.CalculateAsync(input);
        var resized = input with { Width = 900 };
        var latest = await coordinator.CalculateAsync(resized).WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        try { await old; } catch (OperationCanceledException) { }
        Assert.Equal(new MasonryLayout().Calculate(resized).Items, latest.Items);
        await coordinator.DisposeAsync();
        await Assert.ThrowsAsync<ObjectDisposedException>(async () => await coordinator.CalculateAsync(input));
    }
    /// <summary>构造混合尺寸几何材料，不读取或解码文件。</summary>
    private static PageDescriptor[] Pages(int count) => Enumerable.Range(0, count).Select(i => FakeSource.SamplePage(i.ToString(), 600 + i % 3 * 150, 900 + i % 5 * 100)).ToArray();
}
