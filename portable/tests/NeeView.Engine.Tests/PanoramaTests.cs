using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Headless;
using Avalonia.Threading;
using NeeView.PageFrames;
using NeeView.MacOS.Views;
namespace NeeView.Engine.Tests;

/// <summary>原帧级全景组合、容器方向/间隔、分割/宽页与正式查看器资源链回归。</summary>
public sealed class PanoramaTests
{
    [Theory]
    [InlineData(PageFrameOrientation.Horizontal, PageReadOrder.LeftToRight)]
    [InlineData(PageFrameOrientation.Horizontal, PageReadOrder.RightToLeft)]
    [InlineData(PageFrameOrientation.Vertical, PageReadOrder.LeftToRight)]
    [InlineData(PageFrameOrientation.Vertical, PageReadOrder.RightToLeft)]
    public void OriginalFramesRemainPairedAndOrdered(PageFrameOrientation orientation, PageReadOrder order)
    {
        Config.SetCurrent(new()); Config.Current.Book.IsPanorama = true; Config.Current.Book.Orientation = orientation;
        Config.Current.Book.FrameSpace = 17; Config.Current.View.PageMoveType = PageMoveType.Fade;
        var pages = Pages((400, 600), (400, 600), (400, 600), (400, 600), (400, 600), (400, 600));
        var context = new PageFrameContext(new() { PageMode = PageMode.WidePage, BookReadOrder = order }, Config.Current) { CanvasSize = new(800, 600) };
        var selected = new PageFrameFactory(context, new BookContext(pages), new ContentSizeCalculator(context)).CreatePageFrame(PagePosition.Zero, 1)!;
        var window = new PageFramePanorama(pages, selected, context, new(-400, -300, 800, 600));
        Assert.Equal(3, window.Frames.Count); Assert.All(window.Frames, f => Assert.Equal(2, f.Frame.Elements.Count));
        var first = window.Frames[0].Bounds; var next = window.Frames[1].Bounds;
        if (orientation == PageFrameOrientation.Vertical) Assert.Equal(first.Bottom + 17, next.Top);
        else if (order == PageReadOrder.LeftToRight) Assert.Equal(first.Right + 17, next.Left);
        else Assert.Equal(first.Left - 17, next.Right);
        Assert.Equal(orientation == PageFrameOrientation.Horizontal ? PageStretchMode.UniformToVertical : PageStretchMode.UniformToHorizontal, context.StretchMode);
    }

    [Fact]
    public void OriginalSplitWideFirstLastAndDummyRulesAreNotReimplemented()
    {
        Config.SetCurrent(new()); Config.Current.Book.IsPanorama = true; Config.Current.Book.Orientation = PageFrameOrientation.Vertical;
        var pages = Pages((1200, 600), (400, 600), (400, 600), (400, 600));
        var context = new PageFrameContext(new() { IsSupportedDividePage = true }, Config.Current);
        var selected = new PageFrameFactory(context, new BookContext(pages), new ContentSizeCalculator(context)).CreatePageFrame(PagePosition.Zero, 1)!;
        var window = new PageFramePanorama(pages, selected, context, new(-500, -400, 1000, 800));
        Assert.Equal(new PagePosition(0, 1), window.Frames[1].Frame.FrameRange.Min);
        Assert.Same(window.Frames[0].Frame.Elements[0].Page, window.Frames[1].Frame.Elements[0].Page);
        Config.Current.Book.IsInsertDummyPage = true; Config.Current.Book.IsInsertDummyFirstPage = true;
        context = new(new() { PageMode = PageMode.WidePage, IsSupportedSingleFirstPage = true, IsSupportedSingleLastPage = true }, Config.Current);
        selected = new PageFrameFactory(context, new BookContext(pages), new ContentSizeCalculator(context)).CreatePageFrame(PagePosition.Zero, 1)!;
        window = new(pages, selected, context, new(-500, -400, 1000, 800));
        Assert.Single(window.Frames[0].Frame.Elements, e => !e.IsDummy);
        Assert.All(window.Frames, f => Assert.InRange(f.Frame.Elements.Count(e => !e.IsDummy), 1, 2));
        Assert.Equal(3, window.Frames.Last().Frame.FrameRange.Max.Index);
    }

    [AvaloniaTheory]
    [InlineData(PageFrameOrientation.Horizontal)]
    [InlineData(PageFrameOrientation.Vertical)]
    public async Task OfficialViewerUsesSameLeasesTransformsAndScroll(PageFrameOrientation orientation)
    {
        using var fixture = new Fixture(); var state = new SaveData(fixture.State); await state.LoadAsync(TestContext.Current.CancellationToken);
        await using var operation = fixture.Operation(state); using var factory = new BitmapFactory(new NeeView.Backends.MagickImageDecoder());
        var reader = new ReaderView(); reader.Attach(operation, factory); var window = new Window { Width = 800, Height = 600, Content = reader }; window.Show();
        try
        {
            await operation.OpenAsync(fixture.Images, TestContext.Current.CancellationToken); await operation.SetBrowseModeAsync(BrowseLayoutMode.Panorama);
            await operation.SetOrientationAsync(orientation); Config.Current.View.PageMoveDuration = 0; Config.Current.View.ScrollDuration = 0;
            await reader.RefreshAsync(); Assert.True(operation.IsFrameReading); Assert.Null(reader.BrowseLayout);
            Assert.True(reader.PanoramaFrames.Count > 1); Assert.InRange(reader.DisplayCount, 2, 5);
            await reader.ZoomAsync(1.5, new(400, 300)); Assert.True(reader.TransformScale > 1);
            await reader.RotateAsync(1, new() { Angle = 90 }); Assert.Equal(90, reader.TransformAngle);
            reader.Flip(true, true); Assert.True(reader.IsFlipHorizontal);
            reader.ResetTransform(); await reader.RefreshAsync();
            var scrollType = orientation == PageFrameOrientation.Horizontal ? NScrollType.Horizontal : NScrollType.Vertical;
            for (int i = 0; i < 20 && operation.Position.Index < 4; i++)
            { await reader.ScrollToNextFrameAsync(1, new() { ScrollType = scrollType, PagesAsOne = true, Scroll = .5 }); await reader.RefreshAsync(); }
            Assert.True(operation.Position.Index > 0); Assert.InRange(reader.DisplayCount, 1, 5);
            await operation.SetBrowseModeAsync(BrowseLayoutMode.Masonry); await reader.RefreshAsync(); Assert.NotNull(reader.BrowseLayout);
            await operation.SetBrowseModeAsync(BrowseLayoutMode.Panorama); await reader.RefreshAsync(); Assert.Null(reader.BrowseLayout);
        }
        finally { reader.Dispose(); window.Close(); }
        Assert.Equal(0, factory.GetDiagnostics().Leases);
    }

    [Fact]
    public void PanoramaWindowIsBoundedAndExtendsUntilRealEnds()
    {
        Config.SetCurrent(new()); Config.Current.Book.IsPanorama = true; Config.Current.Book.Orientation = PageFrameOrientation.Vertical;
        var pages = Pages(Enumerable.Repeat((400, 600), 10_000).ToArray());
        var context = new PageFrameContext(new(), Config.Current); var selected = new PageFrameFactory(context, new BookContext(pages), new ContentSizeCalculator(context)).CreatePageFrame(new(5000, 0), 1)!;
        var window = new PageFramePanorama(pages, selected, context, new(-500, -400, 1000, 800));
        Assert.InRange(window.Frames.Count, 3, 65);
        Assert.True(window.ContentRect.Top < window.Frames.Min(f => f.Bounds.Top));
        Assert.True(window.ContentRect.Bottom > window.Frames.Max(f => f.Bounds.Bottom));
    }
    [AvaloniaTheory]
    [InlineData(PageEndAction.None)]
    [InlineData(PageEndAction.Loop)]
    public async Task PanoramaRetainsOriginalPageEndActions(PageEndAction action)
    {
        using var fixture = new Fixture(); var state = new SaveData(fixture.State); await state.LoadAsync(TestContext.Current.CancellationToken);
        await using var operation = fixture.Operation(state); using var images = new BitmapFactory(new NeeView.Backends.MagickImageDecoder());
        var viewer = new ReaderView(); viewer.Attach(operation, images); var window = new Window { Width = 800, Height = 600, Content = viewer }; window.Show();
        try
        {
            await operation.OpenAsync(fixture.Images, TestContext.Current.CancellationToken); await operation.SetBrowseModeAsync(BrowseLayoutMode.Panorama);
            await operation.SetOrientationAsync(PageFrameOrientation.Vertical); await operation.SetPageEndActionAsync(action);
            Config.Current.View.PageMoveDuration = 0; Config.Current.View.ScrollDuration = 0;
            await operation.JumpAsync(4); await viewer.RefreshAsync();
            // 原滚动先吸附内容边界，随后才执行页尾动作，不能要求第一次命令立即循环。
            for (int i = 0; i < 8 && operation.Position.Index == 4; i++)
                await viewer.ScrollToNextFrameAsync(1, new() { ScrollType = NScrollType.Vertical, PagesAsOne = false });
            Assert.Equal(action == PageEndAction.Loop ? 0 : 4, operation.Position.Index);
            await viewer.RefreshAsync(); await viewer.ZoomAsync(.65);
            Dispatcher.UIThread.RunJobs();
            using var frame = window.CaptureRenderedFrame(); Assert.NotNull(frame);
            var phase = Environment.GetEnvironmentVariable("NEEVIEW_ACCEPTANCE_PHASE") ?? "p3-panorama";
            frame.Save(Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, $"../../../../../acceptance/{phase}-panorama-layout.png")), Avalonia.Media.Imaging.PngBitmapEncoderOptions.Default);
        }
        finally { viewer.Dispose(); window.Close(); }
    }

    private static List<Page> Pages(params (int Width, int Height)[] sizes)
    {
        var source = new DummyArchive(); return sizes.Select((s, i) =>
        { var page = new Page(new(source) { Id = i, RawEntryName = $"{i}.png" }) { Index = i }; page.Content.HasSize = true; page.Content.PageDataSource = new(new(s.Width, s.Height)); return page; }).ToList();
    }
}
