using System.IO.Compression;
using System.Text.Json.Nodes;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using NeeView;
using NeeView.Backends;
using NeeView.MacOS.ViewModels;
using NeeView.MacOS.Views;
namespace NeeView.Engine.Tests;

/// <summary>原普通书架及文件夹页导航回归；夹具、解码和正式视图均走产品链路。</summary>
public sealed class BookshelfNavigationTests
{
    /// <summary>书架只枚举目录和支持的归档，损坏包保留候选，图片不被误当作独立书籍。</summary>
    [Fact]
    public async Task RealBookshelfEnumeratesMetadataWithoutOpeningArchives()
    {
        using var fixture = new Fixture(); var library = CreateLibrary(fixture);
        await File.WriteAllTextAsync(Path.Combine(library, "broken.CBZ"), "损坏归档", TestContext.Current.CancellationToken);
        await File.WriteAllTextAsync(Path.Combine(library, "ignore.txt"), "", TestContext.Current.CancellationToken);
        File.Copy(Path.Combine(fixture.Images, "001.png"), Path.Combine(library, "cover.png"));
        Directory.CreateDirectory(Path.Combine(library, ".hidden"));
        var items = await new ArchiveFactory().ListBooksAsync(library, TestContext.Current.CancellationToken);
        Assert.Equal(5, items.Count); Assert.Contains(items, i => i.Name == "broken.CBZ" && !i.IsDirectory && i.Length > 0);
        Assert.DoesNotContain(items, i => i.Name is "ignore.txt" or "cover.png" or ".hidden");
        var sorted = FolderCollection.Sort(items, FolderOrder.FileName, FolderSortOrder.First, 10, TestContext.Current.CancellationToken);
        Assert.Equal(new[] { "Book2", "Book10" }, sorted.Take(2).Select(i => i.Name));
    }

    /// <summary>目录分组优先于降序键；时间/大小同值保留自然名称次序，类型混排保留原目录规则。</summary>
    [Fact]
    public void OriginalSortingKeepsGroupPrecedenceAndTies()
    {
        var items = new[] { new FolderItem("20.zip", "/20.zip", false, 10), new FolderItem("10.rar", "/10.rar", false, 5),
            new FolderItem("2.rar", "/2.rar", false, 5), new FolderItem("Folder10", "/Folder10"), new FolderItem("Folder2", "/Folder2") };
        var token = TestContext.Current.CancellationToken;
        Assert.Equal(new[] { "Folder10", "Folder2", "20.zip", "10.rar", "2.rar" }, FolderCollection.Sort(items, FolderOrder.FileNameDescending, FolderSortOrder.First, 5, token).Select(i => i.Name));
        Assert.Equal(new[] { "20.zip", "2.rar", "10.rar", "Folder2", "Folder10" }, FolderCollection.Sort(items, FolderOrder.SizeDescending, FolderSortOrder.Last, 5, token).Select(i => i.Name));
        Assert.Equal(new[] { "2.rar", "10.rar", "20.zip", "Folder2", "Folder10" }, FolderCollection.Sort(items, FolderOrder.FileType, FolderSortOrder.None, 5, token).Select(i => i.Name));
        Assert.Equal(new[] { "Folder2", "Folder10", "2.rar", "10.rar", "20.zip" }, FolderCollection.Sort(items, FolderOrder.TimeStampDescending, FolderSortOrder.First, 5, token).Select(i => i.Name));
    }

    /// <summary>前后书共用列表顺序及 JSON 恢复，加载失败不提交选择，修复来源后重试同一本。</summary>
    [Fact]
    public async Task NextBookUsesVisibleOrderRestoresPageAndRetriesFailure()
    {
        using var fixture = new Fixture(); var library = CreateLibrary(fixture);
        var state = new SaveData(fixture.State); await state.LoadAsync(TestContext.Current.CancellationToken);
        await using var operation = fixture.Operation(state); var commands = new CommandTable(operation);
        await operation.OpenAsync(Path.Combine(library, "Book10"), TestContext.Current.CancellationToken); await operation.JumpAsync(2);
        await operation.OpenAsync(Path.Combine(library, "Book2"), TestContext.Current.CancellationToken);
        await operation.Bookshelf.SyncAsync(operation.Book!, TestContext.Current.CancellationToken);
        await commands.ExecuteAsync("NextBook"); Assert.Equal("Book10", Path.GetFileName(operation.Book!.Path)); Assert.Equal("003.png", operation.Book.CurrentPage!.EntryName);
        operation.Bookshelf.Select(operation.Bookshelf.Items.Last()); await commands.ExecuteAsync("NextBook");
        Assert.Equal("Book10", Path.GetFileName(operation.Book.Path)); Assert.Contains("末项", operation.Error);
        // 真机回归：成功翻页后，旧书架边界提示应让位给当前页面状态。
        await operation.MoveAsync(-1); Assert.Null(operation.Error);
        operation.Bookshelf.Select(operation.Bookshelf.Items.Single(i => i.Name == "Book2"));
        var selected = operation.Bookshelf.SelectedItem; var old = operation.Book;
        var target = Path.Combine(library, "Book10"); Directory.Move(target, target + "离线");
        try { await commands.ExecuteAsync("NextBook"); Assert.Same(old, operation.Book); Assert.Same(selected, operation.Bookshelf.SelectedItem); Assert.NotNull(operation.Error); }
        finally { Directory.Move(target + "离线", target); }
        await commands.ExecuteAsync("NextBook"); Assert.Null(operation.Error); Assert.Equal(target, operation.Book!.Path);
        Assert.Equal(target, operation.Bookshelf.SelectedItem!.Path);
        await commands.ExecuteAsync("NextBook"); Assert.Equal("Book3.cbz", Path.GetFileName(operation.Book.Path));
        await commands.ExecuteAsync("PrevBook"); Assert.Equal(target, operation.Book.Path);
    }

    /// <summary>随机排序采用稳定种子并仅在随机模式循环；普通首尾及无选择均不循环。</summary>
    [Fact]
    public async Task RandomWrapAndNormalBoundariesUseOriginalSelection()
    {
        using var fixture = new Fixture(); var library = CreateLibrary(fixture);
        var state = new SaveData(fixture.State); await state.LoadAsync(TestContext.Current.CancellationToken);
        using var list = new BookshelfFolderList(new ArchiveFactory());
        await list.SetPlaceAsync(library, token: TestContext.Current.CancellationToken); Assert.Null(list.GetFolderItem(1));
        list.Select(list.Items.First()); Assert.Null(list.GetFolderItem(-1));
        list.Select(list.Items.Last()); Assert.Null(list.GetFolderItem(1));
        list.ChangeOrder(FolderOrder.Random); var order = list.Items.Select(i => i.Path).ToArray();
        list.Select(list.Items.Last()); Assert.Same(list.Items.First(), list.GetFolderItem(1));
        await list.RefreshAsync(TestContext.Current.CancellationToken); Assert.Equal(order, list.Items.Select(i => i.Path));
        list.Select(list.Items.First()); Assert.Same(list.Items.Last(), list.GetFolderItem(-1));
    }

    /// <summary>浏览目录不加载书籍，失败保留旧位置与对象；取消晚到枚举和关闭都不能覆盖最新列表。</summary>
    [Fact]
    public async Task BookshelfBrowseFailureAndLateResultsKeepCommittedList()
    {
        using var fixture = new Fixture(); var library = CreateLibrary(fixture);
        var state = new SaveData(fixture.State); await state.LoadAsync(TestContext.Current.CancellationToken);
        var factory = new PausedBookshelfFactory(); using var list = new BookshelfFolderList(factory);
        await list.SetPlaceAsync(library, Path.Combine(library, "Book2"), TestContext.Current.CancellationToken); var selection = list.SelectedItem; var items = list.Items;
        Assert.False(await list.SetPlaceAsync(Path.Combine(library, "missing"), token: TestContext.Current.CancellationToken)); Assert.Same(items, list.Items); Assert.Same(selection, list.SelectedItem); Assert.Equal(library, list.Place);
        factory.PausedPath = Path.Combine(library, "Book2"); var pending = list.SetPlaceAsync(factory.PausedPath, token: TestContext.Current.CancellationToken);
        await factory.Started.Task.WaitAsync(TestContext.Current.CancellationToken);
        await list.SetPlaceAsync(library, Path.Combine(library, "Book10"), TestContext.Current.CancellationToken); factory.Release.SetResult(); await pending;
        Assert.Equal(library, list.Place); Assert.Equal("Book10", list.SelectedItem!.Name); Assert.Null(list.Error); Assert.False(list.IsLoading);
        factory.Reset(Path.Combine(library, "Book10")); pending = list.SetPlaceAsync(factory.PausedPath!, token: TestContext.Current.CancellationToken); await factory.Started.Task.WaitAsync(TestContext.Current.CancellationToken);
        list.Dispose(); factory.Release.SetResult(); Assert.False(await pending); Assert.Equal(library, list.Place);
    }

    /// <summary>文件名正反顺序均按原目录变化分组，前跳从组内回本组首项，端点无循环。</summary>
    [Theory]
    [InlineData(PageSortMode.FileName)]
    [InlineData(PageSortMode.FileNameDescending)]
    public async Task FolderPageNavigationUsesOriginalGroupAndForwardFrame(PageSortMode mode)
    {
        using var fixture = new Fixture(); var zip = CreateGroupedArchive(fixture);
        var state = new SaveData(fixture.State); await state.LoadAsync(TestContext.Current.CancellationToken);
        await using var operation = fixture.Operation(state); await operation.OpenAsync(zip, TestContext.Current.CancellationToken);
        await operation.ApplySettingAsync(s => s.SortMode = mode); await operation.JumpAsync(0); var pages = operation.Book!.Pages;
        int second = pages.GetNextFolderIndex(0); Assert.True(second > 0); Assert.Equal(-1, pages.GetPrevFolderIndex(0));
        // 打开失败仍可阅读旧书；成功章节跳页不能遗留上一请求的错误。
        await operation.OpenAsync(Path.Combine(fixture.Root, "missing.cbz"), TestContext.Current.CancellationToken); Assert.NotNull(operation.Error);
        await operation.MoveFolderPageAsync(1); Assert.Equal(second, operation.Frame!.FrameRange.Min.Index); Assert.Null(operation.Error);
        await operation.JumpAsync(second + 1); await operation.MoveFolderPageAsync(-1); Assert.Equal(second, operation.Frame.FrameRange.Min.Index); Assert.Equal(1, operation.MoveDirection);
        await operation.MoveFolderPageAsync(-1); Assert.Equal(0, operation.Frame.FrameRange.Min.Index);
        await operation.JumpAsync(pages.Count - 1); var frame = operation.Frame;
        await operation.MoveFolderPageAsync(1); Assert.Same(frame, operation.Frame);
    }

    /// <summary>非文件名排序明确不执行目录跳页，不破坏位置、帧或导航历史。</summary>
    [Theory]
    [InlineData(PageSortMode.Size)]
    [InlineData(PageSortMode.TimeStamp)]
    [InlineData(PageSortMode.FileType)]
    [InlineData(PageSortMode.Random)]
    public async Task FolderPageNavigationRejectsUnsupportedSorting(PageSortMode mode)
    {
        using var fixture = new Fixture(); var zip = CreateGroupedArchive(fixture);
        var state = new SaveData(fixture.State); await state.LoadAsync(TestContext.Current.CancellationToken);
        await using var operation = fixture.Operation(state); await operation.OpenAsync(zip, TestContext.Current.CancellationToken);
        await operation.ApplySettingAsync(s => s.SortMode = mode); await operation.JumpAsync(2);
        var frame = operation.Frame; var previous = operation.PageHistory.GetTarget(-1);
        var commands = new CommandTable(operation); await commands.ExecuteAsync("NextFolderPage"); await commands.ExecuteAsync("PrevFolderPage");
        Assert.Same(frame, operation.Frame); Assert.Equal(previous, operation.PageHistory.GetTarget(-1));
    }

    /// <summary>原 Bookshelf 分支与未知字段往返，排序和优先加载选项不写入另一套状态。</summary>
    [Fact]
    public async Task BookshelfJsonKeepsUnknownFieldsAndEnumValues()
    {
        using var fixture = new Fixture(); Directory.CreateDirectory(fixture.State);
        await File.WriteAllTextAsync(Path.Combine(fixture.State, "UserSetting.json"), """{"Config":{"Bookshelf":{"DefaultFolderOrder":7,"FolderSortOrder":2,"Future":42},"Book":{"IsPrioritizeBookMove":true}}} """, TestContext.Current.CancellationToken);
        var state = new SaveData(fixture.State); await state.LoadAsync(TestContext.Current.CancellationToken);
        Assert.Equal(FolderOrder.TimeStampDescending, Config.Current.Bookshelf.DefaultFolderOrder); Assert.True(Config.Current.Book.IsPrioritizeBookMove);
        await state.SaveAsync(null, TestContext.Current.CancellationToken); var json = JsonNode.Parse(await File.ReadAllTextAsync(Path.Combine(fixture.State, "UserSetting.json"), TestContext.Current.CancellationToken))!;
        Assert.Equal(42, json["Config"]!["Bookshelf"]!["Future"]!.GetValue<int>());
        var fresh = new SaveData(fixture.State); await fresh.LoadAsync(TestContext.Current.CancellationToken);
        Assert.Equal(FolderSortOrder.Last, Config.Current.Bookshelf.FolderSortOrder);
    }

    /// <summary>默认书籍加载期间拒绝前后书；优先模式取代旧加载，native晚到来源只释放。</summary>
    [Fact]
    public async Task PrioritizeBookMoveSupersedesSlowOpenWithoutLateCommit()
    {
        using var fixture = new Fixture(); var library = CreateLibrary(fixture);
        var state = new SaveData(fixture.State); await state.LoadAsync(TestContext.Current.CancellationToken);
        var factory = new PausedBookshelfFactory { PausedOpenPath = Path.Combine(library, "Book20.cbz") };
        await using var operation = new BookOperation(factory, new MagickImageDecoder(), state);
        await operation.OpenAsync(Path.Combine(library, "Book2"), TestContext.Current.CancellationToken);
        await operation.Bookshelf.SyncAsync(operation.Book!, TestContext.Current.CancellationToken);
        var original = operation.Book; var slow = operation.OpenAsync(factory.PausedOpenPath, TestContext.Current.CancellationToken);
        await factory.OpenStarted.Task.WaitAsync(TestContext.Current.CancellationToken);
        await operation.MoveBookAsync(1); Assert.Same(original, operation.Book);
        Config.Current.Book.IsPrioritizeBookMove = true;
        await operation.MoveBookAsync(1); Assert.Equal("Book10", Path.GetFileName(operation.Book!.Path));
        factory.OpenRelease.SetResult(); await slow;
        Assert.Equal("Book10", Path.GetFileName(operation.Book.Path)); Assert.True(factory.LateSource!.IsDisposed); Assert.False(operation.IsLoading);
    }

    /// <summary>正式列表方向键只改变选择，Enter打开条目；浏览/排序不刷新正文或扫描同目录。</summary>
    [AvaloniaFact]
    public async Task ActualFolderListOwnsKeysAndKeepsSharedSelection()
    {
        using var fixture = new Fixture(); var library = CreateLibrary(fixture);
        var state = new SaveData(fixture.State); await state.LoadAsync(TestContext.Current.CancellationToken);
        var operation = fixture.Operation(state); var images = new BitmapFactory(new MagickImageDecoder());
        var model = new ReaderWorkspaceViewModel(operation, new CommandTable(operation), state);
        var window = new MainWindow(); window.Bind(model, images, new NoSystemPlatform()); window.Show();
        try
        {
            await window.OpenAsync(Path.Combine(library, "Book2")); await WaitForAsync(() => operation.Bookshelf.SelectedItem?.Name == "Book2");
            if (!model.ShowFolderList) await window.ExecuteAsync("ToggleVisibleBookshelf"); window.UpdateLayout(); Dispatcher.UIThread.RunJobs();
            var list = window.FindControl<ListBox>("FolderList")!; Assert.Equal(4, list.ItemCount); Assert.Same(operation.Bookshelf.SelectedItem, list.SelectedItem);
            int refreshes = 0; model.Refreshed += (_, _) => refreshes++;
            Assert.True(list.ContainerFromIndex(0)!.Focus()); window.KeyPress(Key.Down, RawInputModifiers.None, PhysicalKey.ArrowDown, null); window.KeyRelease(Key.Down, RawInputModifiers.None, PhysicalKey.ArrowDown, null);
            await WaitForAsync(() => model.SelectedFolder?.Name == "Book10"); Assert.Equal("Book2", Path.GetFileName(operation.Book!.Path)); Assert.Equal(0, refreshes);
            window.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null); window.KeyRelease(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null);
            await WaitForAsync(() => Path.GetFileName(operation.Book!.Path) == "Book10" && !operation.IsLoading); Dispatcher.UIThread.RunJobs();
            Assert.Same(operation.Bookshelf.SelectedItem, list.SelectedItem); Assert.Same(operation.Book!.CurrentPage, window.FindControl<ListBox>("PageList")!.SelectedItem);
            var book = operation.Book; refreshes = 0; await window.ExecuteAsync("EnterBookshelfFolder"); Dispatcher.UIThread.RunJobs();
            Assert.Equal(book!.Path, operation.Bookshelf.Place); Assert.Empty(list.Items); Assert.Same(book, operation.Book); Assert.Equal(0, refreshes);
            await window.ExecuteAsync("ParentFolder"); Dispatcher.UIThread.RunJobs(); Assert.Same(book, operation.Book); Assert.Same(operation.Bookshelf.SelectedItem, list.SelectedItem);
            operation.Bookshelf.ChangeOrder(FolderOrder.FileNameDescending); Dispatcher.UIThread.RunJobs();
            Assert.Same(operation.Bookshelf.SelectedItem, list.SelectedItem); Assert.Equal("Book10", model.SelectedFolder!.Name); Assert.Equal(0, refreshes);
            await operation.SaveAsync();
            using var screenshot = window.CaptureRenderedFrame(); Assert.NotNull(screenshot);
            var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../"));
            var phase = Environment.GetEnvironmentVariable("NEEVIEW_ACCEPTANCE_PHASE") ?? "p2-bookmark";
            screenshot!.Save(Path.Combine(root, $"acceptance/{phase}-reading-layout.png"), PngBitmapEncoderOptions.Default);
        }
        finally { await window.PrepareShutdownAsync(); window.Close(); }
    }

    /// <summary>创建包含目录和归档的同一书架；状态目录在书架之外。</summary>
    private static string CreateLibrary(Fixture fixture)
    {
        var library = Path.Combine(fixture.Root, "library"); Directory.CreateDirectory(library);
        foreach (var name in new[] { "Book2", "Book10" })
        {
            var folder = Path.Combine(library, name); Directory.CreateDirectory(folder);
            foreach (var file in Directory.EnumerateFiles(fixture.Images)) File.Copy(file, Path.Combine(folder, Path.GetFileName(file)));
        }
        File.Copy(fixture.Zip, Path.Combine(library, "Book3.cbz")); File.Copy(fixture.Zip, Path.Combine(library, "Book20.cbz")); return library;
    }
    /// <summary>根组、两个章节每组两图，真实 ZIP 夹具验证内部目录名称而非数字页号。</summary>
    private static string CreateGroupedArchive(Fixture fixture)
    {
        var path = Path.Combine(fixture.Root, "chapters.cbz"); using var zip = ZipFile.Open(path, ZipArchiveMode.Create);
        foreach (var name in new[] { "001.png", "002.png", "A/001.png", "A/002.png", "B/001.png", "B/002.png" })
            zip.CreateEntryFromFile(Path.Combine(fixture.Images, "001.png"), name);
        return path;
    }
    /// <summary>以实际状态轮询异步路由，超时即失败；不把固定延迟当作功能通过。</summary>
    private static async Task WaitForAsync(Func<bool> ready)
    { for (int i = 0; i < 300 && !ready(); i++) { Dispatcher.UIThread.RunJobs(); await Task.Delay(10, TestContext.Current.CancellationToken); } Assert.True(ready()); }
    /// <summary>导航验证不执行系统文件操作，误调用明确失败。</summary>
    private sealed class NoSystemPlatform : IPlatformService
    {
        public Task RevealAsync(string path, CancellationToken token = default) => throw new NotSupportedException();
        public Task TrashAsync(string path, CancellationToken token = default) => throw new NotSupportedException();
    }
    /// <summary>模拟不能即时中断的枚举，调用方必须用代次拒绝晚到结果。</summary>
    private sealed class PausedBookshelfFactory : IArchiveFactory
    {
        private readonly ArchiveFactory _inner = new();
        public string? PausedPath { get; set; }
        public string? PausedOpenPath { get; set; }
        public Archive? LateSource { get; private set; }
        public TaskCompletionSource OpenStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource OpenRelease { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Started { get; private set; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; private set; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        /// <summary>重置一次受控等待，测试关闭时保留最后提交集合。</summary>
        public void Reset(string path) { PausedPath = path; Started = new(TaskCreationOptions.RunContinuationsAsynchronously); Release = new(TaskCreationOptions.RunContinuationsAsynchronously); }
        /// <summary>模拟不可中断的来源打开，核验切书取消后的释放与提交裁决。</summary>
        public async Task<Archive> OpenAsync(string path, CancellationToken token)
        {
            if (path != PausedOpenPath) return await _inner.OpenAsync(path, token);
            OpenStarted.TrySetResult(); await OpenRelease.Task;
            LateSource = await _inner.OpenAsync(path, CancellationToken.None); return LateSource;
        }
        public Task<IReadOnlyList<FolderItem>> ListFoldersAsync(string path, CancellationToken token) => _inner.ListFoldersAsync(path, token);
        /// <summary>暂停指定路径并忽略取消后返回元数据，覆盖真实晚到任务边界。</summary>
        public async Task<IReadOnlyList<FolderItem>> ListBooksAsync(string path, CancellationToken token)
        {
            if (path == PausedPath) { Started.TrySetResult(); await Release.Task; }
            return await _inner.ListBooksAsync(path, CancellationToken.None);
        }
    }
}
