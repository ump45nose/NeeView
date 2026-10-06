using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using NeeView;
using NeeView.Backends;
using NeeView.MacOS.ViewModels;
using NeeView.MacOS.Views;
namespace NeeView.Engine.Tests;

/// <summary>当前主页删除的真实临时文件协调；模拟废纸篓不代表AppKit设备验收。</summary>
public sealed class DeleteFileTests
{
    private sealed class FixtureTrash(string root) : IPlatformService
    {
        public List<string> Calls { get; } = [];
        public Exception? Failure { get; init; }
        public Func<Task>? Before { get; init; }
        public Action? After { get; init; }
        public Task RevealAsync(string path, CancellationToken token = default) => Task.CompletedTask;
        public async Task TrashAsync(string path, CancellationToken token = default)
        {
            token.ThrowIfCancellationRequested(); Calls.Add(path);
            if (Before is not null) await Before();
            if (Failure is not null) throw Failure;
            if (new FileInfo(path).LinkTarget is not null) throw new NotSupportedException("模拟后端拒绝链接");
            Directory.CreateDirectory(root); File.Move(path, Path.Combine(root, Path.GetFileName(path)), false);
            After?.Invoke(); // 模拟原生提交后才收到取消，必须继续协调真实结果。
        }
    }
    private static FixtureTrash Attach(Fixture f, BookOperation op, BitmapFactory images, FixtureTrash? platform = null)
    {
        platform ??= new(Path.Combine(f.Root, "模拟废纸篓")); op.AttachFileDeletion(platform, images);
        var backend = new FileOperationBackend(Path.Combine(f.State, "FileRecovery")); op.AttachFileOperations(new(backend), backend, images);
        Config.Current.System.IsFileWriteAccessEnabled = true; op.ConfirmDeleteAsync = _ => Task.FromResult(true);
        return platform;
    }
    private sealed class LateDecoder : IImageDecoder
    {
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public DecodedImageLease? Result { get; private set; }
        public Task<ImageInfo> ProbeAsync(Stream stream, CancellationToken token) => throw new NotSupportedException();
        public async Task<DecodedImageLease> DecodeAsync(Stream stream, DecodeRequest request, CancellationToken token)
        { Started.SetResult(); await Release.Task; return Result = new(new(8, 8), new byte[256]); }
    }
    [Theory]
    [InlineData(PageReadOrder.RightToLeft)]
    [InlineData(PageReadOrder.LeftToRight)]
    public async Task GlobalDeleteKeepsOriginalSingleMainPageScopeAndDoesNotEnterMoveHistory(PageReadOrder order)
    {
        using var f = new Fixture(); var state = new SaveData(f.State); await state.LoadAsync(TestContext.Current.CancellationToken);
        var setting = Config.Current.BookSettingDefault; setting.PageMode = PageMode.WidePage; setting.BookReadOrder = order;
        setting.IsSupportedSingleFirstPage = false; setting.IsSupportedSingleLastPage = false;
        await using var op = f.Operation(state); using var images = new BitmapFactory(new MagickImageDecoder()); var trash = Attach(f, op, images);
        await op.OpenAsync(f.Images, TestContext.Current.CancellationToken); Assert.Equal(2, op.Book!.CurrentPages.Count); var book = op.Book;
        await new CommandTable(op).ExecuteAsync("DeleteFile");
        Assert.Same(book, op.Book); Assert.Equal("001.png", Path.GetFileName(Assert.Single(trash.Calls)));
        Assert.Equal("002.png", book.CurrentPage!.EntryName); Assert.Equal(4, book.Pages.SourcePages.Count);
        Assert.True(File.Exists(Path.Combine(f.Images, "002.png"))); Assert.False(File.Exists(Path.Combine(f.Images, "001.png")));
        Assert.Equal(0, op.DestinationMoves!.UndoCount); Assert.Equal(0, op.DestinationMoves.RedoCount);
        Assert.All(book.CurrentPages, page => Assert.Contains(page, book.Pages));
        Assert.Equal("002.png", state.Find(f.Images)!.Page);
    }
    [Theory]
    [InlineData("write-disabled")]
    [InlineData("no-confirm-host")]
    [InlineData("declined")]
    [InlineData("cancelled")]
    public async Task UnauthorizedOrCancelledDeletionKeepsPageAndOriginalFile(string reason)
    {
        using var f = new Fixture(); var state = new SaveData(f.State); await state.LoadAsync(TestContext.Current.CancellationToken);
        Assert.True(Config.Current.System.IsRemoveConfirmed); Assert.False(Config.Current.System.IsFileWriteAccessEnabled);
        await using var op = f.Operation(state); using var images = new BitmapFactory(new MagickImageDecoder()); var trash = Attach(f, op, images);
        await op.OpenAsync(f.Images, TestContext.Current.CancellationToken); var page = op.Book!.CurrentPage;
        if (reason == "write-disabled") Config.Current.System.IsFileWriteAccessEnabled = false;
        if (reason == "no-confirm-host") op.ConfirmDeleteAsync = null;
        if (reason == "declined") op.ConfirmDeleteAsync = _ => Task.FromResult(false);
        using var cts = new CancellationTokenSource(); if (reason == "cancelled") cts.Cancel();
        await op.DeleteFileAsync(cts.Token);
        Assert.Empty(trash.Calls); Assert.Same(page, op.Book.CurrentPage); Assert.Equal(5, Directory.GetFiles(f.Images).Length); Assert.False(op.IsDeletingFile);
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task BackendFailureOrCancellationLeavesSourceSearchAndFrameUsable(bool cancel)
    {
        using var f = new Fixture(); var state = new SaveData(f.State); await state.LoadAsync(TestContext.Current.CancellationToken);
        await using var op = f.Operation(state); using var images = new BitmapFactory(new MagickImageDecoder());
        var trash = Attach(f, op, images, new(Path.Combine(f.Root, "trash")) { Failure = cancel ? new OperationCanceledException() : new IOException("卷不支持废纸篓") });
        await op.OpenAsync(f.Images, TestContext.Current.CancellationToken); Assert.True(await op.SearchPagesAsync("001 /or 002", op.Book, TestContext.Current.CancellationToken)); var page = op.Book!.CurrentPage; var frame = op.Frame;
        await op.DeleteFileAsync(TestContext.Current.CancellationToken);
        Assert.Single(trash.Calls); Assert.Same(page, op.Book.CurrentPage); Assert.Same(frame, op.Frame);
        Assert.Equal("001 /or 002", op.Book.Pages.SearchKeyword); Assert.Equal(5, op.Book.Pages.SourcePages.Count); Assert.True(File.Exists(page!.ArchiveEntry.FilePath));
        if (!cancel) Assert.Contains("卷不支持废纸篓", op.Error);
    }
    [Theory]
    [InlineData("page")]
    [InlineData("book")]
    [InlineData("permission")]
    [InlineData("close")]
    public async Task ConfirmationCannotDeleteChangedTarget(string change)
    {
        using var f = new Fixture(); var state = new SaveData(f.State); await state.LoadAsync(TestContext.Current.CancellationToken);
        await using var op = f.Operation(state); using var images = new BitmapFactory(new MagickImageDecoder()); var trash = Attach(f, op, images);
        await op.OpenAsync(f.Images, TestContext.Current.CancellationToken);
        var confirmed = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        op.ConfirmDeleteAsync = _ => confirmed.Task; var action = op.DeleteFileAsync(TestContext.Current.CancellationToken); Assert.True(op.IsDeletingFile);
        if (change == "page") await op.JumpAsync(2);
        if (change == "book") await op.OpenAsync(f.Zip, TestContext.Current.CancellationToken);
        if (change == "permission") Config.Current.System.IsFileWriteAccessEnabled = false;
        if (change == "close") await op.DisposeAsync();
        confirmed.SetResult(true); await action;
        Assert.Empty(trash.Calls); Assert.Equal(5, Directory.GetFiles(f.Images).Length);
    }
    [Fact]
    public async Task ConfirmedLateCancellationStillCoordinatesAndRepeatedDeletionReachesEmptyBook()
    {
        using var f = new Fixture(); var state = new SaveData(f.State); await state.LoadAsync(TestContext.Current.CancellationToken);
        await using var op = f.Operation(state); using var images = new BitmapFactory(new MagickImageDecoder()); using var cts = new CancellationTokenSource();
        var trash = Attach(f, op, images, new(Path.Combine(f.Root, "trash")) { After = cts.Cancel });
        Config.Current.System.IsRemoveConfirmed = false; op.ConfirmDeleteAsync = _ => throw new InvalidOperationException("不应弹出确认");
        await op.OpenAsync(f.Images, TestContext.Current.CancellationToken); await op.DeleteFileAsync(cts.Token);
        Assert.Equal("002.png", op.Book!.CurrentPage!.EntryName); Assert.Equal(4, op.Book.Pages.Count);
        await op.JumpAsync(3); await op.DeleteFileAsync(TestContext.Current.CancellationToken); Assert.Equal("004.png", op.Book.CurrentPage!.EntryName);
        for (int i = 0; i < 3; i++) await op.DeleteFileAsync(TestContext.Current.CancellationToken);
        Assert.Empty(op.Book.Pages); Assert.Empty(op.Book.Pages.SourcePages); Assert.Null(op.Book.CurrentPage); Assert.Null(op.Frame);
        Assert.False(op.CanDeleteFile); Assert.Equal(5, trash.Calls.Count); Assert.Empty(Directory.GetFiles(f.Images));
        Assert.Equal("", state.GetLastBook()!.Page); // 原历史规则拒绝空书登记，启动快照仍更新为空。
    }
    [Theory]
    [InlineData(BrowseLayoutMode.Paged)]
    [InlineData(BrowseLayoutMode.Continuous)]
    public async Task FilteredLastResultDeletionKeepsSearchAndRemainingSourceUntilCleared(BrowseLayoutMode mode)
    {
        using var f = new Fixture(); var state = new SaveData(f.State); await state.LoadAsync(TestContext.Current.CancellationToken);
        await using var op = f.Operation(state); using var images = new BitmapFactory(new MagickImageDecoder()); var trash = Attach(f, op, images);
        await op.OpenAsync(f.Images, TestContext.Current.CancellationToken); await op.SetBrowseModeAsync(mode);
        await op.SearchPagesAsync("003", op.Book, TestContext.Current.CancellationToken); await op.DeleteFileAsync(TestContext.Current.CancellationToken);
        Assert.Equal("003", op.Book!.Pages.SearchKeyword); Assert.Empty(op.Book.Pages); Assert.Equal(4, op.Book.Pages.SourcePages.Count);
        Assert.False(op.CanDeleteFile); await op.DeleteFileAsync(TestContext.Current.CancellationToken); Assert.Single(trash.Calls);
        Assert.Null(op.Frame); var anchor = op.Book.CurrentPage!; Assert.Contains(anchor, op.Book.Pages.SourcePages);
        Assert.DoesNotContain(anchor, op.Book.Pages); // 空搜索保留全源锚点，不能当作可见页/文件目标。
        var history = Assert.IsType<NeeView.Collections.HistoryLimitedCollection<PageHistoryUnit>>(typeof(PageHistory)
            .GetField("_history", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.GetValue(op.PageHistory));
        Assert.True(history.GetCurrent().IsEmpty());
        await op.SearchPagesAsync("", op.Book, TestContext.Current.CancellationToken); Assert.Equal(4, op.Book.Pages.Count); Assert.Same(anchor, op.Book.CurrentPage);
        Assert.DoesNotContain(op.Book.Pages, page => page.EntryName == "003.png");
    }
    [Fact]
    public async Task ArchiveAndDirectoryDeletionAreAvailableAndMasonryRequiresExplicitSelection()
    {
        using var f = new Fixture(); var state = new SaveData(f.State); await state.LoadAsync(TestContext.Current.CancellationToken);
        Directory.CreateDirectory(Path.Combine(f.Images, "000-child"));
        await using var op = f.Operation(state); using var images = new BitmapFactory(new MagickImageDecoder()); var trash = Attach(f, op, images);
        await op.OpenAsync(f.Zip, TestContext.Current.CancellationToken); Assert.False(op.CanDeleteFile);
        Config.Current.Archive.Zip.IsFileWriteAccessEnabled = true; Assert.True(op.CanDeleteFile);
        await op.OpenAsync(f.Images, TestContext.Current.CancellationToken); Assert.False(op.Book!.CurrentPage!.IsImage); Assert.True(op.CanDeleteFile);
        await op.SetBrowseModeAsync(BrowseLayoutMode.Masonry); Assert.False(op.CanDeleteFile);
        var page = op.Book.Pages.First(page => page.EntryName == "003.png"); await op.SelectFileActionPageAsync(op.Book, page);
        Assert.True(op.CanDeleteFile); await op.DeleteFileAsync(TestContext.Current.CancellationToken); Assert.Equal("003.png", Path.GetFileName(Assert.Single(trash.Calls)));
        Assert.False(op.CanDeleteFile); Assert.True(Directory.Exists(Path.Combine(f.Images, "000-child")));
    }
    [Fact]
    public async Task DeletedPagePixelAndDisplayLeasesAreRetiredUntilLastDisplayReleases()
    {
        using var f = new Fixture(); var state = new SaveData(f.State); await state.LoadAsync(TestContext.Current.CancellationToken);
        await using var op = f.Operation(state); using var images = new BitmapFactory(new MagickImageDecoder()); Attach(f, op, images);
        await op.OpenAsync(f.Images, TestContext.Current.CancellationToken); var page = op.Book!.CurrentPage!;
        var lease = await images.GetAsync(page, new(8, 8), TestContext.Current.CancellationToken); lease.RegisterDisplayBytes(256); long bytes = images.ByteCount;
        await op.DeleteFileAsync(TestContext.Current.CancellationToken); Assert.Equal(bytes, images.ByteCount); Assert.Equal(1, images.GetDiagnostics().RetiredEntries);
        Assert.NotEmpty(lease.Image.Pixels); lease.Dispose(); Assert.Equal(0, images.ByteCount); Assert.Empty(lease.Image.Pixels);
    }
    [Fact]
    public async Task DeletedPageLateNativeDecodeIsReleasedWithoutReturningDisplayResource()
    {
        using var f = new Fixture(); var state = new SaveData(f.State); await state.LoadAsync(TestContext.Current.CancellationToken);
        await using var op = f.Operation(state); var decoder = new LateDecoder(); using var images = new BitmapFactory(decoder); Attach(f, op, images);
        await op.OpenAsync(f.Images, TestContext.Current.CancellationToken);
        var work = images.GetAsync(op.Book!.CurrentPage!, new(8, 8), TestContext.Current.CancellationToken);
        await decoder.Started.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        try { await op.DeleteFileAsync(TestContext.Current.CancellationToken); }
        finally { decoder.Release.TrySetResult(); }
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => work); Assert.Empty(decoder.Result!.Pixels); Assert.Equal(0, images.ByteCount);
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task MissingOrLinkedSourceDuringConfirmationDoesNotDeleteAnotherFileOrRemovePage(bool link)
    {
        using var f = new Fixture(); var state = new SaveData(f.State); await state.LoadAsync(TestContext.Current.CancellationToken);
        await using var op = f.Operation(state); using var images = new BitmapFactory(new MagickImageDecoder()); var trash = Attach(f, op, images);
        await op.OpenAsync(f.Images, TestContext.Current.CancellationToken); var page = op.Book!.CurrentPage!;
        var otherPath = Path.Combine(f.Images, "005.png"); var otherBytes = await File.ReadAllBytesAsync(otherPath, TestContext.Current.CancellationToken);
        op.ConfirmDeleteAsync = _ => { File.Delete(page.ArchiveEntry.FilePath!); if (link) File.CreateSymbolicLink(page.ArchiveEntry.FilePath!, otherPath); return Task.FromResult(true); };
        await op.DeleteFileAsync(TestContext.Current.CancellationToken);
        Assert.Same(page, op.Book.CurrentPage); Assert.Equal(5, op.Book.Pages.Count); Assert.NotNull(op.Error);
        Assert.Equal(otherBytes, await File.ReadAllBytesAsync(otherPath, TestContext.Current.CancellationToken)); if (Directory.Exists(Path.Combine(f.Root, "模拟废纸篓"))) Assert.Empty(Directory.GetFiles(Path.Combine(f.Root, "模拟废纸篓")));
    }
    [Fact]
    public async Task SavingFailureDoesNotPretendSuccessfulTrashWasRolledBack()
    {
        using var f = new Fixture(); var state = new SaveData(f.State); await state.LoadAsync(TestContext.Current.CancellationToken);
        await using var op = f.Operation(state); using var images = new BitmapFactory(new MagickImageDecoder()); Attach(f, op, images);
        await op.OpenAsync(f.Images, TestContext.Current.CancellationToken); await op.SaveAsync(); var blocker = Path.Combine(f.State, "UserSetting.json.tmp"); Directory.CreateDirectory(blocker);
        try { await op.DeleteFileAsync(TestContext.Current.CancellationToken); Assert.Contains("文件已移至废纸篓", op.Error); Assert.Equal("002.png", op.Book!.CurrentPage!.EntryName); Assert.False(File.Exists(Path.Combine(f.Images, "001.png"))); }
        finally { Directory.Delete(blocker); }
        await op.SaveAsync(); Assert.Equal("002.png", state.Find(f.Images)!.Page);
    }
    [AvaloniaFact]
    public async Task FormalMenuConfirmationCancelAcceptAndShutdownUseOneAwaitableAction()
    {
        using var f = new Fixture(); var state = new SaveData(f.State); await state.LoadAsync(TestContext.Current.CancellationToken);
        var op = f.Operation(state); var images = new BitmapFactory(new MagickImageDecoder()); var trash = Attach(f, op, images);
        var window = new MainWindow(); window.Bind(new(op, new(op), state), images, trash); window.Show();
        try
        {
            await window.OpenAsync(f.Images); Assert.True(window.IsCommandAvailable("DeleteFile"));
            // 运行正式登记判定导出入口，不能以当前目标可用性或Stage文案替代执行入口。
            var implemented = typeof(MainWindow).GetMethod("IsCommandImplemented", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;
            var items = new CommandTable(op).Definitions.Select(d => new { d.Name, d.Text, d.Shortcut, d.MouseGesture, d.Source, d.Stage,
                implemented = (bool)implemented.Invoke(window, [d.Name])! }).ToArray();
            Assert.Equal(235, items.Length); Assert.Equal(170, items.Count(item => item.implemented));
            var phase = Environment.GetEnvironmentVariable("NEEVIEW_ACCEPTANCE_PHASE") ?? "p4-delete";
            var export = Output(phase + "-commands.json");
            await File.WriteAllTextAsync(export, System.Text.Json.JsonSerializer.Serialize(new
            { scope = "当前执行入口登记，不等于完整原功能覆盖；未迁能力保留原命令/菜单占位", total = items.Length, implemented = items.Count(item => item.implemented), items },
                new System.Text.Json.JsonSerializerOptions { WriteIndented = true }) + "\n", TestContext.Current.CancellationToken);
            var action = window.ExecuteAsync("DeleteFile"); await WaitAsync(() => window.OwnedWindows.Count() == 1); var dialog = Assert.Single(window.OwnedWindows); dialog.Close(false); await action;
            Assert.Empty(trash.Calls); Assert.Equal(5, op.Book!.Pages.Count);
            action = window.ExecuteAsync("DeleteFile"); await WaitAsync(() => window.OwnedWindows.Count() == 1); dialog = Assert.Single(window.OwnedWindows);
            Dispatcher.UIThread.RunJobs(); dialog.UpdateLayout();
            using (var bitmap = new RenderTargetBitmap(new PixelSize((int)Math.Ceiling(dialog.Bounds.Width), (int)Math.Ceiling(dialog.Bounds.Height))))
            { bitmap.Render(dialog); bitmap.Save(Output(phase + "-confirm.png"), PngBitmapEncoderOptions.Default); }
            dialog.Close(true); await action;
            Assert.True(trash.Calls.Count == 1, op.Error ?? "没有执行删除"); Assert.Equal("002.png", op.Book!.CurrentPage!.EntryName);
            action = window.ExecuteAsync("DeleteFile"); await WaitAsync(() => window.OwnedWindows.Count() == 1); Assert.Single(window.OwnedWindows); await window.PrepareShutdownAsync(); await action;
            Assert.Single(trash.Calls); Assert.True(File.Exists(Path.Combine(f.Images, "002.png")));
        }
        finally { await window.PrepareShutdownAsync(); window.Close(); }
    }
    [AvaloniaFact]
    public async Task ShutdownWaitsForAlreadyAuthorizedSystemWorkAndRejectsDuplicateDelete()
    {
        using var f = new Fixture(); var state = new SaveData(f.State); await state.LoadAsync(TestContext.Current.CancellationToken);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously); var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var op = f.Operation(state); var images = new BitmapFactory(new MagickImageDecoder());
        var trash = Attach(f, op, images, new(Path.Combine(f.Root, "trash")) { Before = async () => { entered.SetResult(); await release.Task; } });
        Config.Current.System.IsRemoveConfirmed = false;
        var window = new MainWindow(); window.Bind(new(op, new(op), state), images, trash); window.Show();
        try
        {
            await window.OpenAsync(f.Images); var action = window.ExecuteAsync("DeleteFile"); await entered.Task;
            await window.ExecuteAsync("DeleteFile"); Assert.Single(trash.Calls); var closing = window.PrepareShutdownAsync();
            await Task.Delay(20, TestContext.Current.CancellationToken); Assert.False(closing.IsCompleted); Assert.NotNull(op.Book);
            release.SetResult(); await action; await closing; Assert.Null(op.Book); Assert.Equal("002.png", state.Find(f.Images)!.Page);
        }
        finally { release.TrySetResult(); await window.PrepareShutdownAsync(); window.Close(); }
    }
    [AvaloniaFact]
    public async Task FileSettingsDraftCancellationFailureRetryAndJsonPreserveOriginalFields()
    {
        using var f = new Fixture(); Directory.CreateDirectory(f.State);
        await File.WriteAllTextAsync(Path.Combine(f.State, "UserSetting.json"), "{\"Config\":{\"System\":{\"IsRemoveConfirmed\":true,\"IsFileWriteAccessEnabled\":false,\"IsRemoveWantNukeWarning\":true,\"UnmigratedDelete\":{\"Value\":\"kept\"}}}}", TestContext.Current.CancellationToken);
        var state = new SaveData(f.State); await state.LoadAsync(TestContext.Current.CancellationToken);
        var op = f.Operation(state); var images = new BitmapFactory(new MagickImageDecoder()); var trash = new FixtureTrash(Path.Combine(f.Root, "trash"));
        var model = new ReaderWorkspaceViewModel(op, new(op), state); var window = new MainWindow(); window.Bind(model, images, trash); window.Show();
        try
        {
            await window.OpenAsync(f.Images);
            var settings = new SettingsWindow(model); settings.Show(window); settings.FindControl<ListBox>("SettingsNavigation")!.SelectedIndex = 6;
            Assert.True(settings.FindControl<ScrollViewer>("FileSettings")!.IsVisible);
            settings.FindControl<CheckBox>("RemoveConfirmed")!.IsChecked = false; settings.Close(); Assert.True(Config.Current.System.IsRemoveConfirmed);
            settings = new SettingsWindow(model); settings.Show(window); settings.FindControl<CheckBox>("RemoveConfirmed")!.IsChecked = false;
            settings.FindControl<CheckBox>("FileWriteAccess")!.IsChecked = true;
            var blocker = Path.Combine(f.State, "UserSetting.json.tmp"); Directory.CreateDirectory(blocker);
            try
            {
                settings.FindControl<Button>("SaveSettings")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); await WaitAsync(() => settings.FindControl<TextBlock>("Message")!.Text?.StartsWith("保存失败") == true);
                Assert.True(Config.Current.System.IsRemoveConfirmed); Assert.False(Config.Current.System.IsFileWriteAccessEnabled);
            }
            finally { Directory.Delete(blocker); }
            settings.FindControl<Button>("SaveSettings")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); await WaitAsync(() => !settings.IsVisible);
            Assert.False(Config.Current.System.IsRemoveConfirmed); Assert.True(window.IsCommandAvailable("DeleteFile"));
            await window.PrepareShutdownAsync(); await new SaveData(f.State).LoadAsync(TestContext.Current.CancellationToken);
            Assert.False(Config.Current.System.IsRemoveConfirmed); Assert.True(Config.Current.System.IsFileWriteAccessEnabled);
            var saved = System.Text.Json.Nodes.JsonNode.Parse(await File.ReadAllTextAsync(Path.Combine(f.State, "UserSetting.json"), TestContext.Current.CancellationToken))!;
            Assert.True(saved["Config"]!["System"]!["IsRemoveWantNukeWarning"]!.GetValue<bool>());
            Assert.Equal("kept", saved["Config"]!["System"]!["UnmigratedDelete"]!["Value"]!.GetValue<string>());
        }
        finally { await window.PrepareShutdownAsync(); window.Close(); }
    }
    private static async Task WaitAsync(Func<bool> condition)
    {
        for (int i = 0; i < 200 && !condition(); i++) { Dispatcher.UIThread.RunJobs(); await Task.Delay(10, TestContext.Current.CancellationToken); }
        Assert.True(condition());
    }
    private static string Output(string name)
    {
        var output = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../acceptance", name));
        Directory.CreateDirectory(Path.GetDirectoryName(output)!); return output;
    }
}
