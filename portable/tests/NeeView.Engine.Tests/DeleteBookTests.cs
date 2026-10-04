using System.Text.Json.Nodes;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using NeeView;
using NeeView.Backends;
using NeeView.MacOS.ViewModels;
using NeeView.MacOS.Views;
namespace NeeView.Engine.Tests;

/// <summary>整本删除的真实临时目录/文件与模拟废纸篓；不触碰用户图片或AppKit废纸篓。</summary>
public sealed class DeleteBookTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;
    private static TaskCompletionSource Signal() => new(TaskCreationOptions.RunContinuationsAsynchronously);
    private sealed class Trash(string root) : IPlatformService
    {
        public List<string> Calls { get; } = [];
        public Func<Task>? Before { get; init; }
        public Exception? Failure { get; init; }
        public Action? After { get; init; }
        public Task RevealAsync(string path, CancellationToken token = default) => Task.CompletedTask;
        public async Task TrashAsync(string path, CancellationToken token = default)
        {
            token.ThrowIfCancellationRequested(); Calls.Add(path);
            if (Before is not null) await Before();
            if (Failure is not null) throw Failure;
            var attributes = File.GetAttributes(path);
            if ((attributes & FileAttributes.ReparsePoint) != 0) throw new NotSupportedException("链接不作为普通实体删除");
            Directory.CreateDirectory(root); var target = Path.Combine(root, Path.GetFileName(path));
            if ((attributes & FileAttributes.Directory) != 0) Directory.Move(path, target); else File.Move(path, target);
            After?.Invoke();
        }
    }
    private static async Task<SaveData> State(Fixture f, string? temporary = null)
    { var state = new SaveData(f.State, temporary); await state.LoadAsync(Token); return state; }
    private static Trash Attach(Fixture f, BookOperation op, BitmapFactory images, Trash? trash = null)
    {
        trash ??= new(Path.Combine(f.Root, "trash")); op.AttachFileDeletion(trash, images);
        var backend = new FileOperationBackend(Path.Combine(f.State, "Recovery")); op.AttachFileOperations(new(backend), backend, images);
        Config.Current.System.IsFileWriteAccessEnabled = true; op.ConfirmDeleteBookAsync = _ => Task.FromResult(true); return trash;
    }
    private static string[] Shelf(Fixture f, int count = 3)
    {
        var parent = Directory.CreateDirectory(Path.Combine(f.Root, "shelf")).FullName;
        return Enumerable.Range(0, count).Select(i =>
        { var path = Directory.CreateDirectory(Path.Combine(parent, ((char)('a' + i)).ToString())).FullName; File.Copy(Path.Combine(f.Images, "001.png"), Path.Combine(path, "001.png")); return path; }).ToArray();
    }

    [Theory]
    [InlineData("directory")]
    [InlineData("archive")]
    [InlineData("playlist")]
    public async Task DeleteBookTrashesWholeRootAndKeepsHistoryBookmarksAndReferencedFiles(string kind)
    {
        using var f = new Fixture(); var state = await State(f); using var images = new BitmapFactory(new MagickImageDecoder()); await using var op = f.Operation(state); var trash = Attach(f, op, images);
        string path = kind == "directory" ? f.Images : f.Zip;
        if (kind == "playlist")
        { path = Path.Combine(f.Root, "user.nvpls"); await File.WriteAllBytesAsync(path, PlaylistSourceTools.CreateTemporarySource([Path.Combine(f.Images, "001.png"), Path.Combine(f.Images, "002.png")]), Token); }
        await op.OpenAsync(path, Token); var book = op.Book!; await op.JumpAsync(1); await op.SaveAsync();
        await state.RegisterBookmarkAsync(book, token: Token); await op.SaveAsync();
        await new CommandTable(op).ExecuteAsync("DeleteBook");
        Assert.Equal(path, Assert.Single(trash.Calls)); Assert.Null(op.Book); Assert.True(book.Source.IsDisposed); Assert.Null(op.Frame); Assert.Null(state.LastBookPath);
        Assert.False(File.Exists(path) || Directory.Exists(path)); Assert.Contains(state.HistoryEntries, item => item.Path == path);
        Assert.Contains(state.BookmarkRoot.Children!, item => item.Path == path); Assert.Equal(0, op.DestinationMoves!.UndoCount);
        if (kind == "directory") Assert.Equal(5, Directory.GetFiles(Path.Combine(f.Root, "trash", Path.GetFileName(f.Images))).Length);
        else Assert.Equal(5, Directory.GetFiles(f.Images).Length);
    }

    [Theory]
    [InlineData(0, true, 1)]
    [InlineData(2, true, 1)]
    [InlineData(1, false, -1)]
    public async Task NeighborFollowsOriginalNextThenPreviousAndToggle(int index, bool enabled, int next)
    {
        using var f = new Fixture(); var state = await State(f); var paths = Shelf(f); using var images = new BitmapFactory(new MagickImageDecoder()); await using var op = f.Operation(state); var trash = Attach(f, op, images);
        Config.Current.Bookshelf.IsOpenNextBookWhenRemove = enabled; await op.OpenAsync(paths[index], Token); await op.Bookshelf.SyncAsync(op.Book!, Token);
        await op.DeleteBookAsync(Token); Assert.Equal(paths[index], Assert.Single(trash.Calls)); Assert.DoesNotContain(op.Bookshelf.Items, item => item.Path == paths[index]);
        if (next < 0) { Assert.Null(op.Book); Assert.Null(state.LastBookPath); }
        else { Assert.Equal(paths[next], op.Book!.Path); Assert.Equal(paths[next], op.Bookshelf.SelectedItem!.Path); }
    }

    [Fact]
    public async Task SingleItemNeighborIsEmptyAndFallbackDoesNotDeleteSelectedBook()
    {
        using var f = new Fixture(); var state = await State(f); var paths = Shelf(f, 1); using var images = new BitmapFactory(new MagickImageDecoder()); await using var op = f.Operation(state); var trash = Attach(f, op, images);
        await op.OpenAsync(f.Zip, Token); await op.Bookshelf.SetPlaceAsync(Path.GetDirectoryName(paths[0])!, paths[0], Token);
        Assert.Null(op.Bookshelf.GetNextItem(paths[0], true)); Assert.Equal(paths[0], op.Bookshelf.GetNextItem(f.Zip, true)!.Path); Assert.Null(op.Bookshelf.GetNextItem(f.Zip, false));
        await op.DeleteBookAsync(Token); Assert.Equal(f.Zip, Assert.Single(trash.Calls)); Assert.Equal(paths[0], op.Book!.Path); Assert.True(Directory.Exists(paths[0]));
    }

    [Theory]
    [InlineData("disabled")]
    [InlineData("declined")]
    [InlineData("no-host")]
    [InlineData("cancelled")]
    public async Task NoAuthorizationKeepsOriginalSourceAndDoesNotNavigate(string reason)
    {
        using var f = new Fixture(); var state = await State(f); using var images = new BitmapFactory(new MagickImageDecoder()); await using var op = f.Operation(state); var trash = Attach(f, op, images);
        await op.OpenAsync(f.Images, Token); var book = op.Book;
        if (reason == "disabled") Config.Current.System.IsFileWriteAccessEnabled = false;
        if (reason == "declined") op.ConfirmDeleteBookAsync = _ => Task.FromResult(false);
        if (reason == "no-host") op.ConfirmDeleteBookAsync = null;
        using var cancelled = new CancellationTokenSource(); if (reason == "cancelled") cancelled.Cancel();
        await op.DeleteBookAsync(cancelled.Token); Assert.Same(book, op.Book); Assert.False(book!.Source.IsDisposed); Assert.Empty(trash.Calls); Assert.Equal(5, Directory.GetFiles(f.Images).Length);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SystemFailureOrCancellationReopensOriginalSettingsPageSearchAndLock(bool cancel)
    {
        using var f = new Fixture(); var state = await State(f); using var images = new BitmapFactory(new MagickImageDecoder()); await using var op = f.Operation(state);
        var trash = Attach(f, op, images, new(Path.Combine(f.Root, "trash")) { Failure = cancel ? new OperationCanceledException() : new IOException("卷不支持废纸篓") });
        await op.OpenAsync(f.Images, Token); await op.SearchPagesAsync("001 /or 003", op.Book, Token); await op.JumpAsync(1); op.SetBookLock(true); var old = op.Book!;
        await op.DeleteBookAsync(Token); Assert.Single(trash.Calls); Assert.True(old.Source.IsDisposed); Assert.NotSame(old, op.Book);
        Assert.Equal(f.Images, op.Book!.Path); Assert.Equal("003.png", op.Book.CurrentPage!.EntryName); Assert.Equal("001 /or 003", op.Book.Pages.SearchKeyword); Assert.True(op.IsBookLocked);
        if (!cancel) Assert.Contains("卷不支持废纸篓", op.Error); Assert.Equal(5, Directory.GetFiles(f.Images).Length);
    }

    [Theory]
    [InlineData("book")]
    [InlineData("permission")]
    [InlineData("close")]
    public async Task LateConfirmationDoesNotDeleteChangedOrClosedBook(string change)
    {
        using var f = new Fixture(); var state = await State(f); using var images = new BitmapFactory(new MagickImageDecoder()); var op = f.Operation(state); var trash = Attach(f, op, images);
        await op.OpenAsync(f.Images, Token); var entered = Signal(); var confirm = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        op.ConfirmDeleteBookAsync = _ => { entered.SetResult(); return confirm.Task; }; var action = op.DeleteBookAsync(Token); await entered.Task.WaitAsync(TimeSpan.FromSeconds(5), Token);
        try
        {
            if (change == "book") await op.OpenAsync(f.Zip, Token);
            if (change == "permission") Config.Current.System.IsFileWriteAccessEnabled = false;
            if (change == "close") await op.DisposeAsync();
            confirm.TrySetResult(true); await action; Assert.Empty(trash.Calls); Assert.True(Directory.Exists(f.Images));
        }
        finally { confirm.TrySetResult(false); await action; await op.DisposeAsync(); }
    }

    [Fact]
    public async Task PhysicalScopeRejectsTemporaryRootProfileAncestorAndArchiveInternalDirectory()
    {
        using var f = new Fixture(); var state = await State(f, Path.Combine(f.Root, "app-temp")); using var images = new BitmapFactory(new MagickImageDecoder()); await using var op = f.Operation(state); var trash = Attach(f, op, images);
        await op.OpenAsync(f.Root, Token); Assert.False(op.CanDeleteBook); await op.DeleteBookAsync(Token);
        var list = Path.Combine(f.Root, "app-temp", "temporary.nvpls"); Directory.CreateDirectory(Path.GetDirectoryName(list)!); await File.WriteAllBytesAsync(list, PlaylistSourceTools.CreateTemporarySource([Path.Combine(f.Images, "001.png")]), Token);
        await op.OpenAsync(list, Token); Assert.False(op.CanDeleteBook); await op.DeleteBookAsync(Token);
        var nested = Path.Combine(f.Root, "nested.cbz"); using (var archive = System.IO.Compression.ZipFile.Open(nested, System.IO.Compression.ZipArchiveMode.Create)) archive.CreateEntry("chapter/001.png");
        await op.OpenAsync(Path.Combine(nested, "chapter"), Token); Assert.False(op.CanDeleteBook); await op.DeleteBookAsync(Token);
        Assert.Empty(trash.Calls); Assert.True(Directory.Exists(f.Images)); Assert.True(File.Exists(list)); Assert.True(File.Exists(nested));
    }

    [Fact]
    public async Task TrashingDirectoryMovesContainedLinkWithoutTouchingExternalTarget()
    {
        using var f = new Fixture(); var state = await State(f); using var images = new BitmapFactory(new MagickImageDecoder()); await using var op = f.Operation(state); Attach(f, op, images);
        var external = Path.Combine(f.Root, "external.png"); var original = await File.ReadAllBytesAsync(Path.Combine(f.Images, "001.png"), Token);
        await File.WriteAllBytesAsync(external, original, Token); File.CreateSymbolicLink(Path.Combine(f.Images, "external-link.png"), external);
        await op.OpenAsync(f.Images, Token); await op.DeleteBookAsync(Token);
        Assert.False(Directory.Exists(f.Images)); Assert.Equal(original, await File.ReadAllBytesAsync(external, Token));
        Assert.Equal(external, new FileInfo(Path.Combine(f.Root, "trash", Path.GetFileName(f.Images), "external-link.png")).LinkTarget);
    }

    [Theory]
    [InlineData("case")]
    [InlineData("system-link")]
    public async Task ProfileAncestorAliasesAreRejectedBeforeConfirmation(string kind)
    {
        using var f = new Fixture(); var state = await State(f); using var images = new BitmapFactory(new MagickImageDecoder()); await using var op = f.Operation(state); var trash = Attach(f, op, images);
        var factory = new ArchiveFactory(); var physical = await factory.GetPhysicalPathAsync(f.Root, Token);
        var alias = kind == "case" ? Path.Combine(Path.GetDirectoryName(f.Root)!, Path.GetFileName(f.Root).ToUpperInvariant()) : physical;
        if (!Directory.Exists(alias)) alias = physical; // 大小写敏感卷不假定别名存在，仍核对实际路径保护。
        var confirmations = 0; op.ConfirmDeleteBookAsync = _ => { confirmations++; return Task.FromResult(true); };
        await op.OpenAsync(alias, Token); var book = op.Book; await op.DeleteBookAsync(Token);
        Assert.Same(book, op.Book); Assert.Empty(trash.Calls); Assert.Equal(0, confirmations); Assert.True(Directory.Exists(f.Root));
        Assert.Equal(physical, await factory.GetPhysicalPathAsync(f.Root, Token));
    }

    [Fact]
    public async Task TemporaryPlaylistAliasIsRejectedAndMissingProfileUsesExistingAncestor()
    {
        using var f = new Fixture(); var temporary = Directory.CreateDirectory(Path.Combine(f.Root, "app-temp")).FullName;
        var list = Path.Combine(temporary, "source.nvpls"); await File.WriteAllBytesAsync(list, PlaylistSourceTools.CreateTemporarySource([Path.Combine(f.Images, "001.png")]), Token);
        var state = await State(f, temporary); using var images = new BitmapFactory(new MagickImageDecoder()); await using var op = f.Operation(state); var trash = Attach(f, op, images);
        var factory = new ArchiveFactory(); var actual = await factory.GetPhysicalPathAsync(list, Token); var physicalRoot = await factory.GetPhysicalPathAsync(f.Root, Token);
        Assert.Equal(Path.Combine(physicalRoot, "not-created", "profile"), await factory.GetPhysicalPathAsync(Path.Combine(f.Root, "not-created", "profile"), Token));
        await op.OpenAsync(actual, Token); await op.DeleteBookAsync(Token); Assert.Empty(trash.Calls); Assert.True(File.Exists(list)); Assert.NotNull(op.Book);
    }

    [Theory]
    [InlineData("changed")]
    [InlineData("link")]
    [InlineData("missing")]
    public async Task RechecksEntityAfterConfirmationWithoutTouchingReplacement(string change)
    {
        using var f = new Fixture(); var state = await State(f); using var images = new BitmapFactory(new MagickImageDecoder()); await using var op = f.Operation(state); var trash = Attach(f, op, images);
        await op.OpenAsync(f.Zip, Token); var book = op.Book;
        op.ConfirmDeleteBookAsync = async _ =>
        {
            if (change == "changed") File.SetLastWriteTime(f.Zip, DateTime.Now.AddDays(-1));
            else { File.Delete(f.Zip); if (change == "link") File.CreateSymbolicLink(f.Zip, Path.Combine(f.Images, "001.png")); }
            await Task.Yield(); return true;
        };
        await op.DeleteBookAsync(Token); Assert.Empty(trash.Calls); Assert.Same(book, op.Book); Assert.False(book!.Source.IsDisposed); Assert.NotNull(op.Error); Assert.Equal(5, Directory.GetFiles(f.Images).Length);
    }

    [Fact]
    public async Task AlreadyAuthorizedSystemWorkWaitsOnCloseAndLateCancelStillClearsStartup()
    {
        using var f = new Fixture(); var state = await State(f); using var images = new BitmapFactory(new MagickImageDecoder()); var op = f.Operation(state); var entered = Signal(); var release = Signal(); using var cancellation = new CancellationTokenSource();
        var trash = Attach(f, op, images, new(Path.Combine(f.Root, "trash")) { Before = async () => { entered.SetResult(); await release.Task; }, After = cancellation.Cancel });
        await op.OpenAsync(f.Images, Token); var action = op.DeleteBookAsync(cancellation.Token); await entered.Task.WaitAsync(TimeSpan.FromSeconds(5), Token);
        try
        {
            Assert.Null(op.Book); Assert.False(op.CanUnload); await op.DeleteBookAsync(Token); Assert.Single(trash.Calls);
            var closing = op.DisposeAsync().AsTask(); Assert.False(closing.IsCompleted); release.SetResult(); await action; await closing;
            Assert.Null(state.LastBookPath); Assert.True(cancellation.IsCancellationRequested); Assert.False(Directory.Exists(f.Images));
        }
        finally { release.TrySetResult(); await action; await op.DisposeAsync(); }
    }

    [Fact]
    public async Task NewOpenDuringSystemWorkWinsOverAutomaticNextBook()
    {
        using var f = new Fixture(); var state = await State(f); var paths = Shelf(f); using var images = new BitmapFactory(new MagickImageDecoder()); await using var op = f.Operation(state); var entered = Signal(); var release = Signal();
        Attach(f, op, images, new(Path.Combine(f.Root, "trash")) { Before = async () => { entered.SetResult(); await release.Task; } });
        await op.OpenAsync(paths[0], Token); await op.Bookshelf.SyncAsync(op.Book!, Token); var action = op.DeleteBookAsync(Token); await entered.Task.WaitAsync(TimeSpan.FromSeconds(5), Token);
        var opening = op.OpenAsync(f.Zip, Token);
        try { release.SetResult(); await action; await opening; Assert.Equal(f.Zip, op.Book!.Path); Assert.Null(op.Error); }
        finally { release.TrySetResult(); await action; await opening; }
    }

    [Fact]
    public async Task StateFailureReportsRealDeletionAndRetryClearsDeletedLastBook()
    {
        using var f = new Fixture(); var state = await State(f); using var images = new BitmapFactory(new MagickImageDecoder()); await using var op = f.Operation(state);
        var blocker = Path.Combine(f.State, "UserSetting.json.tmp"); Attach(f, op, images, new(Path.Combine(f.Root, "trash")) { After = () => Directory.CreateDirectory(blocker) });
        await op.OpenAsync(f.Images, Token);
        try
        {
            await op.DeleteBookAsync(Token); Assert.Null(op.Book); Assert.False(Directory.Exists(f.Images)); Assert.Contains("书籍已移至废纸篓", op.Error); Assert.Equal(f.Images, state.LastBookPath);
        }
        finally { Directory.Delete(blocker); }
        await op.SaveAsync(); Assert.Null(state.LastBookPath); var restarted = await State(f); Assert.Null(restarted.LastBookPath); Assert.Contains(restarted.HistoryEntries, item => item.Path == f.Images);
    }

    [AvaloniaFact]
    public async Task FormalBookMenuConfirmCancelSuccessAndSettingsUseOriginalFields()
    {
        using var f = new Fixture(); var state = await State(f); var op = f.Operation(state); var images = new BitmapFactory(new MagickImageDecoder()); var trash = Attach(f, op, images);
        var model = new ReaderWorkspaceViewModel(op, new(op), state); var window = new MainWindow(); window.Bind(model, images, trash); window.Show();
        try
        {
            await window.OpenAsync(f.Images); Assert.True(window.IsCommandAvailable("DeleteBook")); Assert.False(window.IsCommandAvailable("CutBook"));
            var action = window.ExecuteAsync("DeleteBook"); await WaitAsync(() => window.OwnedWindows.Any()); Assert.Single(window.OwnedWindows).Close(false); await action; Assert.Empty(trash.Calls); Assert.NotNull(op.Book);
            var settings = new SettingsWindow(model); var result = settings.ShowDialog(window); settings.FindControl<ListBox>("SettingsNavigation")!.SelectedIndex = 6;
            var next = settings.FindControl<CheckBox>("OpenNextBookWhenRemove")!; Assert.True(next.IsChecked); next.IsChecked = false;
            settings.FindControl<Button>("SaveSettings")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); await result; Assert.False(Config.Current.Bookshelf.IsOpenNextBookWhenRemove);
            var saved = JsonNode.Parse(await File.ReadAllTextAsync(Path.Combine(f.State, "UserSetting.json"), Token))!; Assert.False(saved["Config"]!["Bookshelf"]!["IsOpenNextBookWhenRemove"]!.GetValue<bool>());
            action = window.ExecuteAsync("DeleteBook"); await WaitAsync(() => window.OwnedWindows.Any()); var dialog = Assert.Single(window.OwnedWindows); Dispatcher.UIThread.RunJobs(); dialog.UpdateLayout();
            using (var bitmap = new RenderTargetBitmap(new PixelSize((int)Math.Ceiling(dialog.Bounds.Width), (int)Math.Ceiling(dialog.Bounds.Height))))
            { bitmap.Render(dialog); bitmap.Save(Output("book-confirm.png"), PngBitmapEncoderOptions.Default); }
            dialog.Close(true); await action; Assert.Single(trash.Calls); Assert.Null(op.Book); Assert.False(window.IsCommandAvailable("DeleteBook"));
        }
        finally { await window.PrepareShutdownAsync(); window.Close(); }
    }
    private static string Output(string suffix)
    {
        var directory = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "acceptance"); Directory.CreateDirectory(directory);
        return Path.Combine(directory, (Environment.GetEnvironmentVariable("NEEVIEW_ACCEPTANCE_PHASE") ?? "p4-delete-book") + "-" + suffix);
    }
    private static async Task WaitAsync(Func<bool> ready)
    {
        var limit = DateTime.UtcNow.AddSeconds(5);
        while (!ready() && DateTime.UtcNow < limit) { Dispatcher.UIThread.RunJobs(); await Task.Delay(10, Token); }
        Assert.True(ready());
    }
}
