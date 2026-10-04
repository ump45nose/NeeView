using System.Text.Json.Nodes;
using Avalonia.Controls;
using Avalonia;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using NeeView.Backends;
using NeeView.MacOS.Views;
namespace NeeView.Engine.Tests;

/// <summary>原普通目录树的异步替换点：展开、同步、取消和原JSON，不模拟图片读取成功。</summary>
public sealed class DirectoryTreeTests
{
    /// <summary>构造/读取占位无I/O；展开只读一级，自然排序及缓存重展开不重扫。</summary>
    [Fact]
    public async Task ExpansionIsLazyOneLevelNaturallySortedAndCached()
    {
        Config.SetCurrent(new()); var archives = new TreeArchives(); archives.Folders("/tree", "10", "2", "1");
        using var shelf = new BookshelfFolderList(archives); using var tree = new FolderTreeModel(archives, shelf, "/tree");
        Assert.Empty(archives.Reads); Assert.True(Assert.Single(tree.Root.Children).IsPlaceholder);
        tree.Root.IsExpanded = true; Assert.True(await tree.Root.Loading);
        Assert.Equal(new[] { "1", "2", "10" }, tree.Root.Children.Select(n => n.Name)); Assert.Equal(new[] { "/tree" }, archives.Reads);
        Assert.All(tree.Root.Children.Cast<DirectoryNode>(), n => { Assert.True(n.IsDelayCreation); Assert.Same(tree.Root, n.Parent); Assert.Equal("/tree/" + n.Name, n.Path); });
        tree.Root.IsExpanded = false; tree.Root.IsExpanded = true; Assert.Single(archives.Reads); Assert.Null(shelf.Place);
    }

    /// <summary>失败不替换可用子树；成功刷新保留同名引用，移除已选择节点退回有效父节点。</summary>
    [Fact]
    public async Task RefreshPreservesReferencesOnFailureAndRepairsRemovedSelection()
    {
        Config.SetCurrent(new()); var archives = new TreeArchives(); archives.Folders("/tree", "2", "10"); archives.Folders("/tree/2", "child");
        using var shelf = new BookshelfFolderList(archives); using var tree = new FolderTreeModel(archives, shelf, "/tree");
        await tree.Root.CreateChildrenAsync(token: TestContext.Current.CancellationToken); var two = tree.Root.Children.OfType<DirectoryNode>().First(); await two.CreateChildrenAsync(token: TestContext.Current.CancellationToken);
        tree.SelectedItem = two; var child = Assert.Single(two.Children); var original = tree.Root.Children;
        archives.Fail = true; Assert.False(await tree.Root.CreateChildrenAsync(true, TestContext.Current.CancellationToken)); Assert.Same(original, tree.Root.Children); Assert.Same(two, tree.SelectedItem); Assert.NotNull(tree.Root.Error);
        archives.Fail = false; archives.Folders("/tree", "2", "20"); Assert.True(await tree.Root.CreateChildrenAsync(true, TestContext.Current.CancellationToken));
        Assert.Same(two, tree.Root.Children.First()); Assert.Same(child, Assert.Single(two.Children)); Assert.Null(tree.Root.Error);
        archives.Folders("/tree", "20"); Assert.True(await tree.Root.CreateChildrenAsync(true, TestContext.Current.CancellationToken));
        Assert.True(two.IsDisposed); Assert.True(child.IsDisposed); Assert.False(two.IsSelected); Assert.Same(tree.Root, tree.SelectedItem);
    }

    /// <summary>不能中断的后台枚举在折叠/隐藏/关闭后晚到，不提交占位替换或错误状态。</summary>
    [Theory]
    [InlineData("collapse")]
    [InlineData("hide")]
    [InlineData("close")]
    public async Task LateEnumerationCannotCommitAfterCancellation(string action)
    {
        Config.SetCurrent(new()); var archives = new TreeArchives(); archives.Folders("/tree", "late"); archives.PausePath = "/tree";
        using var shelf = new BookshelfFolderList(archives); using var tree = new FolderTreeModel(archives, shelf, "/tree");
        tree.Root.IsExpanded = true; var pending = tree.Root.Loading; await archives.Started.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        if (action == "collapse") tree.Root.IsExpanded = false; else if (action == "hide") tree.CancelPending(); else tree.Dispose();
        archives.Release.SetResult(); Assert.False(await pending); Assert.True(tree.Root.IsDelayCreation); Assert.False(tree.Root.IsLoading); Assert.Null(tree.Root.Error);
        if (action != "close") { archives.PausePath = null; Assert.True(await tree.Root.CreateChildrenAsync(token: TestContext.Current.CancellationToken)); Assert.Equal("late", Assert.Single(tree.Root.Children).Name); }
        else Assert.False(await tree.Root.CreateChildrenAsync(token: TestContext.Current.CancellationToken));
    }

    /// <summary>只展开目标祖先链；树内焦点保留选择，显式同步可更新，不读兄弟分支。</summary>
    [Fact]
    public async Task SynchronizationLoadsOnlyAncestorsAndRespectsKeyboardFocus()
    {
        Config.SetCurrent(new()); var archives = new TreeArchives(); archives.Folders("/tree", "2", "10"); archives.Folders("/tree/2", "child");
        using var shelf = new BookshelfFolderList(archives); using var tree = new FolderTreeModel(archives, shelf, "/tree");
        tree.SelectedItem = tree.Root; tree.HasKeyboardFocus = true;
        var child = await tree.SyncDirectoryAsync("/tree/2/child", token: TestContext.Current.CancellationToken); Assert.NotNull(child); Assert.Same(tree.Root, tree.SelectedItem);
        Assert.Equal(new[] { "/tree", "/tree/2" }, archives.Reads); Assert.True(tree.Root.IsExpanded); Assert.True(child.Parent!.IsExpanded); Assert.True(child.IsDelayCreation);
        Assert.Same(child, await tree.SyncDirectoryAsync(child.Path, true, TestContext.Current.CancellationToken)); Assert.Same(child, tree.SelectedItem); Assert.Equal(2, archives.Reads.Count);
        Assert.Null(await tree.SyncDirectoryAsync("/elsewhere", token: TestContext.Current.CancellationToken)); Assert.Same(child, tree.SelectedItem);
        Assert.Null(await tree.SyncDirectoryAsync("bookmark:/", token: TestContext.Current.CancellationToken)); Assert.Equal(2, archives.Reads.Count);
    }

    /// <summary>取消的旧同步不能覆盖新同步的选择；关闭解除书架自动同步订阅。</summary>
    [Fact]
    public async Task SupersededSyncKeepsLatestSelectionAndDisposeStopsAutoSync()
    {
        Config.SetCurrent(new()); var archives = new TreeArchives(); archives.Folders("/tree", "slow", "fast"); archives.Folders("/tree/slow", "child");
        using var shelf = new BookshelfFolderList(archives); using var tree = new FolderTreeModel(archives, shelf, "/tree");
        await tree.Root.CreateChildrenAsync(token: TestContext.Current.CancellationToken); archives.PausePath = "/tree/slow";
        var old = tree.SyncDirectoryAsync("/tree/slow/child", true, TestContext.Current.CancellationToken); await archives.Started.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        var current = await tree.SyncDirectoryAsync("/tree/fast", true, TestContext.Current.CancellationToken); Assert.NotNull(current);
        Assert.Null(await tree.SyncDirectoryAsync("/tree/fast/missing", true, TestContext.Current.CancellationToken)); var error = tree.Error; Assert.NotNull(error);
        archives.Release.SetResult(); Assert.Null(await old); Assert.Same(current, tree.SelectedItem); Assert.Equal(error, tree.Error);
        tree.Dispose(); int reads = archives.Reads.Count; Config.Current.Bookshelf.IsFolderTreeVisible = Config.Current.Bookshelf.IsSyncFolderTreeAuto = true;
        await shelf.SetPlaceAsync("/tree/slow", token: TestContext.Current.CancellationToken); Assert.Equal(reads, archives.Reads.Count); Assert.Null(tree.SelectedItem);
    }

    /// <summary>展开/确认只改变书架；真实阅读书籍保持，不添加第二个打开链。</summary>
    [Fact]
    public async Task DecideBrowsesShelfWithoutOpeningOrChangingCurrentBook()
    {
        using var fixture = new Fixture(); var state = new SaveData(fixture.State); await state.LoadAsync(TestContext.Current.CancellationToken);
        await using var operation = fixture.Operation(state); await operation.OpenAsync(fixture.Images, TestContext.Current.CancellationToken); var book = operation.Book;
        using var tree = new FolderTreeModel(new ArchiveFactory(), operation.Bookshelf, fixture.Root);
        var node = await tree.SyncDirectoryAsync(fixture.Images, true, TestContext.Current.CancellationToken); Assert.NotNull(node); Assert.Same(book, operation.Book);
        Assert.True(await tree.DecideAsync()); Assert.Equal(fixture.Images, operation.Bookshelf.Place); Assert.Same(book, operation.Book);
        using var other = new FolderTreeModel(new ArchiveFactory(), operation.Bookshelf, fixture.Root);
        tree.SelectedItem = other.Root; Assert.Same(node, tree.SelectedItem);
    }

    /// <summary>原Bookshelf树字段和PageList分支往返；未知分组/名称格式不被保存丢失。</summary>
    [Fact]
    public async Task OriginalTreeAndPageListJsonRoundTripWithoutDroppingUnknownFields()
    {
        using var fixture = new Fixture(); Directory.CreateDirectory(fixture.State);
        var file = Path.Combine(fixture.State, "UserSetting.json");
        await File.WriteAllTextAsync(file, """{"Config":{"Bookshelf":{"IsFolderTreeVisible":true,"FolderTreeLayout":0,"FolderTreeAreaWidth":150,"FolderTreeAreaHeight":180,"IsSyncFolderTree":true,"IsSyncFolderTreeAuto":true,"FutureTree":99},"PageList":{"PanelListItemStyle":3,"FocusMainView":true,"Format":2,"IsGroupBy":true}}}""", TestContext.Current.CancellationToken);
        var state = new SaveData(fixture.State); await state.LoadAsync(TestContext.Current.CancellationToken);
        Assert.True(Config.Current.Bookshelf.IsFolderTreeVisible); Assert.Equal(FolderTreeLayout.Top, Config.Current.Bookshelf.FolderTreeLayout); Assert.Equal(150, Config.Current.Bookshelf.FolderTreeAreaWidth);
        Assert.True(Config.Current.Bookshelf.IsSyncFolderTreeAuto); Assert.True(Config.Current.PageList.FocusMainView); Assert.Equal(PanelListItemStyle.Thumbnail, Config.Current.PageList.PanelListItemStyle);
        await state.SaveAsync(null, TestContext.Current.CancellationToken); var config = JsonNode.Parse(await File.ReadAllTextAsync(file, TestContext.Current.CancellationToken))!["Config"]!;
        Assert.Equal(99, config["Bookshelf"]!["FutureTree"]!.GetValue<int>()); Assert.True(config["PageList"]!["IsGroupBy"]!.GetValue<bool>()); Assert.Equal(2, config["PageList"]!["Format"]!.GetValue<int>());
        await new SaveData(fixture.State).LoadAsync(TestContext.Current.CancellationToken); Assert.Equal(180, Config.Current.Bookshelf.FolderTreeAreaHeight); Assert.True(Config.Current.PageList.FocusMainView);
    }

    /// <summary>正式树Top/Left/隐藏和保存失败回滚；读取失败只报树错误，不能撤销已落盘设置。</summary>
    [AvaloniaFact]
    public async Task ActualTreeLayoutPersistsAndReadFailureDoesNotRollbackSavedSetting()
    {
        using var fixture = new Fixture(); var state = new SaveData(fixture.State); await state.LoadAsync(TestContext.Current.CancellationToken);
        var archives = new TreeArchives { Fail = true }; var operation = new BookOperation(archives, new MagickImageDecoder(), state); var images = new BitmapFactory(new MagickImageDecoder());
        var window = new MainWindow(); window.Bind(new(operation, new CommandTable(operation), state), images, new NoPlatform()); window.Show();
        try
        {
            await window.OpenAsync(fixture.Images); await operation.Bookshelf.SetPlaceAsync(fixture.Root, token: TestContext.Current.CancellationToken);
            await window.SetFolderTreeVisibleAsync(true); Dispatcher.UIThread.RunJobs(); window.UpdateLayout();
            var tree = window.FindControl<FolderTreeView>("BookshelfDirectoryTree")!; var body = window.FindControl<Grid>("FolderBrowserBody")!;
            Assert.True(tree.IsVisible); Assert.NotNull(operation.Bookshelf.FolderTree.Error); Assert.Equal(3, body.ColumnDefinitions.Count);
            var file = Path.Combine(fixture.State, "UserSetting.json"); var saved = JsonNode.Parse(await File.ReadAllTextAsync(file, TestContext.Current.CancellationToken))!["Config"]!["Bookshelf"]!;
            Assert.True(saved["IsFolderTreeVisible"]!.GetValue<bool>());
            var splitter = window.FindControl<GridSplitter>("FolderTreeSplitter")!; var definitions = body.ColumnDefinitions;
            var start = splitter.TranslatePoint(new(2, 80), window)!.Value; double beforeWidth = tree.Bounds.Width;
            window.MouseDown(start, MouseButton.Left); window.MouseMove(start + new Avalonia.Vector(20, 0), RawInputModifiers.LeftMouseButton); window.UpdateLayout();
            Assert.True(tree.Bounds.Width > beforeWidth); double draggedWidth = tree.Bounds.Width;
            operation.Bookshelf.Select(null); Dispatcher.UIThread.RunJobs(); window.UpdateLayout();
            Assert.Same(definitions, body.ColumnDefinitions); Assert.Equal(draggedWidth, tree.Bounds.Width);
            window.MouseUp(start + new Avalonia.Vector(20, 0), MouseButton.Left); await PageListThumbnailTests.SettleAsync(window);
            Assert.Equal(draggedWidth, Config.Current.Bookshelf.FolderTreeAreaWidth);
            await window.SetFolderTreeLayoutAsync(FolderTreeLayout.Top); window.UpdateLayout(); Assert.Equal(3, body.RowDefinitions.Count); Assert.Single(body.ColumnDefinitions);
            await state.SynchronizeWritesAsync(); File.Delete(file); Directory.CreateDirectory(file);
            await window.SetFolderTreeLayoutAsync(FolderTreeLayout.Left); Assert.Equal(FolderTreeLayout.Top, Config.Current.Bookshelf.FolderTreeLayout); Assert.Equal(3, body.RowDefinitions.Count);
            Directory.Delete(file); await window.SetFolderTreeVisibleAsync(false); Assert.False(tree.IsVisible); Assert.Equal(0, body.RowDefinitions[0].Height.Value);
        }
        finally { await window.PrepareShutdownAsync(); window.Close(); }
    }

    /// <summary>真实TreeView方向键只选节点，Enter才改变书架；折叠不抹掉已加载子树。</summary>
    [AvaloniaFact]
    public async Task ActualTreeSelectionRequiresConfirmationAndKeepsExpandedChildren()
    {
        Config.SetCurrent(new()); var archives = new TreeArchives(); archives.Folders("/tree", "Book2", "Book10");
        using var shelf = new BookshelfFolderList(archives); using var model = new FolderTreeModel(archives, shelf, "/tree");
        var view = new FolderTreeView(); view.Attach(model); var window = new Window { Width = 360, Height = 400, Content = view }; window.Show();
        try
        {
            model.Root.IsExpanded = true; await model.Root.Loading; model.SelectedItem = model.Root; Dispatcher.UIThread.RunJobs(); window.UpdateLayout();
            var tree = view.FindControl<TreeView>("DirectoryTree")!; Assert.True(tree.ContainerFromItem(model.Root)!.Focus());
            window.KeyPress(Key.Down, RawInputModifiers.None, PhysicalKey.ArrowDown, null); window.KeyRelease(Key.Down, RawInputModifiers.None, PhysicalKey.ArrowDown, null);
            Assert.Equal("Book2", model.SelectedItem!.Name); Assert.True(model.HasKeyboardFocus); Assert.Null(shelf.Place);
            window.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null); window.KeyRelease(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null);
            Assert.Equal("/tree/Book2", shelf.Place); var children = model.Root.Children;
            model.Root.IsExpanded = false; model.Root.IsExpanded = true; Assert.Same(children, model.Root.Children); Assert.Single(archives.Reads);
            Dispatcher.UIThread.RunJobs(); window.UpdateLayout();
            var phase = Environment.GetEnvironmentVariable("NEEVIEW_ACCEPTANCE_PHASE") ?? "p3-navigation";
            var path = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, $"../../../../../acceptance/{phase}-directory-tree-layout.png"));
            using var frame = window.CaptureRenderedFrame(); frame!.Save(path, PngBitmapEncoderOptions.Default);
        }
        finally { window.Close(); }
        Assert.False(model.HasKeyboardFocus);
    }

    /// <summary>隐藏整个祖先面板取消正在枚举的树，并禁止隐藏期间的自动同步；重显后可继续。</summary>
    [AvaloniaFact]
    public async Task AncestorPanelHideCancelsLateTreeAndSuspendsAutoSynchronization()
    {
        Config.SetCurrent(new()); Config.Current.Bookshelf.IsFolderTreeVisible = Config.Current.Bookshelf.IsSyncFolderTreeAuto = true;
        var archives = new TreeArchives { PausePath = "/tree" }; archives.Folders("/tree", "child");
        using var shelf = new BookshelfFolderList(archives); using var model = new FolderTreeModel(archives, shelf, "/tree");
        var view = new FolderTreeView(); view.Attach(model); var host = new Border { Child = view }; var window = new Window { Width = 360, Height = 400, Content = host }; window.Show();
        try
        {
            model.Root.IsExpanded = true; var pending = model.Root.Loading; await archives.Started.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
            model.SelectedItem = model.Root; Dispatcher.UIThread.RunJobs(); window.UpdateLayout();
            var tree = view.FindControl<TreeView>("DirectoryTree")!; tree.ContainerFromItem(model.Root)!.Focus();
            window.KeyPress(Key.Down, RawInputModifiers.None, PhysicalKey.ArrowDown, null); window.KeyRelease(Key.Down, RawInputModifiers.None, PhysicalKey.ArrowDown, null);
            Assert.Same(model.Root, tree.SelectedItem);
            host.IsVisible = false; Assert.False(model.IsPresented); archives.Release.SetResult(); Assert.False(await pending); Assert.True(model.Root.IsDelayCreation);
            int reads = archives.Reads.Count; await shelf.SetPlaceAsync("/tree/child", token: TestContext.Current.CancellationToken); Assert.Equal(reads, archives.Reads.Count);
            archives.PausePath = null; host.IsVisible = true; await model.Synchronizing; Assert.True(model.IsPresented); Assert.Equal("child", model.SelectedItem!.Name);
        }
        finally { window.Close(); }
        Assert.False(model.IsPresented);
    }

    /// <summary>可控元数据延迟/异常；Open仍交给真实后端，暂停故意忽略取消以验证晚到防护。</summary>
    private sealed class TreeArchives : IArchiveFactory
    {
        private readonly ArchiveFactory _inner = new();
        private readonly Dictionary<string, IReadOnlyList<FolderItem>> _folders = [];
        public List<string> Reads { get; } = [];
        public bool Fail; public string? PausePath;
        public TaskCompletionSource Started { get; } = new(); public TaskCompletionSource Release { get; } = new();
        public void Folders(string path, params string[] names) => _folders[path] = names.Select(n => new FolderItem(n, Path.Combine(path, n), true)).ToArray();
        public async Task<IReadOnlyList<FolderItem>> ListFoldersAsync(string path, CancellationToken token)
        { Reads.Add(path); if (path == PausePath) { Started.TrySetResult(); await Release.Task; } if (Fail) throw new IOException("来源不可访问"); return _folders.GetValueOrDefault(path, []); }
        public Task<IReadOnlyList<FolderItem>> ListBooksAsync(string path, CancellationToken token) => Task.FromResult(_folders.GetValueOrDefault(path, []));
        public Task<Archive> OpenAsync(string path, CancellationToken token) => _inner.OpenAsync(path, token);
        public Task<FolderItem?> GetFileMetadataAsync(string path, CancellationToken token) => _inner.GetFileMetadataAsync(path, token);
    }
    private sealed class NoPlatform : IPlatformService
    { public Task RevealAsync(string path, CancellationToken token = default) => throw new NotSupportedException(); public Task TrashAsync(string path, CancellationToken token = default) => throw new NotSupportedException(); }
}
