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

/// <summary>正式页面模板和原Page确认行为；资源由同一BitmapFactory管理。</summary>
public sealed class PageListThumbnailTests
{
    /// <summary>万页数据只实现可见邻行；Normal不读图，离屏跳转不创建中间页控件。</summary>
    [AvaloniaFact]
    public async Task TenThousandPagesVirtualizeOriginalIdentityAndReleaseOffscreenLeases()
    {
        Config.SetCurrent(new()); await using var source = new EmptyArchive();
        var pages = Enumerable.Range(0, 10000).Select(i => new Page(new(source) { Id = i, RawEntryName = $"{i:00000}.png" })).ToArray();
        int reads = 0, active = 0; var seen = new List<Page>();
        var list = new ListBox { ItemsSource = pages, SelectionMode = SelectionMode.Multiple, SelectedItem = pages[2] };
        var presentation = new PanelListPresentation(list, (_, _, _) => throw new InvalidOperationException("Page不能按路径封面读取"), (page, _, token) =>
        {
            token.ThrowIfCancellationRequested(); reads++; active++; seen.Add(page); Assert.Contains(page, pages); Assert.Same(source, page.ArchiveEntry.Archive);
            var image = new DecodedImageLease(new(8, 8), Enumerable.Repeat((byte)140, 256).ToArray());
            return Task.FromResult(new BitmapLease(image, _ => { }, _ => { active--; image.Dispose(); }));
        });
        var border = new Border { Child = list }; var window = new Window { Width = 520, Height = 400, Content = border }; window.Show();
        try
        {
            presentation.Apply(PanelListItemStyle.Normal); await SettleAsync(window); Assert.Equal(0, reads);
            foreach (var style in new[] { PanelListItemStyle.Content, PanelListItemStyle.Banner, PanelListItemStyle.Thumbnail })
            {
                presentation.Apply(style); await SettleAsync(window); Assert.Same(pages[2], list.SelectedItem); Assert.InRange(active, 1, 16);
                Assert.All(list.GetVisualDescendants().OfType<PanelListItemView>(), v => Assert.Equal(style, v.DisplayStyle));
            }
            var panel = Assert.Single(list.GetVisualDescendants().OfType<VirtualizingThumbnailPanel>()); Assert.InRange(panel.RealizedCount, 1, 28);
            int initialReads = reads; list.SelectedItem = pages[9000]; list.ScrollIntoView(pages[9000]); await SettleAsync(window);
            Assert.NotNull(list.ContainerFromItem(pages[9000])); Assert.InRange(panel.RealizedCount, 1, 28); Assert.InRange(reads - initialReads, 1, 16); Assert.InRange(active, 1, 16);
            Assert.Contains(pages[9000], seen); Assert.All(list.GetVisualDescendants().OfType<ListCoverImage>(), c => Assert.Null(c.Source));
            border.IsVisible = false; await SettleAsync(window); Assert.Equal(0, active);
            border.IsVisible = true; await SettleAsync(window); Assert.InRange(active, 1, 16);
            presentation.Apply(PanelListItemStyle.Normal); await SettleAsync(window); Assert.Equal(0, active); Assert.Same(pages[9000], list.SelectedItem);
        }
        finally { window.Close(); }
        Assert.Equal(0, active);
    }

    /// <summary>回收控件切换Page后旧原生结果晚到，仅归还旧租约；关闭释放当前图。</summary>
    [AvaloniaFact]
    public async Task RecycledPageRejectsLateLeaseAndDisposesDisplayOnClose()
    {
        await using var source = new EmptyArchive(); var old = new Page(new(source) { RawEntryName = "old.png" }); var current = new Page(new(source) { RawEntryName = "current.png" });
        var started = new TaskCompletionSource(); var pending = new TaskCompletionSource<BitmapLease>(); var released = new List<Page>();
        BitmapLease Lease(Page page) { var image = new DecodedImageLease(new(8, 8), new byte[256]); return new(image, _ => { }, _ => { released.Add(page); image.Dispose(); }); }
        var cover = new ListCoverImage { Width = 64, Height = 64, PageSource = old, LoadPageAsync = (page, _, _) =>
            { if (ReferenceEquals(page, old)) { started.TrySetResult(); return pending.Task; } return Task.FromResult(Lease(page)); } };
        var window = new Window { Width = 200, Height = 200, Content = cover }; window.Show(); window.UpdateLayout();
        try
        {
            await started.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken); var loading = cover.Loading; cover.PageSource = current;
            await SettleAsync(window); Assert.True(cover.HasImage); pending.SetResult(Lease(old)); await loading;
            Assert.Contains(old, released); Assert.DoesNotContain(current, released); Assert.True(cover.HasImage);
        }
        finally { window.Close(); }
        Assert.Contains(current, released); Assert.False(cover.HasImage);
    }

    /// <summary>方向键只选页，Enter/单击定位；修饰键按下后提前松开不抢正文焦点。</summary>
    [AvaloniaFact]
    public async Task ActualPageListSeparatesSelectionConfirmationAndFocus()
    {
        using var fixture = new Fixture(); var state = new SaveData(fixture.State); await state.LoadAsync(TestContext.Current.CancellationToken);
        var operation = fixture.Operation(state); var images = new BitmapFactory(new MagickImageDecoder()); var model = new ReaderWorkspaceViewModel(operation, new CommandTable(operation), state);
        var window = new MainWindow(); window.Bind(model, images, new NoPlatform()); window.Show();
        try
        {
            await window.OpenAsync(fixture.Zip); model.ShowPanel("PageListPanel"); await SettleAsync(window); await state.SynchronizeWritesAsync();
            var list = window.FindControl<ListBox>("PageList")!; var book = operation.Book!; var page = book.CurrentPage; Assert.Same(page, list.SelectedItem);
            Assert.True(list.ContainerFromItem(page!)!.Focus()); window.KeyPress(Key.Down, RawInputModifiers.None, PhysicalKey.ArrowDown, null); window.KeyRelease(Key.Down, RawInputModifiers.None, PhysicalKey.ArrowDown, null);
            Assert.Same(book.Pages[1], list.SelectedItem); Assert.Same(page, book.CurrentPage);
            window.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null); window.KeyRelease(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null);
            await WaitAsync(() => ReferenceEquals(book.CurrentPage, book.Pages[1])); Assert.True(list.IsKeyboardFocusWithin);
            Config.Current.PageList.FocusMainView = true; var row = list.ContainerFromItem(book.Pages[3])!; var point = row.TranslatePoint(new(25, row.Bounds.Height / 2), window)!.Value;
            window.MouseDown(point, MouseButton.Left, RawInputModifiers.Shift); window.MouseUp(point, MouseButton.Left);
            Dispatcher.UIThread.RunJobs(); Assert.True(list.IsKeyboardFocusWithin); Assert.Same(book.Pages[1], book.CurrentPage);
            window.MouseDown(point, MouseButton.Left); window.MouseUp(point, MouseButton.Left); await WaitAsync(() => ReferenceEquals(book.CurrentPage, book.Pages[3]) && window.Viewer.IsKeyboardFocusWithin);
            var oldPage = book.Pages[2]; await window.OpenAsync(fixture.Images); var newBook = operation.Book!; await window.CommitPageListAsync(oldPage); Assert.Same(newBook, operation.Book); Assert.Same(newBook.Pages[0], newBook.CurrentPage);
        }
        finally { await window.PrepareShutdownAsync(); window.Close(); }
        Assert.Equal(0, images.GetDiagnostics().Leases); Assert.Equal(0, images.ByteCount);
    }

    /// <summary>真实ZIP逐页缩略共用当前来源，四模板/排序保存不切书，写入失败恢复原样式。</summary>
    [AvaloniaFact]
    public async Task ActualPageStylesShareBookAndPersistWithRollback()
    {
        using var fixture = new Fixture(); var state = new SaveData(fixture.State); await state.LoadAsync(TestContext.Current.CancellationToken);
        var operation = fixture.Operation(state); var images = new BitmapFactory(new MagickImageDecoder()); var model = new ReaderWorkspaceViewModel(operation, new CommandTable(operation), state);
        var window = new MainWindow(); window.Bind(model, images, new NoPlatform()); window.Show();
        try
        {
            await window.OpenAsync(fixture.Zip); model.ShowPanel("PageListPanel"); await SettleAsync(window); var book = operation.Book!; await operation.JumpAsync(2); var selected = book.CurrentPage;
            var list = window.FindControl<ListBox>("PageList")!;
            foreach (var style in Enum.GetValues<PanelListItemStyle>())
            {
                await window.SetPageListStyleAsync(style); await SettleAsync(window); Assert.Same(book, operation.Book); Assert.Same(selected, list.SelectedItem);
                var covers = list.GetVisualDescendants().OfType<ListCoverImage>().ToArray();
                if (style == PanelListItemStyle.Normal) Assert.All(covers, c => Assert.False(c.HasImage));
                else { Assert.Contains(covers, c => c.HasImage); Assert.All(covers.Where(c => c.PageSource is not null), c => { Assert.Contains(c.PageSource!, book.Pages); Assert.Same(book.Source, c.PageSource!.ArchiveEntry.Archive); Assert.Null(c.Source); }); }
            }
            await operation.ApplySettingAsync(s => s.SortMode = PageSortMode.FileNameDescending); await SettleAsync(window); Assert.Same(selected, book.CurrentPage); Assert.Same(selected, list.SelectedItem);
            SaveImage(window, "page-thumbnails"); var file = Path.Combine(fixture.State, "UserSetting.json");
            var json = JsonNode.Parse(await File.ReadAllTextAsync(file, TestContext.Current.CancellationToken))!; Assert.Equal(3, json["Config"]!["PageList"]!["PanelListItemStyle"]!.GetValue<int>());
            await state.SynchronizeWritesAsync(); File.Delete(file); Directory.CreateDirectory(file);
            await window.SetPageListStyleAsync(PanelListItemStyle.Normal); Assert.Equal(PanelListItemStyle.Thumbnail, Config.Current.PageList.PanelListItemStyle); Assert.Same(selected, list.SelectedItem);
            Directory.Delete(file);
        }
        finally { await window.PrepareShutdownAsync(); window.Close(); }
        Assert.Equal(0, images.GetDiagnostics().Leases); Assert.Equal(0, images.ByteCount);
    }

    /// <summary>等待正式布局、防抖与实际租约完成；不使用前台应用或系统输入。</summary>
    internal static async Task SettleAsync(Window window)
    {
        Dispatcher.UIThread.RunJobs(); window.UpdateLayout(); await Task.Delay(220, TestContext.Current.CancellationToken);
        Dispatcher.UIThread.RunJobs(); window.UpdateLayout();
        await Task.WhenAll(window.GetVisualDescendants().OfType<ListCoverImage>().Select(c => c.Loading)).WaitAsync(TimeSpan.FromSeconds(30), TestContext.Current.CancellationToken);
        Dispatcher.UIThread.RunJobs(); window.UpdateLayout();
    }
    private static async Task WaitAsync(Func<bool> ready)
    { for (int i = 0; i < 250 && !ready(); i++) { Dispatcher.UIThread.RunJobs(); await Task.Delay(20, TestContext.Current.CancellationToken); } Assert.True(ready()); }
    private static void SaveImage(Window window, string suffix)
    {
        var phase = Environment.GetEnvironmentVariable("NEEVIEW_ACCEPTANCE_PHASE") ?? "p3-navigation";
        var path = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, $"../../../../../acceptance/{phase}-{suffix}-layout.png"));
        using var image = window.CaptureRenderedFrame(); image!.Save(path, PngBitmapEncoderOptions.Default);
    }
    private sealed class EmptyArchive() : Archive("/synthetic.cbz")
    {
        public override Task<IReadOnlyList<ArchiveEntry>> GetEntriesAsync(CancellationToken token) => throw new NotSupportedException();
        public override Task<Stream> OpenEntryAsync(ArchiveEntry entry, CancellationToken token) => throw new NotSupportedException();
        public override ValueTask DisposeAsync() { IsDisposed = true; return ValueTask.CompletedTask; }
    }
    private sealed class NoPlatform : IPlatformService
    { public Task RevealAsync(string path, CancellationToken token = default) => throw new NotSupportedException(); public Task TrashAsync(string path, CancellationToken token = default) => throw new NotSupportedException(); }
}
