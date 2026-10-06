using System.Text.Json;
using System.Text.Json.Nodes;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using NeeView.Backends;
using NeeView.MacOS.ViewModels;
using NeeView.MacOS.Views;
namespace NeeView.Engine.Tests;

/// <summary>原导入选择/默认值及完整关闭-事务-重建链路；全部写入隔离合成 Profile。</summary>
public sealed class ProfileImportApplyTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;
    private sealed class Reader(Dictionary<string, string> files) : IProfileImportReader
    { public Task<ProfileImportBundle> ReadAsync(ProfileImportSource source, CancellationToken token) => Task.FromResult(new ProfileImportBundle(files, [])); }
    private static async Task<ProfileImportPreview> Preview(Dictionary<string, string> files)
    {
        using var stream = typeof(CommandTable).Assembly.GetManifestResourceStream("NeeView.Command.command-manifest.json")!;
        var definitions = JsonSerializer.Deserialize<List<CommandDefinition>>(stream)!;
        return await new ProfileImportService(new Reader(files), definitions, new HashSet<string>()).PreviewAsync(new("synthetic", ProfileImportSourceKind.Directory), [], Token);
    }
    private static Dictionary<string, string> Files => new()
    {
        ["UserSetting.json"] = """{"Format":"NeeView.UserSetting/46.3.0","Config":{"History":{"LimitSpan":"00:00:00","LimitSize":100},"View":{"Future":42},"FutureBranch":{"X":[1,2]},"StartUp":{"LastBookV2":{"Path":"/import/book.cbz","Page":"内页\\003.png","Props":"RightToLeft Unknown=7"}}},"Commands":{"NextPage":{"ShortCutKey":"Ctrl+J","Parameter":{"Future":4}},"Script:old":{"Script":"must not run"}}}""",
        ["History.json"] = """{"Format":"NeeView.History/46.3.0","Items":[{"Path":"/import/book.cbz","Page":"内页\\003.png","Props":"SinglePage Unknown=7","Future":8,"LastAccessTime":"2026-10-05T10:00:00"}],"Future":42}""",
        ["Bookmark.json"] = """{"Format":"NeeView.Bookmark/46.3.0","Nodes":{"Children":[{"Name":"Folder","Children":[{"Name":"First","Path":"/import/book.cbz","Page":"001.png","Future":9}]},{"Name":"Last","Path":"/import/b"}]}}""",
        ["Foldres.json"] = """{"Format":"NeeView.Folders/46.3.0","Folders":[{"Place":"/import","Parameter":{"IsFolderRecursive":true,"Future":4}}]}""",
        ["QuicAccess.json"] = """{"Format":"NeeView.QuickAccess/46.3.0","Items":[{"Path":"/import","Name":"Imported"}]}"""
    };
    private sealed class Fixture : IDisposable
    {
        public string Root { get; } = Path.Combine(Path.GetTempPath(), "NeeView-P5-Apply-" + Guid.NewGuid().ToString("N"));
        public Fixture() => Directory.CreateDirectory(Root);
        public void Put(string name, string text) => File.WriteAllText(Path.Combine(Root, name), text);
        public JsonObject Get(string name) => JsonNode.Parse(File.ReadAllText(Path.Combine(Root, name)))!.AsObject();
        public void Dispose() => Directory.Delete(Root, true);
    }
    private static ProfileImportSelection All => new(true, true, true, true, true);

    [Fact]
    public async Task DefaultSelectionRestoresSettingDefaultsAndKeepsUnselectedCollections()
    {
        using var fixture = new Fixture();
        fixture.Put("UserSetting.json", """{"Config":{"View":{"IsKeepAngle":true,"OldFuture":1},"FutureBranch":{"X":1}},"Commands":{"NextPage":{"ShortCutKey":"Old"},"PrevPage":{"ShortCutKey":"Old"}},"ExistingFuture":2}""");
        fixture.Put("History.json", """{"Items":[{"Path":"/old","Page":"keep"}]}""");
        var history = File.ReadAllBytes(Path.Combine(fixture.Root, "History.json")); var current = Config.Current;
        var state = new SaveData(fixture.Root); var request = (await Preview(Files)).CreateRequest(new());
        await state.ValidateProfileImportAsync(request, Token); Assert.Same(current, Config.Current);
        var result = await state.ApplyProfileImportAsync(request, Token);
        Assert.DoesNotContain("History.json", result.AppliedFiles); Assert.DoesNotContain("Bookmark.json", result.AppliedFiles);
        Assert.Equal(history, File.ReadAllBytes(Path.Combine(fixture.Root, "History.json")));
        var raw = fixture.Get("UserSetting.json");
        Assert.False(raw["Config"]!["View"]!["IsKeepAngle"]!.GetValue<bool>());
        Assert.Equal(1, raw["Config"]!["View"]!["OldFuture"]!.GetValue<int>());
        Assert.Equal(42, raw["Config"]!["View"]!["Future"]!.GetValue<int>());
        Assert.Equal(2, raw["ExistingFuture"]!.GetValue<int>());
        Assert.Equal(2, raw["Config"]!["FutureBranch"]!["X"]![1]!.GetValue<int>());
        Assert.Null(raw["Commands"]!["PrevPage"]); Assert.Equal("Ctrl+J", raw["Commands"]!["NextPage"]!["ShortCutKey"]!.GetValue<string>());
        Assert.True(File.Exists(Path.Combine(result.BackupDirectory, "manifest.json")));
    }
    [Fact]
    public async Task RawHistoryImportIgnoresCurrentSavePolicyAndRebuiltObjectsSurviveNextSave()
    {
        using var fixture = new Fixture(); Config.Current.History.IsSaveHistory = false;
        var state = new SaveData(fixture.Root); var result = await state.ApplyProfileImportAsync((await Preview(Files)).CreateRequest(All), Token);
        Assert.Single(fixture.Get("History.json")["Items"]!.AsArray());
        var reloaded = new SaveData(fixture.Root); await reloaded.LoadAsync(Token);
        Assert.True(Config.Current.History.IsSaveHistory);
        Assert.Equal("内页\\003.png", reloaded.Find("/import/book.cbz")!.Page);
        Assert.Equal("Folder", reloaded.BookmarkRoot.Children![0].Name);
        Assert.Equal("First", reloaded.BookmarkRoot.Children[0].Children![0].Name);
        Assert.Equal("Imported", reloaded.QuickAccess.Root.Children![0].Name);
        await reloaded.SaveAsync(null, Token);
        Assert.Equal(8, fixture.Get("History.json")["Items"]![0]!["Future"]!.GetValue<int>());
        Assert.Equal(9, fixture.Get("Bookmark.json")["Nodes"]!["Children"]![0]!["Children"]![0]!["Future"]!.GetValue<int>());
        Assert.Equal("Ctrl+J", reloaded.GetShortcut("NextPage", "Right"));
        await state.RestoreProfileImportAsync(result.BackupDirectory, Token);
        Assert.All(ProfileImportFiles.Names, name => Assert.False(File.Exists(Path.Combine(fixture.Root, name))));
    }
    [Fact]
    public async Task BackupRestoresExactBytesAndMissingFilesAndRefusesCorruption()
    {
        using var fixture = new Fixture(); fixture.Put("UserSetting.json", "{ \n\"Unknown\": true }");
        var bytes = File.ReadAllBytes(Path.Combine(fixture.Root, "UserSetting.json"));
        var state = new SaveData(fixture.Root); var result = await state.ApplyProfileImportAsync((await Preview(Files)).CreateRequest(All), Token);
        var imported = File.ReadAllBytes(Path.Combine(fixture.Root, "UserSetting.json"));
        var backupFile = Path.Combine(result.BackupDirectory, "UserSetting.json"); File.AppendAllText(backupFile, "bad");
        await Assert.ThrowsAsync<InvalidDataException>(() => state.RestoreProfileImportAsync(result.BackupDirectory, Token));
        Assert.Equal(imported, File.ReadAllBytes(Path.Combine(fixture.Root, "UserSetting.json")));
        File.WriteAllBytes(backupFile, bytes); await state.RestoreProfileImportAsync(result.BackupDirectory, Token);
        Assert.Equal(bytes, File.ReadAllBytes(Path.Combine(fixture.Root, "UserSetting.json")));
        Assert.False(File.Exists(Path.Combine(fixture.Root, "History.json")));
    }
    [Fact]
    public async Task SelectedMissingFilesStayUntouchedAndEmptySelectionIsRejected()
    {
        using var fixture = new Fixture(); fixture.Put("QuicAccess.json", """{"Format":"NeeView.QuickAccess/46.3.0","Items":[]}""");
        var bytes = File.ReadAllBytes(Path.Combine(fixture.Root, "QuicAccess.json"));
        var preview = await Preview(new() { ["UserSetting.json"] = Files["UserSetting.json"] });
        Assert.Throws<InvalidDataException>(() => preview.CreateRequest(new(false, false, false, true, true)));
        await new SaveData(fixture.Root).ApplyProfileImportAsync(preview.CreateRequest(new()), Token);
        Assert.Equal(bytes, File.ReadAllBytes(Path.Combine(fixture.Root, "QuicAccess.json")));
    }
    [Theory]
    [InlineData("NeeView/37.0.0")]
    [InlineData("NeeView/47.0.0")]
    [InlineData("NeeView/46.3.4341")]
    [InlineData("wrong/46.3.0")]
    [InlineData("")]
    public async Task UnsupportedSettingsCanBePreviewedAndDeselectedButNeverApplied(string format)
    {
        var files = Files; files["UserSetting.json"] = new JsonObject { ["Format"] = format, ["Config"] = new JsonObject() }.ToJsonString();
        var preview = await Preview(files); Assert.Throws<InvalidDataException>(() => preview.CreateRequest(new()));
        using var fixture = new Fixture(); await new SaveData(fixture.Root).ApplyProfileImportAsync(preview.CreateRequest(new(false, false, false, true, true)), Token);
        Assert.False(File.Exists(Path.Combine(fixture.Root, "UserSetting.json")));
    }
    [Theory]
    [InlineData("UserSetting.json", "{\"Format\":\"NeeView/46.3.0\",\"Config\":{\"History\":{\"LimitSpan\":\"bad\"}}}")]
    [InlineData("UserSetting.json", "{\"Format\":\"NeeView/46.3.0\",\"Commands\":{\"NextPage\":{\"Parameter\":{\"IsReverse\":\"bad\"}}}}")]
    [InlineData("History.json", "{\"Format\":\"NeeView.History/46.3.0\",\"Items\":[{\"Path\":\"a\",\"Page\":\"x\",\"Props\":\"Base=bad\"}]}")]
    [InlineData("Bookmark.json", "{\"Format\":\"NeeView.Bookmark/46.3.0\",\"Nodes\":{\"Path\":\"not-folder\"}}")]
    public async Task InvalidKnownDataDoesNotCloseWindowOrCreateBackup(string name, string json)
    {
        using var fixture = new Fixture(); var files = Files; files[name] = json;
        int closed = 0, reopened = 0; var state = new SaveData(fixture.Root);
        var coordinator = new ProfileImportCoordinator(state, () => { closed++; return Task.CompletedTask; }, () => { reopened++; return Task.CompletedTask; });
        var request = (await Preview(files)).CreateRequest(All);
        await Assert.ThrowsAnyAsync<Exception>(() => coordinator.ApplyAsync(request, Token));
        Assert.Equal(0, closed); Assert.Equal(0, reopened); Assert.False(Directory.Exists(Path.Combine(fixture.Root, "ImportBackups")));
    }
    [Fact]
    public async Task PreparationAndPartialCommitFailuresLeaveOriginalProfileRecoverable()
    {
        using var fixture = new Fixture(); fixture.Put("UserSetting.json", "{\"Old\":true}");
        var bytes = File.ReadAllBytes(Path.Combine(fixture.Root, "UserSetting.json"));
        var state = new SaveData(fixture.Root); var request = (await Preview(Files)).CreateRequest(All);
        Directory.CreateDirectory(Path.Combine(fixture.Root, "History.json.tmp"));
        await Assert.ThrowsAnyAsync<UnauthorizedAccessException>(() => state.ApplyProfileImportAsync(request, Token));
        Assert.Equal(bytes, File.ReadAllBytes(Path.Combine(fixture.Root, "UserSetting.json")));
        Directory.Delete(Path.Combine(fixture.Root, "History.json.tmp")); Directory.CreateDirectory(Path.Combine(fixture.Root, "History.json"));
        await Assert.ThrowsAnyAsync<IOException>(() => state.ApplyProfileImportAsync(request, Token));
        Assert.Equal(bytes, File.ReadAllBytes(Path.Combine(fixture.Root, "UserSetting.json")));
        Assert.False(File.Exists(Path.Combine(fixture.Root, ".save-pending.json")));
        Assert.False(File.Exists(Path.Combine(fixture.Root, "UserSetting.json.save-backup")));
    }
    [Fact]
    public async Task FailedWindowRebuildRollsBackAndReopensOriginalConfig()
    {
        using var fixture = new Fixture(); fixture.Put("UserSetting.json", "{\"Config\":{\"View\":{\"IsKeepAngle\":true}}}");
        var bytes = File.ReadAllBytes(Path.Combine(fixture.Root, "UserSetting.json")); int closed = 0, opened = 0;
        var coordinator = new ProfileImportCoordinator(new(fixture.Root), () => { closed++; return Task.CompletedTask; }, async () =>
        { if (++opened == 1) throw new InvalidOperationException("synthetic bind failure"); await new SaveData(fixture.Root).LoadAsync(Token); });
        var request = (await Preview(Files)).CreateRequest(All);
        await Assert.ThrowsAsync<InvalidOperationException>(() => coordinator.ApplyAsync(request, Token));
        Assert.Equal(1, closed); Assert.Equal(2, opened); Assert.True(Config.Current.View.IsKeepAngle);
        Assert.Equal(bytes, File.ReadAllBytes(Path.Combine(fixture.Root, "UserSetting.json")));
    }
    [Fact]
    public async Task UnreleasedFailedWindowBlocksRollbackAndPreservesBackup()
    {
        using var fixture = new Fixture(); fixture.Put("UserSetting.json", "{\"Original\":true}"); int opened = 0;
        var coordinator = new ProfileImportCoordinator(new(fixture.Root), () => Task.CompletedTask, () =>
        { opened++; throw new ProfileImportRecoveryBlockedException("synthetic live window", new IOException("release failure")); });
        var request = (await Preview(Files)).CreateRequest(All);
        var error = await Assert.ThrowsAsync<ProfileImportRecoveryBlockedException>(() => coordinator.ApplyAsync(request, Token));
        var backup = Assert.Single(Directory.GetDirectories(Path.Combine(fixture.Root, "ImportBackups")));
        Assert.Contains(backup, error.Message); Assert.Equal(1, opened);
        Assert.Equal(42, fixture.Get("UserSetting.json")["Config"]!["View"]!["Future"]!.GetValue<int>());
        Assert.True(JsonNode.Parse(File.ReadAllText(Path.Combine(backup, "UserSetting.json")))!["Original"]!.GetValue<bool>());
        Assert.True(File.Exists(Path.Combine(backup, "manifest.json")));
    }
    [Fact]
    public async Task FailedRollbackReportsBothErrorsAndRetainsRecoveryMaterial()
    {
        using var fixture = new Fixture(); fixture.Put("UserSetting.json", "{\"Original\":true}"); int opened = 0;
        var coordinator = new ProfileImportCoordinator(new(fixture.Root), () => Task.CompletedTask, () =>
        {
            opened++;
            var backup = Directory.GetDirectories(Path.Combine(fixture.Root, "ImportBackups")).Single();
            File.AppendAllText(Path.Combine(backup, "UserSetting.json"), "corrupt");
            throw new InvalidOperationException("synthetic rebuild failure");
        });
        var request = (await Preview(Files)).CreateRequest(All);
        var error = await Assert.ThrowsAsync<AggregateException>(() => coordinator.ApplyAsync(request, Token));
        Assert.Collection(error.InnerExceptions, e => Assert.IsType<InvalidOperationException>(e), e => Assert.IsType<InvalidDataException>(e));
        Assert.Equal(1, opened); Assert.Equal(42, fixture.Get("UserSetting.json")["Config"]!["View"]!["Future"]!.GetValue<int>());
        Assert.True(File.Exists(Path.Combine(Directory.GetDirectories(Path.Combine(fixture.Root, "ImportBackups")).Single(), "manifest.json")));
    }
    [Fact]
    public async Task CloseFailureAndPreCloseCancellationNeverApply()
    {
        using var fixture = new Fixture(); var request = (await Preview(Files)).CreateRequest(All);
        var coordinator = new ProfileImportCoordinator(new(fixture.Root), () => throw new IOException("save failure"), () => throw new Exception("must not reopen"));
        await Assert.ThrowsAsync<IOException>(() => coordinator.ApplyAsync(request, Token));
        using var canceled = new CancellationTokenSource(); canceled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => coordinator.ApplyAsync(request, canceled.Token));
        Assert.Empty(Directory.GetFileSystemEntries(fixture.Root));
    }
    [Fact]
    public async Task CancellationAfterCloseReopensOriginalWindow()
    {
        using var fixture = new Fixture(); using var canceled = new CancellationTokenSource(); int opened = 0;
        var coordinator = new ProfileImportCoordinator(new(fixture.Root), () => { canceled.Cancel(); return Task.CompletedTask; }, () => { opened++; return Task.CompletedTask; });
        var request = (await Preview(Files)).CreateRequest(All);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => coordinator.ApplyAsync(request, canceled.Token));
        Assert.Equal(1, opened); Assert.Empty(Directory.GetFileSystemEntries(fixture.Root));
    }
    [Fact]
    public async Task SourceSnapshotDoesNotChangeWhenPreviewCopyOrSourceChanges()
    {
        using var fixture = new Fixture(); var files = Files; var preview = await Preview(files); var request = preview.CreateRequest(All);
        preview.GetDocument("History.json")!["Items"] = new JsonArray(); files["History.json"] = "{}";
        var state = new SaveData(fixture.Root); await state.ApplyProfileImportAsync(request, Token);
        Assert.Single(fixture.Get("History.json")["Items"]!.AsArray());
        await state.ApplyProfileImportAsync(request, Token); Assert.Single(fixture.Get("History.json")["Items"]!.AsArray());
    }
    [Fact]
    public async Task OriginalStringEnumsAndWindowPlacementLoadWithoutChangingGlobalConfigInPreview()
    {
        var files = Files;
        files["UserSetting.json"] = """{"Format":"NeeView/46.3.0","Config":{"BookSetting":{"PageMode":"WidePage","BookReadOrder":"LeftToRight"},"View":{"StretchMode":"Uniform"},"Window":{"Panels":{"Info":{"WindowPlacement":"Normal,20,30,500,400"}}},"Command":{"PresetInputScheme":"TypeA"}}}""";
        using var fixture = new Fixture(); var state = new SaveData(fixture.Root); var before = Config.Current;
        var request = (await Preview(files)).CreateRequest(All); await state.ValidateProfileImportAsync(request, Token); Assert.Same(before, Config.Current);
        await state.ApplyProfileImportAsync(request, Token); await new SaveData(fixture.Root).LoadAsync(Token);
        Assert.Equal(PageMode.WidePage, Config.Current.BookSetting.PageMode);
        Assert.Equal(PageReadOrder.LeftToRight, Config.Current.BookSetting.BookReadOrder);
    }
    [Fact]
    public async Task LegacyHistoryAndBookmarkBooksMergeAndIndependentFilesWinOverFallbacks()
    {
        var files = new Dictionary<string, string>
        {
            ["History.json"] = """{"Format":"NeeView.History/45.0.4000","Items":[{"Path":"/a","Future":8}],"Books":[{"Path":"/a","Page":"内页\\003.png","PageMode":"SinglePage","BookReadOrder":"LeftToRight","BaseScale":1.5,"Future":42}],"Folders":{"/legacy":{"FolderOrder":"Random","Seed":7}}}""",
            ["Bookmark.json"] = """{"Format":"NeeView.Bookmark/45.0.4000","Nodes":{"Children":[{"Name":"F","Children":[{"Path":"/a","EntryTime":"2025-01-02T00:00:00"}]},{"Path":"/z","EntryTime":"2025-02-02T00:00:00"}]},"Books":[{"Path":"/a","Page":"001.png","PageMode":"WidePage","Future":99}],"QuickAccess":{"Items":[{"Path":"/legacy"}]}}"""
        };
        using var fixture = new Fixture(); var state = new SaveData(fixture.Root);
        var request = (await Preview(files)).CreateRequest(new(false, true, true, true, true)); await state.ApplyProfileImportAsync(request, Token);
        var history = fixture.Get("History.json"); Assert.Equal("内页\\003.png", history["Items"]![0]!["Page"]!.GetValue<string>());
        Assert.Contains("Base=1.5", history["Items"]![0]!["Props"]!.GetValue<string>()); Assert.Null(history["Books"]);
        Assert.Equal(42, history["MacImportedLegacyBooks"]![0]!["Future"]!.GetValue<int>());
        Assert.Equal("001.png", fixture.Get("Bookmark.json")["Nodes"]!["Children"]![0]!["Children"]![0]!["Page"]!.GetValue<string>());
        Assert.Equal("/legacy", fixture.Get("Foldres.json")["Folders"]![0]!["Place"]!.GetValue<string>());
        Assert.Equal("/legacy", fixture.Get("QuicAccess.json")["Items"]![0]!["Path"]!.GetValue<string>());
        files["Foldres.json"] = """{"Format":"NeeView.Folders/46.3.0","Folders":[{"Place":"/independent"}]}""";
        files["QuicAccess.json"] = """{"Format":"NeeView.QuickAccess/46.3.0","Items":[{"Path":"/independent"}]}""";
        await state.ApplyProfileImportAsync((await Preview(files)).CreateRequest(new(false, true, true, true, true)), Token);
        Assert.Equal("/independent", fixture.Get("Foldres.json")["Folders"]![0]!["Place"]!.GetValue<string>());
        Assert.Equal("/independent", fixture.Get("QuicAccess.json")["Items"]![0]!["Path"]!.GetValue<string>());
    }
    [Fact]
    public async Task OldUncNormalizationPreservesTailCaseAndOriginalDuplicateMetadata()
    {
        var files = new Dictionary<string, string> { ["History.json"] = """{"Format":"NeeView.History/44.0.0","Items":[{"Path":"\\\\NAS\\Share\\Case.cbz","Future":1},{"Path":"\\\\nas\\share\\Case.cbz","Future":2},{"Path":"\\\\nas\\share\\case.cbz","Future":3}]}""" };
        using var fixture = new Fixture(); await new SaveData(fixture.Root).ApplyProfileImportAsync((await Preview(files)).CreateRequest(new(false, false, false, true, false)), Token);
        var raw = fixture.Get("History.json"); Assert.Collection(raw["Items"]!.AsArray(), _ => { }, _ => { });
        Assert.Equal(@"\\NAS\Share\case.cbz", raw["Items"]![1]!["Path"]!.GetValue<string>());
        Assert.Equal(2, raw["MacImportedLegacyItems"]![1]!["Future"]!.GetValue<int>());
    }
    [Fact]
    public async Task PendingImportCommitRecoversOriginalFilesOnNextLoad()
    {
        using var fixture = new Fixture(); fixture.Put("UserSetting.json", "{\"Imported\":true}"); fixture.Put("UserSetting.json.save-backup", "{\"Original\":true}");
        fixture.Put("History.json", Files["History.json"]); fixture.Put(".save-pending.json", "{\"UserSetting.json\":true,\"History.json\":false}");
        await new SaveData(fixture.Root).LoadAsync(Token); Assert.True(fixture.Get("UserSetting.json")["Original"]!.GetValue<bool>());
        Assert.False(File.Exists(Path.Combine(fixture.Root, "History.json"))); Assert.False(File.Exists(Path.Combine(fixture.Root, ".save-pending.json")));
    }
    [Fact]
    public async Task InterruptedRollbackFailureKeepsMarkerAndBackupsUntilRetry()
    {
        using var fixture = new Fixture();
        fixture.Put("UserSetting.json", "{\"Imported\":true}"); fixture.Put("History.json", "{\"Imported\":true}");
        fixture.Put("UserSetting.json.save-backup", "{\"Original\":true}");
        fixture.Put(".save-pending.json", "{\"UserSetting.json\":true,\"History.json\":true}");
        await Assert.ThrowsAsync<FileNotFoundException>(() => new SaveData(fixture.Root).LoadAsync(Token));
        Assert.True(File.Exists(Path.Combine(fixture.Root, ".save-pending.json")));
        Assert.Equal("{\"Original\":true}", File.ReadAllText(Path.Combine(fixture.Root, "UserSetting.json.save-backup")));
        fixture.Put("History.json.save-backup", "{\"Items\":[],\"Original\":true}");
        await new SaveData(fixture.Root).LoadAsync(Token);
        Assert.True(fixture.Get("UserSetting.json")["Original"]!.GetValue<bool>());
        Assert.True(fixture.Get("History.json")["Original"]!.GetValue<bool>());
        Assert.False(File.Exists(Path.Combine(fixture.Root, ".save-pending.json")));
    }
    [AvaloniaFact]
    public async Task SelectionBindingsAndCanceledConfirmationNeverWriteProfile()
    {
        using var fixture = new Fixture();
        using var stream = typeof(CommandTable).Assembly.GetManifestResourceStream("NeeView.Command.command-manifest.json")!;
        var definitions = JsonSerializer.Deserialize<List<CommandDefinition>>(stream)!;
        using var model = new ProfileImportViewModel(new(new Reader(Files), definitions, new HashSet<string>()));
        await model.SelectSourceAsync(new("synthetic", ProfileImportSourceKind.Directory));
        var owner = new Window(); owner.Show(); var window = new ProfileImportWindow(model);
        var dialogResult = window.ShowDialog<ProfileImportRequest?>(owner);
        try
        {
            Dispatcher.UIThread.RunJobs(); window.UpdateLayout();
            var apply = window.FindControl<Button>("ApplyImportButton")!;
            Assert.True(apply.IsEnabled);
            var boxes = window.GetVisualDescendants().OfType<CheckBox>().ToDictionary(c => c.Content!.ToString()!);
            Assert.True(boxes["设置 / 命令"].IsChecked); Assert.False(boxes["历史 / 阅读进度"].IsChecked);
            boxes["设置 / 命令"].IsChecked = false; boxes["目录参数"].IsChecked = false; boxes["快速访问"].IsChecked = false;
            Assert.False(model.ImportSettings); Assert.False(apply.IsEnabled);
            boxes["历史 / 阅读进度"].IsChecked = true; Assert.True(model.ImportHistory); Assert.True(apply.IsEnabled);
            apply.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
            for (int i = 0; i < 200 && !window.OwnedWindows.Any(); i++)
            { Dispatcher.UIThread.RunJobs(); await Task.Delay(10, Token); }
            var confirmation = Assert.Single(window.OwnedWindows);
            var cancel = confirmation.GetVisualDescendants().OfType<Button>().Single(b => b.Content?.ToString() == "取消");
            cancel.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
            Dispatcher.UIThread.RunJobs(); Assert.True(window.IsVisible); Assert.False(dialogResult.IsCompleted);
            Assert.Empty(Directory.GetFileSystemEntries(fixture.Root)); Assert.NotNull(model.Preview);
            window.Close(); Assert.Null(await dialogResult);
        }
        finally { window.Close(); owner.Close(); }
    }
    [AvaloniaFact]
    public async Task FormalWindowRebuildUsesNewReferencesAndOriginalImportCommand()
    {
        using var fixture = new Fixture(); var state = new SaveData(fixture.Root); await state.LoadAsync(Token);
        var decoder = new MagickImageDecoder(); var images = new BitmapFactory(decoder); var operation = new BookOperation(new ArchiveFactory(), decoder, state);
        var window = new MainWindow(); window.Bind(new(operation, new(operation), state), images, new Platform()); window.Show();
        MainWindow? rebuilt = null; SaveData? reloaded = null; BookOperation? rebuiltOperation = null;
        var coordinator = new ProfileImportCoordinator(state, async () => { Assert.True(Dispatcher.UIThread.CheckAccess()); await window.PrepareShutdownAsync(); window.Close(); }, async () =>
        {
            Assert.True(Dispatcher.UIThread.CheckAccess());
            reloaded = new(fixture.Root); await reloaded.LoadAsync(Token);
            var newImages = new BitmapFactory(decoder); var next = new BookOperation(new ArchiveFactory(), decoder, reloaded); rebuiltOperation = next;
            rebuilt = new(); rebuilt.Bind(new(next, new(next), reloaded), newImages, new Platform()); rebuilt.AttachProfileImport(new ProfileImportReader(), _ => Task.CompletedTask); rebuilt.Show();
        });
        try
        {
            var oldConfig = Config.Current; await coordinator.ApplyAsync((await Preview(Files)).CreateRequest(All), Token);
            Assert.False(window.IsVisible); Assert.NotNull(rebuilt); Assert.NotSame(oldConfig, Config.Current);
            static IEnumerable<MenuItem> Walk(ItemsControl root) => root.Items.OfType<MenuItem>().SelectMany(m => new[] { m }.Concat(Walk(m)));
            Assert.True(Walk(rebuilt.FindControl<Menu>("MenuBar")!).Single(m => m.Tag as string == "ImportBackup").IsEnabled);
            var implementation = typeof(MainWindow).GetMethod("IsCommandImplemented", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;
            var items = new CommandTable(rebuiltOperation!).Definitions.Select(d => new { d.Name, d.Source, implemented = (bool)implementation.Invoke(rebuilt, [d.Name])! }).ToArray();
            Assert.Equal(235, items.Length); Assert.Equal(193, items.Count(i => i.implemented));
            if (System.Environment.GetEnvironmentVariable("NEEVIEW_P5_APPLY_COMMAND_ARTIFACT") is { } report)
                await File.WriteAllTextAsync(report, JsonSerializer.Serialize(new { scope = "正式窗口已装配读取器与导入回调；执行入口数不代表覆盖率", total = items.Length, implemented = items.Count(i => i.implemented), items }, new JsonSerializerOptions { WriteIndented = true }), Token);
            await window.ExecuteAsync("NextPage"); // 已关闭入口不再访问或保存旧实例。
            await rebuilt.PrepareShutdownAsync(); rebuilt.Close();
            var final = new SaveData(fixture.Root); await final.LoadAsync(Token); Assert.Equal("Ctrl+J", final.GetShortcut("NextPage", "Right"));
            Assert.Equal("内页\\003.png", final.Find("/import/book.cbz")!.Page);
        }
        finally { if (window.IsVisible) { await window.PrepareShutdownAsync(); window.Close(); } if (rebuilt?.IsVisible == true) { await rebuilt.PrepareShutdownAsync(); rebuilt.Close(); } }
    }
    private sealed class Platform : IPlatformService
    { public Task RevealAsync(string path, CancellationToken token = default) => Task.CompletedTask; public Task TrashAsync(string path, CancellationToken token = default) => Task.CompletedTask; }
}
