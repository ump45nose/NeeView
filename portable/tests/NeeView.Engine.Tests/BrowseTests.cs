using System.Text.Json.Nodes;
using System.Collections.Concurrent;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using NeeView.Backends;
using NeeView.MacOS.Views;
namespace NeeView.Engine.Tests;

/// <summary>P3布局与正式查看器回归；复用原Page/来源/配置，不创建第二个阅读宿主。</summary>
public sealed class BrowseTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void MasonryUsesShortestColumnAndDirection(bool rtl)
    {
        var layout = new BrowseLayout([new(400, 800), new(400, 400), new(400, 200)], 630, BrowseLayoutMode.Masonry, rightToLeft: rtl);
        Assert.Equal(2, layout.ColumnCount);
        Assert.Equal(layout.Items[1].X, layout.Items[2].X); // 第二列最短，下一项继续投到该列。
        Assert.Equal(8, layout.Items[0].Y); Assert.Equal(8, layout.Items[1].Y);
        Assert.Equal(rtl, layout.Items[0].X > layout.Items[1].X);
        Assert.Equal(2, layout.HitTest(layout.Items[2].X + 1, layout.Items[2].Y + 1));
        Assert.Equal(-1, layout.HitTest(0, 0));
    }

    [Theory]
    [InlineData(BrowseLayoutMode.Continuous)]
    [InlineData(BrowseLayoutMode.Masonry)]
    public void TenThousandMixedSizesQueryMatchesActualIntersection(BrowseLayoutMode mode)
    {
        var sizes = Enumerable.Range(0, 10_000).Select(i => new NeeView.Size(400 + i % 9 * 100, i % 27 == 0 ? 10000 : 200 + i % 13 * 100)).ToArray();
        var layout = new BrowseLayout(sizes, 1200, mode, rightToLeft: true);
        for (int step = 0; step < 20; step++)
        {
            double top = layout.Height * step / 20, bottom = top + 700;
            var expected = layout.Items.Select((r, i) => (r, i)).Where(e => e.r.Bottom > top && e.r.Y < bottom).Select(e => e.i).ToArray();
            Assert.Equal(expected, layout.Query(top, bottom));
        }
        Assert.Equal(10_000, layout.Items.Count);
        if (mode == BrowseLayoutMode.Continuous) Assert.Equal(1, layout.ColumnCount);
        Assert.Empty(new BrowseLayout([], double.NaN, mode).Query(0, 100));
    }

    [Fact]
    public async Task ModeKeepsOriginalPageAndJsonAndRollsBackFailedSave()
    {
        using var fixture = new Fixture(); var state = new SaveData(fixture.State); await state.LoadAsync(TestContext.Current.CancellationToken);
        var operation = fixture.Operation(state);
        try
        {
            await operation.OpenAsync(fixture.Images, TestContext.Current.CancellationToken); await operation.JumpAsync(3);
            var book = operation.Book; var page = book!.CurrentPage; var version = book.PageOrderVersion;
            await operation.SetBrowseModeAsync(BrowseLayoutMode.Masonry); await operation.ScaleBrowseColumnsAsync(.5); await operation.SaveAsync();
            Assert.Same(book, operation.Book); Assert.Same(page, book.CurrentPage); Assert.Equal(version, book.PageOrderVersion);
            var json = JsonNode.Parse(File.ReadAllText(Path.Combine(fixture.State, "UserSetting.json")))!;
            Assert.True(json["Config"]!["Book"]!["IsPanorama"]!.GetValue<bool>());
            Assert.Equal((int)BrowseLayoutMode.Masonry, json["Config"]!["Book"]!["MacPanoramaLayout"]!.GetValue<int>());
            Assert.Equal(160, json["Config"]!["Book"]!["MacGalleryColumnWidth"]!.GetValue<double>());
            var path = Path.Combine(fixture.State, "UserSetting.json"); File.Delete(path); Directory.CreateDirectory(path);
            await Assert.ThrowsAnyAsync<IOException>(() => operation.SetBrowseModeAsync(BrowseLayoutMode.Paged));
            Assert.Equal(BrowseLayoutMode.Masonry, operation.BrowseMode); Directory.Delete(path);
            var commands = new CommandTable(operation); Assert.True(commands.IsAvailable("ToggleIsPanorama"));
            await commands.ExecuteAsync("ToggleIsPanorama"); Assert.Equal(BrowseLayoutMode.Paged, operation.BrowseMode);
            await commands.ExecuteAsync("ToggleIsPanorama"); Assert.Equal(BrowseLayoutMode.Masonry, operation.BrowseMode);
        }
        finally { await operation.DisposeAsync(); }
    }

    [Fact]
    public async Task BrowseWholePageAnchorDoesNotOverwriteOriginalDoublePageSetting()
    {
        using var fixture = new Fixture(); var state = new SaveData(fixture.State); await state.LoadAsync(TestContext.Current.CancellationToken); var operation = fixture.Operation(state);
        try
        {
            await operation.OpenAsync(fixture.Images, TestContext.Current.CancellationToken);
            await operation.ApplySettingAsync(s => s.PageMode = PageMode.WidePage); await operation.SetBrowseModeAsync(BrowseLayoutMode.Masonry);
            var page = operation.Book!.Pages[3]; await operation.ReportBrowsePositionAsync(operation.Book, page); await operation.SaveAsync();
            Assert.Same(page, operation.Book.CurrentPage); Assert.Equal(3, operation.PageSelector.SelectedIndex);
            Assert.Equal(PageMode.WidePage, operation.Book.Setting.PageMode);
            Assert.Equal(page.EntryName, state.GetLastBook()!.Page);
            await operation.ScaleBrowseColumnsAsync(double.MaxValue); Assert.Equal(1600, Config.Current.Book.MacGalleryColumnWidth);
            await operation.SetBrowseModeAsync(BrowseLayoutMode.Paged);
            Assert.Contains(operation.Frame!.Elements, e => !e.IsDummy && ReferenceEquals(e.Page, page));
            Assert.Equal(PageMode.WidePage, operation.Book.Setting.PageMode);
        }
        finally { await operation.DisposeAsync(); }
    }

    [AvaloniaFact]
    public async Task RealViewScrollSortResizeSwitchAndCloseKeepBoundedLeases()
    {
        using var fixture = new Fixture();
        for (int i = 6; i <= 120; i++) File.Copy(Path.Combine(fixture.Images, "001.png"), Path.Combine(fixture.Images, $"{i:000}.png"));
        var state = new SaveData(fixture.State); await state.LoadAsync(TestContext.Current.CancellationToken);
        var operation = fixture.Operation(state); var factory = new BitmapFactory(new MagickImageDecoder()) { Budget = 1, ThumbnailBudget = 1 };
        var window = new MainWindow(); window.Bind(new(operation, new CommandTable(operation), state), factory, new NoPlatform()); window.Show();
        try
        {
            await window.OpenAsync(fixture.Images); await operation.SetBrowseModeAsync(BrowseLayoutMode.Masonry); await window.Viewer.RefreshAsync();
            await WaitAsync(() => window.Viewer.DisplayCount > 0 && window.Viewer.BrowsePendingCount == 0);
            Assert.True(window.Viewer.BrowseLayout!.ColumnCount >= 2); Assert.InRange(window.Viewer.DisplayCount, 1, 24);
            Assert.True(factory.GetDiagnostics().ThumbnailBytes > 0);
            window.Viewer.Pan(new(0, -3000)); await WaitAsync(() => operation.Position.Index > 0 && window.Viewer.BrowsePendingCount == 0);
            var page = operation.Book!.CurrentPage;
            Assert.Null(window.Viewer.BrowseSelection); // 滚动锚点不能成为文件操作的显式选择。
            var pressPoint = window.Viewer.TranslatePoint(new(100, 150), window)!.Value;
            window.MouseDown(pressPoint, MouseButton.Left);
            var anchorOffset = window.Viewer.BrowseOffset;
            await operation.SetBrowseModeAsync(BrowseLayoutMode.Paged); await window.Viewer.RefreshAsync();
            await operation.SetBrowseModeAsync(BrowseLayoutMode.Masonry); await window.Viewer.RefreshAsync();
            window.MouseMove(pressPoint - new Avalonia.Vector(0, 100)); window.MouseUp(pressPoint - new Avalonia.Vector(0, 100), MouseButton.Left);
            Assert.Equal(anchorOffset, window.Viewer.BrowseOffset, 3); // 模式切换取消旧拖动，返回后不能沿用旧按下位置。
            await operation.ApplySettingAsync(s => s.SortMode = PageSortMode.FileNameDescending); await window.Viewer.RefreshAsync();
            Assert.Same(page, operation.Book.CurrentPage);
            Assert.Contains(page!.Index, window.Viewer.BrowseLayout!.Query(window.Viewer.BrowseOffset, window.Viewer.BrowseOffset + window.Viewer.Bounds.Height));
            window.Width = 1450; window.UpdateLayout(); await window.Viewer.RefreshAsync();
            Assert.Contains(page.Index, window.Viewer.BrowseLayout.Query(window.Viewer.BrowseOffset, window.Viewer.BrowseOffset + window.Viewer.Bounds.Height));
            await WaitAsync(() => window.Viewer.BrowsePendingCount == 0);
            using (var frame = window.CaptureRenderedFrame()) frame!.Save(AcceptancePath("masonry-layout.png"), Avalonia.Media.Imaging.PngBitmapEncoderOptions.Default);
            await operation.SetBrowseModeAsync(BrowseLayoutMode.Continuous); await window.Viewer.RefreshAsync(); Assert.Equal(1, window.Viewer.BrowseLayout.ColumnCount);
            await WaitAsync(() => window.Viewer.DisplayCount > 0 && window.Viewer.BrowsePendingCount == 0);
            Assert.False(window.IsCommandAvailable("ViewRotateLeft")); Assert.False(window.IsCommandAvailable("SetStretchModeNone"));
            await operation.SetBrowseModeAsync(BrowseLayoutMode.Paged); await window.Viewer.RefreshAsync(); Assert.Same(page, operation.Book.CurrentPage);
            await WaitAsync(() => window.Viewer.DisplayCount > 0);
            Assert.InRange(window.Viewer.DisplayCount, 1, 2);
            await operation.SetBrowseModeAsync(BrowseLayoutMode.Masonry); await window.Viewer.RefreshAsync();
            await window.OpenAsync(fixture.Zip); await WaitAsync(() => window.Viewer.DisplayCount > 0 && window.Viewer.BrowsePendingCount == 0);
            Assert.Equal(5, window.Viewer.BrowseLayout!.Items.Count);
        }
        finally { await window.PrepareShutdownAsync(); window.Close(); }
        Assert.Equal(0, window.Viewer.DisplayCount); Assert.Equal(0, factory.GetDiagnostics().Leases); Assert.Equal(0, factory.ByteCount);
    }

    [AvaloniaFact]
    public async Task MouseSelectionDoubleClickAndModifiedWheelUseOneReader()
    {
        using var fixture = new Fixture(); var state = new SaveData(fixture.State); await state.LoadAsync(TestContext.Current.CancellationToken);
        var operation = fixture.Operation(state); var window = new MainWindow();
        window.Bind(new(operation, new CommandTable(operation), state), new BitmapFactory(new MagickImageDecoder()), new NoPlatform()); window.Show();
        try
        {
            await window.OpenAsync(fixture.Images); await operation.SetBrowseModeAsync(BrowseLayoutMode.Masonry); await window.Viewer.RefreshAsync();
            await WaitAsync(() => window.Viewer.DisplayCount > 0 && window.Viewer.BrowsePendingCount == 0);
            var rect = window.Viewer.BrowseLayout!.Items[1]; var point = window.Viewer.TranslatePoint(new(rect.X + 15, rect.Y + 40), window)!.Value;
            window.MouseDown(point, MouseButton.Left); window.MouseUp(point, MouseButton.Left);
            await WaitAsync(() => window.Viewer.BrowseSelection?.Index == 1); Assert.Equal(1, operation.Position.Index);
            state.SetShortcut("NextOnePage", "Ctrl+WheelDown"); window.MouseWheel(point, new(0, -1), RawInputModifiers.Control);
            await WaitAsync(() => operation.Position.Index == 2); // 修饰滚轮优先保留命令，不被瀑布流吞掉。
            rect = window.Viewer.BrowseLayout.Items[2]; point = window.Viewer.TranslatePoint(new(rect.X + 15, rect.Y + 40 - window.Viewer.BrowseOffset), window)!.Value;
            window.MouseDown(point, MouseButton.Left); window.MouseUp(point, MouseButton.Left); window.MouseDown(point, MouseButton.Left); window.MouseUp(point, MouseButton.Left);
            await WaitAsync(() => operation.BrowseMode == BrowseLayoutMode.Paged); Assert.Equal(2, operation.Position.Index);
        }
        finally { await window.PrepareShutdownAsync(); window.Close(); }
    }

    [AvaloniaFact]
    public async Task LateNativeBrowseResultsCannotPopulateNewBookOrSurviveClose()
    {
        using var fixture = new Fixture(); var state = new SaveData(fixture.State); await state.LoadAsync(TestContext.Current.CancellationToken);
        var single = Path.Combine(fixture.Root, "single"); Directory.CreateDirectory(single); File.Copy(Path.Combine(fixture.Images, "001.png"), Path.Combine(single, "only.png"));
        var decoder = new LateDecoder(); using var factory = new BitmapFactory(decoder); var operation = fixture.Operation(state);
        var reader = new ReaderView(); reader.Attach(operation, factory); var window = new Window { Width = 800, Height = 600, Content = reader }; window.Show();
        try
        {
            await operation.OpenAsync(fixture.Images, TestContext.Current.CancellationToken); await operation.SetBrowseModeAsync(BrowseLayoutMode.Masonry); await reader.RefreshAsync();
            await decoder.Started.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
            await operation.OpenAsync(single, TestContext.Current.CancellationToken); await reader.RefreshAsync(); decoder.Release.TrySetResult();
            await WaitAsync(() => reader.BrowsePendingCount == 0 && reader.DisplayCount == 1);
            Assert.Single(reader.BrowseLayout!.Items); Assert.Equal(1, factory.GetDiagnostics().Leases);
            await operation.SetBrowseModeAsync(BrowseLayoutMode.Continuous); await reader.RefreshAsync(); await reader.ZoomAsync(2);
            Assert.True(reader.BrowseLayout.Width > reader.Bounds.Width); reader.Pan(new(-200, -100));
            reader.Dispose(); factory.Dispose(); await WaitAsync(() => factory.GetDiagnostics().PendingRequests == 0);
            Assert.Equal(0, reader.DisplayCount); Assert.Equal(0, factory.GetDiagnostics().Leases);
            await WaitAsync(() => decoder.Images.All(pixels => pixels.Pixels.Length == 0));
            Assert.All(decoder.Images, pixels => Assert.Empty(pixels.Pixels));
        }
        finally { decoder.Release.TrySetResult(); reader.Dispose(); await operation.DisposeAsync(); window.Close(); }
    }

    /// <summary>原生解码故意忽略取消，核对晚到所有权；尺寸仍由真实Magick探测。</summary>
    private sealed class LateDecoder : IImageDecoder
    {
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public ConcurrentBag<DecodedImageLease> Images { get; } = [];
        public Task<ImageInfo> ProbeAsync(Stream stream, CancellationToken token) => new MagickImageDecoder().ProbeAsync(stream, token);
        public async Task<DecodedImageLease> DecodeAsync(Stream stream, DecodeRequest request, CancellationToken token)
        { Started.TrySetResult(); await Release.Task; var image = new DecodedImageLease(new(8, 8), new byte[256]); Images.Add(image); return image; }
    }

    /// <summary>正式XAML离线截图只包含自建色块，不包含用户照片。</summary>
    private static string AcceptancePath(string suffix) => Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, $"../../../../../acceptance/{Environment.GetEnvironmentVariable("NEEVIEW_ACCEPTANCE_PHASE") ?? "p3-browse"}-{suffix}"));
    private static async Task WaitAsync(Func<bool> ready)
    {
        for (int i = 0; i < 600 && !ready(); i++) { Dispatcher.UIThread.RunJobs(); await Task.Delay(10, TestContext.Current.CancellationToken); }
        Assert.True(ready());
    }
    private sealed class NoPlatform : IPlatformService
    { public Task RevealAsync(string path, CancellationToken token = default) => throw new NotSupportedException(); public Task TrashAsync(string path, CancellationToken token = default) => throw new NotSupportedException(); }
}
