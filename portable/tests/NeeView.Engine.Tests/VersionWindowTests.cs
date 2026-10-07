using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Threading;
using NeeView.Backends;
using NeeView.MacOS.ViewModels;
using NeeView.MacOS.Views;
namespace NeeView.Engine.Tests;

/// <summary>原版本窗口元数据、系统动作、关闭晚到及正式菜单生命周期的隔离回归。</summary>
public sealed class VersionWindowTests
{
    private sealed class Platform : IPlatformService
    {
        public List<Uri> Links { get; } = [];
        public bool Fail;
        public Func<CancellationToken, Task>? Pending;
        public async Task OpenUriAsync(Uri uri, CancellationToken token = default)
        { token.ThrowIfCancellationRequested(); Links.Add(uri); if (Pending is { } work) await work(token); if (Fail) throw new IOException("isolated link rejection"); }
        public Task RevealAsync(string path, CancellationToken token = default) => throw new NotSupportedException();
        public Task TrashAsync(string path, CancellationToken token = default) => throw new NotSupportedException();
    }
    [Fact]
    public async Task ActualEngineVersionAndCompleteBuildNoteAreCopied()
    {
        string? copied = null; var platform = new Platform(); using var model = new VersionWindowViewModel(platform, (text, _) => { copied = text; return Task.CompletedTask; });
        Assert.Equal(typeof(BookOperation).Assembly.GetName().Version!.ToString(3), model.DisplayVersion);
        await model.CopyVersionAsync(); Assert.Equal(model.VersionNote, copied); Assert.Contains("c5c398d89", copied); Assert.Contains(".NET", copied);
        Assert.Empty(platform.Links); Assert.False(model.IsCheckerEnabled); Assert.Contains("未配置", model.UpdateStatus);
    }
    [Fact]
    public async Task ExactLicenseAndProjectLinksUseOnePlatformBoundary()
    {
        var platform = new Platform(); var license = new Uri("file:///tmp/NeeView-test-license.md");
        using var model = new VersionWindowViewModel(platform, (_, _) => throw new IOException("not a copy action"), license);
        await model.OpenLicenseAsync(); await model.OpenProjectAsync(); await model.OpenOriginalProjectAsync();
        Assert.Equal([license, model.ProjectUri, model.OriginalProjectUri], platform.Links); Assert.Equal("https://github.com/ump45nose/NeeView", model.ProjectUri.AbsoluteUri);
    }
    [Fact]
    public async Task LinkAndClipboardFailuresKeepWindowUsableForRetry()
    {
        var platform = new Platform { Fail = true }; bool copyFail = true;
        using var model = new VersionWindowViewModel(platform, (_, _) => copyFail ? Task.FromException(new IOException("isolated clipboard rejection")) : Task.CompletedTask);
        await model.OpenProjectAsync(); Assert.Contains("rejection", model.Message); Assert.True(model.CanAct);
        platform.Fail = false; await model.OpenProjectAsync(); Assert.Empty(model.Message);
        await model.CopyVersionAsync(); Assert.Contains("clipboard", model.Message); copyFail = false; await model.CopyVersionAsync(); Assert.Empty(model.Message);
    }
    [Fact]
    public async Task CloseCancelsQueuedActionAndRejectsLatePublicationOrNewActions()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously); var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        CancellationToken captured = default; var platform = new Platform { Pending = async token => { captured = token; entered.TrySetResult(); await done.Task; throw new IOException("late native result"); } };
        var model = new VersionWindowViewModel(platform, (_, _) => Task.CompletedTask);
        var work = model.OpenProjectAsync(); await entered.Task; Assert.True(model.IsBusy); await model.OpenLicenseAsync(); Assert.Single(platform.Links);
        model.Dispose(); Assert.True(captured.IsCancellationRequested); done.SetResult(); await work;
        Assert.Empty(model.Message); Assert.False(model.CanAct); await model.OpenLicenseAsync(); Assert.Single(platform.Links);
    }
    [AvaloniaFact]
    public async Task FormalWindowPreservesOriginalRegionsAndCopyEscapeKeys()
    {
        var platform = new Platform(); string? copy = null;
        var model = new VersionWindowViewModel(platform, (text, _) => { copy = text; return Task.CompletedTask; });
        var window = new VersionWindow { DataContext = model }; window.Show();
        try
        {
            Pump(window); Assert.Equal(512, window.Width); Assert.False(window.CanResize); Assert.Equal(WindowStartupLocation.CenterOwner, window.WindowStartupLocation);
            Assert.Equal(model.DisplayVersion, window.FindControl<TextBlock>("DisplayVersion")!.Text);
            Assert.True(window.FindControl<Button>("LicenseLink")!.Bounds.Y >= 0);
            window.FindControl<Button>("CopyVersionButton")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Assert.Equal(model.VersionNote, copy);
            copy = null; window.KeyPress(Key.C, RawInputModifiers.Meta, PhysicalKey.C, null); window.KeyRelease(Key.C, RawInputModifiers.Meta, PhysicalKey.C, null); Assert.Equal(model.VersionNote, copy);
            window.FindControl<Button>("ProjectLink")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Assert.Equal(model.ProjectUri, Assert.Single(platform.Links));
            if (Environment.GetEnvironmentVariable("NEEVIEW_P5_VERSION_SCREENSHOT") is { } path)
            { using var frame = window.CaptureRenderedFrame(); frame!.Save(path, Avalonia.Media.Imaging.PngBitmapEncoderOptions.Default); }
            window.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null); Assert.False(window.IsVisible); Assert.False(model.CanAct);
        }
        finally { window.Close(); model.Dispose(); }
    }
    [AvaloniaFact]
    public async Task OriginalCommandUsesOwnerAndClosingDoesNotAlterReading()
    {
        var root = Path.Combine(Path.GetTempPath(), "NeeView-P5-Version-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
        var state = new SaveData(root); await state.LoadAsync(TestContext.Current.CancellationToken);
        var operation = new BookOperation(new ArchiveFactory(), new MagickImageDecoder(), state); using var images = new BitmapFactory(new MagickImageDecoder());
        var window = new MainWindow(); window.Bind(new(operation, new(operation), state), images, new Platform()); window.Show();
        try
        {
            Assert.True(window.IsCommandAvailable("OpenVersionWindow")); var open = window.ExecuteAsync("OpenVersionWindow"); Pump(window);
            var dialog = Assert.Single(window.OwnedWindows.OfType<VersionWindow>()); Assert.Same(window, dialog.Owner);
            var model = Assert.IsType<VersionWindowViewModel>(dialog.DataContext);
            await model.CopyVersionAsync(); Assert.Equal(model.VersionNote, await dialog.Clipboard!.TryGetTextAsync()); Assert.Empty(model.Message);
            await window.ExecuteAsync("OpenVersionWindow"); Assert.Single(window.OwnedWindows.OfType<VersionWindow>()); Assert.Null(operation.Book);
            await window.PrepareShutdownAsync(); await open; Assert.False(dialog.IsVisible); Assert.Equal(0, images.ByteCount);
        }
        finally { foreach (var owned in window.OwnedWindows.ToArray()) owned.Close(); await window.PrepareShutdownAsync(); window.Close(); Directory.Delete(root, true); }
    }
    private static void Pump(Window window) { Dispatcher.UIThread.RunJobs(); window.UpdateLayout(); Avalonia.Headless.AvaloniaHeadlessPlatform.ForceRenderTimerTick(); }
}
