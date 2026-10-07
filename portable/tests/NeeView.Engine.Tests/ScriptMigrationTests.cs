using System.Collections.Concurrent;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.LogicalTree;
using Avalonia.Interactivity;
using NeeView.Backends;
using NeeView.MacOS.ViewModels;
using NeeView.MacOS.Views;
namespace NeeView.Engine.Tests;
/// <summary>正式窗口/原JSON/真实后端的脚本回归；不激活用户桌面或改动用户图片。</summary>
public sealed class ScriptMigrationTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;
    private sealed class Platform(string trashRoot) : IPlatformService
    {
        public List<Uri> Uris { get; } = [];
        public List<ExternalAppLaunchRequest> Launches { get; } = [];
        public Task RevealAsync(string path, CancellationToken token = default) => Task.CompletedTask;
        public Task TrashAsync(string path, CancellationToken token = default)
        { token.ThrowIfCancellationRequested(); Directory.CreateDirectory(trashRoot); if (Directory.Exists(path)) Directory.Move(path, Path.Combine(trashRoot, Path.GetFileName(path))); else File.Move(path, Path.Combine(trashRoot, Path.GetFileName(path))); return Task.CompletedTask; }
        public Task OpenUriAsync(Uri uri, CancellationToken token = default) { Uris.Add(uri); return Task.CompletedTask; }
        public Task OpenExternalApplicationAsync(ExternalAppLaunchRequest request, CancellationToken token = default) { Launches.Add(request); return Task.CompletedTask; }
    }
    private sealed class CancelAfterCommit(IFileOperationBackend inner, CancellationTokenSource cancel) : IFileOperationBackend
    {
        public Task<bool> FileExistsAsync(string path, CancellationToken token) => inner.FileExistsAsync(path, token);
        public async Task<FileTransferResult> TransferAsync(FileTransferRequest request, CancellationToken token)
        { var result = await inner.TransferAsync(request, token); cancel.Cancel(); return result; }
        public Task ReleaseAsync(FileTransferResult result) => inner.ReleaseAsync(result);
        public Task<IReadOnlyList<string>> RecoverAsync(CancellationToken token = default) => inner.RecoverAsync(token);
        public Task CreateDirectoryAsync(string parent, string name, CancellationToken token) => inner.CreateDirectoryAsync(parent, name, token);
    }
    private sealed class Host(Fixture fixture, SaveData state, BookOperation operation, BitmapFactory images, Platform platform) : IAsyncDisposable
    {
        public ReaderWorkspaceViewModel Model { get; } = new(operation, new(operation), state);
        public MainWindow Window { get; } = new();
        public ConcurrentDictionary<string, object?> Values { get; } = new();
        public Platform System => platform;
        public BitmapFactory Images => images;
        public async Task InitializeAsync()
        {
            var backend = new FileOperationBackend(Path.Combine(fixture.State, "FileRecovery"));
            operation.AttachFileOperations(new(backend), backend, images); operation.AttachFileDeletion(platform, images); operation.AttachExternalApplications(platform, "/NeeView.Mac");
            Window.Bind(Model, images, platform); await Window.AttachScriptsAsync(new JintScriptRuntimeFactory(), (_, _) => { }, Values); Window.Show();
        }
        public Task<object?> Evaluate(string text) => Window.Scripts!.EvaluateAsync(text, Token);
        public async ValueTask DisposeAsync() { await Window.PrepareShutdownAsync(); Window.Close(); }
    }
    private static async Task<Host> CreateAsync(Fixture fixture, string? scripts = null)
    {
        var state = new SaveData(fixture.State); await state.LoadAsync(Token);
        if (scripts is not null) { Config.Current.Script.ScriptFolder = scripts; Config.Current.Script.IsScriptFolderEnabled = true; }
        var op = fixture.Operation(state); var host = new Host(fixture, state, op, new(new MagickImageDecoder()), new(Path.Combine(fixture.Root, "Trash"))); await host.InitializeAsync(); return host;
    }
    private static async Task UntilAsync(Func<bool> condition)
    {
        for (int n = 0; n < 200; n++) { Dispatcher.UIThread.RunJobs(); if (condition()) return; await Task.Delay(10, Token); }
        Assert.True(condition(), "脚本/界面回报未在有界等待内完成。");
    }
    [AvaloniaFact]
    public async Task UnavailableMainViewFloatNeverClosesTheMainWindow()
    {
        using var f = new Fixture(); await using var host = await CreateAsync(f); await host.Window.OpenAsync(f.Images);
        var book = host.Model.Operation.Book;
        Assert.Equal(false, await host.Evaluate("nv.MainView.Window.IsOpen;"));
        await host.Evaluate("nv.MainView.Close();"); Assert.True(host.Window.IsVisible); Assert.Same(book, host.Model.Operation.Book);
        await Assert.ThrowsAsync<ScriptExecutionException>(() => host.Evaluate("nv.MainView.Open();"));
        Assert.True(host.Window.IsVisible); Assert.Same(book, host.Model.Operation.Book);
    }
    [AvaloniaFact]
    public async Task ScriptCopyPageIntoCurrentDirectoryRefreshesIndexAndNormalizedRootDeleteUsesBookFlow()
    {
        using var f = new Fixture(); await using var host = await CreateAsync(f); await host.Window.OpenAsync(f.Images);
        var directory = Directory.CreateDirectory(Path.Combine(f.Images, "copied")).FullName;
        await host.Model.Operation.ApplySettingAsync(s => s.IsRecursiveFolder = true);
        Config.Current.System.DestinationFolderCollection.Add(new("inside", directory));
        await host.Evaluate("nv.DestinationFolderCollection.Items[0].CopyPage(nv.Book.Pages[0]);");
        Assert.True(File.Exists(Path.Combine(directory, "001.png")));
        Assert.Contains(host.Model.Operation.Book!.Pages.SourcePages, p => p.ArchiveEntry.FilePath == Path.Combine(directory, "001.png"));
        Config.Current.System.IsFileWriteAccessEnabled = true;
        string? confirmedBook = null;
        host.Model.Operation.ConfirmDeleteBookAsync = path => { confirmedBook = path; return Task.FromResult(false); };
        await host.Evaluate("nv.DeleteFile(" + System.Text.Json.JsonSerializer.Serialize(f.Images + "/") + ");");
        Assert.Equal(f.Images, confirmedBook);
        Assert.True(Directory.Exists(f.Images)); Assert.NotNull(host.Model.Operation.Book); // 取消原整书确认，不误拒绝为父目录。
    }
    [AvaloniaFact]
    public async Task ActualNvConfigPatchAndDynamicCommandUseSameReadingAndJson()
    {
        using var f = new Fixture(); var folder = Directory.CreateDirectory(Path.Combine(f.Root, "Scripts")).FullName;
        File.WriteAllText(Path.Combine(folder, "capture.nvjs"), "// @args original\nnv.Values.arg=nv.Args[0]; nv.CurrentCommand.Parameter.IsChecked=true;");
        await using var host = await CreateAsync(f, folder); await host.Window.OpenAsync(f.Images); await PageListThumbnailTests.SettleAsync(host.Window);
        var state = host.Model.SaveData; var original = state.GetMoveSizeParameter().Size;
        await host.Evaluate("nv.Config.Theme.ThemeType='Light'; nv.Command.NextSizePage.Patch({Size:2}).Execute();");
        Assert.Equal("Light", Config.Current.Theme.ThemeString); Assert.Equal(original, state.GetMoveSizeParameter().Size);
        await host.Evaluate("nv.Command.Script_capture.Execute('explicit');"); Assert.Equal("explicit", host.Values["arg"]);
        Assert.True(((ScriptCommandParameter)state.GetCommandParameterObject("Script_capture")!).IsChecked);
        Assert.True(host.Model.Commands.IsAvailable("Script_capture"));
        await state.SaveAsync(host.Model.Operation.Book, Token);
        Assert.DoesNotContain("\"Size\": 2", File.ReadAllText(Path.Combine(f.State, "UserSetting.json")));
        await host.Evaluate("nv.Command.NextSizePage.Parameter.Size=3;"); Assert.Equal(3, state.GetMoveSizeParameter().Size);
    }
    [AvaloniaFact]
    public async Task ActualPanelSelectionAndItemOpenDoNotInventSecondCollection()
    {
        using var f = new Fixture(); await using var host = await CreateAsync(f); await host.Window.OpenAsync(f.Images); await PageListThumbnailTests.SettleAsync(host.Window);
        var before = host.Model.Operation.Position;
        Assert.Equal(2d, await host.Evaluate("nv.PageList.SelectedItems=[nv.PageList.Items[1],nv.PageList.Items[3]]; nv.PageList.SelectedItems.length;"));
        Assert.Equal(before, host.Model.Operation.Position);
        Assert.Equal(2, host.Window.FindControl<ListBox>("PageList")!.SelectedItems!.Count);
        await host.Evaluate("nv.PageList.Items[3].Open();"); Assert.Equal(3, host.Model.Operation.Position.Index);
        await host.Evaluate("nv.PageList.Style='Thumbnail';"); Assert.Equal(PanelListItemStyle.Thumbnail, Config.Current.PageList.PanelListItemStyle);
        await host.Evaluate("var app=nv.ExternalAppCollection.CreateNew(); app.Name='Script app'; app.Parameter='{File}'; app.ArchivePolicy='SendExtractFile'; app.WorkingDirectory='/tmp';");
        Assert.Equal("Script app", Config.Current.System.ExternalAppCollection.Last().Name);
        await host.Evaluate("nv.ExternalAppCollection.Remove(nv.ExternalAppCollection.Items[nv.ExternalAppCollection.Items.length-1]);");
        Assert.Equal("系统默认应用", Assert.Single(Config.Current.System.ExternalAppCollection).DisplayName);
    }
    [AvaloniaFact]
    public async Task FiveEventsAndFailedCloseKeepLiveManagerThenReleaseAllTasks()
    {
        using var f = new Fixture(); var folder = Directory.CreateDirectory(Path.Combine(f.Root, "Scripts")).FullName;
        foreach (var name in new[] { "OnStartup", "OnBookLoaded", "OnPageChanged", "OnWindowStateChanged" }) File.WriteAllText(Path.Combine(folder, name + ".nvjs"), $"nv.Values.{name}=(nv.Values.{name}||0)+1;");
        File.WriteAllText(Path.Combine(folder, "OnPageEnd.nvjs"), "nv.Values.end=nv.Args[0];");
        await using var host = await CreateAsync(f, folder); await host.Window.RunStartupScriptsAsync(null); await host.Window.OpenAsync(f.Images);
        await PageListThumbnailTests.SettleAsync(host.Window); await UntilAsync(() => host.Values.ContainsKey("OnPageChanged"));
        Assert.Equal(1d, host.Values["OnStartup"]); Assert.Equal(1d, host.Values["OnBookLoaded"]);
        await host.Window.Viewer.RefreshAsync(); await Task.Delay(50, Token); Assert.Equal(1d, host.Values["OnPageChanged"]);
        host.Window.WindowState = WindowState.Maximized; await UntilAsync(() => host.Values.ContainsKey("OnWindowStateChanged"));
        await host.Model.Operation.MoveToBoundaryAsync(true); Config.Current.Book.PageEndAction = PageEndAction.None; await host.Model.Operation.MoveAsync(1);
        await UntilAsync(() => host.Values.ContainsKey("end")); Assert.Equal(1d, host.Values["end"]);
        var manager = host.Window.Scripts!; var loop = manager.EvaluateAsync("while(true){}", Token);
        await UntilAsync(() => manager.ActiveCount > 0); Directory.CreateDirectory(Path.Combine(f.State, "UserSetting.json.tmp"));
        await Assert.ThrowsAnyAsync<Exception>(() => host.Window.PrepareShutdownAsync());
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => loop); Assert.Same(manager, host.Window.Scripts);
        Assert.Equal(6d, await host.Evaluate("3+3")); Directory.Delete(Path.Combine(f.State, "UserSetting.json.tmp"));
        await host.Window.PrepareShutdownAsync(); Assert.Null(host.Window.Scripts); Assert.Equal(0, manager.ActiveCount);
        await Assert.ThrowsAsync<ObjectDisposedException>(() => manager.EvaluateAsync("1"));
    }
    [AvaloniaFact]
    public async Task ScriptCopyMoveUseExplicitPageAndRealSharedUndoService()
    {
        using var f = new Fixture(); await using var host = await CreateAsync(f); await host.Window.OpenAsync(f.Images); await PageListThumbnailTests.SettleAsync(host.Window);
        var target = Directory.CreateDirectory(Path.Combine(f.Root, "target")).FullName;
        Config.Current.System.DestinationFolderCollection.Add(new("target", target)); Config.Current.System.IsFileWriteAccessEnabled = true;
        var anchor = host.Model.Operation.Book!.CurrentPage;
        await host.Evaluate("nv.DestinationFolderCollection.Items[0].CopyPage(nv.Book.Pages[3]);");
        Assert.True(File.Exists(Path.Combine(target, "004.png"))); Assert.Same(anchor, host.Model.Operation.Book.CurrentPage);
        await host.Evaluate("nv.DestinationFolderCollection.Items[0].MovePage(nv.Book.Pages[2]);");
        Assert.True(File.Exists(Path.Combine(target, "003.png"))); Assert.False(File.Exists(Path.Combine(f.Images, "003.png")));
        Assert.Equal(4, host.Model.Operation.Book.Pages.Count); Assert.Equal(1, host.Model.Operation.DestinationMoves!.UndoCount);
        await host.Evaluate("nv.Command.UndoDestinationMove.Execute();"); Assert.True(File.Exists(Path.Combine(f.Images, "003.png")));
    }
    [AvaloniaFact]
    public async Task ScriptDirectoriesKeepExplicitDestinationAndRefreshCurrentIndex()
    {
        using var f = new Fixture(); await using var host = await CreateAsync(f); await host.Window.OpenAsync(f.Images); await PageListThumbnailTests.SettleAsync(host.Window);
        Config.Current.System.IsFileWriteAccessEnabled = true;
        var external = Directory.CreateDirectory(Path.Combine(f.Root, "external")).FullName;
        File.Copy(Path.Combine(f.Images, "001.png"), Path.Combine(external, "inside.png"));
        var copied = Path.Combine(f.Images, "copied"); var moved = Path.Combine(f.Images, "moved");
        await host.Evaluate($"nv.CopyFile({System.Text.Json.JsonSerializer.Serialize(external)},{System.Text.Json.JsonSerializer.Serialize(copied)});");
        Assert.True(File.Exists(Path.Combine(copied, "inside.png"))); Assert.True(Directory.Exists(external));
        Assert.Contains(host.Model.Operation.Book!.Pages.SourcePages, p => p.ArchiveEntry.SystemPath == copied);
        await host.Evaluate($"nv.MoveFile({System.Text.Json.JsonSerializer.Serialize(external)},{System.Text.Json.JsonSerializer.Serialize(moved)});");
        Assert.False(Directory.Exists(external)); Assert.True(File.Exists(Path.Combine(moved, "inside.png")));
        Assert.Contains(host.Model.Operation.Book!.Pages.SourcePages, p => p.ArchiveEntry.SystemPath == moved);
        Assert.Equal(0, host.Model.Operation.DestinationMoves!.UndoCount); Assert.False(File.Exists(Path.Combine(f.State, ".book-rename-pending.json")));
    }
    [AvaloniaFact]
    public async Task ScriptDeleteRespectsWritePermissionAndUsesExistingTrashFlow()
    {
        using var f = new Fixture(); await using var host = await CreateAsync(f); await host.Window.OpenAsync(f.Images); await PageListThumbnailTests.SettleAsync(host.Window);
        var path = Path.Combine(f.Images, "003.png"); var literal = System.Text.Json.JsonSerializer.Serialize(path);
        await Assert.ThrowsAnyAsync<Exception>(() => host.Evaluate($"nv.DeleteFile({literal});")); Assert.True(File.Exists(path));
        Config.Current.System.IsFileWriteAccessEnabled = true; host.Model.Operation.ConfirmDeleteAsync = _ => Task.FromResult(false);
        await host.Evaluate($"nv.DeleteFile({literal});"); Assert.True(File.Exists(path));
        host.Model.Operation.ConfirmDeleteAsync = _ => Task.FromResult(true);
        await host.Evaluate($"nv.DeleteFile({literal});"); Assert.False(File.Exists(path));
        Assert.True(File.Exists(Path.Combine(f.Root, "Trash", "003.png"))); Assert.Equal(4, host.Model.Operation.Book!.Pages.Count);
        await Assert.ThrowsAnyAsync<Exception>(() => host.Evaluate($"nv.DeleteFile({System.Text.Json.JsonSerializer.Serialize(f.State)});")); Assert.True(Directory.Exists(f.State));
    }
    [AvaloniaFact]
    public async Task ScriptExternalUsesExplicitPageWithoutViewerSelectionAndLiteralArguments()
    {
        using var f = new Fixture(); await using var host = await CreateAsync(f); await host.Window.OpenAsync(f.Images); await PageListThumbnailTests.SettleAsync(host.Window);
        await host.Model.Operation.SetBrowseModeAsync(BrowseLayoutMode.Masonry); Assert.Null(host.Model.Operation.FileActionPage);
        await host.Evaluate("var app=nv.ExternalAppCollection.CreateNew(); app.Command='viewer'; app.Parameter='--file \"{File}\"'; app.Execute(nv.Book.Pages[3]);");
        var request = Assert.Single(host.System.Launches); Assert.Contains(Path.Combine(f.Images, "004.png"), request.Arguments);
        var anchor = host.Model.Operation.Book!.CurrentPage;
        Assert.Equal("ToggleVisibleFilmStrip", await host.Evaluate("nv.Command.ToggleVisibleThumbnailList.Name;"));
        await Assert.ThrowsAnyAsync<Exception>(() => host.Evaluate("nv.Command.TogglePagemark.Execute();"));
        Assert.Same(anchor, host.Model.Operation.Book.CurrentPage);
        await Assert.ThrowsAnyAsync<Exception>(() => host.Evaluate("nv.Book.Config.SortMode='999';"));
    }
    [AvaloniaFact]
    public async Task ScriptTreeEditsSameJsonAndFolderHistoryDoesNotOpenBooks()
    {
        using var f = new Fixture(); await using var host = await CreateAsync(f); await host.Window.OpenAsync(f.Images); await PageListThumbnailTests.SettleAsync(host.Window);
        var book = host.Model.Operation.Book;
        await host.Evaluate($"nv.Bookshelf.Path={System.Text.Json.JsonSerializer.Serialize(f.Images)}; nv.Bookshelf.Path={System.Text.Json.JsonSerializer.Serialize(f.Root)}; nv.Bookshelf.MoveToPrevious();");
        Assert.Equal(f.Images, host.Model.Operation.Bookshelf.Place); Assert.Same(book, host.Model.Operation.Book);
        Assert.Equal(f.Root, await host.Evaluate("nv.Bookshelf.NextHistory[0];"));
        await host.Evaluate("var root=nv.Bookshelf.FolderTree.QuickAccessNode; var folder=root.Add({Type:'folder',Name:'Script group'}); var item=folder.Add({Path:nv.Book.Path,Name:'Book'}); item.Value.Name='Changed';");
        Assert.Equal("Changed", host.Model.SaveData.QuickAccess.Root.Children!.Single().Children!.Single().Name);
        Assert.Contains("Changed", File.ReadAllText(Path.Combine(f.State, "QuicAccess.json")));
        await host.Evaluate("var root=nv.Bookmark.FolderTree.BookmarkNode; var node=root.Add({Name:'Script bookmarks'}); node.Value.Name='Renamed'; nv.Bookmark.FolderTree.SelectedItem=node;");
        Assert.Equal("Renamed", host.Model.SaveData.BookmarkRoot.Children!.Single().Name);
        Assert.Same(host.Model.SaveData.BookmarkRoot.Children!.Single(), host.Window.FindControl<TreeView>("BookmarkTree")!.SelectedItem);
        Assert.True((bool)(await host.Evaluate("var node=nv.Bookmark.FolderTree.BookmarkNode.Children[0]; node.Remove(); nv.Values.stale=node; node.IsDisposed;"))!); Assert.Empty(host.Model.SaveData.BookmarkRoot.Children!);
        await Assert.ThrowsAnyAsync<Exception>(() => host.Evaluate("nv.Values.stale.Add({Name:'stale'});"));
        Directory.CreateDirectory(Path.Combine(f.State, "UserSetting.json.tmp"));
        try { await Assert.ThrowsAnyAsync<Exception>(() => host.Evaluate("nv.Bookshelf.FolderTree.QuickAccessNode.Children[0].Value.Name='Failed';")); Assert.Equal("Script group", host.Model.SaveData.QuickAccess.Root.Children!.Single().Name); }
        finally { Directory.Delete(Path.Combine(f.State, "UserSetting.json.tmp")); }
    }
    [AvaloniaFact]
    public async Task ScriptManualAndConsoleUseActualSurfaceAndCloseCancelsOnlyOwnExecution()
    {
        using var f = new Fixture(); await using var host = await CreateAsync(f);
        await host.Window.ExecuteAsync("HelpScript"); var uri = Assert.Single(host.System.Uris); Assert.Contains("ScriptManual.html", uri.LocalPath);
        Assert.Contains("DeleteFile", File.ReadAllText(uri.LocalPath));
        var words = ScriptCompletion.Create(typeof(ScriptApplicationHost), new ConfigMap(Config.Current), host.Model.Commands);
        Assert.Contains("nv.Config.Script.IsScriptFolderEnabled", words); Assert.Contains("nv.Command.NextPage.Execute", words); Assert.Contains("nv.DeleteFile", words);
        using var cancel = new CancellationTokenSource();
        var external = host.Window.Scripts!.EvaluateAsync("while(true){}", cancel.Token);
        var console = new ScriptConsoleWindow(host.Window.Scripts, () => words); console.Show(host.Window);
        console.GetLogicalDescendants().OfType<TextBox>().Single(t => t.Name == "ScriptInput").Text = "while(true){}"; var run = console.ExecuteInputAsync();
        await UntilAsync(() => host.Window.Scripts.ActiveCount == 2); console.Close(); await run;
        Assert.Equal(1, host.Window.Scripts.ActiveCount); cancel.Cancel(); await Assert.ThrowsAnyAsync<OperationCanceledException>(() => external);
        Assert.Equal(3d, await host.Evaluate("1+2"));
    }
    [AvaloniaFact]
    public async Task ScriptObsoleteMembersRespectOriginalErrorLevelsAndLogReplacement()
    {
        using var f = new Fixture(); await using var host = await CreateAsync(f); var logs = new List<ScriptLog>();
        host.Window.Scripts!.Log += (_, entry) => { lock (logs) logs.Add(entry); };
        await Assert.ThrowsAnyAsync<Exception>(() => host.Evaluate("nv.Book.PageSize;"));
        Assert.Equal(0d, await host.Evaluate("nv.Config.Script.ErrorLevel='Warning'; nv.Book.PageSize;"));
        Assert.Contains(logs, l => l.Level == "warning" && l.Message.Contains("ViewPages"));
        Assert.Null(await host.Evaluate("nv.Command.TogglePagemark;"));
        Assert.Equal("ToggleVisibleFilmStrip", await host.Evaluate("nv.Command.ToggleVisibleThumbnailList.Name;"));
        Assert.Contains(logs, l => l.Level == "info" && l.Message.Contains("ToggleVisibleFilmStrip"));
        Assert.Equal("", await host.Evaluate("nv.Environment.DateVersion;"));
    }
    [AvaloniaFact]
    public async Task ScriptCanceledAfterRealCommitCoordinatesIndexAndKeepsUndo()
    {
        using var f = new Fixture(); await using var host = await CreateAsync(f); await host.Window.OpenAsync(f.Images); await PageListThumbnailTests.SettleAsync(host.Window);
        Config.Current.System.IsFileWriteAccessEnabled = true;
        var target = Directory.CreateDirectory(Path.Combine(f.Root, "canceled-target")).FullName;
        Config.Current.System.DestinationFolderCollection.Add(new("target", target));
        using var cancel = new CancellationTokenSource(); var real = new FileOperationBackend(Path.Combine(f.State, "cancel-recovery"));
        var service = new DestinationMoveService(new CancelAfterCommit(real, cancel));
        host.Model.Operation.AttachFileOperations(service, real, host.Images);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => host.Window.Scripts!.EvaluateAsync("nv.DestinationFolderCollection.Items[0].MovePage([nv.Book.Pages[2],nv.Book.Pages[3]]);", cancel.Token));
        Assert.False(File.Exists(Path.Combine(f.Images, "003.png"))); Assert.True(File.Exists(Path.Combine(target, "003.png")));
        Assert.True(File.Exists(Path.Combine(f.Images, "004.png"))); Assert.False(File.Exists(Path.Combine(target, "004.png")));
        Assert.Equal(4, host.Model.Operation.Book!.Pages.Count); Assert.Equal(1, service.UndoCount);
        await host.Model.Operation.ReplayDestinationMoveAsync(true); Assert.True(File.Exists(Path.Combine(f.Images, "003.png")));
    }
    [AvaloniaFact]
    public async Task ScriptSettingsCancelAndFailedSaveRestoreSameManagerAndFolder()
    {
        using var f = new Fixture(); var original = Directory.CreateDirectory(Path.Combine(f.Root, "original-scripts")).FullName;
        var alternate = Directory.CreateDirectory(Path.Combine(f.Root, "alternate-scripts")).FullName;
        File.WriteAllText(Path.Combine(original, "first.nvjs"), "1"); File.WriteAllText(Path.Combine(alternate, "second.nvjs"), "2");
        await using var host = await CreateAsync(f, original); var branch = Config.Current.Script; var manager = host.Window.Scripts!;
        var canceled = new SettingsWindow(host.Model); canceled.Show(host.Window);
        var draft = (ScriptSettingsViewModel)canceled.FindControl<ScrollViewer>("ScriptSettings")!.DataContext!; draft.Folder = alternate; canceled.Close();
        Assert.Equal(original, branch.ScriptFolder); Assert.True(host.Model.Commands.IsAvailable("Script_first"));
        var settings = new SettingsWindow(host.Model); settings.Show(host.Window);
        draft = (ScriptSettingsViewModel)settings.FindControl<ScrollViewer>("ScriptSettings")!.DataContext!; draft.Folder = alternate;
        await host.Model.SaveData.SynchronizeWritesAsync(); var blocked = Path.Combine(f.State, "UserSetting.json.tmp"); Directory.CreateDirectory(blocked);
        try
        {
            settings.FindControl<Button>("SaveSettings")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await UntilAsync(() => settings.FindControl<TextBlock>("Message")!.Text?.StartsWith("保存失败：") == true);
            Assert.False(settings.WasSaved); Assert.Same(branch, Config.Current.Script); Assert.Equal(original, branch.ScriptFolder); Assert.Equal(alternate, draft.Folder);
            await UntilAsync(() => host.Model.Commands.IsAvailable("Script_first") && !host.Model.Commands.IsAvailable("Script_second"));
            Assert.Same(manager, host.Window.Scripts);
        }
        finally { Directory.Delete(blocked); settings.Close(); }
        File.WriteAllText(Path.Combine(original, "after-rollback.nvjs"), "3");
        await manager.ReloadAsync(Token); await UntilAsync(() => host.Model.Commands.IsAvailable("Script_after-rollback"));
    }
    [Fact]
    public async Task ScriptSampleFolderKeepsOriginalBytesAndExistingFolderUntouched()
    {
        using var f = new Fixture(); var path = Path.Combine(f.Root, "new-scripts");
        await ScriptFolderService.PrepareAsync(path, Token); var sample = File.ReadAllBytes(Path.Combine(path, "Sample.nvjs"));
        using var stream = typeof(ScriptFolderService).Assembly.GetManifestResourceStream("NeeView.Resources.Scripts.Sample.nvjs")!;
        using var bytes = new MemoryStream(); stream.CopyTo(bytes); Assert.Equal(bytes.ToArray(), sample);
        File.Delete(Path.Combine(path, "Sample.nvjs")); await ScriptFolderService.PrepareAsync(path, Token); Assert.False(File.Exists(Path.Combine(path, "Sample.nvjs")));
        using var cancel = new CancellationTokenSource(); cancel.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => ScriptFolderService.PrepareAsync(Path.Combine(f.Root, "cancelled"), cancel.Token));
        Assert.False(Directory.Exists(Path.Combine(f.Root, "cancelled")));
    }
}
