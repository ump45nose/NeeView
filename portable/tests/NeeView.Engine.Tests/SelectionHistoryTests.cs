using System.Text.Json.Nodes;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using NeeView;
using NeeView.Backends;
using NeeView.Collections;
using NeeView.MacOS.ViewModels;
using NeeView.MacOS.Views;
namespace NeeView.Engine.Tests;

/// <summary>原选择、滑条和进程内导航历史专项回归；使用真实目录/ZIP 与正式控件。</summary>
public sealed class SelectionHistoryTests
{
    /// <summary>环形容量、逻辑顺序与后退后的新分支，防止前进到被截断记录。</summary>
    [Fact]
    public void OriginalRingHistoryKeepsCapacityAndCutsForwardBranch()
    {
        var history = new HistoryLimitedCollection<string>(100);
        for (int i = 0; i < 110; i++) history.Add(i.ToString());
        Assert.Equal("10", history.GetHistory(0)); Assert.Equal("109", history.GetCurrent());
        history.Move(-2); Assert.Equal("107", history.GetCurrent());
        Assert.Equal("108", history.GetNext()); history.Add("新分支");
        Assert.False(history.CanNext()); Assert.Equal("107", history.GetPrevious());
        Assert.Null(history.GetHistory(-1)); Assert.Null(history.GetHistory(99));
        history.Add(null); history.TrimEnd(null); Assert.Equal("新分支", history.GetCurrent());
    }

    /// <summary>保留原 96/32 宽度及默认滚轮配置，不擅自添加 512 的 setter 上限。</summary>
    [Fact]
    public void OriginalFilmAndSliderDefaultsRemain()
    {
        var film = new FilmStripConfig(); Assert.Equal(96, film.ImageWidth); Assert.True(film.IsDetailPopupEnabled);
        Assert.Equal(FilmStripMouseWheelAction.MoveSelection, film.MouseWheelAction);
        film.ImageWidth = 8; Assert.Equal(32, film.ImageWidth); film.ImageWidth = 768; Assert.Equal(768, film.ImageWidth);
        var slider = new SliderConfig(); Assert.True(slider.IsSliderLinkedFilmStrip); Assert.False(slider.IsSyncPageMode);
        Assert.Equal(SliderDirection.SyncBookReadDirection, slider.SliderDirection);
        Assert.Equal(-246, FilmStrip.ScrollIntoView(0, 100, 108, 600, 0, true));
        Assert.Equal(10446, FilmStrip.ScrollIntoView(99, 100, 108, 600, 0, true));
    }

    /// <summary>临时选择不移动正文，视觉方向与滚轮条目方向分别保留；请求从中央开始。</summary>
    [Theory]
    [InlineData(SliderDirection.LeftToRight, false)]
    [InlineData(SliderDirection.RightToLeft, true)]
    [InlineData(SliderDirection.SyncBookReadDirection, true)]
    public async Task FilmSelectionAndRequestDirectionMatchOriginal(SliderDirection direction, bool reversed)
    {
        using var fixture = new Fixture(); var state = new SaveData(fixture.State); await state.LoadAsync(TestContext.Current.CancellationToken);
        await using var operation = fixture.Operation(state); await operation.OpenAsync(fixture.Images, TestContext.Current.CancellationToken);
        Config.Current.Slider.SliderDirection = direction; await operation.JumpAsync(2);
        Assert.Equal(reversed, operation.FilmStrip.IsSliderDirectionReversed);
        operation.FilmStrip.MoveSelectedIndex(1); Assert.Equal(reversed ? 1 : 3, operation.PageSelector.SelectedIndex);
        Assert.Equal(2, operation.Book!.CurrentPage!.Index);
        operation.PageSelector.SetSelectedIndex(this, 2, true);
        operation.FilmStrip.MoveSelectedIndex(1, reversed); Assert.Equal(3, operation.PageSelector.SelectedIndex);
        var requested = operation.FilmStrip.RequestThumbnail(1, 3, 2, 0);
        Assert.Equal(new[] { 2, 1, 3, 0, 4 }, requested.Select(p => p.Index));
    }

    /// <summary>原 MoveToNextStep 从最小显示索引计算，负步长仍向前生成，超尾反向生成。</summary>
    [Fact]
    public async Task SizeNavigationKeepsStaticWideAlignmentAndTailDirection()
    {
        using var fixture = new Fixture(); var state = new SaveData(fixture.State); await state.LoadAsync(TestContext.Current.CancellationToken);
        Config.Current.Book.IsStaticWidePage = true;
        Config.Current.BookSettingDefault.PageMode = PageMode.WidePage;
        Config.Current.BookSettingDefault.IsSupportedSingleFirstPage = true;
        Config.Current.BookSettingDefault.IsSupportedSingleLastPage = true;
        await using var operation = fixture.Operation(state); await operation.OpenAsync(fixture.Images, TestContext.Current.CancellationToken);
        await operation.JumpAsync(1); await operation.MoveSizeAsync(1); Assert.Equal(1, operation.Frame!.FrameRange.Min.Index);
        await operation.MoveSizeAsync(3); Assert.Equal(3, operation.Frame.FrameRange.Min.Index);
        await operation.MoveSizeAsync(-1); Assert.Equal(1, operation.Frame.FrameRange.Min.Index); Assert.Equal(1, operation.MoveDirection);
        await operation.MoveSizeAsync(100); Assert.Equal(4, operation.Frame.FrameRange.Min.Index); Assert.Equal(-1, operation.MoveDirection);
        var size = new MoveSizePageCommandParameter(); Assert.Equal(10, size.Size);
        size.Size = -1; Assert.Equal(0, size.Size); size.Size = 1001; Assert.Equal(1000, size.Size);
    }

    /// <summary>原终点判定以显示范围端点为准，越界命令不可把首尾横图切换到另一半。</summary>
    [Fact]
    public async Task SizeNavigationAtTerminalsKeepsDividedFrame()
    {
        using var fixture = new Fixture();
        using (var image = new ImageMagick.MagickImage(ImageMagick.MagickColors.Red, 1600, 600))
        { image.Write(Path.Combine(fixture.Images, "001.png")); image.Write(Path.Combine(fixture.Images, "005.png")); }
        var state = new SaveData(fixture.State); await state.LoadAsync(TestContext.Current.CancellationToken);
        Config.Current.BookSettingDefault.IsSupportedDividePage = true;
        await using var operation = fixture.Operation(state); await operation.OpenAsync(fixture.Images, TestContext.Current.CancellationToken);
        var frame = operation.Frame; var position = operation.Position;
        await operation.MoveSizeAsync(-10); Assert.Same(frame, operation.Frame); Assert.Equal(position, operation.Position);
        await operation.JumpAsync(4, true); frame = operation.Frame; position = operation.Position;
        Assert.Equal(1, position.Part); await operation.MoveSizeAsync(10); Assert.Same(frame, operation.Frame); Assert.Equal(position, operation.Position);
    }

    /// <summary>指定索引经原滑条双页对齐；同步模式忽略一页抖动，末页单独时仍优先。</summary>
    [Fact]
    public async Task SliderUsesOriginalWideSyncRules()
    {
        using var fixture = new Fixture(); var state = new SaveData(fixture.State); await state.LoadAsync(TestContext.Current.CancellationToken);
        Config.Current.BookSettingDefault.PageMode = PageMode.WidePage;
        await using var operation = fixture.Operation(state); await operation.OpenAsync(fixture.Images, TestContext.Current.CancellationToken);
        await operation.JumpAsync(2); Config.Current.Slider.IsSyncPageMode = true;
        Assert.Equal(2, operation.FilmStrip.GetFixedSliderIndex(3, 2));
        Assert.Equal(4, operation.FilmStrip.GetFixedSliderIndex(4, 2));
        Config.Current.Book.IsStaticWidePage = true; Config.Current.Slider.IsSyncPageMode = false;
        Assert.Equal(2, operation.FilmStrip.GetFixedSliderIndex(3, 2));
        await operation.ApplySettingAsync(s => s.IsSupportedSingleLastPage = true);
        Assert.Equal(4, operation.FilmStrip.GetFixedSliderIndex(4, 2));
    }

    /// <summary>页面历史跨书恢复条目而非数字索引，回放保留前进，普通导航截断前进。</summary>
    [Fact]
    public async Task PageHistoryReplaysAcrossBooksAndNewNavigationCutsBranch()
    {
        using var fixture = new Fixture(); var state = new SaveData(fixture.State); await state.LoadAsync(TestContext.Current.CancellationToken);
        await using var operation = fixture.Operation(state); await operation.OpenAsync(fixture.Images, TestContext.Current.CancellationToken);
        await operation.JumpAsync(2); await operation.OpenAsync(fixture.Zip, TestContext.Current.CancellationToken); await operation.JumpAsync(1);
        await operation.NavigateHistoryAsync(-1); Assert.Equal(fixture.Zip, operation.Book!.Path); Assert.Equal(0, operation.PageSelector.SelectedIndex);
        await operation.NavigateHistoryAsync(-1); Assert.Equal(fixture.Images, operation.Book.Path); Assert.Equal("003.png", operation.Book.CurrentPage!.EntryName);
        Assert.True(operation.PageHistory.CanMoveToNext());
        await operation.NavigateHistoryAsync(1); Assert.Equal(fixture.Zip, operation.Book.Path); Assert.Equal(0, operation.PageSelector.SelectedIndex);
        await operation.JumpAsync(4); Assert.False(operation.PageHistory.CanMoveToNext());
        await operation.NavigateHistoryAsync(-1); Assert.Equal(0, operation.PageSelector.SelectedIndex);
    }

    /// <summary>打开顺序历史与访问排序分开；读取失败保持当前书、目标游标及重试机会。</summary>
    [Fact]
    public async Task BookHistoryFailureRetriesAndKeepsStoredOrder()
    {
        using var fixture = new Fixture(); var state = new SaveData(fixture.State); await state.LoadAsync(TestContext.Current.CancellationToken);
        await using var operation = fixture.Operation(state); await operation.OpenAsync(fixture.Images, TestContext.Current.CancellationToken);
        await operation.JumpAsync(4); await operation.OpenAsync(fixture.Zip, TestContext.Current.CancellationToken); await operation.SaveAsync();
        var order = state.HistoryEntries.Select(e => e.Path).ToArray();
        var timestamp = state.HistoryEntries.Single(e => e.Path == fixture.Images).LastAccessTime;
        var moved = fixture.Images + "暂不可访问"; Directory.Move(fixture.Images, moved);
        try
        {
            await operation.NavigateHistoryAsync(-1, true); Assert.Equal(fixture.Zip, operation.Book!.Path); Assert.NotNull(operation.Error);
            Assert.Equal(fixture.Images, operation.BookHistory.GetTarget(-1)); Assert.False(operation.BookHistory.CanMoveToNext());
        }
        finally { Directory.Move(moved, fixture.Images); }
        await operation.NavigateHistoryAsync(-1, true); Assert.Null(operation.Error); Assert.Equal(fixture.Images, operation.Book!.Path);
        Assert.Equal(4, operation.PageSelector.SelectedIndex); Assert.True(operation.BookHistory.CanMoveToNext());
        await operation.SaveAsync(); Assert.Equal(order, state.HistoryEntries.Select(e => e.Path));
        Assert.Equal(timestamp, state.HistoryEntries.Single(e => e.Path == fixture.Images).LastAccessTime);
        await operation.NavigateHistoryAsync(1, true); Assert.Equal(fixture.Zip, operation.Book.Path);
    }

    /// <summary>页面历史条目不存在时保留游标，修复来源后重放同一个原条目。</summary>
    [Fact]
    public async Task MissingPageHistoryRetriesWithoutSkippingTarget()
    {
        using var fixture = new Fixture(); var state = new SaveData(fixture.State); await state.LoadAsync(TestContext.Current.CancellationToken);
        await using var operation = fixture.Operation(state); await operation.OpenAsync(fixture.Images, TestContext.Current.CancellationToken);
        await operation.JumpAsync(2); await operation.OpenAsync(fixture.Zip, TestContext.Current.CancellationToken);
        var target = operation.PageHistory.GetTarget(-1); var file = Path.Combine(fixture.Images, "003.png"); var backup = Path.Combine(fixture.Root, "003.png");
        File.Move(file, backup);
        try
        {
            await operation.NavigateHistoryAsync(-1); Assert.Equal(fixture.Zip, operation.Book!.Path);
            Assert.Contains("历史页面已不存在", operation.Error); Assert.Equal(target, operation.PageHistory.GetTarget(-1));
        }
        finally { File.Move(backup, file); }
        await operation.NavigateHistoryAsync(-1); Assert.Equal(fixture.Images, operation.Book!.Path); Assert.Equal("003.png", operation.Book.CurrentPage!.EntryName);
    }

    /// <summary>历史加载被普通打开取代时，晚到结果不能提交书籍或推进历史游标。</summary>
    [Fact]
    public async Task SupersededHistoryOpenKeepsCursorAndLatestBook()
    {
        using var fixture = new Fixture(); var state = new SaveData(fixture.State); await state.LoadAsync(TestContext.Current.CancellationToken);
        var factory = new PausedFactory();
        await using var operation = new BookOperation(factory, new MagickImageDecoder(), state);
        await operation.OpenAsync(fixture.Images, TestContext.Current.CancellationToken); await operation.OpenAsync(fixture.Zip, TestContext.Current.CancellationToken);
        factory.PausePath = fixture.Images;
        var replay = operation.NavigateHistoryAsync(-1, true); await factory.Started.Task.WaitAsync(TestContext.Current.CancellationToken);
        await operation.OpenAsync(fixture.Zip, TestContext.Current.CancellationToken); await replay;
        Assert.Equal(fixture.Zip, operation.Book!.Path); Assert.Equal(fixture.Images, operation.BookHistory.GetTarget(-1));
        factory.PausePath = null; await operation.NavigateHistoryAsync(-1, true); Assert.Equal(fixture.Images, operation.Book.Path);
    }

    /// <summary>胶片条/滑条及共享步长保存原 JSON 分支，未知参数和快捷键不被覆盖。</summary>
    [Fact]
    public async Task FilmSettingsAndSharedSizeParameterKeepUnknownJson()
    {
        using var fixture = new Fixture(); Directory.CreateDirectory(fixture.State);
        await File.WriteAllTextAsync(Path.Combine(fixture.State, "UserSetting.json"), """{"Config":{"FilmStrip":{"Future":7},"Slider":{"Future":8}},"Commands":{"PrevSizePage":{"ShortCutKey":"Shift+Right","Parameter":{"Size":3,"Future":9}}}}""", TestContext.Current.CancellationToken);
        var state = new SaveData(fixture.State); await state.LoadAsync(TestContext.Current.CancellationToken);
        Assert.Equal(3, state.GetMoveSizeParameter().Size);
        Config.Current.FilmStrip.ImageWidth = 128; Config.Current.FilmStrip.IsSelectedCenter = true;
        Config.Current.Slider.SliderDirection = SliderDirection.LeftToRight;
        state.SetCommandParameter("PrevSizePage", new MoveSizePageCommandParameter { Size = 2 });
        await state.SaveAsync(null, TestContext.Current.CancellationToken);
        var json = JsonNode.Parse(await File.ReadAllTextAsync(Path.Combine(fixture.State, "UserSetting.json"), TestContext.Current.CancellationToken))!;
        Assert.Equal(7, json["Config"]!["FilmStrip"]!["Future"]!.GetValue<int>());
        Assert.Equal(8, json["Config"]!["Slider"]!["Future"]!.GetValue<int>());
        Assert.Equal(9, json["Commands"]!["PrevSizePage"]!["Parameter"]!["Future"]!.GetValue<int>());
        Assert.Equal("Shift+Right", json["Commands"]!["PrevSizePage"]!["ShortCutKey"]!.GetValue<string>());
        var fresh = new SaveData(fixture.State); await fresh.LoadAsync(TestContext.Current.CancellationToken);
        Assert.Equal(128, Config.Current.FilmStrip.ImageWidth); Assert.Equal(2, fresh.GetMoveSizeParameter().Size);
        Assert.Equal(SliderDirection.LeftToRight, Config.Current.Slider.SliderDirection);
    }

    /// <summary>真实控件滚轮三模式、方向键作用域、Enter 确认和修饰键点击隔离。</summary>
    [AvaloniaFact]
    public async Task FilmInputSelectsBeforeConfirmationAndUsesThreeWheelModes()
    {
        using var fixture = new Fixture(); var state = new SaveData(fixture.State); await state.LoadAsync(TestContext.Current.CancellationToken);
        var operation = fixture.Operation(state); var images = new BitmapFactory(new MagickImageDecoder());
        var model = new ReaderWorkspaceViewModel(operation, new CommandTable(operation), state);
        var window = new MainWindow(); window.Bind(model, images, new NoSystemPlatform()); window.Show();
        try
        {
            await window.OpenAsync(fixture.Images); Config.Current.Slider.SliderDirection = SliderDirection.LeftToRight;
            await window.ExecuteAsync("ToggleVisibleFilmStrip"); window.UpdateLayout();
            var strip = window.FindControl<ThumbnailView>("DockFilmStripSocket")!; await strip.RefreshAsync();
            var point = strip.TranslatePoint(new Point(54, 50), window)!.Value;
            int refreshes = 0; model.Refreshed += (_, _) => refreshes++;
            window.MouseWheel(point, new Avalonia.Vector(0, -1), RawInputModifiers.None); Dispatcher.UIThread.RunJobs();
            Assert.Equal(1, operation.PageSelector.SelectedIndex); Assert.Equal(0, operation.Book!.CurrentPage!.Index); Assert.Equal(0, refreshes);
            strip.Focus(); window.KeyPress(Key.Right, RawInputModifiers.None, PhysicalKey.ArrowRight, null); window.KeyRelease(Key.Right, RawInputModifiers.None, PhysicalKey.ArrowRight, null);
            Assert.Equal(2, operation.PageSelector.SelectedIndex); Assert.Equal(0, operation.Book.CurrentPage.Index);
            window.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null); window.KeyRelease(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null);
            await WaitForAsync(() => operation.Book.CurrentPage.Index == 2);
            Config.Current.FilmStrip.MouseWheelAction = FilmStripMouseWheelAction.MovePage;
            window.MouseWheel(point, new Avalonia.Vector(0, -1), RawInputModifiers.None); await WaitForAsync(() => operation.Book.CurrentPage.Index == 3);
            state.SetShortcut("PrevPage", "Control+WheelUp"); state.SetShortcut("PrevScrollPage", "");
            Config.Current.FilmStrip.MouseWheelAction = FilmStripMouseWheelAction.CommandDependent;
            window.MouseWheel(point, new Avalonia.Vector(0, 1), RawInputModifiers.Control); await WaitForAsync(() => operation.Book.CurrentPage.Index == 2);
            window.MouseDown(point, MouseButton.Left, RawInputModifiers.Shift); window.MouseUp(point, MouseButton.Left, RawInputModifiers.Shift);
            await Task.Delay(30, TestContext.Current.CancellationToken); Assert.Equal(2, operation.Book.CurrentPage.Index);
            window.MouseMove(point); await WaitForAsync(() => strip.DetailText is not null); Assert.Contains("001.png", strip.DetailText); Assert.Contains("400 × 600", strip.DetailText);
            await window.ExecuteAsync("ToggleVisibleFilmStrip"); await strip.RefreshAsync(); Assert.Equal(0, strip.DisplayCount); Assert.Null(strip.DetailText);
        }
        finally { await window.PrepareShutdownAsync(); window.Close(); }
    }

    /// <summary>滑条拖动共用临时选择，释放才翻正文；禁用联动则立即定位，指定页为一起始。</summary>
    [AvaloniaFact]
    public async Task LinkedSliderPreviewAndJumpCommandsUseOriginalPosition()
    {
        using var fixture = new Fixture(); var state = new SaveData(fixture.State); await state.LoadAsync(TestContext.Current.CancellationToken);
        var operation = fixture.Operation(state); var images = new BitmapFactory(new MagickImageDecoder());
        var window = new MainWindow(); window.Bind(new(operation, new CommandTable(operation), state), images, new NoSystemPlatform()); window.Show();
        try
        {
            await window.OpenAsync(fixture.Images); await window.ExecuteAsync("ToggleVisibleFilmStrip"); window.UpdateLayout(); Dispatcher.UIThread.RunJobs();
            Assert.Same(operation.Book!.CurrentPage, window.FindControl<ListBox>("PageList")!.SelectedItem);
            await window.PreviewSliderAsync(3); Assert.Equal(3, operation.PageSelector.SelectedIndex); Assert.Equal(0, operation.Book!.CurrentPage!.Index);
            await window.CommitSliderAsync(); Assert.Equal(3, operation.Book.CurrentPage.Index);
            Config.Current.Slider.IsSliderLinkedFilmStrip = false; await window.PreviewSliderAsync(1); Assert.Equal(1, operation.Book.CurrentPage.Index);
            await window.JumpPageAsync(3); Assert.Equal(2, operation.Book.CurrentPage.Index); await window.JumpPageAsync(100); Assert.Equal(4, operation.Book.CurrentPage.Index);
            await window.JumpPageAsync(int.MinValue); Assert.Equal(0, operation.Book.CurrentPage.Index);
            state.SetCommandParameter("PrevSizePage", new MoveSizePageCommandParameter { Size = 2 });
            await window.ExecuteAsync("NextSizePage"); Assert.Equal(2, operation.Book.CurrentPage.Index);
            await window.ExecuteAsync("PrevSizePage"); Assert.Equal(0, operation.Book.CurrentPage.Index);
            await window.ExecuteAsync("PrevHistoryPage"); Assert.Equal(2, operation.Book.CurrentPage.Index);
            // 实际 Thumb/路由事件也必须遵循按下预览、释放确认，不能只验证直接调用。
            await operation.JumpAsync(0); Config.Current.Slider.IsSliderLinkedFilmStrip = true;
            Config.Current.Slider.SliderDirection = SliderDirection.LeftToRight;
            ((ReaderWorkspaceViewModel)window.DataContext!).RefreshSelection(); window.UpdateLayout(); Dispatcher.UIThread.RunJobs();
            var slider = window.FindControl<Slider>("PageSliderView")!;
            var start = slider.TranslatePoint(new Point(10, slider.Bounds.Height / 2), window)!.Value;
            var end = slider.TranslatePoint(new Point(slider.Bounds.Width * .75, slider.Bounds.Height / 2), window)!.Value;
            window.MouseDown(start, MouseButton.Left); window.MouseMove(end, RawInputModifiers.LeftMouseButton); Dispatcher.UIThread.RunJobs();
            int selected = operation.PageSelector.SelectedIndex; Assert.True(selected > 0); Assert.Equal(0, operation.Book.CurrentPage.Index);
            window.MouseUp(end, MouseButton.Left); await WaitForAsync(() => operation.Book.CurrentPage.Index == selected);
            await operation.OpenAsync(fixture.Zip, TestContext.Current.CancellationToken); Dispatcher.UIThread.RunJobs(); window.UpdateLayout();
            Assert.Same(operation.Book.CurrentPage, window.FindControl<ListBox>("PageList")!.SelectedItem);
        }
        finally { await window.PrepareShutdownAsync(); window.Close(); }
    }

    /// <summary>等异步正式输入到达目标状态；超时明确失败，不把固定延迟当作成功。</summary>
    private static async Task WaitForAsync(Func<bool> completed)
    {
        for (int i = 0; i < 100 && !completed(); i++) { Dispatcher.UIThread.RunJobs(); await Task.Delay(10, TestContext.Current.CancellationToken); }
        Assert.True(completed());
    }
    /// <summary>本组不操作 Finder/废纸篓，系统能力调用必须显式失败。</summary>
    private sealed class NoSystemPlatform : IPlatformService
    {
        public Task RevealAsync(string path, CancellationToken token = default) => throw new NotSupportedException();
        public Task TrashAsync(string path, CancellationToken token = default) => throw new NotSupportedException();
    }
    /// <summary>控制历史打开等待，以真实后端验证取消后的提交判定。</summary>
    private sealed class PausedFactory : IArchiveFactory
    {
        private readonly ArchiveFactory _inner = new();
        public string? PausePath { get; set; }
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        /// <summary>指定来源等待取消，其余请求仍走真实目录/ZIP。</summary>
        public async Task<Archive> OpenAsync(string path, CancellationToken token)
        {
            if (path == PausePath) { Started.TrySetResult(); await Task.Delay(Timeout.Infinite, token); }
            return await _inner.OpenAsync(path, token);
        }
        /// <summary>真实枚举能力不参与延迟控制。</summary>
        public Task<IReadOnlyList<FolderItem>> ListFoldersAsync(string path, CancellationToken token) => _inner.ListFoldersAsync(path, token);
        /// <summary>转发真实书架枚举；测试暂停只作用于指定来源加载。</summary>
        public Task<IReadOnlyList<FolderItem>> ListBooksAsync(string path, CancellationToken token) => _inner.ListBooksAsync(path, token);
    }
}
