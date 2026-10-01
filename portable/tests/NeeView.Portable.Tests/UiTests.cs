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
