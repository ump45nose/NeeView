using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.Platform.Storage;
using NeeView.Backends;
using NeeView.MacOS.ViewModels;
using NeeView.MacOS.Views;
namespace NeeView.Engine.Tests;

/// <summary>原备份/保存/重载范围、失败闭环与正式宿主，全部使用隔离合成Profile。</summary>
public sealed class ProfileExportTests
{
    private sealed class Platform : IPlatformService
    { public Task RevealAsync(string path, CancellationToken token = default) => Task.CompletedTask; public Task TrashAsync(string path, CancellationToken token = default) => Task.CompletedTask; }
    private static CancellationToken Token => TestContext.Current.CancellationToken;
    private sealed class Fixture : IDisposable
    {
        public string Root { get; } = Path.Combine(Path.GetTempPath(), "NeeView-P5-ProfileCommands-" + Guid.NewGuid().ToString("N"));
        public string State => Path.Combine(Root, "Profile");
        public string Images => Path.Combine(Root, "Images");
        public Fixture() { Directory.CreateDirectory(State); Directory.CreateDirectory(Images); }
        public void Put(string name, string value) { var path = Path.Combine(State, name); Directory.CreateDirectory(Path.GetDirectoryName(path)!); File.WriteAllText(path, value); }
        public JsonObject Read(string name) => JsonNode.Parse(File.ReadAllText(Path.Combine(State, name)))!.AsObject();
        public void Pictures()
        { using var image = new ImageMagick.MagickImage(ImageMagick.MagickColors.Teal, 20, 40); for (int i = 0; i < 4; i++) image.Write(Path.Combine(Images, i + ".png")); }
        public async Task<SaveData> Load() { var state = new SaveData(State); await state.LoadAsync(Token); return state; }
        public BookOperation Operation(SaveData state) => new(new ArchiveFactory(), new MagickImageDecoder(), state);
        public void Dispose() => Directory.Delete(Root, true);
    }
    [Fact]
    public async Task ExactRootAndFirstLevelAssetsRoundTripWithoutExecutingOrTouchingSources()
    {
        using var f = new Fixture(); var state = await f.Load();
        await state.SaveAsync(null, Token);
        f.Put("Playlists/Default.nvpls", "{\"Format\":\"NeeView.Playlist/2\",\"Items\":[]}");
        f.Put("Playlists/Pagemark.nvpls", "{\"Format\":\"NeeView.Playlist/2\",\"Items\":[]}");
        f.Put("Themes/颜色.JSON", "{\"Future\":42}"); f.Put("Scripts/script.NVJS", "throw new Error('never execute')");
        f.Put("Pagemark.json", "must not export"); f.Put("Themes/nested/no.json", "{}"); f.Put("Scripts/no.js", "ignored");
        var before = Directory.EnumerateFiles(f.State, "*", SearchOption.AllDirectories).ToDictionary(path => path, File.ReadAllBytes);
        var result = await state.ExportBackupAsync(Path.Combine(f.Root, "backup.nvzip"), Token);
        Assert.Equal(9, result.Entries.Count);
        Assert.Contains("Foldres.json", result.Entries); Assert.Contains("QuicAccess.json", result.Entries);
        Assert.Contains("Playlists/Default.nvpls", result.Entries); Assert.Contains("Playlists/Pagemark.nvpls", result.Entries);
        Assert.DoesNotContain("Pagemark.json", result.Entries); Assert.DoesNotContain("Themes/nested/no.json", result.Entries);
        foreach (var pair in before) Assert.Equal(pair.Value, File.ReadAllBytes(pair.Key));
        var imported = await new ProfileImportReader().ReadAsync(new(result.Path, ProfileImportSourceKind.Backup), Token);
        Assert.Equal(5, imported.Files.Count); Assert.Equal(4, imported.Assets!.Count);
        Assert.Equal(Encoding.UTF8.GetBytes("throw new Error('never execute')"), imported.Assets["Scripts/script.NVJS"]);
    }
    [Fact]
    public async Task CustomMaterialFoldersUseOriginalBasenameAndIgnoreDisabledScriptExecutionFlag()
    {
        using var f = new Fixture(); var external = Path.Combine(f.Root, "Materials"); Directory.CreateDirectory(external);
        File.WriteAllText(Path.Combine(external, "custom.nvpls"), "list"); File.WriteAllText(Path.Combine(external, "custom.json"), "theme"); File.WriteAllText(Path.Combine(external, "custom.nvjs"), "script");
        f.Put("UserSetting.json", new JsonObject { ["Config"] = new JsonObject { ["Script"] = new JsonObject { ["ScriptFolder"] = external, ["IsScriptFolderEnabled"] = false } } }.ToJsonString());
        var state = await f.Load(); Config.Current.Playlist.PlaylistFolderRaw = external; Config.Current.Theme.CustomThemeFolder = external;
        var result = await state.ExportBackupAsync(Path.Combine(f.Root, "custom.nvzip"), Token);
        Assert.Equal(["UserSetting.json", "Playlists/custom.nvpls", "Themes/custom.json", "Scripts/custom.nvjs"], result.Entries);
    }
    [Theory]
    [InlineData("missing")]
    [InlineData("oversized")]
    [InlineData("invalid-material")]
    public async Task ExportFailurePreservesExistingBackupAndLeavesNoTemporaryZip(string scenario)
    {
        using var f = new Fixture(); var state = await f.Load();
        var target = Path.Combine(f.Root, "old.nvzip"); var old = new byte[] { 1, 4, 2 }; File.WriteAllBytes(target, old);
        if (scenario != "missing") await state.SaveAsync(null, Token);
        if (scenario == "oversized") { f.Put("Scripts/large.nvjs", ""); using var stream = File.OpenWrite(Path.Combine(f.State, "Scripts/large.nvjs")); stream.SetLength(ProfileImportFiles.MaxFileBytes + 1); }
        if (scenario == "invalid-material") { f.Put("Themes", "not a directory"); }
        await Assert.ThrowsAnyAsync<Exception>(() => state.ExportBackupAsync(target, Token));
        Assert.Equal(old, File.ReadAllBytes(target)); Assert.Empty(Directory.EnumerateFiles(f.Root, "*.tmp"));
    }
    [Fact]
    public async Task CancelledExportAndAuthorityTargetsKeepOriginalBytes()
    {
        using var f = new Fixture(); var state = await f.Load(); await state.SaveAsync(null, Token);
        var target = Path.Combine(f.Root, "backup.nvzip"); File.WriteAllText(target, "old");
        using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => state.ExportBackupAsync(target, cancellation.Token)); Assert.Equal("old", File.ReadAllText(target));
        var settings = File.ReadAllBytes(Path.Combine(f.State, "UserSetting.json"));
        await Assert.ThrowsAsync<InvalidDataException>(() => state.ExportBackupAsync(Path.Combine(f.State, "UserSetting.json"), Token));
        Assert.Equal(settings, File.ReadAllBytes(Path.Combine(f.State, "UserSetting.json")));
    }
    [Fact]
    public async Task SymlinkMaterialFailsWithoutReadingUnrelatedFile()
    {
        using var f = new Fixture(); var state = await f.Load(); await state.SaveAsync(null, Token);
        f.Put("Scripts/real.nvjs", "keep"); File.CreateSymbolicLink(Path.Combine(f.State, "Scripts/link.nvjs"), "real.nvjs");
        await Assert.ThrowsAsync<InvalidDataException>(() => state.ExportBackupAsync(Path.Combine(f.Root, "backup.nvzip"), Token));
        Assert.False(File.Exists(Path.Combine(f.Root, "backup.nvzip")));
    }
    [Fact]
    public async Task SaveAllAndExportFlushCurrentReadingAndUniqueJsonState()
    {
        using var f = new Fixture(); f.Pictures(); var state = await f.Load(); await using var op = f.Operation(state);
        await op.OpenAsync(f.Images, Token); await op.JumpAsync(2); Config.Current.View.IsKeepAngle = true;
        var result = await op.ExportBackupAsync(Path.Combine(f.Root, "reader.nvzip"), Token);
        var bundle = await new ProfileImportReader().ReadAsync(new(result.Path, ProfileImportSourceKind.Backup), Token);
        Assert.Equal("2.png", JsonNode.Parse(bundle.Files["History.json"])!["Items"]![0]!["Page"]!.GetValue<string>());
        Assert.True(JsonNode.Parse(bundle.Files["UserSetting.json"])!["Config"]!["View"]!["IsKeepAngle"]!.GetValue<bool>());
        Assert.Same(op.Book!.Setting, Config.Current.BookSetting); Assert.Equal(2, op.Position.Index);
    }
    [Fact]
    public async Task ReloadUsesOnlySettingAndPreservesConfigBookPageSourceAndCollectionObjects()
    {
        using var f = new Fixture(); f.Pictures(); var state = await f.Load(); await using var op = f.Operation(state);
        await op.OpenAsync(f.Images, Token); await op.JumpAsync(2); await op.SaveAllAsync(Token);
        var root = Config.Current; var view = root.View; var book = op.Book; var source = book!.Source; var page = book.CurrentPage;
        var bookmarks = state.Bookmarks; var folders = state.FolderConfigs; var quick = state.QuickAccess; var history = state.HistoryEntries.ToArray();
        var setting = f.Read("UserSetting.json");
        var config = setting["Config"]!.AsObject(); config["View"] ??= new JsonObject(); config["BookSetting"] ??= new JsonObject();
        config["View"]!["IsKeepAngle"] = true;
        setting["Config"]!["BookSetting"]!["PageMode"] = "WidePage";
        setting["Commands"] = new JsonObject { ["NextPage"] = new JsonObject { ["ShortCutKey"] = "Ctrl+J" } };
        setting["OnlyInNewSetting"] = 42; f.Put("UserSetting.json", setting.ToJsonString());
        foreach (var name in ProfileImportFiles.Names.Where(name => name != "UserSetting.json")) f.Put(name, "external broken material must not load");
        var settingsBytes = File.ReadAllBytes(Path.Combine(f.State, "UserSetting.json"));
        await op.ReloadSettingAsync(Token);
        Assert.Same(root, Config.Current); Assert.Same(view, root.View); Assert.Same(book, op.Book); Assert.Same(source, op.Book!.Source);
        Assert.Same(page, book.CurrentPage); Assert.Equal(2, op.Position.Index); Assert.Equal(PageMode.WidePage, book.Setting.PageMode);
        Assert.Same(bookmarks, state.Bookmarks); Assert.Same(folders, state.FolderConfigs); Assert.Same(quick, state.QuickAccess);
        Assert.Equal(history, state.HistoryEntries); Assert.True(root.View.IsKeepAngle); Assert.Equal("Ctrl+J", state.GetShortcut("NextPage", "Right"));
        Assert.Equal(settingsBytes, File.ReadAllBytes(Path.Combine(f.State, "UserSetting.json")));
        Assert.All(ProfileImportFiles.Names.Where(name => name != "UserSetting.json"), name => Assert.Equal("external broken material must not load", File.ReadAllText(Path.Combine(f.State, name))));
        await op.SaveAllAsync(Token); Assert.Equal(42, f.Read("UserSetting.json")["OnlyInNewSetting"]!.GetValue<int>());
    }
    [Fact]
    public async Task ReloadRecursiveRuleRecollectsSourceThroughOriginalDirtyBookBoundary()
    {
        using var f = new Fixture(); f.Pictures();
        Directory.CreateDirectory(Path.Combine(f.Images, "sub")); File.Copy(Path.Combine(f.Images, "0.png"), Path.Combine(f.Images, "sub/extra.png"));
        var state = await f.Load(); await using var op = f.Operation(state);
        await op.OpenAsync(f.Images, Token); await op.JumpAsync(op.Book!.Pages.FindIndex(page => page.EntryName == "0.png")); await op.SaveAllAsync(Token); var before = op.Book;
        var setting = f.Read("UserSetting.json"); var config = setting["Config"]!.AsObject(); config["BookSetting"] ??= new JsonObject();
        config["BookSetting"]!["IsRecursiveFolder"] = true;
        config["BookSetting"]!["SortMode"] = "FileNameDescending"; f.Put("UserSetting.json", setting.ToJsonString());
        await op.ReloadSettingAsync(Token);
        Assert.NotSame(before, op.Book); Assert.True(op.Book!.Setting.IsRecursiveFolder);
        Assert.Contains(op.Book.Pages, page => page.EntryName == "sub/extra.png");
        Assert.Equal(PageSortMode.FileNameDescending, op.Book.EffectiveSortMode);
        Assert.Equal("0.png", op.Book.CurrentPage!.EntryName);
    }
    private sealed class ControlledFactory : IArchiveFactory
    {
        private readonly ArchiveFactory _inner = new();
        public bool HoldNext { get; set; }
        public bool FailNext { get; set; }
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public async Task<Archive> OpenAsync(string path, CancellationToken token)
        {
            if (FailNext) { FailNext = false; throw new IOException("isolated source failure"); }
            if (HoldNext) { HoldNext = false; Entered.TrySetResult(); await Release.Task.WaitAsync(token); }
            return await _inner.OpenAsync(path, token);
        }
        public Task<IReadOnlyList<FolderItem>> ListFoldersAsync(string path, CancellationToken token) => _inner.ListFoldersAsync(path, token);
        public Task<IReadOnlyList<FolderItem>> ListBooksAsync(string path, CancellationToken token) => _inner.ListBooksAsync(path, token);
    }
    [Fact]
    public async Task RecollectionFailureKeepsOldSourceAndEffectiveRecursionThenAllowsRetry()
    {
        using var f = new Fixture(); f.Pictures(); var state = await f.Load(); var factory = new ControlledFactory();
        await using var op = new BookOperation(factory, new MagickImageDecoder(), state);
        await op.OpenAsync(f.Images, Token); await op.SaveAllAsync(Token); var before = op.Book;
        var setting = f.Read("UserSetting.json"); setting["Config"]!["BookSetting"] ??= new JsonObject();
        setting["Config"]!["BookSetting"]!["IsRecursiveFolder"] = true; f.Put("UserSetting.json", setting.ToJsonString());
        factory.FailNext = true; await Assert.ThrowsAsync<IOException>(() => op.ReloadSettingAsync(Token));
        Assert.Same(before, op.Book); Assert.False(op.Book!.Setting.IsRecursiveFolder); Assert.False(op.IsLoading);
        await op.ReloadSettingAsync(Token); Assert.NotSame(before, op.Book); Assert.True(op.Book!.Setting.IsRecursiveFolder);
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RecollectionCancellationOrNewOpenCannotPublishOldBook(bool newOpen)
    {
        using var f = new Fixture(); f.Pictures(); var state = await f.Load(); var factory = new ControlledFactory();
        await using var op = new BookOperation(factory, new MagickImageDecoder(), state);
        await op.OpenAsync(f.Images, Token); await op.SaveAllAsync(Token); var before = op.Book;
        var setting = f.Read("UserSetting.json"); setting["Config"]!["BookSetting"] ??= new JsonObject();
        setting["Config"]!["BookSetting"]!["IsRecursiveFolder"] = true; f.Put("UserSetting.json", setting.ToJsonString());
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(Token);
        factory.HoldNext = true; var reload = op.ReloadSettingAsync(cancellation.Token); await factory.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5), Token);
        if (newOpen)
        {
            var other = Path.Combine(f.Root, "Other"); Directory.CreateDirectory(other); File.Copy(Path.Combine(f.Images, "0.png"), Path.Combine(other, "new.png"));
            await op.OpenAsync(other, Token); await reload.WaitAsync(TimeSpan.FromSeconds(5), Token);
            Assert.Equal(other, op.Book!.Path); Assert.Equal("new.png", op.Book.CurrentPage!.EntryName);
        }
        else
        {
            cancellation.Cancel(); await Assert.ThrowsAnyAsync<OperationCanceledException>(() => reload);
            Assert.Same(before, op.Book); Assert.False(op.Book!.Setting.IsRecursiveFolder); Assert.False(op.IsLoading);
        }
    }
    [Theory]
    [InlineData("oversized")]
    [InlineData("directory")]
    public async Task UnreadableOrOverBudgetSettingPreservesCommandsAndConfig(string scenario)
    {
        using var f = new Fixture(); var state = await f.Load(); await using var op = f.Operation(state);
        Config.Current.View.IsKeepAngle = true; state.SetShortcut("NextPage", "Ctrl+K"); var path = Path.Combine(f.State, "UserSetting.json");
        if (scenario == "directory") Directory.CreateDirectory(path);
        else { using var stream = File.Create(path); stream.SetLength(ProfileImportFiles.MaxFileBytes + 1); }
        await Assert.ThrowsAnyAsync<Exception>(() => op.ReloadSettingAsync(Token));
        Assert.True(Config.Current.View.IsKeepAngle); Assert.Equal("Ctrl+K", state.GetShortcut("NextPage", "Right"));
        if (scenario == "directory") Directory.Delete(path); else File.Delete(path);
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ClosingStopsUncancellablePickerWaitAndObservesLateCompletion(bool failsLate)
    {
        using var cancellation = new CancellationTokenSource(); var picker = new TaskCompletionSource<IStorageFile?>();
        var waiting = MainWindow.AwaitBackupPathAsync(picker.Task, cancellation.Token);
        cancellation.Cancel(); await Assert.ThrowsAnyAsync<OperationCanceledException>(() => waiting);
        if (failsLate) picker.SetException(new IOException("late native failure")); else picker.SetResult(null);
        Assert.Null(await MainWindow.AwaitBackupPathAsync(Task.FromResult<IStorageFile?>(null), Token));
    }
    [Theory]
    [InlineData("broken")]
    [InlineData("future")]
    [InlineData("bad-parameter")]
    public async Task InvalidReloadPreservesRuntimeAndOldCommands(string scenario)
    {
        using var f = new Fixture(); var state = await f.Load(); await using var op = f.Operation(state);
        Config.Current.View.IsKeepAngle = true; state.SetShortcut("NextPage", "Ctrl+K");
        f.Put("UserSetting.json", scenario switch
        {
            "broken" => "not json",
            "future" => "{\"Format\":\"NeeView.UserSetting/99.0.0\"}",
            _ => "{\"Config\":{\"View\":{\"IsKeepAngle\":false}},\"Commands\":{\"ExportBackup\":{\"Parameter\":{\"FileName\":42}}}}"
        });
        var before = File.ReadAllBytes(Path.Combine(f.State, "UserSetting.json"));
        await Assert.ThrowsAnyAsync<Exception>(() => op.ReloadSettingAsync(Token));
        Assert.True(Config.Current.View.IsKeepAngle); Assert.Equal("Ctrl+K", state.GetShortcut("NextPage", "Right"));
        Assert.Equal(before, File.ReadAllBytes(Path.Combine(f.State, "UserSetting.json")));
        f.Put("UserSetting.json", "{}"); await op.ReloadSettingAsync(Token);
        Assert.True(Config.Current.View.IsKeepAngle); Assert.Equal("Left,LeftClick", state.GetShortcut("NextPage", "Right"));
    }
    [Fact]
    public async Task MissingSettingResetsCommandDifferenceWithoutChangingConfigOrCreatingFiles()
    {
        using var f = new Fixture(); var state = await f.Load(); await using var op = f.Operation(state);
        Config.Current.View.IsKeepAngle = true; state.SetShortcut("NextPage", "Ctrl+K");
        await op.ReloadSettingAsync(Token); Assert.True(Config.Current.View.IsKeepAngle);
        Assert.Equal("Left,LeftClick", state.GetShortcut("NextPage", "Right")); Assert.Empty(Directory.EnumerateFiles(f.State));
    }
    [AvaloniaFact]
    public async Task FormalWindowCommandsReloadLayoutAndEditStringParameterWithoutReopeningBook()
    {
        using var f = new Fixture(); f.Pictures(); var state = await f.Load(); var op = f.Operation(state); var images = new BitmapFactory(new MagickImageDecoder());
        var model = new ReaderWorkspaceViewModel(op, new CommandTable(op), state); var window = new MainWindow(); window.Bind(model, images, new Platform()); window.Show();
        try
        {
            await window.OpenAsync(f.Images); Dispatcher.UIThread.RunJobs();
            await window.ExecuteAsync("SaveSetting"); Assert.True(File.Exists(Path.Combine(f.State, "UserSetting.json")));
            var book = op.Book; var layout = model.Layout;
            var setting = f.Read("UserSetting.json"); setting["Config"]!["Panels"]!["LeftWidth"] = 260;
            setting["Config"]!["Panels"]!["Layout"] = JsonNode.Parse("{\"Docks\":{\"Left\":{\"PanelLayoutV2\":[\"Horizontal:FolderPanel,PageListPanel\"],\"SelectedItem\":\"FolderPanel\"},\"Right\":{\"PanelLayoutV2\":[\"Vertical:HistoryPanel\"],\"SelectedItem\":\"HistoryPanel\"}}}");
            f.Put("UserSetting.json", setting.ToJsonString()); await window.ExecuteAsync("ReloadSetting"); Dispatcher.UIThread.RunJobs();
            Assert.Same(book, op.Book); Assert.NotSame(layout, model.Layout); Assert.Equal(2, model.Layout.Docks["Left"].Items[0].Count);
            Assert.Equal(260, Config.Current.Panels.LeftWidth); Assert.True(window.IsEnabled);
            var destination = Path.Combine(f.Root, "window.nvzip"); state.SetCommandParameter("ExportBackup", new ExportBackupCommandParameter { FileName = destination });
            await window.ExecuteAsync("ExportBackup"); Assert.True(File.Exists(destination)); Assert.Same(book, op.Book);
            var draft = CommandParameterEdit.Create(state, "ExportBackup")!; var dialog = new CommandParameterWindow(draft, "导出备份");
            dialog.Show(window); var field = dialog.FindControl<TextBox>("FileName")!; field.Text = "changed.nvzip"; Dispatcher.UIThread.RunJobs();
            Assert.Equal("changed.nvzip", ((ExportBackupCommandParameter)draft.Value).FileName);
            Assert.Equal(destination, state.GetCommandParameter<ExportBackupCommandParameter>("ExportBackup").FileName); dialog.Close();
            Assert.True(window.IsCommandAvailable("SaveSetting")); Assert.True(window.IsCommandAvailable("ReloadSetting"));
            if (Environment.GetEnvironmentVariable("NEEVIEW_P5_PROFILE_SCREENSHOT") is { } path) { Dispatcher.UIThread.RunJobs(); window.UpdateLayout(); using var capture = window.CaptureRenderedFrame(); capture!.Save(path, Avalonia.Media.Imaging.PngBitmapEncoderOptions.Default); }
        }
        finally { await window.PrepareShutdownAsync(); window.Close(); }
    }
}
