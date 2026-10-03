using System.Text.Json.Nodes;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using NeeView;
using NeeView.MacOS.Views;
using NeeView.MacOS.ViewModels;
namespace NeeView.Engine.Tests;

/// <summary>原书签结构化查询、唯一节点、持久化与正式输入隔离；不操作用户数据或真实窗口。</summary>
public sealed class BookmarkSearchTests
{
    /// <summary>原裸文本、引号、逻辑、假名归一和布尔属性使用固定搜索库，不另写 Contains 语义。</summary>
    [Theory]
    [InlineData("漫画1", "第一本")]
    [InlineData("/bookmark 漫画 /not 2", "第一本")]
    [InlineData("/exact あれ", "第一本")]
    [InlineData("/re ００１", "第一本")]
    [InlineData("/history", "第二本")]
    [InlineData("/bookmark", "第一本,第二本")]
    public async Task OriginalGrammarReturnsSameBookmarkNodes(string keyword, string expected)
    {
        Config.SetCurrent(new());
        var first = new BookmarkNode { Name = "漫画００１-あれ.cbz", Path = "/first.cbz" };
        var second = new BookmarkNode { Name = "漫画2-アレ.cbz", Path = "/second.cbz" };
        var folder = new BookmarkNode { Name = "漫画文件夹", Children = [first, second] };
        using var list = new BookmarkFolderList(new(new() { Children = [folder] }));
        Assert.True(await list.SearchAsync(keyword, [second.Path], token: TestContext.Current.CancellationToken));
        Assert.Equal(expected, string.Join(',', list.Items.Select(node => ReferenceEquals(node, first) ? "第一本" : "第二本")));
        Assert.All(list.Items, node => Assert.True(ReferenceEquals(node, first) || ReferenceEquals(node, second)));
    }

    /// <summary>递归开关、清空、同父索引排序与返回定位保留原目录范围，不改原树注册顺序。</summary>
    [Fact]
    public async Task RecursiveSearchUsesOriginalParentLocalIndexAndClearRestoresPlace()
    {
        Config.SetCurrent(new()); Config.Current.Bookmark.BookmarkFolderOrder = FolderOrder.EntryTime;
        var a0 = new BookmarkNode { Name = "A0", Path = "/a0" }; var a1 = new BookmarkNode { Name = "A1", Path = "/a1" };
        var b0 = new BookmarkNode { Name = "B0", Path = "/b0" };
        var a = new BookmarkNode { Name = "A", Children = [a0, a1] }; var b = new BookmarkNode { Name = "B", Children = [b0] };
        var root = new BookmarkNode { Children = [a, b] }; using var list = new BookmarkFolderList(new(root));
        await list.SearchAsync("/bookmark", [], token: TestContext.Current.CancellationToken); Assert.Equal(new[] { a0, b0, a1 }, list.Items);
        list.Select(b0); list.ChangeOrder(FolderOrder.EntryTimeDescending); Assert.Equal(new[] { a1, b0, a0 }, list.Items); Assert.Same(b0, list.SelectedItem);
        await list.SearchAsync("", [], token: TestContext.Current.CancellationToken); Assert.Equal(new[] { b, a }, list.Items); Assert.Same(root, list.Place);
        list.SetPlace(a); Config.Current.Bookmark.IsSearchIncludeSubdirectories = false;
        await list.SearchAsync("/bookmark", [], token: TestContext.Current.CancellationToken); Assert.Equal(new[] { a0, a1 }, list.Items);
        Assert.True(list.MoveToParent()); Assert.Same(root, list.Place); Assert.Same(a, list.SelectedItem); Assert.Equal("", list.SearchKeyword);
        Assert.Equal(FolderOrder.EntryTimeDescending, list.FolderOrder); Assert.Equal(new[] { b, a }, list.Items);
        Config.Current.Bookmark.IsSearchIncludeSubdirectories = false;
        await list.SearchAsync("/bookmark", [], token: TestContext.Current.CancellationToken); Assert.Empty(list.Items);
        Assert.Equal(new[] { a0, a1 }, a.Children);
    }

    /// <summary>未知选项、坏正则、权限/超时失败保留已提交结果与同一选择，不回退全量伪成功。</summary>
    [Fact]
    public async Task InvalidQueryAndMetadataFailureKeepPreviousValidResult()
    {
        Config.SetCurrent(new()); var node = new BookmarkNode { Name = "旧结果", Path = "/missing" };
        using var list = new BookmarkFolderList(new(new() { Children = [node] }));
        await list.SearchAsync("旧结果", [], token: TestContext.Current.CancellationToken); list.Select(node);
        await Assert.ThrowsAnyAsync<Exception>(() => list.SearchAsync("/unknown value", [], token: TestContext.Current.CancellationToken));
        await Assert.ThrowsAnyAsync<Exception>(() => list.SearchAsync("/re [", [], token: TestContext.Current.CancellationToken));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => list.SearchAsync("/size /gt 1", [], (_, _) => throw new UnauthorizedAccessException(), TestContext.Current.CancellationToken));
        Assert.Same(node, Assert.Single(list.Items)); Assert.Same(node, list.SelectedItem); Assert.Equal("旧结果", list.SearchKeyword);
    }

    /// <summary>日期/大小查真实来源，普通名称不探测，重复别名只读一次；文件夹沿原 EntryTime/-1。</summary>
    [Fact]
    public async Task MetadataPredicatesUseBackendAndDeduplicatePaths()
    {
        using var fixture = new Fixture(); Config.SetCurrent(new());
        var time = new DateTime(2024, 1, 2, 12, 0, 0); File.SetLastWriteTime(fixture.Zip, time);
        var first = new BookmarkNode { Name = "别名一", Path = fixture.Zip }; var alias = new BookmarkNode { Name = "别名二", Path = fixture.Zip };
        var folder = new BookmarkNode { Name = "目录", EntryTime = time, Children = [first, alias] };
        using var list = new BookmarkFolderList(new(new() { Children = [folder] }));
        var backend = new NeeView.Backends.ArchiveFactory(); var calls = 0;
        async Task<FolderItem?> Metadata(string path, CancellationToken token) { calls++; return await backend.GetFileMetadataAsync(path, token); }
        await list.SearchAsync("别名", [], Metadata, TestContext.Current.CancellationToken); Assert.Equal(0, calls);
        await list.SearchAsync("/size /gt 1", [], Metadata, TestContext.Current.CancellationToken); Assert.Equal(1, calls); Assert.Equal(new[] { first, alias }, list.Items);
        await list.SearchAsync("/since 2024-01-01 /until 2024-01-03", [], Metadata, TestContext.Current.CancellationToken); Assert.Equal(2, calls); Assert.Equal(3, list.Items.Count);
        Assert.Null(await backend.GetFileMetadataAsync(Path.Combine(fixture.Root, "missing.cbz"), TestContext.Current.CancellationToken));
        var info = await backend.GetFileMetadataAsync(fixture.Images, TestContext.Current.CancellationToken); Assert.True(info!.IsDirectory); Assert.Equal(-1, info.Length);
    }

    /// <summary>旧元数据调用忽略取消仍不能覆盖新查询或导航/已关闭面板。</summary>
    [Fact]
    public async Task LateSearchCannotOverwriteNewQueryNavigationOrDisposal()
    {
        Config.SetCurrent(new()); var node = new BookmarkNode { Name = "当前", Path = "/book" };
        var root = new BookmarkNode { Children = [node] }; using var list = new BookmarkFolderList(new(root));
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var finish = new TaskCompletionSource<FolderItem?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var old = list.SearchAsync("/size /gt 1", [], (_, _) => { started.SetResult(); return finish.Task; }, TestContext.Current.CancellationToken); await started.Task;
        Assert.True(await list.SearchAsync("当前", [], token: TestContext.Current.CancellationToken));
        finish.SetResult(new("book", "/book", false, 50)); Assert.False(await old); Assert.Equal("当前", list.SearchKeyword);
        var wait = new TaskCompletionSource<FolderItem?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var started2 = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var closing = list.SearchAsync("/size /gt 1", [], (_, _) => { started2.SetResult(); return wait.Task; }, TestContext.Current.CancellationToken); await started2.Task;
        list.Dispose(); wait.SetResult(null); Assert.False(await closing); Assert.Same(node, Assert.Single(list.Items));
    }

    /// <summary>原历史去重前置、上限/零、删除、重启及未知字段，保存失败恢复可重试集合。</summary>
    [Fact]
    public async Task SearchHistoryIsOriginalJsonAndRollsBackFailure()
    {
        using var fixture = new Fixture(); Directory.CreateDirectory(fixture.State);
        await File.WriteAllTextAsync(Path.Combine(fixture.State, "History.json"), "{\"BookmarkSearchHistory\":[\"旧\"],\"BookshelfSearchHistory\":[\"保留\"],\"Future\":7}", TestContext.Current.CancellationToken);
        var state = new SaveData(fixture.State); await state.LoadAsync(TestContext.Current.CancellationToken); Config.Current.System.SearchHistorySize = 2;
        await state.EditBookmarkSearchHistoryAsync("新", token: TestContext.Current.CancellationToken); await state.EditBookmarkSearchHistoryAsync("旧", token: TestContext.Current.CancellationToken); Assert.Equal(new[] { "旧", "新" }, state.BookmarkSearchHistory);
        await state.EditBookmarkSearchHistoryAsync("第三", token: TestContext.Current.CancellationToken); Assert.Equal(new[] { "第三", "旧" }, state.BookmarkSearchHistory);
        var reload = new SaveData(fixture.State); await reload.LoadAsync(TestContext.Current.CancellationToken); Assert.Equal(state.BookmarkSearchHistory, reload.BookmarkSearchHistory);
        Directory.CreateDirectory(Path.Combine(fixture.State, "History.json.tmp"));
        try { await Assert.ThrowsAnyAsync<Exception>(() => state.EditBookmarkSearchHistoryAsync("失败", token: TestContext.Current.CancellationToken)); }
        finally { Directory.Delete(Path.Combine(fixture.State, "History.json.tmp")); }
        Assert.Equal(new[] { "第三", "旧" }, state.BookmarkSearchHistory); await state.EditBookmarkSearchHistoryAsync("第三", remove: true, token: TestContext.Current.CancellationToken); Assert.Equal(new[] { "旧" }, state.BookmarkSearchHistory);
        Config.Current.History.IsKeepSearchHistory = false; await state.SaveAsync(null, 0, TestContext.Current.CancellationToken); var json = JsonNode.Parse(await File.ReadAllTextAsync(Path.Combine(fixture.State, "History.json"), TestContext.Current.CancellationToken))!;
        Assert.Null(json["BookmarkSearchHistory"]); Assert.Equal("保留", json["BookshelfSearchHistory"]![0]!.GetValue<string>()); Assert.Equal(7, json["Future"]!.GetValue<int>());
        Config.Current.System.SearchHistorySize = 0; await state.EditBookmarkSearchHistoryAsync("不保留", token: TestContext.Current.CancellationToken); Assert.Empty(state.BookmarkSearchHistory);
    }

    /// <summary>正式搜索框 Enter/数字/Delete/Backspace 属于文本；查询不打开旧项或重解码正文。</summary>
    [AvaloniaFact]
    public async Task FormalSearchTextIsolatedAndEditingKeepsOriginalQueryNodes()
    {
        using var fixture = new Fixture(); var state = new SaveData(fixture.State); await state.LoadAsync(TestContext.Current.CancellationToken);
        var folder = await state.AddBookmarkFolderAsync(null, "收藏", TestContext.Current.CancellationToken);
        var operation = fixture.Operation(state); await operation.OpenAsync(fixture.Images, TestContext.Current.CancellationToken);
        var node = await state.RegisterBookmarkAsync(operation.Book!, folder, TestContext.Current.CancellationToken);
        await state.RenameBookmarkAsync(node, "漫画示例", token: TestContext.Current.CancellationToken);
        var model = new ReaderWorkspaceViewModel(operation, new(operation), state); var images = new BitmapFactory(new NeeView.Backends.MagickImageDecoder());
        var window = new MainWindow(); window.Bind(model, images, new TestPlatform()); window.Show();
        try
        {
            model.ShowPanel("BookmarkPanel"); Dispatcher.UIThread.RunJobs(); window.UpdateLayout();
            var view = window.FindControl<BookmarkListView>("BookmarkPanelList")!; var input = view.FindControl<TextBox>("BookmarkSearchBox")!;
            Assert.True(window.IsCommandAvailable("FocusBookmarkSearchBox")); view.FocusSearch(); Assert.True(input.IsKeyboardFocusWithin);
            Config.Current.System.IsIncrementalSearchEnabled = false; input.Text = "漫画"; Assert.Same(folder, Assert.Single(view.Navigation!.Items));
            var refreshes = 0; model.Refreshed += (_, _) => refreshes++;
            window.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null);
            await WaitUntilAsync(() => state.BookmarkSearchHistory.Contains("漫画") && view.Navigation.SearchKeyword == "漫画"); Assert.Same(node, Assert.Single(view.Navigation.Items)); Assert.Equal(0, refreshes);
            window.KeyPress(Key.Delete, RawInputModifiers.None, PhysicalKey.Delete, null); window.KeyPress(Key.Back, RawInputModifiers.None, PhysicalKey.Backspace, null);
            Assert.Contains(node, state.BookmarkRoot.Walk()); Assert.Same(state.BookmarkRoot, view.Navigation.Place); Assert.Equal(0, operation.Book!.CurrentPage!.Index);
            input.Text = "/bad expression"; await view.SearchAsync(); Assert.Same(node, Assert.Single(view.Navigation.Items)); Assert.Single(state.BookmarkSearchHistory);
            input.Text = "漫画"; await view.SearchAsync(); await state.RenameBookmarkAsync(node, "漫画新版", token: TestContext.Current.CancellationToken);
            view.Reveal(node); Dispatcher.UIThread.RunJobs(); await view.SearchAsync(false);
            Assert.Equal("漫画", view.Navigation.SearchKeyword); Assert.Same(node, Assert.Single(view.Navigation.Items));
            await view.SetSearchOptionAsync("recursive", false); await view.SearchAsync(false); Assert.Empty(view.Navigation.Items);
            await view.SetSearchOptionAsync("recursive", true); await view.SearchAsync(false); Assert.Same(node, Assert.Single(view.Navigation.Items));
            window.UpdateLayout();
            var phase = Environment.GetEnvironmentVariable("NEEVIEW_ACCEPTANCE_PHASE") ?? "p2-bookmark-search";
            var path = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, $"../../../../../acceptance/{phase}-bookmark-search-layout.png"));
            using var bitmap = new RenderTargetBitmap(new PixelSize(1200, 800)); bitmap.Render(window); bitmap.Save(path, PngBitmapEncoderOptions.Default);
            input.Text = ""; await view.SearchAsync(); Assert.Same(folder, Assert.Single(view.Navigation.Items));
        }
        finally { await window.PrepareShutdownAsync(); window.Close(); }
    }

    /// <summary>设置失败原地回滚；关闭取消未确认搜索并等待晚到请求，保存失败后的恢复入口可重试。</summary>
    [AvaloniaFact]
    public async Task SearchSettingsRollbackAndClosingRejectsLateResults()
    {
        using var fixture = new Fixture(); var state = new SaveData(fixture.State); await state.LoadAsync(TestContext.Current.CancellationToken);
        var node = new BookmarkNode { Name = "原项", Path = fixture.Zip }; state.BookmarkRoot.Children!.Add(node);
        var finish = new TaskCompletionSource<FolderItem?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var view = new BookmarkListView { ReadMetadataAsync = (_, _) => { started.TrySetResult(); return finish.Task; } }; view.Attach(state);
        var window = new Window { Width = 320, Height = 500, Content = view }; window.Show();
        try
        {
            view.SaveSettingsAsync = () => Task.FromException(new IOException("保存失败")); await view.SetSearchOptionAsync("recursive", false);
            Assert.True(Config.Current.Bookmark.IsSearchIncludeSubdirectories);
            view.SaveSettingsAsync = () => state.SaveAsync(null, 0, TestContext.Current.CancellationToken); await view.SetSearchOptionAsync("recursive", false); Assert.False(Config.Current.Bookmark.IsSearchIncludeSubdirectories);
            Config.Current.System.IsIncrementalSearchEnabled = false; view.FindControl<TextBox>("BookmarkSearchBox")!.Text = "/size /gt 1";
            var search = view.SearchAsync(false); await started.Task; var closing = view.PrepareCloseAsync(); Assert.False(closing.IsCompleted);
            finish.SetResult(new("漫画", fixture.Zip, false, 50)); await search; await closing;
            Assert.Equal("", view.Navigation!.SearchKeyword); Assert.Empty(state.BookmarkSearchHistory); Assert.Same(node, Assert.Single(view.Navigation.Items));
            view.CancelClose(); view.FindControl<TextBox>("BookmarkSearchBox")!.Text = "原项"; await view.SearchAsync(); Assert.Equal("原项", view.Navigation.SearchKeyword);
        }
        finally { finish.TrySetResult(null); await view.PrepareCloseAsync(); view.Dispose(); window.Close(); }
    }

    /// <summary>增量更新和节点编辑自动重筛；无效草稿保留，未确认输入不写历史，/history只依赖书籍记录。</summary>
    [AvaloniaFact]
    public async Task IncrementalRefreshPreservesDraftAndTracksRealHistoryMembership()
    {
        using var fixture = new Fixture(); var state = new SaveData(fixture.State); await state.LoadAsync(TestContext.Current.CancellationToken);
        var node = new BookmarkNode { Name = "漫画", Path = fixture.Images }; state.BookmarkRoot.Children!.Add(node);
        using var model = new BookmarkListViewModel(state);
        model.Keyword = "漫画"; await WaitUntilAsync(() => model.List.SearchKeyword == "漫画" && !model.IsSearching);
        Assert.Same(node, Assert.Single(model.Items)); Assert.Empty(state.BookmarkSearchHistory);
        model.Keyword = "/bad draft"; Assert.NotNull(model.SearchError);
        await state.RenameBookmarkAsync(node, "画集", token: TestContext.Current.CancellationToken); Dispatcher.UIThread.RunJobs();
        await WaitUntilAsync(() => !model.IsSearching); Assert.Equal("/bad draft", model.Keyword); Assert.NotNull(model.SearchError); Assert.Empty(model.Items);
        model.Keyword = "/history"; await model.SearchAsync(); Assert.Empty(model.Items);
        var operation = fixture.Operation(state); await operation.OpenAsync(fixture.Images, TestContext.Current.CancellationToken); await operation.SaveAsync();
        Dispatcher.UIThread.RunJobs(); await WaitUntilAsync(() => !model.IsSearching && model.Items.Count == 1); Assert.Same(node, model.Items[0]);
        await state.ClearHistoryAsync(TestContext.Current.CancellationToken); Dispatcher.UIThread.RunJobs(); await WaitUntilAsync(() => !model.IsSearching && model.Items.Count == 0);
        await model.PrepareCloseAsync(); await operation.DisposeAsync();
    }

    /// <summary>确认查询的历史保存失败不污染原历史，同词可重试，结果保持已成功筛选的原节点。</summary>
    [AvaloniaFact]
    public async Task ConfirmedHistoryFailureReportsAndAllowsSameKeywordRetry()
    {
        using var fixture = new Fixture(); var state = new SaveData(fixture.State); await state.LoadAsync(TestContext.Current.CancellationToken);
        var node = new BookmarkNode { Name = "漫画", Path = fixture.Zip }; state.BookmarkRoot.Children!.Add(node);
        using var model = new BookmarkListViewModel(state); Config.Current.System.IsIncrementalSearchEnabled = false;
        var errors = 0; model.Failed += (_, _) => errors++; model.Keyword = "漫画";
        Directory.CreateDirectory(Path.Combine(fixture.State, "History.json.tmp"));
        try { Assert.False(await model.SearchAsync(recordHistory: true)); }
        finally { Directory.Delete(Path.Combine(fixture.State, "History.json.tmp")); }
        Assert.Equal(1, errors); Assert.NotNull(model.SearchError); Assert.Empty(state.BookmarkSearchHistory); Assert.Same(node, Assert.Single(model.Items));
        Assert.True(await model.SearchAsync(recordHistory: true)); Assert.Null(model.SearchError); Assert.Equal(new[] { "漫画" }, state.BookmarkSearchHistory);
        await model.PrepareCloseAsync();
    }

    /// <summary>原确认语法有效即登记历史；慢查询晚到不能把用户已删除的表达式重新追加。</summary>
    [AvaloniaFact]
    public async Task ConfirmedQueryRecordsBeforeSlowFilteringAndDoesNotReaddDeletedHistory()
    {
        using var fixture = new Fixture(); var state = new SaveData(fixture.State); await state.LoadAsync(TestContext.Current.CancellationToken);
        state.BookmarkRoot.Children!.Add(new() { Name = "漫画", Path = fixture.Zip });
        var finish = new TaskCompletionSource<FolderItem?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var model = new BookmarkListViewModel(state) { ReadMetadataAsync = (_, _) => { started.TrySetResult(); return finish.Task; } };
        Config.Current.System.IsIncrementalSearchEnabled = false; model.Keyword = "/size /gt 1";
        var query = model.SearchAsync(recordHistory: true);
        try
        {
            await started.Task.WaitAsync(TimeSpan.FromSeconds(3), TestContext.Current.CancellationToken);
            Assert.Equal(new[] { "/size /gt 1" }, state.BookmarkSearchHistory);
            await model.RemoveHistoryAsync("/size /gt 1"); finish.SetResult(new("漫画", fixture.Zip, false, 50));
            Assert.True(await query); Assert.Empty(state.BookmarkSearchHistory); Assert.Single(model.Items);
        }
        finally { finish.TrySetResult(null); await model.PrepareCloseAsync(); }
    }

    /// <summary>新增字段沿原默认值和 JSON 分支合并，不覆盖尚未迁入的配置或差分命令。</summary>
    [Fact]
    public async Task SearchOptionsKeepOriginalDefaultsAndUnknownJsonFields()
    {
        using var fixture = new Fixture(); Directory.CreateDirectory(fixture.State);
        await File.WriteAllTextAsync(Path.Combine(fixture.State, "UserSetting.json"), "{\"Config\":{\"System\":{\"IsIncrementalSearchEnabled\":false,\"SearchHistorySize\":3,\"Future\":9},\"Bookmark\":{\"IsVisibleSearchBox\":false,\"IsSearchIncludeSubdirectories\":false,\"Future\":10}}}", TestContext.Current.CancellationToken);
        var state = new SaveData(fixture.State); await state.LoadAsync(TestContext.Current.CancellationToken);
        Assert.False(Config.Current.System.IsIncrementalSearchEnabled); Assert.Equal(3, Config.Current.System.SearchHistorySize);
        Assert.False(Config.Current.Bookmark.IsVisibleSearchBox); Assert.False(Config.Current.Bookmark.IsSearchIncludeSubdirectories); Assert.True(Config.Current.History.IsKeepSearchHistory);
        await state.SaveAsync(null, 0, TestContext.Current.CancellationToken);
        var raw = JsonNode.Parse(await File.ReadAllTextAsync(Path.Combine(fixture.State, "UserSetting.json"), TestContext.Current.CancellationToken))!;
        Assert.Equal(9, raw["Config"]!["System"]!["Future"]!.GetValue<int>()); Assert.Equal(10, raw["Config"]!["Bookmark"]!["Future"]!.GetValue<int>());
        Config.SetCurrent(new()); Assert.True(Config.Current.System.IsIncrementalSearchEnabled); Assert.Equal(8, Config.Current.System.SearchHistorySize);
        Assert.True(Config.Current.Bookmark.IsVisibleSearchBox); Assert.True(Config.Current.Bookmark.IsSearchIncludeSubdirectories);
    }

    /// <summary>等待正式异步输入完成，限时防止回归挂起；不向系统注入键鼠。</summary>
    private static async Task WaitUntilAsync(Func<bool> predicate)
    {
        for (var i = 0; i < 150 && !predicate(); i++) { Dispatcher.UIThread.RunJobs(); await Task.Delay(10, TestContext.Current.CancellationToken); }
        Assert.True(predicate());
    }
    /// <summary>后台夹具不虚构 Finder/废纸篓通过。</summary>
    private sealed class TestPlatform : IPlatformService
    {
        public Task RevealAsync(string path, CancellationToken token = default) => throw new NotSupportedException();
        public Task TrashAsync(string path, CancellationToken token = default) => throw new NotSupportedException();
    }
}
