using System.Text.Json.Nodes;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Media.Imaging;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using NeeView;
using NeeView.MacOS.Views;
using NeeView.MacOS.ViewModels;

namespace NeeView.Engine.Tests;

public sealed class HistoryListTests
{
    /// <summary>核对原过滤后前后规则：直接父目录、缺失当前项、首尾和文字筛选。</summary>
    [Fact]
    public async Task FilteredNavigationFollowsOriginalHistoryList()
    {
        using var fixture = new Fixture();
        await SeedAsync(fixture, "/comics/new.cbz", "/other/book.cbz", "/comics/old.cbz", "/comics/sub/nested.cbz");
        var state = new SaveData(fixture.State); await state.LoadAsync(TestContext.Current.CancellationToken);
        var list = new HistoryList(state) { Address = "/comics/new.cbz" };
        Config.Current.History.IsCurrentFolder = true;
        Assert.Equal(new[] { "/comics/new.cbz", "/comics/old.cbz" }, list.GetViewItems().Select(item => item.Path));
        Assert.Null(list.GetTarget(1)); Assert.Equal("/comics/old.cbz", list.GetTarget(-1)!.Path);
        list.Address = "/comics/old.cbz"; Assert.Null(list.GetTarget(-1)); Assert.Equal("/comics/new.cbz", list.GetTarget(1)!.Path);
        list.Address = "/comics/missing.cbz"; Assert.Equal("/comics/new.cbz", list.GetTarget(-1)!.Path); Assert.Null(list.GetTarget(1));
        await list.SearchAsync("OLD", token: TestContext.Current.CancellationToken); Assert.Equal("/comics/old.cbz", Assert.Single(list.GetViewItems()).Path);
        Config.Current.History.IsCurrentFolder = false; await list.SearchAsync("", token: TestContext.Current.CancellationToken); Assert.Equal(4, list.GetViewItems().Count);
        var today = new DateTime(2026, 10, 3);
        Assert.Equal("今天", HistoryList.GetGroupName(today.AddHours(23), today)); Assert.Equal("昨天", HistoryList.GetGroupName(today.AddDays(-1), today));
        Assert.NotEqual("昨天", HistoryList.GetGroupName(today.AddDays(-2), today));
    }

    /// <summary>访问列表打开保留数组、时间与条目位置，当前书不重复打开；区别于进程游标。</summary>
    [Fact]
    public async Task HistoryOpenKeepsAccessOrderAndRestoresEntry()
    {
        using var fixture = new Fixture(); await SeedAsync(fixture, fixture.Images, fixture.Zip);
        var state = new SaveData(fixture.State); await state.LoadAsync(TestContext.Current.CancellationToken);
        await using var operation = fixture.Operation(state); await operation.OpenHistoryAsync(fixture.Images); await operation.SaveAsync();
        var before = state.HistoryEntries.ToArray(); var book = operation.Book;
        await operation.OpenHistoryAsync(fixture.Images); Assert.Same(book, operation.Book);
        Assert.Equal(2, operation.Book!.CurrentPage!.Index);
        var commands = new CommandTable(operation); Assert.True(commands.IsAvailable("PrevHistory"));
        await commands.ExecuteAsync("PrevHistory"); Assert.Equal(fixture.Zip, operation.Book!.Path); Assert.Equal(2, operation.Book.CurrentPage!.Index);
        await operation.SaveAsync(); Assert.Equal(before, state.HistoryEntries);
        await commands.ExecuteAsync("NextHistory"); Assert.Equal(fixture.Images, operation.Book!.Path);
        await operation.SaveAsync(); Assert.Equal(before, state.HistoryEntries);
        Assert.Equal(fixture.Zip, operation.BookHistory.GetTarget(-1)); Assert.Null(operation.BookHistory.GetTarget(1));
    }

    /// <summary>坏的访问列表目标保留旧书和可重试目标，不改历史时间或正文位置。</summary>
    [Fact]
    public async Task FailedHistoryLoadPreservesCurrentBookAndTarget()
    {
        using var fixture = new Fixture(); var missing = Path.Combine(fixture.Root, "不存在.cbz");
        await SeedAsync(fixture, fixture.Images, missing);
        var state = new SaveData(fixture.State); await state.LoadAsync(TestContext.Current.CancellationToken);
        await using var operation = fixture.Operation(state); await operation.OpenHistoryAsync(fixture.Images); await operation.SaveAsync();
        var book = operation.Book; var before = state.HistoryEntries.ToArray();
        await operation.MoveHistoryListAsync(-1); Assert.Same(book, operation.Book); Assert.NotNull(operation.Error);
        Assert.Equal(missing, operation.HistoryList.GetTarget(-1)!.Path); Assert.Equal(before, state.HistoryEntries);
        File.Copy(fixture.Zip, missing); await operation.MoveHistoryListAsync(-1); Assert.Equal(missing, operation.Book!.Path); Assert.Null(operation.Error);
    }

    /// <summary>多选移除只改记录；当前项在翻页/退出保存中不复活，重新显式打开才登记。</summary>
    [Fact]
    public async Task RemovingCurrentHistorySurvivesSaveAndExplicitReopen()
    {
        using var fixture = new Fixture(); await SeedAsync(fixture, fixture.Images, fixture.Zip);
        var state = new SaveData(fixture.State); await state.LoadAsync(TestContext.Current.CancellationToken);
        await using var operation = fixture.Operation(state); await operation.OpenHistoryAsync(fixture.Images);
        await state.RegisterBookmarkAsync(operation.Book!, token: TestContext.Current.CancellationToken);
        Assert.Equal(1, await state.RemoveHistoryAsync([fixture.Images, fixture.Images, "/absent"], TestContext.Current.CancellationToken));
        await operation.JumpAsync(3); await operation.SaveAsync(); Assert.Single(state.HistoryEntries); Assert.True(state.IsBookmark(fixture.Images));
        Assert.True(File.Exists(Path.Combine(fixture.Images, "004.png")));
        var saved = JsonNode.Parse(await File.ReadAllTextAsync(Path.Combine(fixture.State, "History.json"), TestContext.Current.CancellationToken))!;
        Assert.Equal("preserve", saved["UnknownRoot"]!.GetValue<string>()); Assert.Equal("search", saved["BookHistorySearchHistory"]![0]!.GetValue<string>());
        Assert.Equal(99, saved["Items"]![0]!["Future"]!.GetValue<int>());
        await operation.OpenAsync(fixture.Images, TestContext.Current.CancellationToken); await operation.SaveAsync(); Assert.Equal(2, state.HistoryEntries.Count);
        await operation.DisposeAsync(); var fresh = new SaveData(fixture.State); await fresh.LoadAsync(TestContext.Current.CancellationToken); Assert.Equal(2, fresh.HistoryEntries.Count);
    }

    /// <summary>保存准备失败与取消保持原历史及登记状态，修正后可重试。</summary>
    [Fact]
    public async Task HistoryEditFailureRollsBackFilesAndSuppression()
    {
        using var fixture = new Fixture(); await SeedAsync(fixture, fixture.Images, fixture.Zip);
        var state = new SaveData(fixture.State); await state.LoadAsync(TestContext.Current.CancellationToken);
        await using var operation = fixture.Operation(state); await operation.OpenHistoryAsync(fixture.Images); await operation.SaveAsync();
        var history = await File.ReadAllTextAsync(Path.Combine(fixture.State, "History.json"), TestContext.Current.CancellationToken);
        var blocked = Path.Combine(fixture.State, "UserSetting.json.tmp"); Directory.CreateDirectory(blocked);
        try { await Assert.ThrowsAnyAsync<Exception>(() => state.RemoveHistoryAsync([fixture.Images], TestContext.Current.CancellationToken)); }
        finally { Directory.Delete(blocked); }
        Assert.Equal(2, state.HistoryEntries.Count); Assert.Equal(history, await File.ReadAllTextAsync(Path.Combine(fixture.State, "History.json"), TestContext.Current.CancellationToken));
        using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => state.ClearHistoryAsync(cancelled.Token));
        await operation.SaveAsync(); Assert.Equal(2, state.HistoryEntries.Count);
        Assert.Equal(2, await state.ClearHistoryAsync(TestContext.Current.CancellationToken)); await operation.SaveAsync(); Assert.Empty(state.HistoryEntries);
        await operation.DisposeAsync(); var fresh = new SaveData(fixture.State); await fresh.LoadAsync(TestContext.Current.CancellationToken); Assert.Empty(fresh.HistoryEntries);
    }

    /// <summary>首次打开尚无历史记录时清空，晚到防抖和正常退出也不能重新登记。</summary>
    [Fact]
    public async Task ClearBeforeFirstDebounceDoesNotResurrectCurrentBook()
    {
        using var fixture = new Fixture(); var state = new SaveData(fixture.State); await state.LoadAsync(TestContext.Current.CancellationToken);
        await using var operation = fixture.Operation(state); await operation.OpenAsync(fixture.Images, TestContext.Current.CancellationToken);
        Assert.Empty(state.HistoryEntries); Assert.Equal(0, await state.ClearHistoryAsync(TestContext.Current.CancellationToken));
        await operation.DisposeAsync(); Assert.Empty(state.HistoryEntries);
    }

    /// <summary>原启动快照优先于缺失或过期历史；移除后退出仍保存完整快照供下次恢复。</summary>
    /// <param name="staleHistory">是否预置与启动快照冲突的旧历史页与设置。</param>
    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task StartupRestoresExplicitMementoWithoutHistoryDependency(bool staleHistory)
    {
        using var fixture = new Fixture(); await SeedAsync(fixture, staleHistory ? [fixture.Images] : []);
        var last = new JsonObject { ["Path"] = fixture.Images, ["Page"] = "004.png", ["Props"] = "SinglePage LeftToRight FutureFlag",
            ["MacIsSupportedWidePage"] = false, ["MacPagePart"] = 1 };
        await File.WriteAllTextAsync(Path.Combine(fixture.State, "UserSetting.json"),
            new JsonObject { ["Config"] = new JsonObject { ["StartUp"] = new JsonObject { ["LastBookV2"] = last },
                ["BookSettingPolicy"] = new JsonObject { ["Page"] = 0, ["PageMode"] = 0, ["BookReadOrder"] = 0 } } }.ToJsonString(), TestContext.Current.CancellationToken);
        var state = new SaveData(fixture.State); await state.LoadAsync(TestContext.Current.CancellationToken);
        Assert.Equal(1, state.GetLastBook().Part); Assert.False(state.GetLastBook().Memento!.IsSupportedWidePage);
        var operation = fixture.Operation(state); var window = new MainWindow();
        window.Bind(new ReaderWorkspaceViewModel(operation, new CommandTable(operation), state), new BitmapFactory(new NeeView.Backends.MagickImageDecoder()), new TestPlatform()); window.Show();
        try
        {
            // 正式窗口必须调用完整快照恢复；只传路径时此处会落到首图或旧历史页。
            await window.RestoreLastAsync(); Assert.Equal("004.png", operation.Book!.CurrentPage!.EntryName);
            Assert.Equal(PageMode.SinglePage, operation.Book.Setting.PageMode); Assert.Equal(PageReadOrder.LeftToRight, operation.Book.Setting.BookReadOrder);
            Assert.False(operation.Book.Setting.IsSupportedWidePage);
            await operation.SaveAsync(); await state.RemoveHistoryAsync([fixture.Images], TestContext.Current.CancellationToken);
            await operation.JumpAsync(4); await operation.SaveAsync(); Assert.Empty(state.HistoryEntries);
            var fresh = new SaveData(fixture.State); await fresh.LoadAsync(TestContext.Current.CancellationToken);
            Assert.Equal("005.png", fresh.GetLastBook().Memento!.Page); Assert.False(fresh.GetLastBook().Memento!.IsSupportedWidePage);
            await using var restarted = fixture.Operation(fresh); await restarted.RestoreLastAsync(TestContext.Current.CancellationToken);
            Assert.Equal("005.png", restarted.Book!.CurrentPage!.EntryName);
        }
        finally { await window.PrepareShutdownAsync(); window.Close(); }
    }

    /// <summary>正式视图多选删除/文本作用域、菜单占位/开关及导航刷新不触发正文。</summary>
    [AvaloniaFact]
    public async Task HistoryPanelUsesOriginalMenuAndSelectionScopes()
    {
        using var fixture = new Fixture(); await SeedAsync(fixture, fixture.Images, fixture.Zip, "/missing.cbz");
        var state = new SaveData(fixture.State); await state.LoadAsync(TestContext.Current.CancellationToken);
        var operation = fixture.Operation(state); var images = new BitmapFactory(new NeeView.Backends.MagickImageDecoder());
        var model = new ReaderWorkspaceViewModel(operation, new CommandTable(operation), state);
        var window = new MainWindow(); window.Bind(model, images, new TestPlatform()); window.Show();
        try
        {
            await window.OpenHistoryAsync(fixture.Images); await window.ExecuteAsync("FocusHistorySearchBox"); Dispatcher.UIThread.RunJobs(); window.UpdateLayout();
            Assert.True(model.ShowHistory); await window.ExecuteAsync("FocusHistorySearchBox"); Assert.True(model.ShowHistory);
            var refreshes = 0; model.Refreshed += (_, _) => refreshes++;
            var more = window.FindControl<Button>("HistoryMoreButton")!; more.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            var menu = more.ContextMenu!; var items = menu.Items.OfType<MenuItem>().ToArray();
            // 能力入口可渐进增加；验证原占位和真实动作，而非把项目总数当作功能契约。
            Assert.All(items.Take(4), item => Assert.False(item.IsEnabled)); Assert.False(items.Single(item => item.Header?.ToString()?.StartsWith("移除无效历史记录") == true).IsEnabled);
            Assert.Contains(items, item => Equals(item.Header, "历史记录设置…") && item.IsEnabled);
            items.Single(item => Equals(item.Header, "按日期分组")).RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent)); Dispatcher.UIThread.RunJobs(); menu.Close();
            Assert.True(Config.Current.History.IsGroupBy); Assert.Equal(0, refreshes); Assert.True(model.History[0].HasGroupHeader);
            window.UpdateLayout();
            var phase = Environment.GetEnvironmentVariable("NEEVIEW_ACCEPTANCE_PHASE") ?? "p2-history";
            var output = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, $"../../../../../acceptance/{phase}-history-layout.png"));
            // 留下正式 XAML 的独立可检查画面；此图仅是 Headless，不作为真机证明。
            using (var screenshot = new RenderTargetBitmap(new PixelSize(1200, 800)))
            { screenshot.Render(window); screenshot.Save(output, PngBitmapEncoderOptions.Default); }
            var list = window.FindControl<ListBox>("HistoryList")!;
            list.SelectedItems!.Clear(); list.SelectedItems.Add(model.History[0]); list.SelectedItems.Add(model.History[1]);
            model.RefreshHistory(); Dispatcher.UIThread.RunJobs(); Assert.Equal(2, list.SelectedItems.Count);
            var search = window.FindControl<TextBox>("HistorySearchBox")!; search.Focus();
            window.KeyPress(Key.Delete, RawInputModifiers.None, PhysicalKey.Delete, null); window.KeyRelease(Key.Delete, RawInputModifiers.None, PhysicalKey.Delete, null);
            Assert.Equal(3, state.HistoryEntries.Count);
            Assert.True(list.Focus()); window.KeyPress(Key.Delete, RawInputModifiers.None, PhysicalKey.Delete, null); window.KeyRelease(Key.Delete, RawInputModifiers.None, PhysicalKey.Delete, null);
            for (var i = 0; i < 60 && state.HistoryEntries.Count > 1; i++) await Task.Delay(20, TestContext.Current.CancellationToken);
            Dispatcher.UIThread.RunJobs(); Assert.Single(state.HistoryEntries); Assert.Equal(0, refreshes); Assert.Equal(fixture.Images, operation.Book!.Path);
            await operation.SaveAsync(); Assert.Single(state.HistoryEntries);
        }
        finally { await window.PrepareShutdownAsync(); window.Close(); }
    }

    /// <summary>实际指针验证原单/双击配置，并保证右击多选与空白不会沿用错误对象。</summary>
    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task HistoryPointerActivationAndContextTargets(bool doubleClick)
    {
        using var fixture = new Fixture(); await SeedAsync(fixture, fixture.Images, fixture.Zip);
        var state = new SaveData(fixture.State); await state.LoadAsync(TestContext.Current.CancellationToken);
        Config.Current.Panels.OpenWithDoubleClick = doubleClick;
        var operation = fixture.Operation(state); var model = new ReaderWorkspaceViewModel(operation, new CommandTable(operation), state);
        var window = new MainWindow(); window.Bind(model, new BitmapFactory(new NeeView.Backends.MagickImageDecoder()), new TestPlatform()); window.Show();
        try
        {
            await window.OpenHistoryAsync(fixture.Images); await window.ExecuteAsync("FocusHistorySearchBox"); Dispatcher.UIThread.RunJobs(); window.UpdateLayout();
            var list = window.FindControl<ListBox>("HistoryList")!;
            var target = model.History.Single(row => row.Path == fixture.Zip);
            var container = (ListBoxItem)list.ContainerFromItem(target)!;
            var point = container.TranslatePoint(new Point(60, 15), window)!.Value;
            window.MouseDown(point, MouseButton.Left); window.MouseUp(point, MouseButton.Left);
            if (doubleClick)
            {
                Assert.Equal(fixture.Images, operation.Book!.Path);
                window.MouseDown(point, MouseButton.Left); window.MouseUp(point, MouseButton.Left);
            }
            for (var i = 0; i < 60 && operation.Book!.Path != fixture.Zip; i++) await Task.Delay(20, TestContext.Current.CancellationToken);
            Assert.Equal(fixture.Zip, operation.Book!.Path); Dispatcher.UIThread.RunJobs(); window.UpdateLayout();
            list.SelectedItems!.Clear(); foreach (var row in model.History) list.SelectedItems.Add(row);
            container = (ListBoxItem)list.ContainerFromItem(model.History[0])!;
            point = container.TranslatePoint(new Point(60, 15), window)!.Value;
            window.MouseDown(point, MouseButton.Right); window.MouseUp(point, MouseButton.Right); Assert.Equal(2, list.SelectedItems.Count);
            Assert.True(list.ContextMenu!.IsOpen); list.ContextMenu.Close();
            var blank = list.TranslatePoint(new Point(60, list.Bounds.Height - 10), window)!.Value;
            window.MouseDown(blank, MouseButton.Right); window.MouseUp(blank, MouseButton.Right);
            Assert.Empty(list.SelectedItems); Assert.All(list.ContextMenu.Items.OfType<MenuItem>(), item => Assert.False(item.IsEnabled)); list.ContextMenu.Close();
        }
        finally { await window.PrepareShutdownAsync(); window.Close(); }
    }

    /// <summary>原更多菜单清空有确认，取消不写入；未支持配置字段往返保留。</summary>
    [AvaloniaFact]
    public async Task HistoryClearConfirmationAndConfigRoundTrip()
    {
        using var fixture = new Fixture(); await SeedAsync(fixture, fixture.Images, fixture.Zip);
        await File.WriteAllTextAsync(Path.Combine(fixture.State, "UserSetting.json"), """{"Config":{"History":{"IsGroupBy":true,"IsVisibleSearchBox":false,"PanelListItemStyle":3,"LimitSize":50,"Future":{"enabled":true}}}}""", TestContext.Current.CancellationToken);
        var state = new SaveData(fixture.State); await state.LoadAsync(TestContext.Current.CancellationToken);
        var operation = fixture.Operation(state); var model = new ReaderWorkspaceViewModel(operation, new CommandTable(operation), state);
        var window = new MainWindow(); window.Bind(model, new BitmapFactory(new NeeView.Backends.MagickImageDecoder()), new TestPlatform()); window.Show();
        try
        {
            await window.ExecuteAsync("FocusHistorySearchBox"); Dispatcher.UIThread.RunJobs();
            var more = window.FindControl<Button>("HistoryMoreButton")!; more.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            more.ContextMenu!.Items.OfType<MenuItem>().Last().RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            var dialog = Assert.Single(window.OwnedWindows); dialog.Close(false); await Task.Delay(20, TestContext.Current.CancellationToken);
            Assert.Equal(2, state.HistoryEntries.Count);
            await window.ExecuteAsync("ClearHistory"); Assert.Empty(state.HistoryEntries);
            await operation.SaveAsync(); var saved = JsonNode.Parse(await File.ReadAllTextAsync(Path.Combine(fixture.State, "UserSetting.json"), TestContext.Current.CancellationToken))!["Config"]!["History"]!;
            Assert.True(saved["IsGroupBy"]!.GetValue<bool>()); Assert.True(saved["IsVisibleSearchBox"]!.GetValue<bool>());
            Assert.Equal(3, saved["PanelListItemStyle"]!.GetValue<int>()); Assert.Equal(50, saved["LimitSize"]!.GetValue<int>()); Assert.True(saved["Future"]!["enabled"]!.GetValue<bool>());
        }
        finally { await window.PrepareShutdownAsync(); window.Close(); }
    }

    /// <summary>生成固定历史条目与未知字段；三天倒序，不写产品数据目录。</summary>
    private static async Task SeedAsync(Fixture fixture, params string[] paths)
    {
        Directory.CreateDirectory(fixture.State);
        var items = new JsonArray();
        for (var i = 0; i < paths.Length; i++) items.Add(new JsonObject { ["Path"] = paths[i], ["Page"] = "003.png", ["Props"] = "SinglePage", ["LastAccessTime"] = DateTime.Today.AddDays(-i), ["Future"] = 99 });
        await File.WriteAllTextAsync(Path.Combine(fixture.State, "History.json"), new JsonObject { ["Items"] = items, ["UnknownRoot"] = "preserve", ["BookHistorySearchHistory"] = new JsonArray("search") }.ToJsonString(), TestContext.Current.CancellationToken);
    }

    /// <summary>Headless 不模拟系统文件操作，避免把界面测试当 Finder 验收。</summary>
    private sealed class TestPlatform : IPlatformService
    {
        /// <summary>测试范围不包含 Finder。</summary>
        public Task RevealAsync(string path, CancellationToken token = default) => throw new NotSupportedException();
        /// <summary>测试范围不包含废纸篓。</summary>
        public Task TrashAsync(string path, CancellationToken token = default) => throw new NotSupportedException();
    }
}
