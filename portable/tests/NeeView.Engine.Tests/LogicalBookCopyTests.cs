using System.IO.Compression;
using System.Text.Json;
using Avalonia.Headless.XUnit;
using NeeView;
using NeeView.Backends;
using NeeView.MacOS.Views;
namespace NeeView.Engine.Tests;

/// <summary>原Book.Path条目复制；真实临时归档/Profile和正式Headless，不访问系统剪贴板。</summary>
public sealed class LogicalBookCopyTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;
    private static TaskCompletionSource Signal() => new(TaskCreationOptions.RunContinuationsAsynchronously);
    private sealed class Clipboard : IFileClipboard
    {
        public FileClipboardContent Content { get; set; } = new([], []);
        public int Writes { get; private set; }
        public Func<CancellationToken, Task>? BeforeWrite { get; set; }
        public bool HasFileContent => Content.Files.Count > 0 || Content.QueryPaths.Count > 0;
        public async Task WriteAsync(FileClipboardContent content, CancellationToken token)
        { if (BeforeWrite is not null) await BeforeWrite(token); token.ThrowIfCancellationRequested(); Content = content; Writes++; }
        public Task<FileClipboardContent> ReadAsync(CancellationToken token) => Task.FromResult(Content);
    }
    private sealed class Platform : IPlatformService
    {
        public Task RevealAsync(string path, CancellationToken token = default) => Task.CompletedTask;
        public Task TrashAsync(string path, CancellationToken token = default) => throw new NotSupportedException();
    }
    /// <summary>建立含兄弟目录的归档，确保整书不能被当前图片或当前目录文件集合替代。</summary>
    private static string LogicalBook(Fixture f)
    {
        var root = Directory.CreateDirectory(Path.Combine(f.Root, "归档材料")).FullName;
        var folder = Directory.CreateDirectory(Path.Combine(root, "分册")).FullName;
        File.Copy(Path.Combine(f.Images, "001.png"), Path.Combine(folder, "001.png"));
        File.Copy(Path.Combine(f.Images, "002.png"), Path.Combine(folder, "002.png"));
        File.Copy(Path.Combine(f.Images, "003.png"), Path.Combine(root, "兄弟页.png"));
        var zip = Path.Combine(f.Root, "逻辑书.cbz"); ZipFile.CreateFromDirectory(root, zip); return zip + "/分册";
    }
    private static async Task<SaveData> State(Fixture f)
    { var state = new SaveData(f.State); await state.LoadAsync(Token); return state; }
    private static DestinationMoveService Attach(Fixture f, BookOperation op, BitmapFactory images)
    {
        var backend = new FileOperationBackend(Path.Combine(f.State, "Recovery")); var moves = new DestinationMoveService(backend);
        op.AttachFileOperations(moves, backend, images); return moves;
    }
    private static DestinationFolder Target(Fixture f) => new("目标", Directory.CreateDirectory(Path.Combine(f.Root, "目标")).FullName);

    [Theory]
    [InlineData(ArchivePolicy.None)] [InlineData(ArchivePolicy.SendArchiveFile)]
    [InlineData(ArchivePolicy.SendArchivePath)] [InlineData(ArchivePolicy.SendExtractFile)]
    public async Task CopyLogicalBookUsesBookEntryAndOriginalClipboardPolicy(ArchivePolicy policy)
    {
        using var f = new Fixture(); var path = LogicalBook(f); var state = await State(f); await using var op = f.Operation(state);
        var clipboard = new Clipboard(); op.AttachFileClipboard(clipboard); Config.Current.System.ArchiveCopyPolicy = policy;
        Config.Current.System.TextCopyPolicy = TextCopyPolicy.CopyFilePath;
        state.SetCommandParameter("CopyFile", new CopyFileCommandParameter { MultiPagePolicy = (MultiPagePolicy)99 });
        await op.OpenAsync(path, Token); await op.JumpAsync(1); var book = op.Book!; var page = book.CurrentPage; var position = op.Position;
        await state.SaveAsync(book, Token);
        Assert.True(op.CanCopyBook); await new CommandTable(op).ExecuteAsync("CopyBook");
        Assert.Equal(1, clipboard.Writes); Assert.Equal(path, Assert.Single(clipboard.Content.QueryPaths));
        if (policy == ArchivePolicy.SendArchiveFile) Assert.Equal(book.Source.RootArchivePath, Assert.Single(clipboard.Content.Files));
        else if (policy == ArchivePolicy.SendArchivePath) Assert.Equal(path, Assert.Single(clipboard.Content.Files));
        else Assert.Empty(clipboard.Content.Files);
        Assert.Equal(clipboard.Content.Files.Count == 0 ? null : clipboard.Content.Files[0], clipboard.Content.Text);
        if (policy == ArchivePolicy.SendExtractFile) Assert.Contains("未提取", op.Error); else Assert.Null(op.Error);
        Assert.Same(book, op.Book); Assert.Same(page, book.CurrentPage); Assert.Equal(position, op.Position);
        Assert.Equal(path, state.LastBookPath); Assert.True(File.Exists(book.Source.RootArchivePath));
        // 私有QueryPath必须重新打开逻辑书，不能因系统根归档输出而跳回整包。
        await op.OpenAsync(f.Images, Token); await op.PasteFilesAsync(Token); Assert.Equal(path, op.Book!.Path); Assert.Equal(2, op.Book.Pages.Count);
    }

    [Theory]
    [InlineData(ArchivePolicy.None)] [InlineData(ArchivePolicy.SendArchiveFile)]
    [InlineData(ArchivePolicy.SendArchivePath)] [InlineData(ArchivePolicy.SendExtractFile)]
    public async Task CopyLogicalBookToFolderUsesLimitedPolicyAndPreservesReadingAndStacks(ArchivePolicy policy)
    {
        using var f = new Fixture(); var path = LogicalBook(f); var state = await State(f); await using var op = f.Operation(state);
        using var images = new BitmapFactory(new MagickImageDecoder()); var moves = Attach(f, op, images); var target = Target(f);
        Config.Current.System.ArchiveCopyPolicy = policy; Config.Current.Panels.IsDestinationFolderCopyMode = false;
        Config.Current.System.DestinationFolderCollection.Add(target);
        state.SetCommandParameter("CopyBookToFolderAs", new MoveToFolderAsCommandParameter { Index = 1 });
        await op.OpenAsync(path, Token); await op.JumpAsync(1); op.SetBookLock(true);
        var book = op.Book!; var page = book.CurrentPage; var position = op.Position;
        await state.SaveAsync(book, Token);
        Assert.False(Config.Current.System.IsFileWriteAccessEnabled); Assert.True(op.CanCopyBookToFolder); Assert.False(op.CanMoveBookToFolder);
        await new CommandTable(op).ExecuteAsync("CopyBookToFolderAs");
        var files = Directory.GetFiles(target.Path); if (policy == ArchivePolicy.SendArchiveFile)
        { Assert.Equal(Path.GetFileName(book.Source.RootArchivePath), Path.GetFileName(Assert.Single(files))); Assert.Equal(await File.ReadAllBytesAsync(book.Source.RootArchivePath, Token), await File.ReadAllBytesAsync(files[0], Token)); }
        else Assert.Empty(files);
        if (policy is ArchivePolicy.SendArchivePath or ArchivePolicy.SendExtractFile) Assert.Contains("未提取", op.Error); else Assert.Null(op.Error);
        Assert.Same(book, op.Book); Assert.Same(page, book.CurrentPage); Assert.Equal(position, op.Position); Assert.True(op.IsBookLocked);
        Assert.Equal(0, moves.UndoCount); Assert.Equal(0, moves.RedoCount); Assert.Equal(path, state.LastBookPath);
    }

    [Theory]
    [InlineData("directory")] [InlineData("archive")] [InlineData("playlist")]
    public async Task RootBookCopyUsesEntityEvenWhenPolicyIsNoneAndPageParameterIsInvalid(string kind)
    {
        using var f = new Fixture(); var state = await State(f); var target = kind == "directory" ? f.Images : f.Zip;
        if (kind == "playlist")
        {
            target = Path.Combine(f.Root, "列表.nvpls");
            await File.WriteAllTextAsync(target, JsonSerializer.Serialize(new { Format = PlaylistSource.CurrentFormat, Items = new[] { new { Path = Path.Combine(f.Images, "002.png"), Name = "显示别名" } } }), Token);
        }
        await using var op = f.Operation(state); var clipboard = new Clipboard(); op.AttachFileClipboard(clipboard);
        Config.Current.System.ArchiveCopyPolicy = ArchivePolicy.None;
        state.SetCommandParameter("CopyFile", new CopyFileCommandParameter { MultiPagePolicy = (MultiPagePolicy)99 });
        await op.OpenAsync(target, Token); await op.CopyFilesAsync(book: true, token: Token);
        Assert.Null(op.Error); Assert.Equal(target, Assert.Single(clipboard.Content.Files)); Assert.Equal(target, Assert.Single(clipboard.Content.QueryPaths));
    }

    [Fact]
    public async Task PlaylistBookCopyPreservesTheListFileAndLogicalReferencesWithoutCopyingTargets()
    {
        using var f = new Fixture(); var logical = LogicalBook(f); var state = await State(f); var list = Path.Combine(f.Root, "目录别名.nvpls");
        await File.WriteAllTextAsync(list, JsonSerializer.Serialize(new { Format = PlaylistSource.CurrentFormat, Items = new[]
        { new { Path = logical, Name = "内部目录别名" }, new { Path = Path.Combine(f.Images, "002.png"), Name = "实体图片别名" } } }), Token);
        var bytes = await File.ReadAllBytesAsync(list, Token); var rootBytes = await File.ReadAllBytesAsync(Path.GetDirectoryName(logical)!, Token);
        await using var op = f.Operation(state); var clipboard = new Clipboard(); op.AttachFileClipboard(clipboard);
        using var images = new BitmapFactory(new MagickImageDecoder()); var moves = Attach(f, op, images); var target = Target(f);
        Config.Current.System.ArchiveCopyPolicy = ArchivePolicy.SendExtractFile; await op.OpenAsync(list, Token); Assert.True(op.Book!.Source.IsPlaylist);
        await op.CopyFilesAsync(book: true, token: Token); await op.TransferBookToFolderAsync(target, false, Token);
        Assert.Null(op.Error); Assert.Equal(list, Assert.Single(clipboard.Content.Files)); Assert.Equal(list, Assert.Single(clipboard.Content.QueryPaths));
        Assert.Equal(bytes, await File.ReadAllBytesAsync(Assert.Single(Directory.GetFiles(target.Path)), Token));
        Assert.Equal(bytes, await File.ReadAllBytesAsync(list, Token)); Assert.Equal(rootBytes, await File.ReadAllBytesAsync(Path.GetDirectoryName(logical)!, Token));
        Assert.Equal(5, Directory.GetFiles(f.Images).Length); Assert.Equal(0, moves.UndoCount); Assert.Equal(list, op.Book.Path);
    }

    [Fact]
    public async Task ExplicitImageOpenCopiesTheBookScopeRatherThanRequestedPage()
    {
        using var f = new Fixture(); var path = LogicalBook(f); var state = await State(f); await using var op = f.Operation(state);
        var clipboard = new Clipboard(); op.AttachFileClipboard(clipboard); Config.Current.System.ArchiveRecursiveMode = ArchiveEntryCollectionMode.CurrentDirectory;
        Config.Current.System.ArchiveCopyPolicy = ArchivePolicy.SendArchivePath;
        await op.OpenAsync(path + "/002.png", Token); Assert.Equal("002.png", op.Book!.CurrentPage!.EntryName);
        await op.CopyFilesAsync(book: true, token: Token); Assert.Equal(path, Assert.Single(clipboard.Content.Files)); Assert.Equal(path, Assert.Single(clipboard.Content.QueryPaths));
        Config.Current.System.ArchiveRecursiveMode = ArchiveEntryCollectionMode.IncludeSubArchives;
        await op.OpenAsync(path + "/002.png", Token); Assert.Equal(Path.GetDirectoryName(path), op.Book!.Path);
        await op.CopyFilesAsync(book: true, token: Token); Assert.Equal(op.Book.Path, Assert.Single(clipboard.Content.Files));
    }

    [Fact]
    public async Task EmptyPageSearchDoesNotDisableBookCopyOrReplaceLogicalIdentity()
    {
        using var f = new Fixture(); var path = LogicalBook(f); var state = await State(f); await using var op = f.Operation(state);
        var clipboard = new Clipboard(); op.AttachFileClipboard(clipboard); using var images = new BitmapFactory(new MagickImageDecoder()); Attach(f, op, images);
        Config.Current.System.ArchiveCopyPolicy = ArchivePolicy.SendArchiveFile; await op.OpenAsync(path, Token);
        await op.SearchPagesAsync("missing-page-name", op.Book, Token); Assert.Empty(op.Book!.Pages); var book = op.Book;
        Assert.True(op.CanCopyBook); Assert.True(op.CanCopyBookToFolder); await op.CopyFilesAsync(book: true, token: Token);
        await op.TransferBookToFolderAsync(Target(f), false, Token); Assert.Null(op.Error); Assert.Same(book, op.Book); Assert.Empty(book.Pages); Assert.Equal(path, state.LastBookPath);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task LogicalArchiveOutputKeepsWholeBookOverwriteConfirmation(bool accept)
    {
        using var f = new Fixture(); var path = LogicalBook(f); var state = await State(f); await using var op = f.Operation(state);
        using var images = new BitmapFactory(new MagickImageDecoder()); var moves = Attach(f, op, images); var target = Target(f);
        var root = Path.GetDirectoryName(path)!; var destination = Path.Combine(target.Path, Path.GetFileName(root)); await File.WriteAllTextAsync(destination, "old", Token);
        Config.Current.System.ArchiveCopyPolicy = ArchivePolicy.SendArchiveFile; await op.OpenAsync(path, Token);
        int calls = 0; op.ConfirmBookOverwriteAsync = plan => { calls++; Assert.Equal(root, plan.Target.Path); return Task.FromResult(accept); };
        moves.ConfirmOverwriteAsync = _ => throw new InvalidOperationException("整书命令不能调用图片覆盖确认");
        await op.TransferBookToFolderAsync(target, false, Token); Assert.Equal(1, calls); Assert.Null(op.Error);
        Assert.Equal(accept ? await File.ReadAllBytesAsync(root, Token) : System.Text.Encoding.UTF8.GetBytes("old"), await File.ReadAllBytesAsync(destination, Token));
        Assert.Equal(path, op.Book!.Path); Assert.True(File.Exists(root));
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task ClosingOrSwitchingCancelsLogicalBookConfirmationWithoutCopying(bool close)
    {
        using var f = new Fixture(); var path = LogicalBook(f); var state = await State(f); var op = f.Operation(state);
        using var images = new BitmapFactory(new MagickImageDecoder()); Attach(f, op, images); var target = Target(f);
        var destination = Path.Combine(target.Path, Path.GetFileName(Path.GetDirectoryName(path)!)); await File.WriteAllTextAsync(destination, "old", Token);
        Config.Current.System.ArchiveCopyPolicy = ArchivePolicy.SendArchiveFile; await op.OpenAsync(path, Token);
        var entered = Signal(); var release = Signal(); op.ConfirmBookOverwriteAsync = async _ => { entered.TrySetResult(); await release.Task; return true; };
        var copy = op.TransferBookToFolderAsync(target, false, Token);
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5), Token);
            if (close) await op.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5), Token); else await op.OpenAsync(f.Images, Token);
            await copy.WaitAsync(TimeSpan.FromSeconds(5), Token); Assert.Equal("old", await File.ReadAllTextAsync(destination, Token));
            if (close) Assert.Null(op.Book); else Assert.Equal(f.Images, op.Book!.Path);
            Assert.False(op.IsTransferringBook); Assert.Null(op.Error);
        }
        finally { release.TrySetResult(); await copy; await op.DisposeAsync(); }
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task ClosingOrSwitchingCancelsLogicalBookClipboardPreparation(bool close)
    {
        using var f = new Fixture(); var path = LogicalBook(f); var state = await State(f); var op = f.Operation(state);
        var entered = Signal(); var clipboard = new Clipboard { BeforeWrite = async token => { entered.TrySetResult(); await Task.Delay(Timeout.Infinite, token); } };
        op.AttachFileClipboard(clipboard); Config.Current.System.ArchiveCopyPolicy = ArchivePolicy.SendArchivePath; await op.OpenAsync(path, Token);
        var copy = op.CopyFilesAsync(book: true, token: Token);
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5), Token);
            if (close) await op.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5), Token); else await op.OpenAsync(f.Images, Token);
            await copy.WaitAsync(TimeSpan.FromSeconds(5), Token); Assert.Equal(0, clipboard.Writes); Assert.False(op.IsUsingClipboard);
        }
        finally { await copy; await op.DisposeAsync(); }
    }

    [Fact]
    public async Task LogicalBookCopyStillRejectsProtectedDestinationAndCannotMoveContainer()
    {
        using var f = new Fixture(); var path = LogicalBook(f); var state = await State(f); await using var op = f.Operation(state);
        using var images = new BitmapFactory(new MagickImageDecoder()); Attach(f, op, images); Config.Current.System.ArchiveCopyPolicy = ArchivePolicy.SendArchiveFile;
        Config.Current.System.IsFileWriteAccessEnabled = true; await op.OpenAsync(path, Token); Assert.False(op.CanMoveBookToFolder);
        await op.TransferBookToFolderAsync(Target(f), true, Token); Assert.Empty(Directory.GetFiles(Target(f).Path));
        await op.TransferBookToFolderAsync(new("protected", f.State), false, Token); Assert.Contains("Profile", op.Error);
        Assert.Equal(path, op.Book!.Path); Assert.True(File.Exists(Path.GetDirectoryName(path)!));
    }

    [AvaloniaFact]
    public async Task FormalMenusExposeLogicalBookCopyAndKeepMoveUnavailable()
    {
        using var f = new Fixture(); var path = LogicalBook(f); var state = await State(f); var op = f.Operation(state);
        using var images = new BitmapFactory(new MagickImageDecoder()); Attach(f, op, images); var clipboard = new Clipboard(); op.AttachFileClipboard(clipboard);
        Config.Current.System.IsFileWriteAccessEnabled = true; Config.Current.System.ArchiveCopyPolicy = ArchivePolicy.SendArchiveFile;
        var target = Target(f); Config.Current.System.DestinationFolderCollection.Add(target);
        state.SetCommandParameter("CopyBookToFolderAs", new MoveToFolderAsCommandParameter { Index = 1 });
        var window = new MainWindow(); window.Bind(new(op, new(op), state), images, new Platform()); window.Show();
        try
        {
            await window.OpenAsync(path); Assert.True(window.IsCommandAvailable("CopyBook")); Assert.True(window.IsCommandAvailable("CopyBookToFolderAs"));
            Assert.False(window.IsCommandAvailable("MoveBookToFolderAs")); await window.ExecuteAsync("CopyBook"); await window.ExecuteAsync("CopyBookToFolderAs");
            Assert.Equal(Path.GetDirectoryName(path), Assert.Single(clipboard.Content.Files)); Assert.Single(Directory.GetFiles(target.Path)); Assert.Equal(path, op.Book!.Path);
        }
        finally { await window.PrepareShutdownAsync(); window.Close(); }
    }
}
