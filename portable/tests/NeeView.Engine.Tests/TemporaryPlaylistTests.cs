using System.Text;
using System.Text.Json.Nodes;
using Avalonia.Headless.XUnit;
using NeeView;
using NeeView.Backends;
using NeeView.MacOS.ViewModels;
using NeeView.MacOS.Views;
namespace NeeView.Engine.Tests;

/// <summary>原多来源Paste/临时.nvpls与代理条目；只操作自建夹具和模拟剪贴板。</summary>
public sealed class TemporaryPlaylistTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;
    private sealed class Clipboard : IFileClipboard
    {
        public FileClipboardContent Content { get; set; } = new([], []);
        public bool HasFileContent => Content.Files.Count > 0 || Content.QueryPaths.Count > 0;
        public Task<FileClipboardContent> ReadAsync(CancellationToken token) => Task.FromResult(Content);
        public Task WriteAsync(FileClipboardContent content, CancellationToken token) { Content = content; return Task.CompletedTask; }
    }
    private sealed class Platform : IPlatformService
    {
        public Task RevealAsync(string path, CancellationToken token = default) => Task.CompletedTask;
        public Task TrashAsync(string path, CancellationToken token = default) => throw new InvalidOperationException("临时列表不应删除真实图片");
    }
    private sealed class DelayedTemporary(ITemporaryPlaylistService inner) : ITemporaryPlaylistService
    {
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public async Task<string> CreateAsync(IReadOnlyList<string> paths, CancellationToken token)
        { var path = await inner.CreateAsync(paths, token); Entered.SetResult(); await Release.Task; return path; }
    }
    private sealed class DelayedSourceFactory(string blockedPath) : IArchiveFactory
    {
        private readonly ArchiveFactory _inner = new();
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public PlaylistArchive? Source { get; private set; }
        public Task<Archive> OpenAsync(string path, CancellationToken token) => PlaylistSourceTools.IsPlaylist(path)
            ? Task.FromResult<Archive>(Source = new PlaylistArchive(path, this)) : _inner.OpenAsync(path, token);
        public Task<IReadOnlyList<FolderItem>> ListFoldersAsync(string path, CancellationToken token) => _inner.ListFoldersAsync(path, token);
        public Task<IReadOnlyList<FolderItem>> ListBooksAsync(string path, CancellationToken token) => _inner.ListBooksAsync(path, token);
        public async Task<FolderItem?> GetFileMetadataAsync(string path, CancellationToken token)
        {
            if (path == blockedPath) { Entered.TrySetResult(); await Release.Task; }
            // 模拟不能及时中断的来源；晚到仍遵循原请求取消，不提交新画面。
            return await _inner.GetFileMetadataAsync(path, token);
        }
    }
    private static async Task<SaveData> State(Fixture f, string? temporary = null)
    { var state = new SaveData(f.State, temporary); await state.LoadAsync(Token); return state; }
    private static string Image(Fixture f, int number) => Path.Combine(f.Images, $"{number:000}.png");
    private static BookOperation Operation(Fixture f, SaveData state, ITemporaryPlaylistService temporary, Clipboard? clipboard = null)
    { var op = f.Operation(state); op.AttachTemporaryPlaylists(temporary); if (clipboard is not null) op.AttachFileClipboard(clipboard); return op; }
    private static async Task<string> WriteList(Fixture f, JsonArray items, string format = PlaylistSource.CurrentFormat)
    {
        var path = Path.Combine(f.Root, Guid.NewGuid().ToString("N") + ".nvpls");
        await File.WriteAllTextAsync(path, new JsonObject { ["Format"] = format, ["Items"] = items, ["Future"] = 17 }.ToJsonString(), Token); return path;
    }
    private static JsonObject Item(string path, string? name = null) => new() { ["Path"] = path, ["Name"] = name };

    [Theory]
    [InlineData(PageSortMode.Entry, "003.png,001.png,003.png,002.png")]
    [InlineData(PageSortMode.EntryDescending, "002.png,003.png,001.png,003.png")]
    [InlineData(PageSortMode.FileName, "001.png,002.png,003.png,003.png")]
    public async Task PasteBuildsOneOriginalBookWithoutChangingGlobalPlaylist(PageSortMode mode, string expected)
    {
        using var f = new Fixture(); var root = Path.Combine(f.Root, "app-temp"); var state = await State(f, root);
        await using var temporary = new TemporaryPlaylistService(root);
        var paths = new[] { Image(f, 3), Image(f, 1), Image(f, 3), Image(f, 2) };
        var clipboard = new Clipboard { Content = new(["/stale-url"], paths) };
        await using var op = Operation(f, state, temporary, clipboard);
        await op.Playlists.InitializeAsync(Token); var list = op.Playlists.Current!; var registered = await op.Playlists.AddAsync(Image(f, 5), Token);
        var selected = op.Playlists.SelectedItem; var globalFile = await File.ReadAllBytesAsync(list.Path, Token);
        Config.Current.BookSettingDefault.SortMode = mode;
        await op.OpenAsync(f.Zip, Token); await new CommandTable(op).ExecuteAsync("Paste");
        Assert.Null(op.Error); Assert.True(op.Book!.Source.IsPlaylist); Assert.Equal(4, op.Book.Pages.Count);
        Assert.Equal(expected, string.Join(',', op.Book.Pages.Select(p => p.EntryName))); Assert.Equal(mode, op.Book.EffectiveSortMode);
        Assert.Equal(paths, PlaylistSourceTools.Deserialize(await File.ReadAllBytesAsync(op.Book.Path, Token)).Items.Select(i => i.Path));
        Assert.Same(list, op.Playlists.Current); Assert.Same(selected, op.Playlists.SelectedItem); Assert.Same(registered, Assert.Single(list.Items));
        Assert.Equal(globalFile, await File.ReadAllBytesAsync(list.Path, Token)); Assert.Equal(5, Directory.GetFiles(f.Images).Length);
        await op.MoveAsync(1, true); Assert.Null(op.Error); Assert.True(op.Book.Source.IsPlaylist);
    }

    [Theory]
    [InlineData(BookPageCollectMode.Image, 3)]
    [InlineData(BookPageCollectMode.ImageAndBook, 5)]
    [InlineData(BookPageCollectMode.All, 4)]
    public async Task MixedPathsAreNotPreExpandedAndMissingItemsSkipWithContinuousIds(BookPageCollectMode mode, int count)
    {
        using var f = new Fixture(); var state = await State(f); await using var temporary = new TemporaryPlaylistService(Path.Combine(f.Root, "temp"));
        var text = Path.Combine(f.Root, "note.txt"); await File.WriteAllTextAsync(text, "文字", Token);
        var paths = new[] { Image(f, 2), Path.Combine(f.Root, "missing.png"), f.Images, f.Zip, Path.Combine(f.Zip, "001.png"), text, Image(f, 2) };
        await using var op = Operation(f, state, temporary); Config.Current.System.BookPageCollectMode = mode; Config.Current.BookSettingDefault.SortMode = PageSortMode.Entry;
        await op.OpenFilesAsync(paths, Token); Assert.Null(op.Error); Assert.Equal(count, op.Book!.Pages.Count);
        // 原WherePageAll按真实SystemPath排除已包含子项的目录/归档；来源索引仍保留两项。
        if (mode == BookPageCollectMode.All)
            Assert.Equal(new[] { Image(f, 2), Path.Combine(f.Zip, "001.png"), text, Image(f, 2) }, op.Book.Pages.Select(p => p.EntryFullName));
        var archive = Assert.IsType<PlaylistArchive>(op.Book.Source); Assert.Equal(1, archive.SkippedEntryCount);
        var entries = await archive.GetEntriesAsync(Token); Assert.Equal(Enumerable.Range(0, 6), entries.Select(e => e.Id));
        Assert.Equal(paths.Where(p => !p.EndsWith("missing.png", StringComparison.Ordinal)), entries.Select(e => e.SystemPath));
        Assert.Single(entries, e => e.IsDirectory); Assert.Equal(6, entries.Count); Assert.IsType<PlaylistArchiveEntry>(entries[0]);
        await using var stream = await archive.OpenEntryAsync(entries[3], Token); using var decoded = await new MagickImageDecoder().DecodeAsync(stream, new(40, 60), Token); Assert.Equal(new Size(40, 60), decoded.Size);
    }

    [Fact]
    public async Task AliasWithoutImageExtensionKeepsRealTypeAndLogicalPathAndDirectoryCover()
    {
        using var f = new Fixture(); var list = await WriteList(f, new(Item(Image(f, 1), "封面\\别名"), Item(Path.Combine(f.Zip, "002.png"), "包内图"), Item(f.Images, "目录")));
        await using var source = await new ArchiveFactory().OpenAsync(list, Token);
        var entries = await source.GetEntriesAsync(Token); Assert.Equal(3, entries.Count);
        Assert.True(entries[0].IsImage()); Assert.Equal("封面\\别名", entries[0].EntryName); Assert.Equal(Image(f, 1), entries[0].SystemPath);
        Assert.True(entries[1].IsImage()); Assert.Equal(Path.Combine(f.Zip, "002.png"), entries[1].SystemPath); Assert.True(entries[2].IsBook());
        await using var raw = await source.OpenEntryAsync(entries[0], Token); using var data = await new MagickImageDecoder().DecodeAsync(raw, new(40, 60), Token); Assert.Equal(new Size(40, 60), data.Size);
        var state = await State(f); var page = new Page(entries[2], archives: new ArchiveFactory(), folders: state.FolderConfigs);
        using var images = new BitmapFactory(new MagickImageDecoder()); using var cover = await images.GetAsync(page, new(40, 60), Token);
        Assert.Equal(new Size(40, 60), cover.Image.Size);
        var before = await File.ReadAllBytesAsync(list, Token); await source.DisposeAsync(); Assert.Equal(before, await File.ReadAllBytesAsync(list, Token));
    }

    [Theory]
    [InlineData("NeeViewPlaylist.1")]
    [InlineData("NeeView.Playlist/2.0.0")]
    [InlineData("NeeView.Playlist/45.0.3981")]
    public async Task ArchiveAndHubUseOneLegacyParserAndDoNotRewriteSource(string format)
    {
        using var f = new Fixture(); var items = format == "NeeViewPlaylist.1" ? new JsonArray(JsonValue.Create(Image(f, 2)), JsonValue.Create(Image(f, 1))) : new JsonArray(Item(Image(f, 2)), Item(Image(f, 1)));
        var path = await WriteList(f, items, format); var before = await File.ReadAllBytesAsync(path, Token);
        await using var source = await new ArchiveFactory().OpenAsync(path, Token); Assert.Equal(new[] { "002.png", "001.png" }, (await source.GetEntriesAsync(Token)).Select(e => e.EntryName));
        var state = await State(f); var hub = state.Playlists; await hub.SwitchAsync(path, Token); Assert.Equal(new[] { Image(f, 2), Image(f, 1) }, hub.Current!.Items.Select(i => i.Path));
        Assert.Equal(before, await File.ReadAllBytesAsync(path, Token));
    }

    [Fact]
    public async Task CurrentDirectoryArchivePolicyDoesNotInterpretSlashAliasAsFolders()
    {
        using var f = new Fixture(); var state = await State(f); var path = await WriteList(f, new(Item(Image(f, 1), "章节/封面"), Item(Image(f, 2), "章节/次页")));
        Config.Current.System.ArchiveRecursiveMode = ArchiveEntryCollectionMode.CurrentDirectory; Config.Current.BookSettingDefault.SortMode = PageSortMode.Entry;
        await using var op = f.Operation(state); await op.OpenAsync(path, Token);
        Assert.Null(op.Error); Assert.Equal(new[] { "章节/封面", "章节/次页" }, op.Book!.Pages.Select(p => p.EntryName)); Assert.All(op.Book.Pages, p => Assert.True(p.IsImage));
    }

    [Fact]
    public async Task ProxyCopiesRealTargetsAndArchiveExtractionWithoutMovingOrDeletingSources()
    {
        using var f = new Fixture(); var state = await State(f); var temp = Path.Combine(f.Root, "temp");
        await using var temporary = new TemporaryPlaylistService(temp); await using var realizer = new ArchiveEntryRealizer(Path.Combine(f.Root, "realized"));
        var clipboard = new Clipboard(); await using var op = Operation(f, state, temporary, clipboard); op.AttachArchiveEntryRealizer(realizer);
        using var images = new BitmapFactory(new MagickImageDecoder()); var backend = new FileOperationBackend(Path.Combine(f.State, "Recovery")); var moves = new DestinationMoveService(backend); op.AttachFileOperations(moves, backend, images);
        Config.Current.BookSettingDefault.SortMode = PageSortMode.Entry; Config.Current.System.IsFileWriteAccessEnabled = true;
        await op.OpenFilesAsync([Image(f, 1), Path.Combine(f.Zip, "002.png")], Token);
        Assert.False(op.CanFileAction); Assert.False(op.CanDeleteFile); await op.CopyFilesAsync(token: Token); Assert.Equal(Image(f, 1), Assert.Single(clipboard.Content.Files));
        await op.JumpAsync(1); await op.CopyFilesAsync(token: Token);
        Assert.Equal(Path.Combine(f.Zip, "002.png"), Assert.Single(clipboard.Content.QueryPaths)); var extracted = Assert.Single(clipboard.Content.Files); Assert.True(File.Exists(extracted));
        Assert.Equal(await File.ReadAllBytesAsync(Image(f, 2), Token), await File.ReadAllBytesAsync(extracted, Token));
        var target = Directory.CreateDirectory(Path.Combine(f.Root, "target")).FullName; await op.CopyToFolderAsync(new("目标", target), token: Token);
        Assert.Null(op.Error); Assert.Equal(await File.ReadAllBytesAsync(Image(f, 2), Token), await File.ReadAllBytesAsync(Path.Combine(target, "002.png"), Token));
        Assert.Equal(0, moves.UndoCount); Assert.Equal(5, Directory.GetFiles(f.Images).Length); Assert.True(File.Exists(f.Zip));
    }

    [Fact]
    public async Task DuplicateArchiveReferencesShareInnerSourceAndRealizedFile()
    {
        using var f = new Fixture(); var path = await WriteList(f, new(Item(Path.Combine(f.Zip, "001.png"), "a"), Item(Path.Combine(f.Zip, "002.png"), "b"), Item(Path.Combine(f.Zip, "001.png"), "c")));
        await using var source = await new ArchiveFactory().OpenAsync(path, Token); var entries = await source.GetEntriesAsync(Token);
        Assert.Same(entries[0].TargetArchiveEntry, entries[2].TargetArchiveEntry); Assert.Same(entries[0].TargetArchiveEntry.Archive, entries[1].TargetArchiveEntry.Archive);
        await using var realizer = new ArchiveEntryRealizer(Path.Combine(f.Root, "realized"));
        await using var files = await ArchiveEntryUtility.RealizeArchiveEntry(entries, ArchivePolicy.SendExtractFile, realizer, Token);
        Assert.Equal(2, files.Paths.Count); Assert.Equal(new[] { "001.png", "002.png" }, files.Paths.Select(Path.GetFileName));
        await source.DisposeAsync(); Assert.All(files.Paths, p => Assert.True(File.Exists(p)));
    }

    [Fact]
    public async Task RuntimeHistorySurvivesButSavedHistoryExcludesTempAndStartupDoesNotRestoreIt()
    {
        using var f = new Fixture(); var root = Path.Combine(f.Root, "app-temp"); var state = await State(f, root); await using var temporary = new TemporaryPlaylistService(root);
        var op = Operation(f, state, temporary); await op.OpenFilesAsync([Image(f, 1), Image(f, 2)], Token); var path = op.Book!.Path;
        op.Book.MementoControl.IsPageChangeCountEnabled = false; await op.SaveAsync(); Assert.Contains(state.HistoryEntries, e => e.Path == path);
        Assert.DoesNotContain(JsonNode.Parse(await File.ReadAllTextAsync(Path.Combine(f.State, "History.json"), Token))!["Items"]!.AsArray(), e => e!["Path"]!.GetValue<string>() == path);
        Assert.Equal(path, state.LastBookPath); Assert.True(state.IsTemporaryPath(path)); Assert.False(state.IsTemporaryPath(root + "-user/book.nvpls"));
        await op.DisposeAsync(); Assert.True(File.Exists(path));
        var restarted = await State(f, root); await using var next = Operation(f, restarted, temporary); await next.RestoreLastAsync(Token); Assert.Null(next.Book); Assert.Null(next.Error);
        await temporary.DisposeAsync(); Assert.False(File.Exists(path));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task LateTemporaryPreparationCannotOverrideNewerOpenOrCommitOnClose(bool close)
    {
        using var f = new Fixture(); var state = await State(f); await using var temporary = new TemporaryPlaylistService(Path.Combine(f.Root, "temp")); var delayed = new DelayedTemporary(temporary);
        var clipboard = new Clipboard { Content = new([Image(f, 1), Image(f, 2)], []) }; var op = Operation(f, state, delayed, clipboard); await op.OpenAsync(f.Zip, Token);
        var paste = op.PasteFilesAsync(Token); await delayed.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5), Token);
        if (close)
        {
            var exit = op.DisposeAsync().AsTask(); Assert.False(exit.IsCompleted); delayed.Release.SetResult(); await paste; await exit; Assert.Null(op.Book);
        }
        else
        {
            await op.OpenAsync(f.Images, Token); var book = op.Book; await op.OpenAsync(Path.Combine(f.Root, "missing.png"), Token); var error = op.Error;
            delayed.Release.SetResult(); await paste; Assert.Same(book, op.Book); Assert.Equal(error, op.Error); await op.DisposeAsync();
        }
    }

    [Fact]
    public async Task NewerMultiOpenWinsDuringOlderPreparation()
    {
        using var f = new Fixture(); var state = await State(f); await using var temporary = new TemporaryPlaylistService(Path.Combine(f.Root, "temp")); var delayed = new DelayedTemporary(temporary);
        await using var op = Operation(f, state, delayed); var older = op.OpenFilesAsync([Image(f, 1), Image(f, 2)], Token); await delayed.Entered.Task;
        op.AttachTemporaryPlaylists(temporary); await op.OpenFilesAsync([Image(f, 3), Image(f, 4)], Token); var book = op.Book;
        delayed.Release.SetResult(); await older; Assert.Same(book, op.Book); Assert.Equal(new[] { Image(f, 3), Image(f, 4) }, op.Book!.Pages.Select(p => p.EntryFullName)); Assert.Null(op.Error);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CancelDuringSourceIndexingDisposesLateSourceAndDoesNotCommit(bool close)
    {
        using var f = new Fixture(); var state = await State(f); var factory = new DelayedSourceFactory(Image(f, 2));
        await using var temporary = new TemporaryPlaylistService(Path.Combine(f.Root, "temp"));
        var op = new BookOperation(factory, new MagickImageDecoder(), state); op.AttachTemporaryPlaylists(temporary);
        await op.OpenAsync(f.Zip, Token); var opening = op.OpenFilesAsync([Image(f, 1), Image(f, 2)], Token);
        await factory.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5), Token); var late = factory.Source!;
        try
        {
            if (close) await op.DisposeAsync(); else await op.OpenAsync(f.Images, Token);
            var retained = op.Book; factory.Release.SetResult(); await opening;
            Assert.Same(retained, op.Book); Assert.True(late.IsDisposed); Assert.Null(op.Error);
            if (close) Assert.Null(op.Book); else Assert.Equal(f.Images, op.Book!.Path);
        }
        finally { factory.Release.TrySetResult(); await opening; await op.DisposeAsync(); }
    }

    [Fact]
    public async Task TemporaryServicesAreIsolatedAndCancelledPreparationDoesNotPublishFile()
    {
        using var f = new Fixture(); var root = Path.Combine(f.Root, "temp"); await using var first = new TemporaryPlaylistService(root); await using var second = new TemporaryPlaylistService(root);
        var a = await first.CreateAsync([Image(f, 1), Image(f, 1)], Token); var b = await second.CreateAsync([Image(f, 2)], Token);
        var c = await first.CreateAsync([Image(f, 1), Image(f, 1)], Token); Assert.NotEqual(a, c);
        using var cancelled = new CancellationTokenSource(); cancelled.Cancel(); await Assert.ThrowsAnyAsync<OperationCanceledException>(() => first.CreateAsync([Image(f, 1)], cancelled.Token));
        Assert.Equal(3, Directory.GetFiles(root, "*.nvpls", SearchOption.AllDirectories).Length);
        await first.DisposeAsync(); Assert.False(File.Exists(a)); Assert.False(File.Exists(c)); Assert.True(File.Exists(b)); await second.DisposeAsync(); Assert.False(File.Exists(b));
    }

    [Fact]
    public async Task TemporaryExitFailureDoesNotDeleteOutsideAndCanRetry()
    {
        using var f = new Fixture(); await using var temporary = new TemporaryPlaylistService(Path.Combine(f.Root, "temp")); var path = await temporary.CreateAsync([Image(f, 1), Image(f, 2)], Token);
        var directory = Path.GetDirectoryName(path)!; var parked = directory + ".parked"; var outside = Directory.CreateDirectory(Path.Combine(f.Root, "outside")).FullName;
        var marker = Path.Combine(outside, "keep.txt"); await File.WriteAllTextAsync(marker, "保留", Token); Directory.Move(directory, parked); Directory.CreateSymbolicLink(directory, outside);
        try
        {
            await Assert.ThrowsAsync<IOException>(() => temporary.DisposeAsync().AsTask());
            await Assert.ThrowsAsync<IOException>(() => temporary.CreateAsync([Image(f, 1)], Token)); Assert.True(File.Exists(marker));
        }
        finally { Directory.Delete(directory); Directory.Move(parked, directory); }
        await temporary.DisposeAsync(); Assert.False(File.Exists(path)); Assert.True(File.Exists(marker));
    }

    [Fact]
    public async Task InvalidPlaylistFormatDoesNotReplaceCurrentBook()
    {
        using var f = new Fixture(); var state = await State(f); var path = await WriteList(f, new(Item(Image(f, 1))), "NeeView.Playlist/999.0");
        await using var op = f.Operation(state); await op.OpenAsync(f.Images, Token); var book = op.Book; var bytes = await File.ReadAllBytesAsync(path, Token);
        await op.OpenAsync(path, Token); Assert.Same(book, op.Book); Assert.Contains("不支持", op.Error); Assert.Equal(bytes, await File.ReadAllBytesAsync(path, Token));
    }

    [AvaloniaFact]
    public async Task FormalHostUsesOneMultiOpenChainAndEntryCommandsFollowSourceCapability()
    {
        using var f = new Fixture(); var state = await State(f); await using var temporary = new TemporaryPlaylistService(Path.Combine(f.Root, "temp")); var op = Operation(f, state, temporary);
        var images = new BitmapFactory(new MagickImageDecoder()); var model = new ReaderWorkspaceViewModel(op, new(op), state); var window = new MainWindow(); window.Bind(model, images, new Platform()); window.Show();
        try
        {
            await window.OpenAsync(f.Images); Assert.False(window.IsCommandAvailable("SetSortModeEntry"));
            await window.OpenFilesAsync([Image(f, 3), Image(f, 1), Image(f, 3)]); Assert.True(window.IsCommandAvailable("SetSortModeEntry"));
            await window.ExecuteAsync("SetSortModeEntry"); Assert.Equal(new[] { "003.png", "001.png", "003.png" }, op.Book!.Pages.Select(p => p.EntryName));
            await window.ExecuteAsync("SetSortModeEntryDescending"); Assert.Equal(PageSortMode.EntryDescending, op.Book.EffectiveSortMode);
            Assert.False(window.IsCommandAvailable("DeleteFile")); Assert.False(window.IsCommandAvailable("CutFile")); Assert.False(window.IsCommandAvailable("MoveToFolderAs"));
        }
        finally { await window.PrepareShutdownAsync(); window.Close(); }
    }
}
