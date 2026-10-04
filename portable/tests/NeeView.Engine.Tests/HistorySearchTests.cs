using System.Text.Json.Nodes;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using NeeView;
using NeeView.MacOS.ViewModels;
using NeeView.MacOS.Views;
namespace NeeView.Engine.Tests;

/// <summary>原历史查询属性、输入作用域和JSON事务；只使用临时夹具及Headless窗口。</summary>
public sealed class HistorySearchTests
{
    /// <summary>旧盘符/UNC逻辑定位保留原目录关系，Mac合法反斜杠名称不能被当分隔符。</summary>
    /// <param name="path">历史原定位符。</param><param name="parent">直接父级。</param><param name="name">书名。</param>
    [Theory]
    [InlineData(@"C:\Books\a.cbz\dir\x.jpg", @"C:\Books\a.cbz\dir", "x.jpg")]
    [InlineData(@"C:\Books/a.cbz", @"C:\Books", "a.cbz")]
    [InlineData(@"\\NAS\漫画\x.cbz", @"\\NAS\漫画", "x.cbz")]
    [InlineData(@"/漫画/合法\名字.cbz", "/漫画", @"合法\名字.cbz")]
    [InlineData("/漫画/目录/", "/漫画", "目录")]
    public void LogicalHistoryPathsDoNotRewriteMacFileNames(string path, string parent, string name)
    { Assert.Equal(parent, LoosePath.GetDirectoryName(path)); Assert.Equal(name, new HistoryEntry(path, null, default).Name); }

    /// <summary>名称而非路径匹配、访问日期、原布尔属性及逻辑，保持访问倒序。</summary>
    /// <param name="query">原表达式。</param><param name="expected">预期匹配的原记录名称。</param>
    [Theory]
    [InlineData("漫画1", "漫画００１-あれ.cbz")]
    [InlineData("library", "")]
    [InlineData("/exact あれ", "漫画００１-あれ.cbz")]
    [InlineData("/bookmark", "漫画００１-あれ.cbz")]
    [InlineData("/history /not 2", "漫画００１-あれ.cbz")]
    [InlineData("/since 2024-01-03", "漫画００１-あれ.cbz")]
    [InlineData("/until 2024-01-02", "漫画2-アレ.cbz")]
    public async Task OriginalHistoryPropertiesKeepTheirMeaning(string query, string expected)
    {
        using var fixture = new Fixture();
        await SeedAsync(fixture, ("/library/漫画００１-あれ.cbz", new DateTime(2024, 1, 3)), ("/library/漫画2-アレ.cbz", new DateTime(2024, 1, 2)));
        var state = new SaveData(fixture.State); await state.LoadAsync(TestContext.Current.CancellationToken);
        state.BookmarkRoot.Children!.Add(new() { Path = state.HistoryEntries[0].Path });
        using var list = new HistoryList(state); var calls = 0;
        Assert.True(await list.SearchAsync(query, (_, _) => { calls++; throw new InvalidOperationException("不应探测文件"); }, TestContext.Current.CancellationToken));
        Assert.Equal(expected, string.Join(',', list.GetViewItems().Select(entry => entry.Name))); Assert.Equal(0, calls);
        Assert.All(list.GetViewItems(), entry => Assert.Contains(entry, state.HistoryEntries));
    }

    /// <summary>大小用真实文件长度，目录/缺失为-1；目录范围先排除子目录且不改查询导航。</summary>
    [Fact]
    public async Task SizeUsesBackendWhileFolderFilterIsDirectParentOnly()
    {
        using var fixture = new Fixture(); var missing = Path.Combine(fixture.Root, "不存在.cbz"); var nested = Path.Combine(fixture.Images, "子目录.cbz");
        await SeedAsync(fixture, (fixture.Zip, DateTime.Today), (fixture.Images, DateTime.Today.AddDays(-1)), (missing, DateTime.Today.AddDays(-2)), (nested, DateTime.Today.AddDays(-3)));
        var state = new SaveData(fixture.State); await state.LoadAsync(TestContext.Current.CancellationToken);
        using var list = new HistoryList(state) { Address = fixture.Zip }; Config.Current.History.IsCurrentFolder = true;
        var backend = new NeeView.Backends.ArchiveFactory(); var calls = new List<string>();
        async Task<FolderItem?> Read(string path, CancellationToken token) { calls.Add(path); return await backend.GetFileMetadataAsync(path, token); }
        Assert.True(await list.SearchAsync("/size /gt 1", Read, TestContext.Current.CancellationToken)); Assert.Equal(fixture.Zip, Assert.Single(list.GetViewItems()).Path);
        Assert.DoesNotContain(nested, calls); Assert.Equal(3, calls.Count);
        await list.SearchAsync("/size /lt 0", Read, TestContext.Current.CancellationToken); Assert.Equal(new[] { fixture.Images, missing }, list.GetViewItems().Select(entry => entry.Path));
        Assert.Equal(fixture.Images, list.GetTarget(-1)!.Path); Assert.Null(list.GetTarget(1));
        Config.Current.History.IsCurrentFolder = false; await list.SearchAsync("", token: TestContext.Current.CancellationToken); Assert.Equal(4, list.GetViewItems().Count);
    }

    /// <summary>坏语法、坏正则与来源失败不覆盖旧结果；预取消不登记表达式。</summary>
    [Fact]
    public async Task ErrorsAndCancellationKeepCommittedFilter()
    {
        using var fixture = new Fixture(); await SeedAsync(fixture, ("/漫画.cbz", DateTime.Today), ("/画集.cbz", DateTime.Today));
        var state = new SaveData(fixture.State); await state.LoadAsync(TestContext.Current.CancellationToken); using var list = new HistoryList(state);
        await list.SearchAsync("漫画", token: TestContext.Current.CancellationToken);
        await Assert.ThrowsAnyAsync<Exception>(() => list.SearchAsync("/unknown value", token: TestContext.Current.CancellationToken));
        await Assert.ThrowsAnyAsync<Exception>(() => list.SearchAsync("/re [", token: TestContext.Current.CancellationToken));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => list.SearchAsync("/size /gt 1", (_, _) => throw new UnauthorizedAccessException(), TestContext.Current.CancellationToken));
        using var cancelled = new CancellationTokenSource(); cancelled.Cancel(); Assert.False(await list.SearchAsync("画集", token: cancelled.Token));
        Assert.Equal("漫画", list.SearchKeyword); Assert.Equal("漫画.cbz", Assert.Single(list.GetViewItems()).Name);
    }

    /// <summary>来源忽略取消的旧请求不能覆盖新查询、目录变化或已关闭内核。</summary>
    [Fact]
    public async Task LateMetadataCannotReplaceNewQueryEnvironmentOrDisposal()
    {
        using var fixture = new Fixture(); await SeedAsync(fixture, ("/a/漫画.cbz", DateTime.Today));
        var state = new SaveData(fixture.State); await state.LoadAsync(TestContext.Current.CancellationToken); using var list = new HistoryList(state) { Address = "/a/漫画.cbz" };
        var finish = new TaskCompletionSource<FolderItem?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var old = list.SearchAsync("/size /gt 1", (_, _) => { started.TrySetResult(); return finish.Task; }, TestContext.Current.CancellationToken); await started.Task.WaitAsync(TimeSpan.FromSeconds(3), TestContext.Current.CancellationToken);
        await list.SearchAsync("漫画", token: TestContext.Current.CancellationToken); finish.SetResult(new("漫画", "/a/漫画.cbz", false, 50)); Assert.False(await old); Assert.Equal("漫画", list.SearchKeyword);
        finish = new(TaskCreationOptions.RunContinuationsAsynchronously); started = new(TaskCreationOptions.RunContinuationsAsynchronously);
        Config.Current.History.IsCurrentFolder = true;
        var changing = list.SearchAsync("/size /gt 1", (_, _) => { started.TrySetResult(); return finish.Task; }, TestContext.Current.CancellationToken); await started.Task.WaitAsync(TimeSpan.FromSeconds(3), TestContext.Current.CancellationToken);
        list.Address = "/b/other.cbz"; finish.SetResult(new("漫画", "/a/漫画.cbz", false, 50)); Assert.False(await changing); Assert.Equal("漫画", list.SearchKeyword);
        Config.Current.History.IsCurrentFolder = false;
        finish = new(TaskCreationOptions.RunContinuationsAsynchronously); started = new(TaskCreationOptions.RunContinuationsAsynchronously);
        var closing = list.SearchAsync("/size /gt 1", (_, _) => { started.TrySetResult(); return finish.Task; }, TestContext.Current.CancellationToken); await started.Task.WaitAsync(TimeSpan.FromSeconds(3), TestContext.Current.CancellationToken);
        list.Dispose(); finish.SetResult(null); Assert.False(await closing);
    }

    /// <summary>原历史字段、模块隔离、容量及失败回滚；保存开关统一控制已迁四类历史，未知字段仍保留。</summary>
    [Fact]
    public async Task OriginalExpressionHistoryPersistsIndependentlyAndRollsBack()
    {
        using var fixture = new Fixture(); await SeedAsync(fixture);
        var state = new SaveData(fixture.State); await state.LoadAsync(TestContext.Current.CancellationToken); Config.Current.System.SearchHistorySize = 2;
        var original = state.BookHistorySearchHistory;
        await state.EditBookHistorySearchHistoryAsync("新", token: TestContext.Current.CancellationToken); await state.EditBookHistorySearchHistoryAsync("旧", token: TestContext.Current.CancellationToken); await state.EditBookHistorySearchHistoryAsync("新", token: TestContext.Current.CancellationToken);
        Assert.Equal(new[] { "新", "旧" }, original); Assert.Equal(new[] { "书签旧" }, state.BookmarkSearchHistory);
        var blocked = Path.Combine(fixture.State, "History.json.tmp"); Directory.CreateDirectory(blocked);
        try { await Assert.ThrowsAnyAsync<Exception>(() => state.EditBookHistorySearchHistoryAsync("失败", token: TestContext.Current.CancellationToken)); }
        finally { Directory.Delete(blocked); }
        Assert.Same(original, state.BookHistorySearchHistory); Assert.Equal(new[] { "新", "旧" }, original);
        await state.EditBookHistorySearchHistoryAsync("新", remove: true, token: TestContext.Current.CancellationToken); Assert.Equal(new[] { "旧" }, original);
        var reload = new SaveData(fixture.State); await reload.LoadAsync(TestContext.Current.CancellationToken); Assert.Equal(original, reload.BookHistorySearchHistory);
        Config.Current.History.IsKeepSearchHistory = false; await state.SaveAsync(null, TestContext.Current.CancellationToken);
        var raw = JsonNode.Parse(await File.ReadAllTextAsync(Path.Combine(fixture.State, "History.json"), TestContext.Current.CancellationToken))!;
        Assert.Null(raw["BookHistorySearchHistory"]); Assert.Null(raw["BookmarkSearchHistory"]); Assert.Null(raw["BookshelfSearchHistory"]); Assert.Equal(9, raw["Future"]!.GetValue<int>());
        Config.Current.System.SearchHistorySize = 0; await state.EditBookHistorySearchHistoryAsync("零容量", token: TestContext.Current.CancellationToken); Assert.Empty(original);
    }

    /// <summary>原500ms增量不登记；手动输入、无效草稿和环境重筛保持不同的提交含义。</summary>
    [AvaloniaFact]
    public async Task InputAndEnvironmentUseOriginalHistoryMeaning()
    {
        using var fixture = new Fixture(); await SeedAsync(fixture, ("/漫画.cbz", DateTime.Today), ("/画集.cbz", DateTime.Today));
        var state = new SaveData(fixture.State); await state.LoadAsync(TestContext.Current.CancellationToken); using var list = new HistoryList(state); using var model = new HistorySearchViewModel(state, list);
        model.Keyword = " 漫画 "; Assert.Equal("", list.SearchKeyword); await WaitUntilAsync(() => !model.IsSearching); Assert.Equal("漫画", list.SearchKeyword);
        Assert.Equal(new[] { "旧" }, state.BookHistorySearchHistory);
        Config.Current.System.IsIncrementalSearchEnabled = false; model.Keyword = "画集"; Assert.Equal("漫画", list.SearchKeyword);
        Assert.True(await model.SearchAsync()); Assert.Equal("画集", list.SearchKeyword); Assert.Equal("画集", model.History[0]);
        model.Keyword = "/unknown 草稿"; Assert.NotNull(model.Error);
        await state.RemoveHistoryAsync(["/画集.cbz"], TestContext.Current.CancellationToken); model.RefreshEnvironment(); await WaitUntilAsync(() => !model.IsSearching);
        Assert.Equal("/unknown 草稿", model.Keyword); Assert.NotNull(model.Error); Assert.Empty(list.GetViewItems());
        model.Keyword = "/bookmark"; await model.SearchAsync(false); Assert.Empty(list.GetViewItems());
        state.BookmarkRoot.Children!.Add(new() { Path = "/漫画.cbz" }); model.RefreshEnvironment(); await WaitUntilAsync(() => !model.IsSearching);
        Assert.Equal("漫画.cbz", Assert.Single(list.GetViewItems()).Name);
        await model.PrepareCloseAsync();
    }

    /// <summary>确认保存先于慢筛选，删除后晚到不重新追加；关闭取消未提交的显示需求。</summary>
    [AvaloniaFact]
    public async Task ConfirmationAndCloseObserveHistoryAndLateIo()
    {
        using var fixture = new Fixture(); await SeedAsync(fixture, ("/漫画.cbz", DateTime.Today));
        var state = new SaveData(fixture.State); await state.LoadAsync(TestContext.Current.CancellationToken); using var list = new HistoryList(state);
        var finish = new TaskCompletionSource<FolderItem?>(TaskCreationOptions.RunContinuationsAsynchronously); var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var model = new HistorySearchViewModel(state, list) { ReadMetadataAsync = (_, _) => { started.TrySetResult(); return finish.Task; } };
        Config.Current.System.IsIncrementalSearchEnabled = false; model.Keyword = "/size /gt 1"; var searching = model.SearchAsync();
        try
        {
            await started.Task.WaitAsync(TimeSpan.FromSeconds(3), TestContext.Current.CancellationToken); Assert.Equal("/size /gt 1", state.BookHistorySearchHistory[0]);
            await model.RemoveHistoryAsync("/size /gt 1"); var close = model.PrepareCloseAsync(); Assert.False(close.IsCompleted);
            finish.SetResult(new("漫画", "/漫画.cbz", false, 50)); Assert.False(await searching); await close;
            Assert.DoesNotContain("/size /gt 1", model.History); Assert.Equal("", list.SearchKeyword); Assert.Single(list.GetViewItems());
            model.CancelClose(); model.Keyword = "漫画"; Assert.True(await model.SearchAsync());
        }
        finally { finish.TrySetResult(null); await model.PrepareCloseAsync(); }
    }

    /// <summary>保存失败单独提示、回滚并允许同词重试，不丢已成功的查询结果。</summary>
    [AvaloniaFact]
    public async Task ConfirmedSaveFailureAllowsSameInputRetry()
    {
        using var fixture = new Fixture(); await SeedAsync(fixture, ("/漫画.cbz", DateTime.Today));
        var state = new SaveData(fixture.State); await state.LoadAsync(TestContext.Current.CancellationToken); using var list = new HistoryList(state); using var model = new HistorySearchViewModel(state, list);
        Config.Current.System.IsIncrementalSearchEnabled = false; model.Keyword = "漫画"; var blocked = Path.Combine(fixture.State, "History.json.tmp"); Directory.CreateDirectory(blocked);
        try { Assert.False(await model.SearchAsync()); }
        finally { Directory.Delete(blocked); }
        Assert.NotNull(model.Error); Assert.Equal(new[] { "旧" }, model.History); Assert.Single(list.GetViewItems());
        Assert.True(await model.SearchAsync()); Assert.Null(model.Error); Assert.Equal("漫画", model.History[0]); await model.PrepareCloseAsync();
    }

    /// <summary>关闭增量只取消未确认输入，保存草稿等待Enter；不把配置变化当确认搜索。</summary>
    [AvaloniaFact]
    public async Task DisablingIncrementalCancelsPendingDraftWithoutDiscardingText()
    {
        using var fixture = new Fixture(); await SeedAsync(fixture, ("/漫画.cbz", DateTime.Today));
        var state = new SaveData(fixture.State); await state.LoadAsync(TestContext.Current.CancellationToken); using var list = new HistoryList(state); using var model = new HistorySearchViewModel(state, list);
        model.Keyword = "漫画"; Assert.True(model.IsSearching);
        Config.Current.System.IsIncrementalSearchEnabled = false; model.RefreshEnvironment(); await model.PrepareCloseAsync();
        Assert.Equal("漫画", model.Keyword); Assert.Equal("", list.SearchKeyword); Assert.Equal(new[] { "旧" }, model.History);
        model.CancelClose(); Assert.True(await model.SearchAsync()); Assert.Equal("漫画", list.SearchKeyword);
    }

    /// <summary>历史更多菜单写入失败恢复开关，修正后同菜单可重试；不重建正文。</summary>
    [AvaloniaFact]
    public async Task SearchMenuSaveFailureRestoresRuntimeOption()
    {
        using var fixture = new Fixture(); var state = new SaveData(fixture.State); await state.LoadAsync(TestContext.Current.CancellationToken);
        var operation = fixture.Operation(state); var model = new ReaderWorkspaceViewModel(operation, new CommandTable(operation), state); var window = new MainWindow();
        window.Bind(model, new BitmapFactory(new NeeView.Backends.MagickImageDecoder()), new TestPlatform()); window.Show();
        try
        {
            await window.ExecuteAsync("FocusHistorySearchBox"); var more = window.FindControl<Button>("HistoryMoreButton")!;
            more.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            var menu = more.ContextMenu!; var option = menu.Items.OfType<MenuItem>().Single(item => item.Header?.ToString() == "保存搜索历史");
            var blocked = Path.Combine(fixture.State, "UserSetting.json.tmp"); Directory.CreateDirectory(blocked);
            try { option.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent)); await WaitUntilAsync(() => Config.Current.History.IsKeepSearchHistory); }
            finally { Directory.Delete(blocked); menu.Close(); }
            Assert.True(Config.Current.History.IsKeepSearchHistory);
            more.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); menu = more.ContextMenu!;
            option = menu.Items.OfType<MenuItem>().Single(item => item.Header?.ToString() == "保存搜索历史"); option.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            await WaitUntilAsync(() => !Config.Current.History.IsKeepSearchHistory && File.Exists(Path.Combine(fixture.State, "UserSetting.json"))); menu.Close();
            var raw = JsonNode.Parse(await File.ReadAllTextAsync(Path.Combine(fixture.State, "UserSetting.json"), TestContext.Current.CancellationToken))!;
            Assert.False(raw["Config"]!["History"]!["IsKeepSearchHistory"]!.GetValue<bool>());
        }
        finally { await window.PrepareShutdownAsync(); window.Close(); }
    }

    /// <summary>正式XAML的确认/失焦、聚焦全选及菜单删除只操作搜索，不翻页或删除访问记录。</summary>
    [AvaloniaFact]
    public async Task FormalHistorySearchUsesOriginalTextScopeAndSeparatePresentation()
    {
        using var fixture = new Fixture(); await SeedAsync(fixture, (fixture.Images, DateTime.Today), (fixture.Zip, DateTime.Today.AddDays(-1)));
        var state = new SaveData(fixture.State); await state.LoadAsync(TestContext.Current.CancellationToken); var operation = fixture.Operation(state);
        var model = new ReaderWorkspaceViewModel(operation, new CommandTable(operation), state); var window = new MainWindow();
        window.Bind(model, new BitmapFactory(new NeeView.Backends.MagickImageDecoder()), new TestPlatform()); window.Show();
        try
        {
            await window.OpenHistoryAsync(fixture.Images); await window.ExecuteAsync("FocusHistorySearchBox"); Dispatcher.UIThread.RunJobs(); window.UpdateLayout();
            Config.Current.System.IsIncrementalSearchEnabled = false; var box = window.FindControl<TextBox>("HistorySearchBox")!;
            var book = operation.Book; var refreshes = 0; model.Refreshed += (_, _) => refreshes++;
            box.Text = "/history /not 不存在"; box.Focus(); window.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null); window.KeyRelease(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null);
            await WaitUntilAsync(() => operation.HistoryList.SearchKeyword == "/history /not 不存在" && !model.HistorySearch.IsSearching);
            Assert.Same(book, operation.Book); Assert.Equal(0, refreshes); Assert.Equal(2, state.HistoryEntries.Count);
            box.Text = "/unknown draft"; await window.ExecuteAsync("FocusHistorySearchBox"); Assert.Equal(box.Text!.Length, box.SelectionEnd - box.SelectionStart);
            window.KeyPress(Key.Delete, RawInputModifiers.None, PhysicalKey.Delete, null); window.KeyRelease(Key.Delete, RawInputModifiers.None, PhysicalKey.Delete, null); Assert.Equal(2, state.HistoryEntries.Count);
            box.Text = "/bookmark"; window.Viewer.Focus(); await WaitUntilAsync(() => operation.HistoryList.SearchKeyword == "/bookmark" && !model.HistorySearch.IsSearching);
            Assert.Empty(model.History); Assert.Equal("/bookmark", state.BookHistorySearchHistory[0]);
            await window.ExecuteAsync("FocusHistorySearchBox"); box.Text = "/history"; await window.SearchHistoryAsync(); Dispatcher.UIThread.RunJobs(); window.UpdateLayout();
            var historyButton = window.FindControl<Button>("HistorySearchHistoryButton")!; historyButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Assert.NotEmpty(historyButton.ContextMenu!.Items);
            historyButton.ContextMenu.Close(); Assert.Equal(0, refreshes);
            var phase = Environment.GetEnvironmentVariable("NEEVIEW_ACCEPTANCE_PHASE") ?? "p2-history-search";
            var output = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, $"../../../../../acceptance/{phase}-history-search-layout.png"));
            using var screenshot = new RenderTargetBitmap(new PixelSize(1200, 800)); screenshot.Render(window); screenshot.Save(output, PngBitmapEncoderOptions.Default);
        }
        finally { await window.PrepareShutdownAsync(); window.Close(); }
    }

    /// <summary>写入固定原历史结构、独立两组搜索历史及未来字段，不访问产品数据。</summary>
    private static async Task SeedAsync(Fixture fixture, params (string Path, DateTime Time)[] entries)
    {
        Directory.CreateDirectory(fixture.State);
        var items = new JsonArray(entries.Select(entry => (JsonNode)new JsonObject { ["Path"] = entry.Path, ["LastAccessTime"] = entry.Time, ["Page"] = "001.png", ["FutureItem"] = 8 }).ToArray());
        var raw = new JsonObject { ["Items"] = items, ["BookHistorySearchHistory"] = new JsonArray("旧"), ["BookmarkSearchHistory"] = new JsonArray("书签旧"), ["BookshelfSearchHistory"] = new JsonArray("书架未知"), ["Future"] = 9 };
        await File.WriteAllTextAsync(Path.Combine(fixture.State, "History.json"), raw.ToJsonString(), TestContext.Current.CancellationToken);
    }
    /// <summary>限定等待正式异步绑定/查询完成，不发送真实系统键鼠。</summary>
    private static async Task WaitUntilAsync(Func<bool> predicate)
    {
        for (var i = 0; i < 200 && !predicate(); i++) { Dispatcher.UIThread.RunJobs(); await Task.Delay(10, TestContext.Current.CancellationToken); }
        Dispatcher.UIThread.RunJobs(); Assert.True(predicate());
    }
    /// <summary>不模拟未验收的Finder/废纸篓。</summary>
    private sealed class TestPlatform : IPlatformService
    {
        /// <summary>本批不执行Finder。</summary>
        public Task RevealAsync(string path, CancellationToken token = default) => throw new NotSupportedException();
        /// <summary>本批不执行废纸篓。</summary>
        public Task TrashAsync(string path, CancellationToken token = default) => throw new NotSupportedException();
    }
}
