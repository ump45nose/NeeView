using System.Text.Json;
using System.Text.Json.Nodes;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using NeeView.MacOS.ViewModels;
using NeeView.MacOS.Views;
using NeeView.Runtime.LayoutPanel;
using NeeView.Windows;
namespace NeeView.Engine.Tests;

public sealed class FloatingPanelTests
{
    /// <summary>固定原浮动/关闭/重开/停靠及拆组顺序；关闭位置与打开窗口集合独立。</summary>
    [Fact]
    public void OriginalFloatCloseAndDockKeepDistinctState()
    {
        var layout = new LayoutPanelManager(null);
        layout.CombinePanel("PageListPanel", "FolderPanel", PanelDock.Bottom);
        var group = layout.Find("FolderPanel")!.Value.Group;
        layout.OpenWindow("PageListPanel", new(WindowStateEx.Normal, 10, 20, 320, 480));
        Assert.Single(group); Assert.Equal("FolderPanel", group[0].Key);
        Assert.Equal(1, layout.Docks["Left"].Items.IndexOf(layout.Find("PageListPanel")!.Value.Group));
        Assert.True(layout.IsPanelSelected("PageListPanel")); Assert.Contains("PageListPanel", layout.Windows);
        layout.Close("PageListPanel"); Assert.False(layout.IsPanelSelected("PageListPanel")); Assert.True(layout.IsFloating("PageListPanel"));
        layout.Open("PageListPanel"); Assert.Contains("PageListPanel", layout.Windows);
        layout.OpenDock("PageListPanel"); Assert.Empty(layout.Windows); Assert.False(layout.IsFloating("PageListPanel"));
        Assert.Same(layout.Find("PageListPanel")!.Value.Group, layout.Docks["Left"].SelectedItem);
        layout.OpenWindow("FolderPanel"); Assert.Same(layout.Find("PageListPanel")!.Value.Group, layout.Docks["Left"].SelectedItem);
        layout.CombinePanel("FolderPanel", "FileInformationPanel", PanelDock.Bottom);
        Assert.Empty(layout.Windows); Assert.False(layout.Panels["FolderPanel"].WindowPlacement.IsValid());
    }
    /// <summary>原Windows.Panels/WindowPlacement往返，未知面板/字段/损坏位置仍保存，空选择不复活。</summary>
    [Fact]
    public async Task OriginalFloatingJsonPreservesUnknownDataAndClosedSelection()
    {
        using var fixture = new Fixture(); Directory.CreateDirectory(fixture.State);
        var file = Path.Combine(fixture.State, "UserSetting.json");
        await File.WriteAllTextAsync(file, """
        {"Config":{"Panels":{"Layout":{"Docks":{"Left":{"PanelLayoutV2":["Vertical:FolderPanel,HistoryPanel"],"SelectedItem":"FolderPanel"}},"Panels":{"HistoryPanel":{"GridLength":".7*","WindowPlacement":"Normal,40,50,300,500","Future":8},"FolderPanel":{"WindowPlacement":"future"}},"Windows":{"Panels":["HistoryPanel","FuturePanel"],"Future":99},"AlternativePanelSource":{"Future":2}}}}}
        """, TestContext.Current.CancellationToken);
        var state = new SaveData(fixture.State); await state.LoadAsync(TestContext.Current.CancellationToken);
        var layout = new LayoutPanelManager(Config.Current.Panels.Layout);
        Assert.Contains("HistoryPanel", layout.Windows); Assert.Single(layout.Find("HistoryPanel")!.Value.Group);
        Assert.Equal(.7, layout.Panels["HistoryPanel"].Weight); Assert.Equal(40, layout.Panels["HistoryPanel"].WindowPlacement.Left);
        layout.Close("FolderPanel", true); Config.Current.Panels.Layout = layout.CreateMemento();
        await state.SaveAsync(null, TestContext.Current.CancellationToken);
        var saved = JsonNode.Parse(await File.ReadAllTextAsync(file, TestContext.Current.CancellationToken))!["Config"]!["Panels"]!["Layout"]!;
        Assert.Equal(99, saved["Windows"]!["Future"]!.GetValue<int>()); Assert.Equal(8, saved["Panels"]!["HistoryPanel"]!["Future"]!.GetValue<int>());
        Assert.Equal("future", saved["Panels"]!["FolderPanel"]!["WindowPlacement"]!.GetValue<string>());
        Assert.Contains("FuturePanel", saved["Windows"]!["Panels"]!.AsArray().Select(n => n!.GetValue<string>()));
        Assert.Equal(2, saved["AlternativePanelSource"]!["Future"]!.GetValue<int>());
        var restored = new LayoutPanelManager(JsonSerializer.Deserialize<LayoutPanelManagerMemento>(saved.ToJsonString()));
        Assert.Null(restored.Docks["Left"].SelectedItem); Assert.Contains("HistoryPanel", restored.Windows);
        Assert.Equal("", WindowPlacement.None.ToString()); Assert.False(WindowPlacement.Parse("future").IsValid());
    }
    /// <summary>正式右键浮动只迁移唯一控件，关闭保留位置，重开/停靠不重建正文或选择对象。</summary>
    [AvaloniaFact]
    public async Task ActualFloatMenuReusesContentAndSupportsReopenDock()
    {
        using var fixture = new Fixture(); var state = new SaveData(fixture.State); await state.LoadAsync(TestContext.Current.CancellationToken);
        var operation = fixture.Operation(state); var images = new BitmapFactory(new NeeView.Backends.MagickImageDecoder());
        var model = new ReaderWorkspaceViewModel(operation, new CommandTable(operation), state); var main = new MainWindow(); main.Bind(model, images, new NoPlatform()); main.Show(); main.UpdateLayout();
        try
        {
            await main.OpenAsync(fixture.Images); Dispatcher.UIThread.RunJobs(); main.UpdateLayout();
            var list = main.FindControl<ListBox>("FolderList")!; var content = list.GetVisualAncestors().OfType<Border>().Last(b => b.Tag as string == "FolderPanel");
            var selected = list.SelectedItem; int reads = 0; model.Refreshed += (_, _) => reads++;
            var button = Assert.IsType<Button>(main.FindControl<StackPanel>("LeftRailItems")!.Children[0]);
            Assert.Equal(new[] { "浮动", "停靠", "关闭" }, button.ContextMenu!.Items.OfType<MenuItem>().Select(i => i.Header));
            Assert.IsType<MenuItem>(button.ContextMenu.Items[0]).RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            var floating = Assert.IsType<FloatingPanelWindow>(Assert.Single(main.OwnedWindows)); floating.UpdateLayout();
            Assert.Same(content, floating.ContentHost.Child); Assert.Same(selected, list.SelectedItem); Assert.True(model.ShowFolderList);
            Assert.False(WindowInteraction.HasDialog(main)); Assert.Null(model.Layout.Docks["Left"].SelectedItem);
            var header = floating.PanelHeader.TranslatePoint(new(25, 12), floating)!.Value;
            floating.MouseDown(header, MouseButton.Left); floating.MouseUp(header, MouseButton.Left);
            Assert.True(floating.IsVisible); // 标题单击不能关闭浮窗。
            floating.Position = new(210, 120); floating.Width = 360; floating.Height = 520; floating.UpdateLayout();
            floating.WindowState = WindowState.Maximized;
            Assert.Equal(WindowStateEx.Maximized, model.Layout.Panels["FolderPanel"].WindowPlacement.WindowStateEx);
            Assert.Equal(210, model.Layout.Panels["FolderPanel"].WindowPlacement.Left);
            floating.WindowState = WindowState.Normal;
            floating.Close(); Assert.Empty(main.OwnedWindows); Assert.True(model.Layout.IsFloating("FolderPanel")); Assert.False(model.ShowFolderList);
            model.SelectPanel("FolderPanel"); floating = Assert.IsType<FloatingPanelWindow>(Assert.Single(main.OwnedWindows)); floating.UpdateLayout();
            Assert.Same(content, floating.ContentHost.Child); Assert.Equal(210, floating.Position.X);
            Assert.IsType<MenuItem>(floating.PanelHeader.ContextMenu!.Items[1]).RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent)); main.UpdateLayout();
            Assert.Empty(main.OwnedWindows); Assert.Same(main, TopLevel.GetTopLevel(content)); Assert.False(model.Layout.IsFloating("FolderPanel")); Assert.Same(selected, list.SelectedItem); Assert.Equal(0, reads);
        }
        finally { await main.PrepareShutdownAsync(); main.Close(); }
    }
    /// <summary>浮窗跨屏幕坐标拖回内容/图标栏，Escape取消保持原位置和打开集合。</summary>
    [AvaloniaFact]
    public async Task FloatPointerDragBackAndEscapeUseRealHostCoordinates()
    {
        using var fixture = new Fixture(); var state = new SaveData(fixture.State); await state.LoadAsync(TestContext.Current.CancellationToken);
        var operation = fixture.Operation(state); var images = new BitmapFactory(new NeeView.Backends.MagickImageDecoder());
        var model = new ReaderWorkspaceViewModel(operation, new CommandTable(operation), state); var main = new MainWindow(); main.Bind(model, images, new NoPlatform()); main.Position = new(100, 100); main.Show(); main.UpdateLayout();
        try
        {
            model.Layout.OpenWindow("PageListPanel", new(WindowStateEx.Normal, 700, 500, 300, 400)); main.UpdateLayout();
            var floating = Assert.IsType<FloatingPanelWindow>(Assert.Single(main.OwnedWindows)); floating.UpdateLayout();
            var start = floating.PanelHeader.TranslatePoint(new(25, 10), floating)!.Value;
            var host = main.FindControl<Grid>("RightDockHost")!; var point = host.TranslatePoint(new(host.Bounds.Width / 2, host.Bounds.Height * .8), main)!.Value;
            var target = floating.PointToClient(main.PointToScreen(point));
            floating.MouseDown(start, MouseButton.Left); floating.MouseMove(target, RawInputModifiers.LeftMouseButton);
            Assert.True(model.IsPanelDragging); Assert.NotEmpty(main.FindControl<Canvas>("PanelDropPreview")!.Children);
            SendKey(floating, Key.Escape, PhysicalKey.Escape); floating.MouseUp(target, MouseButton.Left);
            Assert.False(model.IsPanelDragging); Assert.Contains("PageListPanel", model.Layout.Windows); Assert.Empty(main.FindControl<Canvas>("PanelDropPreview")!.Children);
            floating.MouseDown(start, MouseButton.Left); floating.MouseMove(target, RawInputModifiers.LeftMouseButton); floating.MouseUp(target, MouseButton.Left); main.UpdateLayout();
            Assert.Empty(main.OwnedWindows); Assert.Equal("Right", model.Layout.Find("PageListPanel")!.Value.Side);
            Assert.Equal(new[] { "FileInformationPanel", "PageListPanel" }, model.Layout.Docks["Right"].SelectedItem!.Select(p => p.Key));
            model.Layout.OpenWindow("PageListPanel", new(WindowStateEx.Normal, 700, 500, 300, 400)); main.UpdateLayout();
            floating = Assert.IsType<FloatingPanelWindow>(Assert.Single(main.OwnedWindows)); floating.UpdateLayout();
            start = floating.PanelHeader.TranslatePoint(new(25, 10), floating)!.Value;
            point = main.FindControl<StackPanel>("LeftRailItems")!.TranslatePoint(new(18, 4), main)!.Value; target = floating.PointToClient(main.PointToScreen(point));
            floating.MouseDown(start, MouseButton.Left); floating.MouseMove(target, RawInputModifiers.LeftMouseButton); floating.MouseUp(target, MouseButton.Left); main.UpdateLayout();
            Assert.Empty(main.OwnedWindows); Assert.Equal("Left", model.Layout.Find("PageListPanel")!.Value.Side); Assert.False(model.Layout.IsFloating("PageListPanel"));
        }
        finally { await main.PrepareShutdownAsync(); main.Close(); }
    }
    /// <summary>浮窗文本/列表键与Command+W作用域；非模态浮窗不强制锁定主自动隐藏。</summary>
    [AvaloniaFact]
    public async Task FloatingInputScopesAndAutoHideRemainIndependent()
    {
        using var fixture = new Fixture(); var state = new SaveData(fixture.State); await state.LoadAsync(TestContext.Current.CancellationToken);
        Config.Current.Panels.IsLeftAutoHide = true; Config.Current.Panels.IsRightAutoHide = true; Config.Current.AutoHide.AutoHideDelayTime = .001;
        var operation = fixture.Operation(state); var images = new BitmapFactory(new NeeView.Backends.MagickImageDecoder());
        var model = new ReaderWorkspaceViewModel(operation, new CommandTable(operation), state); var main = new MainWindow(); main.Bind(model, images, new NoPlatform()); main.Show();
        try
        {
            await main.OpenAsync(fixture.Images); model.Layout.OpenWindow("HistoryPanel"); var floating = Assert.IsType<FloatingPanelWindow>(Assert.Single(main.OwnedWindows));
            main.UpdateLayout(); floating.UpdateLayout(); var input = main.FindControl<TextBox>("HistorySearchBox")!; input.Focus();
            SendKey(floating, Key.Left, PhysicalKey.ArrowLeft); Assert.Equal(0, operation.Book!.CurrentPage!.Index);
            var list = main.FindControl<ListBox>("HistoryList")!; list.Focus(); SendKey(floating, Key.Down, PhysicalKey.ArrowDown); Assert.Equal(0, operation.Book.CurrentPage!.Index);
            // FindControl使用原NameScope，移动视觉父级后仍返回同一正式控件；Enter进入原打开流程。
            Assert.Same(list, floating.ContentHost.Child!.GetVisualDescendants().OfType<ListBox>().Single());
            await main.OpenAsync(fixture.Zip); await main.OpenAsync(fixture.Images); Dispatcher.UIThread.RunJobs(); floating.UpdateLayout();
            list.SelectedItem = model.History.Single(r => r.Path == fixture.Zip); list.Focus();
            SendKey(floating, Key.Enter, PhysicalKey.Enter);
            for (int i = 0; i < 60 && operation.Book!.Path != fixture.Zip; i++) await Task.Delay(20, TestContext.Current.CancellationToken);
            Assert.Equal(fixture.Zip, operation.Book!.Path);
            var auto = new AutoHidePresenter(main, model); main.Viewer.Focus(); await Task.Delay(80, TestContext.Current.CancellationToken); auto.Update();
            Assert.False(WindowInteraction.HasDialog(main)); Assert.False(model.RightVisible);
            using (auto)
            {
                var dialog = new Window(); dialog.Show(main); auto.Update(); Assert.True(WindowInteraction.HasDialog(main)); Assert.True(model.RightVisible); dialog.Close();
            }
            SendKey(floating, Key.W, PhysicalKey.W, RawInputModifiers.Meta);
            Assert.Empty(main.OwnedWindows); Assert.True(main.IsVisible); Assert.NotNull(operation.Book);
        }
        finally { await main.PrepareShutdownAsync(); main.Close(); }
    }
    /// <summary>退出保存失败保留浮窗/内容/资源且允许重试，成功退出保存打开集合供重启恢复。</summary>
    [AvaloniaFact]
    public async Task ShutdownFailureRetainsFloatingContentAndRestartRestores()
    {
        using var fixture = new Fixture(); var state = new SaveData(fixture.State); await state.LoadAsync(TestContext.Current.CancellationToken);
        var operation = fixture.Operation(state); var images = new BitmapFactory(new NeeView.Backends.MagickImageDecoder());
        var model = new ReaderWorkspaceViewModel(operation, new CommandTable(operation), state); var main = new MainWindow(); main.Bind(model, images, new NoPlatform()); main.Show();
        await main.OpenAsync(fixture.Images); model.Layout.OpenWindow("NavigatePanel"); main.UpdateLayout();
        var floating = Assert.IsType<FloatingPanelWindow>(Assert.Single(main.OwnedWindows)); floating.UpdateLayout();
        var content = floating.ContentHost.Child; var navigator = main.FindControl<ThumbnailView>("Navigator")!; await navigator.RefreshAsync(); Assert.True(navigator.DisplayCount > 0);
        var file = Path.Combine(fixture.State, "UserSetting.json"); if (File.Exists(file)) File.Delete(file); Directory.CreateDirectory(file);
        try { await Assert.ThrowsAnyAsync<IOException>(() => main.PrepareShutdownAsync()); Assert.True(floating.IsVisible); Assert.True(floating.IsEnabled); Assert.Same(content, floating.ContentHost.Child); Assert.True(navigator.DisplayCount > 0); }
        finally { Directory.Delete(file); }
        floating.Position = new(250, 150); floating.UpdateLayout(); SaveImage(floating);
        await main.PrepareShutdownAsync(); main.Close(); Assert.False(floating.IsVisible); Assert.Equal(0, navigator.DisplayCount);
        var restartState = new SaveData(fixture.State); await restartState.LoadAsync(TestContext.Current.CancellationToken);
        var restartOperation = fixture.Operation(restartState); var restartImages = new BitmapFactory(new NeeView.Backends.MagickImageDecoder());
        var restartModel = new ReaderWorkspaceViewModel(restartOperation, new CommandTable(restartOperation), restartState);
        var restart = new MainWindow(); restart.Bind(restartModel, restartImages, new NoPlatform()); Assert.Empty(restart.OwnedWindows);
        try
        {
            restart.Show(); restart.UpdateLayout(); var restored = Assert.IsType<FloatingPanelWindow>(Assert.Single(restart.OwnedWindows));
            Assert.Equal("NavigatePanel", restored.PanelKey); Assert.Equal(250, restored.Position.X); Assert.Equal("FileInformationPanel", restartModel.Layout.Docks["Right"].SelectedItem![0].Key);
        }
        finally { await restart.PrepareShutdownAsync(); restart.Close(); }
    }
    /// <summary>固定异地屏幕位置恢复后仍在当前工作区；不以Headless替代Retina多显示器验收。</summary>
    [AvaloniaFact]
    public void DisconnectedDisplayPlacementIsReachable()
    {
        var owner = new Window(); owner.Show(); var floating = new FloatingPanelWindow("PageListPanel", "页面列表");
        try { floating.RestorePlacement(new(WindowStateEx.Normal, int.MaxValue, int.MinValue, int.MaxValue, int.MaxValue), owner); floating.Show(owner); Assert.True(floating.Width > 0); Assert.True(floating.Height > 0); var screen = floating.Screens.ScreenFromWindow(floating); if (screen is not null) Assert.True(screen.WorkingArea.Contains(floating.Position)); }
        finally { floating.Close(); owner.Close(); }
    }
    private static void SaveImage(FloatingPanelWindow floating)
    {
        var phase = Environment.GetEnvironmentVariable("NEEVIEW_ACCEPTANCE_PHASE") ?? "p2-floating";
        using var image = new RenderTargetBitmap(new PixelSize(320, 480)); image.Render(floating);
        image.Save(Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, $"../../../../../acceptance/{phase}-floating-layout.png")), PngBitmapEncoderOptions.Default);
    }
    private static void SendKey(Window window, Key key, PhysicalKey physical, RawInputModifiers modifiers = RawInputModifiers.None)
    { window.KeyPress(key, modifiers, physical, null); if (window.IsVisible) window.KeyRelease(key, modifiers, physical, null); }
    private sealed class NoPlatform : IPlatformService
    {
        public Task RevealAsync(string path, CancellationToken token = default) => throw new NotSupportedException();
        public Task TrashAsync(string path, CancellationToken token = default) => throw new NotSupportedException();
    }
}
