using NeeView.Core;
using Xunit;

namespace NeeView.Portable.Tests;

public sealed class CoreBehaviorTests
{
    /// <summary>分割页在书籍边界不循环，连续模式不携带半页区域。</summary>
    [Fact]
    public void DivideBoundariesNeverWrapAndContinuousUsesWholePages()
    {
        var page = FakeSource.SamplePage("wide", 1400, 800); var options = new ReaderOptions { DivideWide = true };
        Assert.Equal(new ReadingAnchor(page.Id, 0), ReadingRules.Navigate([page], new(page.Id), options, -1));
        Assert.Equal(new ReadingAnchor(page.Id, 1), ReadingRules.Navigate([page], new(page.Id, 1), options, 1));
        var second = FakeSource.SamplePage("next");
        Assert.Equal(0, ReadingRules.Navigate([page, second], new(second.Id), options with { Mode = ReaderMode.Continuous }, -1)!.Part);
    }
    [Theory]
    [InlineData(-2, 0, -4)] [InlineData(-2, 1, -3)] [InlineData(0, 0, 0)] [InlineData(1, 1, 3)]
    public void HalfPageRetainsNegativePosition(int index, int part, int value)
    { var position = new PagePosition(index, part); Assert.Equal(value, position.Value); Assert.Equal(index, position.Index); Assert.Equal(part, position.Part); }
    [Fact]
    public void DirectedRangeRetainsClosedInterval()
    { var range = new PageRange(new(2, 1), -3); Assert.Equal(new(1, 1), range.Min); Assert.Equal(new(2, 1), range.Max); Assert.Equal(new(1, 0), range.Next(-1)); }
    [Fact]
    public void DivideOnlyAppliesInSinglePageMode()
    {
        var pages = new[] { FakeSource.SamplePage("wide", 1400, 800), FakeSource.SamplePage("normal") };
        var single = new ReaderOptions { DivideWide = true };
        var first = new ReadingAnchor(pages[0].Id);
        Assert.True(ReadingRules.Frame(pages, first, single)[0].Divided);
        Assert.Equal(new ReadingAnchor(pages[0].Id, 1), ReadingRules.Navigate(pages, first, single, 1));
        Assert.Equal(pages[1].Id, ReadingRules.Navigate(pages, first with { Part = 1 }, single, 1)!.Content);
        var doublePage = single with { DoublePage = true };
        Assert.False(ReadingRules.Frame(pages, first, doublePage)[0].Divided);
        Assert.Single(ReadingRules.Frame(pages, first, doublePage));
    }
    [Theory] [InlineData(false)] [InlineData(true)]
    public void FirstLastAndWidePagesDoNotPair(bool rtl)
    {
        var pages = new[] { FakeSource.SamplePage("cover"), FakeSource.SamplePage("1"), FakeSource.SamplePage("2"), FakeSource.SamplePage("last") };
        var options = new ReaderOptions { DoublePage = true, SingleFirst = true, SingleLast = true, Direction = rtl ? ReadDirection.RightToLeft : ReadDirection.LeftToRight };
        var anchor = new ReadingAnchor(pages[0].Id);
        Assert.Single(ReadingRules.Frame(pages, anchor, options));
        var next = ReadingRules.Navigate(pages, anchor, options, 1)!;
        Assert.Equal(2, ReadingRules.Frame(pages, next, options).Count);
        var last = ReadingRules.Navigate(pages, next, options, 1)!;
        Assert.Single(ReadingRules.Frame(pages, last, options));
        Assert.Equal(next.Content, ReadingRules.Navigate(pages, last, options, -1)!.Content);
    }
    [Fact]
    public void DoublePageLayoutMirrorsVisualOrder()
    {
        var pages = new[] { FakeSource.SamplePage("1"), FakeSource.SamplePage("2") };
        var rtl = new PagedLayout().Calculate(new(pages, new() { DoublePage = true }, 1200, 900, new(pages[0].Id)));
        Assert.Equal(pages[1].Id, rtl.Items[0].Page.Id);
        var ltr = new PagedLayout().Calculate(new(pages, new() { DoublePage = true, Direction = ReadDirection.LeftToRight }, 1200, 900, new(pages[0].Id)));
        Assert.Equal(pages[0].Id, ltr.Items[0].Page.Id);
    }
    [Fact]
    public void ActualPixelsUsesDisplayDeviceScale()
    {
        var page = FakeSource.SamplePage();
        var layout = new PagedLayout().Calculate(new([page], new() { Scale = ScaleMode.ActualPixels }, 1200, 900, new(page.Id), 2));
        Assert.Equal(300, layout.Items[0].Bounds.Width);
    }
    [Fact]
    public void TenThousandMasonryEntriesQueryOnlyViewport()
    {
        var pages = Enumerable.Range(0, 10000).Select(i => FakeSource.SamplePage(i.ToString(), 400 + i % 7 * 50, 600 + i % 11 * 80)).ToArray();
        var layout = new MasonryLayout().Calculate(new(pages, new() { Mode = ReaderMode.Masonry }, 1000, 800, null));
        Assert.Equal(10000, layout.Items.Count); Assert.InRange(layout.Visible(10000, 10800).Count(), 1, 20);
        Assert.True(layout.Items[0].Bounds.X > layout.Items[1].Bounds.X);
        var item = layout.Items[5000]; Assert.Equal(item.Bounds.Y + item.Bounds.Height * 0.4, layout.RestoreY(new(item.Page.Id, 0, 0.4)));
    }
    [Fact]
    public void NaturalSortAndSettingsRestoreKeepMeaning()
    {
        Assert.True(NaturalNameComparer.Instance.Compare("2.jpg", "10.jpg") < 0);
        var defaults = new ReaderOptions { Zoom = 1 }; var current = new ReaderOptions { Zoom = 2, DoublePage = true }; var saved = new ReaderOptions { Zoom = 3 };
        var mixed = OptionRestore.Mix(defaults, current, saved, new Dictionary<string, RestorePolicy> { [nameof(ReaderOptions.Zoom)] = RestorePolicy.Continue });
        Assert.Equal(2, mixed.Zoom); Assert.False(mixed.DoublePage);
    }
}
