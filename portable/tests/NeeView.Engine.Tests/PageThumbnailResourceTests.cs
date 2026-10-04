using System.Diagnostics;
using System.Text.Json;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using NeeView.Backends;
using NeeView.MacOS.Views;
using NeeView.MacOS.ViewModels;
namespace NeeView.Engine.Tests;

/// <summary>用户指定目录只读采样：正式页面模板、原Page/BitmapFactory，不启动前台应用。</summary>
public sealed class PageThumbnailResourceTests
{
    public static bool HasResourceDirectory => Directory.Exists(Environment.GetEnvironmentVariable("NEEVIEW_P3_RESOURCE_ROOT"));

    /// <summary>三个子目录、三段缩略定位及切书/隐藏，实图仅存本机忽略目录，报告不含私人名称。</summary>
    [AvaloniaFact(SkipUnless = nameof(HasResourceDirectory), Skip = "需显式提供只读图片资源目录")]
    public async Task ResourceSamplesShowThumbnailsScrollAndCloseWithBoundedResources()
    {
        var root = Environment.GetEnvironmentVariable("NEEVIEW_P3_RESOURCE_ROOT")!;
        var artifact = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../artifacts/p3-resource-thumbnails")); Directory.CreateDirectory(artifact);
        var folders = await Task.Run(() => Directory.EnumerateDirectories(root).Order(StringComparer.Ordinal)
            .Where(p => Directory.EnumerateFiles(p).Any(IsImage)).Take(3).ToArray(), TestContext.Current.CancellationToken);
        Assert.Equal(3, folders.Length);
        using var fixture = new Fixture(); var state = new SaveData(fixture.State); await state.LoadAsync(TestContext.Current.CancellationToken);
        var operation = fixture.Operation(state); var images = new BitmapFactory(new MagickImageDecoder()); var model = new ReaderWorkspaceViewModel(operation, new CommandTable(operation), state);
        var window = new MainWindow { Width = 1440, Height = 900 }; window.Bind(model, images, new NoPlatform()); window.Show(); var measurements = new List<object>();
        try
        {
            await window.SetPageListStyleAsync(PanelListItemStyle.Thumbnail);
            for (int sample = 0; sample < folders.Length; sample++)
            {
                await window.OpenAsync(folders[sample]); model.ShowPanel("PageListPanel"); await PageListThumbnailTests.SettleAsync(window);
                var list = window.FindControl<ListBox>("PageList")!; var book = operation.Book!;
                foreach (double position in new[] { 0d, .5, .95 })
                {
                    int index = (int)Math.Floor((book.Pages.Count - 1) * position); var page = book.Pages[index]; var current = book.CurrentPage;
                    var clock = Stopwatch.StartNew(); list.SelectedItem = page; list.ScrollIntoView(page); await PageListThumbnailTests.SettleAsync(window);
                    var covers = list.GetVisualDescendants().OfType<ListCoverImage>().Where(c => c.HasImage).ToArray(); Assert.NotEmpty(covers); Assert.InRange(covers.Length, 1, 16);
                    Assert.Contains(covers, c => ReferenceEquals(c.PageSource, page)); Assert.Same(current, book.CurrentPage);
                    var panel = Assert.Single(list.GetVisualDescendants().OfType<VirtualizingThumbnailPanel>()); Assert.InRange(panel.RealizedCount, 1, 24);
                    var diagnostics = images.GetDiagnostics(); Assert.True(diagnostics.ThumbnailBytes < images.ThumbnailBudget + 4 * 1024 * 1024);
                    using var frame = window.CaptureRenderedFrame(); Assert.NotNull(frame);
                    int distinctSamples = ReadRenderedCoverSamples(frame, covers.First(c => ReferenceEquals(c.PageSource, page)), window);
                    Assert.True(distinctSamples >= 6, "真实缩略绘制区域必须含多种图片像素，不能仅持有Bitmap或显示纯色占位");
                    measurements.Add(new { Sample = sample + 1, Position = position, Pages = book.Pages.Count, Index = index, Shown = covers.Length, panel.RealizedCount,
                        diagnostics.ThumbnailBytes, diagnostics.DisplayBytes, diagnostics.Leases, DistinctRenderedSamples = distinctSamples, SettleMs = clock.Elapsed.TotalMilliseconds });
                    if (position == .5) frame.Save(Path.Combine(artifact, $"sample-{sample + 1}.png"), Avalonia.Media.Imaging.PngBitmapEncoderOptions.Default);
                }
                await window.SetPageListStyleAsync(PanelListItemStyle.Normal); await PageListThumbnailTests.SettleAsync(window);
                Assert.All(list.GetVisualDescendants().OfType<ListCoverImage>(), c => Assert.False(c.HasImage));
                await window.SetPageListStyleAsync(PanelListItemStyle.Thumbnail); await PageListThumbnailTests.SettleAsync(window);
                // 正式目录树只沿该样本父链展开；不递归扫描全部资源。
                await window.SetFolderTreeVisibleAsync(true); var node = await operation.Bookshelf.FolderTree.SyncDirectoryAsync(folders[sample], true, TestContext.Current.CancellationToken);
                Assert.NotNull(node); Assert.Same(book, operation.Book); Assert.True(node.IsDelayCreation);
                await window.SetFolderTreeVisibleAsync(false);
            }
        }
        finally { await window.PrepareShutdownAsync(); window.Close(); }
        Assert.Equal(0, images.GetDiagnostics().Leases); Assert.Equal(0, images.ByteCount);
        await File.WriteAllTextAsync(Path.Combine(artifact, "report.json"), JsonSerializer.Serialize(new { Utc = DateTimeOffset.UtcNow,
            Scope = "正式XAML/原Page/真实后端只读Headless采样，不代表正式Mac运行、Retina或P95性能", Measurements = measurements,
            ClosedLeases = images.GetDiagnostics().Leases, ClosedBytes = images.ByteCount }, new JsonSerializerOptions { WriteIndented = true }), TestContext.Current.CancellationToken);
    }
    /// <summary>从实际完整帧的缩略区域取5×5像素样本，拒绝纯色/空白；不保存或输出私人像素。</summary>
    /// <param name="frame">正式XAML捕获的已绘制帧。</param>
    /// <param name="cover">实际可见的目标缩略控件。</param>
    /// <param name="window">当前Headless宿主，用于坐标和显示缩放。</param>
    /// <returns>采样得到的不同像素数；是绘制存在检查，不是原图色彩准确性验收。</returns>
    private static int ReadRenderedCoverSamples(Avalonia.Media.Imaging.Bitmap frame, ListCoverImage cover, Window window)
    {
        var start = cover.TranslatePoint(new(0, 0), window)!.Value; var scale = window.RenderScaling;
        var rect = new PixelRect((int)(start.X * scale), (int)(start.Y * scale), (int)(cover.Bounds.Width * scale), (int)(cover.Bounds.Height * scale));
        int stride = rect.Width * 4; var pixels = new byte[stride * rect.Height]; var pin = GCHandle.Alloc(pixels, GCHandleType.Pinned);
        try { frame.CopyPixels(rect, pin.AddrOfPinnedObject(), pixels.Length, stride); } finally { pin.Free(); }
        var samples = new HashSet<int>();
        for (int y = 1; y <= 5; y++) for (int x = 1; x <= 5; x++)
        { int offset = (rect.Height * y / 6) * stride + (rect.Width * x / 6) * 4; samples.Add(BitConverter.ToInt32(pixels, offset)); }
        return samples.Count;
    }
    private static bool IsImage(string path) => Path.GetExtension(path).ToLowerInvariant() is ".jpg" or ".jpeg" or ".png" or ".webp" or ".bmp" or ".tif" or ".tiff" or ".gif";
    private sealed class NoPlatform : IPlatformService
    { public Task RevealAsync(string path, CancellationToken token = default) => throw new NotSupportedException(); public Task TrashAsync(string path, CancellationToken token = default) => throw new NotSupportedException(); }
}
