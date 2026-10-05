using System.IO.Compression;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using NeeView;
using NeeView.Backends;
using NeeView.MacOS.ViewModels;
using NeeView.MacOS.Views;
namespace NeeView.Engine.Tests;

/// <summary>目录页面沿原复制链迁移，只写自建临时目录/Profile，不访问系统剪贴板。</summary>
public sealed class DirectoryCopyTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;
    private static TaskCompletionSource Signal() => new(TaskCreationOptions.RunContinuationsAsynchronously);
    private sealed class Clipboard : IFileClipboard
    {
        public FileClipboardContent Content { get; private set; } = new([], []);
        public int Writes { get; private set; }
        public bool HasFileContent => Content.Files.Count > 0 || Content.QueryPaths.Count > 0;
        public Task WriteAsync(FileClipboardContent content, CancellationToken token) { token.ThrowIfCancellationRequested(); Content = content; Writes++; return Task.CompletedTask; }
        public Task<FileClipboardContent> ReadAsync(CancellationToken token) => Task.FromResult(Content);
    }
    private sealed class Platform : IPlatformService
    {
        public Task RevealAsync(string path, CancellationToken token = default) => Task.CompletedTask;
        public Task TrashAsync(string path, CancellationToken token = default) => throw new NotSupportedException();
    }
    private sealed class ObservedBackend(FileOperationBackend inner) : IFileOperationBackend, IBookTransferBackend
    {
        public Func<Task>? Before { get; init; }
        public Func<Task>? After { get; init; }
        public Exception? Failure { get; init; }
        public int DirectoryCalls { get; private set; }
        public FileTransferResult? LastResult { get; private set; }
        public List<string> Sources { get; } = [];
        public Task<bool> FileExistsAsync(string path, CancellationToken token) => inner.FileExistsAsync(path, token);
        public async Task<FileTransferResult> TransferAsync(FileTransferRequest request, CancellationToken token)
        { var result = await inner.TransferAsync(request, token); Sources.Add(request.Source); return result; }
        public Task ReleaseAsync(FileTransferResult result) => inner.ReleaseAsync(result);
        public Task<IReadOnlyList<string>> RecoverAsync(CancellationToken token = default) => inner.RecoverAsync(token);
        public Task CreateDirectoryAsync(string parent, string name, CancellationToken token) => inner.CreateDirectoryAsync(parent, name, token);
        public Task<BookTransferPlan> PlanBookTransferAsync(string source, string folder, CancellationToken token) => inner.PlanBookTransferAsync(source, folder, token);
        public Task<bool?> WasBookMovedAsync(BookRenamePlan plan, CancellationToken token) => inner.WasBookMovedAsync(plan, token);
        public async Task<FileTransferResult> TransferBookAsync(BookTransferPlan plan, bool move, CancellationToken token)
        { Assert.False(move); DirectoryCalls++; if (Before is not null) await Before(); if (Failure is not null) throw Failure; var result = await inner.TransferBookAsync(plan, move, token); LastResult = result; Sources.Add(plan.Target.Path); if (After is not null) await After(); return result; }
    }
    private static async Task<SaveData> State(Fixture f)
    { var state = new SaveData(f.State); await state.LoadAsync(Token); Config.Current.System.BookPageCollectMode = BookPageCollectMode.ImageAndBook; return state; }
    private static FileOperationBackend Backend(Fixture f) => new(Path.Combine(f.State, "Recovery"));
    private static DestinationMoveService Attach(BookOperation op, BitmapFactory images, IFileOperationBackend backend)
    { var moves = new DestinationMoveService(backend); op.AttachFileOperations(moves, backend, images); return moves; }
    private static DestinationFolder Target(Fixture f) => new("目标", Directory.CreateDirectory(Path.Combine(f.Root, "目标")).FullName);
    private static string Child(Fixture f, string name = "000目录")
    {
        var path = Directory.CreateDirectory(Path.Combine(f.Images, name)).FullName;
        Directory.CreateDirectory(Path.Combine(path, "空目录")); File.WriteAllText(Path.Combine(path, "说明.txt"), "非图片也保留");
        File.Copy(Path.Combine(f.Images, "001.png"), Path.Combine(path, "图片.png")); return path;
    }
    private static async Task Select(BookOperation op, string name)
    { await op.JumpAsync(op.Book!.Pages.FindIndex(page => page.EntryName == name)); Assert.Equal(name, op.Book.CurrentPage!.EntryName); }

    [Theory]
    [InlineData(ArchivePolicy.None)] [InlineData(ArchivePolicy.SendArchiveFile)]
    [InlineData(ArchivePolicy.SendArchivePath)] [InlineData(ArchivePolicy.SendExtractFile)]
    public async Task PhysicalDirectoryClipboardAlwaysUsesOriginalPathAndSurvivesClosing(ArchivePolicy policy)
    {
        using var f = new Fixture(); var child = Child(f); var state = await State(f); var clipboard = new Clipboard(); var op = f.Operation(state); op.AttachFileClipboard(clipboard);
        await using var realizer = new ArchiveEntryRealizer(Path.Combine(f.Root, "temp")); op.AttachArchiveEntryRealizer(realizer);
        Config.Current.System.ArchiveCopyPolicy = policy; Config.Current.System.TextCopyPolicy = TextCopyPolicy.CopyFilePath;
        await op.OpenAsync(f.Images, Token); await Select(op, Path.GetFileName(child)); var page = op.Book!.CurrentPage; var position = op.Position;
        Assert.True(page!.ArchiveEntry.CanRealize()); Assert.True(op.CanCopyFiles(MultiPagePolicy.Once)); Assert.False(op.CanFileAction);
        await new CommandTable(op).ExecuteAsync("CopyFile"); Assert.Null(op.Error); Assert.Equal(child, Assert.Single(clipboard.Content.Files));
        Assert.Equal(child, Assert.Single(clipboard.Content.QueryPaths)); Assert.Equal(child, clipboard.Content.Text); Assert.Same(page, op.Book.CurrentPage); Assert.Equal(position, op.Position);
        await op.OpenAsync(f.Zip, Token); await op.DisposeAsync(); await realizer.DisposeAsync(); Assert.True(File.Exists(Path.Combine(child, "说明.txt")));
    }

    [Theory]
    [InlineData(ArchivePolicy.None)] [InlineData(ArchivePolicy.SendArchiveFile)]
    [InlineData(ArchivePolicy.SendArchivePath)] [InlineData(ArchivePolicy.SendExtractFile)]
    public async Task FixedDirectoryCopyIncludesWholeTreeWithoutWriteAccessOrClassificationHistory(ArchivePolicy policy)
    {
        using var f = new Fixture(); var child = Child(f); var state = await State(f); await using var op = f.Operation(state); using var images = new BitmapFactory(new MagickImageDecoder());
        var backend = Backend(f); var moves = Attach(op, images, backend); var target = Target(f); Config.Current.System.ArchiveCopyPolicy = policy;
        await op.OpenAsync(f.Images, Token); await Select(op, Path.GetFileName(child)); var book = op.Book; var page = book!.CurrentPage; var position = op.Position;
        Assert.False(Config.Current.System.IsFileWriteAccessEnabled); Assert.True(op.CanCopyToFolder()); Assert.False(op.CanFileAction);
        Config.Current.Panels.IsDestinationFolderCopyMode = false; Config.Current.System.DestinationFolderCollection.Add(target);
        state.SetCommandParameter("CopyToFolderAs", new CopyToFolderAsCommandParameter { Index = 1 }); await op.CopyToFolderAsync(target, token: Token);
        var destination = Path.Combine(target.Path, Path.GetFileName(child)); Assert.Null(op.Error);
        Assert.Equal("非图片也保留", await File.ReadAllTextAsync(Path.Combine(destination, "说明.txt"), Token)); Assert.True(Directory.Exists(Path.Combine(destination, "空目录")));
        Assert.Equal(await File.ReadAllBytesAsync(Path.Combine(child, "图片.png"), Token), await File.ReadAllBytesAsync(Path.Combine(destination, "图片.png"), Token));
        Assert.Same(book, op.Book); Assert.Same(page, book.CurrentPage); Assert.Equal(position, op.Position); Assert.Equal(0, moves.UndoCount); Assert.Equal(0, moves.RedoCount); Assert.Empty(await backend.RecoverAsync(Token));
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task DirectoryOverwriteRequiresDedicatedConfirmationAndReplacesEntireTree(bool accept)
    {
        using var f = new Fixture(); var child = Child(f); var state = await State(f); await using var op = f.Operation(state); using var images = new BitmapFactory(new MagickImageDecoder());
        var backend = Backend(f); var moves = Attach(op, images, backend); var target = Target(f); var destination = Directory.CreateDirectory(Path.Combine(target.Path, Path.GetFileName(child))).FullName;
        await File.WriteAllTextAsync(Path.Combine(destination, "old.txt"), "保留", Token); await op.OpenAsync(f.Images, Token); await Select(op, Path.GetFileName(child));
        moves.ConfirmOverwriteAsync = _ => throw new InvalidOperationException("不能调用图片覆盖确认"); int calls = 0;
        moves.ConfirmDirectoryOverwriteAsync = plan => { calls++; Assert.True(plan.Target.IsDirectory); Assert.NotNull(plan.DestinationHash); return Task.FromResult(accept); };
        await op.CopyToFolderAsync(target, token: Token); Assert.Equal(1, calls); Assert.Equal(!accept, File.Exists(Path.Combine(destination, "old.txt")));
        Assert.Equal(accept, File.Exists(Path.Combine(destination, "图片.png"))); Assert.True(File.Exists(Path.Combine(child, "图片.png"))); Assert.Empty(await backend.RecoverAsync(Token)); Assert.Equal(0, moves.UndoCount);
    }

    [Theory]
    [InlineData("source")] [InlineData("target")]
    public async Task ChangedDirectoryAfterConfirmationIsNotCopiedOrOverwritten(string changed)
    {
        using var f = new Fixture(); var child = Child(f); var state = await State(f); await using var op = f.Operation(state); using var images = new BitmapFactory(new MagickImageDecoder());
        var backend = Backend(f); var moves = Attach(op, images, backend); var target = Target(f); var destination = Directory.CreateDirectory(Path.Combine(target.Path, Path.GetFileName(child))).FullName;
        await File.WriteAllTextAsync(Path.Combine(destination, "old.txt"), "保留", Token); await op.OpenAsync(f.Images, Token); await Select(op, Path.GetFileName(child)); var book = op.Book;
        moves.ConfirmDirectoryOverwriteAsync = async _ => { await File.WriteAllTextAsync(Path.Combine(changed == "source" ? child : destination, "external.txt"), "外部修改", Token); return true; };
        await op.CopyToFolderAsync(target, token: Token); Assert.NotNull(op.Error); Assert.Same(book, op.Book); Assert.True(File.Exists(Path.Combine(destination, "old.txt")));
        Assert.False(File.Exists(Path.Combine(destination, "图片.png"))); Assert.Empty(await backend.RecoverAsync(Token));
    }

    [Theory]
    [InlineData("same")] [InlineData("child")] [InlineData("profile")]
    [InlineData("profile-alias")] [InlineData("tree-link")] [InlineData("type-change")]
    public async Task UnsafeOrChangedDirectoryTargetsAreRejectedBeforeTransfer(string kind)
    {
        using var f = new Fixture(); var child = Child(f); var state = await State(f); await using var op = f.Operation(state); using var images = new BitmapFactory(new MagickImageDecoder());
        var backend = new ObservedBackend(Backend(f)); Attach(op, images, backend); await op.OpenAsync(f.Images, Token); await Select(op, Path.GetFileName(child));
        var folder = Target(f).Path;
        if (kind == "same") folder = f.Images;
        if (kind == "child") folder = Directory.CreateDirectory(Path.Combine(child, "inside")).FullName;
        if (kind == "profile") folder = f.State;
        if (kind == "profile-alias") { folder = Path.Combine(f.Root, "alias"); Directory.CreateSymbolicLink(folder, f.State); }
        if (kind == "tree-link") File.CreateSymbolicLink(Path.Combine(child, "link.png"), Path.Combine(f.Images, "001.png"));
        if (kind == "type-change") { Directory.Delete(child, true); await File.WriteAllTextAsync(child, "变化", Token); }
        await op.CopyToFolderAsync(new("目标", folder), token: Token); Assert.Equal(0, backend.DirectoryCalls); Assert.NotNull(op.Error); Assert.True(File.Exists(Path.Combine(f.Images, "001.png")));
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task SwitchOrCloseCancelsPendingDirectoryConfirmation(bool close)
    {
        using var f = new Fixture(); var child = Child(f); var state = await State(f); var op = f.Operation(state); using var images = new BitmapFactory(new MagickImageDecoder());
        var backend = new ObservedBackend(Backend(f)); var moves = Attach(op, images, backend); var target = Target(f); Directory.CreateDirectory(Path.Combine(target.Path, Path.GetFileName(child)));
        var entered = Signal(); var confirm = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        moves.ConfirmDirectoryOverwriteAsync = _ => { entered.SetResult(); return confirm.Task; }; await op.OpenAsync(f.Images, Token); await Select(op, Path.GetFileName(child));
        var action = op.CopyToFolderAsync(target, token: Token);
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5), Token); Assert.True(moves.IsBusy);
            var change = close ? op.DisposeAsync().AsTask() : op.OpenAsync(f.Zip, Token); await Task.WhenAll(action, change).WaitAsync(TimeSpan.FromSeconds(5), Token);
            Assert.Equal(0, backend.DirectoryCalls); Assert.False(moves.IsBusy); if (!close) Assert.Equal(f.Zip, op.Book!.Path);
        }
        finally { confirm.TrySetResult(false); await op.DisposeAsync(); }
    }

    [Fact]
    public async Task ClosingWaitsForAuthorizedCopyAndLateCancellationDoesNotHideSuccessfulFiles()
    {
        using var f = new Fixture(); var child = Child(f); var state = await State(f); var op = f.Operation(state); using var images = new BitmapFactory(new MagickImageDecoder());
        using var cancel = new CancellationTokenSource(); var entered = Signal(); var release = Signal();
        var backend = new ObservedBackend(Backend(f)) { After = async () => { entered.SetResult(); await release.Task; cancel.Cancel(); } };
        Attach(op, images, backend); var target = Target(f); await op.OpenAsync(f.Images, Token); await Select(op, Path.GetFileName(child));
        var action = op.CopyToFolderAsync(target, token: cancel.Token);
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5), Token); var close = op.DisposeAsync().AsTask(); Assert.False(close.IsCompleted); release.SetResult(); await action; await close;
            Assert.True(File.Exists(Path.Combine(target.Path, Path.GetFileName(child), "图片.png"))); Assert.True(File.Exists(Path.Combine(child, "图片.png"))); Assert.Equal(0, op.DestinationMoves!.UndoCount);
        }
        finally { release.TrySetResult(); await op.DisposeAsync(); }
    }

    [Fact]
    public async Task BackendFailureKeepsCurrentPageAndSourceTree()
    {
        using var f = new Fixture(); var child = Child(f); var state = await State(f); await using var op = f.Operation(state); using var images = new BitmapFactory(new MagickImageDecoder());
        var backend = new ObservedBackend(Backend(f)) { Failure = new IOException("NAS offline") }; Attach(op, images, backend); var target = Target(f);
        await op.OpenAsync(f.Images, Token); await Select(op, Path.GetFileName(child)); var book = op.Book; var page = book!.CurrentPage;
        await op.CopyToFolderAsync(target, token: Token); Assert.Contains("NAS offline", op.Error); Assert.Same(book, op.Book); Assert.Same(page, book.CurrentPage); Assert.True(Directory.Exists(child)); Assert.Empty(Directory.GetFileSystemEntries(target.Path));
    }

    [Fact]
    public async Task CopyInsideCurrentBookReloadsRealDirectoryTypeAndPreservesPageSearchAndLock()
    {
        using var f = new Fixture(); var child = Child(f); var container = Directory.CreateDirectory(Path.Combine(f.Images, "目标目录")).FullName; var state = await State(f);
        await using var op = f.Operation(state); using var images = new BitmapFactory(new MagickImageDecoder()); var backend = new ObservedBackend(Backend(f)); Attach(op, images, backend);
        await op.OpenAsync(f.Images, Token); await op.SearchPagesAsync("000", op.Book, Token); op.SetBookLock(true); var old = op.Book!;
        await op.CopyToFolderAsync(new("内部目标", container), token: Token); Assert.Null(op.Error); Assert.True(Directory.Exists(Path.Combine(container, Path.GetFileName(child))));
        Assert.Equal(await new ArchiveFactory().GetPhysicalPathAsync(Path.Combine(container, Path.GetFileName(child)), Token), await new ArchiveFactory().GetPhysicalPathAsync(backend.LastResult!.Destination, Token));
        Assert.NotSame(old, op.Book); Assert.True(old.Source.IsDisposed);
        Assert.Equal("000目录", op.Book!.CurrentPage!.EntryName); Assert.Equal("000", op.Book.Pages.SearchKeyword); Assert.True(op.IsBookLocked);
        var target = op.Book.Pages.SourcePages.Single(page => page.EntryName == "目标目录"); Assert.True(target.ArchiveEntry.IsDirectory); Assert.False(target.IsImage);
        Assert.True(Directory.Exists(Path.Combine(container, Path.GetFileName(child), "空目录"))); Assert.Equal(0, op.DestinationMoves!.UndoCount);
    }

    [Fact]
    public async Task PlaylistAliasCopiesActualDirectoryAndDoesNotUseAliasAsTargetName()
    {
        using var f = new Fixture(); var child = Child(f); var state = await State(f); var list = Path.Combine(f.Root, "user.nvpls");
        await File.WriteAllTextAsync(list, new System.Text.Json.Nodes.JsonObject { ["Format"] = PlaylistSource.CurrentFormat,
            ["Items"] = new System.Text.Json.Nodes.JsonArray(new System.Text.Json.Nodes.JsonObject { ["Path"] = child, ["Name"] = "展示别名" }) }.ToJsonString(), Token);
        await using var op = f.Operation(state); var clipboard = new Clipboard(); op.AttachFileClipboard(clipboard); using var images = new BitmapFactory(new MagickImageDecoder()); Attach(op, images, Backend(f));
        await op.OpenAsync(list, Token); Assert.Equal("展示别名", op.Book!.CurrentPage!.EntryName); Assert.True(op.CanCopyFiles(MultiPagePolicy.Once)); await op.CopyFilesAsync(token: Token);
        Assert.Equal(child, Assert.Single(clipboard.Content.Files)); var target = Target(f); await op.CopyToFolderAsync(target, token: Token); Assert.Null(op.Error);
        Assert.True(Directory.Exists(Path.Combine(target.Path, Path.GetFileName(child)))); Assert.False(Directory.Exists(Path.Combine(target.Path, "展示别名"))); Assert.Equal(list, op.Book.Path);
    }

    [Theory]
    [InlineData(MultiPagePolicy.All)] [InlineData(MultiPagePolicy.AllLeftToRight)]
    public async Task MixedSelectedGroupPreservesOrderAndDeduplicatesRepeatedDirectoryAliases(MultiPagePolicy policy)
    {
        using var f = new Fixture(); var child = Child(f); var state = await State(f); var list = Path.Combine(f.Root, "mixed.nvpls");
        await File.WriteAllTextAsync(list, new System.Text.Json.Nodes.JsonObject { ["Format"] = PlaylistSource.CurrentFormat,
            ["Items"] = new System.Text.Json.Nodes.JsonArray(new System.Text.Json.Nodes.JsonObject { ["Path"] = child, ["Name"] = "甲" },
                new System.Text.Json.Nodes.JsonObject { ["Path"] = f.Zip, ["Name"] = "归档" },
                new System.Text.Json.Nodes.JsonObject { ["Path"] = Path.Combine(f.Images, "001.png"), ["Name"] = "图片" },
                new System.Text.Json.Nodes.JsonObject { ["Path"] = child, ["Name"] = "乙" }) }.ToJsonString(), Token);
        Config.Current.BookSettingDefault.SortMode = PageSortMode.Entry; Config.Current.BookSettingDefault.BookReadOrder = PageReadOrder.RightToLeft;
        await using var op = f.Operation(state); var clipboard = new Clipboard(); op.AttachFileClipboard(clipboard); using var images = new BitmapFactory(new MagickImageDecoder());
        var backend = new ObservedBackend(Backend(f)); Attach(op, images, backend); await op.OpenAsync(list, Token);
        // 提供原阅读控制已经确定的混合选区，测试文件链路不依赖查看器可见区域。
        op.Book!.SetCurrentPages(op.Book.Pages); state.SetCommandParameter("CopyFile", new CopyFileCommandParameter { MultiPagePolicy = policy });
        var expected = policy == MultiPagePolicy.All ? new[] { child, f.Zip, Path.Combine(f.Images, "001.png") } : [child, Path.Combine(f.Images, "001.png"), f.Zip];
        await op.CopyFilesAsync(token: Token); Assert.Null(op.Error); Assert.Equal(expected, clipboard.Content.Files); Assert.Equal(4, clipboard.Content.QueryPaths.Count);
        var target = Target(f); await op.CopyToFolderAsync(target, policy, Token); Assert.Null(op.Error); Assert.Equal(expected, backend.Sources);
        Assert.Equal(1, backend.DirectoryCalls); Assert.Equal(3, Directory.GetFileSystemEntries(target.Path).Length); Assert.Equal(0, op.DestinationMoves!.UndoCount);
    }

    [Fact]
    public async Task DirectoryCopyKeepsExistingClassificationRedoStack()
    {
        using var f = new Fixture(); var child = Child(f); var state = await State(f); await using var op = f.Operation(state); using var images = new BitmapFactory(new MagickImageDecoder()); Attach(op, images, Backend(f));
        var target = Target(f); Config.Current.System.IsFileWriteAccessEnabled = true; await op.OpenAsync(f.Images, Token); await Select(op, "001.png");
        await op.ClassifyAsync(target, false, Token); await op.ReplayDestinationMoveAsync(true, Token); Assert.Equal(1, op.DestinationMoves!.RedoCount);
        await Select(op, Path.GetFileName(child)); await op.CopyToFolderAsync(target, token: Token); Assert.Null(op.Error); Assert.Equal(1, op.DestinationMoves.RedoCount); Assert.Equal(0, op.DestinationMoves.UndoCount);
    }

    [Fact]
    public async Task ConfirmationCannotRedirectTargetAliasIntoProfile()
    {
        using var f = new Fixture(); var child = Child(f); var state = await State(f); await using var op = f.Operation(state); using var images = new BitmapFactory(new MagickImageDecoder());
        var backend = new ObservedBackend(Backend(f)); var moves = Attach(op, images, backend); var target = Target(f); var alias = Path.Combine(f.Root, "目标别名"); Directory.CreateSymbolicLink(alias, target.Path);
        Directory.CreateDirectory(Path.Combine(target.Path, Path.GetFileName(child))); moves.ConfirmDirectoryOverwriteAsync = _ => { Directory.Delete(alias); Directory.CreateSymbolicLink(alias, f.State); return Task.FromResult(true); };
        await op.OpenAsync(f.Images, Token); await Select(op, Path.GetFileName(child)); await op.CopyToFolderAsync(new("别名", alias), token: Token);
        Assert.NotNull(op.Error); Assert.Equal(0, backend.DirectoryCalls); Assert.False(Directory.Exists(Path.Combine(f.State, Path.GetFileName(child)))); Assert.True(File.Exists(Path.Combine(child, "图片.png")));
    }

    [Theory]
    [InlineData(ArchivePolicy.None, false)] [InlineData(ArchivePolicy.SendArchiveFile, true)]
    [InlineData(ArchivePolicy.SendArchivePath, false)] [InlineData(ArchivePolicy.SendExtractFile, false)]
    public async Task InternalDirectoryUsesOriginalPolicyWithoutInventingRecursiveExtraction(ArchivePolicy policy, bool copiesRoot)
    {
        using var f = new Fixture(); var state = await State(f); var zip = Path.Combine(f.Root, "nested.cbz");
        using (var archive = ZipFile.Open(zip, ZipArchiveMode.Create)) { archive.CreateEntryFromFile(Path.Combine(f.Images, "001.png"), "内部目录/001.png"); }
        Config.Current.System.ArchiveRecursiveMode = ArchiveEntryCollectionMode.CurrentDirectory; Config.Current.System.ArchiveCopyPolicy = policy;
        await using var op = f.Operation(state); var clipboard = new Clipboard(); op.AttachFileClipboard(clipboard); using var images = new BitmapFactory(new MagickImageDecoder()); Attach(op, images, Backend(f));
        await op.OpenAsync(zip, Token); Assert.True(op.Book!.CurrentPage!.ArchiveEntry.IsDirectory); Assert.False(op.Book.CurrentPage.ArchiveEntry.CanRealize()); Assert.True(op.CanCopyFiles(MultiPagePolicy.Once));
        await op.CopyFilesAsync(token: Token); if (policy == ArchivePolicy.SendExtractFile) Assert.Contains("未提取", op.Error); else Assert.Null(op.Error);
        Assert.Equal(Path.Combine(zip, "内部目录"), Assert.Single(clipboard.Content.QueryPaths));
        if (policy is ArchivePolicy.SendArchiveFile or ArchivePolicy.SendArchivePath) Assert.Equal(policy == ArchivePolicy.SendArchiveFile ? zip : Path.Combine(zip, "内部目录"), Assert.Single(clipboard.Content.Files));
        else Assert.Empty(clipboard.Content.Files);
        var target = Target(f); await op.CopyToFolderAsync(target, token: Token); if (policy is ArchivePolicy.SendExtractFile or ArchivePolicy.SendArchivePath) Assert.Contains("未提取", op.Error); else Assert.Null(op.Error);
        Assert.Equal(copiesRoot ? 1 : 0, Directory.GetFileSystemEntries(target.Path).Length);
        if (copiesRoot) Assert.Equal(await File.ReadAllBytesAsync(zip, Token), await File.ReadAllBytesAsync(Path.Combine(target.Path, Path.GetFileName(zip)), Token));
    }

    [Fact]
    public async Task DirectoryCopyCannotEnterMoveHistoryAndDeclinedLaterItemKeepsEarlierCopy()
    {
        using var f = new Fixture(); var a = Child(f, "A"); var b = Child(f, "B"); var backend = Backend(f); var moves = new DestinationMoveService(backend); var target = Target(f);
        var planA = await backend.PlanBookTransferAsync(a, target.Path, Token); var invalid = new FileTransferRequest(planA.Target.Path, planA.Destination, true, DirectoryCopyPlan: planA);
        Assert.Empty(await moves.TransferManyAsync([invalid], Token)); Assert.NotNull(moves.Error); Assert.Equal(0, moves.UndoCount); Assert.False(Directory.Exists(planA.Destination));
        Directory.CreateDirectory(Path.Combine(target.Path, "B")); await File.WriteAllTextAsync(Path.Combine(target.Path, "B", "old.txt"), "保留", Token);
        var planB = await backend.PlanBookTransferAsync(b, target.Path, Token); moves.ConfirmDirectoryOverwriteAsync = _ => Task.FromResult(false);
        var results = await moves.TransferManyAsync([new(planA.Target.Path, planA.Destination, false, DirectoryCopyPlan: planA), new(planB.Target.Path, planB.Destination, false, DirectoryCopyPlan: planB)], Token);
        Assert.Single(results); Assert.True(File.Exists(Path.Combine(target.Path, "A", "图片.png"))); Assert.True(File.Exists(Path.Combine(target.Path, "B", "old.txt"))); Assert.Equal(0, moves.UndoCount); Assert.Empty(await backend.RecoverAsync(Token));
    }

    [AvaloniaFact]
    public async Task FormalDirectoryMenuAndOverwriteWindowKeepClassificationDisabled()
    {
        using var f = new Fixture(); var child = Child(f); var state = await State(f); var op = f.Operation(state); var images = new BitmapFactory(new MagickImageDecoder()); Attach(op, images, Backend(f));
        var target = Target(f); Directory.CreateDirectory(Path.Combine(target.Path, Path.GetFileName(child))); Config.Current.System.DestinationFolderCollection.Add(target);
        state.SetCommandParameter("CopyToFolderAs", new CopyToFolderAsCommandParameter { Index = 1 }); op.AttachFileClipboard(new Clipboard());
        var window = new MainWindow(); window.Bind(new ReaderWorkspaceViewModel(op, new(op), state), images, new Platform()); window.Show();
        try
        {
            await window.OpenAsync(f.Images); await Select(op, Path.GetFileName(child)); Assert.True(window.IsCommandAvailable("CopyFile")); Assert.True(window.IsCommandAvailable("CopyToFolderAs")); Assert.False(window.IsCommandAvailable("MoveToFolderAs")); Assert.False(window.IsCommandAvailable("CutFile"));
            var action = window.ExecuteAsync("CopyToFolderAs"); var limit = DateTime.UtcNow.AddSeconds(5);
            while (!window.OwnedWindows.Any() && DateTime.UtcNow < limit) { Dispatcher.UIThread.RunJobs(); await Task.Delay(10, Token); }
            var dialog = Assert.Single(window.OwnedWindows); Dispatcher.UIThread.RunJobs(); dialog.UpdateLayout();
            using var frame = dialog.CaptureRenderedFrame(); Assert.NotNull(frame); var phase = Environment.GetEnvironmentVariable("NEEVIEW_ACCEPTANCE_PHASE") ?? "p4-directory-copy";
            frame.Save(Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../acceptance", phase + "-directory-overwrite.png")), Avalonia.Media.Imaging.PngBitmapEncoderOptions.Default);
            dialog.Close(false); await action; Assert.Equal(0, op.DestinationMoves!.UndoCount);
        }
        finally { await window.PrepareShutdownAsync(); window.Close(); }
    }
}
