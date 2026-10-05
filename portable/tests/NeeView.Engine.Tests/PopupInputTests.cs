using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using NeeView.MacOS.ViewModels;
using NeeView.MacOS.Views;
namespace NeeView.Engine.Tests;

/// <summary>真实下拉框打开但主窗逻辑焦点仍在查看器，复现独立popup的全局命令漏隔离。</summary>
public sealed class PopupInputTests
{
    [AvaloniaTheory]
    [InlineData(false)] [InlineData(true)]
    public async Task DropdownNavigationDoesNotReachGlobalReaderCommands(bool autoHide)
    {
        using var fixture = new Fixture(); var state = new SaveData(fixture.State); await state.LoadAsync(TestContext.Current.CancellationToken);
        var parent = Directory.CreateDirectory(Path.Combine(fixture.Root, "input-books")).FullName;
        var a = Directory.CreateDirectory(Path.Combine(parent, "A")).FullName;
        var b = Directory.CreateDirectory(Path.Combine(parent, "B")).FullName;
        foreach (var directory in new[] { a, b })
            foreach (var file in Directory.GetFiles(fixture.Images)) File.Copy(file, Path.Combine(directory, Path.GetFileName(file)));
        state.SetShortcut("PrevBook", "Up"); state.SetShortcut("NextBook", "Down"); state.SetShortcut("NextPage", "Right"); state.SetShortcut("PrevPage", "Left");
        var operation = fixture.Operation(state); var model = new ReaderWorkspaceViewModel(operation, new CommandTable(operation), state);
        Config.Current.Window.IsAutoHideInNormal = autoHide;
        var main = new MainWindow(); main.Bind(model, new BitmapFactory(new NeeView.Backends.MagickImageDecoder()), new NoPlatform()); main.Show();
        try
        {
            await main.OpenAsync(b); await operation.JumpAsync(2);
            if (autoHide) await main.ExecuteAsync("ShowHiddenPanels");
            Pump(main);
            var combo = main.FindControl<ComboBox>("BrowseModeView")!;
            // 先显示菜单并完成宿主布局，再打开popup；不对隐藏的placement target强制Arrange。
            main.Viewer.Focus(); Pump(main);
            Assert.True(combo.IsEffectivelyVisible);
            combo.IsDropDownOpen = true; Dispatcher.UIThread.RunJobs(); main.Viewer.Focus();
            foreach (var (key, physical) in new[] { (Key.Up, PhysicalKey.ArrowUp), (Key.Down, PhysicalKey.ArrowDown), (Key.Left, PhysicalKey.ArrowLeft), (Key.Right, PhysicalKey.ArrowRight), (Key.Enter, PhysicalKey.Enter), (Key.Space, PhysicalKey.Space), (Key.Home, PhysicalKey.Home), (Key.End, PhysicalKey.End), (Key.PageUp, PhysicalKey.PageUp), (Key.PageDown, PhysicalKey.PageDown), (Key.Escape, PhysicalKey.Escape) })
            {
                Assert.True(combo.IsDropDownOpen); Assert.Same(main.Viewer, main.FocusManager!.GetFocusedElement());
                SendKey(main, key, physical); await Settle(main);
                Assert.Equal(b, operation.Book!.Path); Assert.Equal(2, operation.Position.Index);
            }
            combo.IsDropDownOpen = false; Pump(main);
            // 无popup时原Up/Down切书语义必须仍能执行。
            main.Viewer.Focus(); SendKey(main, Key.Up, PhysicalKey.ArrowUp);
            for (int i = 0; i < 100 && operation.Book!.Path != a; i++) await Settle(main);
            Assert.Equal(a, operation.Book!.Path);
            SendKey(main, Key.Down, PhysicalKey.ArrowDown);
            for (int i = 0; i < 100 && operation.Book!.Path != b; i++) await Settle(main);
            Assert.Equal(b, operation.Book!.Path);
        }
        finally { await main.PrepareShutdownAsync(); main.Close(); }
    }
    /// <summary>主窗/浮窗的实际上下文popup也隔离主窗残留焦点；Command+W仍只关闭浮窗。</summary>
    [AvaloniaTheory]
    [InlineData(false)] [InlineData(true)]
    public async Task ContextPopupKeepsNavigationLocal(bool floatingOwner)
    {
        using var fixture = new Fixture(); var state = new SaveData(fixture.State); await state.LoadAsync(TestContext.Current.CancellationToken);
        state.SetShortcut("NextBook", ""); state.SetShortcut("PrevBook", "");
        state.SetShortcut("NextPage", "Down"); state.SetShortcut("PrevPage", "Up");
        var operation = fixture.Operation(state); var model = new ReaderWorkspaceViewModel(operation, new CommandTable(operation), state);
        var main = new MainWindow(); main.Bind(model, new BitmapFactory(new NeeView.Backends.MagickImageDecoder()), new NoPlatform()); main.Show();
        try
        {
            await main.OpenAsync(fixture.Images); await operation.JumpAsync(1); Pump(main);
            FloatingPanelWindow? floating = null;
            if (floatingOwner) { model.Layout.OpenWindow("HistoryPanel"); floating = Assert.IsType<FloatingPanelWindow>(Assert.Single(main.OwnedWindows)); Pump(floating); }
            Control target = floating is null ? main.Viewer : floating.PanelHeader;
            var originalMenu = target.ContextMenu;
            var menu = new ContextMenu { ItemsSource = new[] { new MenuItem { Header = "First" }, new MenuItem { Header = "Second" } } };
            target.ContextMenu = menu;
            try
            {
                menu.Open(target); Dispatcher.UIThread.RunJobs(); main.Viewer.Focus();
                Assert.True(menu.IsOpen); Assert.NotEmpty((floating as Window ?? main).OpenedPopups);
                SendKey(main, Key.Down, PhysicalKey.ArrowDown); await Settle(main);
                SendKey(main, Key.Up, PhysicalKey.ArrowUp); await Settle(main);
                Assert.Equal(1, operation.Position.Index);
                menu.Close(); Pump(main); main.Viewer.Focus();
                SendKey(main, Key.Down, PhysicalKey.ArrowDown); await Settle(main);
                Assert.Equal(2, operation.Position.Index);
                if (floating is not null)
                {
                    menu.Open(target); Dispatcher.UIThread.RunJobs();
                    floating.PanelHeader.Focusable = true; floating.PanelHeader.Focus();
                    Assert.True(menu.IsOpen); Assert.Same(floating.PanelHeader, floating.FocusManager!.GetFocusedElement());
                    // KeyDown立即销毁浮窗；释放不能再发往已Dispose的Headless宿主。
                    floating.KeyPress(Key.W, RawInputModifiers.Meta, PhysicalKey.W, null);
                    main.KeyRelease(Key.W, RawInputModifiers.Meta, PhysicalKey.W, null);
                    Assert.Empty(main.OwnedWindows); Assert.True(main.IsVisible); Assert.NotNull(operation.Book);
                }
            }
            finally { menu.Close(); target.ContextMenu = originalMenu; }
        }
        finally { await main.PrepareShutdownAsync(); main.Close(); }
    }
    /// <summary>从真实窗口输入入口发送完整按下/释放，不直接调用作用域辅助方法。</summary>
    private static void SendKey(Window window, Key key, PhysicalKey physical)
    { window.KeyPress(key, RawInputModifiers.None, physical, null); window.KeyRelease(key, RawInputModifiers.None, physical, null); }
    /// <summary>完成当前正式宿主的绑定与布局。</summary>
    private static void Pump(Window window) { Dispatcher.UIThread.RunJobs(); window.UpdateLayout(); }
    /// <summary>允许异步命令提交后再核对原阅读状态。</summary>
    private static async Task Settle(Window window) { await Task.Delay(50, TestContext.Current.CancellationToken); Pump(window); }
    private sealed class NoPlatform : IPlatformService
    {
        public Task RevealAsync(string path, CancellationToken token = default) => throw new NotSupportedException();
        public Task TrashAsync(string path, CancellationToken token = default) => throw new NotSupportedException();
    }
}
