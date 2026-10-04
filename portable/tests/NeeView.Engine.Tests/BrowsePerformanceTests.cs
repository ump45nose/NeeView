using System.Diagnostics;
using System.Text.Json;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using NeeView.Backends;
using NeeView.MacOS.Views;
namespace NeeView.Engine.Tests;

/// <summary>固定万项元数据及几何测量；不读取用户原图，不把CPU测量当作屏幕帧率。</summary>
public sealed class BrowsePerformanceTests
{
    [Fact]
    public async Task TenThousandDirectoryIndexAndRepeatedLayoutAreMeasured()
    {
        using var fixture = new Fixture();
        var directory = Path.Combine(fixture.Root, "index"); Directory.CreateDirectory(directory);
        // 只测真实文件系统元数据；空PNG从未进入探测/解码链。
        for (int i = 9999; i >= 0; i--) File.WriteAllBytes(Path.Combine(directory, $"page-{i}.png"), []);
        var token = TestContext.Current.CancellationToken;
        var factory = new ArchiveFactory(); var clock = Stopwatch.StartNew();
        var source = await factory.OpenAsync(directory, token);
        await using var collection = new ArchiveEntryCollection(source, factory, false);
        var entries = await collection.GetEntriesAsync(token); var enumerateMs = clock.Elapsed.TotalMilliseconds;
        Assert.Equal(10_000, entries.Count);
        clock.Restart();
        // 独立来源使本段只测过滤/Page创建，不重复统计本地枚举。
        var metadata = new MetadataArchive(entries.Select(e => e.ArchiveEntry).ToArray());
        await using var metadataCollection = new ArchiveEntryCollection(metadata, factory, false);
        var pages = await BookSourceFactory.CreatePageCollectionAsync(metadataCollection, BookPageCollectMode.Image, factory, token);
        var createMs = clock.Elapsed.TotalMilliseconds;
        var identities = pages.ToHashSet(); var book = new Book(source, pages, new());
        clock.Restart(); await Task.Run(() => book.Sort(token), token); var sortMs = clock.Elapsed.TotalMilliseconds;
        Assert.Equal("page-0.png", book.Pages[0].EntryName); Assert.Equal("page-9999.png", book.Pages[^1].EntryName);
        Assert.All(book.Pages, p => Assert.Contains(p, identities));

        var sizes = Enumerable.Range(0, 10_000).Select(i => new Size(400 + i % 9 * 100, 200 + i % 13 * 100)).ToArray();
        // 热身后重复尺寸补齐，同一数据集/宽度用于前后测量。
        _ = new BrowseLayout(sizes, 1200, BrowseLayoutMode.Masonry, rightToLeft: true);
        var full = new List<object>();
        var incremental = new List<object>();
        var current = new BrowseLayout(sizes, 1200, BrowseLayoutMode.Masonry, rightToLeft: true);
        for (int i = 0; i < 20; i++)
        {
            sizes[9000 + i] = new(1600, 700 + i * 100);
            long before = GC.GetAllocatedBytesForCurrentThread(); clock.Restart();
            var layout = new BrowseLayout(sizes, 1200, BrowseLayoutMode.Masonry, rightToLeft: true);
            var milliseconds = clock.Elapsed.TotalMilliseconds; long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
            Assert.Equal(10_000, layout.Items.Count);
            Assert.NotEmpty(layout.Query(layout.Height * .5, layout.Height * .5 + 700));
            full.Add(new { Milliseconds = milliseconds, AllocatedBytes = allocated });
            before = GC.GetAllocatedBytesForCurrentThread(); clock.Restart();
            current = current.WithSizes(new Dictionary<int, Size> { [9000 + i] = sizes[9000 + i] }, token);
            milliseconds = clock.Elapsed.TotalMilliseconds; allocated = GC.GetAllocatedBytesForCurrentThread() - before;
            Assert.Equal(layout.Items, current.Items);
            Assert.InRange(current.RecomputedItemCount, 1, 1040);
            incremental.Add(new { Milliseconds = milliseconds, AllocatedBytes = allocated, current.RecomputedItemCount, current.ReusedBlockCount });
        }
        WriteReport("index-layout", new { Scope = "10000真实本地空文件元数据/原Page创建/原排序/纯几何；无解码/屏幕P95/NAS外推",
            Pages = pages.Count, EnumerateMs = enumerateMs, CreatePagesMs = createMs, SortMs = sortMs, FullLayoutSamples = full, IncrementalLayoutSamples = incremental });
    }

    [AvaloniaFact]
    public async Task TenThousandPagesReflowResizeSwitchAndCloseRejectStaleLayouts()
    {
        using var fixture = new Fixture(); var state = new SaveData(fixture.State); await state.LoadAsync(TestContext.Current.CancellationToken);
        var sources = new SyntheticFactory(); var decoder = new SyntheticDecoder();
        var operation = new BookOperation(sources, decoder, state); using var factory = new BitmapFactory(decoder) { Budget = 1, ThumbnailBudget = 1 };
        var reader = new ReaderView(); reader.Attach(operation, factory);
        var window = new Window { Width = 1200, Height = 700, Content = reader }; window.Show();
        try
        {
            await operation.OpenAsync(Path.Combine(fixture.Root, "large"), TestContext.Current.CancellationToken);
            Assert.False(sources.EnumeratedOnUiThread);
            await operation.SetBrowseModeAsync(BrowseLayoutMode.Masonry);
            var starting = Stopwatch.StartNew(); var refresh = reader.RefreshAsync();
            // 启动后必须让出UI，而不是在Refresh调用中同步计算万项矩形。
            var enqueueMs = starting.Elapsed.TotalMilliseconds;
            reader.Navigate(new(0, .9)); // 首次后台布局未发布时的导航不能丢失。
            await refresh; await WaitAsync(() => reader.BrowsePendingCount == 0);
            Assert.Equal(10_000, reader.BrowseLayout!.Items.Count); Assert.InRange(reader.DisplayCount, 1, 32);
            Assert.True(reader.BrowseOffset > reader.BrowseLayout.Height * .8);
            reader.Navigate(new(0, .9)); await WaitAsync(() => reader.BrowsePendingCount == 0);
            Assert.InRange(factory.GetDiagnostics().Leases, 1, 32);

            window.Width = 1600; window.UpdateLayout(); var old = reader.RefreshAsync();
            await operation.SetBrowseModeAsync(BrowseLayoutMode.Continuous); await reader.ZoomAsync(2); await reader.RefreshAsync(); await old;
            Assert.Equal(1, reader.BrowseLayout!.ColumnCount); Assert.True(reader.BrowseLayout.Width > reader.Bounds.Width);
            // 排序发布新PageOrderVersion，同一Page身份应保留，原显示快照不能混用新下标。
            var page = operation.Book!.CurrentPage; await operation.ApplySettingAsync(s => s.SortMode = PageSortMode.FileNameDescending); await reader.RefreshAsync();
            Assert.Same(page, operation.Book.CurrentPage);
            Assert.Contains(page!.Index, reader.BrowseLayout.Query(reader.BrowseOffset, reader.BrowseOffset + reader.Bounds.Height));

            await operation.OpenAsync(Path.Combine(fixture.Root, "small"), TestContext.Current.CancellationToken); await reader.RefreshAsync();
            Assert.Single(reader.BrowseLayout.Items);
            WriteReport("viewer", new { Scope = "10000原Page/正式ReaderView Headless；合成解码，仅测请求提交耗时及生命周期，不代表屏幕帧耗时",
                EnqueueMs = enqueueMs, Publications = reader.BrowseLayoutPublications, FinalPageCount = reader.BrowseLayout.Items.Count });
            window.Width = 1100; window.UpdateLayout(); var closing = reader.RefreshAsync();
            var publicationsBeforeClose = reader.BrowseLayoutPublications; reader.Dispose(); await closing;
            Assert.Equal(publicationsBeforeClose, reader.BrowseLayoutPublications);
            Assert.Null(reader.BrowseLayout); Assert.False(reader.BrowseLayoutPending); Assert.Equal(0, reader.DisplayCount);
        }
        finally { reader.Dispose(); await operation.DisposeAsync(); window.Close(); }
        factory.Dispose(); await WaitAsync(() => factory.GetDiagnostics().PendingRequests == 0);
        Assert.Equal(0, factory.GetDiagnostics().Leases); Assert.Equal(0, factory.ByteCount);
    }

    [AvaloniaFact]
    public async Task ExternalSizeCompletionKeepsContinuousPageAndFraction()
    {
        using var fixture = new Fixture(); var state = new SaveData(fixture.State); await state.LoadAsync(TestContext.Current.CancellationToken);
        var operation = new BookOperation(new SyntheticFactory(), new SyntheticDecoder(), state); using var factory = new BitmapFactory(new SyntheticDecoder());
        var reader = new ReaderView(); reader.Attach(operation, factory); var window = new Window { Width = 800, Height = 600, Content = reader }; window.Show();
        try
        {
            await operation.OpenAsync(Path.Combine(fixture.Root, "large"), TestContext.Current.CancellationToken); await operation.SetBrowseModeAsync(BrowseLayoutMode.Continuous); await reader.RefreshAsync();
            await WaitAsync(() => reader.BrowsePendingCount == 0);
            var rect = reader.BrowseLayout!.Items[40]; reader.Pan(new(0, -(rect.Y + rect.Height * .3 - reader.BrowseOffset)));
            await WaitAsync(() => reader.BrowsePendingCount == 0);
            int index = reader.BrowseLayout.Query(reader.BrowseOffset, reader.BrowseOffset + reader.Bounds.Height)[0];
            var anchor = operation.Book!.Pages[index]; rect = reader.BrowseLayout.Items[index]; double fraction = (reader.BrowseOffset - rect.Y) / rect.Height;
            // 模拟其他消费者或已经取消的探测完成；HasSize已为true仍需补齐布局。
            anchor.Content.HasSize = true; anchor.Content.PageDataSource = new(new(400, 1800));
            await reader.RefreshAsync();
            await WaitAsync(() => reader.BrowsePendingCount == 0 && reader.BrowseLayout.GetPageSize(index) == new Size(400, 1800));
            rect = reader.BrowseLayout.Items[index];
            Assert.Equal(fraction, (reader.BrowseOffset - rect.Y) / rect.Height, 6);
            Assert.Same(anchor, operation.Book.Pages[index]);
        }
        finally { reader.Dispose(); await operation.DisposeAsync(); window.Close(); }
    }

    /// <summary>匿名数值写入当前阶段；自建文件由Fixture清理。</summary>
    internal static void WriteReport(string suffix, object report)
    {
        var phase = Environment.GetEnvironmentVariable("NEEVIEW_ACCEPTANCE_PHASE") ?? "p3-performance";
        var path = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, $"../../../../../acceptance/{phase}-{suffix}.json"));
        File.WriteAllText(path, JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }) + "\n");
    }

    /// <summary>保留实际ArchiveEntry及元数据，隔离创建和枚举耗时。</summary>
    private sealed class MetadataArchive(IReadOnlyList<ArchiveEntry> entries) : Archive("metadata")
    {
        public override bool IsDirectory => true;
        public override Task<IReadOnlyList<ArchiveEntry>> GetEntriesAsync(CancellationToken token) { token.ThrowIfCancellationRequested(); return Task.FromResult(entries); }
        public override Task<Stream> OpenEntryAsync(ArchiveEntry entry, CancellationToken token) => throw new NotSupportedException();
        public override ValueTask DisposeAsync() { IsDisposed = true; return ValueTask.CompletedTask; }
    }

    private static async Task WaitAsync(Func<bool> ready)
    {
        for (int i = 0; i < 1000 && !ready(); i++) { Dispatcher.UIThread.RunJobs(); await Task.Delay(10, TestContext.Current.CancellationToken); }
        Assert.True(ready());
    }
    /// <summary>元数据夹具只构造原来源/Page，测试不维护新的运行宿主。</summary>
    private sealed class SyntheticFactory : IArchiveFactory
    {
        public bool EnumeratedOnUiThread { get; private set; }
        public Task<Archive> OpenAsync(string path, CancellationToken token) => Task.FromResult<Archive>(new SyntheticArchive(path, path.EndsWith("small") ? 1 : 10_000, () => EnumeratedOnUiThread |= Dispatcher.UIThread.CheckAccess()));
        public Task<IReadOnlyList<FolderItem>> ListFoldersAsync(string path, CancellationToken token) => Task.FromResult<IReadOnlyList<FolderItem>>([]);
        public Task<IReadOnlyList<FolderItem>> ListBooksAsync(string path, CancellationToken token) => Task.FromResult<IReadOnlyList<FolderItem>>([]);
    }
    private sealed class SyntheticArchive(string path, int count, Action enumerated) : Archive(path)
    {
        public override bool IsDirectory => true;
        public override Task<IReadOnlyList<ArchiveEntry>> GetEntriesAsync(CancellationToken token)
        {
            enumerated(); token.ThrowIfCancellationRequested();
            return Task.FromResult<IReadOnlyList<ArchiveEntry>>(Enumerable.Range(0, count).Select(i => new ArchiveEntry(this) { Id = i, RawEntryName = $"{i:00000}.png" }).ToArray());
        }
        public override Task<Stream> OpenEntryAsync(ArchiveEntry entry, CancellationToken token) => Task.FromResult<Stream>(new MemoryStream());
        public override ValueTask DisposeAsync() { IsDisposed = true; return ValueTask.CompletedTask; }
    }
    private sealed class SyntheticDecoder : IImageDecoder
    {
        public Task<ImageInfo> ProbeAsync(Stream stream, CancellationToken token) => Task.FromResult(new ImageInfo(new(400, 600), "synthetic"));
        public Task<DecodedImageLease> DecodeAsync(Stream stream, DecodeRequest request, CancellationToken token) => Task.FromResult(new DecodedImageLease(new(8, 8), new byte[256]));
    }
}
