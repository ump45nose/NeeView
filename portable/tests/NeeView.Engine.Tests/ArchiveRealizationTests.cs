using System.IO.Compression;
using System.Text.Json.Nodes;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using NeeView;
using NeeView.Backends;
using NeeView.MacOS.ViewModels;
using NeeView.MacOS.Views;
namespace NeeView.Engine.Tests;

/// <summary>原归档四策略、原页组及临时实体生命周期；只操作自建文件/模拟剪贴板。</summary>
public sealed class ArchiveRealizationTests
{
    private sealed class Clipboard : IFileClipboard
    {
        public FileClipboardContent Content { get; set; } = new([], []);
        public bool Fail { get; set; }
        public Func<Task>? AfterCommit { get; set; }
        public int Writes { get; private set; }
        public bool HasFileContent => Content.Files.Count > 0 || Content.QueryPaths.Count > 0;
        public async Task WriteAsync(FileClipboardContent content, CancellationToken token)
        { token.ThrowIfCancellationRequested(); if (Fail) throw new IOException("模拟剪贴板拒绝"); Content = content; Writes++; if (AfterCommit is not null) await AfterCommit(); }
        public Task<FileClipboardContent> ReadAsync(CancellationToken token) => Task.FromResult(Content);
    }
    private sealed class Platform : IPlatformService
    {
        public Task RevealAsync(string path, CancellationToken token = default) => Task.CompletedTask;
        public Task TrashAsync(string path, CancellationToken token = default) => Task.CompletedTask;
    }
    private sealed class DelayedRealizer(IArchiveEntryRealizer inner) : IArchiveEntryRealizer
    {
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public string? Path { get; private set; }
        public async Task<RealizedFileLease> ExtractAsync(ArchiveEntry entry, long remainingBytes, CancellationToken token)
        {
            var lease = await inner.ExtractAsync(entry, remainingBytes, token); Path = lease.Path; Entered.TrySetResult();
            // 模拟不能即时中断的原生晚到；调用方必须清理已生成租约。
            await Release.Task; return lease;
        }
        public Task RetainClipboardAsync(RealizedFilePathList files) => inner.RetainClipboardAsync(files);
    }
    private static CancellationToken Token => TestContext.Current.CancellationToken;
    private static async Task<SaveData> State(Fixture fixture)
    { var state = new SaveData(fixture.State); await state.LoadAsync(Token); return state; }
    private static void Wide(PageReadOrder order)
    {
        var setting = Config.Current.BookSettingDefault; setting.PageMode = PageMode.WidePage; setting.BookReadOrder = order;
        setting.IsSupportedSingleFirstPage = false; setting.IsSupportedSingleLastPage = false; setting.IsSupportedWidePage = false;
    }
    private static BookOperation Operation(Fixture f, SaveData state, IArchiveEntryRealizer realizer, Clipboard? clipboard = null)
    {
        var operation = f.Operation(state); operation.AttachArchiveEntryRealizer(realizer);
        if (clipboard is not null) operation.AttachFileClipboard(clipboard); return operation;
    }
    private static DestinationMoveService AttachTransfer(Fixture f, BookOperation operation, BitmapFactory images)
    {
        var backend = new FileOperationBackend(Path.Combine(f.State, "Recovery")); var moves = new DestinationMoveService(backend);
        operation.AttachFileOperations(moves, backend, images); return moves;
    }
    private static DestinationFolder Target(Fixture f) => new("目标", Directory.CreateDirectory(Path.Combine(f.Root, "目标")).FullName);
    private static string[] Extracted(string root) => Directory.Exists(root) ? Directory.GetFiles(root, "*", SearchOption.AllDirectories) : [];

    [Theory]
    [InlineData(ArchivePolicy.None, TextCopyPolicy.None)]
    [InlineData(ArchivePolicy.None, TextCopyPolicy.CopyFilePath)]
    [InlineData(ArchivePolicy.None, TextCopyPolicy.OriginalPath)]
    [InlineData(ArchivePolicy.SendArchiveFile, TextCopyPolicy.None)]
    [InlineData(ArchivePolicy.SendArchiveFile, TextCopyPolicy.CopyFilePath)]
    [InlineData(ArchivePolicy.SendArchiveFile, TextCopyPolicy.OriginalPath)]
    [InlineData(ArchivePolicy.SendArchivePath, TextCopyPolicy.None)]
    [InlineData(ArchivePolicy.SendArchivePath, TextCopyPolicy.CopyFilePath)]
    [InlineData(ArchivePolicy.SendArchivePath, TextCopyPolicy.OriginalPath)]
    [InlineData(ArchivePolicy.SendExtractFile, TextCopyPolicy.None)]
    [InlineData(ArchivePolicy.SendExtractFile, TextCopyPolicy.CopyFilePath)]
    [InlineData(ArchivePolicy.SendExtractFile, TextCopyPolicy.OriginalPath)]
    public async Task ClipboardUsesFourOriginalPoliciesAndActualBaselineText(ArchivePolicy policy, TextCopyPolicy text)
    {
        using var f = new Fixture(); var state = await State(f); var root = Path.Combine(f.Root, "realized");
        await using var realizer = new ArchiveEntryRealizer(root); var clipboard = new Clipboard();
        await using var op = Operation(f, state, realizer, clipboard);
        Config.Current.System.ArchiveCopyPolicy = policy; Config.Current.System.TextCopyPolicy = text;
        await op.OpenAsync(f.Zip, Token); var book = op.Book; var page = book!.CurrentPage; var position = op.Position;
        Assert.True(op.CanCopyFiles(MultiPagePolicy.Once)); Assert.False(op.CanFileAction);
        await new CommandTable(op).ExecuteAsync("CopyFile"); Assert.Null(op.Error); Assert.Equal(1, clipboard.Writes);
        Assert.Equal(Path.Combine(f.Zip, "001.png"), Assert.Single(clipboard.Content.QueryPaths));
        if (policy == ArchivePolicy.None) Assert.Empty(clipboard.Content.Files);
        else if (policy == ArchivePolicy.SendArchiveFile) Assert.Equal(f.Zip, Assert.Single(clipboard.Content.Files));
        else if (policy == ArchivePolicy.SendArchivePath) Assert.Equal(clipboard.Content.QueryPaths, clipboard.Content.Files);
        else
        {
            var file = Assert.Single(clipboard.Content.Files); Assert.Equal("001.png", Path.GetFileName(file));
            Assert.Equal(await File.ReadAllBytesAsync(Path.Combine(f.Images, "001.png"), Token), await File.ReadAllBytesAsync(file, Token));
            Assert.NotEqual(Assert.Single(clipboard.Content.QueryPaths), file);
        }
        Assert.Equal(text == TextCopyPolicy.None || clipboard.Content.Files.Count == 0 ? null : string.Join(Environment.NewLine, clipboard.Content.Files), clipboard.Content.Text);
        Assert.Same(book, op.Book); Assert.Same(page, book.CurrentPage); Assert.Equal(position, op.Position);
        // 回贴优先逻辑QueryPath；提取路径绝不能成为历史或书籍身份。
        await op.OpenAsync(f.Images, Token); await op.PasteFilesAsync(Token); Assert.Equal(f.Zip, op.Book!.Path); Assert.Equal("001.png", op.Book.CurrentPage!.EntryName);
        Assert.Equal(0, (int)ArchivePolicy.None); Assert.Equal(3, (int)ArchivePolicy.SendExtractFile);
    }

    [Theory]
    [InlineData(ArchivePolicy.None, "")]
    [InlineData(ArchivePolicy.SendArchiveFile, "漫画.cbz")]
    [InlineData(ArchivePolicy.SendArchivePath, "001.png,002.png")]
    [InlineData(ArchivePolicy.SendExtractFile, "001.png,002.png")]
    public async Task FixedCopyUsesLimitedRealizationAndLeavesArchivePositionAndHistory(ArchivePolicy policy, string expected)
    {
        using var f = new Fixture(); var state = await State(f); Wide(PageReadOrder.RightToLeft);
        var root = Path.Combine(f.Root, "realized"); await using var realizer = new ArchiveEntryRealizer(root);
        await using var op = Operation(f, state, realizer); using var images = new BitmapFactory(new MagickImageDecoder());
        var moves = AttachTransfer(f, op, images); var target = Target(f); Config.Current.System.ArchiveCopyPolicy = policy;
        await op.OpenAsync(f.Zip, Token); var book = op.Book; var page = book!.CurrentPage; var position = op.Position;
        Assert.False(Config.Current.System.IsFileWriteAccessEnabled); Assert.True(op.CanCopyToFolder(MultiPagePolicy.All)); Assert.False(op.CanFileAction);
        await op.CopyToFolderAsync(target, MultiPagePolicy.All, Token); Assert.Null(op.Error);
        Assert.Equal(expected, string.Join(',', Directory.GetFiles(target.Path).Select(Path.GetFileName).OrderBy(name => name, StringComparer.Ordinal)));
        foreach (var path in Directory.GetFiles(target.Path))
            Assert.Equal(await File.ReadAllBytesAsync(policy == ArchivePolicy.SendArchiveFile ? f.Zip : Path.Combine(f.Images, Path.GetFileName(path)), Token), await File.ReadAllBytesAsync(path, Token));
        Assert.Same(book, op.Book); Assert.Same(page, book.CurrentPage); Assert.Equal(position, op.Position);
        Assert.Equal(0, moves.UndoCount); Assert.Equal(0, moves.RedoCount); Assert.Empty(Extracted(root));
    }

    [Theory]
    [InlineData(PageReadOrder.RightToLeft, MultiPagePolicy.Once, "001.png")]
    [InlineData(PageReadOrder.LeftToRight, MultiPagePolicy.Once, "001.png")]
    [InlineData(PageReadOrder.RightToLeft, MultiPagePolicy.All, "001.png,002.png")]
    [InlineData(PageReadOrder.LeftToRight, MultiPagePolicy.All, "001.png,002.png")]
    [InlineData(PageReadOrder.RightToLeft, MultiPagePolicy.AllLeftToRight, "002.png,001.png")]
    [InlineData(PageReadOrder.LeftToRight, MultiPagePolicy.AllLeftToRight, "001.png,002.png")]
    public async Task ExtractedClipboardPreservesOriginalGroupOrder(PageReadOrder order, MultiPagePolicy policy, string expected)
    {
        using var f = new Fixture(); var state = await State(f); Wide(order);
        await using var realizer = new ArchiveEntryRealizer(Path.Combine(f.Root, "realized")); var clipboard = new Clipboard();
        await using var op = Operation(f, state, realizer, clipboard); state.SetCommandParameter("CopyFile", new CopyFileCommandParameter { MultiPagePolicy = policy });
        await op.OpenAsync(f.Zip, Token); await op.CopyFilesAsync(token: Token);
        Assert.Equal(expected, string.Join(',', clipboard.Content.Files.Select(Path.GetFileName))); Assert.Null(op.Error);
    }

    [Fact]
    public async Task ClipboardLeaseSurvivesBookAndWindowAndIsReplacedOnlyAfterSuccessfulWrite()
    {
        using var f = new Fixture(); var state = await State(f); var root = Path.Combine(f.Root, "realized");
        await using var realizer = new ArchiveEntryRealizer(root); var clipboard = new Clipboard(); var op = Operation(f, state, realizer, clipboard);
        await op.OpenAsync(f.Zip, Token); await op.CopyFilesAsync(token: Token); var old = Assert.Single(clipboard.Content.Files);
        await op.OpenAsync(f.Images, Token); Assert.True(File.Exists(old)); await op.DisposeAsync(); Assert.True(File.Exists(old));
        var reopened = Operation(f, state, realizer, clipboard);
        try
        {
            await reopened.OpenAsync(f.Zip, Token); clipboard.Fail = true; await reopened.CopyFilesAsync(token: Token);
            Assert.Equal(old, Assert.Single(clipboard.Content.Files)); Assert.True(File.Exists(old)); Assert.Single(Extracted(root)); Assert.NotNull(reopened.Error);
            clipboard.Fail = false; await reopened.CopyFilesAsync(token: Token); var next = Assert.Single(clipboard.Content.Files);
            Assert.NotEqual(old, next); Assert.False(File.Exists(old)); Assert.True(File.Exists(next)); Assert.Single(Extracted(root));
            await reopened.OpenAsync(f.Images, Token); await reopened.CopyFilesAsync(token: Token); Assert.Empty(Extracted(root));
        }
        finally { await reopened.DisposeAsync(); }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task LateExtractionOnSwitchOrCloseIsReleasedWithoutClipboardCommit(bool close)
    {
        using var f = new Fixture(); var state = await State(f); var root = Path.Combine(f.Root, "realized");
        await using var realizer = new ArchiveEntryRealizer(root); var delayed = new DelayedRealizer(realizer); var clipboard = new Clipboard();
        var op = Operation(f, state, delayed, clipboard); await op.OpenAsync(f.Zip, Token);
        var copy = op.CopyFilesAsync(token: Token); await delayed.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5), Token);
        var change = close ? op.DisposeAsync().AsTask() : op.OpenAsync(f.Images, Token);
        Assert.False(change.IsCompleted); delayed.Release.TrySetResult(); await copy; await change;
        Assert.Equal(0, clipboard.Writes); Assert.Empty(Extracted(root));
        if (!close) { Assert.Equal(f.Images, op.Book!.Path); await op.DisposeAsync(); }
    }

    [Fact]
    public async Task CommittedClipboardWriteRetainsExtractionDespiteLateCancellationAndClose()
    {
        using var f = new Fixture(); var state = await State(f); var root = Path.Combine(f.Root, "realized"); await using var realizer = new ArchiveEntryRealizer(root);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously); var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var clipboard = new Clipboard { AfterCommit = async () => { entered.SetResult(); await release.Task; } }; var op = Operation(f, state, realizer, clipboard);
        await op.OpenAsync(f.Zip, Token); var copy = op.CopyFilesAsync(token: Token); await entered.Task.WaitAsync(TimeSpan.FromSeconds(5), Token);
        var close = op.DisposeAsync().AsTask(); Assert.False(close.IsCompleted); release.SetResult(); await copy; await close;
        var file = Assert.Single(clipboard.Content.Files); Assert.True(File.Exists(file)); await realizer.DisposeAsync(); Assert.False(File.Exists(file));
    }

    [Fact]
    public async Task FixedCopyPreparationIsBusyAndSupersededOpenCannotClearItsErrorOrInstallFiles()
    {
        using var f = new Fixture(); var state = await State(f); var root = Path.Combine(f.Root, "realized"); await using var realizer = new ArchiveEntryRealizer(root);
        var delayed = new DelayedRealizer(realizer); var clipboard = new Clipboard(); await using var op = Operation(f, state, delayed, clipboard);
        using var images = new BitmapFactory(new MagickImageDecoder()); var moves = AttachTransfer(f, op, images); var target = Target(f);
        await op.OpenAsync(f.Zip, Token); var copy = op.CopyToFolderAsync(target, token: Token); await delayed.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5), Token);
        Assert.True(moves.IsBusy); Assert.False(op.CanCopyFiles(MultiPagePolicy.Once)); Assert.False(op.CanFileAction);
        await op.OpenAsync(Path.Combine(f.Root, "missing.png"), Token); var error = op.Error; Assert.NotNull(error);
        delayed.Release.SetResult(); await copy; Assert.Equal(error, op.Error); Assert.Empty(Directory.GetFiles(target.Path)); Assert.Empty(Extracted(root)); Assert.Equal(0, moves.UndoCount);
    }

    [Fact]
    public async Task ClosingFixedCopyCancelsPreparationAndWaitsForLateLeaseRelease()
    {
        using var f = new Fixture(); var state = await State(f); var root = Path.Combine(f.Root, "realized"); await using var realizer = new ArchiveEntryRealizer(root);
        var delayed = new DelayedRealizer(realizer); var op = Operation(f, state, delayed); using var images = new BitmapFactory(new MagickImageDecoder());
        var moves = AttachTransfer(f, op, images); var target = Target(f); await op.OpenAsync(f.Zip, Token);
        var copy = op.CopyToFolderAsync(target, token: Token); await delayed.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5), Token);
        var close = op.DisposeAsync().AsTask(); Assert.False(close.IsCompleted); Assert.False(op.CanCopyToFolder(MultiPagePolicy.Once));
        delayed.Release.SetResult(); await copy; await close;
        Assert.Empty(Directory.GetFiles(target.Path)); Assert.Empty(Extracted(root)); Assert.False(moves.IsBusy);
    }

    [Fact]
    public async Task DeclinedSecondArchiveOverwritePreservesFirstCommittedCopyAndCleansWholeBatch()
    {
        using var f = new Fixture(); var state = await State(f); Wide(PageReadOrder.RightToLeft); var root = Path.Combine(f.Root, "realized");
        await using var realizer = new ArchiveEntryRealizer(root); await using var op = Operation(f, state, realizer); using var images = new BitmapFactory(new MagickImageDecoder());
        var moves = AttachTransfer(f, op, images); var target = Target(f); moves.ConfirmOverwriteAsync = _ => Task.FromResult(false);
        await File.WriteAllTextAsync(Path.Combine(target.Path, "002.png"), "保留目标", Token); await op.OpenAsync(f.Zip, Token); var page = op.Book!.CurrentPage;
        await op.CopyToFolderAsync(target, MultiPagePolicy.All, Token);
        Assert.Equal(await File.ReadAllBytesAsync(Path.Combine(f.Images, "001.png"), Token), await File.ReadAllBytesAsync(Path.Combine(target.Path, "001.png"), Token));
        Assert.Equal("保留目标", await File.ReadAllTextAsync(Path.Combine(target.Path, "002.png"), Token));
        Assert.Same(page, op.Book.CurrentPage); Assert.Empty(Extracted(root)); Assert.Equal(0, moves.UndoCount);
    }

    [Theory]
    [InlineData("Rar.rar")]
    [InlineData("Rar.solid.rar")]
    [InlineData("Rar5.solid.rar")]
    [InlineData("7Zip.LZMA.7z")]
    [InlineData("7Zip.solid.7z")]
    public async Task RealRarAndSevenZipExtractionIsIndependentOfSourceLifetime(string fixture)
    {
        using var f = new Fixture(); var root = Path.Combine(f.Root, "realized"); await using var realizer = new ArchiveEntryRealizer(root);
        var source = await new ArchiveFactory().OpenAsync(Path.Combine(AppContext.BaseDirectory, "Fixtures", fixture), Token);
        try
        {
            var entry = (await source.GetEntriesAsync(Token)).Last(e => e.IsImage());
            await using var files = await ArchiveEntryUtility.RealizeArchiveEntry([entry, entry], ArchivePolicy.SendExtractFile, realizer, Token);
            Assert.Single(files.Paths); Assert.Equal(entry.Length, files.TemporaryBytes); await source.DisposeAsync();
            await using var stream = File.OpenRead(files.Paths[0]);
            using var data = await new MagickImageDecoder().DecodeAsync(stream, new(96, 128), Token); Assert.Equal(new(96, 128), data.Size);
        }
        finally { await source.DisposeAsync(); }
        Assert.Empty(Extracted(root));
    }

    [Fact]
    public async Task InternalDirectoryPathAndDuplicateLeafNamesPreserveLogicalIdentityAndSafeOutput()
    {
        using var f = new Fixture(); var state = await State(f); var zip = Path.Combine(f.Root, "nested.cbz");
        using (var archive = ZipFile.Open(zip, ZipArchiveMode.Create))
        { archive.CreateEntryFromFile(Path.Combine(f.Images, "001.png"), "目录/../中文.png"); archive.CreateEntryFromFile(Path.Combine(f.Images, "002.png"), "另一处/中文.png"); }
        var root = Path.Combine(f.Root, "realized"); await using var realizer = new ArchiveEntryRealizer(root);
        var source = await new ArchiveFactory().OpenAsync(zip, Token);
        try
        {
            var entries = (await source.GetEntriesAsync(Token)).Where(e => e.IsImage()).ToArray();
            await using var files = await ArchiveEntryUtility.RealizeArchiveEntry(entries, ArchivePolicy.SendExtractFile, realizer, Token);
            Assert.Equal(2, files.Paths.Count); Assert.All(files.Paths, path => { Assert.Equal("中文.png", Path.GetFileName(path)); Assert.StartsWith(root + Path.DirectorySeparatorChar, path); });
            Assert.NotEqual(files.Paths[0], files.Paths[1]); Assert.Equal(2, files.Paths.Select(Path.GetDirectoryName).Distinct().Count());
        }
        finally { await source.DisposeAsync(); }
        Assert.Empty(Extracted(root));
        // 真实归档内目录的相对条目仍通过所属Archive映射读取。
        using (var archive = ZipFile.Open(zip, ZipArchiveMode.Update)) { archive.GetEntry("目录/../中文.png")!.Delete(); archive.CreateEntryFromFile(Path.Combine(f.Images, "001.png"), "目录/001.png"); }
        var clipboard = new Clipboard(); await using var op = Operation(f, state, realizer, clipboard);
        await op.OpenAsync(Path.Combine(zip, "目录"), Token); await op.CopyFilesAsync(token: Token);
        Assert.Equal(Path.Combine(zip, "目录", "001.png"), Assert.Single(clipboard.Content.QueryPaths)); Assert.True(File.Exists(Assert.Single(clipboard.Content.Files))); Assert.Null(op.Error);
    }

    private sealed class MemoryArchive(byte[] data) : Archive("/virtual.cbz")
    {
        public int Reads { get; private set; }
        public override Task<IReadOnlyList<ArchiveEntry>> GetEntriesAsync(CancellationToken token) => Task.FromResult<IReadOnlyList<ArchiveEntry>>([]);
        public override Task<Stream> OpenEntryAsync(ArchiveEntry entry, CancellationToken token) { Reads++; return Task.FromResult<Stream>(new MemoryStream(data)); }
        public override ValueTask DisposeAsync() { IsDisposed = true; return ValueTask.CompletedTask; }
    }
    [Fact]
    public async Task InvalidLengthOversizeAndCancelledExtractionCleanAllPreparedMaterial()
    {
        using var f = new Fixture(); var root = Path.Combine(f.Root, "realized"); await using var realizer = new ArchiveEntryRealizer(root);
        await using var source = new MemoryArchive([1, 2, 3]);
        var oversized = new ArchiveEntry(source) { RawEntryName = "big.png", Length = RealizedFilePathList.TemporaryByteBudget + 1 };
        await Assert.ThrowsAsync<NotSupportedException>(() => realizer.ExtractAsync(oversized, RealizedFilePathList.TemporaryByteBudget, Token)); Assert.Equal(0, source.Reads);
        var wrong = new ArchiveEntry(source) { RawEntryName = "wrong.png", Length = 9 };
        await Assert.ThrowsAsync<InvalidDataException>(() => realizer.ExtractAsync(wrong, 12, Token)); Assert.Empty(Extracted(root));
        var valid = new ArchiveEntry(source) { RawEntryName = "valid.png", Length = 3 };
        await Assert.ThrowsAsync<InvalidDataException>(() => ArchiveEntryUtility.RealizeArchiveEntry([valid, wrong], ArchivePolicy.SendExtractFile, realizer, Token)); Assert.Empty(Extracted(root));
        var unknown = new ArchiveEntry(source) { RawEntryName = "unknown.png", Length = -1 };
        await Assert.ThrowsAsync<InvalidDataException>(() => realizer.ExtractAsync(unknown, 2, Token)); Assert.Empty(Extracted(root));
        using var cancelled = new CancellationTokenSource(); cancelled.Cancel(); await Assert.ThrowsAnyAsync<OperationCanceledException>(() => realizer.ExtractAsync(valid, 12, cancelled.Token));
        Assert.Empty(Extracted(root)); Assert.False(new ArchiveEntry(source) { IsDirectory = true }.CanRealize()); Assert.False(new ArchiveEntry(source) { IsShortcut = true }.CanRealize());
    }

    [Fact]
    public async Task FailedRequestCleanupRemainsProcessOwnedAndBlocksFurtherExtractionUntilExitRetry()
    {
        using var f = new Fixture(); var root = Path.Combine(f.Root, "realized");
        await using var realizer = new ArchiveEntryRealizer(root); await using var source = new MemoryArchive([1, 2, 3]);
        var entry = new ArchiveEntry(source) { RawEntryName = "001.png", Length = 3 };
        var files = await ArchiveEntryUtility.RealizeArchiveEntry([entry], ArchivePolicy.SendExtractFile, realizer, Token);
        var directory = Path.GetDirectoryName(Assert.Single(files.Paths))!;
        var parked = directory + ".parked"; var outside = Directory.CreateDirectory(Path.Combine(f.Root, "outside")).FullName;
        var marker = Path.Combine(outside, "must-remain.txt"); await File.WriteAllTextAsync(marker, "保留", Token);
        Directory.Move(directory, parked); Directory.CreateSymbolicLink(directory, outside);
        try
        {
            await Assert.ThrowsAsync<AggregateException>(() => files.DisposeAsync().AsTask());
            await Assert.ThrowsAsync<IOException>(() => realizer.ExtractAsync(entry, 12, Token));
            Assert.Equal(1, source.Reads); Assert.True(File.Exists(marker));
            await Assert.ThrowsAsync<AggregateException>(() => realizer.DisposeAsync().AsTask());
            Assert.True(File.Exists(marker));
        }
        finally { Directory.Delete(directory); Directory.Move(parked, directory); }
        // 模拟固定复制 finally 报错后批次不再由调用方持有，进程后端仍可清理。
        await realizer.DisposeAsync(); Assert.False(Directory.Exists(directory)); Assert.True(File.Exists(marker));
    }

    [AvaloniaFact]
    public async Task FormalMenusAndArchiveSettingsUseEngineCapabilityAndAtomicOriginalJson()
    {
        using var f = new Fixture(); var state = await State(f); var root = Path.Combine(f.Root, "realized"); await using var realizer = new ArchiveEntryRealizer(root);
        var clipboard = new Clipboard(); var op = Operation(f, state, realizer, clipboard); var images = new BitmapFactory(new MagickImageDecoder()); AttachTransfer(f, op, images);
        var model = new ReaderWorkspaceViewModel(op, new(op), state); var window = new MainWindow(); window.Bind(model, images, new Platform()); window.Show();
        try
        {
            await window.OpenAsync(f.Zip); Assert.True(window.IsCommandAvailable("CopyFile")); Assert.True(window.IsCommandAvailable("CopyToFolderAs")); Assert.False(window.IsCommandAvailable("MoveToFolderAs"));
            var settings = new SettingsWindow(model); settings.Show(window); Assert.Equal(3, settings.FindControl<ComboBox>("ArchiveCopyPolicy")!.SelectedIndex);
            settings.FindControl<ListBox>("SettingsNavigation")!.SelectedIndex = 6;
            settings.FindControl<ComboBox>("ArchiveCopyPolicy")!.SelectedIndex = 1; settings.Close(); Assert.Equal(ArchivePolicy.SendExtractFile, Config.Current.System.ArchiveCopyPolicy);
            settings = new SettingsWindow(model); settings.Show(window); settings.FindControl<ComboBox>("ArchiveCopyPolicy")!.SelectedIndex = 2;
            var blocker = Path.Combine(f.State, "UserSetting.json.tmp"); Directory.CreateDirectory(blocker);
            try
            {
                settings.FindControl<Button>("SaveSettings")!.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
                for (int i = 0; i < 200 && settings.FindControl<TextBlock>("Message")!.Text?.StartsWith("保存失败") != true; i++)
                { await Task.Delay(10, Token); Avalonia.Threading.Dispatcher.UIThread.RunJobs(); }
                Assert.StartsWith("保存失败", settings.FindControl<TextBlock>("Message")!.Text); Assert.True(settings.IsVisible);
                Assert.Equal(ArchivePolicy.SendExtractFile, Config.Current.System.ArchiveCopyPolicy);
            }
            finally { Directory.Delete(blocker); }
            settings.FindControl<Button>("SaveSettings")!.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
            for (int i = 0; i < 100 && settings.IsVisible; i++) { await Task.Delay(10, Token); Avalonia.Threading.Dispatcher.UIThread.RunJobs(); }
            Assert.False(settings.IsVisible); Assert.Equal(ArchivePolicy.SendArchivePath, Config.Current.System.ArchiveCopyPolicy);
            var raw = JsonNode.Parse(await File.ReadAllTextAsync(Path.Combine(f.State, "UserSetting.json"), Token))!; Assert.Equal(2, raw["Config"]!["System"]!["ArchiveCopyPolicy"]!.GetValue<int>());
            settings = new SettingsWindow(model); settings.Show(window); settings.FindControl<ListBox>("SettingsNavigation")!.SelectedIndex = 6; settings.UpdateLayout();
            var phase = Environment.GetEnvironmentVariable("NEEVIEW_ACCEPTANCE_PHASE") ?? "p4-realization";
            using var bitmap = settings.CaptureRenderedFrame(); Assert.NotNull(bitmap);
            bitmap.Save(Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, $"../../../../../acceptance/{phase}-archive-settings-layout.png")), Avalonia.Media.Imaging.PngBitmapEncoderOptions.Default);
            settings.Close();
        }
        finally { await window.PrepareShutdownAsync(); window.Close(); }
    }

    [Fact]
    public async Task UnknownArchivePolicyAndFutureFieldsSurviveSaveAndCannotBeSilentlyInterpreted()
    {
        using var f = new Fixture(); Directory.CreateDirectory(f.State);
        await File.WriteAllTextAsync(Path.Combine(f.State, "UserSetting.json"), "{\"Config\":{\"System\":{\"ArchiveCopyPolicy\":99,\"Future\":\"kept\"}}}", Token);
        var state = await State(f); await using var realizer = new ArchiveEntryRealizer(Path.Combine(f.Root, "realized")); var clipboard = new Clipboard(); await using var op = Operation(f, state, realizer, clipboard);
        Assert.Equal((ArchivePolicy)99, Config.Current.System.ArchiveCopyPolicy); await op.OpenAsync(f.Zip, Token); await op.CopyFilesAsync(token: Token);
        Assert.Contains("未知", op.Error); Assert.Equal(0, clipboard.Writes); await op.SaveAsync();
        var raw = JsonNode.Parse(await File.ReadAllTextAsync(Path.Combine(f.State, "UserSetting.json"), Token))!;
        Assert.Equal(99, raw["Config"]!["System"]!["ArchiveCopyPolicy"]!.GetValue<int>()); Assert.Equal("kept", raw["Config"]!["System"]!["Future"]!.GetValue<string>());
    }
}
