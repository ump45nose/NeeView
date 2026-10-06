using System.Text.Json.Nodes;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using NeeView.Backends;
using NeeView.MacOS.ViewModels;
using NeeView.MacOS.Views;
namespace NeeView.Engine.Tests;

/// <summary>原随机/排序、两个显隐命令、窗口状态及设置目录；全部隔离合成夹具。</summary>
public sealed class OriginalCommandCompletionTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;
    private sealed class Fixture : IDisposable
    {
        public string Root { get; } = Path.Combine(Path.GetTempPath(), "NeeView-P5-OriginalCommands-" + Guid.NewGuid().ToString("N"));
        public string State => Path.Combine(Root, "Profile");
        public string Images => Path.Combine(Root, "Images");
        public Fixture()
        {
            Directory.CreateDirectory(State); Directory.CreateDirectory(Images);
            using var image = new ImageMagick.MagickImage(ImageMagick.MagickColors.Teal, 20, 40);
            for (int i = 0; i < 4; i++) image.Write(Path.Combine(Images, i + ".png"));
        }
        public async Task<SaveData> Load() { var state = new SaveData(State); await state.LoadAsync(Token); return state; }
        public void Dispose() => Directory.Delete(Root, true);
    }
    private sealed class Platform : IPlatformService
    {
        public string? Opened { get; private set; }
        public bool Fail { get; set; }
        public bool Hold { get; set; }
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public async Task OpenFolderAsync(string path, CancellationToken token = default)
        {
            Entered.TrySetResult();
            if (Hold) await Task.Delay(Timeout.InfiniteTimeSpan, token);
            if (Fail) throw new IOException("isolated Finder failure");
            Opened = path;
        }
        public Task RevealAsync(string path, CancellationToken token = default) => Task.CompletedTask;
        public Task TrashAsync(string path, CancellationToken token = default) => throw new NotSupportedException();
    }
    private static void Pump(Window window) { Dispatcher.UIThread.RunJobs(); window.UpdateLayout(); Dispatcher.UIThread.RunJobs(); }
    private static MainWindow Window(BookOperation operation, SaveData state, Platform platform, out ReaderWorkspaceViewModel model)
    {
        model = new(operation, new(operation), state);
        var window = new MainWindow(); window.Bind(model, new BitmapFactory(new MagickImageDecoder()), platform); window.Show(); return window;
    }
    [Theory]
    [InlineData(PageSortModeClass.None, 1)]
    [InlineData(PageSortModeClass.Normal, 9)]
    [InlineData(PageSortModeClass.WithEntry, 11)]
    [InlineData(PageSortModeClass.Full, 11)]
    public void SortCycleUsesOriginalSourceCapabilityAndEnumOrder(PageSortModeClass scope, int count)
    {
        var mode = PageSortMode.FileName; var result = new List<PageSortMode>();
        do { mode = scope.GetTogglePageSortMode(mode); result.Add(mode); } while (mode != PageSortMode.FileName && result.Count < 12);
        Assert.Equal(count, result.Count); Assert.Equal(PageSortMode.FileName, result[^1]);
        Assert.All(result, value => Assert.True(scope.Contains(value)));
        if (scope == PageSortModeClass.Normal) Assert.DoesNotContain(result, value => value.IsEntryCategory());
        Assert.Equal(PageSortMode.FileName, scope.ValidatePageSortMode((PageSortMode)99));
        Assert.Equal(PageSortMode.FileNameDescending, PageSortModeClass.Normal.ValidatePageSortMode(PageSortMode.EntryDescending));
    }
    [Fact]
    public async Task SortCommandPreservesSourceAndPageThenPersistsOriginalSetting()
    {
        using var f = new Fixture(); var state = await f.Load(); await using var operation = new BookOperation(new ArchiveFactory(), new MagickImageDecoder(), state);
        await operation.OpenAsync(f.Images, Token); await operation.JumpAsync(2); var book = operation.Book!; var page = book.CurrentPage;
        var commands = new CommandTable(operation); await commands.ExecuteAsync("ToggleSortMode");
        Assert.Same(book, operation.Book); Assert.Same(page, book.CurrentPage);
        Assert.Equal(PageSortMode.Random, book.Setting.SortMode); Assert.Equal(PageSortMode.Random, book.EffectiveSortMode);
        await commands.ExecuteAsync("ToggleSortMode"); Assert.Equal(PageSortMode.FileName, book.Setting.SortMode); Assert.Same(page, book.CurrentPage);
        await operation.SaveAllAsync(Token); Assert.Equal("2.png", state.Find(f.Images)!.Page);
    }
    [Fact]
    public async Task RandomCommandUsesFilteredRealPagesAllowsCurrentAndIgnoresEmptyBook()
    {
        using var f = new Fixture(); var state = await f.Load(); await using var operation = new BookOperation(new ArchiveFactory(), new MagickImageDecoder(), state);
        var commands = new CommandTable(operation); await commands.ExecuteAsync("JumpRandomPage");
        await operation.OpenAsync(f.Images, Token); var book = operation.Book; await operation.SearchPagesAsync("2.png", book, Token);
        for (int i = 0; i < 5; i++) { await commands.ExecuteAsync("JumpRandomPage"); Assert.Same(book, operation.Book); Assert.Equal("2.png", book!.CurrentPage!.EntryName); Assert.Equal(0, operation.Position.Index); Assert.Equal(0, operation.Position.Part); }
        await operation.SearchPagesAsync("absent", book, Token); await commands.ExecuteAsync("JumpRandomPage"); Assert.Empty(book!.Pages); Assert.Null(operation.Frame);
    }
    [AvaloniaFact]
    public async Task MenusChangePersistentFlagsWhileShortcutShowsOnlyItsHiddenRegion()
    {
        using var f = new Fixture(); var state = await f.Load(); var op = new BookOperation(new ArchiveFactory(), new MagickImageDecoder(), state);
        var window = Window(op, state, new(), out var model);
        try
        {
            await window.OpenAsync(f.Images); Pump(window); window.Viewer.Focus(); window.MouseMove(new(600, 400));
            int refreshes = 0; model.Refreshed += (_, _) => refreshes++;
            state.SetCommandParameter("ToggleVisibleAddressBar", new ToggleCommandParameter { ToggleMode = ToggleMode.On });
            await window.ExecuteAsync("ToggleVisibleAddressBar", true); Pump(window);
            Assert.False(Config.Current.MenuBar.IsAddressBarEnabled); Assert.False(window.FindControl<TextBox>("AddressBar")!.IsEffectivelyVisible);
            await window.ExecuteAsync("ToggleVisibleAddressBar"); Pump(window); Assert.True(Config.Current.MenuBar.IsAddressBarEnabled);
            Config.Current.MenuBar.IsHideMenu = true; Config.Current.Slider.IsHidePageSlider = true;
            Config.Current.Panels.IsHideLeftPanel = true; Config.Current.AutoHide.AutoHideDelayTime = .01;
            model.RefreshPanels(); Pump(window); window.MouseMove(new(600, 400)); await Task.Delay(80, Token); Pump(window);
            Assert.False(model.MenuVisible); Assert.False(model.SliderVisible); Assert.False(model.LeftVisible);
            Config.Current.AutoHide.AutoHideDelayTime = 1;
            state.SetCommandParameter("ToggleVisiblePageSlider", new ToggleCommandParameter { ToggleMode = ToggleMode.On });
            await window.ExecuteAsync("ToggleVisibleAddressBar"); Pump(window); Assert.True(model.MenuVisible); Assert.False(model.SliderVisible); Assert.False(model.LeftVisible);
            await window.ExecuteAsync("ToggleVisiblePageSlider"); Pump(window); Assert.True(model.SliderVisible); Assert.False(model.LeftVisible);
            state.SetCommandParameter("ToggleVisiblePageSlider", new ToggleCommandParameter { ToggleMode = ToggleMode.Off });
            await window.ExecuteAsync("ToggleVisiblePageSlider"); Pump(window); Assert.False(model.SliderVisible); Assert.True(Config.Current.Slider.IsEnabled);
            await window.ExecuteAsync("ToggleVisiblePageSlider", true); Pump(window); Assert.False(Config.Current.Slider.IsEnabled); Assert.False(model.SliderVisible);
            await op.SaveAllAsync(Token); var raw = JsonNode.Parse(File.ReadAllText(Path.Combine(f.State, "UserSetting.json")))!;
            Assert.False(raw["Config"]!["Slider"]!["IsEnabled"]!.GetValue<bool>()); Assert.Equal(0, refreshes);
            if (Environment.GetEnvironmentVariable("NEEVIEW_P5_COMMANDS_SCREENSHOT") is { } path) { Pump(window); using var capture = window.CaptureRenderedFrame(); capture!.Save(path, Avalonia.Media.Imaging.PngBitmapEncoderOptions.Default); }
        }
        finally { await window.PrepareShutdownAsync(); window.Close(); }
    }
    [AvaloniaFact]
    public async Task MinimizeRestoresActualPriorStateAndMaximizeLeavesFullScreen()
    {
        using var f = new Fixture(); var state = await f.Load(); var op = new BookOperation(new ArchiveFactory(), new MagickImageDecoder(), state);
        var window = Window(op, state, new(), out _);
        try
        {
            await window.ExecuteAsync("ToggleWindowMaximize"); Assert.Equal(WindowState.Maximized, window.WindowState);
            await window.ExecuteAsync("ToggleWindowMinimize"); Assert.Equal(WindowState.Minimized, window.WindowState);
            await window.ExecuteAsync("ToggleWindowMinimize"); Assert.Equal(WindowState.Maximized, window.WindowState);
            await window.ExecuteAsync("SetFullScreen"); await window.ExecuteAsync("ToggleWindowMinimize"); await window.ExecuteAsync("ToggleWindowMinimize"); Assert.Equal(WindowState.FullScreen, window.WindowState);
            await window.ExecuteAsync("ToggleWindowMaximize"); Assert.Equal(WindowState.Maximized, window.WindowState);
            await window.ExecuteAsync("ToggleWindowMaximize"); Assert.Equal(WindowState.Normal, window.WindowState);
            window.WindowState = WindowState.Maximized; window.WindowState = WindowState.Minimized;
            await window.ExecuteAsync("ToggleWindowMinimize"); Assert.Equal(WindowState.Maximized, window.WindowState);
        }
        finally { await window.PrepareShutdownAsync(); window.Close(); }
    }
    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SettingDirectoryUsesUniquePlatformBoundaryAndFailureAllowsClose(bool failure)
    {
        using var f = new Fixture(); var state = await f.Load(); var op = new BookOperation(new ArchiveFactory(), new MagickImageDecoder(), state); var platform = new Platform { Fail = failure };
        var window = Window(op, state, platform, out _);
        try
        {
            Assert.True(window.IsCommandAvailable("OpenSettingFilesFolder")); await window.ExecuteAsync("OpenSettingFilesFolder");
            if (!failure) Assert.Equal(f.State, platform.Opened);
            Assert.Empty(Directory.EnumerateFiles(f.State)); await window.PrepareShutdownAsync();
        }
        finally { await window.PrepareShutdownAsync(); window.Close(); }
    }
    [AvaloniaFact]
    public async Task ClosingCancelsUncommittedFinderRequestAndFinishesSharedAction()
    {
        using var f = new Fixture(); var state = await f.Load(); var op = new BookOperation(new ArchiveFactory(), new MagickImageDecoder(), state); var platform = new Platform { Hold = true };
        var window = Window(op, state, platform, out _);
        var action = window.ExecuteAsync("OpenSettingFilesFolder"); await platform.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5), Token);
        Assert.False(window.IsCommandAvailable("OpenSettingFilesFolder"));
        await window.PrepareShutdownAsync().WaitAsync(TimeSpan.FromSeconds(5), Token); await action; Assert.Null(platform.Opened); window.Close();
    }
}
