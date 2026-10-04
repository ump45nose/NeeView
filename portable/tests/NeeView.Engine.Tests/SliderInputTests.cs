using System.Text.Json.Nodes;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using NeeView;
using NeeView.MacOS.Views;
using NeeView.MacOS.ViewModels;
namespace NeeView.Engine.Tests;

/// <summary>直接装载正式底部页号与滑条，核验原绑定、焦点及保存语义。</summary>
public sealed class SliderInputTests
{
    /// <summary>原 converter 与 WPF 数值绑定范围/舍入保持，NaN 和非法输入不破坏索引。</summary>
    [Fact]
    public void RawNumberConversionKeepsOriginalRangeAndRounding()
    {
        foreach (var (text, expected) in new[] { ("1", 0), ("0", 0), ("-8", 0), ("99999999999999999", 4), ("2e1", 4), ("2.6", 2), ("2.5", 2), ("3.5", 2) })
        { Assert.True(SliderTextBox.TryConvertBack(text, 4, out var index)); Assert.Equal(expected, index); }
        Assert.False(SliderTextBox.TryConvertBack("错误", 4, out _)); Assert.False(SliderTextBox.TryConvertBack("NaN", 4, out _));
        Assert.True(SliderTextBox.TryConvertBack("999", 0, out var empty)); Assert.Equal(0, empty);
    }

    /// <summary>真实点击编辑，Enter 强制正文定位并保持焦点；普通刷新不能吞掉草稿。</summary>
    [AvaloniaFact]
    public async Task EnterAndInvalidInputUseRawSelectionWithTextScope()
    {
        using var fixture = new Fixture(); var state = new SaveData(fixture.State); await state.LoadAsync(TestContext.Current.CancellationToken);
        var operation = fixture.Operation(state); var model = new ReaderWorkspaceViewModel(operation, new CommandTable(operation), state);
        var window = CreateWindow(model); window.Show();
        try
        {
            await window.OpenAsync(fixture.Images); Config.Current.FilmStrip.IsEnabled = true; model.Refresh(); Pump(window);
            var control = window.FindControl<SliderTextBox>("PageNumberView")!;
            var point = control.TranslatePoint(new Point(control.Bounds.Width / 2, control.Bounds.Height / 2), window)!.Value;
            window.MouseDown(point, MouseButton.Left); window.MouseUp(point, MouseButton.Left);
            var input = control.FindControl<TextBox>("NumberInput")!; Assert.True(input.IsVisible); Assert.True(input.IsFocused);
            input.Text = "4"; model.Refresh(); Pump(window); Assert.Equal("4", input.Text);
            SendKey(window, Key.Left, PhysicalKey.ArrowLeft); Assert.Equal(0, operation.Book!.CurrentPage!.Index);
            SendKey(window, Key.Enter, PhysicalKey.Enter); await WaitAsync(() => operation.Book.CurrentPage.Index == 3);
            Assert.True(input.IsVisible); Assert.True(input.IsFocused); Assert.Equal("4", input.Text);
            input.Text = "错误"; SendKey(window, Key.Enter, PhysicalKey.Enter); await Task.Delay(20, TestContext.Current.CancellationToken);
            Assert.Equal(3, operation.Book.CurrentPage.Index);
            input.Text = "-9"; SendKey(window, Key.Enter, PhysicalKey.Enter); await WaitAsync(() => operation.Book.CurrentPage.Index == 0);
            input.Text = "1e20"; SendKey(window, Key.Enter, PhysicalKey.Enter); await WaitAsync(() => operation.Book.CurrentPage.Index == 4);
            Assert.Equal(4, operation.PageSelector.SelectedIndex);
        }
        finally { await window.PrepareShutdownAsync(); window.Close(); }
    }

    /// <summary>Escape 经失焦提交而不是取消；直接输入绕过静态双页及同步滑块对齐。</summary>
    [AvaloniaFact]
    public async Task EscapeAndBlurCommitWithoutSliderPairAlignment()
    {
        using var fixture = new Fixture(); var state = new SaveData(fixture.State); await state.LoadAsync(TestContext.Current.CancellationToken);
        var operation = fixture.Operation(state); var model = new ReaderWorkspaceViewModel(operation, new CommandTable(operation), state);
        var window = CreateWindow(model); window.Show();
        try
        {
            await window.OpenAsync(fixture.Images);
            Config.Current.Book.IsStaticWidePage = true; Config.Current.Slider.IsSyncPageMode = true;
            Config.Current.FilmStrip.IsEnabled = true;
            await operation.ApplySettingAsync(s => { s.PageMode = PageMode.WidePage; s.IsSupportedWidePage = false; }); Pump(window);
            var control = window.FindControl<SliderTextBox>("PageNumberView")!; control.BeginEdit();
            var input = control.FindControl<TextBox>("NumberInput")!; input.Text = "2";
            SendKey(window, Key.Escape, PhysicalKey.Escape); await WaitAsync(() => operation.Position.Index == 1);
            Assert.False(input.IsVisible); Assert.True(window.Viewer.IsFocused);
            control.BeginEdit(); input.Text = "4"; window.FindControl<TextBox>("AddressBar")!.Focus();
            await WaitAsync(() => operation.Position.Index == 3); Assert.False(input.IsVisible);
            // 单页号与阅读方向无关，普通双页滑块则仍保留原对齐规则。
            Config.Current.Slider.SliderDirection = SliderDirection.RightToLeft; model.RefreshSelection(); Pump(window);
            Assert.Equal("4 / 5", control.FindControl<TextBlock>("NumberDisplay")!.Text);
        }
        finally { await window.PrepareShutdownAsync(); window.Close(); }
    }

    /// <summary>切书撤销旧草稿，互斥入口拒绝已过期来源；关闭不提交未完成输入。</summary>
    [AvaloniaFact]
    public async Task SourceChangeAndShutdownRejectOldDraft()
    {
        using var fixture = new Fixture(); var state = new SaveData(fixture.State); await state.LoadAsync(TestContext.Current.CancellationToken);
        var operation = fixture.Operation(state); var model = new ReaderWorkspaceViewModel(operation, new CommandTable(operation), state);
        var window = CreateWindow(model); window.Show();
        try
        {
            await window.OpenAsync(fixture.Images); Pump(window); var old = operation.Book!;
            var control = window.FindControl<SliderTextBox>("PageNumberView")!; control.BeginEdit();
            var input = control.FindControl<TextBox>("NumberInput")!; input.Text = "5";
            await window.OpenAsync(fixture.Zip); Pump(window);
            Assert.False(input.IsVisible); Assert.Equal(0, operation.Position.Index);
            await operation.JumpAsync(4, expectedBook: old); Assert.Equal(0, operation.Position.Index);
            control.BeginEdit(); input.Text = "5"; await window.PrepareShutdownAsync();
            Assert.Equal("001.png", state.GetLastBook()!.Page);
        }
        finally { await window.PrepareShutdownAsync(); window.Close(); }
    }

    /// <summary>滑条默认滚轮按帧移动、命令模式只执行绑定；页号框独立按一页步进。</summary>
    [AvaloniaFact]
    public async Task SliderAndNumberWheelKeepDistinctOriginalModes()
    {
        using var fixture = new Fixture(); var state = new SaveData(fixture.State); await state.LoadAsync(TestContext.Current.CancellationToken);
        var operation = fixture.Operation(state); var model = new ReaderWorkspaceViewModel(operation, new CommandTable(operation), state);
        var window = CreateWindow(model); window.Show();
        try
        {
            await window.OpenAsync(fixture.Images); Pump(window);
            var slider = window.FindControl<Slider>("PageSliderView")!;
            var point = slider.TranslatePoint(new Point(slider.Bounds.Width / 2, slider.Bounds.Height / 2), window)!.Value;
            window.MouseWheel(point, new Avalonia.Vector(0, -.5), RawInputModifiers.None); Assert.Equal(0, operation.Position.Index);
            window.MouseWheel(point, new Avalonia.Vector(0, -.5), RawInputModifiers.None); await WaitAsync(() => operation.Position.Index == 1);
            Config.Current.Slider.MouseWheelAction = SliderMouseWheelAction.CommandDependent;
            state.SetShortcut("NextPage", "Control+WheelDown"); state.SetShortcut("NextScrollPage", "");
            window.MouseWheel(point, new Avalonia.Vector(0, -1), RawInputModifiers.Control); await WaitAsync(() => operation.Position.Index == 2);
            var number = window.FindControl<SliderTextBox>("PageNumberView")!; number.BeginEdit(); Pump(window);
            var input = number.FindControl<TextBox>("NumberInput")!;
            point = input.TranslatePoint(new Point(input.Bounds.Width / 2, input.Bounds.Height / 2), window)!.Value;
            window.MouseWheel(point, new Avalonia.Vector(0, 1), RawInputModifiers.None); await WaitAsync(() => operation.Position.Index == 1);
        }
        finally { await window.PrepareShutdownAsync(); window.Close(); }
    }

    /// <summary>真实设置取消不改配置；保存前端字段不重建正文，原字段与未知 JSON 往返。</summary>
    [AvaloniaFact]
    public async Task SliderSettingsStayIndependentAndPersistOriginalFields()
    {
        using var fixture = new Fixture(); Directory.CreateDirectory(fixture.State);
        await File.WriteAllTextAsync(Path.Combine(fixture.State, "UserSetting.json"), """{"Config":{"Slider":{"Future":17,"IsHidePageSlider":true,"IsVisiblePlaylistMark":false}}}""", TestContext.Current.CancellationToken);
        var state = new SaveData(fixture.State); await state.LoadAsync(TestContext.Current.CancellationToken);
        var operation = fixture.Operation(state); var model = new ReaderWorkspaceViewModel(operation, new CommandTable(operation), state);
        var window = CreateWindow(model); window.Show();
        try
        {
            await window.OpenAsync(fixture.Images); Pump(window);
            // 原字段现已生效；这项外观测试用原一次显示命令固定打开覆盖底栏。
            await window.ExecuteAsync("ShowHiddenPanels"); Pump(window);
            var cancel = new SettingsWindow(model); cancel.Show(window);
            cancel.FindControl<ComboBox>("SliderIndexLayout")!.SelectedIndex = 1;
            cancel.FindControl<Button>("CancelSettings")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.Equal(SliderIndexLayout.Right, Config.Current.Slider.SliderIndexLayout);
            int refreshes = 0; model.Refreshed += (_, _) => refreshes++;
            var settings = new SettingsWindow(model); settings.Show(window);
            settings.FindControl<ComboBox>("SliderIndexLayout")!.SelectedIndex = 1;
            settings.FindControl<NumericUpDown>("SliderThickness")!.Value = 37;
            settings.FindControl<NumericUpDown>("SliderOpacity")!.Value = .65m;
            settings.FindControl<ComboBox>("SliderWheel")!.SelectedIndex = 1;
            settings.FindControl<Button>("SaveSettings")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await WaitAsync(() => !settings.IsVisible); Pump(window); model.RefreshSelection(); Pump(window);
            Assert.Equal(0, refreshes); Assert.Equal(0, Grid.GetColumn(window.FindControl<SliderTextBox>("PageNumberView")!));
            var slot = window.FindControl<Grid>("DockPageSliderSocket")!; Assert.Equal(37, slot.Height); Assert.Equal(.65, slot.Opacity);
            var json = JsonNode.Parse(await File.ReadAllTextAsync(Path.Combine(fixture.State, "UserSetting.json"), TestContext.Current.CancellationToken))!;
            Assert.Equal(17, json["Config"]!["Slider"]!["Future"]!.GetValue<int>());
            Assert.True(json["Config"]!["Slider"]!["IsHidePageSlider"]!.GetValue<bool>());
            Assert.False(json["Config"]!["Slider"]!["IsVisiblePlaylistMark"]!.GetValue<bool>());
            var fresh = new SaveData(fixture.State); await fresh.LoadAsync(TestContext.Current.CancellationToken);
            Assert.Equal(SliderIndexLayout.Left, Config.Current.Slider.SliderIndexLayout); Assert.Equal(37, Config.Current.Slider.Thickness);
            Assert.Equal(SliderMouseWheelAction.CommandDependent, Config.Current.Slider.MouseWheelAction);
            Config.Current.Slider.SliderIndexLayout = SliderIndexLayout.None; model.RefreshSelection(); Pump(window);
            Assert.False(window.FindControl<SliderTextBox>("PageNumberView")!.IsVisible);
            Config.Current.Slider.IsEnabled = false; model.RefreshSelection(); Pump(window); Assert.False(slot.IsVisible);
            // 新节点截图单独保存，不覆盖此前任何阶段材料。
            Config.Current.Slider.IsEnabled = true; Config.Current.Slider.SliderIndexLayout = SliderIndexLayout.Left; model.RefreshSelection(); Pump(window);
            Config.Current.Slider.Thickness = 15; model.RefreshSelection(); Pump(window);
            var thumb = window.FindControl<Slider>("PageSliderView")!.GetVisualDescendants().OfType<Thumb>().Single();
            var thumbTop = thumb.TranslatePoint(new Point(0, 0), slot)!.Value.Y;
            Assert.InRange(thumbTop, 0, 15); Assert.True(thumbTop + thumb.Bounds.Height <= 15.01, "最小原厚度下滑块不能被固定模板留白裁切。");
            Config.Current.Slider.Thickness = 37; model.RefreshSelection(); Pump(window);
            var phase = Environment.GetEnvironmentVariable("NEEVIEW_ACCEPTANCE_PHASE") ?? "p2-slider";
            var output = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, $"../../../../../acceptance/{phase}-number-layout.png"));
            using var image = new RenderTargetBitmap(new PixelSize(1200, 800)); image.Render(window); image.Save(output, PngBitmapEncoderOptions.Default);
        }
        finally { await window.PrepareShutdownAsync(); window.Close(); }
    }

    /// <summary>装配同一正式入口，系统操作使用拒绝访问的替身。</summary>
    private static MainWindow CreateWindow(ReaderWorkspaceViewModel model)
    { var window = new MainWindow(); window.Bind(model, new BitmapFactory(new NeeView.Backends.MagickImageDecoder()), new NoPlatform()); return window; }
    /// <summary>推进绑定与布局，读取实际控件状态。</summary>
    private static void Pump(Window window) { Dispatcher.UIThread.RunJobs(); window.UpdateLayout(); }
    /// <summary>用真实 Headless 输入路由发送按下和释放。</summary>
    private static void SendKey(Window window, Key key, PhysicalKey physical)
    { window.KeyPress(key, RawInputModifiers.None, physical, null); window.KeyRelease(key, RawInputModifiers.None, physical, null); }
    /// <summary>等待真实异步导航或保存，失败时保留测试断言。</summary>
    private static async Task WaitAsync(Func<bool> complete)
    { for (int i = 0; i < 100 && !complete(); i++) { Dispatcher.UIThread.RunJobs(); await Task.Delay(10, TestContext.Current.CancellationToken); } Assert.True(complete()); Dispatcher.UIThread.RunJobs(); }
    /// <summary>本批不执行 Finder 或废纸篓操作。</summary>
    private sealed class NoPlatform : IPlatformService
    {
        /// <summary>拒绝未纳入本批的系统定位。</summary>
        public Task RevealAsync(string path, CancellationToken token = default) => throw new NotSupportedException();
        /// <summary>拒绝未纳入本批的文件删除。</summary>
        public Task TrashAsync(string path, CancellationToken token = default) => throw new NotSupportedException();
    }
}
