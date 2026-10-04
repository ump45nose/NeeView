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
using NeeView.MacOS.Views;
using NeeView.MacOS.ViewModels;
namespace NeeView.Engine.Tests;

/// <summary>原 .nvpls、集合、书内导航和正式面板链路；自建夹具不写用户状态。</summary>
public sealed class PlaylistTests
{
    /// <summary>v1/v2/原 alpha 错版本读取不覆盖源，真实更名升级并保留未知根/条目字段。</summary>
    [Theory]
    [InlineData("NeeViewPlaylist.1")]
    [InlineData("NeeView.Playlist/2.0.1")]
    [InlineData("NeeView.Playlist/45.0.3981")]
    public async Task OriginalFormatsNamesAndUnknownFieldsRoundTrip(string format)
    {
        using var fixture = new Fixture(); var state = await StateAsync(fixture);
        string path = state.Playlists.Config.DefaultPlaylist; Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var source = new JsonObject { ["Format"] = format, ["Future"] = 42, ["Items"] = format == "NeeViewPlaylist.1"
            ? new JsonArray("/图片/001.png") : new JsonArray(new JsonObject { ["Path"] = "/图片/001.png", ["Name"] = "别名", ["Invalid"] = true, ["FutureItem"] = "保留" }) };
        await File.WriteAllTextAsync(path, source.ToJsonString(), TestContext.Current.CancellationToken);
        var bytes = await File.ReadAllBytesAsync(path, TestContext.Current.CancellationToken);
        await state.Playlists.InitializeAsync(token: TestContext.Current.CancellationToken); var item = Assert.Single(state.Playlists.Current!.Items);
        Assert.Equal(bytes, await File.ReadAllBytesAsync(path, TestContext.Current.CancellationToken));
        Assert.Same(item, await state.Playlists.AddAsync(item.Path, token: TestContext.Current.CancellationToken));
        Assert.Equal(bytes, await File.ReadAllBytesAsync(path, TestContext.Current.CancellationToken));
        await state.Playlists.RenameAsync(item, "  新别名  ", token: TestContext.Current.CancellationToken);
        var saved = JsonNode.Parse(await File.ReadAllTextAsync(path, TestContext.Current.CancellationToken))!;
        Assert.Equal(PlaylistSource.CurrentFormat, saved["Format"]!.GetValue<string>()); Assert.Equal(42, saved["Future"]!.GetValue<int>());
        Assert.Equal("新别名", saved["Items"]![0]!["Name"]!.GetValue<string>());
        if (format != "NeeViewPlaylist.1") { Assert.True(saved["Items"]![0]!["Invalid"]!.GetValue<bool>()); Assert.Equal("保留", saved["Items"]![0]!["FutureItem"]!.GetValue<string>()); }
        await state.Playlists.RenameAsync(item, "001.png", token: TestContext.Current.CancellationToken);
        saved = JsonNode.Parse(await File.ReadAllTextAsync(path, TestContext.Current.CancellationToken))!; Assert.Null(saved["Items"]![0]!["Name"]);
    }

    /// <summary>默认文件惰性创建，Pagemark 不凭空参与文件循环；相对/点段配置规范化后不重复。</summary>
    [Fact]
    public async Task DefaultEnumerationAndShortConfigKeepOriginalRules()
    {
        using var fixture = new Fixture(); var state = await StateAsync(fixture); var hub = state.Playlists;
        await hub.InitializeAsync(token: TestContext.Current.CancellationToken); Assert.Single(hub.PlaylistFiles); Assert.False(File.Exists(hub.Config.DefaultPlaylist));
        await hub.CreateNamedAsync("中文列表", token: TestContext.Current.CancellationToken); Assert.Equal("中文列表.nvpls", hub.Config.CurrentPlaylistRaw);
        await hub.MovePlaylistAsync(1); Assert.Equal(hub.Config.DefaultPlaylist, hub.Current!.Path);
        await hub.MovePlaylistAsync(-1); Assert.Equal("中文列表.nvpls", Path.GetFileName(hub.Current!.Path));
        await state.SaveAsync(null, token: TestContext.Current.CancellationToken); var json = JsonNode.Parse(await File.ReadAllTextAsync(Path.Combine(fixture.State, "UserSetting.json"), TestContext.Current.CancellationToken))!;
        Assert.Equal("中文列表.nvpls", json["Config"]!["Playlist"]!["CurrentPlaylist"]!.GetValue<string>());
        await Assert.ThrowsAsync<ArgumentException>(() => hub.CreateNamedAsync("../覆盖", token: TestContext.Current.CancellationToken));
        await Assert.ThrowsAsync<IOException>(() => hub.CreateNamedAsync("中文列表", token: TestContext.Current.CancellationToken));
        var config = new PlaylistConfig { PlaylistFolderRaw = Path.Combine(fixture.State, "Playlists", "..", "Playlists"), CurrentPlaylistRaw = "中文列表.nvpls" };
        var normalized = new PlaylistHub(config); await normalized.InitializeAsync(token: TestContext.Current.CancellationToken); Assert.Equal(2, normalized.PlaylistFiles.Count);
    }

    /// <summary>损坏/未来格式切换失败保留当前对象与源字节；初始化坏源也禁止空集合覆盖。</summary>
    [Fact]
    public async Task BadAndFutureSourcesNeverOverwriteUsableList()
    {
        using var fixture = new Fixture(); var state = await StateAsync(fixture); var hub = state.Playlists;
        await hub.AddAsync(Path.Combine(fixture.Images, "001.png"), token: TestContext.Current.CancellationToken); var current = hub.Current;
        foreach (var text in new[] { "损坏", """{"Format":"NeeView.Playlist/9.0.0","Items":[]} """ })
        {
            var bad = Path.Combine(fixture.Root, "bad.nvpls"); await File.WriteAllTextAsync(bad, text, TestContext.Current.CancellationToken);
            await Assert.ThrowsAnyAsync<Exception>(() => hub.SwitchAsync(bad, token: TestContext.Current.CancellationToken)); Assert.Same(current, hub.Current);
            Assert.Equal(text, await File.ReadAllTextAsync(bad, TestContext.Current.CancellationToken));
            var broken = new PlaylistHub(new() { PlaylistFolderRaw = fixture.Root, CurrentPlaylistRaw = bad });
            await broken.InitializeAsync(token: TestContext.Current.CancellationToken); Assert.Null(broken.Current); Assert.NotNull(broken.Error);
            await Assert.ThrowsAsync<IOException>(() => broken.AddAsync("/不允许覆盖.png", token: TestContext.Current.CancellationToken));
            Assert.Equal(text, await File.ReadAllTextAsync(bad, TestContext.Current.CancellationToken));
        }
    }

    /// <summary>原删除即时索引/逆序恢复、首插、单项移动和 Path 自然排序保持对象身份。</summary>
    [Fact]
    public async Task CollectionEditsAndReverseRecoveryKeepIdentity()
    {
        using var fixture = new Fixture(); var state = await StateAsync(fixture); var hub = state.Playlists;
        var items = new List<PlaylistItem>(); foreach (var name in new[] { "1", "10", "2", "3", "4" }) items.Add((await hub.AddAsync(Path.Combine(fixture.Images, name + ".png"), token: TestContext.Current.CancellationToken))!);
        await hub.RemoveAsync([items[3], items[0], items[2]], token: TestContext.Current.CancellationToken); Assert.Equal(new[] { items[1], items[4] }, hub.Current!.Items);
        await hub.RestoreAsync(token: TestContext.Current.CancellationToken); Assert.Equal(items, hub.Current.Items); Assert.False(hub.Current.CanRestore);
        // 恢复本身必须已落盘，不能由后面的排序写入掩盖恢复遗漏。
        var reopened = new PlaylistHub(hub.Config);
        await reopened.InitializeAsync(token: TestContext.Current.CancellationToken);
        Assert.Equal(items.Select(item => item.Path), reopened.Current!.Items.Select(item => item.Path));
        await hub.MoveAsync(items[0], items[3], token: TestContext.Current.CancellationToken); Assert.Equal(items[0], hub.Current.Items[3]);
        await hub.SortAsync(token: TestContext.Current.CancellationToken); Assert.Equal(new[] { "1.png", "2.png", "3.png", "4.png", "10.png" }, hub.Current.Items.Select(item => item.Name));
        hub.Config.IsFirstIn = true; var first = await hub.AddAsync("/新图片.png", token: TestContext.Current.CancellationToken); Assert.Same(first, hub.Current.Items[0]);
    }

    /// <summary>外部修改与取消失败不污染集合、别名、选中对象、删除恢复栈或外部文件。</summary>
    [Fact]
    public async Task FailedEditsRollbackInPlaceAndKeepRecovery()
    {
        using var fixture = new Fixture(); var state = await StateAsync(fixture); var hub = state.Playlists;
        var first = (await hub.AddAsync(Path.Combine(fixture.Images, "001.png"), token: TestContext.Current.CancellationToken))!;
        var second = (await hub.AddAsync(Path.Combine(fixture.Images, "002.png"), token: TestContext.Current.CancellationToken))!;
        await hub.RemoveAsync([second], token: TestContext.Current.CancellationToken); hub.SelectedItem = first;
        string external = """{"Format":"NeeView.Playlist/2.0.1","Items":[],"External":true}""";
        await File.WriteAllTextAsync(hub.Current!.Path, external, TestContext.Current.CancellationToken);
        await Assert.ThrowsAsync<IOException>(() => hub.RenameAsync(first, "失败别名", token: TestContext.Current.CancellationToken));
        Assert.Same(first, Assert.Single(hub.Current.Items)); Assert.Equal("001.png", first.Name); Assert.Same(first, hub.SelectedItem); Assert.True(hub.Current.CanRestore);
        await Assert.ThrowsAsync<IOException>(() => hub.RestoreAsync(token: TestContext.Current.CancellationToken)); Assert.Single(hub.Current.Items); Assert.True(hub.Current.CanRestore);
        Assert.Equal(external, await File.ReadAllTextAsync(hub.Current.Path, TestContext.Current.CancellationToken));
        using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => hub.RemoveAsync([first], cancelled.Token)); Assert.Same(first, Assert.Single(hub.Current.Items));
        Assert.Empty(Directory.EnumerateFiles(hub.Config.PlaylistFolder, "*.tmp"));
    }

    /// <summary>书内标记默认不循环，首尾补项按原算法；排序后的索引和单页书边界正确。</summary>
    [Fact]
    public async Task MarkerNavigationMatchesOriginalTerminalAndLoopAlgorithm()
    {
        using var fixture = new Fixture(); var state = await StateAsync(fixture); await using var operation = fixture.Operation(state);
        await operation.OpenAsync(fixture.Images, token: TestContext.Current.CancellationToken); var book = operation.Book!;
        book.Marker.SetMarkers([book.Pages[1], book.Pages[3]]);
        Assert.Same(book.Pages[3], book.Marker.GetNearMarkedPage(1, 1, false, false));
        Assert.Null(book.Marker.GetNearMarkedPage(3, 1, false, false));
        Assert.Same(book.Pages[1], book.Marker.GetNearMarkedPage(3, 1, true, false));
        Assert.Same(book.Pages[4], book.Marker.GetNearMarkedPage(3, 1, false, true));
        Assert.Same(book.Pages[0], book.Marker.GetNearMarkedPage(1, -1, false, true));
        var single = new Book(new DummyArchive(), [book.Pages[0]], new()); single.Marker.SetMarkers(single.Pages); Assert.Null(single.Marker.GetNearMarkedPage(0, 1, true, true));
    }

    /// <summary>双页反向主图取范围首项，临时胶片选择不改变登记；菜单忽略固定 On 参数。</summary>
    [Fact]
    public async Task MainPageAndPanelRegistrationIgnoreUncommittedSelection()
    {
        using var fixture = new Fixture(); var state = await StateAsync(fixture); await using var operation = fixture.Operation(state);
        await operation.Playlists.InitializeAsync(token: TestContext.Current.CancellationToken); await operation.OpenAsync(fixture.Images, token: TestContext.Current.CancellationToken);
        await operation.ApplySettingAsync(setting => { setting.PageMode = PageMode.WidePage; setting.BookReadOrder = PageReadOrder.RightToLeft; setting.IsSupportedWidePage = false; });
        await operation.JumpAsync(2, backwards: true); var main = operation.Book!.CurrentPage!;
        Assert.Equal(operation.Frame!.FrameRange.Min.Index, main.Index);
        operation.PageSelector.SetSelectedIndex(this, 4, true);
        await operation.AddSelectedPlaylistPageAsync(); Assert.Equal(main.EntryFullName, Assert.Single(operation.Playlists.Current!.Items).Path);
        state.SetCommandParameter("TogglePlaylistItem", new ToggleCommandParameter { ToggleMode = ToggleMode.On });
        await operation.TogglePlaylistItemAsync(); Assert.Single(operation.Playlists.Current.Items); Assert.True(main.IsMarked);
        await operation.TogglePlaylistItemAsync(true); Assert.Empty(operation.Playlists.Current.Items); Assert.False(main.IsMarked);
        await operation.ApplySettingAsync(setting => setting.IsSupportedDividePage = true);
        await operation.TogglePlaylistItemAsync(); Assert.Single(operation.Playlists.Current.Items);
    }

    /// <summary>真实宽图两半显示共用原页面登记，翻到后半页不会重复或转移标记。</summary>
    [Fact]
    public async Task SplitImagePartsShareOneOriginalPlaylistTarget()
    {
        using var fixture = new Fixture();
        using (var wide = new ImageMagick.MagickImage(ImageMagick.MagickColors.Gray, 900, 400)) wide.Write(Path.Combine(fixture.Images, "003.png"));
        var state = await StateAsync(fixture); await using var operation = fixture.Operation(state);
        await operation.Playlists.InitializeAsync(TestContext.Current.CancellationToken); await operation.OpenAsync(fixture.Images, TestContext.Current.CancellationToken);
        await operation.ApplySettingAsync(setting => { setting.PageMode = PageMode.SinglePage; setting.IsSupportedDividePage = true; setting.IsSupportedWidePage = false; });
        await operation.JumpAsync(2); var page = operation.Book!.CurrentPage!; Assert.Equal(0, operation.Position.Part);
        await operation.TogglePlaylistItemAsync(); await operation.MoveAsync(1);
        Assert.Same(page, operation.Book.CurrentPage); Assert.Equal(1, operation.Position.Part); Assert.True(page.IsMarked);
        Assert.Equal(page.EntryFullName, Assert.Single(operation.Playlists.Current!.Items).Path);
        await operation.TogglePlaylistItemAsync(); Assert.Empty(operation.Playlists.Current.Items); Assert.False(page.IsMarked);
    }

    /// <summary>当前全局列表切换、排序和循环参数重映射标记；分组/过滤顺序供列表导航使用。</summary>
    [Fact]
    public async Task GlobalMarkersFollowSortingFilteringAndListSwitch()
    {
        using var fixture = new Fixture(); var state = await StateAsync(fixture); await using var operation = fixture.Operation(state);
        await operation.Playlists.InitializeAsync(token: TestContext.Current.CancellationToken); await operation.OpenAsync(fixture.Images, token: TestContext.Current.CancellationToken); var book = operation.Book!;
        await operation.Playlists.AddAsync(book.Pages[1].EntryFullName, token: TestContext.Current.CancellationToken); await operation.Playlists.AddAsync(Path.Combine(fixture.Zip, "003.png"), token: TestContext.Current.CancellationToken); await operation.Playlists.AddAsync(book.Pages[3].EntryFullName, token: TestContext.Current.CancellationToken);
        operation.Playlists.Config.IsGroupBy = true; Assert.Equal(new[] { "002.png", "004.png", "003.png" }, operation.Playlists.GetViewItems(book).Select(item => item.Name));
        operation.Playlists.Config.IsCurrentBookFilterEnabled = true; Assert.Equal(2, operation.Playlists.GetViewItems(book).Count);
        await operation.MovePlaylistItemInBookAsync(1); Assert.Equal("002.png", book.CurrentPage!.EntryName);
        await operation.ApplySettingAsync(setting => setting.SortMode = PageSortMode.FileNameDescending);
        Assert.Equal(new[] { 1, 3 }, book.Marker.Markers.Select(page => page.Index).Order().ToArray());
        state.SetCommandParameter("NextPlaylistItemInBook", new MovePlaylistItemInBookCommandParameter { IsLoop = true });
        await operation.MovePlaylistItemInBookAsync(1); Assert.Equal("004.png", book.CurrentPage!.EntryName);
        await operation.Playlists.SwitchAsync(operation.Playlists.Config.PagemarkPlaylist, token: TestContext.Current.CancellationToken); Assert.Empty(book.Marker.Markers);
        await operation.Playlists.SwitchAsync(operation.Playlists.Config.DefaultPlaylist, token: TestContext.Current.CancellationToken); Assert.Equal(2, book.Marker.Markers.Count);
    }

    /// <summary>归档内部登记走原加载链，缺失内部页保持旧书；名称带 .cbz 的真实目录仍按普通图片打开。</summary>
    [Fact]
    public async Task ArchiveLogicalTargetsAndMissingEntriesKeepBook()
    {
        using var fixture = new Fixture(); var state = await StateAsync(fixture); await using var operation = fixture.Operation(state);
        await operation.Playlists.InitializeAsync(token: TestContext.Current.CancellationToken); await operation.OpenAsync(fixture.Images, token: TestContext.Current.CancellationToken);
        var target = (await operation.Playlists.AddAsync(Path.Combine(fixture.Zip, "003.png"), token: TestContext.Current.CancellationToken))!;
        await operation.OpenPlaylistItemAsync(target); Assert.Equal(fixture.Zip, operation.Book!.Path); Assert.Equal("003.png", operation.Book.CurrentPage!.EntryName);
        var current = operation.Book;
        var missing = (await operation.Playlists.AddAsync(Path.Combine(fixture.Zip, "不存在.png"), token: TestContext.Current.CancellationToken))!;
        await operation.OpenPlaylistItemAsync(missing); Assert.Same(current, operation.Book); Assert.Contains("指定页面", operation.Error!);
        string directory = Path.Combine(fixture.Root, "真实目录.cbz"); Directory.CreateDirectory(directory); File.Copy(Path.Combine(fixture.Images, "001.png"), Path.Combine(directory, "001.png"));
        await operation.OpenAsync(Path.Combine(directory, "001.png"), token: TestContext.Current.CancellationToken); Assert.True(operation.Book!.Source.IsDirectory); Assert.Equal(directory, operation.Book.Path);
    }

    /// <summary>切换列表后晚到旧项打开不能提交书籍；真实来源必须释放。</summary>
    [Fact]
    public async Task LatePlaylistOpenCannotCommitAfterSwitch()
    {
        using var fixture = new Fixture(); var state = await StateAsync(fixture); var factory = new PausingFactory(fixture.Zip);
        await using var operation = new BookOperation(factory, new NeeView.Backends.MagickImageDecoder(), state);
        await operation.Playlists.InitializeAsync(token: TestContext.Current.CancellationToken); await operation.OpenAsync(fixture.Images, token: TestContext.Current.CancellationToken); var current = operation.Book;
        var item = (await operation.Playlists.AddAsync(Path.Combine(fixture.Zip, "002.png"), token: TestContext.Current.CancellationToken))!;
        var open = operation.OpenPlaylistItemAsync(item); await factory.Started.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        await operation.Playlists.SwitchAsync(operation.Playlists.Config.PagemarkPlaylist, token: TestContext.Current.CancellationToken); factory.Release.SetResult(); await open;
        Assert.Same(current, operation.Book); Assert.Empty(operation.Book!.Marker.Markers); Assert.False(operation.IsLoading);
    }

    /// <summary>正式面板原菜单占位、多选删除恢复、标记纯刷新与文本输入隔离。</summary>
    [AvaloniaFact]
    public async Task FormalPanelEditsAndMarksStaySeparateFromReaderRefresh()
    {
        using var fixture = new Fixture(); var state = await StateAsync(fixture); var operation = fixture.Operation(state);
        var model = new ReaderWorkspaceViewModel(operation, new CommandTable(operation), state); var window = new MainWindow();
        window.Bind(model, new BitmapFactory(new NeeView.Backends.MagickImageDecoder()), new NoPlatform()); window.Show();
        try
        {
            await window.OpenAsync(fixture.Images); model.ShowPanel("PlaylistPanel");
            var view = window.FindControl<PlaylistView>("PlaylistPanelView")!;
            await WaitAsync(() => operation.Playlists.Current is not null); Pump(window);
            Assert.True(window.IsCommandAvailable("ToggleVisiblePlaylist"));
            var menu = view.CreateMoreMenu(); Assert.Equal(8, menu.Items.OfType<MenuItem>().Count(item => !item.IsEnabled));
            int refreshes = 0; model.Refreshed += (_, _) => refreshes++;
            await operation.TogglePlaylistItemAsync(); Pump(window); Assert.Equal(0, refreshes); Assert.Single(model.MarkerIndices);
            var list = view.FindControl<ListBox>("PlaylistItems")!; Assert.Single(list.Items);
            await operation.Playlists.AddAsync(Path.Combine(fixture.Images, "003.png"), token: TestContext.Current.CancellationToken); Pump(window);
            foreach (var row in list.Items) list.SelectedItems!.Add(row);
            await view.RemoveSelectedAsync(); Pump(window); Assert.Empty(list.Items);
            view.FindControl<Button>("PlaylistRestore")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); await WaitAsync(() => list.Items.Count == 2); Pump(window);
            Assert.Equal(2, list.Items.Count); Assert.Equal(0, refreshes);
            var input = window.FindControl<TextBox>("AddressBar")!; input.Focus(); int marks = operation.Playlists.Current!.Items.Count;
            window.KeyPress(Key.M, RawInputModifiers.Control, PhysicalKey.M, null); window.KeyRelease(Key.M, RawInputModifiers.Control, PhysicalKey.M, null); Pump(window);
            Assert.Equal(marks, operation.Playlists.Current!.Items.Count);
            Config.Current.FilmStrip.IsEnabled = true; Config.Current.FilmStrip.IsVisiblePlaylistMark = true; Config.Current.Slider.IsVisiblePlaylistMark = true;
            model.RefreshSelection(); model.RefreshPanels(); await window.FindControl<ThumbnailView>("DockFilmStripSocket")!.RefreshAsync(); Pump(window);
            var markers = window.FindControl<PageMarkersView>("PageMarkers")!; Assert.True(markers.IsVisible); Assert.Equal(new[] { 0, 2 }, markers.Indices.Order().ToArray());
            var settings = new SettingsWindow(model); settings.Show(window);
            Assert.True(settings.FindControl<CheckBox>("SliderMarks")!.IsChecked); Assert.True(settings.FindControl<CheckBox>("FilmMarks")!.IsChecked);
            settings.Close();
            // 无修饰指针必须替换选择；Toggle 模式会累加选择而误开首项，真机发现后补此回归。
            var secondRow = (ListBoxItem)list.ContainerFromIndex(1)!;
            var point = secondRow.TranslatePoint(new Point(20, secondRow.Bounds.Height / 2), window)!.Value;
            window.MouseDown(point, MouseButton.Left); window.MouseUp(point, MouseButton.Left);
            await WaitAsync(() => operation.Book!.CurrentPage!.EntryName == "003.png"); Pump(window);
            Assert.Single(list.SelectedItems!); Assert.Equal("003.png", ((PlaylistRow)list.SelectedItem!).Name);
            // 更新集合不能丢掉列表焦点，否则原 Up/Down 全局键会误切书。
            window.KeyPress(Key.Up, RawInputModifiers.None, PhysicalKey.ArrowUp, null); window.KeyRelease(Key.Up, RawInputModifiers.None, PhysicalKey.ArrowUp, null); Pump(window);
            Assert.Equal("003.png", operation.Book!.CurrentPage!.EntryName); Assert.Equal("001.png", ((PlaylistRow)list.SelectedItem!).Name);
            window.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null); window.KeyRelease(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null);
            await WaitAsync(() => operation.Book.CurrentPage!.EntryName == "001.png"); Pump(window);
            // 菜单/快捷键导航也必须回显 Hub 当前项，不能只切正文却留下旧行高亮。
            await operation.MovePlaylistItemAsync(1); Pump(window);
            Assert.Equal("003.png", operation.Book.CurrentPage!.EntryName);
            Assert.Equal("003.png", ((PlaylistRow)list.SelectedItem!).Name);
            var phase = Environment.GetEnvironmentVariable("NEEVIEW_ACCEPTANCE_PHASE") ?? "p2-playlist";
            using var image = new RenderTargetBitmap(new PixelSize(1200, 800)); image.Render(window);
            image.Save(Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, $"../../../../../acceptance/{phase}-playlist-layout.png")), PngBitmapEncoderOptions.Default);
        }
        finally { await window.PrepareShutdownAsync(); window.Close(); }
    }

    /// <summary>自建 JSON 装配真实 SaveData，默认目录隔离于用户 Profile。</summary>
    private static async Task<SaveData> StateAsync(Fixture fixture) { var state = new SaveData(fixture.State); await state.LoadAsync(TestContext.Current.CancellationToken); return state; }
    /// <summary>推进正式绑定和布局。</summary>
    private static void Pump(Window window) { Dispatcher.UIThread.RunJobs(); window.UpdateLayout(); }
    /// <summary>等待真实异步编辑，不以按钮点击当作已落盘。</summary>
    private static async Task WaitAsync(Func<bool> done) { for (int i = 0; i < 100 && !done(); i++) { Dispatcher.UIThread.RunJobs(); await Task.Delay(10, TestContext.Current.CancellationToken); } Assert.True(done()); Dispatcher.UIThread.RunJobs(); }
    /// <summary>仅暂停目标归档打开以复现切换竞争，其余来源使用正式后端。</summary>
    private sealed class PausingFactory(string archivePath) : IArchiveFactory
    {
        private readonly NeeView.Backends.ArchiveFactory _factory = new();
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        /// <summary>打开完成前等待测试释放，取消仍沿正式链路。</summary>
        public async Task<Archive> OpenAsync(string path, CancellationToken token) { if (path.StartsWith(archivePath, StringComparison.Ordinal)) { Started.TrySetResult(); await Release.Task.WaitAsync(token); } return await _factory.OpenAsync(path, token); }
        /// <summary>目录枚举复用实际后端。</summary>
        public Task<IReadOnlyList<FolderItem>> ListFoldersAsync(string path, CancellationToken token) => _factory.ListFoldersAsync(path, token);
        /// <summary>书架枚举复用实际后端。</summary>
        public Task<IReadOnlyList<FolderItem>> ListBooksAsync(string path, CancellationToken token) => _factory.ListBooksAsync(path, token);
    }
    /// <summary>专项不执行 Finder/废纸篓。</summary>
    private sealed class NoPlatform : IPlatformService
    {
        /// <summary>拒绝测试范围外系统操作。</summary>
        public Task RevealAsync(string path, CancellationToken token = default) => throw new NotSupportedException();
        /// <summary>拒绝测试范围外文件删除。</summary>
        public Task TrashAsync(string path, CancellationToken token = default) => throw new NotSupportedException();
    }
}
