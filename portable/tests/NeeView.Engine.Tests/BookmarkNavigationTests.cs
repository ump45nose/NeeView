using System.Text.Json.Nodes;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using NeeView;
using NeeView.MacOS.ViewModels;
using NeeView.MacOS.Views;
namespace NeeView.Engine.Tests;

/// <summary>原书签列表导航与排序回归；不将正式 XAML 的 Headless 结果当作真机验收。</summary>
public sealed class BookmarkNavigationTests
{
    /// <summary>进入/逐级返回保持原节点定位；过期目录和书籍不能作为目录进入。</summary>
    [Fact]
    public void ParentNavigationKeepsNodesAndCannotEscapeBookmarkRoot()
    {
        Config.SetCurrent(new()); var root = new BookmarkNode { Children = [] }; var collection = new BookmarkCollection(root);
        var a = collection.AddNewFolder(root, "中文"); var b = collection.AddNewFolder(a, "子目录");
        var leaf = new BookmarkNode { Path = "/a.cbz" }; b.Children!.Add(leaf);
        var list = new BookmarkFolderList(collection);
        Assert.False(list.CanMoveToParent); Assert.True(list.SetPlace(b, leaf)); Assert.Same(leaf, list.SelectedItem);
        Assert.Equal("书签 / 中文 / 子目录", list.FullPath); Assert.False(list.SetPlace(leaf)); Assert.Same(b, list.Place);
        Assert.True(list.MoveToParent()); Assert.Same(a, list.Place); Assert.Same(b, list.SelectedItem);
        Assert.True(list.MoveToParent()); Assert.Same(root, list.Place); Assert.Same(a, list.SelectedItem); Assert.False(list.MoveToParent());
        collection.Remove(a); Assert.False(list.SetPlace(a)); Assert.Same(root, list.Place);
    }

    /// <summary>同步优先当前目录的同路径别名；没有匹配保持当前位置，不能按显示名称猜路径。</summary>
    [Fact]
    public void SyncPrefersCurrentFolderBeforeWalkingOtherAliases()
    {
        Config.SetCurrent(new()); var root = new BookmarkNode { Children = [] }; var collection = new BookmarkCollection(root);
        var a = collection.AddNewFolder(root, "A"); var b = collection.AddNewFolder(root, "B");
        var first = new BookmarkNode { Name = "第一处", Path = "/漫画.cbz" }; a.Children!.Add(first);
        var current = new BookmarkNode { Name = "第二处", Path = first.Path }; b.Children!.Add(current);
        var list = new BookmarkFolderList(collection); list.SetPlace(b); Assert.True(list.Sync(first.Path)); Assert.Same(current, list.SelectedItem);
        Assert.False(list.Sync("/missing.cbz")); Assert.Same(b, list.Place); Assert.Same(current, list.SelectedItem);
        list.MoveToRoot(); Assert.True(list.Sync(first.Path)); Assert.Same(a, list.Place); Assert.Same(first, list.SelectedItem);
    }

    /// <summary>注册排序按原节点索引，与日期值及目录分组无关；降序反转整列而不改原树。</summary>
    [Theory]
    [InlineData(FolderSortOrder.First)]
    [InlineData(FolderSortOrder.Last)]
    [InlineData(FolderSortOrder.None)]
    public void EntryOrderUsesIndexInsteadOfDates(FolderSortOrder grouping)
    {
        Config.SetCurrent(new()); Config.Current.Bookshelf.FolderSortOrder = grouping;
        var book = new BookmarkNode { Name = "Book10", Path = "/x.cbz", EntryTime = DateTime.MaxValue };
        var folder = new BookmarkNode { Name = "Folder2", Children = [], EntryTime = DateTime.MinValue };
        var book2 = new BookmarkNode { Name = "Book2", Path = "/y.cbz" };
        var root = new BookmarkNode { Children = [book, folder, book2] }; var list = new BookmarkFolderList(new(root));
        list.ChangeOrder(FolderOrder.EntryTime); Assert.Equal(root.Children, list.Items);
        list.ChangeOrder(FolderOrder.EntryTimeDescending); Assert.Equal(root.Children.Reverse(), list.Items);
        Assert.Equal(new[] { book, folder, book2 }, root.Children);
    }

    /// <summary>自然名称/类型/真实目标路径排序与目录分组保持原比较规则；随机刷新保留次序。</summary>
    [Fact]
    public void SortPreservesGroupingTargetPathsAndRandomSeed()
    {
        Config.SetCurrent(new());
        var a = new BookmarkNode { Name = "Book10", Path = "/a.cbz" }; var b = new BookmarkNode { Name = "Book2", Path = "/z.cbz" };
        var folder = new BookmarkNode { Name = "Folder", Children = [] }; var root = new BookmarkNode { Children = [a, b, folder] };
        var list = new BookmarkFolderList(new(root)); Assert.Equal(new[] { folder, b, a }, list.Items);
        list.Select(b); list.ChangeOrder(FolderOrder.FileNameDescending); Assert.Equal(new[] { folder, a, b }, list.Items); Assert.Same(b, list.SelectedItem);
        Config.Current.Bookshelf.FolderSortOrder = FolderSortOrder.None; list.ChangeOrder(FolderOrder.Path); Assert.Equal(new[] { a, b, folder }, list.Items);
        list.ChangeOrder(FolderOrder.FileType); Assert.Equal(new[] { b, a, folder }, list.Items);
        list.ChangeOrder(FolderOrder.Random); var sequence = list.Items.ToArray(); list.Refresh(); Assert.Equal(sequence, list.Items); Assert.Same(b, list.SelectedItem);
    }

    /// <summary>移动当前文件夹保留位置并更新面包屑；删除当前位置退到最近存活祖先。</summary>
    [Fact]
    public void MovedAndDeletedPlacesRecoverThroughLiveAncestors()
    {
        Config.SetCurrent(new()); var root = new BookmarkNode { Children = [] }; var collection = new BookmarkCollection(root);
        var a = collection.AddNewFolder(root, "A"); var b = collection.AddNewFolder(root, "B"); var inner = collection.AddNewFolder(a, "子");
        var list = new BookmarkFolderList(collection); list.SetPlace(inner); collection.Move(inner, b, 0); list.Refresh();
        Assert.Same(inner, list.Place); Assert.Equal("书签 / B / 子", list.FullPath);
        collection.Remove(inner); list.Refresh(); Assert.Same(b, list.Place); collection.Remove(b); list.Refresh(); Assert.Same(root, list.Place);
    }

    /// <summary>原 JSON 新字段读取/保存保留未知搜索和布局字段；不支持的旧排序不被默默覆盖。</summary>
    [Fact]
    public async Task ConfigurationRetainsPendingFieldsAndUnsupportedOrders()
    {
        using var fixture = new Fixture(); Directory.CreateDirectory(fixture.State);
        await File.WriteAllTextAsync(Path.Combine(fixture.State, "UserSetting.json"), """{"Config":{"Bookmark":{"BookmarkFolderOrder":6,"IsFolderTreeVisible":true,"IsVisibleItemsCount":false,"IsSearchIncludeSubdirectories":false,"Future":{"keep":7}}}}""", TestContext.Current.CancellationToken);
        var state = new SaveData(fixture.State); await state.LoadAsync(TestContext.Current.CancellationToken);
        var list = new BookmarkFolderList(state.Bookmarks); Assert.Equal(FolderOrder.FileName, list.FolderOrder); Assert.NotNull(list.CapabilityMessage);
        Assert.Throws<NotSupportedException>(() => list.ChangeOrder(FolderOrder.Size)); Assert.Equal(FolderOrder.TimeStamp, Config.Current.Bookmark.BookmarkFolderOrder);
        Assert.True(Config.Current.Bookmark.IsFolderTreeVisible); Assert.False(Config.Current.Bookmark.IsVisibleItemsCount);
        await state.SaveAsync(null, 0, TestContext.Current.CancellationToken);
        var json = JsonNode.Parse(await File.ReadAllTextAsync(Path.Combine(fixture.State, "UserSetting.json"), TestContext.Current.CancellationToken))!;
        Assert.Equal(6, json["Config"]!["Bookmark"]!["BookmarkFolderOrder"]!.GetValue<int>());
        Assert.False(json["Config"]!["Bookmark"]!["IsSearchIncludeSubdirectories"]!.GetValue<bool>());
        Assert.Equal(7, json["Config"]!["Bookmark"]!["Future"]!["keep"]!.GetValue<int>());
    }

    /// <summary>进度保存不重排书签；失败编辑回报仅在原地回滚完成后发布。</summary>
    [Fact]
    public async Task BookmarkNotificationsExcludeReadingSavesAndFollowRollback()
    {
        using var fixture = new Fixture(); var state = new SaveData(fixture.State); await state.LoadAsync(TestContext.Current.CancellationToken);
        var count = 0; state.BookmarksChanged += (_, _) => count++;
        await state.SaveAsync(null, 0, TestContext.Current.CancellationToken); Assert.Equal(0, count);
        var node = await state.AddBookmarkFolderAsync(null, "原名", TestContext.Current.CancellationToken); Assert.Equal(1, count);
        Directory.CreateDirectory(Path.Combine(fixture.State, "Bookmark.json.tmp"));
        try { await Assert.ThrowsAnyAsync<Exception>(() => state.RenameBookmarkAsync(node, "新名", token: TestContext.Current.CancellationToken)); }
        finally { Directory.Delete(Path.Combine(fixture.State, "Bookmark.json.tmp")); }
        Assert.Equal("原名", node.Name); Assert.Equal(2, count);
    }

    /// <summary>正式列表 Enter 进入/打开、Backspace 返回，方向键仅改选择；编辑树与阅读内核继续共用。</summary>
    [AvaloniaFact]
    public async Task ActualListOwnsNavigationKeysAndSharesEditorNodes()
    {
        using var fixture = new Fixture(); var state = new SaveData(fixture.State); await state.LoadAsync(TestContext.Current.CancellationToken);
        var folder = await state.AddBookmarkFolderAsync(null, "漫画", TestContext.Current.CancellationToken);
        var operation = fixture.Operation(state); await operation.OpenAsync(fixture.Images, TestContext.Current.CancellationToken);
        var leaf = await state.RegisterBookmarkAsync(operation.Book!, folder, TestContext.Current.CancellationToken);
        var model = new ReaderWorkspaceViewModel(operation, new(operation), state); var images = new BitmapFactory(new NeeView.Backends.MagickImageDecoder());
        var window = new MainWindow(); window.Bind(model, images, new TestPlatform()); window.Show();
        try
        {
            model.ShowPanel("BookmarkPanel"); Dispatcher.UIThread.RunJobs(); window.UpdateLayout();
            var view = window.FindControl<BookmarkListView>("BookmarkPanelList")!; var list = view.FindControl<ListBox>("BookmarkItems")!;
            Assert.False(window.FindControl<TreeView>("BookmarkTree")!.IsVisible); Assert.False(window.IsCommandAvailable("FocusBookmarkList"));
            var refreshes = 0; model.Refreshed += (_, _) => refreshes++;
            list.SelectedItem = folder; Assert.True(list.Focus(), $"列表不可聚焦：visible={list.IsEffectivelyVisible}, bounds={list.Bounds}"); Assert.Same(folder, list.SelectedItem);
            window.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null);
            Dispatcher.UIThread.RunJobs(); Assert.Same(folder, view.Navigation!.Place); Assert.Same(leaf, Assert.Single(list.Items)); Assert.Equal(0, refreshes);
            list.SelectedItem = leaf; list.Focus(); window.KeyPress(Key.Down, RawInputModifiers.None, PhysicalKey.ArrowDown, null);
            Assert.Equal(0, operation.Book!.CurrentPage!.Index); Assert.Equal(0, refreshes);
            window.KeyPress(Key.Back, RawInputModifiers.None, PhysicalKey.Backspace, null); Dispatcher.UIThread.RunJobs();
            Assert.Same(state.BookmarkRoot, view.Navigation.Place); Assert.Same(folder, list.SelectedItem);
            view.Reveal(leaf); Dispatcher.UIThread.RunJobs(); Assert.Same(folder, view.Navigation.Place); Assert.Same(leaf, list.SelectedItem); Assert.Same(leaf, model.SelectedBookmark);
            // 缺失来源通过既有打开链报错，当前位置和旧书保持；不删除书签节点。
            var missing = new BookmarkNode { Name = "离线", Path = Path.Combine(fixture.Root, "missing.cbz") }; folder.Children!.Add(missing);
            view.Reveal(missing); var old = operation.Book; await view.OpenSelectedAsync(); Assert.Same(old, operation.Book); Assert.NotNull(operation.Error);
            view.Reveal(leaf); await view.OpenSelectedAsync(); Assert.Equal(fixture.Images, operation.Book!.Path);
            await state.RenameBookmarkAsync(folder, "漫画重命名", token: TestContext.Current.CancellationToken); Dispatcher.UIThread.RunJobs(); Assert.Contains("漫画重命名", view.Navigation.FullPath);
            Config.Current.Bookmark.IsFolderTreeVisible = true; model.RefreshBookmarkTree(); window.UpdateLayout(); Assert.True(window.FindControl<TreeView>("BookmarkTree")!.IsVisible);
            var tree = window.FindControl<TreeView>("BookmarkTree")!;
            var row = (TreeViewItem)tree.ContainerFromItem(folder)!; var point = row.TranslatePoint(new Point(80, 12), window)!.Value;
            window.MouseDown(point, MouseButton.Left); window.MouseUp(point, MouseButton.Left); Dispatcher.UIThread.RunJobs();
            Assert.Same(folder, view.Navigation.Place); Assert.Same(folder, model.SelectedBookmark);
            // 原列表新建采用当前浏览目录，即使列表选中一个文件夹也创建同级节点。
            view.FindControl<Button>("BookmarkRootButton")!.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
            list.Focus(); list.SelectedItem = folder; Dispatcher.UIThread.RunJobs(); window.UpdateLayout();
            window.GetVisualDescendants().OfType<Button>().Single(button => button.Content as string == "新建").RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
            var dialog = Assert.Single(window.OwnedWindows, dialog => dialog.Title == "新建书签文件夹");
            dialog.GetVisualDescendants().OfType<TextBox>().Single().Text = "同级新目录";
            dialog.GetVisualDescendants().OfType<Button>().Single(button => button.Content as string == "确定").RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
            for (var i = 0; i < 60 && state.BookmarkRoot.Children!.Count == 1; i++) await Task.Delay(10, TestContext.Current.CancellationToken);
            Assert.Contains(state.BookmarkRoot.Children!, node => node.Name == "同级新目录"); Assert.DoesNotContain(folder.Children!, node => node.Name == "同级新目录");
            Dispatcher.UIThread.RunJobs(); window.UpdateLayout();
            var phase = Environment.GetEnvironmentVariable("NEEVIEW_ACCEPTANCE_PHASE") ?? "p2-bookmark-navigation";
            var output = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, $"../../../../../acceptance/{phase}-bookmark-navigation-layout.png"));
            Directory.CreateDirectory(Path.GetDirectoryName(output)!);
            using var bitmap = new RenderTargetBitmap(new PixelSize(1200, 800)); bitmap.Render(window); bitmap.Save(output, PngBitmapEncoderOptions.Default);
            Assert.True(new FileInfo(output).Length > 1000);
        }
        finally { await window.PrepareShutdownAsync(); window.Close(); }
    }

    /// <summary>真实排序控件的保存失败回滚配置、选择与文件；解除错误后可继续保存。</summary>
    [AvaloniaFact]
    public async Task ActualSortFailureRestoresSettingsAndCanRetry()
    {
        using var fixture = new Fixture(); var state = new SaveData(fixture.State); await state.LoadAsync(TestContext.Current.CancellationToken);
        var node = await state.AddBookmarkFolderAsync(null, "漫画", TestContext.Current.CancellationToken); await state.SaveAsync(null, 0, TestContext.Current.CancellationToken);
        var setting = Path.Combine(fixture.State, "UserSetting.json"); var before = await File.ReadAllTextAsync(setting, TestContext.Current.CancellationToken);
        var view = new BookmarkListView(); view.Attach(state); view.SaveSettingsAsync = () => state.SaveAsync(null, 0);
        var window = new Window { Content = view, Width = 360, Height = 500 }; window.Show(); Dispatcher.UIThread.RunJobs(); window.UpdateLayout();
        try
        {
            view.Reveal(node); var combo = view.FindControl<ComboBox>("BookmarkOrder")!; var failures = 0; view.Failed += (_, _) => failures++;
            Directory.CreateDirectory(setting + ".tmp");
            try
            {
                combo.SelectedItem = BookmarkListViewModel.Orders.Single(choice => choice.Mode == FolderOrder.FileNameDescending);
                for (var i = 0; i < 60 && failures == 0; i++) await Task.Delay(10, TestContext.Current.CancellationToken);
                Dispatcher.UIThread.RunJobs(); Assert.Equal(1, failures); Assert.Equal(FolderOrder.FileName, Config.Current.Bookmark.BookmarkFolderOrder);
                Assert.Equal(FolderOrder.FileName, ((BookmarkOrderChoice)combo.SelectedItem!).Mode); Assert.Same(node, Assert.Single(view.SelectedNodes));
                Assert.Equal(before, await File.ReadAllTextAsync(setting, TestContext.Current.CancellationToken));
            }
            finally { Directory.Delete(setting + ".tmp"); }
            combo.SelectedItem = BookmarkListViewModel.Orders.Single(choice => choice.Mode == FolderOrder.EntryTime);
            await view.PrepareCloseAsync();
            Assert.Equal(FolderOrder.EntryTime, Config.Current.Bookmark.BookmarkFolderOrder);
            Assert.Equal((int)FolderOrder.EntryTime, JsonNode.Parse(await File.ReadAllTextAsync(setting, TestContext.Current.CancellationToken))!["Config"]!["Bookmark"]!["BookmarkFolderOrder"]!.GetValue<int>());
        }
        finally { view.Dispose(); window.Close(); }
    }

    /// <summary>关闭等待已开始的原打开链，并拒绝关闭期间的新列表动作。</summary>
    [AvaloniaFact]
    public async Task ClosingWaitsForPendingOpenWithoutAcceptingAnotherInput()
    {
        using var fixture = new Fixture(); var state = new SaveData(fixture.State); await state.LoadAsync(TestContext.Current.CancellationToken);
        var node = new BookmarkNode { Path = fixture.Zip }; state.BookmarkRoot.Children!.Add(node);
        var view = new BookmarkListView(); view.Attach(state); var window = new Window { Content = view, Width = 320, Height = 500 }; window.Show();
        var finish = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously); var calls = 0;
        view.OpenBookAsync = _ => { calls++; return finish.Task; }; view.Reveal(node);
        try
        {
            var open = view.OpenSelectedAsync(); var close = view.PrepareCloseAsync(); Assert.False(close.IsCompleted);
            await view.OpenSelectedAsync(); Assert.Equal(1, calls); finish.SetResult(); await open; await close;
            view.CancelClose(); view.OpenBookAsync = _ => { calls++; return Task.CompletedTask; }; await view.OpenSelectedAsync(); Assert.Equal(2, calls);
        }
        finally { finish.TrySetResult(); await view.PrepareCloseAsync(); view.Dispose(); window.Close(); }
    }

    /// <summary>后台正式窗口测试不模拟 Finder/废纸篓成功。</summary>
    private sealed class TestPlatform : IPlatformService
    {
        public Task RevealAsync(string path, CancellationToken token = default) => throw new NotSupportedException();
        public Task TrashAsync(string path, CancellationToken token = default) => throw new NotSupportedException();
    }
}
