using System.Text.Json.Nodes;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using NeeView;
using NeeView.MacOS.Views;
using NeeView.MacOS.ViewModels;
namespace NeeView.Engine.Tests;

/// <summary>原默认方案、严格鼠标组合及正式查看器输入回归；不注入系统键鼠。</summary>
public sealed class MouseInputTests
{
    /// <summary>原先方案调整、再配对/滚轮交换，不把书籍排序或视图方向一起互换。</summary>
    [Theory]
    [InlineData(InputScheme.TypeA, PageReadOrder.RightToLeft, "Left,LeftClick", "WheelDown")]
    [InlineData(InputScheme.TypeA, PageReadOrder.LeftToRight, "Right,RightClick", "WheelDown")]
    [InlineData(InputScheme.TypeB, PageReadOrder.RightToLeft, "Left,WheelDown", "")]
    [InlineData(InputScheme.TypeB, PageReadOrder.LeftToRight, "Right,WheelDown", "")]
    [InlineData(InputScheme.TypeC, PageReadOrder.RightToLeft, "Left,LeftClick", "")]
    [InlineData(InputScheme.TypeC, PageReadOrder.LeftToRight, "Right,RightClick", "")]
    public void DefaultsKeepOriginalOrder(InputScheme scheme, PageReadOrder order, string next, string scroll)
    {
        var config = new CommandConfig { PresetInputScheme = scheme, PresetPageReadOrder = order };
        Assert.Equal(next, DefaultInputScheme.GetShortcut("NextPage", "", config));
        Assert.Equal(scroll, DefaultInputScheme.GetShortcut("NextScrollPage", "", config));
        Assert.Equal("Up", DefaultInputScheme.GetShortcut("PrevBook", "", config));
        Assert.Equal("RightButton+WheelUp", DefaultInputScheme.GetShortcut("ViewScaleUp", "", config));
        if (scheme == InputScheme.TypeC) Assert.Equal("WheelUp", DefaultInputScheme.GetShortcut("ViewScrollUp", "", config));
    }
    /// <summary>同义键及顺序归一，额外修饰不被忽略；原WheelClick是中键别名。</summary>
    [Theory]
    [InlineData("Shift+Control+RightButton+WheelUp", "RightButton+Ctrl+Shift+WheelUp")]
    [InlineData("Command+XButton1+LeftDoubleClick", "Meta+XButton1+LeftDoubleClick")]
    [InlineData("WheelClick", "MiddleClick")]
    [InlineData("XButton2DoubleClick", "XButton2DoubleClick")]
    public void MouseParsingUsesExactSets(string source, string equivalent)
    {
        Assert.True(MouseGestureSource.TryNormalize(source, out var first)); Assert.True(MouseGestureSource.TryNormalize(equivalent, out var second)); Assert.Equal(first, second);
        Assert.False(MouseGestureSource.TryNormalize("Super+" + source, out _));
        Assert.False(MouseGestureSource.TryNormalize("LeftButton+LeftButton+WheelDown", out _));
    }
    /// <summary>鼠标动作和同义组合的新增冲突均显示命令，不能只按输入字符串比较。</summary>
    [Fact]
    public void SettingsRecognizeAllMouseActionsAndConflicts()
    {
        var a = new ShortcutEdit(new("NextPage", "下一页", "", "", ""), "", true) { Value = "Control+RightButton+WheelLeft,Shift+XButton1+XButton2DoubleClick" };
        SettingsWindow.ValidateInputs([a]);
        var b = new ShortcutEdit(new("PrevPage", "上一页", "", "", ""), "", true) { Value = "RightButton+Ctrl+WheelLeft" };
        Assert.Contains("输入冲突", Assert.Throws<ArgumentException>(() => SettingsWindow.ValidateInputs([a, b])).Message);
    }
    /// <summary>原运行时交换受全局、参数、输入来源和滑条方向共同约束。</summary>
    [Theory]
    [InlineData(true, true, true, "PrevPage")]
    [InlineData(false, true, true, "NextPage")]
    [InlineData(true, false, true, "NextPage")]
    [InlineData(true, true, false, "NextPage")]
    public void ReverseCommandHonorsOriginalGuards(bool enabled, bool allow, bool parameter, string expected)
    { Assert.Equal(expected, DefaultInputScheme.ResolveCommand("NextPage", new() { IsReversePageMove = enabled }, true, allow, parameter)); }
    /// <summary>原共享参数写入唯一拥有者，未知参数保留；旧Mac独立参数只作兼容读取。</summary>
    [Fact]
    public async Task SharedParametersUseOriginalOwner()
    {
        using var fixture = new Fixture(); var state = new SaveData(fixture.State); await state.LoadAsync(TestContext.Current.CancellationToken);
        state.SetCommandParameter("ViewScrollRight", new ViewScrollCommandParameter { Scroll = .6, AllowCrossScroll = false });
        Assert.Equal(.6, state.GetCommandParameter<ViewScrollCommandParameter>("ViewScrollDown").Scroll);
        state.SetCommandParameter("NextPage", new ReversibleCommandParameter { IsReverse = false });
        Assert.False(state.GetCommandParameter<ReversibleCommandParameter>("PrevPage").IsReverse);
        await state.SaveAsync(null, 0, TestContext.Current.CancellationToken);
        var raw = JsonNode.Parse(await File.ReadAllTextAsync(Path.Combine(fixture.State, "UserSetting.json"), TestContext.Current.CancellationToken))!;
        Assert.Null(raw["Commands"]!["ViewScrollRight"]); Assert.NotNull(raw["Commands"]!["ViewScrollUp"]);
    }
    /// <summary>方案默认从原Config读取；自定义、解绑和未知命令参数仍优先，重启不展开235项。</summary>
    [Fact]
    public async Task PresetsAndDifferencesPersistWithoutLosingUnknownFields()
    {
        using var fixture = new Fixture(); Directory.CreateDirectory(fixture.State);
        await File.WriteAllTextAsync(Path.Combine(fixture.State, "UserSetting.json"), """{"Config":{"Command":{"PresetInputScheme":1,"PresetPageReadOrder":1,"Future":9}},"Commands":{"NextPage":{"ShortCutKey":"Ctrl+Left","Parameter":{"Future":7}},"PrevScrollPage":{"ShortCutKey":""}}}""", TestContext.Current.CancellationToken);
        var state = new SaveData(fixture.State); await state.LoadAsync(TestContext.Current.CancellationToken);
        Assert.Equal("Ctrl+Left", state.GetShortcut("NextPage", "")); Assert.Equal("Left,WheelUp", state.GetShortcut("PrevPage", "")); Assert.Equal("", state.GetShortcut("PrevScrollPage", "WheelUp"));
        state.SetShortcutDifference("NextPage", "Right,WheelDown", "Left,LeftClick"); await state.SaveAsync(null, 0, TestContext.Current.CancellationToken);
        var raw = JsonNode.Parse(await File.ReadAllTextAsync(Path.Combine(fixture.State, "UserSetting.json"), TestContext.Current.CancellationToken))!;
        Assert.Null(raw["Commands"]!["NextPage"]!["ShortCutKey"]); Assert.Equal(7, raw["Commands"]!["NextPage"]!["Parameter"]!["Future"]!.GetValue<int>()); Assert.Equal(9, raw["Config"]!["Command"]!["Future"]!.GetValue<int>());
        Assert.Equal(2, raw["Commands"]!.AsObject().Count);
        await new SaveData(fixture.State).LoadAsync(TestContext.Current.CancellationToken); Assert.Equal(InputScheme.TypeB, Config.Current.Command.PresetInputScheme);
    }
    /// <summary>普通轮滚多格执行、不强制Ctrl缩放；半格按作用域/修饰及反方向分开累积。</summary>
    [AvaloniaFact]
    public async Task WheelRespectsBindingsAndAllSteps()
    {
        using var fixture = new Fixture(); var state = new SaveData(fixture.State); await state.LoadAsync(TestContext.Current.CancellationToken); var operation = fixture.Operation(state);
        var window = Window(operation, state); window.Show();
        try
        {
            await window.OpenAsync(fixture.Images); Pump(window); var point = Center(window);
            state.SetShortcut("NextScrollPage", ""); state.SetShortcut("NextOnePage", "Ctrl+WheelDown");
            window.MouseWheel(point, new(0, -2), RawInputModifiers.Control); await WaitAsync(() => operation.Position.Index == 2);
            window.MouseWheel(point, new(0, -.5), RawInputModifiers.Control); window.MouseWheel(point, new(0, -.5), RawInputModifiers.None); Assert.Equal(2, operation.Position.Index);
            window.MouseWheel(point, new(0, .5), RawInputModifiers.Control); Assert.Equal(2, operation.Position.Index);
            window.MouseWheel(point, new(0, -1), RawInputModifiers.Control); await WaitAsync(() => operation.Position.Index == 3);
            state.SetShortcut("NextOnePage", "Ctrl+WheelLeft"); window.MouseWheel(point, new(-3, 0), RawInputModifiers.Control); await WaitAsync(() => operation.Position.Index == 4);
        }
        finally { await window.PrepareShutdownAsync(); window.Close(); }
    }
    /// <summary>原右键滚轮缩放不追加右键翻页；改绑后也不强制缩放。</summary>
    [AvaloniaFact]
    public async Task HeldButtonWheelConsumesRelease()
    {
        using var fixture = new Fixture(); var state = new SaveData(fixture.State); await state.LoadAsync(TestContext.Current.CancellationToken); var operation = fixture.Operation(state);
        var window = Window(operation, state); window.Show();
        try
        {
            await window.OpenAsync(fixture.Images); await operation.JumpAsync(2); Pump(window); var point = Center(window); var width = window.Viewer.GetContentRect().Width;
            window.MouseDown(point, MouseButton.Right); window.MouseWheel(point, new(0, 1), RawInputModifiers.RightMouseButton); await WaitAsync(() => window.Viewer.GetContentRect().Width > width);
            window.MouseUp(point, MouseButton.Right); Assert.Equal(2, operation.Position.Index);
            state.SetShortcut("ViewScaleUp", ""); state.SetShortcut("NextOnePage", "RightButton+WheelUp"); width = window.Viewer.GetContentRect().Width;
            window.MouseDown(point, MouseButton.Right); window.MouseWheel(point, new(0, 1), RawInputModifiers.RightMouseButton); await WaitAsync(() => operation.Position.Index == 3);
            window.MouseUp(point, MouseButton.Right); Assert.Equal(3, operation.Position.Index); Assert.Equal(width, window.Viewer.GetContentRect().Width);
        }
        finally { await window.PrepareShutdownAsync(); window.Close(); }
    }
    /// <summary>组合按下优先、双击不退化为第二次单击；拖动和失去捕获取消待确认动作。</summary>
    [AvaloniaFact]
    public async Task ChordsDoubleClicksAndCaptureKeepDistinctActions()
    {
        var reader = new ReaderView { Width = 500, Height = 500 }; var window = new Avalonia.Controls.Window { Width = 500, Height = 500, Content = reader }; var requested = new List<string>();
        var events = new List<string>();
        reader.AddHandler(InputElement.PointerPressedEvent, (_, e) => events.Add("press:" + e.GetCurrentPoint(reader).Properties.PointerUpdateKind + ":" + e.ClickCount), Avalonia.Interactivity.RoutingStrategies.Tunnel, true);
        reader.AddHandler(InputElement.PointerMovedEvent, (_, e) => events.Add("move:" + e.GetCurrentPoint(reader).Properties.PointerUpdateKind), Avalonia.Interactivity.RoutingStrategies.Tunnel, true);
        reader.TryGestureRequested = value => { requested.Add(value); return true; }; window.Show(); Pump(window);
        try
        {
            var point = new Point(200, 200);
            window.MouseDown(point, MouseButton.Left); window.MouseDown(point, MouseButton.Right, RawInputModifiers.LeftMouseButton);
            Assert.True(requested.Contains("LeftButton+RightClick"), string.Join(",", events)); window.MouseUp(point, MouseButton.Right, RawInputModifiers.LeftMouseButton); window.MouseUp(point, MouseButton.Left);
            Assert.DoesNotContain("LeftClick", requested);
            requested.Clear(); point = new(300, 300);
            window.MouseDown(point, MouseButton.Middle); window.MouseUp(point, MouseButton.Middle);
            window.MouseDown(point, MouseButton.Middle); window.MouseUp(point, MouseButton.Middle);
            Assert.Contains("MiddleClick", requested); Assert.Contains("MiddleDoubleClick", requested); Assert.Equal(2, requested.Count);
            requested.Clear(); point = new(400, 300); window.MouseDown(point, MouseButton.XButton1); window.MouseUp(point, MouseButton.XButton1);
            Assert.Equal(["XButton1Click"], requested);
            requested.Clear(); window.MouseDown(new(100, 100), MouseButton.Left); window.MouseMove(new(120, 150), RawInputModifiers.LeftMouseButton); window.MouseUp(new(120, 150), MouseButton.Left); Assert.Empty(requested);
        }
        finally { reader.Dispose(); window.Close(); }
    }
    /// <summary>非左键也捕获释放，移出控件或捕获转移时不提交待确认点击。</summary>
    [AvaloniaFact]
    public void AllButtonsCancelClickOutsideOrAfterCaptureTransfer()
    {
        var reader = new ReaderView { Width = 250, Height = 250 }; var other = new Border { Width = 200, Height = 250 };
        var window = new Avalonia.Controls.Window { Width = 500, Height = 300, Content = new StackPanel { Orientation = Avalonia.Layout.Orientation.Horizontal, Children = { reader, other } } };
        var requested = new List<string>(); IPointer? pointer = null;
        reader.TryGestureRequested = value => { requested.Add(value); return !value.Contains("Double"); };
        reader.AddHandler(InputElement.PointerPressedEvent, (_, e) => pointer = e.Pointer, RoutingStrategies.Tunnel, true);
        window.Show(); Pump(window);
        try
        {
            foreach (var button in new[] { MouseButton.Left, MouseButton.Right, MouseButton.Middle })
            {
                window.MouseDown(new(80, 80), button); Assert.Same(reader, pointer!.Captured);
                window.MouseUp(new(400, 80), button); Assert.Empty(requested); Assert.Null(pointer.Captured);
                window.MouseDown(new(140, 180), button); pointer!.Capture(other);
                window.MouseUp(new(140, 180), button); Assert.Empty(requested);
            }
        }
        finally { reader.Dispose(); window.Close(); }
    }
    /// <summary>方案C轮滚只平移而不翻页，文本输入继续由编辑作用域拥有。</summary>
    [AvaloniaFact]
    public async Task ClickPresetScrollsAndTextScopeBlocksPageCommands()
    {
        using var fixture = new Fixture(); var state = new SaveData(fixture.State); await state.LoadAsync(TestContext.Current.CancellationToken); var operation = fixture.Operation(state);
        Config.Current.Command.PresetInputScheme = InputScheme.TypeC;
        var window = Window(operation, state); window.Show();
        try
        {
            await window.OpenAsync(fixture.Images); await window.Viewer.ZoomAsync(3); Pump(window); var before = window.Viewer.GetContentRect();
            window.MouseWheel(Center(window), new(0, -1), RawInputModifiers.None); Assert.Equal(0, operation.Position.Index); Assert.NotEqual(before, window.Viewer.GetContentRect());
            window.FindControl<TextBox>("AddressBar")!.Focus(); window.KeyPress(Key.Left, RawInputModifiers.None, PhysicalKey.ArrowLeft, null); window.KeyRelease(Key.Left, RawInputModifiers.None, PhysicalKey.ArrowLeft, null); Assert.Equal(0, operation.Position.Index);
        }
        finally { await window.PrepareShutdownAsync(); window.Close(); }
    }
    /// <summary>阅读方向变化后的按键自动交换；菜单和默认垂直轮滚保留命令含义。</summary>
    [AvaloniaFact]
    public async Task RuntimeReverseDistinguishesKeyMenuAndWheel()
    {
        using var fixture = new Fixture(); var state = new SaveData(fixture.State); await state.LoadAsync(TestContext.Current.CancellationToken); var operation = fixture.Operation(state);
        var window = Window(operation, state); window.Show();
        try
        {
            await window.OpenAsync(fixture.Images); await operation.ApplySettingAsync(s => s.BookReadOrder = PageReadOrder.LeftToRight); await operation.JumpAsync(2); Pump(window); window.Viewer.Focus();
            window.KeyPress(Key.Right, RawInputModifiers.None, PhysicalKey.ArrowRight, null); window.KeyRelease(Key.Right, RawInputModifiers.None, PhysicalKey.ArrowRight, null); await WaitAsync(() => operation.Position.Index == 3);
            await window.ExecuteAsync("PrevPage", fromMenu: true); Assert.Equal(2, operation.Position.Index);
            window.MouseWheel(Center(window), new(0, -1), RawInputModifiers.None); await WaitAsync(() => operation.Position.Index == 3);
            state.SetCommandParameter("PrevPage", new ReversibleCommandParameter { IsReverse = false });
            window.Viewer.Focus(); window.KeyPress(Key.Right, RawInputModifiers.None, PhysicalKey.ArrowRight, null); window.KeyRelease(Key.Right, RawInputModifiers.None, PhysicalKey.ArrowRight, null); await WaitAsync(() => operation.Position.Index == 2);
        }
        finally { await window.PrepareShutdownAsync(); window.Close(); }
    }
    /// <summary>原B方案右击打开完整菜单/禁用占位；关闭解除宿主，切方案不残留自动右键菜单。</summary>
    [AvaloniaFact]
    public async Task RightClickPresetOpensTemporaryViewerMenu()
    {
        using var fixture = new Fixture(); var state = new SaveData(fixture.State); await state.LoadAsync(TestContext.Current.CancellationToken); var operation = fixture.Operation(state);
        Config.Current.Command.PresetInputScheme = InputScheme.TypeB; var window = Window(operation, state); window.Show();
        try
        {
            await window.OpenAsync(fixture.Images); Pump(window); var point = Center(window);
            window.MouseDown(point, MouseButton.Right); window.MouseUp(point, MouseButton.Right); await WaitAsync(() => window.Viewer.ContextMenu?.IsOpen == true);
            var menu = window.Viewer.ContextMenu!; Assert.Equal(8, menu.Items.Count);
            Assert.Contains(menu.Items.OfType<MenuItem>().SelectMany(i => i.Items.OfType<MenuItem>()), i => i.Tag is string name && name == "Unload" && !i.IsEnabled);
            menu.Close(); Pump(window); Assert.Null(window.Viewer.ContextMenu);
        }
        finally { window.Viewer.ContextMenu?.Close(); await window.PrepareShutdownAsync(); window.Close(); }
    }
    /// <summary>默认键位按钮只是草稿；失败回滚后取消/重试不留下运行中的新绑定。</summary>
    [AvaloniaFact]
    public async Task InputDefaultsFailureRollsBackAndCanRetry()
    {
        using var fixture = new Fixture(); var state = new SaveData(fixture.State); await state.LoadAsync(TestContext.Current.CancellationToken); var operation = fixture.Operation(state);
        var model = new ReaderWorkspaceViewModel(operation, new CommandTable(operation), state); var window = Window(operation, state, model); window.Show();
        try
        {
            await window.OpenAsync(fixture.Images); var old = state.GetShortcut("NextPage", ""); var oldAuto = Config.Current.Window.IsAutoHideInNormal;
            var settings = new SettingsWindow(model, window.IsCommandAvailable); settings.Show(window);
            settings.FindControl<ListBox>("SettingsNavigation")!.SelectedIndex = 1; Pump(settings);
            using (var screenshot = settings.CaptureRenderedFrame())
            {
                var phase = Environment.GetEnvironmentVariable("NEEVIEW_ACCEPTANCE_PHASE") ?? "p2-mouse-input";
                screenshot!.Save(Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, $"../../../../../acceptance/{phase}-input-settings-layout.png")), Avalonia.Media.Imaging.PngBitmapEncoderOptions.Default);
            }
            settings.FindControl<ComboBox>("InputScheme")!.SelectedIndex = 1; settings.FindControl<ComboBox>("InputReadOrder")!.SelectedIndex = 1;
            settings.FindControl<CheckBox>("FilmEnabled")!.IsChecked = true; settings.FindControl<CheckBox>("AutoNormal")!.IsChecked = true;
            settings.FindControl<ComboBox>("Mode")!.SelectedIndex = 1;
            settings.FindControl<Button>("InputDefaults")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Assert.Equal(old, state.GetShortcut("NextPage", ""));
            var blocker = Path.Combine(fixture.State, "UserSetting.json.tmp"); Directory.CreateDirectory(blocker);
            settings.FindControl<Button>("SaveSettings")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await WaitAsync(() => settings.FindControl<TextBlock>("Message")!.Text?.StartsWith("保存失败") == true);
            Assert.True(Config.Current.Command.PresetInputScheme == InputScheme.TypeA, settings.FindControl<TextBlock>("Message")!.Text); Assert.Equal(old, state.GetShortcut("NextPage", ""));
            Assert.False(Config.Current.FilmStrip.IsEnabled); Assert.Equal(oldAuto, Config.Current.Window.IsAutoHideInNormal); Assert.Equal(PageMode.SinglePage, operation.Book!.Setting.PageMode);
            Directory.Delete(blocker); settings.FindControl<Button>("SaveSettings")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); await WaitAsync(() => !settings.IsVisible);
            Assert.Equal("Right,WheelDown", state.GetShortcut("NextPage", ""));
            var fresh = new SaveData(fixture.State); await fresh.LoadAsync(TestContext.Current.CancellationToken); Assert.Equal(InputScheme.TypeB, Config.Current.Command.PresetInputScheme); Assert.Equal("Right,WheelDown", fresh.GetShortcut("NextPage", ""));
        }
        finally
        {
            var blocker = Path.Combine(fixture.State, "UserSetting.json.tmp"); if (Directory.Exists(blocker)) Directory.Delete(blocker);
            await window.PrepareShutdownAsync(); window.Close();
        }
    }
    /// <summary>唯一正式窗口链，平台替身拒绝真实Finder或废纸篓操作。</summary>
    private static MainWindow Window(BookOperation operation, SaveData state, ReaderWorkspaceViewModel? model = null)
    { var window = new MainWindow(); window.Bind(model ?? new(operation, new CommandTable(operation), state), new BitmapFactory(new NeeView.Backends.MagickImageDecoder()), new NoPlatform()); return window; }
    private static Point Center(MainWindow window) => window.Viewer.TranslatePoint(new(window.Viewer.Bounds.Width / 2, window.Viewer.Bounds.Height / 2), window)!.Value;
    private static void Pump(Avalonia.Controls.Window window) { Dispatcher.UIThread.RunJobs(); window.UpdateLayout(); }
    private static async Task WaitAsync(Func<bool> complete)
    { for (int i = 0; i < 150 && !complete(); i++) { Dispatcher.UIThread.RunJobs(); await Task.Delay(10, TestContext.Current.CancellationToken); } Assert.True(complete()); }
    private sealed class NoPlatform : IPlatformService
    {
        public Task RevealAsync(string path, CancellationToken token = default) => throw new NotSupportedException();
        public Task TrashAsync(string path, CancellationToken token = default) => throw new NotSupportedException();
    }
}
