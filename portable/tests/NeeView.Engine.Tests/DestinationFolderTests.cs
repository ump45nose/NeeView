using System.Text.Json.Nodes;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using NeeView;
using NeeView.Backends;
using NeeView.MacOS.Views;
namespace NeeView.Engine.Tests;

/// <summary>fork单图分类及真实文件恢复回归，全部使用应用生成的临时图片副本。</summary>
public sealed class DestinationFolderTests
{
    private sealed class TestPlatform : IPlatformService { public Task RevealAsync(string path, CancellationToken token = default) => Task.CompletedTask; public Task TrashAsync(string path, CancellationToken token = default) => Task.CompletedTask; }
    private static string Target(Fixture fixture, string name = "target") => Directory.CreateDirectory(Path.Combine(fixture.Root, name)).FullName;
    private static FileOperationBackend Backend(Fixture fixture) => new(Path.Combine(fixture.State, "FileRecovery"));
    private static void Attach(BookOperation operation, FileOperationBackend backend, BitmapFactory images, out DestinationMoveService service)
    { service = new(backend); operation.AttachFileOperations(service, backend, images); Config.Current.System.IsFileWriteAccessEnabled = true; }

    /// <summary>暂停真实文件后端，验证正在落盘的动作与切书/关闭/晚取消的协调。</summary>
    private sealed class PausedBackend(IFileOperationBackend inner) : IFileOperationBackend
    {
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Action? AfterCommit { get; set; }
        public Task<bool> FileExistsAsync(string path, CancellationToken token) => inner.FileExistsAsync(path, token);
        public async Task<FileTransferResult> TransferAsync(FileTransferRequest request, CancellationToken token)
        { Started.TrySetResult(); await Release.Task.WaitAsync(token); var result = await inner.TransferAsync(request, token); AfterCommit?.Invoke(); return result; }
        public Task ReleaseAsync(FileTransferResult result) => inner.ReleaseAsync(result);
        public Task<IReadOnlyList<string>> RecoverAsync(CancellationToken token = default) => inner.RecoverAsync(token);
        public Task CreateDirectoryAsync(string parent, string name, CancellationToken token) => inner.CreateDirectoryAsync(parent, name, token);
    }
    private sealed class CountedArchives : IArchiveFactory
    {
        private readonly ArchiveFactory _inner = new();
        public int Reads { get; private set; }
        public TaskCompletionSource? Late { get; set; }
        public Task<Archive> OpenAsync(string path, CancellationToken token) => _inner.OpenAsync(path, token);
        public Task<IReadOnlyList<FolderItem>> ListBooksAsync(string path, CancellationToken token) => _inner.ListBooksAsync(path, token);
        public async Task<IReadOnlyList<FolderItem>> ListFoldersAsync(string path, CancellationToken token)
        {
            Reads++; var result = await _inner.ListFoldersAsync(path, token);
            if (Late is { } late) await late.Task; // 模拟无法立即中断的枚举。
            return result;
        }
    }

    [Fact]
    public async Task InPlaceConfigurationFailureRestoresClonedFoldersAndCanRetry()
    {
        using var f = new Fixture(); var state = new SaveData(f.State); await state.LoadAsync(TestContext.Current.CancellationToken);
        await using var op = f.Operation(state); Config.Current.System.DestinationFolderCollection = new([new("原名", Target(f))]);
        var panel = op.DestinationFolders; var failure = Path.Combine(f.State, "UserSetting.json.tmp"); Directory.CreateDirectory(failure);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => op.EditDestinationFoldersAsync(() =>
        {
            Config.Current.System.DestinationFolderCollection[0].Name = "污染";
            Config.Current.System.DestinationFolderCollection.Clear(); Config.Current.Panels.IsDestinationFolderCopyMode = true;
            Config.Current.System.IsFileWriteAccessEnabled = true;
        }, TestContext.Current.CancellationToken));
        Assert.Equal("原名", Assert.Single(Config.Current.System.DestinationFolderCollection).Name);
        Assert.Equal("原名", Assert.Single(panel.ManagedFolders).Name);
        Assert.False(Config.Current.Panels.IsDestinationFolderCopyMode); Assert.False(Config.Current.System.IsFileWriteAccessEnabled);
        Directory.Delete(failure); await op.EditDestinationFoldersAsync(() => Config.Current.System.DestinationFolderCollection[0].Name = "重试", TestContext.Current.CancellationToken);
        await state.LoadAsync(TestContext.Current.CancellationToken); Assert.Equal("重试", Assert.Single(Config.Current.System.DestinationFolderCollection).Name);
    }

    [Fact]
    public async Task MovingLastSearchResultKeepsQueryAndOtherSourcePagesAndSaveRetry()
    {
        using var f = new Fixture(); var state = new SaveData(f.State); await state.LoadAsync(TestContext.Current.CancellationToken);
        await using var op = f.Operation(state); using var images = new BitmapFactory(new MagickImageDecoder()); Attach(op, Backend(f), images, out var moves);
        await op.OpenAsync(f.Images, TestContext.Current.CancellationToken); await op.SearchPagesAsync("001", op.Book, TestContext.Current.CancellationToken); Assert.Single(op.Book!.Pages);
        Directory.CreateDirectory(Path.Combine(f.State, "UserSetting.json.tmp"));
        await op.ClassifyAsync(new DestinationFolder("", Target(f)), false, TestContext.Current.CancellationToken);
        Assert.Equal(1, moves.UndoCount); Assert.Equal("001", op.Book.Pages.SearchKeyword); Assert.Empty(op.Book.Pages);
        Assert.Equal(4, op.Book.Pages.SourcePages.Count); Assert.NotEqual("001.png", op.Book.CurrentPage!.EntryName); Assert.Null(op.Frame); Assert.False(op.CanFileAction);
        Assert.Contains("文件操作已成功", op.Error);
        Directory.Delete(Path.Combine(f.State, "UserSetting.json.tmp")); await op.SaveAsync();
        Assert.NotEqual("001.png", state.GetLastBook()!.Page);
        await op.ReplayDestinationMoveAsync(true, TestContext.Current.CancellationToken); Assert.Equal("001", op.Book!.Pages.SearchKeyword); Assert.Single(op.Book.Pages); Assert.Equal("001.png", op.Book.CurrentPage!.EntryName);
        await op.ReplayDestinationMoveAsync(false, TestContext.Current.CancellationToken); Assert.Equal("001", op.Book!.Pages.SearchKeyword); Assert.Empty(op.Book.Pages);
        await op.SearchPagesAsync("", op.Book, TestContext.Current.CancellationToken); Assert.Equal(4, op.Book.Pages.Count); Assert.NotNull(op.Frame);
    }

    [Fact]
    public async Task InFlightMoveCompletesOldBookBeforeNewBookCommit()
    {
        using var f = new Fixture(); var state = new SaveData(f.State); await state.LoadAsync(TestContext.Current.CancellationToken);
        await using var op = f.Operation(state); using var images = new BitmapFactory(new MagickImageDecoder());
        var backend = new PausedBackend(Backend(f)); var moves = new DestinationMoveService(backend); op.AttachFileOperations(moves, backend, images); Config.Current.System.IsFileWriteAccessEnabled = true;
        await op.OpenAsync(f.Images, TestContext.Current.CancellationToken); var old = op.Book!; var move = op.ClassifyAsync(new DestinationFolder("", Target(f)), false, TestContext.Current.CancellationToken);
        await backend.Started.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        var open = op.OpenAsync(f.Zip, TestContext.Current.CancellationToken); Assert.False(open.IsCompleted); backend.Release.SetResult(); await move; await open;
        Assert.Equal(4, old.Pages.SourcePages.Count); Assert.Equal(f.Zip, op.Book!.Path); Assert.Equal(5, op.Book.Pages.Count); Assert.Equal("001.png", op.Book.CurrentPage!.EntryName);
        Assert.Equal(1, moves.UndoCount); Assert.False(File.Exists(Path.Combine(f.Images, "001.png")));
    }

    [Fact]
    public async Task CancellationAfterFileCommitStillUpdatesHistoryAndReader()
    {
        using var f = new Fixture(); var state = new SaveData(f.State); await state.LoadAsync(TestContext.Current.CancellationToken);
        await using var op = f.Operation(state); using var images = new BitmapFactory(new MagickImageDecoder()); using var token = new CancellationTokenSource();
        var backend = new PausedBackend(Backend(f)) { AfterCommit = token.Cancel }; var moves = new DestinationMoveService(backend); op.AttachFileOperations(moves, backend, images); Config.Current.System.IsFileWriteAccessEnabled = true;
        backend.Release.SetResult(); await op.OpenAsync(f.Images, TestContext.Current.CancellationToken); await op.ClassifyAsync(new DestinationFolder("", Target(f)), false, token.Token);
        Assert.True(token.IsCancellationRequested); Assert.Equal(1, moves.UndoCount); Assert.Equal("002.png", op.Book!.CurrentPage!.EntryName); Assert.Equal(4, op.Book.Pages.Count);
    }

    [Fact]
    public async Task SameDirectoryNavigationDoesNotRescanAndDisabledOrClosedPanelRejectsLateItems()
    {
        using var f = new Fixture(); var state = new SaveData(f.State); await state.LoadAsync(TestContext.Current.CancellationToken);
        var archives = new CountedArchives(); await using var op = new BookOperation(archives, new MagickImageDecoder(), state);
        Directory.CreateDirectory(Path.Combine(f.Images, "child")); await op.OpenAsync(Path.Combine(f.Images, "001.png"), TestContext.Current.CancellationToken); using var panel = op.DestinationFolders;
        await panel.RefreshChildrenAsync(TestContext.Current.CancellationToken); int reads = archives.Reads; Assert.Single(panel.CurrentFolderChildren);
        await op.JumpAsync(op.Book!.Pages.FindIndex(page => page.EntryName == "002.png")); Assert.Equal(reads, archives.Reads);
        archives.Late = new(TaskCreationOptions.RunContinuationsAsynchronously); var refresh = panel.RefreshChildrenAsync(TestContext.Current.CancellationToken);
        await op.EditDestinationFoldersAsync(() => Config.Current.Panels.IsDestinationFolderAutoRefreshEnabled = false, TestContext.Current.CancellationToken);
        var previous = panel.CurrentFolderChildren; archives.Late.SetResult(); await refresh; Assert.Same(previous, panel.CurrentFolderChildren); Assert.False(panel.IsRefreshing);
        archives.Late = new(TaskCreationOptions.RunContinuationsAsynchronously); refresh = panel.RefreshChildrenAsync(TestContext.Current.CancellationToken); panel.Dispose(); archives.Late.SetResult(); await refresh;
        Assert.Same(previous, panel.CurrentFolderChildren); Assert.Null(panel.Error);
    }

    [Theory]
    [InlineData("path")]
    [InlineData("backup-link")]
    [InlineData("source-link")]
    public async Task RecoveryRejectsMismatchedPathsAndLinksWithoutTouchingExternalFiles(string change)
    {
        using var f = new Fixture(); var state = new SaveData(f.State); await state.LoadAsync(TestContext.Current.CancellationToken); var backend = Backend(f);
        var source = Path.Combine(f.Images, "001.png"); var target = Path.Combine(Target(f), "001.png"); await File.WriteAllTextAsync(target, "previous", TestContext.Current.CancellationToken);
        var result = await backend.TransferAsync(new(source, target, true, true), TestContext.Current.CancellationToken);
        var external = Path.Combine(f.Root, "external"); await File.WriteAllTextAsync(external, "external", TestContext.Current.CancellationToken);
        var journal = JsonNode.Parse(await File.ReadAllTextAsync(result.Journal, TestContext.Current.CancellationToken))!; journal["Completed"] = false;
        if (change == "path") journal["Temporary"] = external;
        else if (change == "backup-link") { File.Delete(result.Backup!); File.CreateSymbolicLink(result.Backup!, external); }
        else File.CreateSymbolicLink(source, external);
        await File.WriteAllTextAsync(result.Journal, journal.ToJsonString(), TestContext.Current.CancellationToken);
        Assert.Single(await backend.RecoverAsync(TestContext.Current.CancellationToken)); Assert.True(File.Exists(result.Journal)); Assert.Equal("external", await File.ReadAllTextAsync(external, TestContext.Current.CancellationToken));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task InterruptedStagingRestoresOriginalSourceAndOverwrittenTarget(bool overwrite)
    {
        using var f = new Fixture(); var state = new SaveData(f.State); await state.LoadAsync(TestContext.Current.CancellationToken); var backend = Backend(f);
        var source = Path.Combine(f.Images, "001.png"); var target = Path.Combine(Target(f), "001.png"); var bytes = await File.ReadAllBytesAsync(source, TestContext.Current.CancellationToken);
        if (overwrite) await File.WriteAllTextAsync(target, "previous", TestContext.Current.CancellationToken); var result = await backend.TransferAsync(new(source, target, true, overwrite), TestContext.Current.CancellationToken);
        var journal = JsonNode.Parse(await File.ReadAllTextAsync(result.Journal, TestContext.Current.CancellationToken))!; journal["Completed"] = false;
        File.Move(target, journal["SourceStage"]!.GetValue<string>()); if (overwrite) File.Move(result.Backup!, journal["DestinationStage"]!.GetValue<string>());
        File.Copy(journal["SourceStage"]!.GetValue<string>(), journal["Temporary"]!.GetValue<string>());
        await File.WriteAllTextAsync(result.Journal, journal.ToJsonString(), TestContext.Current.CancellationToken);
        Assert.Empty(await backend.RecoverAsync(TestContext.Current.CancellationToken)); Assert.Equal(bytes, await File.ReadAllBytesAsync(source, TestContext.Current.CancellationToken));
        if (overwrite) Assert.Equal("previous", await File.ReadAllTextAsync(target, TestContext.Current.CancellationToken)); else Assert.False(File.Exists(target));
    }

    [AvaloniaFact]
    public async Task FormalShutdownWaitsForFixedIndexActionThenSavesAndReleases()
    {
        using var f = new Fixture(); var state = new SaveData(f.State); await state.LoadAsync(TestContext.Current.CancellationToken);
        var op = f.Operation(state); var images = new BitmapFactory(new MagickImageDecoder()); var backend = new PausedBackend(Backend(f)); var moves = new DestinationMoveService(backend);
        op.AttachFileOperations(moves, backend, images); Config.Current.System.IsFileWriteAccessEnabled = true; Config.Current.System.DestinationFolderCollection = new([new("", Target(f))]);
        state.SetCommandParameter("MoveToFolderAs", new MoveToFolderAsCommandParameter { Index = 1 });
        var window = new MainWindow(); window.Bind(new(op, new CommandTable(op), state), images, new TestPlatform()); window.Show();
        try
        {
            await window.OpenAsync(f.Images); var action = window.ExecuteAsync("MoveToFolderAs"); await backend.Started.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
            var close = window.PrepareShutdownAsync(); await Task.Delay(20, TestContext.Current.CancellationToken); Assert.False(close.IsCompleted); Assert.NotNull(op.Book);
            backend.Release.SetResult(); await action; await close; Assert.Null(op.Book); Assert.Equal("002.png", state.Find(f.Images)!.Page); Assert.Equal(1, moves.UndoCount);
        }
        finally { backend.Release.TrySetResult(); await window.PrepareShutdownAsync(); window.Close(); }
    }

    [Fact]
    public async Task MoveUsesOriginalMainImageNextLastEmptyAndUndoLocation()
    {
        using var fixture = new Fixture(); var state = new SaveData(fixture.State); await state.LoadAsync(TestContext.Current.CancellationToken);
        await using var operation = fixture.Operation(state); using var images = new BitmapFactory(new MagickImageDecoder()); var backend = Backend(fixture);
        Attach(operation, backend, images, out var moves); string target = Target(fixture);
        Config.Current.BookSettingDefault.PageMode = PageMode.WidePage;
        await operation.OpenAsync(fixture.Images, TestContext.Current.CancellationToken); var main = operation.Book!.CurrentPage!;
        Assert.Equal("001.png", main.EntryName); Assert.True(operation.CanFileAction);
        await operation.ClassifyAsync(new DestinationFolder("目标", target), false, TestContext.Current.CancellationToken);
        Assert.Equal("002.png", operation.Book.CurrentPage!.EntryName); Assert.Equal(4, operation.Book.Pages.Count);
        Assert.DoesNotContain(main, operation.Book.Pages.SourcePages); Assert.False(File.Exists(Path.Combine(fixture.Images, "001.png")));
        Assert.True(File.Exists(Path.Combine(target, "001.png"))); Assert.Equal(1, moves.UndoCount);
        await operation.ReplayDestinationMoveAsync(true, TestContext.Current.CancellationToken); Assert.Equal("001.png", operation.Book!.CurrentPage!.EntryName);
        Assert.Equal(5, operation.Book.Pages.Count); Assert.True(moves.CanRedo);
        await operation.ReplayDestinationMoveAsync(false, TestContext.Current.CancellationToken); Assert.Equal("002.png", operation.Book!.CurrentPage!.EntryName);
        Config.Current.BookSetting.PageMode = PageMode.SinglePage;
        while (operation.Book!.Pages.Count > 0) { await operation.JumpAsync(operation.Book.Pages.Count - 1); await operation.ClassifyAsync(new DestinationFolder("", target), false, TestContext.Current.CancellationToken); }
        Assert.Null(operation.Book.CurrentPage); Assert.Null(operation.Frame); Assert.Empty(operation.Book.Pages.SourcePages);
        await operation.ReplayDestinationMoveAsync(true, TestContext.Current.CancellationToken); Assert.Single(operation.Book!.Pages); Assert.NotNull(operation.Frame);
    }

    [Fact]
    public async Task NumericModeRemappingAndFixedMovePreserveCopyPosition()
    {
        using var fixture = new Fixture(); var state = new SaveData(fixture.State); await state.LoadAsync(TestContext.Current.CancellationToken);
        await using var operation = fixture.Operation(state); using var images = new BitmapFactory(new MagickImageDecoder());
        Attach(operation, Backend(fixture), images, out var moves);
        var target1 = Target(fixture, "1"); var target2 = Target(fixture, "2");
        await operation.EditDestinationFoldersAsync(() => { Config.Current.System.DestinationFolderCollection = new([new("A", target1), new("B", target2)]); Config.Current.Panels.IsDestinationFolderCopyMode = true; }, TestContext.Current.CancellationToken);
        state.SetCommandParameter("MoveToDestinationFolder1", new MoveToFolderAsCommandParameter { Index = 2 });
        await state.SaveAsync(null, TestContext.Current.CancellationToken); await state.LoadAsync(TestContext.Current.CancellationToken);
        await operation.OpenAsync(fixture.Images, TestContext.Current.CancellationToken); var book = operation.Book; var page = book!.CurrentPage;
        await new CommandTable(operation).ExecuteAsync("MoveToDestinationFolder1");
        Assert.Same(book, operation.Book); Assert.Same(page, book.CurrentPage); Assert.Equal(5, book.Pages.Count);
        Assert.True(File.Exists(Path.Combine(target2, "001.png"))); Assert.Equal(0, moves.UndoCount);
        await operation.ClassifyAsync(1, followPanelMode: false);
        Assert.False(File.Exists(Path.Combine(fixture.Images, "001.png"))); Assert.Equal("002.png", book.CurrentPage!.EntryName); Assert.Equal(1, moves.UndoCount);
        await operation.OpenAsync(fixture.Zip, TestContext.Current.CancellationToken); Assert.False(operation.CanFileAction);
        await operation.ClassifyAsync(1); Assert.Equal(5, operation.Book!.Pages.Count);
    }

    [Fact]
    public async Task FailedCancelledReplayAndMissingTargetKeepHistoryStack()
    {
        using var fixture = new Fixture(); var state = new SaveData(fixture.State); await state.LoadAsync(TestContext.Current.CancellationToken);
        var backend = Backend(fixture); var service = new DestinationMoveService(backend); string source = Path.Combine(fixture.Images, "001.png"), target = Path.Combine(Target(fixture), "001.png");
        Assert.Null(await service.TransferAsync(source, Path.Combine(fixture.Root, "missing", "x.png"), true, TestContext.Current.CancellationToken)); Assert.Equal(0, service.UndoCount); Assert.True(File.Exists(source));
        Assert.NotNull(await service.TransferAsync(source, target, true, TestContext.Current.CancellationToken));
        await File.WriteAllTextAsync(source, "other", TestContext.Current.CancellationToken);
        service.ConfirmOverwriteAsync = _ => Task.FromResult(false);
        Assert.Null(await service.ReplayAsync(true, TestContext.Current.CancellationToken)); Assert.Equal(1, service.UndoCount); Assert.Equal(0, service.RedoCount);
        Assert.Equal("other", await File.ReadAllTextAsync(source, TestContext.Current.CancellationToken));
        using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
        Assert.Null(await service.ReplayAsync(true, cancelled.Token)); Assert.Equal(1, service.UndoCount);
        File.Delete(target); Assert.Null(await service.ReplayAsync(true, TestContext.Current.CancellationToken)); Assert.Equal(1, service.UndoCount);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ClassificationInsideRecursiveBookReplacesTargetPageWithoutStalePixels(bool copy)
    {
        using var f = new Fixture(); var state = new SaveData(f.State); await state.LoadAsync(TestContext.Current.CancellationToken);
        var child = Directory.CreateDirectory(Path.Combine(f.Images, "child")).FullName;
        File.Copy(Path.Combine(f.Images, "003.png"), Path.Combine(child, "001.png"));
        Config.Current.BookSettingDefault.IsRecursiveFolder = true; Config.Current.System.BookPageCollectMode = BookPageCollectMode.Image;
        await using var op = f.Operation(state); using var images = new BitmapFactory(new MagickImageDecoder()); var backend = Backend(f);
        Attach(op, backend, images, out var moves); moves.ConfirmOverwriteAsync = _ => Task.FromResult(true);
        await op.OpenAsync(Path.Combine(f.Images, "001.png"), TestContext.Current.CancellationToken); var page = op.Book!.CurrentPage!;
        var oldTarget = op.Book.Pages.SourcePages.Single(p => p.ArchiveEntry.FilePath == Path.Combine(child, "001.png"));
        await op.ClassifyAsync(new DestinationFolder("", child), copy, TestContext.Current.CancellationToken);
        Assert.DoesNotContain(oldTarget, op.Book.Pages.SourcePages);
        var target = op.Book.Pages.SourcePages.Single(p => p.ArchiveEntry.FilePath == Path.Combine(child, "001.png")); Assert.NotSame(page, target);
        Assert.Equal(copy ? 6 : 5, op.Book.Pages.SourcePages.Count); Assert.Equal(copy ? 0 : 1, moves.UndoCount);
        await using var stream = await target.ArchiveEntry.Archive.OpenEntryAsync(target.ArchiveEntry, TestContext.Current.CancellationToken);
        Assert.True(stream.Length > 0); if (copy) Assert.Same(page, op.Book.CurrentPage); else Assert.Equal("002.png", op.Book.CurrentPage!.EntryName);
    }

    [Fact]
    public async Task OverwriteUndoRedoRestoresBothOriginalFilesAcrossSeveralCycles()
    {
        using var fixture = new Fixture(); var state = new SaveData(fixture.State); await state.LoadAsync(TestContext.Current.CancellationToken); var backend = Backend(fixture);
        var moves = new DestinationMoveService(backend) { ConfirmOverwriteAsync = _ => Task.FromResult(true) };
        string source = Path.Combine(fixture.Images, "001.png"), target = Path.Combine(Target(fixture), "001.png");
        var original = await File.ReadAllBytesAsync(source, TestContext.Current.CancellationToken); await File.WriteAllTextAsync(target, "original target", TestContext.Current.CancellationToken);
        Assert.NotNull(await moves.TransferAsync(source, target, true, TestContext.Current.CancellationToken)); Assert.Equal(original, await File.ReadAllBytesAsync(target, TestContext.Current.CancellationToken));
        for (int i = 0; i < 3; i++)
        {
            Assert.NotNull(await moves.ReplayAsync(true, TestContext.Current.CancellationToken)); Assert.Equal(original, await File.ReadAllBytesAsync(source, TestContext.Current.CancellationToken)); Assert.Equal("original target", await File.ReadAllTextAsync(target, TestContext.Current.CancellationToken));
            Assert.NotNull(await moves.ReplayAsync(false, TestContext.Current.CancellationToken)); Assert.False(File.Exists(source)); Assert.Equal(original, await File.ReadAllBytesAsync(target, TestContext.Current.CancellationToken));
        }
        await File.WriteAllTextAsync(source, "new source conflict", TestContext.Current.CancellationToken);
        Assert.NotNull(await moves.ReplayAsync(true, TestContext.Current.CancellationToken)); Assert.Equal("original target", await File.ReadAllTextAsync(target, TestContext.Current.CancellationToken));
        Assert.NotNull(await moves.ReplayAsync(false, TestContext.Current.CancellationToken)); Assert.Equal("new source conflict", await File.ReadAllTextAsync(source, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task UndoWhileBrowsingDestinationKeepsRestoredOverwriteAndRefreshesPageVersion()
    {
        using var f = new Fixture(); var state = new SaveData(f.State); await state.LoadAsync(TestContext.Current.CancellationToken);
        await using var op = f.Operation(state); using var images = new BitmapFactory(new MagickImageDecoder()); var backend = Backend(f);
        Attach(op, backend, images, out var moves); moves.ConfirmOverwriteAsync = _ => Task.FromResult(true);
        string target = Target(f); File.Copy(Path.Combine(f.Images, "003.png"), Path.Combine(target, "001.png"));
        var previous = await File.ReadAllBytesAsync(Path.Combine(target, "001.png"), TestContext.Current.CancellationToken);
        await op.OpenAsync(f.Images, TestContext.Current.CancellationToken); await op.ClassifyAsync(new DestinationFolder("", target), false, TestContext.Current.CancellationToken);
        await op.OpenAsync(target, TestContext.Current.CancellationToken); var old = op.Book!.CurrentPage;
        await op.ReplayDestinationMoveAsync(true, TestContext.Current.CancellationToken);
        Assert.Equal(target, op.Book!.Path); Assert.Single(op.Book.Pages); Assert.NotSame(old, op.Book.CurrentPage); Assert.NotNull(op.Frame);
        Assert.Equal(previous, await File.ReadAllBytesAsync(op.Book.CurrentPage!.ArchiveEntry.FilePath!, TestContext.Current.CancellationToken));
        await op.ReplayDestinationMoveAsync(false, TestContext.Current.CancellationToken); Assert.Single(op.Book!.Pages); Assert.False(File.Exists(Path.Combine(f.Images, "001.png")));
    }

    [Fact]
    public async Task InterruptedInstalledMoveRollsBackButExternalChangeIsRetainedForReview()
    {
        using var fixture = new Fixture(); var state = new SaveData(fixture.State); await state.LoadAsync(TestContext.Current.CancellationToken); var backend = Backend(fixture);
        string source = Path.Combine(fixture.Images, "001.png"), target = Path.Combine(Target(fixture), "001.png");
        var original = await File.ReadAllBytesAsync(source, TestContext.Current.CancellationToken); await File.WriteAllTextAsync(target, "previous", TestContext.Current.CancellationToken);
        var result = await backend.TransferAsync(new(source, target, true, true), TestContext.Current.CancellationToken);
        var journal = JsonNode.Parse(await File.ReadAllTextAsync(result.Journal, TestContext.Current.CancellationToken))!; journal["Completed"] = false;
        await File.WriteAllTextAsync(result.Journal, journal.ToJsonString(), TestContext.Current.CancellationToken);
        Assert.Empty(await backend.RecoverAsync(TestContext.Current.CancellationToken)); Assert.Equal(original, await File.ReadAllBytesAsync(source, TestContext.Current.CancellationToken)); Assert.Equal("previous", await File.ReadAllTextAsync(target, TestContext.Current.CancellationToken));
        result = await backend.TransferAsync(new(source, target, true, true), TestContext.Current.CancellationToken);
        journal = JsonNode.Parse(await File.ReadAllTextAsync(result.Journal, TestContext.Current.CancellationToken))!; journal["Completed"] = false;
        await File.WriteAllTextAsync(result.Journal, journal.ToJsonString(), TestContext.Current.CancellationToken); await File.WriteAllTextAsync(target, "external change", TestContext.Current.CancellationToken);
        Assert.Single(await backend.RecoverAsync(TestContext.Current.CancellationToken)); Assert.Equal("external change", await File.ReadAllTextAsync(target, TestContext.Current.CancellationToken)); Assert.True(File.Exists(result.Backup)); Assert.True(File.Exists(result.Journal));
    }

    [Fact]
    public async Task CapacityAndNewBranchRetireOnlyOwnedRecoveryFiles()
    {
        using var fixture = new Fixture(); var state = new SaveData(fixture.State); await state.LoadAsync(TestContext.Current.CancellationToken); var backend = Backend(fixture); var service = new DestinationMoveService(backend);
        Config.Current.Panels.DestinationMoveHistoryCapacity = 1;
        var target = Target(fixture);
        for (int i = 1; i <= 2; i++) Assert.NotNull(await service.TransferAsync(Path.Combine(fixture.Images, $"{i:000}.png"), Path.Combine(target, $"{i:000}.png"), true, TestContext.Current.CancellationToken));
        Assert.Equal(1, service.UndoCount); Assert.NotNull(await service.ReplayAsync(true, TestContext.Current.CancellationToken)); Assert.Equal(1, service.RedoCount);
        Assert.NotNull(await service.TransferAsync(Path.Combine(fixture.Images, "003.png"), Path.Combine(target, "003.png"), true, TestContext.Current.CancellationToken)); Assert.False(service.CanRedo);
        Config.Current.Panels.DestinationMoveHistoryCapacity = 0; await service.ApplyHistoryCapacityAsync(); Assert.Equal(0, service.UndoCount);
        Assert.Empty(Directory.EnumerateFiles(Path.Combine(fixture.State, "FileRecovery"))); Assert.Equal(2, Directory.GetFiles(target).Length);
    }

    [Fact]
    public async Task UndoRefusesExternalReplacementAndKeepsHistoryAndRecoveryMaterials()
    {
        using var f = new Fixture(); var state = new SaveData(f.State); await state.LoadAsync(TestContext.Current.CancellationToken);
        var service = new DestinationMoveService(Backend(f)); var source = Path.Combine(f.Images, "001.png"); var target = Path.Combine(Target(f), "001.png");
        var result = await service.TransferAsync(source, target, true, TestContext.Current.CancellationToken); Assert.NotNull(result);
        await File.WriteAllTextAsync(target, "externally replaced", TestContext.Current.CancellationToken);
        Assert.Null(await service.ReplayAsync(true, TestContext.Current.CancellationToken)); Assert.Equal(1, service.UndoCount); Assert.False(File.Exists(source));
        Assert.Equal("externally replaced", await File.ReadAllTextAsync(target, TestContext.Current.CancellationToken)); Assert.True(File.Exists(result.Journal));
    }

    [Fact]
    public async Task DirectoryAliasesCannotMoveOrOverwriteTheSamePhysicalFile()
    {
        using var fixture = new Fixture(); var state = new SaveData(fixture.State); await state.LoadAsync(TestContext.Current.CancellationToken); var backend = Backend(fixture);
        var alias = Path.Combine(fixture.Root, "alias"); Directory.CreateSymbolicLink(alias, fixture.Images);
        var source = Path.Combine(fixture.Images, "001.png"); var original = await File.ReadAllBytesAsync(source, TestContext.Current.CancellationToken);
        await Assert.ThrowsAsync<IOException>(() => backend.TransferAsync(new(source, Path.Combine(alias, "001.png"), true, true), TestContext.Current.CancellationToken));
        Assert.Equal(original, await File.ReadAllBytesAsync(source, TestContext.Current.CancellationToken));
        var link = Path.Combine(fixture.Images, "linked.png"); File.CreateSymbolicLink(link, source);
        await Assert.ThrowsAsync<IOException>(() => backend.TransferAsync(new(link, Path.Combine(Target(fixture), "linked.png"), true), TestContext.Current.CancellationToken));
        Assert.True(File.Exists(source));
    }

    [Fact]
    public async Task MasonryScrollDoesNotChooseClassificationTargetAndOldBookSelectionIsRejected()
    {
        using var fixture = new Fixture(); var state = new SaveData(fixture.State); await state.LoadAsync(TestContext.Current.CancellationToken);
        await using var operation = fixture.Operation(state); using var images = new BitmapFactory(new MagickImageDecoder()); Attach(operation, Backend(fixture), images, out _);
        await operation.OpenAsync(fixture.Images, TestContext.Current.CancellationToken); var book = operation.Book!; var page = book.Pages[0];
        await operation.SetBrowseModeAsync(BrowseLayoutMode.Masonry); await operation.ReportBrowsePositionAsync(book, book.Pages[2]); Assert.False(operation.CanFileAction);
        await operation.SelectFileActionPageAsync(book, page); Assert.True(operation.CanFileAction);
        await operation.ReportBrowsePositionAsync(book, book.Pages[3]); Assert.Same(page, operation.FileActionPage);
        await operation.ClassifyAsync(new DestinationFolder("", Target(fixture)), false, TestContext.Current.CancellationToken); Assert.False(File.Exists(Path.Combine(fixture.Images, page.EntryName))); Assert.False(operation.CanFileAction);
        await operation.OpenAsync(fixture.Zip, TestContext.Current.CancellationToken); await operation.SelectFileActionPageAsync(book, page); Assert.Null(operation.FileActionPage);
    }

    [Fact]
    public async Task DestinationJsonTypoUnknownFieldsAndUnlimitedManualFoldersRoundTrip()
    {
        using var fixture = new Fixture(); Directory.CreateDirectory(fixture.State);
        await File.WriteAllTextAsync(Path.Combine(fixture.State, "UserSetting.json"), """{"Config":{"System":{"DestinationFodlerCollection":[{"Name":"old","Path":"/old"}],"Future":42},"Panels":{"DestinationFolderSectionRatio":0.8}}} """, TestContext.Current.CancellationToken);
        var state = new SaveData(fixture.State); await state.LoadAsync(TestContext.Current.CancellationToken); await using var operation = fixture.Operation(state);
        Assert.False(Config.Current.System.IsFileWriteAccessEnabled); Assert.Single(Config.Current.System.DestinationFolderCollection);
        await operation.EditDestinationFoldersAsync(() => Config.Current.System.DestinationFolderCollection = new(Enumerable.Range(1, 12).Select(i => new DestinationFolder("" + i, "/" + i))), TestContext.Current.CancellationToken);
        await state.LoadAsync(TestContext.Current.CancellationToken); Assert.Equal(12, Config.Current.System.DestinationFolderCollection.Count); Assert.Equal(.8, Config.Current.Panels.DestinationFolderSectionRatio);
        var raw = JsonNode.Parse(await File.ReadAllTextAsync(Path.Combine(fixture.State, "UserSetting.json"), TestContext.Current.CancellationToken))!;
        Assert.Null(raw["Config"]!["System"]!["DestinationFodlerCollection"]); Assert.Equal(42, raw["Config"]!["System"]!["Future"]!.GetValue<int>());
        for (int i = 1; i <= 9; i++) { var name = "MoveToDestinationFolder" + i; Assert.Equal(i, state.GetDestinationParameter(name).Index); Assert.Equal("" + i, new CommandTable(operation).Definitions.Single(d => d.Name == name).Shortcut); }
    }

    [AvaloniaFact]
    public async Task FormalPanelAndNumericInputTextIsolationUseRealFileChain()
    {
        using var fixture = new Fixture(); var state = new SaveData(fixture.State); await state.LoadAsync(TestContext.Current.CancellationToken);
        var operation = fixture.Operation(state); var images = new BitmapFactory(new MagickImageDecoder()); Attach(operation, Backend(fixture), images, out var moves);
        Config.Current.System.DestinationFolderCollection = new([new("目标", Target(fixture))]);
        var model = new NeeView.MacOS.ViewModels.ReaderWorkspaceViewModel(operation, new CommandTable(operation), state);
        var window = new MainWindow(); window.Bind(model, images, new TestPlatform()); window.Show();
        try
        {
            Directory.CreateDirectory(Path.Combine(fixture.Images, "保留")); Directory.CreateDirectory(Path.Combine(fixture.Images, "待分类"));
            await window.OpenAsync(Path.Combine(fixture.Images, "001.png")); model.ShowPanel("DestinationFolderPanel"); Dispatcher.UIThread.RunJobs();
            Assert.NotNull(window.FindControl<DestinationFolderPanelView>("DestinationPanelView"));
            window.UpdateLayout(); using (var frame = window.CaptureRenderedFrame())
            {
                Assert.NotNull(frame); var phase = Environment.GetEnvironmentVariable("NEEVIEW_ACCEPTANCE_PHASE") ?? "p4-destination";
                frame.Save(Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, $"../../../../../acceptance/{phase}-destination-layout.png")), Avalonia.Media.Imaging.PngBitmapEncoderOptions.Default);
            }
            var address = window.FindControl<TextBox>("AddressBar")!; address.Focus(); window.KeyPress(Key.D1, Avalonia.Input.RawInputModifiers.None, PhysicalKey.Digit1, "1"); await Task.Delay(50, TestContext.Current.CancellationToken); Assert.Equal(0, moves.UndoCount);
            window.Viewer.Focus(); window.KeyPress(Key.D1, Avalonia.Input.RawInputModifiers.None, PhysicalKey.Digit1, "1");
            for (int i = 0; i < 100 && moves.UndoCount == 0; i++) { await Task.Delay(20, TestContext.Current.CancellationToken); Dispatcher.UIThread.RunJobs(); }
            Assert.Equal(1, moves.UndoCount); Assert.False(File.Exists(Path.Combine(fixture.Images, "001.png")));
            await window.PrepareShutdownAsync();
        }
        finally { await window.PrepareShutdownAsync(); window.Close(); }
    }
}
