using System.Text.Json.Nodes;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using NeeView.MacOS.Views;
using NeeView.MacOS.ViewModels;
namespace NeeView.Engine.Tests;

/// <summary>固定原窗口规则与正式覆盖区域，测试不会修改用户状态或替换主入口。</summary>
public sealed class AutoHideTests
{
    /// <summary>原重复请求不延长隐藏、边缘显示延迟可取消、焦点立即显示、零延迟至少 1ms。</summary>
    [Fact]
    public void OriginalDelayAndForceKeepDeadline()
    {
        var state = new AutoHideVisibility(); var config = new AutoHideConfig { AutoHideDelayVisibleTime = .2 };
        Assert.True(state.Update(0, true, false, false, config));
        Assert.True(state.Update(.9, true, false, false, config));
        Assert.False(state.Update(1, true, false, false, config));
        Assert.False(state.Update(1.1, true, false, true, config));
        Assert.False(state.Update(1.2, true, false, false, config));
        Assert.True(state.Update(1.3, true, true, false, config));
        state.Update(2, true, false, false, config); state.Update(2.9, true, false, false, config, force: true);
        Assert.True(state.Update(3, true, false, false, config)); Assert.False(state.Update(3.91, true, false, false, config));
        Assert.True(state.Update(4, false, false, false, config)); config.AutoHideDelayTime = 0;
        Assert.True(state.Update(5, true, false, false, config)); Assert.False(state.Update(5.002, true, false, false, config));
    }

    /// <summary>旧 Mac 别名/原拼写兼容，原字段优先，未知字段保留并只保存一套有效值。</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task OriginalConfigAndAliasesRoundTrip(bool canonical)
    {
        using var fixture = new Fixture(); Directory.CreateDirectory(fixture.State);
        var path = Path.Combine(fixture.State, "UserSetting.json");
        var json = JsonNode.Parse("""{"Config":{"Panels":{"IsLeftAutoHide":true,"IsRightAutoHide":true,"Future":7},"AutoHide":{"AutoHideHitTestMargin":17,"AutoHideConfrictTopMargin":"Deny","AutoHideFocusLockMode":"TextBoxFocusLock","Future":9},"MenuBar":{"IsAddressBarEnabled":false},"Window":{"IsAutoHideInFullScreen":false,"WindowsOnly":"preserve"}}}""")!;
        if (canonical) { json["Config"]!["Panels"]!["IsHideLeftPanel"] = false; json["Config"]!["AutoHide"]!["AutoHideHitTestHorizontalMargin"] = 28; }
        await File.WriteAllTextAsync(path, json.ToJsonString(), TestContext.Current.CancellationToken);
        var state = new SaveData(fixture.State); await state.LoadAsync(TestContext.Current.CancellationToken);
        Assert.Equal(!canonical, Config.Current.Panels.IsHideLeftPanel); Assert.True(Config.Current.Panels.IsHideRightPanel);
        Assert.Equal(canonical ? 28 : 17, Config.Current.AutoHide.AutoHideHitTestHorizontalMargin);
        Assert.Equal(17, Config.Current.AutoHide.AutoHideHitTestVerticalMargin); Assert.Equal(AutoHideConflictMode.Deny, Config.Current.AutoHide.AutoHideConflictTopMargin);
        Assert.Equal(AutoHideFocusLockMode.TextBoxFocusLock, Config.Current.AutoHide.AutoHideFocusLockMode); Assert.False(Config.Current.MenuBar.IsAddressBarEnabled);
        await state.SaveAsync(null, TestContext.Current.CancellationToken); await new SaveData(fixture.State).LoadAsync(TestContext.Current.CancellationToken);
        var saved = JsonNode.Parse(await File.ReadAllTextAsync(path, TestContext.Current.CancellationToken))!;
        Assert.Null(saved["Config"]!["Panels"]!["IsLeftAutoHide"]); Assert.Equal(7, saved["Config"]!["Panels"]!["Future"]!.GetValue<int>());
        Assert.Equal(9, saved["Config"]!["AutoHide"]!["Future"]!.GetValue<int>()); Assert.Equal("preserve", saved["Config"]!["Window"]!["WindowsOnly"]!.GetValue<string>());
        Assert.False(Config.Current.Window.IsAutoHideInFullScreen); Assert.Equal(!canonical, Config.Current.Panels.IsHideLeftPanel);
    }

    /// <summary>普通/最大化/全屏资格及取消恢复上一状态；胶片条随可隐藏滑条覆盖正文。</summary>
    [AvaloniaFact]
    public async Task WindowModesFollowOriginalCouplingAndRestoration()
    {
        using var fixture = new Fixture(); var state = new SaveData(fixture.State); await state.LoadAsync(TestContext.Current.CancellationToken);
        var operation = fixture.Operation(state); var model = new ReaderWorkspaceViewModel(operation, new CommandTable(operation), state);
        var window = Create(model); window.Show();
        try
        {
            await window.OpenAsync(fixture.Images); Config.Current.FilmStrip.IsEnabled = true; model.RefreshPanels(); Pump(window);
            Assert.False(model.AutoHideMode); Assert.False(model.LeftAutoHide); Assert.True(model.SliderVisible);
            window.WindowState = WindowState.Maximized; Pump(window); Assert.False(model.AutoHideMode);
            await window.ExecuteAsync("SetFullScreen"); Pump(window);
            Assert.True(model.AutoHideMode); Assert.True(model.CanHideSlider); Assert.False(model.CanHideFilmStrip);
            Assert.False(model.MenuVisible); Assert.False(model.LeftVisible); Assert.False(model.RightVisible); Assert.False(model.SliderVisible); Assert.False(model.FilmStripVisible);
            var dock = window.FindControl<Border>("MainViewDockSocket")!;
            Assert.Equal(0, Grid.GetColumn(dock)); Assert.Equal(7, Grid.GetColumnSpan(dock));
            Assert.Equal(new Thickness(0, 32, 0, 20), window.FindControl<Grid>("LeftDockHost")!.Margin);
            await window.ExecuteAsync("ShowHiddenPanels"); Pump(window);
            Assert.True(model.SliderVisible); Assert.True(model.FilmStripVisible); Assert.True(model.MenuVisible);
            await window.ExecuteAsync("CancelFullScreen"); Pump(window);
            Assert.Equal(WindowState.Maximized, window.WindowState); Assert.False(model.AutoHideMode);
            Config.Current.Window.IsAutoHideInNormal = true; window.WindowState = WindowState.Normal; Pump(window); Assert.True(model.AutoHideMode);
        }
        finally { await window.PrepareShutdownAsync(); window.Close(); }
    }

    /// <summary>原左右隐藏命令改变资格而不是关闭选择；反复弹出正文尺寸和位置不变，侧栏图标独立。</summary>
    [AvaloniaFact]
    public async Task EdgeOverlaysDoNotResizeOrRefreshReading()
    {
        using var fixture = new Fixture(); var state = new SaveData(fixture.State); await state.LoadAsync(TestContext.Current.CancellationToken);
        var operation = fixture.Operation(state); var model = new ReaderWorkspaceViewModel(operation, new CommandTable(operation), state);
        var window = Create(model); window.Show();
        try
        {
            await window.OpenAsync(fixture.Images); await operation.JumpAsync(2); Pump(window);
            await window.ExecuteAsync("ToggleHidePanel"); await window.ExecuteAsync("ToggleHideMenu"); await window.ExecuteAsync("ToggleHidePageSlider"); Pump(window);
            window.Viewer.Focus(); window.MouseMove(new(600, 400)); await Settle(window);
            var bounds = window.Viewer.Bounds; var position = operation.Position; int refreshes = 0; model.Refreshed += (_, _) => refreshes++;
            window.MouseMove(new(8, 300)); await Settle(window); Assert.True(model.LeftVisible); Assert.Equal(bounds, window.Viewer.Bounds);
            window.MouseMove(new(1190, 300)); await Settle(window); Assert.True(model.RightVisible); Assert.Equal(bounds, window.Viewer.Bounds);
            window.MouseMove(new(600, 2)); await Settle(window); Assert.True(model.MenuVisible); Assert.Equal(bounds, window.Viewer.Bounds);
            window.MouseMove(new(600, 795)); await Settle(window); Assert.True(model.SliderVisible); Assert.Equal(bounds, window.Viewer.Bounds);
            window.MouseMove(new(600, 400)); await Settle(window); Assert.False(model.LeftVisible); Assert.False(model.RightVisible); Assert.False(model.SliderVisible);
            Assert.Equal(position, operation.Position); Assert.Equal(0, refreshes);
            await window.ExecuteAsync("ToggleVisibleSideBar"); Pump(window); Assert.False(model.SideBarVisible);
            Assert.Equal(0, window.FindControl<Grid>("SidePanelFrame")!.ColumnDefinitions[0].ActualWidth);
            Assert.True(Config.Current.Panels.IsLeftVisible); Assert.True(window.IsCommandAvailable("ShowHiddenPanels")); Assert.False(window.IsCommandAvailable("ToggleFullDesktop"));
            SaveImage(window, "hidden-layout");
            await window.ExecuteAsync("ShowHiddenPanels"); Pump(window); SaveImage(window, "overlay-layout");
        }
        finally { await window.PrepareShutdownAsync(); window.Close(); }
    }

    /// <summary>正式文本焦点、实际弹出菜单和拖动捕获锁定所属区域；移回正文后延迟收起。</summary>
    [AvaloniaFact]
    public async Task TextPopupAndCaptureKeepTheirRegionVisible()
    {
        using var fixture = new Fixture(); var state = new SaveData(fixture.State); await state.LoadAsync(TestContext.Current.CancellationToken);
        var operation = fixture.Operation(state); var model = new ReaderWorkspaceViewModel(operation, new CommandTable(operation), state);
        var window = Create(model); window.Show();
        try
        {
            await window.OpenAsync(fixture.Images); await window.ExecuteAsync("ToggleHideMenu"); Pump(window);
            window.MouseMove(new(600, 2)); await Settle(window); var address = window.FindControl<TextBox>("AddressBar")!;
            address.Focus(); window.MouseMove(new(600, 400)); await Settle(window); Assert.True(model.MenuVisible);
            window.Viewer.Focus(); await Settle(window); Assert.False(model.MenuVisible);
            model.ShowPanel("HistoryPanel"); await window.ExecuteAsync("ToggleHideLeftPanel"); Pump(window);
            window.MouseMove(new(10, 300)); await Settle(window); Assert.True(model.LeftVisible);
            var more = window.FindControl<Button>("HistoryMoreButton")!; more.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.True(more.ContextMenu!.IsOpen); window.MouseMove(new(600, 400)); await Settle(window); Assert.True(model.LeftVisible);
            more.ContextMenu.Close(); window.Viewer.Focus(); await Settle(window); Assert.False(model.LeftVisible);
            await window.ExecuteAsync("ToggleHidePageSlider"); window.MouseMove(new(600, 799)); await Settle(window);
            var slider = window.FindControl<Slider>("PageSliderView")!; var point = slider.TranslatePoint(new(slider.Bounds.Width / 2, slider.Bounds.Height / 2), window)!.Value;
            window.MouseDown(point, MouseButton.Left); window.MouseMove(new(600, 400), RawInputModifiers.LeftMouseButton); await Settle(window);
            Assert.True(model.SliderVisible); window.MouseUp(new(600, 400), MouseButton.Left); window.Viewer.Focus(); await Settle(window); Assert.False(model.SliderVisible);
        }
        finally { await window.PrepareShutdownAsync(); window.Close(); }
    }

    /// <summary>原显示锁经真实键盘路由先解除再切换；普通阅读按键解除，不吞正文动作。</summary>
    [AvaloniaFact]
    public async Task ShowHiddenPanelsKeepsOriginalInputOrder()
    {
        using var fixture = new Fixture(); var state = new SaveData(fixture.State); await state.LoadAsync(TestContext.Current.CancellationToken);
        var operation = fixture.Operation(state); var model = new ReaderWorkspaceViewModel(operation, new CommandTable(operation), state);
        var window = Create(model); window.Show();
        try
        {
            await window.OpenAsync(fixture.Images); state.SetShortcut("ShowHiddenPanels", "Ctrl+Alt+Shift+F10"); await window.ExecuteAsync("ToggleHidePanel");
            window.Viewer.Focus(); window.MouseMove(new(600, 400)); await Settle(window);
            var modifiers = RawInputModifiers.Control | RawInputModifiers.Alt | RawInputModifiers.Shift;
            window.KeyPress(Key.F10, modifiers, PhysicalKey.F10, null); window.KeyRelease(Key.F10, modifiers, PhysicalKey.F10, null); await Settle(window); Assert.True(model.LeftVisible);
            window.KeyPress(Key.F10, modifiers, PhysicalKey.F10, null); window.KeyRelease(Key.F10, modifiers, PhysicalKey.F10, null); await Settle(window); Assert.False(model.LeftVisible);
            window.KeyPress(Key.F10, modifiers, PhysicalKey.F10, null); window.KeyRelease(Key.F10, modifiers, PhysicalKey.F10, null); await Settle(window); Assert.True(model.LeftVisible);
            window.KeyPress(Key.Space, RawInputModifiers.None, PhysicalKey.Space, null); window.KeyRelease(Key.Space, RawInputModifiers.None, PhysicalKey.Space, null); await Settle(window); Assert.False(model.LeftVisible, $"普通按键后 left={model.LeftVisible}, focus={window.FocusManager?.GetFocusedElement()}, status={window.FindControl<TextBlock>("StatusField")!.Text}");
        }
        finally { await window.PrepareShutdownAsync(); window.Close(); }
    }

    /// <summary>胶片条独立覆盖插槽、底部边缘及取消/保存配置，关闭停止计时；不刷新正文。</summary>
    [AvaloniaFact]
    public async Task IndependentFilmAndSettingsPersistWithoutReadingRefresh()
    {
        using var fixture = new Fixture(); var state = new SaveData(fixture.State); await state.LoadAsync(TestContext.Current.CancellationToken);
        var operation = fixture.Operation(state); var model = new ReaderWorkspaceViewModel(operation, new CommandTable(operation), state);
        var window = Create(model); window.Show();
        try
        {
            await window.OpenAsync(fixture.Images); Config.Current.FilmStrip.IsEnabled = true; await window.ExecuteAsync("ToggleHideFilmStrip"); Pump(window);
            Assert.True(model.CanHideFilmStrip); var film = window.FindControl<ThumbnailView>("DockFilmStripSocket")!; Assert.Same(window.FindControl<Border>("LayerFilmStripSocket"), film.Parent);
            window.Viewer.Focus(); window.MouseMove(new(600, 400)); await Settle(window); var bounds = window.Viewer.Bounds;
            window.MouseMove(new(600, 795)); await Settle(window); Assert.True(model.FilmStripVisible); Assert.Equal(bounds, window.Viewer.Bounds);
            var settings = new SettingsWindow(model); settings.Show(window); settings.FindControl<CheckBox>("AutoNormal")!.IsChecked = true;
            settings.FindControl<Button>("CancelSettings")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Assert.False(Config.Current.Window.IsAutoHideInNormal);
            int refreshes = 0; model.Refreshed += (_, _) => refreshes++;
            settings = new SettingsWindow(model); settings.Show(window); settings.FindControl<CheckBox>("AutoNormal")!.IsChecked = true;
            settings.FindControl<CheckBox>("AddressEnabled")!.IsChecked = false; settings.FindControl<NumericUpDown>("HideDelay")!.Value = .3m;
            settings.FindControl<Button>("SaveSettings")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            for (int i = 0; i < 100 && settings.IsVisible; i++) await Settle(window);
            Assert.False(settings.IsVisible); model.RefreshPanels(); Pump(window); Assert.Equal(0, refreshes); Assert.True(model.AutoHideMode);
            var fresh = new SaveData(fixture.State); await fresh.LoadAsync(TestContext.Current.CancellationToken);
            Assert.True(Config.Current.Window.IsAutoHideInNormal); Assert.False(Config.Current.MenuBar.IsAddressBarEnabled); Assert.Equal(.3, Config.Current.AutoHide.AutoHideDelayTime);
        }
        finally { await window.PrepareShutdownAsync(); window.Close(); }
    }

    /// <summary>边角原 Allow/AllowPixel/Deny 规则只作用于补充边缘命中，不抢已打开区域输入。</summary>
    [AvaloniaTheory]
    [InlineData(AutoHideConflictMode.Allow, true)]
    [InlineData(AutoHideConflictMode.AllowPixel, false)]
    [InlineData(AutoHideConflictMode.Deny, false)]
    public async Task CornerConflictKeepsOriginalPriority(AutoHideConflictMode mode, bool visible)
    {
        using var fixture = new Fixture(); var state = new SaveData(fixture.State); await state.LoadAsync(TestContext.Current.CancellationToken);
        var operation = fixture.Operation(state); var model = new ReaderWorkspaceViewModel(operation, new CommandTable(operation), state);
        var window = Create(model); window.Show();
        try
        {
            await window.OpenAsync(fixture.Images); Config.Current.AutoHide.AutoHideConflictTopMargin = mode;
            await window.ExecuteAsync("ToggleHidePanel"); await window.ExecuteAsync("ToggleHideMenu"); window.Viewer.Focus();
            window.MouseMove(new(600, 400)); await Settle(window); window.MouseMove(new(8, 10)); await Settle(window);
            Assert.True(model.LeftVisible); Assert.Equal(visible, model.MenuVisible);
        }
        finally { await window.PrepareShutdownAsync(); window.Close(); }
    }
    /// <summary>精确手势只交给实际命中的正文；覆盖面板和底栏必须保留控件输入。</summary>
    [AvaloniaFact]
    public async Task NativeGesturesExcludeVisibleOverlays()
    {
        using var fixture = new Fixture(); var state = new SaveData(fixture.State); await state.LoadAsync(TestContext.Current.CancellationToken);
        var operation = fixture.Operation(state); var model = new ReaderWorkspaceViewModel(operation, new CommandTable(operation), state);
        var window = Create(model); window.Show();
        try
        {
            await window.OpenAsync(fixture.Images); await window.ExecuteAsync("ToggleHidePanel"); await window.ExecuteAsync("ToggleHidePageSlider");
            window.Viewer.Focus(); window.Activate(); Pump(window); var source = window.TryGetPlatformHandle()?.Handle ?? 0;
            // 原生回调发生在已经布局的帧；推进 Headless 帧与延迟绑定后才检查实际控件命中。
            await window.ExecuteAsync("ShowHiddenPanels"); await Settle(window);
            var left = window.FindControl<Border>("LeftPanel")!; var point = left.TranslatePoint(new(50, 100), window)!.Value;
            Assert.True(left.IsVisible); Assert.True(left.Bounds.Width > 0);
            Assert.False(window.HandlePlatformGesture(new(false, point.X, point.Y, 0, 80, 0, source)), $"left={left.Bounds}, point={point}, hit={window.InputHitTest(point)}");
            var bottom = window.FindControl<Border>("DockStatusArea")!; point = bottom.TranslatePoint(new(500, bottom.Bounds.Height / 2), window)!.Value;
            Assert.False(window.HandlePlatformGesture(new(false, point.X, point.Y, 0, 80, 0, source)), $"bottomVisible={bottom.IsVisible}, slider={model.SliderVisible}, bounds={bottom.Bounds}, point={point}, hit={window.InputHitTest(point)}");
            Assert.True(window.HandlePlatformGesture(new(false, 600, 400, 0, 80, 0, source)));
        }
        finally { await window.PrepareShutdownAsync(); window.Close(); }
    }

    /// <summary>正式窗口装配，短测试延迟只在自建配置中设置。</summary>
    private static MainWindow Create(ReaderWorkspaceViewModel model)
    { Config.Current.AutoHide.AutoHideDelayTime = .03; var window = new MainWindow(); window.Bind(model, new BitmapFactory(new NeeView.Backends.MagickImageDecoder()), new NoPlatform()); return window; }
    /// <summary>推进正式绑定与布局。</summary>
    private static void Pump(Window window) { Dispatcher.UIThread.RunJobs(); window.UpdateLayout(); }
    /// <summary>允许 UI 计时器跨过真实延迟，不将 MouseMove 的同步更新当作完整隐藏验收。</summary>
    private static async Task Settle(Window window) { for (int i = 0; i < 6; i++) { await Task.Delay(20, TestContext.Current.CancellationToken); Pump(window); } }
    /// <summary>新节点图片另存，避免全量测试覆盖历史验收。</summary>
    private static void SaveImage(Window window, string suffix)
    {
        var phase = Environment.GetEnvironmentVariable("NEEVIEW_ACCEPTANCE_PHASE") ?? "p2-autohide";
        using var image = new RenderTargetBitmap(new PixelSize(1200, 800)); image.Render(window);
        image.Save(Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, $"../../../../../acceptance/{phase}-{suffix}.png")), PngBitmapEncoderOptions.Default);
    }
    /// <summary>本批拒绝未授权系统文件操作。</summary>
    private sealed class NoPlatform : IPlatformService
    {
        /// <summary>测试不访问 Finder。</summary>
        public Task RevealAsync(string path, CancellationToken token = default) => throw new NotSupportedException();
        /// <summary>测试不删除源文件。</summary>
        public Task TrashAsync(string path, CancellationToken token = default) => throw new NotSupportedException();
    }
}
