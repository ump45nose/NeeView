using System.IO.Compression;
using System.Text;
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

/// <summary>原真实序列化形状、只读来源、路径边界与正式预览窗口的兼容回归。</summary>
public sealed class ProfileImportTests
{
    private static IReadOnlyList<CommandDefinition> Definitions()
    {
        using var stream = typeof(CommandTable).Assembly.GetManifestResourceStream("NeeView.Command.command-manifest.json")!;
        return JsonSerializer.Deserialize<List<CommandDefinition>>(stream)!;
    }
    private static ProfileImportService Service(IProfileImportReader? reader = null) => new(reader ?? new ProfileImportReader(), Definitions(), new HashSet<string> { "NextPage" });
    private static ProfilePathMapping[] Mappings => [new(@"P:\Books", "/Volumes/Picture/Books"), new(@"P:\Books\Special", "/Volumes/Other/Special")];

    [Theory]
    [InlineData(@"p:\books\Special\a.cbz\内页", "/Volumes/Other/Special/a.cbz/内页", ProfilePathStatus.Mapped)]
    [InlineData(@"P:\Books\猫.jpg", "/Volumes/Picture/Books/猫.jpg", ProfilePathStatus.Mapped)]
    [InlineData(@"P:\Books2\a", @"P:\Books2\a", ProfilePathStatus.Unmapped)]
    [InlineData(@"P:\Books\..\a", @"P:\Books\..\a", ProfilePathStatus.Unmapped)]
    [InlineData(@"Q:\Other", @"Q:\Other", ProfilePathStatus.Unmapped)]
    [InlineData("bookmark:1", "bookmark:1", ProfilePathStatus.Unchanged)]
    [InlineData("quickaccess:/Root", "quickaccess:/Root", ProfilePathStatus.Unchanged)]
    [InlineData("/Volumes/Picture/A", "/Volumes/Picture/A", ProfilePathStatus.Unchanged)]
    [InlineData(@"inner\001.jpg", @"inner\001.jpg", ProfilePathStatus.Unchanged)]
    [InlineData("P:relative", "P:relative", ProfilePathStatus.Unchanged)]
    public void MappingUsesLongestBoundaryAndNeverReinterpretsLogicalPaths(string source, string expected, ProfilePathStatus status)
    { var result = new ProfilePathMapper(Mappings).Map(source); Assert.Equal(expected, result.Path); Assert.Equal(status, result.Status); }

    [Fact]
    public async Task TotalJsonAndZipEntryBudgetsAreEnforced()
    {
        using var fixture = new Fixture(false);
        foreach (var name in ProfileImportFiles.Names.Take(3))
        { using var stream = File.Create(Path.Combine(fixture.Root, name)); stream.SetLength(23 * 1024 * 1024); }
        await Assert.ThrowsAsync<InvalidDataException>(() => new ProfileImportReader().ReadAsync(new(fixture.Root, ProfileImportSourceKind.Directory), TestContext.Current.CancellationToken));
        using (var zip = ZipFile.Open(fixture.Backup, ZipArchiveMode.Create))
        {
            using (var writer = new StreamWriter(zip.CreateEntry("History.json").Open())) writer.Write("{}");
            for (var i = 0; i < ProfileImportFiles.MaxEntries; i++) zip.CreateEntry("Scripts/" + i);
        }
        await Assert.ThrowsAsync<InvalidDataException>(() => new ProfileImportReader().ReadAsync(new(fixture.Backup, ProfileImportSourceKind.Backup), TestContext.Current.CancellationToken));
    }
    [Fact]
    public async Task ClosingBeforeReadCompletionSuppressesLateResultAndError()
    {
        var reader = new LateReader(); var model = new ProfileImportViewModel(Service(reader));
        var task = model.SelectSourceAsync(new("old", ProfileImportSourceKind.Directory)); model.Dispose();
        reader.Pending.SetResult(new(new Dictionary<string, string> { ["History.json"] = "{broken" }, []));
        await task;
        Assert.Null(model.Preview); Assert.Null(model.Error); Assert.False(model.IsBusy); Assert.False(model.CanPreview);
    }
    [Fact]
    public async Task ClearedMappingRowsCannotInvalidateNewPreview()
    {
        using var model = new ProfileImportViewModel(Service(new LateReader()));
        var old = new ProfilePathMappingEdit { WindowsPrefix = @"P:\Books", MacPrefix = "/Volumes/Picture" };
        model.Mappings.Add(old); await model.SelectSourceAsync(new("new", ProfileImportSourceKind.Directory));
        model.Mappings.Clear(); await model.RefreshAsync(); var preview = model.Preview;
        old.WindowsPrefix = "changed"; Assert.NotNull(preview); Assert.Same(preview, model.Preview);
    }
    [Fact]
    public async Task UnprojectedSettingsAndUnsupportedCustomizationsArePreservedAndReported()
    {
        using var fixture = new Fixture(false);
        var raw = JsonNode.Parse("""{"Format":"NeeView.UserSetting/46.3.0","Config":{"Future":{"Path":"P:\\Books"},"View":{"FutureEffect":5},"Command":{"PresetInputScheme":99}},"ContextMenu":{"Future":true},"SusiePlugins":["windows-only"]}""")!;
        await File.WriteAllTextAsync(Path.Combine(fixture.Root, "UserSetting.json"), raw.ToJsonString(), TestContext.Current.CancellationToken);
        var preview = await Service().PreviewAsync(new(fixture.Root, ProfileImportSourceKind.Directory), Mappings, TestContext.Current.CancellationToken);
        Assert.True(JsonNode.DeepEquals(raw, preview.GetDocument("UserSetting.json")));
        Assert.Contains(preview.Notices, n => n.Contains("Config.Future")); Assert.Contains(preview.Notices, n => n.Contains("Config.View.FutureEffect"));
        Assert.Contains(preview.Notices, n => n.StartsWith("SusiePlugins")); Assert.Contains(preview.Notices, n => n.StartsWith("输入方案含未识别"));
    }
    [Fact]
    public void DriveRootAndUncMappingsRespectWindowsCaseAndMacCase()
    {
        var mapper = new ProfilePathMapper([new(@"P:\", "/Volumes/Picture"), new(@"\\NAS\Share", "/Volumes/Share")]);
        Assert.Equal("/Volumes/Picture/Ab/a", mapper.Map(@"p:\Ab\a").Path);
        Assert.Equal("/Volumes/Share/Book", mapper.Map(@"\\nas\share\Book").Path);
        Assert.Equal(ProfilePathStatus.Unmapped, mapper.Map(@"\\NAS\Share2\Book").Status);
        Assert.Equal(ProfilePathStatus.Unchanged, mapper.Map("P:").Status);
    }
    [Theory]
    [InlineData("relative", "/Volumes/Picture")]
    [InlineData(@"C:\Books\..", "/Volumes/Picture")]
    [InlineData(@"C:\Books", "relative")]
    [InlineData(@"C:\Books", "/Volumes/../Picture")]
    public void InvalidMappingsFailBeforeReading(string windows, string mac) =>
        Assert.Throws<ArgumentException>(() => new ProfilePathMapper([new(windows, mac)]));
    [Fact]
    public void DuplicateCanonicalPrefixesAreRejected() => Assert.Throws<ArgumentException>(() =>
        new ProfilePathMapper([new(@"P:\Books\", "/Volumes/a"), new("p:/books", "/Volumes/b")]));

    [Fact]
    public async Task ProfileAndOriginalNvzipProduceEquivalentReadOnlyCandidates()
    {
        using var fixture = new Fixture(); var service = Service();
        var original = fixture.Files.ToDictionary(p => p.Key, p => File.ReadAllBytes(Path.Combine(fixture.Root, p.Key)));
        var backupBytes = File.ReadAllBytes(fixture.Backup);
        var profile = await service.PreviewAsync(new(fixture.Root, ProfileImportSourceKind.Directory), Mappings, TestContext.Current.CancellationToken);
        var backup = await service.PreviewAsync(new(fixture.Backup, ProfileImportSourceKind.Backup), Mappings, TestContext.Current.CancellationToken);
        foreach (var name in ProfileImportFiles.Names)
        {
            Assert.True(JsonNode.DeepEquals(profile.GetDocument(name), backup.GetDocument(name)));
            Assert.Equal(original[name], File.ReadAllBytes(Path.Combine(fixture.Root, name)));
        }
        Assert.Equal(backupBytes, File.ReadAllBytes(fixture.Backup));
        Assert.Equal(5, profile.Files.Count(f => f.Present));
        Assert.Contains(backup.Notices, n => n.Contains("Scripts\\do-not-run.js"));
        var history = profile.GetDocument("History.json")!;
        Assert.Equal("/Volumes/Picture/Books/a.cbz", history["Items"]![0]!["Path"]!.GetValue<string>());
        Assert.Equal(@"内页\003.png", history["Items"]![0]!["Page"]!.GetValue<string>());
        Assert.Equal("Unknown=1", history["Items"]![0]!["Props"]!.GetValue<string>());
        Assert.Equal(@"P:\Books\secret", history["FuturePath"]!.GetValue<string>());
        Assert.Equal(42, history["Items"]![0]!["Future"]!.GetValue<int>());
        history["Items"]![0]!["Path"] = "mutated";
        Assert.Equal("/Volumes/Picture/Books/a.cbz", profile.GetDocument("History.json")!["Items"]![0]!["Path"]!.GetValue<string>());
        var tree = profile.GetDocument("Bookmark.json")!["Nodes"]!["Children"]!;
        Assert.Equal("原目录", tree[0]!["Name"]!.GetValue<string>());
        Assert.Equal("/Volumes/Other/Special/b.cbz", tree[0]!["Children"]![0]!["Path"]!.GetValue<string>());
        Assert.Equal("second", tree[1]!["Name"]!.GetValue<string>());
    }

    [Fact]
    public async Task PreviewKeepsCurrentConfigAndFilesUntouchedAndExpandsDifferenceDefaults()
    {
        using var fixture = new Fixture(); var current = Path.Combine(fixture.Root, "Current"); Directory.CreateDirectory(current);
        var currentPath = Path.Combine(current, "UserSetting.json"); await File.WriteAllTextAsync(currentPath, """{"Config":{"Command":{"PresetInputScheme":2}},"Future":true}""", TestContext.Current.CancellationToken);
        var state = new SaveData(current); await state.LoadAsync(TestContext.Current.CancellationToken); var config = Config.Current;
        var before = File.ReadAllBytes(currentPath);
        var preview = await Service().PreviewAsync(new(fixture.Root, ProfileImportSourceKind.Directory), Mappings, TestContext.Current.CancellationToken);
        Assert.Same(config, Config.Current); Assert.Equal(before, File.ReadAllBytes(currentPath)); Assert.Empty(state.HistoryEntries);
        var custom = Assert.Single(preview.Commands, c => c.Name == "NextPage");
        Assert.True(custom.Explicit); Assert.Equal("Ctrl+J", custom.Shortcut);
        var missing = Assert.Single(preview.Commands, c => c.Name == "PrevPage");
        Assert.False(missing.Explicit); Assert.Equal("Right,WheelUp", missing.Shortcut);
        Assert.Contains(preview.Commands, c => c.Name == "ImportBackup" && c.Capability.Contains("未迁移"));
        Assert.Contains(preview.Commands, c => c.Name == "Script:Untrusted" && c.Capability.Contains("不执行"));
        Assert.Contains(preview.Notices, n => n.Contains("未知 UserSetting 字段") && n.EndsWith("Future"));
        Assert.Equal(1, preview.UnmappedCount);
    }

    [Fact]
    public async Task OriginalLegacyJsonPropertyNamesAreMappedAndReportedWithoutApplyingMigrations()
    {
        using var fixture = new Fixture(false);
        await File.WriteAllTextAsync(Path.Combine(fixture.Root, "History.json"), """{"Format":"NeeView.History/44.0.0","Folders":{"P:\\Books":{"Future":7}},"Books":[{"Path":"P:\\Books\\a.cbz","Page":"001.png"}]}""", TestContext.Current.CancellationToken);
        await File.WriteAllTextAsync(Path.Combine(fixture.Root, "Bookmark.json"), """{"Format":"NeeView.Bookmark/45.0.0","QuickAccess":{"Items":[{"Path":"P:\\Books"}]}}""", TestContext.Current.CancellationToken);
        var preview = await Service().PreviewAsync(new(fixture.Root, ProfileImportSourceKind.Directory), Mappings, TestContext.Current.CancellationToken);
        Assert.Equal(7, preview.GetDocument("History.json")!["Folders"]!["/Volumes/Picture/Books"]!["Future"]!.GetValue<int>());
        Assert.Contains(preview.Notices, n => n.Contains("FoldersLegacy")); Assert.Contains(preview.Notices, n => n.Contains("QuickAccessLegacy"));
        Assert.Contains(preview.Files, f => f.Name == "Foldres.json" && !f.Present);
        Assert.Equal("/Volumes/Picture/Books", preview.GetDocument("Bookmark.json")!["QuickAccess"]!["Items"]![0]!["Path"]!.GetValue<string>());
    }

    [Theory]
    [InlineData("NeeView/46.3.0", false)]
    [InlineData("NeeView.UserSetting/46.3.0", false)]
    [InlineData("NeeView.UserSetting/47.0.0", true)]
    [InlineData("Other/46.3.0", true)]
    public async Task FormatNamesAndFutureVersionsAreReportedHonestly(string format, bool needsReport)
    {
        using var fixture = new Fixture(false); await File.WriteAllTextAsync(Path.Combine(fixture.Root, "UserSetting.json"), new JsonObject { ["Format"] = format }.ToJsonString(), TestContext.Current.CancellationToken);
        var preview = await Service().PreviewAsync(new(fixture.Root, ProfileImportSourceKind.Directory), [], TestContext.Current.CancellationToken);
        Assert.Equal(needsReport, preview.Notices.Any(n => n.StartsWith("UserSetting.json 版本")));
    }

    [Theory]
    [InlineData("[1]")]
    [InlineData("{broken")]
    [InlineData("{\"Items\":12}")]
    [InlineData("{\"Items\":[{\"Path\":123}]}")]
    public async Task DamagedOrWrongShapeFailsWithoutChangingSource(string text)
    {
        using var fixture = new Fixture(false); var path = Path.Combine(fixture.Root, "History.json"); await File.WriteAllTextAsync(path, text, TestContext.Current.CancellationToken);
        await Assert.ThrowsAsync<InvalidDataException>(() => Service().PreviewAsync(new(fixture.Root, ProfileImportSourceKind.Directory), [], TestContext.Current.CancellationToken));
        Assert.Equal(text, File.ReadAllText(path));
    }
    [Fact]
    public async Task DuplicateCriticalZipEntriesFailAndUnrelatedPathsNeverExtract()
    {
        using var fixture = new Fixture(false); var zipPath = Path.Combine(fixture.Root, "duplicate.nvzip");
        using (var zip = ZipFile.Open(zipPath, ZipArchiveMode.Create))
        {
            foreach (var name in new[] { "History.json", "history.JSON", "../escaped.json" })
            { using var writer = new StreamWriter(zip.CreateEntry(name).Open()); writer.Write("{}"); }
        }
        await Assert.ThrowsAsync<InvalidDataException>(() => new ProfileImportReader().ReadAsync(new(zipPath, ProfileImportSourceKind.Backup), TestContext.Current.CancellationToken));
        Assert.False(File.Exists(Path.Combine(Path.GetDirectoryName(fixture.Root)!, "escaped.json")));
    }
    [Fact]
    public async Task OversizedJsonAndPreCancelledReadAreRejected()
    {
        using var fixture = new Fixture(false); using (var stream = File.Create(Path.Combine(fixture.Root, "History.json"))) stream.SetLength(ProfileImportFiles.MaxFileBytes + 1);
        await Assert.ThrowsAsync<InvalidDataException>(() => new ProfileImportReader().ReadAsync(new(fixture.Root, ProfileImportSourceKind.Directory), TestContext.Current.CancellationToken));
        using var cancel = new CancellationTokenSource(); cancel.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new ProfileImportReader().ReadAsync(new(fixture.Root, ProfileImportSourceKind.Directory), cancel.Token));
    }
    [Fact]
    public async Task BomAndCommentsReadButInvalidUtf8Fails()
    {
        using var fixture = new Fixture(false); var path = Path.Combine(fixture.Root, "History.json");
        await File.WriteAllTextAsync(path, "/* 原配置 */ {\"Items\":[],}", new UTF8Encoding(true), TestContext.Current.CancellationToken);
        Assert.NotNull((await Service().PreviewAsync(new(fixture.Root, ProfileImportSourceKind.Directory), [], TestContext.Current.CancellationToken)).GetDocument("History.json"));
        await File.WriteAllBytesAsync(path, [0xff, 0xff], TestContext.Current.CancellationToken);
        await Assert.ThrowsAsync<DecoderFallbackException>(() => new ProfileImportReader().ReadAsync(new(fixture.Root, ProfileImportSourceKind.Directory), TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task SupersededAndClosedPreviewNeverReceivesLateResults()
    {
        var reader = new LateReader(); using var model = new ProfileImportViewModel(Service(reader));
        var old = model.SelectSourceAsync(new("old", ProfileImportSourceKind.Directory));
        var next = model.SelectSourceAsync(new("new", ProfileImportSourceKind.Directory));
        reader.Pending.SetResult(new(new Dictionary<string, string> { ["History.json"] = "{}" }, []));
        await Task.WhenAll(old, next);
        Assert.Equal("new", model.SourceLabel); Assert.Contains(model.Preview!.Files, f => f.Name == "UserSetting.json" && f.Present);
        model.Mappings.Add(new()); Assert.Null(model.Preview);
        model.Mappings[0].WindowsPrefix = @"P:\Books"; model.Mappings[0].MacPrefix = "/Volumes/Picture";
        await model.RefreshAsync(); Assert.NotNull(model.Preview);
        model.Dispose(); Assert.Null(model.Preview); Assert.False(model.CanPreview);
    }
    private sealed class LateReader : IProfileImportReader
    {
        public TaskCompletionSource<ProfileImportBundle> Pending { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task<ProfileImportBundle> ReadAsync(ProfileImportSource source, CancellationToken token) => source.Path == "old" ? Pending.Task :
            Task.FromResult(new ProfileImportBundle(new Dictionary<string, string> { ["UserSetting.json"] = "{}" }, []));
    }
    [AvaloniaFact]
    public async Task StartupAttachmentEnablesPreviewWithoutEnablingOriginalImportCommand()
    {
        using var fixture = new Fixture(false); var state = new SaveData(fixture.Root); await state.LoadAsync(TestContext.Current.CancellationToken);
        var operation = new BookOperation(new ArchiveFactory(), new MagickImageDecoder(), state); using var images = new BitmapFactory(new MagickImageDecoder());
        var commands = new CommandTable(operation); var window = new MainWindow(); window.Bind(new(operation, commands, state), images, new TestPlatform());
        try
        {
            var menu = window.FindControl<Menu>("MenuBar")!;
            static IEnumerable<MenuItem> Walk(ItemsControl root) => root.Items.OfType<MenuItem>().SelectMany(m => new[] { m }.Concat(Walk(m)));
            Assert.False(Walk(menu).Single(m => m.Tag as string == "PreviewProfileImport").IsEnabled);
            window.AttachProfileImport(new ProfileImportReader());
            var preview = Walk(menu).Single(m => m.Tag as string == "PreviewProfileImport");
            Assert.True(preview.IsEnabled); Assert.Equal("PreviewProfileImport", ToolTip.GetTip(preview));
            Assert.False(Walk(menu).Single(m => m.Tag as string == "ImportBackup").IsEnabled);
            Assert.Equal(235, commands.Definitions.Count);
        }
        finally { await window.PrepareShutdownAsync(); window.Close(); }
    }
    private sealed class TestPlatform : IPlatformService
    { public Task RevealAsync(string path, CancellationToken token = default) => Task.CompletedTask; public Task TrashAsync(string path, CancellationToken token = default) => Task.CompletedTask; }
    [AvaloniaFact]
    public async Task OfficialPreviewXamlRendersPathsCommandsAndSourceReadOnlyScope()
    {
        using var fixture = new Fixture(); using var model = new ProfileImportViewModel(Service());
        model.Mappings.Add(new() { WindowsPrefix = @"P:\Books", MacPrefix = "/Volumes/Picture/Books" });
        await model.SelectSourceAsync(new(fixture.Root, ProfileImportSourceKind.Directory));
        var window = new ProfileImportWindow(model); window.Show();
        try
        {
            Dispatcher.UIThread.RunJobs(); window.UpdateLayout();
            Assert.Contains(window.GetVisualDescendants().OfType<TextBlock>(), t => t.Text?.Contains("来源只读") == true);
            window.GetVisualDescendants().OfType<TabControl>().Single().SelectedIndex = 1;
            Dispatcher.UIThread.RunJobs(); window.UpdateLayout();
            Assert.Contains(window.FindControl<ListBox>("ImportPathList")!.Items.OfType<ProfileImportPath>(), p => p.Result == "/Volumes/Picture/Books/a.cbz");
            using var frame = window.CaptureRenderedFrame(); Assert.NotNull(frame);
            if (System.Environment.GetEnvironmentVariable("NEEVIEW_P5_PREVIEW_ARTIFACT") is { } image)
                frame.Save(image, Avalonia.Media.Imaging.PngBitmapEncoderOptions.Default);
            window.GetVisualDescendants().OfType<TabControl>().Single().SelectedIndex = 2;
            Dispatcher.UIThread.RunJobs(); window.UpdateLayout();
            Assert.Equal(236, window.FindControl<ListBox>("ImportCommandList")!.Items.Count);
            Assert.True(window.FindControl<Button>("ApplyImportButton")!.IsEnabled);
        }
        finally { window.Close(); }
    }
    private sealed class Fixture : IDisposable
    {
        public string Root { get; } = Path.Combine(Path.GetTempPath(), "NeeView-P5-" + Guid.NewGuid().ToString("N"));
        public string Backup => Path.Combine(Root, "profile.nvzip");
        public Dictionary<string, string> Files { get; } = new()
        {
            ["UserSetting.json"] = """{"Format":"NeeView.UserSetting/46.3.0","Config":{"Command":{"PresetInputScheme":1},"System":{"DestinationFolderCollection":[{"Name":"分类","Path":"P:\\Books"}]},"StartUp":{"LastBookV2":{"Path":"P:\\Books\\a.cbz","Page":"内页\\003.png"}}},"Commands":{"NextPage":{"ShortCutKey":"Ctrl+J","Parameter":{"Future":true}},"Script:Untrusted":{"ShortCutKey":"Ctrl+9","Script":"do-not-run"}},"Future":42}""",
            ["History.json"] = """{"Format":"NeeView.History/46.3.0","Items":[{"Path":"P:\\Books\\a.cbz","Page":"内页\\003.png","Props":"Unknown=1","Future":42},{"Path":"Q:\\Unmapped"}],"FuturePath":"P:\\Books\\secret"}""",
            ["Bookmark.json"] = """{"Format":"NeeView.Bookmark/46.3.0","Nodes":{"Children":[{"Name":"原目录","Children":[{"Path":"P:\\Books\\Special\\b.cbz","Page":"001.jpg","Color":"Red"}]},{"Name":"second","Path":"bookmark:1"}]}}""",
            ["Foldres.json"] = """{"Format":"NeeView.Folders/46.3.0","Folders":[{"Place":"P:\\Books","Parameter":{"Seed":7},"Thumbs":{"Future":true}}]}""",
            ["QuicAccess.json"] = """{"Format":"NeeView.QuickAccess/46.3.0","Items":[{"Name":"常用","Children":[{"Path":"P:\\Books"}]}]}"""
        };
        public Fixture(bool populated = true)
        {
            Directory.CreateDirectory(Root); if (!populated) return;
            foreach (var file in Files) File.WriteAllText(Path.Combine(Root, file.Key), file.Value);
            using var zip = ZipFile.Open(Backup, ZipArchiveMode.Create);
            foreach (var file in Files) { using var writer = new StreamWriter(zip.CreateEntry(file.Key).Open()); writer.Write(file.Value); }
            using var script = new StreamWriter(zip.CreateEntry(@"Scripts\do-not-run.js").Open()); script.Write("throw new Error('must not run');");
        }
        public void Dispose() => Directory.Delete(Root, true);
    }
}
