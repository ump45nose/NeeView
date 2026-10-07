using System.Text.Json;
using System.Text.Json.Nodes;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using NeeView;
using NeeView.Backends;
using NeeView.MacOS.ViewModels;
using NeeView.MacOS.Views;
namespace NeeView.Engine.Tests;

/// <summary>原书籍改名语义和真实临时目录/归档的路径联动；不触碰用户资源。</summary>
public sealed class RenameBookTests
{
    private sealed class ObservedBackend(FileOperationBackend inner) : IFileOperationBackend, IBookRenameBackend
    {
        public Func<Task>? Before { get; init; }
        public Action? After { get; init; }
        public Exception? Failure { get; set; }
        public int Calls { get; private set; }
        public Task<bool> FileExistsAsync(string path, CancellationToken token) => inner.FileExistsAsync(path, token);
        public Task<FileTransferResult> TransferAsync(FileTransferRequest request, CancellationToken token) => inner.TransferAsync(request, token);
        public Task ReleaseAsync(FileTransferResult result) => inner.ReleaseAsync(result);
        public Task<IReadOnlyList<string>> RecoverAsync(CancellationToken token = default) => inner.RecoverAsync(token);
        public Task CreateDirectoryAsync(string parent, string name, CancellationToken token) => inner.CreateDirectoryAsync(parent, name, token);
        public Task<BookRenameTarget> GetRenameTargetAsync(string path, CancellationToken token) => inner.GetRenameTargetAsync(path, token);
        public Task<BookRenamePlan> PlanRenameAsync(BookRenameTarget target, string name, CancellationToken token) => inner.PlanRenameAsync(target, name, token);
        public Task<bool?> WasRenamedAsync(BookRenamePlan plan, CancellationToken token) => inner.WasRenamedAsync(plan, token);
        public async Task RenameAsync(BookRenamePlan plan, CancellationToken token)
        { Calls++; if (Before is not null) await Before(); if (Failure is not null) throw Failure; await inner.RenameAsync(plan, token); After?.Invoke(); }
    }
    private static FileOperationBackend Backend(Fixture f) => new(Path.Combine(f.State, "FileRecovery"));
    private static void Attach(BookOperation op, BitmapFactory images, IFileOperationBackend backend, string? name = "新书")
    {
        op.AttachFileOperations(new(backend), backend, images); Config.Current.System.IsFileWriteAccessEnabled = true;
        op.AskBookNameAsync = _ => Task.FromResult(name); op.ConfirmBookRenameAsync = _ => Task.FromResult(true);
    }
    private static string Marker(Fixture f) => Path.Combine(f.State, ".book-rename-pending.json");
    private static async Task SeedAsync(Fixture f, string source)
    {
        Directory.CreateDirectory(f.State);
        await File.WriteAllTextAsync(Path.Combine(f.State, "History.json"), new JsonObject { ["UnknownRoot"] = "kept", ["Items"] = new JsonArray(
            new JsonObject { ["Path"] = source, ["Page"] = "003.png", ["Props"] = "SinglePage Future=kept", ["Future"] = "kept" },
            new JsonObject { ["Path"] = source + "/子书", ["Page"] = "002.png" },
            new JsonObject { ["Path"] = source + "-similar", ["Page"] = "004.png" }) }.ToJsonString(), TestContext.Current.CancellationToken);
        await File.WriteAllTextAsync(Path.Combine(f.State, "Bookmark.json"), new JsonObject { ["Nodes"] = new JsonObject { ["Children"] = new JsonArray(
            new JsonObject { ["Name"] = "自定义", ["Path"] = source, ["Page"] = "003.png", ["Props"] = "Future=kept", ["Future"] = source },
            new JsonObject { ["Children"] = new JsonArray(new JsonObject { ["Path"] = source + "/子书" }) }) } }.ToJsonString(), TestContext.Current.CancellationToken);
        await File.WriteAllTextAsync(Path.Combine(f.State, QuickAccessCollection.FileName), new JsonObject { ["Items"] = new JsonArray(new JsonObject { ["Name"] = "快速别名", ["Path"] = source, ["Future"] = source }) }.ToJsonString(), TestContext.Current.CancellationToken);
        await File.WriteAllTextAsync(Path.Combine(f.State, FolderConfigCollection.FileName), new JsonObject { ["Folders"] = new JsonArray(
            new JsonObject { ["Place"] = source, ["Parameter"] = new JsonObject { ["FolderOrder"] = 1, ["Seed"] = 12, ["Future"] = "kept" }, ["Thumbs"] = new JsonObject { ["子书"] = "002.png" } },
            new JsonObject { ["Place"] = f.Root, ["Thumbs"] = new JsonObject { [Path.GetFileName(source)] = source + "/003.png" } }) }.ToJsonString(), TestContext.Current.CancellationToken);
        var folder = Path.Combine(f.State, "Playlists"); Directory.CreateDirectory(folder);
        foreach (var name in new[] { "Default.nvpls", "其他.nvpls" })
            await File.WriteAllTextAsync(Path.Combine(folder, name), new JsonObject { ["Format"] = PlaylistSource.CurrentFormat, ["Future"] = "kept", ["Items"] = new JsonArray(
                new JsonObject { ["Path"] = source + "/003.png", ["Name"] = "列表别名", ["Future"] = source },
                new JsonObject { ["Path"] = source + "-similar/004.png" }) }.ToJsonString(), TestContext.Current.CancellationToken);
    }
    [Theory]
    [InlineData("directory")]
    [InlineData("archive")]
    [InlineData("image")]
    public async Task CurrentBookRenamesEntityAndRetainsReadingAndAllKnownReferences(string kind)
    {
        using var f = new Fixture(); var source = kind == "archive" ? f.Zip : f.Images; await SeedAsync(f, source);
        var state = new SaveData(f.State); await state.LoadAsync(TestContext.Current.CancellationToken);
        await using var op = f.Operation(state); using var images = new BitmapFactory(new MagickImageDecoder()); var backend = Backend(f);
        var destination = Path.Combine(f.Root, kind == "archive" ? "新书.cbz" : "新书"); Attach(op, images, backend, Path.GetFileName(destination));
        await op.OpenAsync(kind == "image" ? Path.Combine(f.Images, "003.png") : source, TestContext.Current.CancellationToken);
        await op.JumpAsync(2); await op.ApplySettingAsync(s => s.BookReadOrder = PageReadOrder.LeftToRight);
        await op.SearchPagesAsync("003 /or 004", op.Book, TestContext.Current.CancellationToken);
        await op.Playlists.InitializeAsync(TestContext.Current.CancellationToken); var selected = state.Playlists.Current!.Items[0]; state.Playlists.SelectedItem = selected;
        var bookmark = state.BookmarkRoot.Children![0]; var quick = state.QuickAccess.Root.Children![0]; var oldBook = op.Book!;
        op.SetBookLock(true); await op.RenameBookAsync(TestContext.Current.CancellationToken); await op.SaveAsync();
        Assert.Null(op.Error); Assert.False(op.IsRenamingBook); Assert.Equal(destination, op.Book!.Path); Assert.True(oldBook.Source.IsDisposed);
        Assert.Equal("003.png", op.Book.CurrentPage!.EntryName); Assert.Equal("003 /or 004", op.Book.Pages.SearchKeyword); Assert.Equal(PageReadOrder.LeftToRight, op.Book.Setting.BookReadOrder); Assert.True(op.IsBookLocked);
        Assert.Equal(destination, bookmark.Path); Assert.Equal("自定义", bookmark.Name); Assert.Equal("Future=kept", bookmark.Props);
        Assert.Same(bookmark, state.BookmarkRoot.Children[0]); Assert.Same(quick, state.QuickAccess.Root.Children[0]); Assert.Equal(destination, quick.Path);
        Assert.Same(selected, state.Playlists.SelectedItem); Assert.Equal(destination + "/003.png", selected.Path); Assert.Equal("列表别名", selected.Name);
        Assert.Equal(destination, state.LastBookPath); Assert.Equal("003.png", state.Find(destination)!.Page); Assert.Null(state.Find(source));
        Assert.Contains(state.HistoryEntries, e => e.Path == destination + "/子书"); Assert.Contains(state.HistoryEntries, e => e.Path == source + "-similar");
        Assert.Equal(12, state.FolderConfigs.GetFolderParameter(destination).Seed); Assert.Equal(destination + "/003.png", state.FolderConfigs.GetThumbnailTarget(destination));
        Assert.False(File.Exists(Marker(f))); Assert.False(op.DestinationMoves!.CanUndo);
        foreach (var path in Directory.GetFiles(Path.Combine(f.State, "Playlists")))
        {
            var data = JsonNode.Parse(await File.ReadAllTextAsync(path, TestContext.Current.CancellationToken))!;
            Assert.Equal(destination + "/003.png", data["Items"]![0]!["Path"]!.GetValue<string>()); Assert.Equal(source, data["Items"]![0]!["Future"]!.GetValue<string>());
            Assert.Equal(source + "-similar/004.png", data["Items"]![1]!["Path"]!.GetValue<string>());
        }
        await op.DisposeAsync(); var loaded = new SaveData(f.State); await loaded.LoadAsync(TestContext.Current.CancellationToken); await using var restored = f.Operation(loaded); await restored.RestoreLastAsync(TestContext.Current.CancellationToken);
        Assert.Equal(destination, restored.Book!.Path); Assert.Equal("003.png", restored.Book.CurrentPage!.EntryName);
        var raw = JsonNode.Parse(await File.ReadAllTextAsync(Path.Combine(f.State, "History.json"), TestContext.Current.CancellationToken))!;
        Assert.Equal("kept", raw["UnknownRoot"]!.GetValue<string>());
    }
    [Theory]
    [InlineData("cancel")]
    [InlineData("book")]
    [InlineData("permission")]
    [InlineData("close")]
    public async Task DialogCannotRenameWhenCancelledOrTargetBookChanged(string change)
    {
        using var f = new Fixture(); var state = new SaveData(f.State); await state.LoadAsync(TestContext.Current.CancellationToken);
        await using var op = f.Operation(state); using var images = new BitmapFactory(new MagickImageDecoder()); var backend = new ObservedBackend(Backend(f)); Attach(op, images, backend);
        await op.OpenAsync(f.Images, TestContext.Current.CancellationToken);
        var asked = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously); var answer = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
        op.AskBookNameAsync = _ => { asked.SetResult(); return answer.Task; }; var action = op.RenameBookAsync(TestContext.Current.CancellationToken); await asked.Task;
        if (change == "book") await op.OpenAsync(f.Zip, TestContext.Current.CancellationToken);
        if (change == "permission") Config.Current.System.IsFileWriteAccessEnabled = false;
        if (change == "close") await op.DisposeAsync();
        answer.SetResult(change == "cancel" ? null : "新书"); await action;
        Assert.Equal(0, backend.Calls); Assert.True(Directory.Exists(f.Images)); Assert.False(File.Exists(Marker(f))); Assert.False(op.IsRenamingBook);
    }
    [Fact]
    public async Task PageNavigationDuringNameDialogRetainsLatestPageBecauseTargetIsBook()
    {
        using var f = new Fixture(); var state = new SaveData(f.State); await state.LoadAsync(TestContext.Current.CancellationToken);
        await using var op = f.Operation(state); using var images = new BitmapFactory(new MagickImageDecoder()); Attach(op, images, Backend(f));
        await op.OpenAsync(f.Images, TestContext.Current.CancellationToken);
        op.AskBookNameAsync = async _ => { await op.JumpAsync(3); return "新书"; };
        await op.RenameBookAsync(TestContext.Current.CancellationToken); Assert.Equal("004.png", op.Book!.CurrentPage!.EntryName); Assert.Null(op.Error);
    }
    [Theory]
    [InlineData("directory")]
    [InlineData("archive")]
    public async Task CaseOnlyRenameChangesActualDirectoryEntryWithoutOverwrite(string kind)
    {
        using var f = new Fixture(); string source = Path.Combine(f.Root, kind == "directory" ? "CaseBook" : "CaseBook.cbz");
        if (kind == "directory") Directory.Move(f.Images, source); else File.Move(f.Zip, source);
        var state = new SaveData(f.State); await state.LoadAsync(TestContext.Current.CancellationToken);
        await using var op = f.Operation(state); using var images = new BitmapFactory(new MagickImageDecoder()); Attach(op, images, Backend(f), Path.GetFileName(source).ToLowerInvariant());
        await op.OpenAsync(source, TestContext.Current.CancellationToken); await op.RenameBookAsync(TestContext.Current.CancellationToken);
        Assert.Null(op.Error); Assert.Contains(Directory.EnumerateFileSystemEntries(f.Root), p => p == source.ToLowerInvariant().Replace(f.Root.ToLowerInvariant(), f.Root));
        Assert.DoesNotContain(Directory.EnumerateFileSystemEntries(f.Root), p => p == source);
    }
    [Theory]
    [InlineData("../outside")]
    [InlineData(".")]
    [InlineData("..")]
    [InlineData("\0")]
    public async Task InvalidNamesDoNotMoveEntity(string name)
    {
        using var f = new Fixture(); var backend = Backend(f); var target = await backend.GetRenameTargetAsync(f.Images, TestContext.Current.CancellationToken);
        await Assert.ThrowsAsync<ArgumentException>(() => backend.PlanRenameAsync(target, name, TestContext.Current.CancellationToken)); Assert.True(Directory.Exists(f.Images));
    }
    [Fact]
    public async Task MacLegalNameAndConflictNumberingPreserveExistingTarget()
    {
        using var f = new Fixture(); var backend = Backend(f); var target = await backend.GetRenameTargetAsync(f.Zip, TestContext.Current.CancellationToken);
        var duplicate = Path.Combine(f.Root, "目标.cbz"); await File.WriteAllTextAsync(duplicate, "existing", TestContext.Current.CancellationToken);
        await File.WriteAllTextAsync(Path.Combine(f.Root, "目标 (2).cbz"), "second", TestContext.Current.CancellationToken);
        var plan = await backend.PlanRenameAsync(target, "目标.cbz", TestContext.Current.CancellationToken); Assert.True(plan.Conflict); Assert.Equal(Path.Combine(f.Root, "目标 (3).cbz"), plan.Destination);
        await backend.RenameAsync(plan, TestContext.Current.CancellationToken); Assert.Equal("existing", await File.ReadAllTextAsync(duplicate, TestContext.Current.CancellationToken)); Assert.True(File.Exists(plan.Destination));
        target = await backend.GetRenameTargetAsync(f.Images, TestContext.Current.CancellationToken); plan = await backend.PlanRenameAsync(target, "CON:目录\\合法.", TestContext.Current.CancellationToken);
        await backend.RenameAsync(plan, TestContext.Current.CancellationToken); Assert.True(Directory.Exists(plan.Destination));
    }
    [Fact]
    public async Task ExtensionAndConflictConfirmationMayCancelWithoutClosingSource()
    {
        using var f = new Fixture(); var state = new SaveData(f.State); await state.LoadAsync(TestContext.Current.CancellationToken);
        await using var op = f.Operation(state); using var images = new BitmapFactory(new MagickImageDecoder()); Attach(op, images, Backend(f), "目标.zip");
        await File.WriteAllTextAsync(Path.Combine(f.Root, "目标.zip"), "existing", TestContext.Current.CancellationToken); await op.OpenAsync(f.Zip, TestContext.Current.CancellationToken); var book = op.Book;
        op.ConfirmBookRenameAsync = plan => { Assert.True(plan.Conflict); Assert.True(plan.ExtensionChanged); return Task.FromResult(false); };
        await op.RenameBookAsync(TestContext.Current.CancellationToken); Assert.Same(book, op.Book); Assert.False(book!.Source.IsDisposed); Assert.True(File.Exists(f.Zip));
    }
    [Fact]
    public async Task FailedSystemRenameReopensOldSourceAndLeavesReferencesAndJournalUnchanged()
    {
        using var f = new Fixture(); var state = new SaveData(f.State); await state.LoadAsync(TestContext.Current.CancellationToken);
        await using var op = f.Operation(state); using var images = new BitmapFactory(new MagickImageDecoder()); Attach(op, images, new ObservedBackend(Backend(f)) { Failure = new IOException("拒绝访问") });
        await op.OpenAsync(f.Images, TestContext.Current.CancellationToken); await op.JumpAsync(2); await op.RenameBookAsync(TestContext.Current.CancellationToken);
        Assert.Equal(f.Images, op.Book!.Path); Assert.Equal("003.png", op.Book.CurrentPage!.EntryName); Assert.Contains("拒绝访问", op.Error); Assert.False(File.Exists(Marker(f))); Assert.True(op.CanRenameBook);
    }
    [Fact]
    public async Task LateCancellationStillRestoresActualNewBookAndSavesPaths()
    {
        using var f = new Fixture(); var state = new SaveData(f.State); await state.LoadAsync(TestContext.Current.CancellationToken);
        await using var op = f.Operation(state); using var images = new BitmapFactory(new MagickImageDecoder()); using var cancel = new CancellationTokenSource();
        Attach(op, images, new ObservedBackend(Backend(f)) { After = cancel.Cancel }); await op.OpenAsync(f.Images, TestContext.Current.CancellationToken); await op.RenameBookAsync(cancel.Token);
        Assert.Null(op.Error); Assert.Equal(Path.Combine(f.Root, "新书"), op.Book!.Path); Assert.Equal(op.Book.Path, state.LastBookPath); Assert.False(File.Exists(Marker(f)));
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task StartupRecoveryDistinguishesPreparedFromCommittedRename(bool commit)
    {
        using var f = new Fixture(); await SeedAsync(f, f.Images); var state = new SaveData(f.State); await state.LoadAsync(TestContext.Current.CancellationToken);
        var backend = Backend(f); var target = await backend.GetRenameTargetAsync(f.Images, TestContext.Current.CancellationToken); var plan = await backend.PlanRenameAsync(target, "新书", TestContext.Current.CancellationToken);
        await state.PrepareBookRenameAsync(plan, TestContext.Current.CancellationToken); if (commit) await backend.RenameAsync(plan, TestContext.Current.CancellationToken);
        var loaded = new SaveData(f.State); await loaded.LoadAsync(TestContext.Current.CancellationToken); Assert.Empty(await loaded.RecoverBookRenameAsync(backend, TestContext.Current.CancellationToken));
        Assert.False(File.Exists(Marker(f))); Assert.Equal(commit ? plan.Destination : f.Images, loaded.BookmarkRoot.Children![0].Path);
    }
    [Fact]
    public async Task AmbiguousExternalChangeLeavesRecoveryRecordAndKnownReferencesUntouched()
    {
        using var f = new Fixture(); await SeedAsync(f, f.Images); var state = new SaveData(f.State); await state.LoadAsync(TestContext.Current.CancellationToken);
        var backend = Backend(f); var target = await backend.GetRenameTargetAsync(f.Images, TestContext.Current.CancellationToken); var plan = await backend.PlanRenameAsync(target, "新书", TestContext.Current.CancellationToken);
        await state.PrepareBookRenameAsync(plan, TestContext.Current.CancellationToken); Directory.CreateDirectory(plan.Destination);
        Assert.NotEmpty(await state.RecoverBookRenameAsync(backend, TestContext.Current.CancellationToken)); Assert.True(File.Exists(Marker(f))); Assert.Equal(f.Images, state.BookmarkRoot.Children![0].Path);
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task MissingSourcesAreRejectedAndSymbolicSnapshotsRetainLinkWithoutFollowing(bool directory)
    {
        using var f = new Fixture(); var backend = Backend(f); var path = Path.Combine(f.Root, "link");
        if (directory) Directory.CreateSymbolicLink(path, f.Images); else File.CreateSymbolicLink(path, f.Zip);
        var target = await backend.GetRenameTargetAsync(path, TestContext.Current.CancellationToken);
        Assert.Equal(directory ? f.Images : f.Zip, target.LinkTarget); Assert.False(target.IsDirectory);
        await Assert.ThrowsAsync<FileNotFoundException>(() => backend.GetRenameTargetAsync(Path.Combine(f.Root, "missing"), TestContext.Current.CancellationToken));
        Assert.True(Directory.Exists(f.Images)); Assert.True(File.Exists(f.Zip));
    }
    [Fact]
    public async Task SaveFailureAfterEntityCommitKeepsNewPathsAndCanRecoverAfterRestart()
    {
        using var f = new Fixture(); await SeedAsync(f, f.Images); var state = new SaveData(f.State); await state.LoadAsync(TestContext.Current.CancellationToken);
        await using var op = f.Operation(state); using var images = new BitmapFactory(new MagickImageDecoder()); var backend = Backend(f); var blocker = Path.Combine(f.State, "UserSetting.json.tmp");
        Attach(op, images, new ObservedBackend(backend) { After = () => Directory.CreateDirectory(blocker) }); await op.OpenAsync(f.Images, TestContext.Current.CancellationToken);
        try
        {
            await op.RenameBookAsync(TestContext.Current.CancellationToken); Assert.Contains("书籍已重命名", op.Error); Assert.True(Directory.Exists(Path.Combine(f.Root, "新书"))); Assert.Equal(Path.Combine(f.Root, "新书"), state.BookmarkRoot.Children![0].Path); Assert.True(File.Exists(Marker(f)));
        }
        finally { Directory.Delete(blocker); }
        var loaded = new SaveData(f.State); await loaded.LoadAsync(TestContext.Current.CancellationToken); Assert.Empty(await loaded.RecoverBookRenameAsync(backend, TestContext.Current.CancellationToken));
        Assert.Equal(Path.Combine(f.Root, "新书"), loaded.BookmarkRoot.Children![0].Path); await op.SaveAsync(); Assert.False(File.Exists(Marker(f)));
    }
    [AvaloniaFact]
    public async Task FormalRenameDialogsRespectCancelAndShutdownAndExportActualCommandRegistration()
    {
        using var f = new Fixture(); var state = new SaveData(f.State); await state.LoadAsync(TestContext.Current.CancellationToken); var op = f.Operation(state); var images = new BitmapFactory(new MagickImageDecoder()); Attach(op, images, Backend(f));
        var window = new MainWindow(); window.Bind(new(op, new(op), state), images, new NoopPlatform()); window.Show();
        try
        {
            await window.OpenAsync(f.Zip); Assert.True(window.IsCommandAvailable("RenameBook"));
            var action = window.ExecuteAsync("RenameBook"); await WaitAsync(() => window.OwnedWindows.Count == 1); var dialog = Assert.Single(window.OwnedWindows); Dispatcher.UIThread.RunJobs(); dialog.UpdateLayout();
            var input = Assert.IsType<StackPanel>(dialog.Content).Children.OfType<TextBox>().Single(); Assert.Equal("漫画.cbz", input.Text); Assert.Equal(2, input.SelectionEnd);
            using (var bitmap = new RenderTargetBitmap(new PixelSize((int)Math.Ceiling(dialog.Bounds.Width), (int)Math.Ceiling(dialog.Bounds.Height)))) { bitmap.Render(dialog); bitmap.Save(Output("name.png"), PngBitmapEncoderOptions.Default); }
            dialog.Close(null); await action; Assert.True(File.Exists(f.Zip));
            await File.WriteAllTextAsync(Path.Combine(f.Root, "重名.zip"), "protected", TestContext.Current.CancellationToken);
            action = window.ExecuteAsync("RenameBook"); await WaitAsync(() => window.OwnedWindows.Count == 1); Assert.Single(window.OwnedWindows).Close("重名.zip");
            await WaitAsync(() => window.OwnedWindows.Any(w => w.Title == "更改书籍扩展名")); Assert.Single(window.OwnedWindows).Close(true);
            await WaitAsync(() => window.OwnedWindows.Any(w => w.Title == "书籍名称已存在")); dialog = Assert.Single(window.OwnedWindows); Dispatcher.UIThread.RunJobs(); dialog.UpdateLayout();
            using (var bitmap = new RenderTargetBitmap(new PixelSize((int)Math.Ceiling(dialog.Bounds.Width), (int)Math.Ceiling(dialog.Bounds.Height)))) { bitmap.Render(dialog); bitmap.Save(Output("conflict.png"), PngBitmapEncoderOptions.Default); }
            dialog.Close(false); await action; Assert.True(File.Exists(f.Zip)); Assert.Equal("protected", File.ReadAllText(Path.Combine(f.Root, "重名.zip")));
            action = window.ExecuteAsync("RenameBook"); await WaitAsync(() => window.OwnedWindows.Count == 1); dialog = Assert.Single(window.OwnedWindows); dialog.Close("改名.cbz"); await action;
            Assert.Equal(Path.Combine(f.Root, "改名.cbz"), op.Book!.Path);
            var implemented = typeof(MainWindow).GetMethod("IsCommandImplemented", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;
            var items = new CommandTable(op).Definitions.Select(d => new { d.Name, d.Text, d.Shortcut, d.MouseGesture, d.Source, d.Stage, implemented = (bool)implemented.Invoke(window, [d.Name])! }).ToArray();
            Assert.Equal(235, items.Length); Assert.True(items.Single(i => i.Name == "RenameBook").implemented);
            Assert.False(items.Single(i => i.Name == "CutFile").implemented);
            await File.WriteAllTextAsync(Output("commands.json"), JsonSerializer.Serialize(new { scope = "执行入口登记，不等于完整原功能覆盖率", total = items.Length, implemented = items.Count(i => i.implemented), items }, new JsonSerializerOptions { WriteIndented = true }), TestContext.Current.CancellationToken);
            action = window.ExecuteAsync("RenameBook"); await WaitAsync(() => window.OwnedWindows.Count == 1); await window.PrepareShutdownAsync(); await action; Assert.True(File.Exists(Path.Combine(f.Root, "改名.cbz")));
        }
        finally { await window.PrepareShutdownAsync(); window.Close(); }
    }
    [AvaloniaFact]
    public async Task ShutdownWaitsForAuthorizedRenameWithoutDuplicateAction()
    {
        using var f = new Fixture(); var state = new SaveData(f.State); await state.LoadAsync(TestContext.Current.CancellationToken); var op = f.Operation(state); var images = new BitmapFactory(new MagickImageDecoder());
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously); var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var backend = new ObservedBackend(Backend(f)) { Before = async () => { entered.SetResult(); await release.Task; } }; Attach(op, images, backend);
        var window = new MainWindow(); window.Bind(new(op, new(op), state), images, new NoopPlatform()); window.Show();
        try
        {
            await window.OpenAsync(f.Images); var action = window.ExecuteAsync("RenameBook"); await WaitAsync(() => window.OwnedWindows.Count == 1); Assert.Single(window.OwnedWindows).Close("新书"); await entered.Task;
            await window.ExecuteAsync("RenameBook"); Assert.Equal(1, backend.Calls);
            var closing = window.PrepareShutdownAsync(); await Task.Delay(20, TestContext.Current.CancellationToken); Assert.False(closing.IsCompleted); release.SetResult(); await action; await closing;
            Assert.Null(op.Book); Assert.Equal(Path.Combine(f.Root, "新书"), state.LastBookPath); Assert.True(Directory.Exists(Path.Combine(f.Root, "新书")));
        }
        finally { release.TrySetResult(); await window.PrepareShutdownAsync(); window.Close(); }
    }
    [Fact]
    public async Task FailedRenameCanRetrySamePlanAndCloseCancelsUnconfirmedRetryWithoutDeadlock()
    {
        using var f = new Fixture(); var state = new SaveData(f.State); await state.LoadAsync(TestContext.Current.CancellationToken);
        await using var op = f.Operation(state); using var images = new BitmapFactory(new MagickImageDecoder()); var backend = new ObservedBackend(Backend(f)) { Failure = new IOException("暂不可访问") }; Attach(op, images, backend);
        await op.OpenAsync(f.Images, TestContext.Current.CancellationToken);
        op.RetryBookRenameAsync = message => { Assert.Contains("暂不可访问", message); backend.Failure = null; return Task.FromResult(true); };
        await op.RenameBookAsync(TestContext.Current.CancellationToken); Assert.Equal(2, backend.Calls); Assert.Null(op.Error); Assert.Equal(Path.Combine(f.Root, "新书"), op.Book!.Path);
        backend.Failure = new IOException("暂不可访问"); op.AskBookNameAsync = _ => Task.FromResult<string?>("再改名");
        var retryEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        op.RetryBookRenameAsync = _ => { retryEntered.SetResult(); return new TaskCompletionSource<bool>().Task; };
        var action = op.RenameBookAsync(TestContext.Current.CancellationToken); await retryEntered.Task;
        await op.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken); await action;
        Assert.Null(op.Book); Assert.Equal(Path.Combine(f.Root, "新书"), state.LastBookPath); Assert.False(File.Exists(Marker(f)));
    }
    [Fact]
    public async Task DirectEngineDisposeWaitsForAlreadyAuthorizedRenameAndPersistsRealDestination()
    {
        using var f = new Fixture(); var state = new SaveData(f.State); await state.LoadAsync(TestContext.Current.CancellationToken);
        await using var op = f.Operation(state); using var images = new BitmapFactory(new MagickImageDecoder());
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously); var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Attach(op, images, new ObservedBackend(Backend(f)) { Before = async () => { entered.SetResult(); await release.Task; } }); await op.OpenAsync(f.Images, TestContext.Current.CancellationToken);
        var action = op.RenameBookAsync(TestContext.Current.CancellationToken); await entered.Task; var close = op.DisposeAsync().AsTask();
        await Task.Delay(20, TestContext.Current.CancellationToken); Assert.False(close.IsCompleted); release.SetResult(); await action; await close;
        Assert.Null(op.Book); Assert.Equal(Path.Combine(f.Root, "新书"), state.LastBookPath); Assert.False(File.Exists(Marker(f)));
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ClosingCancelsRenameWaitingForNavigationGateBeforePhysicalAuthorization(bool afterName)
    {
        using var f = new Fixture(); var state = new SaveData(f.State); await state.LoadAsync(TestContext.Current.CancellationToken);
        await using var op = f.Operation(state); using var images = new BitmapFactory(new MagickImageDecoder());
        var backend = new ObservedBackend(Backend(f)); Attach(op, images, backend);
        await op.OpenAsync(f.Images, TestContext.Current.CancellationToken);
        var gate = Assert.IsType<SemaphoreSlim>(typeof(BookOperation).GetField("_gate", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(op));
        var held = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        if (afterName)
            op.AskBookNameAsync = async _ => { await gate.WaitAsync(TestContext.Current.CancellationToken); held.SetResult(); return "新书"; };
        else { await gate.WaitAsync(TestContext.Current.CancellationToken); held.SetResult(); }
        var action = op.RenameBookAsync(TestContext.Current.CancellationToken);
        await held.Task;
        try
        {
            var close = op.DisposeAsync().AsTask();
            // 改名先结束，退出仍等持锁操作完成，不能因互相等待而死锁或提交实体改名。
            await action.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
            Assert.False(op.IsRenamingBook); Assert.False(close.IsCompleted); Assert.Equal(0, backend.Calls); Assert.Equal(0, gate.CurrentCount);
            Assert.True(Directory.Exists(f.Images)); Assert.False(File.Exists(Marker(f)));
        }
        finally { gate.Release(); }
        await op.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        Assert.Equal(1, gate.CurrentCount);
    }
    [Fact]
    public async Task NewerFailedOpenIsReportedAndDoesNotLetOldRenameOverrideRequestGeneration()
    {
        using var f = new Fixture(); var state = new SaveData(f.State); await state.LoadAsync(TestContext.Current.CancellationToken);
        await using var op = f.Operation(state); using var images = new BitmapFactory(new MagickImageDecoder());
        Attach(op, images, new ObservedBackend(Backend(f)) { Before = () => op.OpenAsync(Path.Combine(f.Root, "missing.cbz"), TestContext.Current.CancellationToken) });
        await op.OpenAsync(f.Images, TestContext.Current.CancellationToken); await op.RenameBookAsync(TestContext.Current.CancellationToken);
        Assert.Null(op.Book); Assert.NotNull(op.Error); Assert.Equal(Path.Combine(f.Root, "新书"), state.LastBookPath);
        await op.RestoreLastAsync(TestContext.Current.CancellationToken); Assert.Equal(state.LastBookPath, op.Book!.Path);
    }
    [Fact]
    public async Task ExternalPlaylistModificationIsNotOverwrittenAndPendingPathsRetryAfterReload()
    {
        using var f = new Fixture(); await SeedAsync(f, f.Images); var state = new SaveData(f.State); await state.LoadAsync(TestContext.Current.CancellationToken);
        await state.Playlists.InitializeAsync(TestContext.Current.CancellationToken); var list = state.Playlists.Current!; var external = await File.ReadAllTextAsync(list.Path, TestContext.Current.CancellationToken) + "\n ";
        await File.WriteAllTextAsync(list.Path, external, TestContext.Current.CancellationToken);
        var backend = Backend(f); var target = await backend.GetRenameTargetAsync(f.Images, TestContext.Current.CancellationToken); var plan = await backend.PlanRenameAsync(target, "新书", TestContext.Current.CancellationToken);
        await state.PrepareBookRenameAsync(plan, TestContext.Current.CancellationToken); await backend.RenameAsync(plan, TestContext.Current.CancellationToken);
        await Assert.ThrowsAsync<IOException>(() => state.RenameBookPathsAsync(plan)); Assert.Equal(external, await File.ReadAllTextAsync(list.Path, TestContext.Current.CancellationToken)); Assert.True(File.Exists(Marker(f)));
        await state.Playlists.SwitchAsync(list.Path, TestContext.Current.CancellationToken); await state.FlushBookRenameAsync(); Assert.False(File.Exists(Marker(f))); Assert.Equal(plan.Destination + "/003.png", state.Playlists.Current!.Items[0].Path);
    }
    [Fact]
    public async Task TargetAppearingAfterConfirmationIsNotOverwrittenAndOriginalSourceReopens()
    {
        using var f = new Fixture(); var state = new SaveData(f.State); await state.LoadAsync(TestContext.Current.CancellationToken);
        await using var op = f.Operation(state); using var images = new BitmapFactory(new MagickImageDecoder()); var destination = Path.Combine(f.Root, "新书");
        Attach(op, images, new ObservedBackend(Backend(f)) { Before = () => { Directory.CreateDirectory(destination); File.WriteAllText(Path.Combine(destination, "keep"), "existing"); return Task.CompletedTask; } });
        await op.OpenAsync(f.Images, TestContext.Current.CancellationToken); await op.RenameBookAsync(TestContext.Current.CancellationToken);
        Assert.Contains("占用", op.Error); Assert.Equal(f.Images, op.Book!.Path); Assert.Equal("existing", File.ReadAllText(Path.Combine(destination, "keep"))); Assert.False(File.Exists(Marker(f)));
    }
    [Fact]
    public async Task HistorySuppressionAndRingNavigationSurviveRenameWithoutReRegisteringRemovedRecord()
    {
        using var f = new Fixture(); var state = new SaveData(f.State); await state.LoadAsync(TestContext.Current.CancellationToken);
        await using var op = f.Operation(state); using var images = new BitmapFactory(new MagickImageDecoder()); Attach(op, images, Backend(f));
        await op.OpenAsync(f.Images, TestContext.Current.CancellationToken); await op.SaveAsync(); await op.JumpAsync(2); await op.SaveAsync();
        await state.RemoveHistoryAsync([f.Images], TestContext.Current.CancellationToken); await op.RenameBookAsync(TestContext.Current.CancellationToken); await op.SaveAsync();
        Assert.Empty(state.HistoryEntries); Assert.Equal(Path.Combine(f.Root, "新书"), state.LastBookPath);
        await op.NavigateHistoryAsync(-1); Assert.Equal(Path.Combine(f.Root, "新书"), op.Book!.Path); Assert.Equal("001.png", op.Book.CurrentPage!.EntryName);
    }
    [Fact]
    public async Task NewOpenDuringPhysicalRenameWinsAndCanReadOldArchiveWithoutStaleRestore()
    {
        using var f = new Fixture(); var state = new SaveData(f.State); await state.LoadAsync(TestContext.Current.CancellationToken);
        await using var op = f.Operation(state); using var images = new BitmapFactory(new MagickImageDecoder());
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously); var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Attach(op, images, new ObservedBackend(Backend(f)) { Before = async () => { entered.SetResult(); await release.Task; } }); await op.OpenAsync(f.Images, TestContext.Current.CancellationToken);
        var action = op.RenameBookAsync(TestContext.Current.CancellationToken); await entered.Task; var next = op.OpenAsync(f.Zip, TestContext.Current.CancellationToken);
        release.SetResult(); await action; await next; Assert.Equal(f.Zip, op.Book!.Path); Assert.Null(op.Error); Assert.True(Directory.Exists(Path.Combine(f.Root, "新书")));
    }
    [Fact]
    public async Task FailedCloseResetsRenamePromptCancellationAndAllowsRetry()
    {
        using var f = new Fixture(); var state = new SaveData(f.State); await state.LoadAsync(TestContext.Current.CancellationToken);
        await using var op = f.Operation(state); using var images = new BitmapFactory(new MagickImageDecoder()); Attach(op, images, Backend(f));
        await op.OpenAsync(f.Images, TestContext.Current.CancellationToken); var blocker = Path.Combine(f.State, "UserSetting.json.tmp"); Directory.CreateDirectory(blocker);
        try { await Assert.ThrowsAsync<UnauthorizedAccessException>(() => op.DisposeAsync().AsTask()); }
        finally { Directory.Delete(blocker); }
        Assert.True(op.CanRenameBook); await op.RenameBookAsync(TestContext.Current.CancellationToken); Assert.Null(op.Error); Assert.Equal(Path.Combine(f.Root, "新书"), op.Book!.Path);
    }
    [Fact]
    public async Task PlaylistStoredInsideBookMovesItsFileLocationAndDoesNotRecreateOldDirectory()
    {
        using var f = new Fixture(); var state = new SaveData(f.State); await state.LoadAsync(TestContext.Current.CancellationToken);
        Config.Current.Playlist.PlaylistFolderRaw = f.Images;
        var path = Path.Combine(f.Images, "Default.nvpls"); await File.WriteAllTextAsync(path, new JsonObject { ["Format"] = PlaylistSource.CurrentFormat, ["Items"] = new JsonArray(new JsonObject { ["Path"] = f.Images + "/003.png" }) }.ToJsonString(), TestContext.Current.CancellationToken);
        await state.Playlists.SwitchAsync(path, TestContext.Current.CancellationToken); var item = state.Playlists.Current!.Items[0];
        await using var op = f.Operation(state); using var images = new BitmapFactory(new MagickImageDecoder()); Attach(op, images, Backend(f)); await op.OpenAsync(f.Images, TestContext.Current.CancellationToken);
        await op.RenameBookAsync(TestContext.Current.CancellationToken); await op.SaveAsync(); var destination = Path.Combine(f.Root, "新书");
        Assert.Null(op.Error); Assert.False(Directory.Exists(f.Images)); Assert.Equal(destination, Config.Current.Playlist.PlaylistFolder); Assert.Equal(Path.Combine(destination, "Default.nvpls"), state.Playlists.Current.Path);
        Assert.Same(item, state.Playlists.Current.Items[0]); Assert.Equal(destination + "/003.png", item.Path);
        var loaded = new SaveData(f.State); await loaded.LoadAsync(TestContext.Current.CancellationToken); Assert.Equal(destination, Config.Current.Playlist.PlaylistFolder); await loaded.Playlists.InitializeAsync(TestContext.Current.CancellationToken); Assert.Equal(item.Path, loaded.Playlists.Current!.Items[0].Path);
    }
    [Fact]
    public async Task SuccessfulDirectoryRenameRecoveryToleratesOwnSubsequentMetadataWrites()
    {
        using var f = new Fixture(); await SeedAsync(f, f.Images); var state = new SaveData(f.State); await state.LoadAsync(TestContext.Current.CancellationToken);
        var backend = Backend(f); var target = await backend.GetRenameTargetAsync(f.Images, TestContext.Current.CancellationToken); var plan = await backend.PlanRenameAsync(target, "新书", TestContext.Current.CancellationToken);
        await state.PrepareBookRenameAsync(plan, TestContext.Current.CancellationToken); await backend.RenameAsync(plan, TestContext.Current.CancellationToken);
        await File.WriteAllTextAsync(Path.Combine(plan.Destination, "metadata.nvpls"), "saved", TestContext.Current.CancellationToken);
        Assert.Empty(await state.RecoverBookRenameAsync(backend, TestContext.Current.CancellationToken)); Assert.Equal(plan.Destination, state.BookmarkRoot.Children![0].Path);
    }
    [Theory]
    [InlineData("")]
    [InlineData("{")]
    [InlineData("null")]
    [InlineData("{}")]
    [InlineData("invalid-path")]
    public async Task InvalidRecoveryRecordPreservesMaterialAndKnownReferences(string record)
    {
        using var f = new Fixture(); await SeedAsync(f, f.Images); var state = new SaveData(f.State); await state.LoadAsync(TestContext.Current.CancellationToken);
        var backend = Backend(f);
        if (record == "invalid-path")
        {
            var target = await backend.GetRenameTargetAsync(f.Images, TestContext.Current.CancellationToken);
            record = JsonSerializer.Serialize(new BookRenamePlan(target, Path.Combine(f.Images, "nested"), false, false));
        }
        await File.WriteAllTextAsync(Marker(f), record, TestContext.Current.CancellationToken);
        var history = File.ReadAllText(Path.Combine(f.State, "History.json"));
        Assert.NotEmpty(await state.RecoverBookRenameAsync(backend, TestContext.Current.CancellationToken));
        Assert.Equal(record, File.ReadAllText(Marker(f))); Assert.Equal(f.Images, state.BookmarkRoot.Children![0].Path);
        Assert.Equal(history, File.ReadAllText(Path.Combine(f.State, "History.json"))); Assert.True(Directory.Exists(f.Images));
    }
    [Fact]
    public async Task NonCurrentPlaylistFailureRecoversAfterRestartWithoutRepeatingCompletedWrites()
    {
        using var f = new Fixture(); await SeedAsync(f, f.Images); var state = new SaveData(f.State); await state.LoadAsync(TestContext.Current.CancellationToken);
        await state.Playlists.InitializeAsync(TestContext.Current.CancellationToken);
        var other = Path.Combine(f.State, "Playlists", "其他.nvpls"); var original = File.ReadAllText(other);
        await File.WriteAllTextAsync(other, "{broken", TestContext.Current.CancellationToken);
        var backend = Backend(f); var target = await backend.GetRenameTargetAsync(f.Images, TestContext.Current.CancellationToken);
        var plan = await backend.PlanRenameAsync(target, "新书", TestContext.Current.CancellationToken);
        await state.PrepareBookRenameAsync(plan, TestContext.Current.CancellationToken); await backend.RenameAsync(plan, TestContext.Current.CancellationToken);
        await Assert.ThrowsAsync<IOException>(() => state.RenameBookPathsAsync(plan));
        Assert.True(File.Exists(Marker(f))); Assert.Equal("{broken", File.ReadAllText(other));
        var current = state.Playlists.Current!.Path; var completed = File.ReadAllText(current);
        Assert.Equal(plan.Destination + "/003.png", JsonNode.Parse(completed)!["Items"]![0]!["Path"]!.GetValue<string>());
        var loaded = new SaveData(f.State); await loaded.LoadAsync(TestContext.Current.CancellationToken);
        Assert.Equal(plan.Destination, loaded.BookmarkRoot.Children![0].Path);
        Assert.NotEmpty(await loaded.RecoverBookRenameAsync(backend, TestContext.Current.CancellationToken));
        await File.WriteAllTextAsync(other, original, TestContext.Current.CancellationToken);
        Assert.Empty(await loaded.RecoverBookRenameAsync(backend, TestContext.Current.CancellationToken));
        Assert.False(File.Exists(Marker(f))); Assert.Equal(completed, File.ReadAllText(current));
        var recovered = File.ReadAllText(other); Assert.Equal(plan.Destination + "/003.png", JsonNode.Parse(recovered)!["Items"]![0]!["Path"]!.GetValue<string>());
        Assert.Equal(f.Images, JsonNode.Parse(recovered)!["Items"]![0]!["Future"]!.GetValue<string>());
        Assert.Empty(await loaded.RecoverBookRenameAsync(backend, TestContext.Current.CancellationToken)); Assert.Equal(recovered, File.ReadAllText(other));
    }
    [Fact]
    public async Task ApplicationProfileAndItsAncestorCannotBeRenamedAsBook()
    {
        using var f = new Fixture(); var state = new SaveData(Path.Combine(f.Images, "Profile")); await state.LoadAsync(TestContext.Current.CancellationToken);
        await using var op = f.Operation(state); using var images = new BitmapFactory(new MagickImageDecoder()); Attach(op, images, Backend(f)); await op.OpenAsync(f.Images, TestContext.Current.CancellationToken);
        Assert.False(op.CanRenameBook); await op.RenameBookAsync(TestContext.Current.CancellationToken); Assert.True(Directory.Exists(f.Images));
    }
    private sealed class NoopPlatform : IPlatformService
    {
        public Task RevealAsync(string path, CancellationToken token = default) => Task.CompletedTask;
        public Task TrashAsync(string path, CancellationToken token = default) => throw new NotSupportedException();
    }
    private static async Task WaitAsync(Func<bool> condition)
    { for (int i = 0; i < 300 && !condition(); i++) { Dispatcher.UIThread.RunJobs(); await Task.Delay(10, TestContext.Current.CancellationToken); } Assert.True(condition()); }
    private static string Output(string suffix)
    { var phase = Environment.GetEnvironmentVariable("NEEVIEW_ACCEPTANCE_PHASE") ?? "p4-rename"; return Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../acceptance", phase + "-" + suffix)); }
}
