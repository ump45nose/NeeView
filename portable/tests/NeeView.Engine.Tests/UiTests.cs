using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Media.Imaging;
using Avalonia.Markup.Xaml.Styling;
using Avalonia.Themes.Fluent;
using Avalonia.Threading;
using NeeView.MacOS.Views;
using NeeView.MacOS.ViewModels;
using NeeView;

[assembly: AvaloniaTestApplication(typeof(NeeView.Engine.Tests.TestAvaloniaBuilder))]
namespace NeeView.Engine.Tests;

/// <summary>测试直接编译正式视图源码和资源，不建立另一个产品入口。</summary>
public sealed class TestApplication : Avalonia.Application
{
    /// <summary>加载同一转换主题，验证原图标和颜色资源可解析。</summary>
    public override void Initialize()
    {
        RequestedThemeVariant = Avalonia.Styling.ThemeVariant.Dark;
        Styles.Add(new FluentTheme());
        Styles.Add(new StyleInclude(new Uri("avares://NeeView.Engine.Tests/")) { Source = new Uri("avares://NeeView.Engine.Tests/Styles/NeeViewTheme.axaml") });
    }
}
public static class TestAvaloniaBuilder
{
    /// <summary>使用实际 Skia 绘制，Headless 截图包含真实视图和图片。</summary>
    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<TestApplication>().UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false });
}
public sealed class UiTests
{
    /// <summary>菜单保留原未实现能力；历史、书签和可见缩略图进入真实链路。</summary>
    [AvaloniaFact]
    public async Task CompleteMenusAndNavigationUseOriginalBook()
    {
        using var fixture = new Fixture(); var state = new SaveData(fixture.State); await state.LoadAsync(TestContext.Current.CancellationToken);
        var operation = fixture.Operation(state); var images = new BitmapFactory(new NeeView.Backends.MagickImageDecoder());
        var model = new ReaderWorkspaceViewModel(operation, new CommandTable(operation), state);
        var window = new MainWindow(); window.Bind(model, images, new TestPlatform()); window.Show();
        try
        {
            await window.OpenAsync(fixture.Images); window.UpdateLayout();
            var menu = window.FindControl<Menu>("MenuBar")!;
            Assert.Equal(8, menu.ItemCount);
            var file = (MenuItem)menu.Items[0]!;
            Assert.False(file.Items.OfType<MenuItem>().Single(i => i.Tag as string == "Print").IsEnabled);
            Assert.True(file.Items.OfType<MenuItem>().Single(i => i.Tag as string == "LoadAs").IsEnabled);
            await window.ExecuteAsync("ToggleBookmark"); Dispatcher.UIThread.RunJobs();
            Assert.True(model.IsBookmark); Assert.Single(model.Bookmarks);
            await window.ExecuteAsync("ToggleVisibleHistoryList"); window.UpdateLayout();
            Assert.True(model.ShowHistory); Assert.Single(model.History);
            Assert.True(((MenuItem)menu.Items[1]!).Items.OfType<MenuItem>().Single(i => i.Tag as string == "ToggleVisibleHistoryList").IsChecked);
            await window.ExecuteAsync("ToggleVisibleBookmarkList"); window.UpdateLayout();
            Assert.True(model.ShowBookmarks); Assert.Equal(1, window.FindControl<TreeView>("BookmarkTree")!.ItemCount);
            await window.ExecuteAsync("ToggleVisibleFilmStrip"); window.UpdateLayout();
            var strip = window.FindControl<ThumbnailView>("DockFilmStripSocket")!; await strip.RefreshAsync();
            Assert.True(strip.IsVisible); Assert.InRange(strip.DisplayCount, 1, 5);
            await window.ExecuteAsync("ToggleVisibleFilmStrip"); await strip.RefreshAsync(); Assert.Equal(0, strip.DisplayCount);
            // 来自其他窗口的原生手势必须继续传播，不能操作主窗口。
            Assert.False(window.HandlePlatformGesture(new(false, 400, 300, 0, 80, 0, (nint)12345)));
        }
        finally { await window.PrepareShutdownAsync(); window.Close(); }
    }
    /// <summary>原鼠标组合绑定必须可保存；新输入冲突和错误值则明确阻止。</summary>
    [AvaloniaFact]
    public async Task InputEditorPreservesOriginalGesturesAndRejectsConflicts()
    {
        using var fixture = new Fixture(); var state = new SaveData(fixture.State); await state.LoadAsync(TestContext.Current.CancellationToken);
        await using var operation = fixture.Operation(state);
        var edits = new CommandTable(operation).Definitions.Select(d => new ShortcutEdit(d, d.Shortcut, false)).ToArray();
        SettingsWindow.ValidateInputs(edits);
        var next = edits.Single(e => e.Name == "NextPage"); next.Value = "Ctrl+O";
        Assert.Throws<ArgumentException>(() => SettingsWindow.ValidateInputs(edits));
        next.Value = "NoSuchInput"; Assert.Throws<ArgumentException>(() => SettingsWindow.ValidateInputs(edits));
        next.Value = "Control+Shift+F12"; SettingsWindow.ValidateInputs(edits);
    }
    /// <summary>原窗口区域相对位置及图标栏尺寸直接对照 XAML 来源。</summary>
    [AvaloniaFact]
    public async Task OriginalRegionsAndRealReaderRender()
    {
        using var fixture = new Fixture(); var state = new SaveData(fixture.State); await state.LoadAsync(TestContext.Current.CancellationToken);
        var operation = fixture.Operation(state); var factory = new BitmapFactory(new NeeView.Backends.MagickImageDecoder());
        var model = new ReaderWorkspaceViewModel(operation, new CommandTable(operation), state);
        var window = new MainWindow(); window.Bind(model, factory, new TestPlatform()); window.Show();
        try
        {
            await window.OpenAsync(fixture.Images); window.UpdateLayout(); await window.Viewer.RefreshAsync();
            Dispatcher.UIThread.RunJobs(); window.UpdateLayout();
            var menu = window.FindControl<Border>("DockMenuSocket")!;
            var left = window.FindControl<Border>("LeftPanel")!; var right = window.FindControl<Border>("RightPanel")!;
            var bottom = window.FindControl<Border>("DockStatusArea")!;
            var area = window.FindControl<Grid>("SidePanelFrame")!;
            Assert.Equal(41, area.ColumnDefinitions[0].ActualWidth); Assert.Equal(41, area.ColumnDefinitions[6].ActualWidth);
            Assert.True(menu.Bounds.Bottom <= area.Bounds.Top);
            Assert.True(left.Bounds.Right < window.Viewer.Bounds.Left); Assert.True(window.Viewer.Bounds.Right < right.Bounds.Left);
            Assert.True(area.Bounds.Bottom <= bottom.Bounds.Top);
            Assert.Equal(5, window.FindControl<ListBox>("PageList")!.ItemCount);
            var output = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../acceptance/p2-bookshelf-window-layout.png")); Directory.CreateDirectory(Path.GetDirectoryName(output)!);
            using var image = new RenderTargetBitmap(new PixelSize(1200, 800)); image.Render(window); image.Save(output, PngBitmapEncoderOptions.Default);
            Assert.True(new FileInfo(output).Length > 1000);
        }
        finally { await window.PrepareShutdownAsync(); window.Close(); }
    }
    /// <summary>输入文本不能触发原数字/翻页命令，默认 Left 在查看器中仍翻页。</summary>
    [AvaloniaFact]
    public async Task TextInputAndViewerScopesStaySeparate()
    {
        using var fixture = new Fixture(); var state = new SaveData(fixture.State); await state.LoadAsync(TestContext.Current.CancellationToken);
        var operation = fixture.Operation(state); var images = new BitmapFactory(new NeeView.Backends.MagickImageDecoder());
        var window = new MainWindow(); window.Bind(new(operation, new CommandTable(operation), state), images, new TestPlatform()); window.Show();
        try
        {
            await window.OpenAsync(fixture.Images); window.UpdateLayout();
            window.FindControl<TextBox>("AddressBar")!.Focus();
            window.KeyPress(Avalonia.Input.Key.Left, Avalonia.Input.RawInputModifiers.None, Avalonia.Input.PhysicalKey.ArrowLeft, null); window.KeyRelease(Avalonia.Input.Key.Left, Avalonia.Input.RawInputModifiers.None, Avalonia.Input.PhysicalKey.ArrowLeft, null);
            await Task.Delay(50, TestContext.Current.CancellationToken); Assert.Equal(0, operation.Book!.CurrentPage!.Index);
            var fileMenu = (MenuItem)window.FindControl<Menu>("MenuBar")!.Items[0]!;
            fileMenu.IsSubMenuOpen = true;
            window.KeyPress(Avalonia.Input.Key.Left, Avalonia.Input.RawInputModifiers.None, Avalonia.Input.PhysicalKey.ArrowLeft, null); window.KeyRelease(Avalonia.Input.Key.Left, Avalonia.Input.RawInputModifiers.None, Avalonia.Input.PhysicalKey.ArrowLeft, null);
            await Task.Delay(50, TestContext.Current.CancellationToken); Assert.Equal(0, operation.Book.CurrentPage!.Index);
            window.FindControl<Menu>("MenuBar")!.Close();
            window.Viewer.Focus(); window.KeyPress(Avalonia.Input.Key.Left, Avalonia.Input.RawInputModifiers.None, Avalonia.Input.PhysicalKey.ArrowLeft, null); window.KeyRelease(Avalonia.Input.Key.Left, Avalonia.Input.RawInputModifiers.None, Avalonia.Input.PhysicalKey.ArrowLeft, null);
            for (int i = 0; i < 30 && operation.Book.CurrentPage!.Index == 0; i++) await Task.Delay(20, TestContext.Current.CancellationToken);
            Assert.Equal(1, operation.Book.CurrentPage!.Index);
            var settings = new SettingsWindow(); settings.Show(); settings.UpdateLayout(); Assert.NotNull(settings.FindControl<ComboBox>("Scope")); settings.Close();
        }
        finally { await window.PrepareShutdownAsync(); window.Close(); }
    }
    /// <summary>侧栏悬停与切换不请求图像；列宽可调整，隐藏/重新显示保持实际宽度。</summary>
    [AvaloniaFact]
    public async Task PanelChangesStayIndependentAndShutdownIsShared()
    {
        using var fixture = new Fixture(); var state = new SaveData(fixture.State); await state.LoadAsync(TestContext.Current.CancellationToken);
        var operation = fixture.Operation(state); var images = new BitmapFactory(new NeeView.Backends.MagickImageDecoder());
        var model = new ReaderWorkspaceViewModel(operation, new CommandTable(operation), state);
        var window = new MainWindow(); window.Bind(model, images, new TestPlatform()); window.Show(); window.UpdateLayout();
        var refreshes = 0; model.Refreshed += (_, _) => refreshes++;
        var grid = window.FindControl<Grid>("SidePanelFrame")!;
        grid.ColumnDefinitions[1].Width = new GridLength(300); window.UpdateLayout();
        Assert.Equal(300, window.FindControl<Border>("LeftPanel")!.Bounds.Width);
        model.Hover(true, true); model.Hover(true, false); model.SelectPanel("FolderPanel"); window.UpdateLayout();
        Assert.Equal(0, grid.ColumnDefinitions[1].ActualWidth);
        model.SelectPanel("FolderPanel"); window.UpdateLayout(); Assert.Equal(300, grid.ColumnDefinitions[1].ActualWidth); Assert.Equal(0, refreshes);
        Assert.True(MainWindow.MatchKey("Control+Right", new Avalonia.Input.KeyEventArgs { Key = Avalonia.Input.Key.Right, KeyModifiers = Avalonia.Input.KeyModifiers.Control }));
        Assert.False(MainWindow.MatchKey("Control+Right", new Avalonia.Input.KeyEventArgs { Key = Avalonia.Input.Key.Right, KeyModifiers = Avalonia.Input.KeyModifiers.Meta }));
        var close = window.PrepareShutdownAsync(); Assert.Same(close, window.PrepareShutdownAsync()); await close; window.Close();
    }
    /// <summary>无系统文件请求时不模拟 Finder/废纸篓成功。</summary>
    private sealed class TestPlatform : IPlatformService
    {
        public Task RevealAsync(string path, CancellationToken token = default) => throw new NotSupportedException("Headless 未执行 Finder。");
        public Task TrashAsync(string path, CancellationToken token = default) => throw new NotSupportedException("Headless 未执行废纸篓。");
    }
}
