using System.Text.Json;
using System.Text.Json.Nodes;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using NeeView;
using NeeView.MacOS.Views;
using NeeView.MacOS.ViewModels;
using NeeView.PageFrames;
using NeeView.Runtime.LayoutPanel;
namespace NeeView.Engine.Tests;

public sealed class DockingScrollTests
{
    /// <summary>固定原算法样本：两轴余量、分段步长及五种模式，不按实现生成预期值。</summary>
    [Theory]
    [InlineData(NScrollType.NType, 0, -100)]
    [InlineData(NScrollType.ZType, -100, 0)]
    [InlineData(NScrollType.Diagonal, -100, -100)]
    [InlineData(NScrollType.Horizontal, -100, 0)]
    [InlineData(NScrollType.Vertical, 0, -100)]
    public void OriginalScrollModesUseContentMovement(NScrollType type, double x, double y)
    {
        var context = new PageFrameContext(new() { BookReadOrder = PageReadOrder.LeftToRight }, new());
        var result = new NScroll(context, new(0, 0, 300, 300), new(0, 0, 100, 100)).ScrollN(1, new ScrollPageCommandParameter { ScrollType = type }, 10);
        Assert.Equal(new NeeView.Vector(x, y), result.Vector); Assert.False(result.IsTerminated); Assert.False(result.IsLineBreak);
        // 到对应终点后零位移才报告终止；不将滚动动作直接当作翻页。
        var end = new NScroll(context, new(-200, -200, 300, 300), new(0, 0, 100, 100)).ScrollN(1, new ScrollPageCommandParameter { ScrollType = type }, 10);
        Assert.True(end.IsTerminated);
    }

    /// <summary>N/Z 换行位移、右向左符号、终端容差和可注入时钟下的原停顿语义。</summary>
    [Fact]
    public void OriginalLineBreakAndRepeatLimitStayOrdered()
    {
        var context = new PageFrameContext(new() { BookReadOrder = PageReadOrder.LeftToRight }, new());
        var parameter = new ScrollPageCommandParameter { LineBreakStopTime = .5 };
        var line = new NScroll(context, new(0, -200, 300, 300), new(0, 0, 100, 100)).ScrollN(1, parameter, 10);
        Assert.Equal(new NeeView.Vector(-100, 200), line.Vector); Assert.True(line.IsLineBreak);
        long now = 0; var control = new PageFrameScrollControl(() => now);
        now = 1000; Assert.NotNull(control.ScrollToNext(context, new(0, 0, 300, 300), new(0, 0, 100, 100), 1, parameter));
        now = 1100; Assert.Null(control.ScrollToNext(context, new(0, -200, 300, 300), new(0, 0, 100, 100), 1, parameter));
        now = 1700; Assert.Equal(line.Vector, control.ScrollToNext(context, new(0, -200, 300, 300), new(0, 0, 100, 100), 1, parameter)!.Vector);
        parameter.LineBreakStopMode = LineBreakStopMode.Page; now = 1800;
        Assert.NotNull(control.ScrollToNext(context, new(0, -200, 300, 300), new(0, 0, 100, 100), 1, parameter));
        now = 1900; Assert.Null(control.ScrollToNext(context, new(-200, -200, 300, 300), new(0, 0, 100, 100), 1, parameter));
        var rtl = new PageFrameContext(new() { BookReadOrder = PageReadOrder.RightToLeft }, new());
        var horizontal = new NScroll(rtl, new(-200, 0, 300, 100), new(0, 0, 100, 100)).ScrollN(1, new ScrollPageCommandParameter { ScrollType = NScrollType.Horizontal }, 10);
        Assert.Equal(new NeeView.Vector(100, 0), horizontal.Vector);
        Assert.True(new NScroll(context, new(0, 0, 100, 109), new(0, 0, 100, 100)).ScrollN(1, parameter, 10).IsTerminated);
    }

    /// <summary>原 leader 携组跨栏、成员拆组、组合权重与自拖拒绝，所有面板只出现一次。</summary>
    [Fact]
    public void DockLeaderAndMemberMovesPreserveOriginalGrouping()
    {
        var layout = new LayoutPanelManager(null);
        Assert.True(layout.CombinePanel("PageListPanel", "FolderPanel", PanelDock.Bottom));
        var group = layout.Find("FolderPanel")!.Value.Group;
        Assert.Equal(new[] { "FolderPanel", "PageListPanel" }, group.Select(p => p.Key)); Assert.Equal(.5, group[0].Weight);
        Assert.True(layout.MovePanel("FolderPanel", "Right", 1));
        Assert.Same(group, layout.Docks["Right"].Items[1]); Assert.Equal("Right", layout.Find("PageListPanel")!.Value.Side);
        Assert.True(layout.MovePanel("PageListPanel", "Left", 0)); Assert.Single(group);
        Assert.False(layout.CombinePanel("FolderPanel", "FolderPanel", PanelDock.Top));
        Assert.True(layout.CombinePanel("FolderPanel", "PageListPanel", PanelDock.Right));
        Assert.Equal(PanelOrientation.Horizontal, layout.Find("FolderPanel")!.Value.Group.Orientation);
        Assert.Equal(9, layout.Docks.Values.SelectMany(d => d.Items).SelectMany(g => g).Select(p => p.Key).Distinct().Count());
        Assert.Equal(PanelDock.Top, LayoutPanelManager.GetLayoutDockFromPos(50, 10, 100, 100, new()));
        Assert.Equal(PanelDock.Right, LayoutPanelManager.GetLayoutDockFromPos(90, 10, 100, 100, layout.Find("FolderPanel")!.Value.Group));
    }

    /// <summary>原 PanelLayoutV2/SelectedItem/GridLength 与命令差分参数往返，保留 Windows 未支持字段。</summary>
    [Fact]
    public async Task OriginalLayoutJsonAndScrollParametersRoundTrip()
    {
        using var fixture = new Fixture(); Directory.CreateDirectory(fixture.State);
        var path = Path.Combine(fixture.State, "UserSetting.json");
        await File.WriteAllTextAsync(path, """
        {"Config":{"Panels":{"Layout":{"Docks":{"Left":{"PanelLayoutV2":["Vertical:FolderPanel,HistoryPanel"],"SelectedItem":"HistoryPanel"},"Right":{"PanelLayoutV2":["Horizontal:PageListPanel,FileInformationPanel"],"SelectedItem":"PageListPanel"}},"Panels":{"FolderPanel":{"GridLength":"0.3*","WindowPlacement":"future"}},"Windows":{"Future":99}}}},"Commands":{"NextScrollPage":{"Parameter":{"ScrollType":"Vertical","Scroll":0.5,"Future":7}}}}
        """, TestContext.Current.CancellationToken);
        var state = new SaveData(fixture.State); await state.LoadAsync(TestContext.Current.CancellationToken);
        var layout = new LayoutPanelManager(Config.Current.Panels.Layout);
        Assert.Equal("FolderPanel", layout.Docks["Left"].SelectedItem![0].Key); Assert.Equal(.3, layout.Panels["FolderPanel"].Weight);
        Assert.Equal(NScrollType.Vertical, state.GetScrollParameter("NextScrollPage").ScrollType);
        Assert.Equal(.5, state.GetScrollParameter("NextScrollPage").Scroll);
        layout.CombinePanel("BookmarkPanel", "HistoryPanel", PanelDock.Bottom);
        Config.Current.Panels.Layout = layout.CreateMemento(); await state.SaveAsync(null, TestContext.Current.CancellationToken);
        var saved = JsonNode.Parse(await File.ReadAllTextAsync(path, TestContext.Current.CancellationToken))!;
        Assert.Equal(99, saved["Config"]!["Panels"]!["Layout"]!["Windows"]!["Future"]!.GetValue<int>());
        Assert.Equal("future", saved["Config"]!["Panels"]!["Layout"]!["Panels"]!["FolderPanel"]!["WindowPlacement"]!.GetValue<string>());
        Assert.Null(saved["Commands"]!["NextScrollPage"]!["Parameter"]);
        Assert.Equal(7, saved["Commands"]!["PrevScrollPage"]!["Parameter"]!["Future"]!.GetValue<int>());
        var fresh = new SaveData(fixture.State); await fresh.LoadAsync(TestContext.Current.CancellationToken);
        Assert.Equal(NScrollType.Vertical, fresh.GetScrollParameter("NextScrollPage").ScrollType);
        Assert.Equal(.5, fresh.GetScrollParameter("NextScrollPage").Scroll);
        var restored = new LayoutPanelManager(Config.Current.Panels.Layout);
        Assert.Equal("Left", restored.Find("BookmarkPanel")!.Value.Side);
        Assert.Equal(new[] { "FolderPanel", "HistoryPanel", "BookmarkPanel" }, restored.Docks["Left"].SelectedItem!.Select(p => p.Key));
    }

    /// <summary>真实指针拖入内容、跨栏图标、取消自动隐藏锁；纯表现不发布阅读刷新。</summary>
    [AvaloniaFact]
    public async Task PointerDockingCombinesSplitsAndCancelsWithoutReadingRefresh()
    {
        using var fixture = new Fixture(); var state = new SaveData(fixture.State); await state.LoadAsync(TestContext.Current.CancellationToken);
        var operation = fixture.Operation(state); var images = new BitmapFactory(new NeeView.Backends.MagickImageDecoder());
        var model = new ReaderWorkspaceViewModel(operation, new CommandTable(operation), state);
        var window = new MainWindow(); window.Bind(model, images, new NoPlatform()); window.Show(); window.UpdateLayout();
        try
        {
            int refreshes = 0; model.Refreshed += (_, _) => refreshes++;
            var rail = window.FindControl<StackPanel>("LeftRailItems")!;
            var source = (Control)rail.Children[1]; var start = source.TranslatePoint(new(18, 18), window)!.Value;
            var host = window.FindControl<Grid>("RightDockHost")!; var target = host.TranslatePoint(new(host.Bounds.Width / 2, host.Bounds.Height * .8), window)!.Value;
            window.MouseDown(start, MouseButton.Left); window.MouseMove(target, RawInputModifiers.LeftMouseButton); window.MouseUp(target, MouseButton.Left); window.UpdateLayout();
            Assert.Equal("Right", model.Layout.Find("PageListPanel")!.Value.Side);
            Assert.Equal(new[] { "FileInformationPanel", "PageListPanel" }, model.Layout.Docks["Right"].SelectedItem!.Select(p => p.Key));
            // leader 图标跨栏携带整个组，内容控件和原选择对象不重新创建。
            var rightRail = window.FindControl<StackPanel>("RightRailItems")!;
            source = rightRail.Children[0]; start = source.TranslatePoint(new(18, 18), window)!.Value;
            target = rail.TranslatePoint(new(18, 4), window)!.Value;
            window.MouseDown(start, MouseButton.Left); window.MouseMove(target, RawInputModifiers.LeftMouseButton); window.MouseUp(target, MouseButton.Left); window.UpdateLayout();
            Assert.Equal("Left", model.Layout.Find("PageListPanel")!.Value.Side); Assert.Equal(2, model.Layout.Docks["Left"].Items[0].Count);
            Config.Current.Panels.IsLeftAutoHide = true; model.Hover(true, false); window.UpdateLayout();
            source = rail.Children[0]; start = source.TranslatePoint(new(18, 18), window)!.Value;
            window.MouseDown(start, MouseButton.Left); window.MouseMove(new(600, 400), RawInputModifiers.LeftMouseButton);
            Assert.True(model.IsPanelDragging); Assert.True(model.LeftVisible);
            window.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null); window.KeyRelease(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null); window.MouseUp(new(600, 400), MouseButton.Left);
            Assert.False(model.IsPanelDragging); Assert.Equal(2, model.Layout.Docks["Left"].Items[0].Count); Assert.Equal(0, refreshes);
            Config.Current.Panels.IsLeftAutoHide = false; model.RefreshPanels(); window.UpdateLayout();
            var phase = Environment.GetEnvironmentVariable("NEEVIEW_ACCEPTANCE_PHASE") ?? "p2-bookmark";
            var output = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, $"../../../../../acceptance/{phase}-docking-layout.png"));
            using var image = new RenderTargetBitmap(new PixelSize(1200, 800)); image.Render(window); image.Save(output, PngBitmapEncoderOptions.Default);
        }
        finally { await window.PrepareShutdownAsync(); window.Close(); }
    }

    /// <summary>执行原滚轮命令，大图先滚动、终点才翻页；上一帧进入尾部，小图直接翻页。</summary>
    [AvaloniaFact]
    public async Task ScrollCommandMovesImageBeforeOriginalFrameNavigation()
    {
        using var fixture = new Fixture(); var state = new SaveData(fixture.State); await state.LoadAsync(TestContext.Current.CancellationToken);
        var operation = fixture.Operation(state); var images = new BitmapFactory(new NeeView.Backends.MagickImageDecoder());
        var window = new MainWindow(); window.Bind(new(operation, new CommandTable(operation), state), images, new NoPlatform()); window.Show();
        try
        {
            await window.OpenAsync(fixture.Images); window.UpdateLayout(); await window.ExecuteAsync("ViewScaleUp"); await window.Viewer.ZoomAsync(3); window.UpdateLayout();
            window.Viewer.ResetTransform(); await window.Viewer.ZoomAsync(3);
            window.Viewer.Navigate(new(.5, 0)); var before = window.Viewer.GetContentRect();
            Assert.True(before.Height > window.Viewer.Bounds.Height);
            await window.ExecuteAsync("NextScrollPage"); Assert.Equal(0, operation.Book!.CurrentPage!.Index); Assert.True(window.Viewer.GetContentRect().Y < before.Y);
            for (int i = 0; i < 20 && operation.Book.CurrentPage!.Index == 0; i++) await window.ExecuteAsync("NextScrollPage");
            Assert.Equal(1, operation.Book.CurrentPage!.Index); Assert.True(Math.Abs(window.Viewer.GetContentRect().Top) < .01, $"{window.Viewer.GetContentRect()} / {operation.Frame!.FrameRange} / {operation.Frame.Direction}");
            window.Viewer.Navigate(new(1, 0)); await window.ExecuteAsync("PrevScrollPage");
            Assert.Equal(0, operation.Book.CurrentPage!.Index); Assert.InRange(window.Viewer.GetContentRect().Bottom - window.Viewer.Bounds.Height, -.01, .01);
            await window.ExecuteAsync("SetStretchModeUniform"); await window.ExecuteAsync("NextScrollPage"); Assert.Equal(1, operation.Book.CurrentPage!.Index);
            var fitted = window.Viewer.GetContentRect(); window.Viewer.Pan(new(100000, -100000));
            Assert.Equal(fitted, window.Viewer.GetContentRect());
        }
        finally { await window.PrepareShutdownAsync(); window.Close(); }
    }

    /// <summary>反向半页在视口重算后保留原 Part 与移动方向，不能混用书籍阅读方向。</summary>
    [Fact]
    public async Task ReverseDividedPageSurvivesViewportRecalculation()
    {
        using var fixture = new Fixture();
        var wide = Path.Combine(fixture.Images, "000.png");
        using (var image = new ImageMagick.MagickImage(ImageMagick.MagickColors.White, 1000, 600)) image.Write(wide);
        var state = new SaveData(fixture.State); await state.LoadAsync(TestContext.Current.CancellationToken);
        await using var operation = fixture.Operation(state); await operation.OpenAsync(wide, TestContext.Current.CancellationToken);
        await operation.ApplySettingAsync(s => s.IsSupportedDividePage = true);
        await operation.JumpAsync(0, true); var before = operation.Frame!.FrameRange;
        Assert.Equal(1, operation.Position.Part);
        operation.SetViewport(new(900, 700), 2);
        Assert.Equal(before, operation.Frame!.FrameRange); Assert.Equal(1, operation.Position.Part); Assert.Equal(-1, operation.MoveDirection);
    }

    /// <summary>Headless 不模拟 macOS 系统文件能力。</summary>
    private sealed class NoPlatform : IPlatformService
    {
        public Task RevealAsync(string path, CancellationToken token = default) => throw new NotSupportedException();
        public Task TrashAsync(string path, CancellationToken token = default) => throw new NotSupportedException();
    }
}
