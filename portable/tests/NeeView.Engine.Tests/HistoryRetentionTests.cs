using System.Text.Json.Nodes;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using NeeView;
using NeeView.MacOS.ViewModels;
using NeeView.MacOS.Views;
namespace NeeView.Engine.Tests;

/// <summary>原历史文件限制与运行集合分离，使用临时夹具及正式设置XAML验证事务。</summary>
public sealed class HistoryRetentionTests
{
    /// <summary>原数量优先、严格截止和连续前缀；源引用不被替换或修改。</summary>
    /// <param name="size">原数量上限。</param><param name="days">原期限，零无限。</param><param name="expected">预期留下条目数。</param>
    [Theory]
    [InlineData(-1, 0, 4)]
    [InlineData(0, 0, 0)]
    [InlineData(1, 0, 1)]
    [InlineData(3, 2, 2)]
    [InlineData(-1, 2, 2)]
    public void OriginalLimitUsesStrictDatePrefix(int size, int days, int expected)
    {
        var now = new DateTime(2026, 10, 4, 12, 0, 0);
        var items = Enumerable.Range(0, 4).Select(i => new HistoryEntry($"/{i}.cbz", "page.png", now.AddDays(-i))).ToArray();
        var result = BookHistoryCollection.Limit(items, size, TimeSpan.FromDays(days), e => e.LastAccessTime, now).ToArray();
        Assert.Equal(expected, result.Length); Assert.All(result, item => Assert.Contains(item, items));
        // 非排序输入不能被Where纠正：首个过期项之后的较新项同样不保留。
        var unordered = new[] { items[0], items[3], items[1] };
        Assert.Single(BookHistoryCollection.Limit(unordered, -1, TimeSpan.FromDays(2), e => e.LastAccessTime, now));
    }

    /// <summary>原setter归一及极大旧期限不会造成日期溢出，默认仍无限。</summary>
    [Fact]
    public void OriginalDefaultsAndOversizedSpanAreSafe()
    {
        var config = new HistoryConfig(); Assert.Equal(-1, config.LimitSize); Assert.Equal(TimeSpan.Zero, config.LimitSpan);
        config.LimitSize = -9; config.LimitSpan = TimeSpan.FromDays(-3);
        Assert.Equal(-1, config.LimitSize); Assert.Equal(TimeSpan.Zero, config.LimitSpan);
        var item = new HistoryEntry("/book.cbz", null, DateTime.Today);
        Assert.Single(BookHistoryCollection.Limit([item], -1, TimeSpan.MaxValue, e => e.LastAccessTime));
    }

    /// <summary>加载使用原配置裁剪内存但不写文件，幸存条目与其他模块的未知字段往返。</summary>
    /// <param name="size">载入数量上限。</param><param name="expected">两天期限内实际条目数。</param>
    [Theory]
    [InlineData(0, 0)]
    [InlineData(1, 1)]
    [InlineData(-1, 2)]
    public async Task LoadLimitsWithoutWritingSource(int size, int expected)
    {
        using var fixture = new Fixture(); await SeedAsync(fixture, ("/new.cbz", 0), ("/yesterday.cbz", 1), ("/old.cbz", 3));
        await File.WriteAllTextAsync(Path.Combine(fixture.State, "UserSetting.json"), new JsonObject { ["Config"] = new JsonObject
        { ["History"] = new JsonObject { ["LimitSize"] = size, ["LimitSpan"] = "2.00:00:00", ["IsAutoCleanupEnabled"] = true, ["Future"] = 17 } } }.ToJsonString(), TestContext.Current.CancellationToken);
        var before = await ReadAsync(fixture, "History.json"); var state = new SaveData(fixture.State);
        await state.LoadAsync(TestContext.Current.CancellationToken);
        Assert.Equal(expected, state.HistoryEntries.Count); Assert.Equal(before, await ReadAsync(fixture, "History.json"));
        Assert.Null(state.Find("/old.cbz").Memento); Assert.Single(state.BookHistorySearchHistory); Assert.Single(state.BookmarkSearchHistory);
        await state.SaveAsync(null, 0, TestContext.Current.CancellationToken);
        var history = JsonNode.Parse(await ReadAsync(fixture, "History.json"))!;
        Assert.Equal(expected, history["Items"]!.AsArray().Count); Assert.Equal("keep", history["FutureRoot"]!.GetValue<string>());
        if (expected > 0) Assert.Equal(99, history["Items"]![0]!["Future"]!.GetValue<int>());
        var settings = JsonNode.Parse(await ReadAsync(fixture, "UserSetting.json"))!["Config"]!["History"]!;
        Assert.Equal("2.00:00:00", settings["LimitSpan"]!.GetValue<string>()); Assert.Equal(17, settings["Future"]!.GetValue<int>());
        Assert.True(settings["IsAutoCleanupEnabled"]!.GetValue<bool>()); // Engine加载保持只读；来源清理由窗口启动生命周期触发。
    }

    /// <summary>保存只限制副本；阅读导航/查找仍完整，其他事务同样遵循已提交上限。</summary>
    [Fact]
    public async Task SaveProjectionLeavesRuntimeRecordsAndOtherTransactionsIntact()
    {
        using var fixture = new Fixture(); await SeedAsync(fixture, ("/new.cbz", 0), ("/middle.cbz", 1), ("/old.cbz", 2));
        var state = new SaveData(fixture.State); await state.LoadAsync(TestContext.Current.CancellationToken);
        await state.SaveAsync(null, 0, TestContext.Current.CancellationToken, historyLimits: (1, TimeSpan.Zero));
        Assert.Equal(3, state.HistoryEntries.Count); Assert.NotNull(state.Find("/old.cbz").Memento);
        Assert.Single(JsonNode.Parse(await ReadAsync(fixture, "History.json"))!["Items"]!.AsArray());
        await state.EditBookHistorySearchHistoryAsync("latest", token: TestContext.Current.CancellationToken);
        await state.AddBookmarkFolderAsync(null, "folder", TestContext.Current.CancellationToken);
        Assert.Equal(3, state.HistoryEntries.Count); Assert.Single(JsonNode.Parse(await ReadAsync(fixture, "History.json"))!["Items"]!.AsArray());
        // 同进程放宽限制还能写出完整集合；重启后只拥有之前保存的有限历史。
        await state.SaveAsync(null, 0, TestContext.Current.CancellationToken, historyLimits: (-1, TimeSpan.Zero));
        Assert.Equal(3, JsonNode.Parse(await ReadAsync(fixture, "History.json"))!["Items"]!.AsArray().Count);
    }

    /// <summary>数量限制前只排除应用临时根；临时目录外和同前缀的用户来源正常保留。</summary>
    [Fact]
    public async Task AppTemporarySourcesDoNotConsumeSavedLimit()
    {
        using var fixture = new Fixture(); var temporary = Path.Combine(fixture.Root, "application-temp");
        var user = temporary + "-user/book.cbz";
        await SeedAsync(fixture, (Path.Combine(temporary, "cache", "temp.cbz"), 0), (temporary, 0), (user, 1), (fixture.Zip, 2));
        var state = new SaveData(fixture.State, temporary + Path.DirectorySeparatorChar); await state.LoadAsync(TestContext.Current.CancellationToken);
        await state.SaveAsync(null, 0, TestContext.Current.CancellationToken, historyLimits: (1, TimeSpan.Zero));
        Assert.Equal(4, state.HistoryEntries.Count); // 排除仅作用于文件副本，加载遵循原fromLoad算法。
        var saved = Assert.Single(JsonNode.Parse(await ReadAsync(fixture, "History.json"))!["Items"]!.AsArray());
        Assert.Equal(user, saved!["Path"]!.GetValue<string>());
    }

    /// <summary>设置保存失败或取消不应用候选，原文件和运行列表完整，修正后可重试。</summary>
    [Fact]
    public async Task FailedOrCancelledLimitsRemainUncommitted()
    {
        using var fixture = new Fixture(); await SeedAsync(fixture, ("/new.cbz", 0), ("/old.cbz", 3));
        var state = new SaveData(fixture.State); await state.LoadAsync(TestContext.Current.CancellationToken);
        await state.SaveAsync(null, 0, TestContext.Current.CancellationToken);
        var history = await ReadAsync(fixture, "History.json"); var settings = await ReadAsync(fixture, "UserSetting.json");
        var blocked = Path.Combine(fixture.State, "UserSetting.json.tmp"); Directory.CreateDirectory(blocked);
        try { await Assert.ThrowsAnyAsync<Exception>(() => state.SaveAsync(null, 0, TestContext.Current.CancellationToken, historyLimits: (0, TimeSpan.FromDays(1)))); }
        finally { Directory.Delete(blocked); }
        Assert.Equal(-1, Config.Current.History.LimitSize); Assert.Equal(TimeSpan.Zero, Config.Current.History.LimitSpan);
        Assert.Equal(2, state.HistoryEntries.Count); Assert.Equal(history, await ReadAsync(fixture, "History.json")); Assert.Equal(settings, await ReadAsync(fixture, "UserSetting.json"));
        using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => state.SaveAsync(null, 0, cancelled.Token, historyLimits: (0, TimeSpan.Zero)));
        Assert.Equal(-1, Config.Current.History.LimitSize);
        await state.SaveAsync(null, 0, TestContext.Current.CancellationToken, historyLimits: (0, TimeSpan.Zero));
        Assert.Equal(0, Config.Current.History.LimitSize); Assert.Equal(2, state.HistoryEntries.Count);
        Assert.Empty(JsonNode.Parse(await ReadAsync(fixture, "History.json"))!["Items"]!.AsArray());
    }

    /// <summary>保序访问不延长已过期条目寿命；零历史也不破坏独立LastBook启动位置。</summary>
    [Fact]
    public async Task KeepOrderAndZeroLimitPreserveLastBook()
    {
        using var fixture = new Fixture(); await SeedAsync(fixture, (fixture.Images, 3));
        var state = new SaveData(fixture.State); await state.LoadAsync(TestContext.Current.CancellationToken);
        await using var operation = fixture.Operation(state); await operation.OpenHistoryAsync(fixture.Images); await operation.JumpAsync(3);
        var access = Assert.Single(state.HistoryEntries).LastAccessTime;
        await operation.SaveAsync((-1, TimeSpan.FromDays(1)));
        Assert.Equal(access, Assert.Single(state.HistoryEntries).LastAccessTime);
        Assert.Empty(JsonNode.Parse(await ReadAsync(fixture, "History.json"))!["Items"]!.AsArray());
        await operation.SaveAsync((0, TimeSpan.Zero)); await operation.DisposeAsync();
        var fresh = new SaveData(fixture.State); await fresh.LoadAsync(TestContext.Current.CancellationToken);
        Assert.Empty(fresh.HistoryEntries); Assert.Equal("004.png", fresh.GetLastBook().Memento!.Page);
        await using var restored = fixture.Operation(fresh); await restored.RestoreLastAsync(TestContext.Current.CancellationToken);
        Assert.Equal(3, restored.Position.Index); Assert.Empty(fresh.HistoryEntries);
    }

    /// <summary>原候选顺序及自定义精确期限只存于草稿，不因打开/取消归一为整数天。</summary>
    [Fact]
    public void SettingsDraftPreservesCustomValues()
    {
        var config = new HistoryConfig { LimitSize = 37, LimitSpan = TimeSpan.FromHours(12) };
        var draft = new HistorySettingsViewModel(config);
        Assert.Equal((37, TimeSpan.FromHours(12)), draft.GetLimits());
        Assert.Equal(new[] { 0, 1, 10, 20, 50, 100, 200, 500, 1000, -1, 37 }, draft.SizeChoices.Select(e => e.Value));
        draft.SelectedSize = draft.SizeChoices.Single(e => e.Value == 0); Assert.Equal(37, config.LimitSize);
        Assert.Equal(TimeSpan.FromHours(12), config.LimitSpan);
    }

    /// <summary>正式历史设置页取消/失败/重试、无正文刷新及零条目重载通过同一产品入口。</summary>
    [AvaloniaFact]
    public async Task FormalSettingsCancelFailureAndRetryStayIndependent()
    {
        using var fixture = new Fixture(); await SeedAsync(fixture, (fixture.Images, 0), (fixture.Zip, 1));
        var state = new SaveData(fixture.State); await state.LoadAsync(TestContext.Current.CancellationToken);
        var operation = fixture.Operation(state); var model = new ReaderWorkspaceViewModel(operation, new CommandTable(operation), state);
        var window = new MainWindow(); window.Bind(model, new BitmapFactory(new NeeView.Backends.MagickImageDecoder()), new NoPlatform()); window.Show();
        try
        {
            await window.OpenHistoryAsync(fixture.Images); Pump(window);
            var cancel = new SettingsWindow(model); cancel.Show(window); cancel.SelectHistoryPage(); Pump(cancel);
            Assert.True(cancel.FindControl<ScrollViewer>("HistorySettings")!.IsVisible);
            var draft = (HistorySettingsViewModel)cancel.FindControl<ScrollViewer>("HistorySettings")!.DataContext!;
            cancel.FindControl<ComboBox>("HistoryLimitSize")!.SelectedItem = draft.SizeChoices.Single(e => e.Value == 0);
            cancel.FindControl<Button>("CancelSettings")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.Equal(-1, Config.Current.History.LimitSize);
            int refreshes = 0; model.Refreshed += (_, _) => refreshes++;
            var settings = new SettingsWindow(model); settings.Show(window); settings.SelectHistoryPage(); Pump(settings);
            draft = (HistorySettingsViewModel)settings.FindControl<ScrollViewer>("HistorySettings")!.DataContext!;
            settings.FindControl<ComboBox>("HistoryLimitSize")!.SelectedItem = draft.SizeChoices.Single(e => e.Value == 0);
            settings.FindControl<ComboBox>("HistoryLimitSpan")!.SelectedItem = draft.SpanChoices.Single(e => e.Value == TimeSpan.FromDays(7));
            var blocked = Path.Combine(fixture.State, "UserSetting.json.tmp"); Directory.CreateDirectory(blocked);
            try
            {
                settings.FindControl<Button>("SaveSettings")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                await WaitAsync(() => settings.FindControl<TextBlock>("Message")!.Text?.StartsWith("保存失败：") == true);
                Assert.True(settings.IsVisible); Assert.Equal(-1, Config.Current.History.LimitSize);
                Assert.Equal(2, state.HistoryEntries.Count); Assert.Equal(0, draft.GetLimits().Size);
            }
            finally { Directory.Delete(blocked); }
            Pump(settings);
            var phase = Environment.GetEnvironmentVariable("NEEVIEW_ACCEPTANCE_PHASE") ?? "p2-history-retention";
            var output = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, $"../../../../../acceptance/{phase}-history-settings-layout.png"));
            using (var image = new RenderTargetBitmap(new PixelSize(900, 620))) { image.Render(settings); image.Save(output, PngBitmapEncoderOptions.Default); }
            settings.FindControl<Button>("SaveSettings")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await WaitAsync(() => !settings.IsVisible); Pump(window);
            Assert.Equal(0, refreshes); Assert.Equal(2, state.HistoryEntries.Count);
            Assert.Equal(0, Config.Current.History.LimitSize); Assert.Equal(TimeSpan.FromDays(7), Config.Current.History.LimitSpan);
            var fresh = new SaveData(fixture.State); await fresh.LoadAsync(TestContext.Current.CancellationToken);
            Assert.Empty(fresh.HistoryEntries); Assert.Equal("004.png", fresh.GetLastBook().Memento!.Page);
        }
        finally { await window.PrepareShutdownAsync(); window.Close(); }
    }

    /// <summary>原更多菜单跳转同一设置导航页，取消不会写文件或改正文。</summary>
    [AvaloniaFact]
    public async Task MoreMenuOpensSharedHistorySettingsPage()
    {
        using var fixture = new Fixture(); var state = new SaveData(fixture.State); await state.LoadAsync(TestContext.Current.CancellationToken);
        var operation = fixture.Operation(state); var model = new ReaderWorkspaceViewModel(operation, new CommandTable(operation), state);
        var window = new MainWindow(); window.Bind(model, new BitmapFactory(new NeeView.Backends.MagickImageDecoder()), new NoPlatform()); window.Show();
        try
        {
            await window.ExecuteAsync("FocusHistorySearchBox"); Pump(window);
            var button = window.FindControl<Button>("HistoryMoreButton")!; button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            var item = button.ContextMenu!.Items.OfType<MenuItem>().Single(e => Equals(e.Header, "历史记录设置…")); button.ContextMenu.Close();
            item.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent)); Pump(window);
            var settings = Assert.IsType<SettingsWindow>(Assert.Single(window.OwnedWindows));
            Assert.Equal(4, settings.FindControl<ListBox>("SettingsNavigation")!.SelectedIndex);
            Assert.True(settings.FindControl<ScrollViewer>("HistorySettings")!.IsVisible);
            settings.FindControl<Button>("CancelSettings")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.False(File.Exists(Path.Combine(fixture.State, "UserSetting.json")));
        }
        finally { await window.PrepareShutdownAsync(); window.Close(); }
    }

    /// <summary>建立带未知字段的原倒序历史，不触碰用户数据目录。</summary>
    private static async Task SeedAsync(Fixture fixture, params (string Path, int AgeDays)[] entries)
    {
        Directory.CreateDirectory(fixture.State); var now = DateTime.Now;
        var items = new JsonArray(entries.Select(e => (JsonNode)new JsonObject { ["Path"] = e.Path, ["Page"] = "004.png",
            ["Props"] = "SinglePage", ["LastAccessTime"] = now.AddDays(-e.AgeDays), ["Future"] = 99 }).ToArray());
        await File.WriteAllTextAsync(Path.Combine(fixture.State, "History.json"), new JsonObject { ["Items"] = items,
            ["FutureRoot"] = "keep", ["BookHistorySearchHistory"] = new JsonArray("history"), ["BookmarkSearchHistory"] = new JsonArray("bookmark") }.ToJsonString(), TestContext.Current.CancellationToken);
    }
    /// <summary>读取本测试的临时文件并遵守xUnit取消。</summary>
    private static Task<string> ReadAsync(Fixture fixture, string name) => File.ReadAllTextAsync(Path.Combine(fixture.State, name), TestContext.Current.CancellationToken);
    /// <summary>推进正式绑定与布局，不使用真实系统焦点。</summary>
    private static void Pump(Window window) { Dispatcher.UIThread.RunJobs(); window.UpdateLayout(); }
    /// <summary>有界等待异步保存或错误反馈，超时明确失败。</summary>
    private static async Task WaitAsync(Func<bool> done)
    { for (int i = 0; i < 100 && !done(); i++) { Dispatcher.UIThread.RunJobs(); await Task.Delay(20, TestContext.Current.CancellationToken); } Assert.True(done()); }
    /// <summary>拒绝原生文件能力，Headless不代表Finder验收。</summary>
    private sealed class NoPlatform : IPlatformService
    {
        /// <summary>本批不调用Finder。</summary>
        public Task RevealAsync(string path, CancellationToken token = default) => throw new NotSupportedException();
        /// <summary>本批不调用废纸篓。</summary>
        public Task TrashAsync(string path, CancellationToken token = default) => throw new NotSupportedException();
    }
}
