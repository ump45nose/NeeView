using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Themes.Fluent;
using Avalonia.VisualTree;
using NeeView.Application;
using NeeView.Content;
using NeeView.Core;
using NeeView.Desktop;
using NeeView.Persistence;
using Xunit;

[assembly: AvaloniaTestApplication(typeof(NeeView.Portable.Tests.TestAvaloniaBuilder))]

namespace NeeView.Portable.Tests;

public sealed class TestApplication : Avalonia.Application
{
    /// <summary>装载与正式界面相同的样式，Headless 交互覆盖真实 XAML 和主题入口。</summary>
    public override void Initialize()
    {
        Styles.Add(new FluentTheme());
        Styles.Add(new Avalonia.Markup.Xaml.Styling.StyleInclude(new Uri("avares://NeeView.Desktop/"))
        { Source = new Uri("avares://NeeView.Desktop/Styles/ReaderTheme.axaml") });
    }
}
public static class TestAvaloniaBuilder
{
    /// <summary>为界面测试初始化独立 Headless 平台。</summary>
    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<TestApplication>().UseHeadless(new AvaloniaHeadlessPlatformOptions());
}
public sealed class UiTests
{
    /// <summary>短瀑布流启动时尺寸探测改变滚动范围，宿主不能把范围修正保存为用户的新位置。</summary>
    [AvaloniaFact]
    public async Task StartupMasonryExtentChangesKeepSavedAnchor()
    {
        await using var workspace = new TestWorkspace();
        var directory = Path.Combine(workspace.Root, "short-book"); Directory.CreateDirectory(directory);
        for (var number = 1; number <= 3; number++)
            await File.WriteAllBytesAsync(Path.Combine(directory, $"{number:D3}.jpg"), [(byte)number], TestContext.Current.CancellationToken);
        ReadingAnchor anchor;
        await using (var saved = workspace.Session())
        {
            await saved.OpenAsync(new(directory)); anchor = new(saved.Snapshot.Index!.Pages[2].Id);
            await saved.SetOptionsAsync(saved.Snapshot.Options with { Mode = ReaderMode.Masonry }); await saved.LocateAsync(anchor);
        }
        var session = workspace.Session(); var decoder = new MixedProbeDecoder();
        await using var scheduler = new ImageScheduler(decoder);
        var window = new MainWindow(session, workspace.Settings, workspace.States, new CountingFiles(), new DestinationFolderService(workspace.Settings),
            new FolderNavigator(), new TestPlatform(), new LegacyImporter(workspace.States, workspace.Settings), scheduler, decoder);
        var scroll = ((ReaderShell)window.Content!).FindControl<ScrollViewer>("ViewerScroll")!;
        var extentChanges = 0; scroll.ScrollChanged += (_, e) => { if (e.ExtentDelta != default) extentChanges++; };
        try
        {
            window.Show(); await window.OpenAsync(directory);
            // 驱动真实测量及异步尺寸回报，覆盖启动窗口、默认占位和已知尺寸之间的多次重排。
            for (var step = 0; step < 20; step++) { window.UpdateLayout(); await Task.Delay(20, TestContext.Current.CancellationToken); }
            await session.FlushAsync(); Assert.True(extentChanges > 0);
            Assert.Equal(ReaderMode.Masonry, session.Snapshot.Options.Mode); Assert.Equal(anchor, session.Snapshot.Anchor);
            Assert.All(session.Snapshot.Index!.Pages, page => Assert.NotNull(page.Size));
            // 重放 macOS 的晚到范围修正：查看器记录的目标与宿主实际 offset 不同，事件同时携带 extent delta。
            var viewer = (ReaderView)scroll.Content!;
            viewer.SetViewport(scroll.Viewport.Width, scroll.Viewport.Height, scroll.Offset.Y + 25, false);
            scroll.RaiseEvent(new ScrollChangedEventArgs(new Vector(0, -25), new Vector(0, -25), default));
            await session.FlushAsync(); Assert.Equal(anchor, session.Snapshot.Anchor);
        }
        finally { await window.PrepareShutdownAsync(); window.Close(); }
    }
    /// <summary>分类列表多次重绑定时，Avalonia 的空数据模板清理不能中断刷新或留下重复条目。</summary>
    [AvaloniaFact]
    public async Task DestinationRefreshClearsTemplatesAndKeepsSingleChild()
    {
        await using var workspace = new TestWorkspace();
        await using var session = workspace.Session();
        var model = new ReaderWorkspaceViewModel(session, workspace.Settings, workspace.States, new CountingFiles(),
            new DestinationFolderService(workspace.Settings), new TestPlatform(), new FolderNavigator());
        var panel = new ReaderDestinationPanel(model, _ => Task.CompletedTask);
        var window = new Window { Content = panel, Width = 500, Height = 600 }; window.Show();
        try
        {
            // 真机失败发生在刷新已有子目录后添加目标；重复绑定复现容器销毁与重建。
            var child = Path.Combine(workspace.Root, "收纳");
            panel.SetData(new([], [child])); window.UpdateLayout();
            var lists = panel.GetVisualDescendants().OfType<ListBox>().ToArray();
            foreach (var list in lists) Assert.NotNull(list.ItemTemplate!.Build(null));
            for (var refresh = 0; refresh < 3; refresh++)
            {
                panel.SetData(new([workspace.Root], [child])); window.UpdateLayout();
                Assert.Single(lists[1].Items);
                Assert.Single(lists[1].GetVisualDescendants().OfType<ListBoxItem>());
            }
        }
        finally { window.Close(); }
    }
    /// <summary>地址框仍隔离数字分类，但 Command+W 必须保留系统关闭语义。</summary>
    [AvaloniaFact]
    public void TextEditingKeepsSystemCloseShortcut()
    {
        var commands = new List<string>();
        var router = new ReaderInputRouter(() => new AppSettings(), () => new(0, null, null, null, new(), false, null),
            (command, _) => { commands.Add(command); return Task.CompletedTask; }, _ => { });
        var address = new TextBox(); var owner = new Window { Content = address }; owner.Show(); address.Focus();
        try
        {
            var close = new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Source = address, Key = Key.W, KeyModifiers = KeyModifiers.Meta };
            router.KeyDown(owner, close); Assert.True(close.Handled); Assert.Equal(["Close"], commands);
            var digit = new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Source = address, Key = Key.D1 };
            router.KeyDown(owner, digit); Assert.False(digit.Handled); Assert.Single(commands);
        }
        finally { owner.Close(); }
    }
    /// <summary>程序恢复跨列锚点不能被当作用户滚动；真实滚动仍更新锚点并保留分类选择。</summary>
    [AvaloniaFact]
    public async Task ProgrammaticMasonryScrollKeepsAnchorAndUserScrollStillUpdatesIt()
    {
        await using var workspace = new TestWorkspace();
        var directory = Path.Combine(workspace.Root, "mixed"); Directory.CreateDirectory(directory);
        for (var number = 1; number <= 12; number++)
            await File.WriteAllBytesAsync(Path.Combine(directory, $"{number:D3}.jpg"), [1], TestContext.Current.CancellationToken);
        await using var session = workspace.Session();
        await session.OpenAsync(new(directory), TestContext.Current.CancellationToken);
        var pages = session.Snapshot.Index!.Pages.ToArray();
        for (var index = 0; index < pages.Length; index++)
            await session.ReportSizeAsync(pages[index].Id, index % 3 == 2 ? new(3840, 2160) : new(2160, 3840), session.Snapshot.Generation);
        await session.SetOptionsAsync(session.Snapshot.Options with { Mode = ReaderMode.Masonry });
        var anchor = new ReadingAnchor(pages[3].Id, 0, 0.25);
        await session.LocateAsync(anchor, true);
        await using var scheduler = new ImageScheduler(new FakeDecoder());
        await using var viewer = new ReaderView(session, scheduler, new FakeDecoder());
        var restored = new TaskCompletionSource<double>(TaskCreationOptions.RunContinuationsAsynchronously);
        // 模拟宿主设置 ScrollViewer.Offset 后回报视口的真实事件链，不直接操作业务锚点。
        viewer.ScrollRequested += top => { viewer.SetViewport(770, 600, top); restored.TrySetResult(top); };
        viewer.SetViewport(770, 600, 0, false);
        var offset = await restored.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        await session.FlushAsync();
        Assert.Equal(anchor, session.Snapshot.Anchor);

        var userOffset = offset + 1000;
        var visible = viewer.Layout.Visible(userOffset, userOffset + 600).OrderBy(item => item.Bounds.Y)
            .First(item => item.Bounds.Bottom > userOffset);
        viewer.SetViewport(770, 600, userOffset); await session.FlushAsync();
        Assert.Equal(visible.Page.Id, session.Snapshot.Anchor!.Content);
        Assert.NotEqual(anchor.Content, session.Snapshot.Anchor.Content);
        Assert.Equal(anchor.Content, session.Snapshot.Selection);
    }
    /// <summary>启动恢复读取晚到时，Finder/显式打开的新书不能被旧 LastSource 覆盖。</summary>
    [AvaloniaFact]
    public async Task ExplicitOpenWinsAgainstLateStartupRestore()
    {
        await using var workspace = new TestWorkspace(); var old = Path.Combine(workspace.Root, "old"); var current = Path.Combine(workspace.Root, "current");
        Directory.CreateDirectory(old); Directory.CreateDirectory(current);
        await File.WriteAllBytesAsync(Path.Combine(old, "old.jpg"), [1], TestContext.Current.CancellationToken);
        await File.WriteAllBytesAsync(Path.Combine(current, "current.jpg"), [1], TestContext.Current.CancellationToken);
        await workspace.Settings.SaveAsync(new() { LastSource = old }, TestContext.Current.CancellationToken);
        var delayed = new DelayedSettings(workspace.Settings); var session = workspace.Session(); await using var scheduler = new ImageScheduler(new FakeDecoder());
        var window = new MainWindow(session, delayed, workspace.States, new CountingFiles(), new DestinationFolderService(workspace.Settings),
            new FolderNavigator(), new TestPlatform(), new LegacyImporter(workspace.States, workspace.Settings), scheduler, new FakeDecoder());
        var restore = window.RestoreLastAsync();
        try
        {
            await delayed.Started.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
            await window.OpenAsync(current); delayed.Release.TrySetResult(); await restore;
            Assert.Equal(current, session.Snapshot.Index!.Locator.Path);
        }
        finally { delayed.Release.TrySetResult(); await restore; await window.PrepareShutdownAsync(); }
    }
    /// <summary>验证数字键在文本框中输入，在查看器中才执行分类。</summary>
    [AvaloniaFact]
    public async Task TextEditingDoesNotTriggerNumericClassification()
    {
        await using var workspace = new TestWorkspace();
        var directory = Path.Combine(workspace.Root, "images"); Directory.CreateDirectory(directory);
        await File.WriteAllBytesAsync(Path.Combine(directory, "1.jpg"), [1], TestContext.Current.CancellationToken);
        await workspace.Settings.SaveAsync(new() { DestinationFolders = [workspace.Root] }, TestContext.Current.CancellationToken);
        var session = workspace.Session(); await using var scheduler = new ImageScheduler(new FakeDecoder());
        var files = new CountingFiles();
        var window = new MainWindow(session, workspace.Settings, workspace.States, files, new DestinationFolderService(workspace.Settings),
            new FolderNavigator(), new TestPlatform(), new LegacyImporter(workspace.States, workspace.Settings), scheduler, new FakeDecoder());
        window.Show(); await session.OpenAsync(new(directory), TestContext.Current.CancellationToken);
        await Task.Delay(50, TestContext.Current.CancellationToken);
        var address = window.GetVisualDescendants().OfType<TextBox>().First(); address.Focus();
        window.KeyPress(Key.D1, RawInputModifiers.None, PhysicalKey.Digit1, "1"); window.KeyTextInput("1"); window.KeyRelease(Key.D1, RawInputModifiers.None, PhysicalKey.Digit1, "1");
        Assert.Equal(0, files.Calls);
        var viewer = window.GetVisualDescendants().OfType<ReaderView>().Single(); viewer.Focus();
        window.KeyPress(Key.D1, RawInputModifiers.None, PhysicalKey.Digit1, "1"); window.KeyRelease(Key.D1, RawInputModifiers.None, PhysicalKey.Digit1, "1");
        await Task.Delay(50, TestContext.Current.CancellationToken); Assert.Equal(1, files.Calls);
        await window.FlushAsync(); window.Close(); await Task.Delay(50, TestContext.Current.CancellationToken);
    }
    /// <summary>验证瀑布流滚动锚点与显式分类选择分离。</summary>
    [Fact]
    public void MasonryRequiresExplicitSelectionForFileTarget()
    {
        var page = FakeSource.SamplePage();
        var snapshot = new ReaderSnapshot(1, new(new("book"), new("/tmp"), [page], null, new(true, AccessCost.Random, false)),
            new(page.Id), null, new() { Mode = ReaderMode.Masonry }, false, null);
        Assert.Null(snapshot.ActionTarget); Assert.Equal(page, (snapshot with { Selection = page.Id }).ActionTarget);
    }
    private sealed class CountingFiles : IFileActionService
    {
        public int Calls; public bool CanUndo => false; public bool CanRedo => false; public int Capacity { get; set; }
        public Task<FileActionResult> ExecuteAsync(PageDescriptor page, FileActionKind action, string? target, ConflictChoice conflict = ConflictChoice.Cancel, CancellationToken token = default)
        { Calls++; return Task.FromResult(new FileActionResult(true, page.Locator.Path, target)); }
        public Task<FileActionResult> UndoAsync(ConflictChoice conflict = ConflictChoice.Cancel, CancellationToken token = default) => Task.FromResult(new FileActionResult(false, "", null));
        public Task<FileActionResult> RedoAsync(ConflictChoice conflict = ConflictChoice.Cancel, CancellationToken token = default) => UndoAsync(conflict, token);
        public Task<IReadOnlyList<FileRecoveryResult>> RecoverAsync(CancellationToken token = default) => Task.FromResult<IReadOnlyList<FileRecoveryResult>>([]);
        public Task DrainAsync() => Task.CompletedTask;
    }
    /// <summary>以三张测试流首字节提供混合横竖尺寸；像素输出沿用受限假解码器。</summary>
    private sealed class MixedProbeDecoder : IImageDecoder
    {
        private readonly FakeDecoder _pixels = new();
        /// <summary>前两图竖向，第三图横向，触发未知尺寸补齐后的滚动范围变化。</summary>
        public Task<ImageInfo> ProbeAsync(Stream stream, CancellationToken token)
        { token.ThrowIfCancellationRequested(); return Task.FromResult(new ImageInfo(stream.ReadByte() == 3 ? new(3840, 2160) : new(2160, 3840), "test")); }
        /// <summary>仅返回少量测试像素，回归不依赖真实原生内存或图片文件。</summary>
        public Task<DecodedImageLease> DecodeAsync(Stream stream, DecodeRequest request, CancellationToken token) => _pixels.DecodeAsync(stream, request, token);
    }
    /// <summary>模拟读取旧配置已完成但 UI 恢复回调尚未返回的存储。</summary>
    private sealed class DelayedSettings(ISettingsStore inner) : ISettingsStore
    {
        public TaskCompletionSource Started = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        /// <summary>捕获旧值后延迟返回，用于重放启动与 Finder 打开竞争。</summary>
        public async Task<AppSettings> LoadAsync(CancellationToken token = default)
        { var snapshot = await inner.LoadAsync(token); Started.TrySetResult(); await Release.Task.WaitAsync(token); return snapshot; }
        /// <summary>保存仍使用真实临时设置文件。</summary>
        public Task SaveAsync(AppSettings settings, CancellationToken token = default) => inner.SaveAsync(settings, token);
        /// <summary>原子更新不参与人工延迟，保持真实会话保存行为。</summary>
        public Task<AppSettings> UpdateAsync(Func<AppSettings, AppSettings> update, CancellationToken token = default) => inner.UpdateAsync(update, token);
    }
}
