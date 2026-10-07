using System.Text.Json.Nodes;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using NeeView.Backends;
using NeeView.MacOS.Views;
using NeeView.MacOS.ViewModels;
namespace NeeView.Engine.Tests;

public sealed class ListTemplateTests
{
    /// <summary>原差分Profile从各自默认构造合并；未迁字段和三个列表字段仍写回原JSON分支。</summary>
    [Fact]
    public async Task OriginalProfilesAndPartialJsonPreserveDefaultsAndUnknownFields()
    {
        using var fixture = new Fixture(); Directory.CreateDirectory(fixture.State);
        await File.WriteAllTextAsync(Path.Combine(fixture.State, "UserSetting.json"), """{"Config":{"Panels":{"ThumbnailItemProfile":{"ImageWidth":256,"Future":17},"ContentItemProfile":{"IsImagePopupEnabled":false}},"Bookshelf":{"PanelListItemStyle":0},"History":{"PanelListItemStyle":2},"Bookmark":{"PanelListItemStyle":3}}}""", TestContext.Current.CancellationToken);
        var state = new SaveData(fixture.State); await state.LoadAsync(TestContext.Current.CancellationToken);
        Assert.Equal(PanelListItemStyle.Normal, Config.Current.Bookshelf.PanelListItemStyle);
        Assert.Equal(PanelListItemStyle.Banner, Config.Current.History.PanelListItemStyle);
        Assert.Equal(PanelListItemStyle.Thumbnail, Config.Current.Bookmark.PanelListItemStyle);
        Assert.Equal(64, Config.Current.Panels.ContentItemProfile.ImageWidth); Assert.False(Config.Current.Panels.ContentItemProfile.IsImagePopupEnabled);
        Assert.Equal(PanelListItemImageShape.Original, Config.Current.Panels.ThumbnailItemProfile.ImageShape);
        Assert.True(Config.Current.Panels.ThumbnailItemProfile.IsTextVisible); Assert.True(Config.Current.Panels.ThumbnailItemProfile.IsTextWrapped);
        Assert.Equal(256, Config.Current.Panels.ThumbnailItemProfile.ImageWidth); Assert.Equal(50, Config.Current.Panels.BannerItemProfile.ShapeHeight);
        await state.SaveAsync(null, TestContext.Current.CancellationToken);
        var json = JsonNode.Parse(await File.ReadAllTextAsync(Path.Combine(fixture.State, "UserSetting.json"), TestContext.Current.CancellationToken))!;
        Assert.Equal(17, json["Config"]!["Panels"]!["ThumbnailItemProfile"]!["Future"]!.GetValue<int>());
        Assert.Null(json["Config"]!["FolderList"]);
    }
    /// <summary>稳定路径缓存关闭请求来源后仍可共享；显式刷新不释放显示中的旧像素，并继续计入预算。</summary>
    [Fact]
    public async Task PathCoverSharesCacheClosesSourcesAndRefreshRetainsLiveLease()
    {
        using var fixture = new Fixture(); var state = new SaveData(fixture.State); await state.LoadAsync(TestContext.Current.CancellationToken);
        var archives = new CountingArchives(); using var cache = new BitmapFactory(new MagickImageDecoder());
        using var first = await cache.GetCoverAsync(fixture.Zip, archives, state.FolderConfigs, new(64, 64, true), TestContext.Current.CancellationToken);
        using var second = await cache.GetCoverAsync(fixture.Zip, archives, state.FolderConfigs, new(64, 64, true), TestContext.Current.CancellationToken);
        Assert.Same(first.Image, second.Image); Assert.Equal(1, archives.Opens); Assert.Equal(1, archives.Closes);
        var bytes = first.Image.ByteCount; cache.InvalidateCovers(); Assert.Equal(bytes, cache.ByteCount); Assert.NotEmpty(first.Image.Pixels);
        using var refreshed = await cache.GetCoverAsync(fixture.Zip, archives, state.FolderConfigs, new(64, 64, true), TestContext.Current.CancellationToken);
        Assert.NotSame(first.Image, refreshed.Image); Assert.Equal(2, archives.Opens); Assert.Equal(2, archives.Closes);
        Assert.Equal(bytes + refreshed.Image.ByteCount, cache.ByteCount);
        first.Dispose(); second.Dispose(); Assert.Empty(first.Image.Pixels); Assert.Equal(refreshed.Image.ByteCount, cache.ByteCount);
    }
    /// <summary>显式单图、归档内部定位及原Foldres指定封面不被自然首图覆盖。</summary>
    [Fact]
    public async Task CoversUseExactPictureAndOriginalFolderOverride()
    {
        using var fixture = new Fixture(); var state = new SaveData(fixture.State); await state.LoadAsync(TestContext.Current.CancellationToken);
        var archives = new ArchiveFactory();
        await using (var image = await ArchivePageUtility.GetSelectedPageAsync(Path.Combine(fixture.Images, "005.png"), archives, state.FolderConfigs, TestContext.Current.CancellationToken)) Assert.Equal("005.png", image.Entry!.EntryName);
        await using (var image = await ArchivePageUtility.GetSelectedPageAsync(Path.Combine(fixture.Zip, "004.png"), archives, state.FolderConfigs, TestContext.Current.CancellationToken)) Assert.Equal("004.png", image.Entry!.EntryName);
        state.FolderConfigs.Restore(new JsonObject { ["Folders"] = new JsonArray(new JsonObject { ["Place"] = fixture.Root, ["Thumbs"] = new JsonObject { [Path.GetFileName(fixture.Images)] = "005.png" } }) });
        await using (var image = await ArchivePageUtility.GetSelectedPageAsync(fixture.Images, archives, state.FolderConfigs, TestContext.Current.CancellationToken)) Assert.Equal("005.png", image.Entry!.EntryName);
        Directory.CreateDirectory(Path.Combine(fixture.Root, "空书"));
        await using var empty = await ArchivePageUtility.GetSelectedPageAsync(Path.Combine(fixture.Root, "空书"), archives, state.FolderConfigs, TestContext.Current.CancellationToken); Assert.Null(empty.Entry);
        state.FolderConfigs.Restore(new JsonObject { ["Folders"] = new JsonArray(new JsonObject { ["Place"] = fixture.Root, ["Thumbs"] = new JsonObject { [Path.GetFileName(fixture.Zip)] = "005.png" } }) });
        await using var zipCover = await ArchivePageUtility.GetSelectedPageAsync(fixture.Zip, archives, state.FolderConfigs, TestContext.Current.CancellationToken); Assert.Equal("005.png", zipCover.Entry!.EntryName);
    }
    /// <summary>刷新期间原生解码不可中断，旧结果只释放，后续相同路径独立重提且来源均关闭。</summary>
    [Fact]
    public async Task NativeCoverArrivingAfterRefreshCannotReplaceNewDemand()
    {
        using var fixture = new Fixture(); var state = new SaveData(fixture.State); await state.LoadAsync(TestContext.Current.CancellationToken);
        var archives = new CountingArchives(); var decoder = new DeferredDecoder(); using var cache = new BitmapFactory(decoder);
        var old = cache.GetCoverAsync(fixture.Zip, archives, state.FolderConfigs, new(8, 8, true), TestContext.Current.CancellationToken);
        await decoder.Started.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        cache.InvalidateCovers(); var current = cache.GetCoverAsync(fixture.Zip, archives, state.FolderConfigs, new(8, 8, true), TestContext.Current.CancellationToken); decoder.Release.TrySetResult();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => old); using var image = await current;
        Assert.Equal(2, decoder.Images.Count); Assert.Empty(decoder.Images[0].Pixels); Assert.Same(decoder.Images[1], image.Image); Assert.Equal(2, archives.Closes);
    }
    /// <summary>离开可见树后返回的租约不创建显示图，始终归还；不因控件回收泄漏原生资源。</summary>
    [AvaloniaFact]
    public async Task LateListLeaseAfterHideIsReleasedWithoutDisplay()
    {
        var started = new TaskCompletionSource(); var result = new TaskCompletionSource<BitmapLease>(); bool released = false;
        var pixels = new DecodedImageLease(new(8, 8), new byte[256]);
        var cover = new ListCoverImage { Width = 64, Height = 64, Source = "/fixture.cbz", LoadCoverAsync = (_, _, _) => { started.TrySetResult(); return result.Task; } };
        var host = new Border { Child = cover }; var window = new Window { Width = 300, Height = 200, Content = host }; window.Show();
        try
        {
            window.UpdateLayout(); await started.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
            var loading = cover.Loading; host.IsVisible = false; result.SetResult(new(pixels, _ => { }, _ => { released = true; pixels.Dispose(); }));
            await loading; Assert.True(released); Assert.Empty(pixels.Pixels); Assert.False(cover.HasImage);
        }
        finally { window.Close(); }
    }
    /// <summary>一万条目只实现可见邻行；Normal不取封面，切模板/滚动保持同一条目及网格键盘导航。</summary>
    [AvaloniaFact]
    public async Task TenThousandItemsVirtualizeAndOnlyVisibleCoversLoad()
    {
        Config.SetCurrent(new()); int reads = 0;
        var items = Enumerable.Range(0, 10000).Select(i => new FolderItem($"漫画{i:00000}", $"/fixture/{i}.cbz", false)).ToArray();
        var list = new ListBox { ItemsSource = items, SelectionMode = SelectionMode.Multiple }; list.SelectedItem = items[2];
        var presentation = new PanelListPresentation(list, (_, _, token) =>
        { token.ThrowIfCancellationRequested(); reads++; return Task.FromResult(new BitmapLease(new(new(8, 8), Enumerable.Repeat((byte)140, 256).ToArray()), _ => { }, _ => { })); });
        var border = new Border { Child = list }; var window = new Window { Width = 520, Height = 400, Content = border }; window.Show();
        try
        {
            presentation.Apply(PanelListItemStyle.Normal); await SettleAsync(window); Assert.Equal(0, reads); Assert.Same(items[2], list.SelectedItem);
            presentation.Apply(PanelListItemStyle.Thumbnail); await SettleAsync(window);
            var panel = Assert.Single(list.GetVisualDescendants().OfType<VirtualizingThumbnailPanel>());
            Assert.InRange(panel.Columns, 2, 4); Assert.InRange(panel.RealizedCount, 1, 24); Assert.InRange(reads, 1, 16);
            var row = list.ContainerFromItem(items[2])!; row.Focus();
            window.KeyPress(Key.Down, Avalonia.Input.RawInputModifiers.None, PhysicalKey.ArrowDown, null); window.KeyRelease(Key.Down, Avalonia.Input.RawInputModifiers.None, PhysicalKey.ArrowDown, null); await SettleAsync(window);
            Assert.Same(items[2 + panel.Columns], list.SelectedItem);
            list.SelectedItem = items[9000]; list.ScrollIntoView(items[9000]); await SettleAsync(window);
            Assert.NotNull(list.ContainerFromItem(items[9000])); Assert.True(list.ContainerFromItem(items[9000])!.Bounds.Height > 0); Assert.InRange(panel.RealizedCount, 1, 28);
            Assert.True(reads < 40); Assert.Same(items[9000], list.SelectedItem);
            SaveImage(window, "virtual-grid");
            var covers = list.GetVisualDescendants().OfType<ListCoverImage>().ToArray(); Assert.Contains(covers, cover => cover.HasImage);
            border.IsVisible = false; await SettleAsync(window); Assert.All(covers, cover => Assert.False(cover.HasImage));
            border.IsVisible = true; presentation.Apply(PanelListItemStyle.Normal); await SettleAsync(window);
            Assert.Same(items[9000], list.SelectedItem); Assert.All(covers, cover => Assert.False(cover.HasImage));
        }
        finally { window.Close(); }
    }
    /// <summary>正式三个列表可独立切换、保存和失败回滚；普通外观变更不重建正文或丢选择。</summary>
    [AvaloniaFact]
    public async Task ActualListMenusPersistAndRollbackWithoutChangingReader()
    {
        using var fixture = new Fixture(); var state = new SaveData(fixture.State); await state.LoadAsync(TestContext.Current.CancellationToken);
        var operation = fixture.Operation(state); var cache = new BitmapFactory(new MagickImageDecoder());
        var model = new ReaderWorkspaceViewModel(operation, new CommandTable(operation), state); var window = new MainWindow(); window.Bind(model, cache, new NoPlatform()); window.Show();
        try
        {
            await window.OpenAsync(fixture.Images); await operation.SaveAllAsync(TestContext.Current.CancellationToken); await state.RegisterBookmarkAsync(operation.Book!, token: TestContext.Current.CancellationToken);
            model.ShowPanel("HistoryPanel"); Dispatcher.UIThread.RunJobs(); await SettleAsync(window); var book = operation.Book;
            var list = window.FindControl<ListBox>("HistoryList")!; var selected = list.SelectedItem;
            await WaitForCoverAsync(window, list); SaveImage(window, "content");
            await window.SetListStyleAsync(true, PanelListItemStyle.Banner); await SettleAsync(window); Assert.Same(book, operation.Book); Assert.Same(selected, list.SelectedItem);
            Assert.All(list.GetVisualDescendants().OfType<PanelListItemView>(), view => Assert.Equal(PanelListItemStyle.Banner, view.DisplayStyle)); SaveImage(window, "banner");
            var bookmark = window.FindControl<BookmarkListView>("BookmarkPanelList")!; await bookmark.SetListStyleAsync(PanelListItemStyle.Thumbnail); await window.SetListStyleAsync(false, PanelListItemStyle.Normal);
            var saved = JsonNode.Parse(await File.ReadAllTextAsync(Path.Combine(fixture.State, "UserSetting.json"), TestContext.Current.CancellationToken))!["Config"]!;
            Assert.Equal(2, saved["History"]!["PanelListItemStyle"]!.GetValue<int>()); Assert.Equal(3, saved["Bookmark"]!["PanelListItemStyle"]!.GetValue<int>()); Assert.Equal(0, saved["Bookshelf"]!["PanelListItemStyle"]!.GetValue<int>());
            var file = Path.Combine(fixture.State, "UserSetting.json"); File.Delete(file); Directory.CreateDirectory(file);
            await window.SetListStyleAsync(true, PanelListItemStyle.Thumbnail); Assert.Equal(PanelListItemStyle.Banner, Config.Current.History.PanelListItemStyle); Assert.Same(selected, list.SelectedItem); Assert.Same(book, operation.Book);
            await bookmark.SetListStyleAsync(PanelListItemStyle.Normal); Assert.Equal(PanelListItemStyle.Thumbnail, Config.Current.Bookmark.PanelListItemStyle);
            Directory.Delete(file); await window.SetListStyleAsync(true, PanelListItemStyle.Content); await SettleAsync(window);
            await WaitForCoverAsync(window, list);
        }
        finally { await window.PrepareShutdownAsync(); window.Close(); }
    }
    /// <summary>历史详细模板展示原访问日期，Banner/Thumbnail与主题可独立修改。</summary>
    [AvaloniaFact]
    public void HistoryPresentationKeepsAccessTimeAndGroupIdentity()
    {
        Config.SetCurrent(new()); var date = new DateTime(2024, 2, 3, 4, 5, 0);
        var row = new HistoryRow(new("/comic.cbz", "005.png", date), "本周");
        var view = new PanelListItemView { DataContext = row };
        var text = Assert.IsType<ListItemText>(view.FindControl<Grid>("ItemRoot")!.DataContext);
        Assert.Equal(date, text.LastAccessTime); Assert.Equal(date.ToString("g"), text.Date); Assert.True(text.DateVisible); Assert.True(text.HasGroupHeader); Assert.Same(row, view.DataContext);
        var window = new Window { Content = view }; window.Show();
        try
        {
            row.Update(new("/comic.cbz", "006.png", date.AddDays(1)), "今天");
            var updated = Assert.IsType<ListItemText>(view.FindControl<Grid>("ItemRoot")!.DataContext);
            Assert.Equal(date.AddDays(1), updated.LastAccessTime); Assert.Equal("006.png", updated.Page);
            Assert.Equal("今天", updated.GroupHeader); Assert.Same(row, view.DataContext);
        }
        finally { window.Close(); }
    }
    /// <summary>布局与防抖队列完成后读取实际显示资源，不用单次控件创建冒充加载完成。</summary>
    private static async Task SettleAsync(Window window)
    {
        Dispatcher.UIThread.RunJobs(); window.UpdateLayout(); await Task.Delay(220, TestContext.Current.CancellationToken);
        Dispatcher.UIThread.RunJobs(); window.UpdateLayout();
        await Task.WhenAll(window.GetVisualDescendants().OfType<ListCoverImage>().Select(cover => cover.Loading)).WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
        Dispatcher.UIThread.RunJobs(); window.UpdateLayout();
    }
    /// <summary>模板切换后有效视口可能在下一帧才发布；等待当前封面，失败仍回报真实后端错误。</summary>
    private static async Task WaitForCoverAsync(Window window, ListBox list)
    {
        var deadline = System.Diagnostics.Stopwatch.StartNew();
        while (deadline.Elapsed < TimeSpan.FromSeconds(10))
        {
            Dispatcher.UIThread.RunJobs(); window.UpdateLayout();
            var covers = list.GetVisualDescendants().OfType<ListCoverImage>().ToArray();
            if (covers.Any(cover => cover.HasImage)) return;
            if (covers.Any(cover => cover.Error is not null)) break;
            await Task.Delay(20, TestContext.Current.CancellationToken);
        }
        Assert.Fail("当前列表封面未完成：" + string.Join("; ", list.GetVisualDescendants().OfType<ListCoverImage>()
            .Select(cover => $"bounds={cover.Bounds}, visible={cover.IsVisible}, task={cover.Loading.Status}, error={cover.Error}")));
    }
    private static void SaveImage(Window window, string suffix)
    {
        var phase = Environment.GetEnvironmentVariable("NEEVIEW_ACCEPTANCE_PHASE") ?? "p2-list-templates";
        var path = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, $"../../../../../acceptance/{phase}-{suffix}-layout.png"));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!); using var image = window.CaptureRenderedFrame(); image!.Save(path, PngBitmapEncoderOptions.Default);
    }
    /// <summary>真实后端包装，只计数来源所有权，不模拟读取或解码成功。</summary>
    private sealed class CountingArchives : IArchiveFactory
    {
        private readonly ArchiveFactory _inner = new(); public int Opens, Closes;
        public async Task<Archive> OpenAsync(string path, CancellationToken token) { Opens++; return new CountingArchive(await _inner.OpenAsync(path, token), () => Closes++); }
        public Task<IReadOnlyList<FolderItem>> ListFoldersAsync(string path, CancellationToken token) => _inner.ListFoldersAsync(path, token);
        public Task<IReadOnlyList<FolderItem>> ListBooksAsync(string path, CancellationToken token) => _inner.ListBooksAsync(path, token);
        public Task<FolderItem?> GetFileMetadataAsync(string path, CancellationToken token) => _inner.GetFileMetadataAsync(path, token);
    }
    private sealed class CountingArchive(Archive inner, Action closed) : Archive(inner.Path)
    {
        public override bool IsDirectory => inner.IsDirectory;
        public override Task<IReadOnlyList<ArchiveEntry>> GetEntriesAsync(CancellationToken token) => inner.GetEntriesAsync(token);
        public override Task<Stream> OpenEntryAsync(ArchiveEntry entry, CancellationToken token) => inner.OpenEntryAsync(entry, token);
        public override async ValueTask DisposeAsync() { await inner.DisposeAsync(); closed(); }
    }
    private sealed class NoPlatform : IPlatformService
    {
        public Task RevealAsync(string path, CancellationToken token = default) => throw new NotSupportedException();
        public Task TrashAsync(string path, CancellationToken token = default) => throw new NotSupportedException();
    }
    private sealed class DeferredDecoder : IImageDecoder
    {
        public TaskCompletionSource Started { get; } = new();
        public TaskCompletionSource Release { get; } = new();
        public List<DecodedImageLease> Images { get; } = [];
        public Task<ImageInfo> ProbeAsync(Stream stream, CancellationToken token) => throw new NotSupportedException();
        public async Task<DecodedImageLease> DecodeAsync(Stream stream, DecodeRequest request, CancellationToken token)
        { Started.TrySetResult(); await Release.Task; var image = new DecodedImageLease(new(8, 8), new byte[256]); Images.Add(image); return image; }
    }
}
