using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using NeeView;
using NeeView.Backends;
using NeeView.MacOS.ViewModels;
using NeeView.MacOS.Views;
namespace NeeView.Engine.Tests;
/// <summary>书架bookmark scheme、原目录参数及独立面板互联；全部使用临时数据。</summary>
public sealed class BookshelfBookmarkTests
{
    [Fact]
    public async Task SchemeNavigationSharesNodesButNotIndependentListPosition()
    {
        using var f = new Fixture(); var state = new SaveData(f.State); var token = TestContext.Current.CancellationToken; await state.LoadAsync(token);
        var a = await state.AddBookmarkFolderAsync(null, "漫画", token); var b = await state.AddBookmarkFolderAsync(a, "第2卷", token);
        var leaf = new BookmarkNode { Name = "同一本书", Path = f.Zip }; b.Children!.Add(leaf);
        await using var op = f.Operation(state); using var independent = new BookmarkFolderList(state.Bookmarks, state.FolderConfigs);
        independent.SetPlace(a);
        Assert.True(await op.Bookshelf.SetPlaceAsync("bookmark:", token: token)); Assert.Same(a, Assert.Single(op.Bookshelf.Items).Bookmark);
        op.Bookshelf.Select(op.Bookshelf.Items[0]); Assert.True(await op.Bookshelf.EnterAsync(token)); Assert.Equal("bookmark:漫画", op.Bookshelf.Place);
        op.Bookshelf.Select(op.Bookshelf.Items[0]); await op.Bookshelf.EnterAsync(token); Assert.Same(leaf, Assert.Single(op.Bookshelf.Items).Bookmark); Assert.Same(a, independent.Place);
        await op.Bookshelf.UpAsync(token); Assert.Same(b, op.Bookshelf.SelectedItem!.Bookmark); await op.Bookshelf.UpAsync(token); Assert.Same(a, op.Bookshelf.SelectedItem!.Bookmark); Assert.False(await op.Bookshelf.UpAsync(token));
    }
    [Fact]
    public async Task DirectoryParametersRandomSeedsAndAliasSelectionSurviveRestart()
    {
        using var f = new Fixture(); var token = TestContext.Current.CancellationToken; var state = new SaveData(f.State); await state.LoadAsync(token);
        var a = await state.AddBookmarkFolderAsync(null, "A", token); var b = await state.AddBookmarkFolderAsync(null, "B", token);
        for (int i = 0; i < 8; i++) a.Children!.Add(new() { Name = "别名" + i, Path = f.Zip });
        await using var op = f.Operation(state); await op.Bookshelf.SetPlaceAsync("bookmark:A", token: token);
        op.Bookshelf.Select(op.Bookshelf.Items[3]); var selected = op.Bookshelf.SelectedItem!.Bookmark;
        await op.ChangeFolderOrderAsync(FolderOrder.Random); var order = op.Bookshelf.Items.Select(e => e.Name).ToArray(); var seed = state.FolderConfigs.GetFolderParameter("bookmark:A").Seed;
        Assert.Same(selected, op.Bookshelf.SelectedItem!.Bookmark); await op.Bookshelf.RefreshAsync(token); Assert.Equal(order, op.Bookshelf.Items.Select(e => e.Name)); Assert.Same(selected, op.Bookshelf.SelectedItem!.Bookmark);
        await op.OpenAsync(f.Zip, token); await op.Bookshelf.SyncAsync(op.Book!, token, fileSystem: false); Assert.Same(selected, op.Bookshelf.SelectedItem!.Bookmark);
        await op.Bookshelf.SetPlaceAsync("bookmark:B", token: token); await op.ChangeFolderOrderAsync(FolderOrder.EntryTimeDescending);
        Assert.Equal(FolderOrder.FileName, Config.Current.Bookmark.BookmarkFolderOrder);
        var restarted = new SaveData(f.State); await restarted.LoadAsync(token); using var list = new BookshelfFolderList(new ArchiveFactory(), restarted.FolderConfigs, restarted);
        await list.SetPlaceAsync("bookmark:A", token: token); Assert.Equal(order, list.Items.Select(e => e.Name)); Assert.Equal(seed, restarted.FolderConfigs.GetFolderParameter("bookmark:A").Seed);
        await list.SetPlaceAsync("bookmark:B", token: token); Assert.Equal(FolderOrder.EntryTimeDescending, list.FolderOrder);
    }
    [Fact]
    public async Task MetadataSortUsesSourceValuesAndFolderEntryTimeOnly()
    {
        Config.SetCurrent(new()); Config.Current.Bookshelf.FolderSortOrder = FolderSortOrder.None;
        var x = new BookmarkNode { Name = "X", Path = "/x", EntryTime = DateTime.MaxValue }; var y = new BookmarkNode { Name = "Y", Path = "/y", EntryTime = DateTime.MinValue };
        var folder = new BookmarkNode { Name = "F", Children = [], EntryTime = new(2020, 1, 1) }; var root = new BookmarkNode { Children = [x, y, folder] };
        using var list = new BookmarkFolderList(new(root)); int reads = 0;
        list.ReadMetadataAsync = (path, _) => { reads++; return Task.FromResult<FolderItem?>(new(path, path, false, path == "/x" ? 100 : 2, path == "/x" ? new(2019, 1, 1) : new(2021, 1, 1))); };
        list.ChangeOrder(FolderOrder.TimeStamp); Assert.True(await list.LoadMetadataAsync(TestContext.Current.CancellationToken)); Assert.Equal(new[] { x, folder, y }, list.Items); Assert.Equal(2, reads);
        list.ChangeOrder(FolderOrder.SizeDescending); await list.LoadMetadataAsync(TestContext.Current.CancellationToken); Assert.Equal(new[] { x, y, folder }, list.Items); Assert.Equal(2, reads);
        Assert.Equal(FolderOrder.FileName, Config.Current.Bookmark.BookmarkFolderOrder);
    }
    [Fact]
    public async Task MetadataLateResultsCannotReorderNewPlaceOrDeletedNodes()
    {
        Config.SetCurrent(new()); var root = new BookmarkNode { Children = [] }; var collection = new BookmarkCollection(root); var folder = collection.AddNewFolder(root, "A");
        folder.Children!.Add(new() { Path = "/slow" }); using var list = new BookmarkFolderList(collection); list.SetPlace(folder);
        var started = new TaskCompletionSource(); var finish = new TaskCompletionSource<FolderItem?>();
        list.ReadMetadataAsync = (_, _) => { started.SetResult(); return finish.Task; }; list.ChangeOrder(FolderOrder.Size);
        var load = list.LoadMetadataAsync(TestContext.Current.CancellationToken); await started.Task; list.MoveToRoot(); finish.SetResult(new("old", "/slow", false, 100));
        Assert.False(await load); Assert.Same(root, list.Place); Assert.Same(folder, Assert.Single(list.Items));
    }
    [Fact]
    public async Task MetadataCompletionKeepsPendingStructuredSearchAlive()
    {
        Config.SetCurrent(new()); var node = new BookmarkNode { Name = "漫画", Path = "/book" };
        using var list = new BookmarkFolderList(new(new() { Children = [node] }));
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var finish = new TaskCompletionSource<FolderItem?>(TaskCreationOptions.RunContinuationsAsynchronously);
        list.ReadMetadataAsync = (path, _) => Task.FromResult<FolderItem?>(new("book", path, false, 10));
        list.ChangeOrder(FolderOrder.Size);
        var search = list.SearchAsync("/size /gt 1", [], (_, _) => { started.TrySetResult(); return finish.Task; }, TestContext.Current.CancellationToken);
        try
        {
            await started.Task.WaitAsync(TimeSpan.FromSeconds(3), TestContext.Current.CancellationToken);
            Assert.True(await list.LoadMetadataAsync(TestContext.Current.CancellationToken)); Assert.True(list.IsSearching);
            finish.SetResult(new("book", node.Path!, false, 10)); Assert.True(await search);
            Assert.Equal("/size /gt 1", list.SearchKeyword); Assert.Same(node, Assert.Single(list.Items));
        }
        finally { finish.TrySetResult(null); await search; }
    }
    [Fact]
    public async Task FailedSourceAndFolderSaveKeepCurrentPlaceAndCanRetry()
    {
        using var f = new Fixture(); var state = new SaveData(f.State); var token = TestContext.Current.CancellationToken; await state.LoadAsync(token);
        var folder = await state.AddBookmarkFolderAsync(null, "A", token); await using var op = f.Operation(state); await op.Bookshelf.SetPlaceAsync("bookmark:A", token: token);
        Assert.False(await op.Bookshelf.SetPlaceAsync("bookmark:missing", token: token)); Assert.Equal("bookmark:A", op.Bookshelf.Place);
        var blocker = Path.Combine(f.State, "Foldres.json.tmp"); Directory.CreateDirectory(blocker);
        try { Assert.NotNull(await Record.ExceptionAsync(() => op.ChangeFolderOrderAsync(FolderOrder.Random))); Assert.Equal(FolderOrder.FileName, op.Bookshelf.FolderOrder); }
        finally { Directory.Delete(blocker); }
        await op.ChangeFolderOrderAsync(FolderOrder.EntryTime); Assert.Equal(FolderOrder.EntryTime, op.Bookshelf.FolderOrder);
    }
    [Fact]
    public async Task BookmarkTransactionsRefreshShelfWithoutReadingProgressRescan()
    {
        using var f = new Fixture(); var state = new SaveData(f.State); var token = TestContext.Current.CancellationToken; await state.LoadAsync(token);
        await using var op = f.Operation(state); await op.Bookshelf.SetPlaceAsync("bookmark:", token: token); int updates = 0; op.Bookshelf.Changed += (_, _) => updates++;
        await state.SaveAsync(null, token); Assert.Equal(0, updates);
        var node = await state.AddBookmarkFolderAsync(null, "New", token); Assert.Same(node, Assert.Single(op.Bookshelf.Items).Bookmark);
        op.Bookshelf.Select(op.Bookshelf.Items[0]); await op.Bookshelf.EnterAsync(token); await state.RemoveBookmarksAsync([node], token); Assert.Equal("bookmark:", op.Bookshelf.Place);
    }
    [AvaloniaFact]
    public async Task FocusCommandAndTransferKeepOriginalPanelMeanings()
    {
        using var f = new Fixture(); var state = new SaveData(f.State); var token = TestContext.Current.CancellationToken; await state.LoadAsync(token);
        var folder = await state.AddBookmarkFolderAsync(null, "漫画", token); var op = f.Operation(state); await op.OpenAsync(f.Images, token); var leaf = await state.RegisterBookmarkAsync(op.Book!, folder, token);
        var model = new ReaderWorkspaceViewModel(op, new(op), state); var window = new MainWindow(); window.Bind(model, new BitmapFactory(new MagickImageDecoder()), new NoPlatform()); window.Show();
        try
        {
            Pump(window); await window.ExecuteAsync("FocusBookmarkList"); Pump(window);
            Assert.True(model.ShowFolderList); Assert.Equal("bookmark:", op.Bookshelf.Place);
            Assert.True(window.FindControl<ListBox>("FolderList")!.IsKeyboardFocusWithin, "原聚焦命令应把键盘焦点交给书架列表。");
            var separate = window.FindControl<BookmarkListView>("BookmarkPanelList")!; Assert.Same(state.BookmarkRoot, separate.Navigation!.Place);
            op.Bookshelf.Select(op.Bookshelf.Items.Single()); await window.ExecuteAsync("EnterBookshelfFolder"); Pump(window); op.Bookshelf.Select(op.Bookshelf.Items.Single());
            window.FindControl<ListBox>("FolderList")!.ContextMenu!.Items.OfType<MenuItem>().Last().RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent)); Pump(window);
            Assert.True(model.ShowBookmarks); Assert.Same(folder, separate.Navigation.Place); Assert.Same(leaf, Assert.Single(separate.SelectedNodes));
            await window.ExecuteAsync("FocusBookmarkList"); Pump(window); Assert.Same(folder, separate.Navigation.Place);
            var tree = window.FindControl<TreeView>("BookshelfBookmarkTree")!; tree.SelectedItem = folder; await WaitAsync(() => op.Bookshelf.Place == "bookmark:漫画");
            Pump(window); await window.Viewer.RefreshAsync(); await WaitAsync(() => window.Viewer.DisplayCount > 0); Pump(window);
            var content = window.Viewer.GetContentRect();
            Assert.InRange(content.X, -.01, window.Viewer.Bounds.Width); Assert.InRange(content.Y, -.01, window.Viewer.Bounds.Height);
            Assert.True(content.X + content.Width <= window.Viewer.Bounds.Width + .01);
            Assert.True(content.Y + content.Height <= window.Viewer.Bounds.Height + .01);
            using var screenshot = window.CaptureRenderedFrame(); var phase = Environment.GetEnvironmentVariable("NEEVIEW_ACCEPTANCE_PHASE") ?? "p2-bookshelf-bookmarks";
            screenshot!.Save(Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, $"../../../../../acceptance/{phase}-bookshelf-bookmarks-layout.png")), PngBitmapEncoderOptions.Default);
        }
        finally { await window.PrepareShutdownAsync(); window.Close(); }
    }
    [AvaloniaFact]
    public async Task StartupRestoresEachListAndSelectedTargetFromOriginalJson()
    {
        using var f = new Fixture(); var state = new SaveData(f.State); var token = TestContext.Current.CancellationToken; await state.LoadAsync(token);
        var folder = await state.AddBookmarkFolderAsync(null, "A", token); var leaf = new BookmarkNode { Path = f.Zip }; folder.Children!.Add(leaf);
        await using (var op = f.Operation(state))
        {
            await op.Bookshelf.SetPlaceAsync("bookmark:A", leaf.Path, token); await op.ChangeFolderOrderAsync(FolderOrder.Random);
            Config.Current.StartUp.IsOpenLastFolder = true; Config.Current.StartUp.IsOpenLastBookmarkFolder = true;
            using var vm = new BookmarkListViewModel(state); vm.List.SetPlace(folder, leaf); await vm.PrepareCloseAsync();
        }
        var restarted = new SaveData(f.State); await restarted.LoadAsync(token); await using var restored = f.Operation(restarted); Assert.True(await restored.RestoreBookshelfAsync());
        Assert.Equal("bookmark:A", restored.Bookshelf.Place); Assert.Equal(f.Zip, restored.Bookshelf.SelectedItem!.Path);
        using var bookmarks = new BookmarkListViewModel(restarted); Assert.Equal("bookmark:A", bookmarks.List.ParameterPath); Assert.Equal(f.Zip, bookmarks.List.SelectedItem!.Path);
    }
    private static void Pump(Window window) { Dispatcher.UIThread.RunJobs(); window.UpdateLayout(); Dispatcher.UIThread.RunJobs(); }
    private static async Task WaitAsync(Func<bool> ready) { for (int i = 0; i < 200 && !ready(); i++) { Dispatcher.UIThread.RunJobs(); await Task.Delay(10, TestContext.Current.CancellationToken); } Assert.True(ready()); }
    private sealed class NoPlatform : IPlatformService { public Task RevealAsync(string path, CancellationToken token) => Task.CompletedTask; public Task TrashAsync(string path, CancellationToken token) => Task.CompletedTask; }
}
