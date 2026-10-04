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
using NeeView.PageFrames;
using NeeView.MacOS.Views;
using NeeView.MacOS.ViewModels;
namespace NeeView.Engine.Tests;

/// <summary>原变换规则、共享/页面生命周期及正式绘制/命中/参数表单回归。</summary>
public sealed class ViewTransformTests
{
    [Theory]
    [InlineData(.8, 1, .5, true, 1)]
    [InlineData(1.2, -1, .5, true, 1)]
    [InlineData(.8, 1, .5, false, 1.2)]
    [InlineData(1.2, -1, .5, false, .8)]
    public void OriginalScaleSnapsOnlyAcrossDefault(double start, int direction, double step, bool snap, double expected)
        => Assert.Equal(expected, ViewTransformMath.Scale(start, direction, new() { Scale = step, IsSnapDefaultScale = snap }), 8);
    [Theory]
    [InlineData(0, 20, 30, 30)]
    [InlineData(0, -20, 30, -30)]
    [InlineData(170, 45, 0, -145)]
    [InlineData(-170, -45, 0, 145)]
    public void OriginalRotationKeepsMinimumStepThenNormalize(double start, double delta, double frequency, double expected)
        => Assert.Equal(expected, ViewTransformMath.Rotate(start, delta, frequency), 8);
    [Fact]
    public void OriginalStretchCycleSkipsDisabledModesAndStopsWithoutLoop()
    {
        var parameter = new ToggleStretchModeCommandParameter { IsLoop = false, IsEnableUniform = false };
        Assert.Equal(PageStretchMode.UniformToFill, ViewTransformMath.ToggleStretch(PageStretchMode.None, 1, parameter));
        Assert.Equal(PageStretchMode.None, ViewTransformMath.ToggleStretch(PageStretchMode.None, -1, parameter));
        Assert.Equal(PageStretchMode.UniformToHorizontal, ViewTransformMath.ToggleStretch(PageStretchMode.UniformToHorizontal, 1, parameter));
        parameter.IsLoop = true; Assert.Equal(PageStretchMode.None, ViewTransformMath.ToggleStretch(PageStretchMode.UniformToHorizontal, 1, parameter));
    }
    /// <summary>原map锁定切换清空值；共享和每页保持不混用，Clear不修改书籍缩放。</summary>
    [Fact]
    public void OriginalMapSelectsSharedOrPageValuesAndClearsOnLockChange()
    {
        var context = new SharedContext { IsScaleLocked = true, IsKeepScaleBooks = true, ShareScale = 2, IsAngleLocked = true, IsKeepAngleBooks = false, ShareAngle = 45 };
        using var map = new PageFrameTransformMap(context); var first = new PageFrameTransformKey(null, PagePart.Left); var second = new PageFrameTransformKey(null, PagePart.Right);
        var a = map.CreateAccessor(first)!; var b = map.CreateAccessor(second)!;
        Assert.Equal(2, a.Scale); Assert.Equal(0, a.Angle); a.SetScale(3); Assert.Equal(3, b.Scale); Assert.Equal(3, context.ShareScale);
        map.IsScaleLocked = false; Assert.Equal(1, a.Scale); a.SetScale(4); Assert.Equal(1, b.Scale);
        map.IsFlipLocked = true; a.SetFlipHorizontal(true); Assert.True(b.IsFlipHorizontal); Assert.True(context.ShareFlipHorizontal);
        a.SetPoint(new(80, 30)); Assert.Equal(default, b.Point); map.Clear(); Assert.Equal(1, a.Scale); Assert.False(a.IsFlipHorizontal); Assert.Equal(default, a.Point);
    }
    /// <summary>纯NType到末端不导航；小图预置snap与无snap的差别保留。</summary>
    [Fact]
    public void PresetAndPureNTypeKeepOriginalBoundarySemantics()
    {
        var area = new DragArea(new(0, 0, 100, 100), new(30, 30, 40, 40));
        Assert.Equal(default, area.SnapAlignment(LimitedHorizontalAlignment.Left, LimitedVerticalAlignment.Top, false));
        Assert.Equal(new NeeView.Vector(-30, -30), area.SnapAlignment(LimitedHorizontalAlignment.Left, LimitedVerticalAlignment.Top, true));
        var context = new PageFrameContext(new(), new()); var control = new PageFrameScrollControl(() => 1000);
        var result = control.ScrollToNext(context, new(0, 0, 100, 100), new(0, 0, 100, 100), 1, new ViewScrollNTypeCommandParameter());
        Assert.True(result!.IsTerminated);
    }
    /// <summary>复用真实原页面键，默认清除；页面记忆及Books双开关分别生效。</summary>
    [AvaloniaFact]
    public async Task TransformLifecycleResetsOrKeepsAccordingToOriginalOptions()
    {
        using var fixture = new Fixture(); var state = new SaveData(fixture.State); await state.LoadAsync(TestContext.Current.CancellationToken);
        var operation = fixture.Operation(state); var window = Window(operation, state); window.Show();
        try
        {
            await window.OpenAsync(fixture.Images); Pump(window); await window.Viewer.ZoomAsync(2); await window.ExecuteAsync("ViewRotateRight"); await window.ExecuteAsync("ViewFlipHorizontalOn");
            await operation.JumpAsync(1); await window.Viewer.RefreshAsync(); Assert.Equal(1, window.Viewer.TransformScale); Assert.Equal(0, window.Viewer.TransformAngle); Assert.False(window.Viewer.IsFlipHorizontal);
            Config.Current.View.IsKeepPageTransform = true;
            await window.Viewer.ZoomAsync(2); await operation.JumpAsync(2); await window.Viewer.RefreshAsync(); Assert.Equal(1, window.Viewer.TransformScale);
            await operation.JumpAsync(1); await window.Viewer.RefreshAsync(); Assert.Equal(2, window.Viewer.TransformScale);
            Config.Current.View.IsKeepScale = true; Config.Current.View.IsKeepAngle = true; Config.Current.View.IsKeepFlip = true;
            await window.Viewer.RefreshAsync(); await window.Viewer.ZoomAsync(3); await window.ExecuteAsync("ViewRotateRight"); await window.ExecuteAsync("ViewFlipHorizontalOn");
            var angle = window.Viewer.TransformAngle; await operation.JumpAsync(2); await window.Viewer.RefreshAsync(); Assert.Equal(3, window.Viewer.TransformScale); Assert.Equal(angle, window.Viewer.TransformAngle); Assert.True(window.Viewer.IsFlipHorizontal);
            await window.OpenAsync(fixture.Zip); await window.Viewer.RefreshAsync(); Assert.Equal(1, window.Viewer.TransformScale); Assert.Equal(0, window.Viewer.TransformAngle); Assert.False(window.Viewer.IsFlipHorizontal);
            Config.Current.View.IsKeepScaleBooks = true; Config.Current.View.IsKeepAngleBooks = true; Config.Current.View.IsKeepFlipBooks = true;
            await window.Viewer.ZoomAsync(2); await window.ExecuteAsync("ViewRotateRight"); await window.ExecuteAsync("ViewFlipVerticalOn"); angle = window.Viewer.TransformAngle;
            await window.OpenAsync(fixture.Images); await window.Viewer.RefreshAsync(); Assert.Equal(2, window.Viewer.TransformScale); Assert.Equal(angle, window.Viewer.TransformAngle); Assert.True(window.Viewer.IsFlipVertical);
            await window.ExecuteAsync("ViewReset"); Assert.Equal(1, window.Viewer.TransformScale); Assert.Equal(0, window.Viewer.TransformAngle); Assert.False(window.Viewer.IsFlipVertical);
        }
        finally { await window.PrepareShutdownAsync(); window.Close(); }
    }
    /// <summary>菜单/输入共用共享参数；基准缩放落入原Props，Reset不清除。</summary>
    [AvaloniaFact]
    public async Task HostCommandsUseParametersAndKeepBaseScaleSeparate()
    {
        using var fixture = new Fixture(); var state = new SaveData(fixture.State); await state.LoadAsync(TestContext.Current.CancellationToken); var operation = fixture.Operation(state);
        state.SetCommandParameter("ViewScaleUp", new ViewScaleCommandParameter { Scale = .5, IsSnapDefaultScale = false });
        state.SetCommandParameter("ViewRotateLeft", new ViewRotateCommandParameter { Angle = 20 });
        var window = Window(operation, state); window.Show();
        try
        {
            await window.OpenAsync(fixture.Images); Pump(window); await window.ExecuteAsync("ViewScaleUp"); Assert.Equal(1.5, window.Viewer.TransformScale);
            await window.ExecuteAsync("ViewScaleDown"); Assert.Equal(1, window.Viewer.TransformScale);
            Config.Current.View.AngleFrequency = 30; await window.ExecuteAsync("ViewRotateRight"); Assert.Equal(30, window.Viewer.TransformAngle);
            await window.ExecuteAsync("ViewFlipHorizontalOn"); Assert.Equal(-30, window.Viewer.TransformAngle); await window.ExecuteAsync("ViewFlipHorizontalOn"); Assert.Equal(-30, window.Viewer.TransformAngle);
            state.SetCommandParameter("ToggleViewFlipHorizontal", new ToggleCommandParameter { ToggleMode = ToggleMode.On });
            await window.ExecuteAsync("ToggleViewFlipHorizontal"); Assert.True(window.Viewer.IsFlipHorizontal);
            await window.ExecuteAsync("ToggleViewFlipHorizontal", true); Assert.False(window.Viewer.IsFlipHorizontal);
            await window.ExecuteAsync("ViewBaseScaleUp"); Assert.Equal(1.2, operation.Book!.Setting.BaseScale); Assert.Equal(1, window.Viewer.TransformScale);
            await window.ExecuteAsync("ViewReset"); Assert.Equal(1.2, operation.Book.Setting.BaseScale);
            await operation.SaveAsync(); Assert.Equal(1.2, state.Find(operation.Book.Path)!.BaseScale);
            await window.ExecuteAsync("ViewScrollNTypeDown"); Assert.Equal(0, operation.Position.Index);
            state.SetCommandParameter("ViewPresetScroll", new ViewPresetScrollCommandParameter { Horizontal = LimitedHorizontalAlignment.Left, Vertical = LimitedVerticalAlignment.Top, IsSnap = true });
            await window.ExecuteAsync("ViewPresetScroll"); var rect = window.Viewer.GetContentRect(); Assert.Equal(0, rect.X, 6); Assert.Equal(0, rect.Y, 6);
            await window.ExecuteAsync("SetStretchModeUniformToFill"); Assert.True(window.Viewer.IsFlipHorizontal == false);
            foreach (var name in new[] { "ViewReset", "ViewRotateLeft", "ViewScaleStretch", "ViewBaseScaleUp", "ViewPresetScroll", "ViewScrollNTypeDown", "SetStretchModeUniformToFill" }) Assert.True(window.IsCommandAvailable(name));
        }
        finally { await window.PrepareShutdownAsync(); window.Close(); }
    }
    /// <summary>矩阵往返/导航器使用同一方向，自动旋转及翻转组合不会误命中双页书籍。</summary>
    [AvaloniaFact]
    public async Task DrawingAndBookCardHitTestShareTransformMatrix()
    {
        using var fixture = new Fixture(); var library = Path.Combine(fixture.Root, "library"); Directory.CreateDirectory(library);
        var child = Path.Combine(library, "child"); Directory.CreateDirectory(child); File.Copy(Path.Combine(fixture.Images, "001.png"), Path.Combine(child, "001.png"));
        var state = new SaveData(fixture.State); await state.LoadAsync(TestContext.Current.CancellationToken); var operation = fixture.Operation(state); var window = Window(operation, state); window.Show();
        try
        {
            await window.OpenAsync(library); Pump(window); await window.Viewer.RefreshAsync();
            await window.Viewer.RotateAsync(1, new() { Angle = 30 }); window.Viewer.Flip(true, true);
            Pump(window); using (var screenshot = window.CaptureRenderedFrame()) SaveImage(screenshot!, "transform");
            using var presenter = new ReaderTransformPresenter { Viewport = new(window.Viewer.Bounds.Width, window.Viewer.Bounds.Height) };
            presenter.Synchronize(operation.Book, operation.Frame); presenter.Angle = window.Viewer.TransformAngle; presenter.IsFlipHorizontal = true;
            var target = presenter.GetTargets().Single().Target; var point = presenter.GetMatrix().Transform(new Point(target.X + target.Width / 2, target.Y + target.Height / 3));
            Assert.True(presenter.GetMatrix().TryInvert(out var inverse)); var roundTrip = inverse.Transform(point); Assert.Equal(target.X + target.Width / 2, roundTrip.X, 8);
            var parentPath = operation.Book!.Path; var requested = new List<Page>(); window.Viewer.ChildBookRequested += (_, page) => requested.Add(page);
            var click = window.Viewer.TranslatePoint(point, window)!.Value; window.MouseMove(click); window.MouseDown(click, MouseButton.Left); window.MouseUp(click, MouseButton.Left);
            window.MouseDown(click, MouseButton.Left); window.MouseUp(click, MouseButton.Left);
            await WaitAsync(() => operation.Book!.Path == child); Assert.Single(requested); Assert.NotEqual(parentPath, operation.Book!.Path);
        }
        finally { await window.PrepareShutdownAsync(); window.Close(); }
    }
    /// <summary>导航器的归一化原图位置经过翻转/旋转后仍居中，分割先按原裁剪定位。</summary>
    [AvaloniaFact]
    public async Task NavigatorCentersMirroredRotatedImagePoint()
    {
        using var fixture = new Fixture(); var state = new SaveData(fixture.State); await state.LoadAsync(TestContext.Current.CancellationToken); var operation = fixture.Operation(state); var window = Window(operation, state); window.Show();
        try
        {
            await window.OpenAsync(fixture.Images); Pump(window); await window.Viewer.ZoomAsync(3); await window.Viewer.RotateAsync(1, new() { Angle = 45 }); window.Viewer.Flip(true, true);
            window.Viewer.Navigate(new(.2, .3));
            using var presenter = new ReaderTransformPresenter { Viewport = new(window.Viewer.Bounds.Width, window.Viewer.Bounds.Height) };
            presenter.Synchronize(operation.Book, operation.Frame); presenter.Scale = window.Viewer.TransformScale; presenter.Angle = window.Viewer.TransformAngle; presenter.IsFlipHorizontal = true;
            var target = presenter.GetTargets().Single().Target;
            var offset = presenter.GetMatrix(false).Transform(new Point(target.X + target.Width * .2, target.Y + target.Height * .3));
            var rect = window.Viewer.GetContentRect(); Assert.Equal(window.Viewer.Bounds.Width / 2 - offset.X, rect.X + rect.Width / 2, 6); Assert.Equal(window.Viewer.Bounds.Height / 2 - offset.Y, rect.Y + rect.Height / 2, 6);
        }
        finally { await window.PrepareShutdownAsync(); window.Close(); }
    }
    /// <summary>原枚举和多态/未知字段读取，参数草稿取消不落盘，编辑共享拥有者后可重启。</summary>
    [AvaloniaFact]
    public async Task ParameterEditorKeepsDraftAndUnknownOriginalJson()
    {
        using var fixture = new Fixture(); Directory.CreateDirectory(fixture.State);
        await File.WriteAllTextAsync(Path.Combine(fixture.State, "UserSetting.json"), """{"Config":{"View":{"Future":9}},"Commands":{"ViewRotateLeft":{"Parameter":{"$type":"ViewRotateCommandParameter","Angle":60,"Future":7},"ShortCutKey":"R"}}}""", TestContext.Current.CancellationToken);
        var state = new SaveData(fixture.State); await state.LoadAsync(TestContext.Current.CancellationToken);
        var draft = CommandParameterEdit.Create(state, "ViewRotateRight")!; var editor = new CommandParameterWindow(draft, "旋转"); editor.Show(); Pump(editor);
        try
        {
            editor.FindControl<NumericUpDown>("Angle")!.Value = 90; Assert.Equal(60, state.GetCommandParameter<ViewRotateCommandParameter>("ViewRotateRight").Angle);
            using (var screenshot = editor.CaptureRenderedFrame()) SaveImage(screenshot!, "parameter");
            draft.Apply(state); await state.SaveAsync(null, TestContext.Current.CancellationToken);
            var raw = JsonNode.Parse(await File.ReadAllTextAsync(Path.Combine(fixture.State, "UserSetting.json"), TestContext.Current.CancellationToken))!;
            Assert.Null(raw["Commands"]!["ViewRotateRight"]); Assert.Equal(7, raw["Commands"]!["ViewRotateLeft"]!["Parameter"]!["Future"]!.GetValue<int>()); Assert.Equal("ViewRotateCommandParameter", raw["Commands"]!["ViewRotateLeft"]!["Parameter"]!["$type"]!.GetValue<string>());
            await new SaveData(fixture.State).LoadAsync(TestContext.Current.CancellationToken); Assert.Equal(90, state.GetCommandParameter<ViewRotateCommandParameter>("ViewRotateLeft").Angle);
        }
        finally { editor.Close(); }
    }
    [AvaloniaFact]
    public async Task UneditedOriginalNumericValuesSurviveBothFormsAndRestart()
    {
        using var fixture = new Fixture(); var state = new SaveData(fixture.State); await state.LoadAsync(TestContext.Current.CancellationToken);
        state.SetCommandParameter("PrevScrollPage", new ScrollPageCommandParameter { LineBreakStopTime = 5.5, EndMargin = 1500 });
        var draft = CommandParameterEdit.Create(state, "NextScrollPage")!; var editor = new CommandParameterWindow(draft, "滚动翻页"); editor.Show(); Pump(editor);
        try
        {
            Assert.Equal(5.5m, editor.FindControl<NumericUpDown>("LineBreakStopTime")!.Value);
            Assert.Equal(1500m, editor.FindControl<NumericUpDown>("EndMargin")!.Value); draft.Apply(state);
        }
        finally { editor.Close(); }
        Config.Current.View.AngleFrequency = 270; Config.Current.View.ViewOriginCenterRatio = .75; Config.Current.BookSetting.BaseScale = 3;
        var operation = fixture.Operation(state); var settings = new SettingsWindow(new(operation, new CommandTable(operation), state)); settings.Show(); Pump(settings);
        try
        {
            settings.FindControl<CheckBox>("KeepAngle")!.IsChecked = true;
            settings.FindControl<Button>("SaveSettings")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); await WaitAsync(() => !settings.IsVisible);
            var reloaded = new SaveData(fixture.State); await reloaded.LoadAsync(TestContext.Current.CancellationToken);
            Assert.Equal(270, Config.Current.View.AngleFrequency); Assert.Equal(.75, Config.Current.View.ViewOriginCenterRatio); Assert.Equal(3, Config.Current.BookSetting.BaseScale);
            var parameter = reloaded.GetCommandParameter<ScrollPageCommandParameter>("NextScrollPage"); Assert.Equal(5.5, parameter.LineBreakStopTime); Assert.Equal(1500, parameter.EndMargin);
        }
        finally { settings.Close(); await operation.DisposeAsync(); }
    }
    /// <summary>原窗口跟随保留相对适配比例，关闭跟随回到1，锁定阻止跟随。</summary>
    [Fact]
    public async Task ResizeTrackingKeepsManualScaleRelativeToStretch()
    {
        using var fixture = new Fixture(); var state = new SaveData(fixture.State); await state.LoadAsync(TestContext.Current.CancellationToken); await using var operation = fixture.Operation(state);
        await operation.OpenAsync(fixture.Images, TestContext.Current.CancellationToken); operation.SetViewport(new(800, 600), 1);
        Config.Current.View.IsScaleStretchTracking = true;
        using var presenter = new ReaderTransformPresenter { Viewport = new(800, 600) }; presenter.Synchronize(operation.Book, operation.Frame); presenter.Angle = 30; presenter.Stretch(); presenter.Scale *= 2;
        var first = presenter.Scale; presenter.Viewport = new(1600, 1200); presenter.Synchronize(operation.Book, operation.Frame); Assert.Equal(first * 2, presenter.Scale, 8);
        Config.Current.View.IsScaleStretchTracking = false; presenter.Synchronize(operation.Book, operation.Frame); Assert.Equal(1, presenter.Scale);
        Config.Current.View.IsKeepScale = true; Config.Current.View.IsScaleStretchTracking = true; presenter.Synchronize(operation.Book, operation.Frame); presenter.Scale = 3;
        presenter.Viewport = new(800, 600); presenter.Synchronize(operation.Book, operation.Frame); Assert.Equal(3, presenter.Scale);
    }
    [AvaloniaFact]
    public async Task ViewSettingsCancelAndSaveFailureKeepOriginalRuntimeFields()
    {
        using var fixture = new Fixture(); var state = new SaveData(fixture.State); await state.LoadAsync(TestContext.Current.CancellationToken); var operation = fixture.Operation(state);
        Config.Current.View.StretchMode = PageStretchMode.UniformToFill; Config.Current.View.StretchMode = PageStretchMode.None;
        var model = new ReaderWorkspaceViewModel(operation, new CommandTable(operation), state); var settings = new SettingsWindow(model, _ => true); settings.Show(); Pump(settings);
        try
        {
            settings.FindControl<CheckBox>("KeepScale")!.IsChecked = true; settings.FindControl<NumericUpDown>("BaseScale")!.Value = 1.5m; Assert.False(Config.Current.View.IsKeepScale);
            var blocker = Path.Combine(fixture.State, "UserSetting.json.tmp"); Directory.CreateDirectory(blocker);
            settings.FindControl<Button>("SaveSettings")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); await WaitAsync(() => settings.FindControl<TextBlock>("Message")!.Text?.StartsWith("保存失败") == true);
            Assert.False(Config.Current.View.IsKeepScale); Assert.Equal(1, Config.Current.BookSetting.BaseScale); Assert.Equal(PageStretchMode.UniformToFill, Config.Current.View.ValidStretchMode);
            Directory.Delete(blocker); settings.FindControl<Button>("SaveSettings")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); await WaitAsync(() => !settings.IsVisible);
            Assert.True(Config.Current.View.IsKeepScale); Assert.Equal(1.5, Config.Current.BookSetting.BaseScale);
        }
        finally { settings.Close(); await operation.DisposeAsync(); }
    }
    /// <summary>Retina原始大小只做一次设备比例换算；基准缩放额外独立作用。</summary>
    [Fact]
    public async Task OriginalSizeAtRetinaDoesNotApplyDeviceScaleTwice()
    {
        using var fixture = new Fixture(); var state = new SaveData(fixture.State); await state.LoadAsync(TestContext.Current.CancellationToken); await using var operation = fixture.Operation(state);
        await operation.OpenAsync(fixture.Images, TestContext.Current.CancellationToken); Config.Current.View.StretchMode = PageStretchMode.None; operation.SetViewport(new(800, 600), 2);
        using var presenter = new ReaderTransformPresenter { Viewport = new(800, 600), DeviceScale = 2 }; presenter.Synchronize(operation.Book, operation.Frame); presenter.Stretch();
        Assert.Equal(1, presenter.Scale); Assert.Equal(operation.Book!.CurrentPage!.Content.PageDataSource.Size.Width / 2, presenter.GetContentRect().Width, 8);
        operation.Book.Setting.BaseScale = 1.5; Assert.Equal(operation.Book.CurrentPage.Content.PageDataSource.Size.Width * .75, presenter.GetContentRect().Width, 8);
    }
    private static MainWindow Window(BookOperation operation, SaveData state)
    { var window = new MainWindow(); window.Bind(new ReaderWorkspaceViewModel(operation, new CommandTable(operation), state), new BitmapFactory(new NeeView.Backends.MagickImageDecoder()), new NoPlatform()); return window; }
    private static void Pump(Window window) { Dispatcher.UIThread.RunJobs(); window.UpdateLayout(); Dispatcher.UIThread.RunJobs(); }
    private static async Task WaitAsync(Func<bool> predicate)
    { for (int i = 0; i < 200 && !predicate(); i++) { Dispatcher.UIThread.RunJobs(); await Task.Delay(10, TestContext.Current.CancellationToken); } Assert.True(predicate()); }
    private static void SaveImage(Bitmap image, string label)
    { var phase = Environment.GetEnvironmentVariable("NEEVIEW_ACCEPTANCE_PHASE") ?? "p2-view-transform"; image.Save(Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, $"../../../../../acceptance/{phase}-{label}-layout.png")), PngBitmapEncoderOptions.Default); }
    private sealed class SharedContext : IShareTransformContext
    {
        public bool IsFlipLocked { get; set; } public bool IsScaleLocked { get; set; } public bool IsAngleLocked { get; set; }
        public bool IsKeepAngleBooks { get; set; } public bool IsKeepFlipBooks { get; set; } public bool IsKeepScaleBooks { get; set; }
        public double ShareAngle { get; set; } public bool ShareFlipHorizontal { get; set; } public bool ShareFlipVertical { get; set; } public double ShareScale { get; set; } = 1;
    }
    private sealed class NoPlatform : IPlatformService
    { public Task RevealAsync(string path, CancellationToken token) => Task.CompletedTask; public Task TrashAsync(string path, CancellationToken token) => Task.CompletedTask; }
}
