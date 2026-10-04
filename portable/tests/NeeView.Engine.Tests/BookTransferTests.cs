using System.Text.Json.Nodes;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using NeeView;
using NeeView.Backends;
using NeeView.MacOS.ViewModels;
using NeeView.MacOS.Views;
namespace NeeView.Engine.Tests;

/// <summary>整书传输使用真实临时目录/归档和Profile；不写用户图片，不操作系统剪贴板。</summary>
public sealed class BookTransferTests
{
    private sealed class Platform : IPlatformService
    {
        public Task RevealAsync(string path, CancellationToken token = default) => Task.CompletedTask;
        public Task TrashAsync(string path, CancellationToken token = default) => throw new NotSupportedException();
    }
    private static CancellationToken Token => TestContext.Current.CancellationToken;
    private static TaskCompletionSource Signal() => new(TaskCreationOptions.RunContinuationsAsynchronously);
    private sealed class ObservedBackend(FileOperationBackend inner) : IFileOperationBackend, IBookTransferBackend
    {
        public Func<Task>? Before { get; init; }
        public Action? After { get; init; }
        public Exception? Failure { get; init; }
        public int Calls { get; private set; }
        public Task<bool> FileExistsAsync(string path, CancellationToken token) => inner.FileExistsAsync(path, token);
        public Task<FileTransferResult> TransferAsync(FileTransferRequest request, CancellationToken token) => inner.TransferAsync(request, token);
        public Task ReleaseAsync(FileTransferResult result) => inner.ReleaseAsync(result);
        public Task<IReadOnlyList<string>> RecoverAsync(CancellationToken token = default) => inner.RecoverAsync(token);
        public Task CreateDirectoryAsync(string parent, string name, CancellationToken token) => inner.CreateDirectoryAsync(parent, name, token);
        public Task<BookTransferPlan> PlanBookTransferAsync(string source, string folder, CancellationToken token) => inner.PlanBookTransferAsync(source, folder, token);
        public Task<bool?> WasBookMovedAsync(BookRenamePlan plan, CancellationToken token) => inner.WasBookMovedAsync(plan, token);
        public async Task<FileTransferResult> TransferBookAsync(BookTransferPlan plan, bool move, CancellationToken token)
        { Calls++; if (Before is not null) await Before(); if (Failure is not null) throw Failure; var result = await inner.TransferBookAsync(plan, move, token); After?.Invoke(); return result; }
    }
    private static async Task<SaveData> State(Fixture f) { var state = new SaveData(f.State); await state.LoadAsync(Token); return state; }
    private static FileOperationBackend Backend(Fixture f) => new(Path.Combine(f.State, "Recovery"));
    private static DestinationFolder Target(Fixture f) => new("目标目录", Directory.CreateDirectory(Path.Combine(f.Root, "目标目录")).FullName);
    private static void Attach(BookOperation op, BitmapFactory images, IFileOperationBackend backend)
    { op.AttachFileOperations(new(backend), backend, images); Config.Current.System.IsFileWriteAccessEnabled = true; }

    [Theory]
    [InlineData("directory", false)] [InlineData("directory", true)]
    [InlineData("archive", false)] [InlineData("archive", true)]
    [InlineData("playlist", false)] [InlineData("playlist", true)]
    public async Task RealRootsTransferAndMoveUnloadsWithoutNeighborNavigation(string kind, bool move)
    {
        using var f = new Fixture(); var state = await State(f); await using var op = f.Operation(state); using var images = new BitmapFactory(new MagickImageDecoder());
        var backend = Backend(f); Attach(op, images, backend); var target = Target(f);
        var path = kind == "directory" ? f.Images : f.Zip;
        if (kind == "playlist") { path = Path.Combine(f.Root, "user.nvpls"); await File.WriteAllBytesAsync(path, PlaylistSourceTools.CreateTemporarySource([Path.Combine(f.Images, "003.png")]), Token); }
        if (kind == "directory") { Directory.CreateDirectory(Path.Combine(path, "empty")); await File.WriteAllTextAsync(Path.Combine(path, "note.txt"), "metadata", Token); }
        await op.OpenAsync(path, Token); if (kind != "playlist") await op.JumpAsync(2); await op.SaveAsync(); var book = op.Book!;
        await state.RegisterBookmarkAsync(book, token: Token); await op.Bookshelf.SyncAsync(book, Token);
        Config.Current.System.DestinationFolderCollection.Add(target);
        state.SetCommandParameter(move ? "MoveBookToFolderAs" : "CopyBookToFolderAs", new MoveToFolderAsCommandParameter { Index = 1 });
        Config.Current.Panels.IsDestinationFolderCopyMode = move; // 整书命令不随分类面板模式。
        await new CommandTable(op).ExecuteAsync(move ? "MoveBookToFolderAs" : "CopyBookToFolderAs");
        var destination = Path.Combine(target.Path, Path.GetFileName(path)); Assert.True(File.Exists(destination) || Directory.Exists(destination));
        Assert.Equal(!move, File.Exists(path) || Directory.Exists(path)); Assert.Equal(0, op.DestinationMoves!.UndoCount);
        if (move)
        {
            Assert.Null(op.Book); Assert.True(book.Source.IsDisposed); Assert.Null(op.Frame); Assert.Equal(destination, state.LastBookPath);
            Assert.Contains(state.HistoryEntries, item => item.Path == destination); Assert.Contains(state.BookmarkRoot.Children!, item => item.Path == destination);
            Assert.DoesNotContain(op.Bookshelf.Items, item => item.Path == path);
            await op.RestoreLastAsync(Token); Assert.Equal(destination, op.Book!.Path);
        }
        else { Assert.Same(book, op.Book); Assert.False(book.Source.IsDisposed); Assert.Equal(path, state.LastBookPath); }
        if (kind == "directory") { Assert.True(Directory.Exists(Path.Combine(destination, "empty"))); Assert.Equal("metadata", await File.ReadAllTextAsync(Path.Combine(destination, "note.txt"), Token)); }
        else Assert.True(File.Exists(Path.Combine(f.Images, "003.png")));
        Assert.Empty(await backend.RecoverAsync(Token)); Assert.False(File.Exists(Path.Combine(f.State, ".book-rename-pending.json")));
    }

    [Theory]
    [InlineData(false, false)] [InlineData(false, true)] [InlineData(true, false)] [InlineData(true, true)]
    public async Task OverwriteRequiresWholeBookConfirmationAndReplacesOnlyConfirmedTarget(bool move, bool accept)
    {
        using var f = new Fixture(); var state = await State(f); await using var op = f.Operation(state); using var images = new BitmapFactory(new MagickImageDecoder());
        var backend = Backend(f); Attach(op, images, backend); var target = Target(f); var destination = Directory.CreateDirectory(Path.Combine(target.Path, Path.GetFileName(f.Images))).FullName;
        await File.WriteAllTextAsync(Path.Combine(destination, "keep.txt"), "old", Token); await op.OpenAsync(f.Images, Token); var book = op.Book;
        BookTransferPlan? confirmed = null; op.ConfirmBookOverwriteAsync = plan => { confirmed = plan; return Task.FromResult(accept); };
        await op.TransferBookToFolderAsync(target, move, Token); Assert.True(confirmed!.Target.IsDirectory); Assert.NotNull(confirmed.DestinationHash);
        if (!accept) { Assert.Same(book, op.Book); Assert.Equal("old", await File.ReadAllTextAsync(Path.Combine(destination, "keep.txt"), Token)); }
        else { Assert.False(File.Exists(Path.Combine(destination, "keep.txt"))); Assert.Equal(5, Directory.GetFiles(destination).Length); Assert.Equal(!move, Directory.Exists(f.Images)); }
        Assert.Empty(await backend.RecoverAsync(Token)); Assert.Equal(0, op.DestinationMoves!.UndoCount);
    }

    [Theory]
    [InlineData("same")] [InlineData("child")] [InlineData("ancestor")] [InlineData("alias")]
    public async Task BackendRejectsSelfContainedAndAliasedTargets(string kind)
    {
        using var f = new Fixture(); var backend = Backend(f); var folder = f.Root;
        if (kind == "child") folder = Directory.CreateDirectory(Path.Combine(f.Images, "child")).FullName;
        if (kind == "ancestor") { var child = Directory.CreateDirectory(Path.Combine(f.Images, Path.GetFileName(f.Images))).FullName; await Assert.ThrowsAsync<IOException>(() => backend.PlanBookTransferAsync(child, f.Root, Token)); return; }
        if (kind == "alias") { folder = Path.Combine(f.Root, "alias"); Directory.CreateSymbolicLink(folder, f.Root); }
        await Assert.ThrowsAsync<IOException>(() => backend.PlanBookTransferAsync(f.Images, folder, Token)); Assert.Equal(5, Directory.GetFiles(f.Images).Length);
    }

    [Theory]
    [InlineData("source")] [InlineData("target")]
    public async Task SourceOrTargetChangesAfterConfirmationNeverOverwrite(string change)
    {
        using var f = new Fixture(); var backend = Backend(f); var folder = Target(f).Path; var plan = await backend.PlanBookTransferAsync(f.Images, folder, Token);
        if (change == "source") await File.WriteAllTextAsync(Path.Combine(f.Images, "001.png"), "external", Token);
        else { Directory.CreateDirectory(plan.Destination); await File.WriteAllTextAsync(Path.Combine(plan.Destination, "external.txt"), "external", Token); }
        await Assert.ThrowsAsync<IOException>(() => backend.TransferBookAsync(plan, true, Token)); Assert.True(Directory.Exists(f.Images));
        if (change == "target") Assert.Equal("external", await File.ReadAllTextAsync(Path.Combine(plan.Destination, "external.txt"), Token));
        Assert.Empty(await backend.RecoverAsync(Token));
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task TreeAndTargetLinksAreExplicitlyUnsupported(bool targetLink)
    {
        using var f = new Fixture(); var backend = Backend(f); var folder = Target(f).Path;
        if (targetLink) Directory.CreateSymbolicLink(Path.Combine(folder, Path.GetFileName(f.Images)), f.Images);
        else File.CreateSymbolicLink(Path.Combine(f.Images, "linked.png"), Path.Combine(f.Images, "001.png"));
        await Assert.ThrowsAsync<IOException>(() => backend.PlanBookTransferAsync(f.Images, folder, Token)); Assert.True(File.Exists(Path.Combine(f.Images, "001.png")));
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task FailureOrEarlyCancelReopensOriginalPageSearchAndLock(bool cancel)
    {
        using var f = new Fixture(); var state = await State(f); await using var op = f.Operation(state); using var images = new BitmapFactory(new MagickImageDecoder());
        var backend = new ObservedBackend(Backend(f)) { Failure = cancel ? new OperationCanceledException() : new IOException("offline") };
        Attach(op, images, backend); await op.OpenAsync(f.Images, Token); await op.SearchPagesAsync("001 /or 003", op.Book, Token); await op.JumpAsync(1); op.SetBookLock(true); var old = op.Book!;
        await op.TransferBookToFolderAsync(Target(f), true, Token); Assert.Equal(1, backend.Calls); Assert.True(old.Source.IsDisposed);
        Assert.Equal(f.Images, op.Book!.Path); Assert.Equal("003.png", op.Book.CurrentPage!.EntryName); Assert.Equal("001 /or 003", op.Book.Pages.SearchKeyword); Assert.True(op.IsBookLocked);
        Assert.False(File.Exists(Path.Combine(f.State, ".book-rename-pending.json"))); Assert.Equal(0, op.DestinationMoves!.UndoCount);
    }

    [Fact]
    public async Task CopyDoesNotRequireWriteAccessButMoveDoes()
    {
        using var f = new Fixture(); var state = await State(f); await using var op = f.Operation(state); using var images = new BitmapFactory(new MagickImageDecoder()); var backend = new ObservedBackend(Backend(f));
        Attach(op, images, backend); await op.OpenAsync(f.Zip, Token); Config.Current.System.IsFileWriteAccessEnabled = false;
        Assert.True(op.CanCopyBookToFolder); Assert.False(op.CanMoveBookToFolder); await op.TransferBookToFolderAsync(Target(f), true, Token); Assert.Equal(0, backend.Calls);
        await op.TransferBookToFolderAsync(Target(f), false, Token); Assert.Equal(1, backend.Calls); Assert.True(File.Exists(f.Zip));
    }

    [Fact]
    public async Task ClosingWaitsForAuthorizedMoveAndLateCancelPreservesRealResult()
    {
        using var f = new Fixture(); var state = await State(f); var op = f.Operation(state); using var images = new BitmapFactory(new MagickImageDecoder()); var entered = Signal(); var release = Signal(); using var cancel = new CancellationTokenSource();
        var inner = Backend(f); var backend = new ObservedBackend(inner) { Before = async () => { entered.SetResult(); await release.Task; }, After = cancel.Cancel };
        Attach(op, images, backend); await op.OpenAsync(f.Images, Token); var target = Target(f); var action = op.TransferBookToFolderAsync(target, true, cancel.Token); await entered.Task.WaitAsync(TimeSpan.FromSeconds(5), Token);
        try
        {
            Assert.Null(op.Book); Assert.False(op.CanUnload); Assert.False(op.CanCopyBookToFolder); var close = op.DisposeAsync().AsTask(); Assert.False(close.IsCompleted);
            release.SetResult(); await action; await close; Assert.False(Directory.Exists(f.Images)); Assert.Equal(Path.Combine(target.Path, Path.GetFileName(f.Images)), state.LastBookPath); Assert.Empty(await inner.RecoverAsync(Token));
        }
        finally { release.TrySetResult(); await op.DisposeAsync(); }
    }

    [Fact]
    public async Task NewOpenRequestWinsOverAuthorizedMoveCompletion()
    {
        using var f = new Fixture(); var state = await State(f); await using var op = f.Operation(state); using var images = new BitmapFactory(new MagickImageDecoder()); var entered = Signal(); var release = Signal();
        var backend = new ObservedBackend(Backend(f)) { Before = async () => { entered.SetResult(); await release.Task; } }; Attach(op, images, backend); await op.OpenAsync(f.Images, Token);
        var action = op.TransferBookToFolderAsync(Target(f), true, Token); await entered.Task.WaitAsync(TimeSpan.FromSeconds(5), Token);
        try { var open = op.OpenAsync(f.Zip, Token); release.SetResult(); await Task.WhenAll(action, open); Assert.Equal(f.Zip, op.Book!.Path); Assert.Null(op.Error); }
        finally { release.TrySetResult(); }
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task PendingConfirmationCanBeClosedOrSupersededWithoutTransfer(bool close)
    {
        using var f = new Fixture(); var state = await State(f); var op = f.Operation(state); using var images = new BitmapFactory(new MagickImageDecoder()); var backend = new ObservedBackend(Backend(f)); Attach(op, images, backend);
        var target = Target(f); Directory.CreateDirectory(Path.Combine(target.Path, Path.GetFileName(f.Images))); var entered = Signal(); var confirm = new TaskCompletionSource<bool>();
        op.ConfirmBookOverwriteAsync = _ => { entered.SetResult(); return confirm.Task; }; await op.OpenAsync(f.Images, Token); var action = op.TransferBookToFolderAsync(target, true, Token);
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5), Token); if (close) await op.DisposeAsync(); else await op.OpenAsync(f.Zip, Token);
            confirm.SetResult(true); await action; Assert.Equal(0, backend.Calls); Assert.True(Directory.Exists(f.Images)); if (!close) Assert.Equal(f.Zip, op.Book!.Path);
        }
        finally { confirm.TrySetResult(false); await op.DisposeAsync(); }
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task InterruptedDirectoryInstallationRestoresSourceAndExistingTarget(bool overwrite)
    {
        using var f = new Fixture(); var backend = Backend(f); var target = Target(f); var destination = Path.Combine(target.Path, Path.GetFileName(f.Images));
        if (overwrite) { Directory.CreateDirectory(destination); await File.WriteAllTextAsync(Path.Combine(destination, "old.txt"), "old", Token); }
        var result = await backend.TransferBookAsync(await backend.PlanBookTransferAsync(f.Images, target.Path, Token), true, Token);
        var node = JsonNode.Parse(await File.ReadAllTextAsync(result.Journal, Token))!; node["Completed"] = false;
        Directory.Move(destination, node["SourceStage"]!.GetValue<string>()); if (overwrite) Directory.Move(result.Backup!, node["DestinationStage"]!.GetValue<string>());
        await File.WriteAllTextAsync(result.Journal, node.ToJsonString(), Token); Assert.Empty(await backend.RecoverAsync(Token)); Assert.Equal(5, Directory.GetFiles(f.Images).Length);
        if (overwrite) Assert.Equal("old", await File.ReadAllTextAsync(Path.Combine(destination, "old.txt"), Token)); else Assert.False(Directory.Exists(destination));
    }

    [Fact]
    public async Task MoveMarkerRecoversPathsAfterProcessInterruptionAndRejectsChangedContent()
    {
        using var f = new Fixture(); var state = await State(f); await using var op = f.Operation(state); using var images = new BitmapFactory(new MagickImageDecoder()); var backend = Backend(f); Attach(op, images, backend);
        await op.OpenAsync(f.Images, Token); await op.SaveAsync(); var plan = await backend.PlanBookTransferAsync(f.Images, Target(f).Path, Token);
        var marker = new BookRenamePlan(plan.Target, plan.Destination, false, false, true, plan.ContentHash); await state.PrepareBookRenameAsync(marker, Token);
        await op.UnloadAsync(Token); var result = await backend.TransferBookAsync(plan, true, Token); await backend.ReleaseAsync(result);
        var restarted = await State(f); Assert.Empty(await restarted.RecoverBookRenameAsync(backend, Token)); Assert.Contains(restarted.HistoryEntries, item => item.Path == plan.Destination);
        Assert.False(File.Exists(Path.Combine(f.State, ".book-rename-pending.json")));
        await File.WriteAllTextAsync(Path.Combine(plan.Destination, "001.png"), "external", Token); Assert.Null(await backend.WasRenamedAsync(marker, Token));
    }

    [Theory]
    [InlineData("target")] [InlineData("stage")] [InlineData("backup-link")]
    public async Task AmbiguousDirectoryRecoveryPreservesChangedDataAndJournal(string change)
    {
        using var f = new Fixture(); var backend = Backend(f); var folder = Target(f).Path; var destination = Directory.CreateDirectory(Path.Combine(folder, Path.GetFileName(f.Images))).FullName;
        await File.WriteAllTextAsync(Path.Combine(destination, "old.txt"), "old", Token);
        var result = await backend.TransferBookAsync(await backend.PlanBookTransferAsync(f.Images, folder, Token), true, Token);
        var node = JsonNode.Parse(await File.ReadAllTextAsync(result.Journal, Token))!; node["Completed"] = false;
        if (change == "target") await File.WriteAllTextAsync(Path.Combine(destination, "001.png"), "external", Token);
        else if (change == "stage") { Directory.CreateDirectory(node["SourceStage"]!.GetValue<string>()); await File.WriteAllTextAsync(Path.Combine(node["SourceStage"]!.GetValue<string>(), "external.txt"), "external", Token); }
        else { Directory.Delete(result.Backup!, true); Directory.CreateSymbolicLink(result.Backup!, f.Root); }
        await File.WriteAllTextAsync(result.Journal, node.ToJsonString(), Token); Assert.Single(await backend.RecoverAsync(Token)); Assert.True(File.Exists(result.Journal));
        if (change == "target") Assert.Equal("external", await File.ReadAllTextAsync(Path.Combine(destination, "001.png"), Token));
        else if (change == "stage") Assert.Equal("external", await File.ReadAllTextAsync(Path.Combine(node["SourceStage"]!.GetValue<string>(), "external.txt"), Token));
        Assert.True(File.Exists(f.Zip)); Assert.False(Directory.Exists(f.Images));
    }

    [Theory]
    [InlineData("profile")] [InlineData("profile-alias")] [InlineData("logical")]
    public async Task ProtectedAndLogicalBookTargetsAreNeverTransferred(string kind)
    {
        using var f = new Fixture(); var state = await State(f); await using var op = f.Operation(state); using var images = new BitmapFactory(new MagickImageDecoder()); var backend = new ObservedBackend(Backend(f)); Attach(op, images, backend);
        if (kind == "logical")
        {
            var nested = Directory.CreateDirectory(Path.Combine(f.Root, "nested", "sub")).FullName;
            File.Copy(Path.Combine(f.Images, "001.png"), Path.Combine(nested, "001.png")); var zip = Path.Combine(f.Root, "nested.cbz");
            System.IO.Compression.ZipFile.CreateFromDirectory(Path.GetDirectoryName(nested)!, zip);
            await op.OpenAsync(zip + "/sub", Token); Assert.NotNull(op.Book); Assert.False(op.CanMoveBookToFolder); Assert.False(op.CanCopyBookToFolder);
            await op.TransferBookToFolderAsync(Target(f), true, Token); Assert.Equal(0, backend.Calls); return;
        }
        var folder = f.State;
        if (kind == "profile-alias") { folder = Path.Combine(f.Root, "profile-alias"); Directory.CreateSymbolicLink(folder, f.State); }
        await op.OpenAsync(f.Images, Token); var book = op.Book;
        await op.TransferBookToFolderAsync(new("protected", folder), true, Token); Assert.Equal(0, backend.Calls); Assert.Same(book, op.Book); Assert.True(Directory.Exists(f.Images)); Assert.NotNull(op.Error);
    }

    [Fact]
    public async Task StateSaveFailureAfterMoveKeepsRealResultAndCanRetry()
    {
        using var f = new Fixture(); var state = await State(f); await using var op = f.Operation(state); using var images = new BitmapFactory(new MagickImageDecoder());
        var blocker = Path.Combine(f.State, "UserSetting.json.tmp"); var backend = new ObservedBackend(Backend(f)) { After = () => Directory.CreateDirectory(blocker) }; Attach(op, images, backend);
        await op.OpenAsync(f.Images, Token); var target = Target(f); var destination = Path.Combine(target.Path, Path.GetFileName(f.Images));
        try
        {
            await op.TransferBookToFolderAsync(target, true, Token); Assert.Null(op.Book); Assert.False(Directory.Exists(f.Images)); Assert.True(Directory.Exists(destination)); Assert.Contains("书籍已移动", op.Error);
            Assert.True(File.Exists(Path.Combine(f.State, ".book-rename-pending.json"))); Directory.Delete(blocker); await op.SaveAsync();
            Assert.Equal(destination, state.LastBookPath); Assert.False(File.Exists(Path.Combine(f.State, ".book-rename-pending.json")));
        }
        finally { if (Directory.Exists(blocker)) Directory.Delete(blocker); }
    }

    [Fact]
    public async Task EarlyCancelledTransferLeavesNoChangesOrRecoveryMaterials()
    {
        using var f = new Fixture(); var backend = Backend(f); var target = Target(f); var plan = await backend.PlanBookTransferAsync(f.Images, target.Path, Token); using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => backend.TransferBookAsync(plan, true, cancelled.Token)); Assert.Equal(5, Directory.GetFiles(f.Images).Length); Assert.False(Directory.Exists(plan.Destination)); Assert.Empty(await backend.RecoverAsync(Token));
    }

    [Fact]
    public async Task CopyPreservesFileAndDirectoryModificationTimesAndKeepsUndoRedoStacks()
    {
        using var f = new Fixture(); var state = await State(f); await using var op = f.Operation(state); using var images = new BitmapFactory(new MagickImageDecoder()); var backend = Backend(f); Attach(op, images, backend); var target = Target(f);
        await op.OpenAsync(f.Images, Token); await op.ClassifyAsync(target, copy: false, Token); await op.ReplayDestinationMoveAsync(true, Token);
        Assert.Equal(0, op.DestinationMoves!.UndoCount); Assert.Equal(1, op.DestinationMoves.RedoCount); var rootTime = Directory.GetLastWriteTimeUtc(f.Images);
        var file = Path.Combine(f.Images, "002.png"); var stamp = new DateTime(2020, 1, 2, 3, 4, 5, DateTimeKind.Utc); File.SetLastWriteTimeUtc(file, stamp);
        await op.TransferBookToFolderAsync(target, false, Token); Assert.Equal(1, op.DestinationMoves.RedoCount); Assert.Equal(0, op.DestinationMoves.UndoCount);
        var destination = Path.Combine(target.Path, Path.GetFileName(f.Images)); Assert.Equal(stamp, File.GetLastWriteTimeUtc(Path.Combine(destination, "002.png"))); Assert.Equal(rootTime, Directory.GetLastWriteTimeUtc(destination));
    }

    [Fact]
    public async Task UnfinishedEntityJournalCannotCommitJsonPathMarkerEarly()
    {
        using var f = new Fixture(); var state = await State(f); var backend = Backend(f); var plan = await backend.PlanBookTransferAsync(f.Images, Target(f).Path, Token);
        var marker = new BookRenamePlan(plan.Target, plan.Destination, false, false, true, plan.ContentHash);
        await state.PrepareBookRenameAsync(marker, Token); var result = await backend.TransferBookAsync(plan, true, Token);
        var node = JsonNode.Parse(await File.ReadAllTextAsync(result.Journal, Token))!; node["Completed"] = false;
        await File.WriteAllTextAsync(result.Journal, node.ToJsonString(), Token);
        Assert.Null(await backend.WasBookMovedAsync(marker, Token)); Assert.Single(await state.RecoverBookRenameAsync(backend, Token)); Assert.True(File.Exists(Path.Combine(f.State, ".book-rename-pending.json")));
        Assert.Empty(await backend.RecoverAsync(Token)); Assert.True(Directory.Exists(f.Images)); Assert.False(Directory.Exists(plan.Destination));
        Assert.False(await backend.WasBookMovedAsync(marker, Token)); Assert.Empty(await state.RecoverBookRenameAsync(backend, Token)); Assert.False(File.Exists(Path.Combine(f.State, ".book-rename-pending.json")));
    }

    [AvaloniaFact]
    public async Task FormalMenusKeepOriginalIndexAndWholeDirectoryConfirmation()
    {
        using var f = new Fixture(); var state = await State(f); var op = f.Operation(state); var images = new BitmapFactory(new MagickImageDecoder()); var backend = Backend(f); Attach(op, images, backend);
        var target = Target(f); Config.Current.System.DestinationFolderCollection.Add(target); state.SetCommandParameter("CopyBookToFolderAs", new MoveToFolderAsCommandParameter { Index = 1 });
        var destination = Directory.CreateDirectory(Path.Combine(target.Path, Path.GetFileName(f.Images))).FullName; await File.WriteAllTextAsync(Path.Combine(destination, "old.txt"), "old", Token);
        var window = new MainWindow(); window.Bind(new ReaderWorkspaceViewModel(op, new(op), state), images, new Platform()); window.Show();
        try
        {
            await window.OpenAsync(f.Images); Assert.True(window.IsCommandAvailable("CopyBookToFolderAs")); Assert.True(window.IsCommandAvailable("MoveBookToFolderAs")); Assert.False(window.IsCommandAvailable("CutBook"));
            var action = window.ExecuteAsync("CopyBookToFolderAs"); await WaitAsync(() => window.OwnedWindows.Any()); var dialog = Assert.Single(window.OwnedWindows); Dispatcher.UIThread.RunJobs(); dialog.UpdateLayout();
            using (var bitmap = new RenderTargetBitmap(new PixelSize((int)Math.Ceiling(dialog.Bounds.Width), (int)Math.Ceiling(dialog.Bounds.Height))))
            { bitmap.Render(dialog); bitmap.Save(Output("book-overwrite.png"), PngBitmapEncoderOptions.Default); }
            dialog.Close(false); await action; Assert.True(File.Exists(Path.Combine(destination, "old.txt")));
            action = window.ExecuteAsync("CopyBookToFolderAs"); await WaitAsync(() => window.OwnedWindows.Any()); Assert.Single(window.OwnedWindows).Close(true); await action;
            Assert.NotNull(op.Book); Assert.Equal(5, Directory.GetFiles(destination).Length); Assert.Equal(0, op.DestinationMoves!.UndoCount);
        }
        finally { await window.PrepareShutdownAsync(); window.Close(); }
    }
    private static async Task WaitAsync(Func<bool> ready)
    { var limit = DateTime.UtcNow.AddSeconds(5); while (!ready() && DateTime.UtcNow < limit) { Dispatcher.UIThread.RunJobs(); await Task.Delay(10, Token); } Assert.True(ready()); }
    private static string Output(string name) => Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "acceptance", (Environment.GetEnvironmentVariable("NEEVIEW_ACCEPTANCE_PHASE") ?? "p4-book-transfer") + "-" + name);
}
