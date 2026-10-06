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

/// <summary>附属 Profile 的原格式、选择及失败恢复；只操作隔离合成材料，不执行脚本。</summary>
public sealed class ProfileImportAssetTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;
    private const string Setting = "{\"Format\":\"NeeView.UserSetting/46.3.0\",\"Config\":{}}";
    private sealed class Reader(ProfileImportBundle bundle) : IProfileImportReader
    { public Task<ProfileImportBundle> ReadAsync(ProfileImportSource source, CancellationToken token) => Task.FromResult(bundle); }
    private sealed class Fixture : IDisposable
    {
        public string Root { get; } = Path.Combine(Path.GetTempPath(), "NeeView-P5-Assets-" + Guid.NewGuid().ToString("N"));
        public Fixture() => Directory.CreateDirectory(Root);
        public string Put(string name, byte[] data)
        { var path = Path.Combine(Root, name); Directory.CreateDirectory(Path.GetDirectoryName(path)!); File.WriteAllBytes(path, data); return path; }
        public string Put(string name, string text) => Put(name, Encoding.UTF8.GetBytes(text));
        public byte[] Get(string name) => File.ReadAllBytes(Path.Combine(Root, name));
        public void Dispose() => Directory.Delete(Root, true);
    }
    private static IReadOnlyList<CommandDefinition> Definitions()
    { using var stream = typeof(CommandTable).Assembly.GetManifestResourceStream("NeeView.Command.command-manifest.json")!; return JsonSerializer.Deserialize<List<CommandDefinition>>(stream)!; }
    private static ProfileImportBundle Bundle(params (string Path, string Text)[] files) => new(new Dictionary<string, string> { ["UserSetting.json"] = Setting }, [], files.ToDictionary(p => p.Path, p => Encoding.UTF8.GetBytes(p.Text)));
    private static Task<ProfileImportPreview> PreviewAsync(ProfileImportBundle bundle, params ProfilePathMapping[] mappings) =>
        new ProfileImportService(new Reader(bundle), Definitions(), new HashSet<string>()).PreviewAsync(new("synthetic", ProfileImportSourceKind.Directory), mappings, Token);
    private static ProfileImportSelection Assets(bool playlists = false, bool themes = false, bool scripts = false) => new(false, false, false, false, false, playlists, themes, scripts);

    /// <summary>原一级导出范围及两种ZIP分隔符均产生同一字节快照，来源未改写。</summary>
    [Theory]
    [InlineData('/')]
    [InlineData('\\')]
    public async Task DirectoryAndZipKeepOriginalBytesAndSkipUnsupportedNestedMaterials(char separator)
    {
        using var f = new Fixture(); var bundle = Bundle(("Playlists/one.nvpls", "{\"Format\":\"NeeView.Playlist/2.0.1\",\"Items\":[]}"), ("Themes/主题.json", "theme material"), ("Scripts/OnStartup.nvjs", "throw 'must not run';"));
        f.Put("UserSetting.json", Setting);
        foreach (var item in bundle.Assets!) f.Put(item.Key, item.Value);
        f.Put("Themes/nested/ignored.json", "not exported"); f.Put("Scripts/ignored.js", "not exported");
        var zipPath = Path.Combine(f.Root, "import.nvzip");
        using (var zip = ZipFile.Open(zipPath, ZipArchiveMode.Create))
        {
            using (var writer = new StreamWriter(zip.CreateEntry("UserSetting.json").Open())) writer.Write(Setting);
            foreach (var item in bundle.Assets!) { using var stream = zip.CreateEntry(item.Key.Replace('/', separator)).Open(); stream.Write(item.Value); }
            zip.CreateEntry("Themes/nested/ignored.json"); zip.CreateEntry("Scripts/ignored.js");
        }
        var source = File.ReadAllBytes(zipPath); var reader = new ProfileImportReader();
        var dir = await reader.ReadAsync(new(f.Root, ProfileImportSourceKind.Directory), Token);
        var archive = await reader.ReadAsync(new(zipPath, ProfileImportSourceKind.Backup), Token);
        Assert.Equal(3, dir.Assets!.Count); Assert.Equal(3, archive.Assets!.Count);
        foreach (var item in bundle.Assets!) { Assert.Equal(item.Value, dir.Assets[item.Key]); Assert.Equal(item.Value, archive.Assets[item.Key]); Assert.Equal(item.Value, f.Get(item.Key)); }
        Assert.Equal(source, File.ReadAllBytes(zipPath)); Assert.Contains("Themes/nested", dir.ExtraEntries);
        Assert.Contains("Scripts/ignored.js", dir.ExtraEntries);
    }

    /// <summary>只映射原列表物理Path；内部相对路径、原版本、顺序和未知字段保持。</summary>
    [Theory]
    [InlineData("NeeViewPlaylist.1", true)]
    [InlineData("NeeView.Playlist/2.0.0", false)]
    [InlineData("NeeView.Playlist/2.0.1", false)]
    [InlineData("NeeView.Playlist/45.0.3981", false)]
    public async Task PlaylistMappingUsesExistingFormatParserAndPreservesUnknownData(string format, bool v1)
    {
        var items = v1 ? new JsonArray(@"P:\Books\a.cbz\内页", @"inner\001.png") : new JsonArray(new JsonObject { ["Path"] = @"P:\Books\a.cbz\内页", ["Name"] = "原别名", ["Future"] = 9 }, new JsonObject { ["Path"] = @"inner\001.png" });
        var text = new JsonObject { ["Format"] = format, ["Items"] = items, ["FuturePath"] = @"P:\Books\unknown" }.ToJsonString();
        var preview = await PreviewAsync(Bundle(("Playlists/one.nvpls", text)), new ProfilePathMapping(@"P:\Books", "/Volumes/Books"));
        Assert.Null(Assert.Single(preview.Assets).Error);
        var raw = JsonNode.Parse(preview.GetAsset("Playlists/one.nvpls")!)!;
        Assert.Equal(format, raw["Format"]!.GetValue<string>()); Assert.Equal(@"P:\Books\unknown", raw["FuturePath"]!.GetValue<string>());
        Assert.Equal("/Volumes/Books/a.cbz/内页", (v1 ? raw["Items"]![0] : raw["Items"]![0]!["Path"])!.GetValue<string>());
        Assert.Equal(@"inner\001.png", (v1 ? raw["Items"]![1] : raw["Items"]![1]!["Path"])!.GetValue<string>());
        if (!v1) { Assert.Equal(9, raw["Items"]![0]!["Future"]!.GetValue<int>()); Assert.Equal("原别名", raw["Items"]![0]!["Name"]!.GetValue<string>()); }
        Assert.Equal(2, preview.Paths.Count); Assert.Single(preview.Paths, p => p.Status == ProfilePathStatus.Mapped);
    }

    [Fact]
    public async Task DefaultSkipsAllAssetsAndThemeOnlyKeepsUnselectedBytes()
    {
        var preview = await PreviewAsync(Bundle(("Themes/x.json", "imported"), ("Scripts/x.nvjs", "must not run")));
        var defaults = new ProfileImportSelection(); Assert.False(defaults.Playlists); Assert.False(defaults.Themes); Assert.False(defaults.Scripts);
        Assert.Empty(preview.CreateRequest(defaults).AssetNames);
        using var f = new Fixture(); f.Put("UserSetting.json", Setting); f.Put("Scripts/x.nvjs", "untouched");
        var setting = f.Get("UserSetting.json"); var state = new SaveData(f.Root);
        var result = await state.ApplyProfileImportAsync(preview.CreateRequest(Assets(themes: true)), Token);
        Assert.Equal(new[] { "Themes/x.json" }, result.AppliedFiles);
        Assert.Equal("untouched", File.ReadAllText(Path.Combine(f.Root, "Scripts/x.nvjs"))); Assert.Equal(setting, f.Get("UserSetting.json"));
        Assert.Contains(preview.Assets, a => a.Capability.Contains("导入后按配置选择加载")); Assert.Contains(preview.Assets, a => a.Capability.Contains("不执行事件脚本"));
        Assert.Throws<InvalidDataException>(() => preview.CreateRequest(Assets()));
    }

    [Theory]
    [InlineData("{broken")]
    [InlineData("{}")]
    [InlineData("{\"Format\":\"NeeView.Playlist/3.0.0\",\"Items\":[]}")]
    [InlineData("{\"Format\":\"NeeView.Playlist/2.0.1\",\"Items\":[{}]}")]
    public async Task InvalidPlaylistDoesNotBlockSelectingOnlyScripts(string text)
    {
        var preview = await PreviewAsync(Bundle(("Playlists/bad.nvpls", text), ("Scripts/OnStartup.nvjs", "throw 'must not run'")));
        Assert.NotNull(Assert.Single(preview.Assets, a => a.Path == "Playlists/bad.nvpls").Error);
        Assert.Throws<InvalidDataException>(() => preview.CreateRequest(Assets(playlists: true)));
        var request = preview.CreateRequest(Assets(scripts: true)); using var f = new Fixture();
        await new SaveData(f.Root).ApplyProfileImportAsync(request, Token); Assert.False(Directory.Exists(Path.Combine(f.Root, "Playlists")));
    }

    [Fact]
    public async Task PreviewAndConfirmedRequestOwnIndependentBytes()
    {
        var bundle = Bundle(("Scripts/OnStartup.nvjs", "original")); var preview = await PreviewAsync(bundle);
        bundle.Assets!["Scripts/OnStartup.nvjs"][0] = 0; var request = preview.CreateRequest(Assets(scripts: true));
        preview.GetAsset("Scripts/OnStartup.nvjs")![0] = 0;
        using var f = new Fixture(); await new SaveData(f.Root).ApplyProfileImportAsync(request, Token);
        Assert.Equal("original", File.ReadAllText(Path.Combine(f.Root, "Scripts/OnStartup.nvjs")));
    }

    [Fact]
    public async Task SerializedPlaylistFolderAndAbsoluteSelectionAreMappedButShortNameIsPreserved()
    {
        var bundle = new ProfileImportBundle(new Dictionary<string, string> { ["UserSetting.json"] = """{"Format":"NeeView.UserSetting/46.3.0","Config":{"Playlist":{"PlaylistFolder":"P:\\Lists","CurrentPlaylist":"P:\\Lists\\one.nvpls"}}}""" }, []);
        var preview = await PreviewAsync(bundle, new ProfilePathMapping(@"P:\Lists", "/Library/Lists"));
        var config = preview.GetDocument("UserSetting.json")!["Config"]!["Playlist"]!;
        Assert.Equal("/Library/Lists", config["PlaylistFolder"]!.GetValue<string>());
        Assert.Equal("/Library/Lists/one.nvpls", config["CurrentPlaylist"]!.GetValue<string>());
        bundle = new ProfileImportBundle(new Dictionary<string, string> { ["UserSetting.json"] = """{"Format":"NeeView.UserSetting/46.3.0","Config":{"Playlist":{"CurrentPlaylist":"one.nvpls"}}}""" }, []);
        Assert.Equal("one.nvpls", (await PreviewAsync(bundle)).GetDocument("UserSetting.json")!["Config"]!["Playlist"]!["CurrentPlaylist"]!.GetValue<string>());
    }

    [Fact]
    public async Task ImportedListsJoinExistingHubAndRestoreCustomDirectoryAndExactBytes()
    {
        using var f = new Fixture(); using var external = new Fixture();
        var setting = "{\"Config\":{\"Playlist\":{\"PlaylistFolder\":" + JsonSerializer.Serialize(external.Root) + ",\"CurrentPlaylist\":\"CUSTOM.NVPLS\",\"Future\":7}}}";
        f.Put("UserSetting.json", setting); f.Put("Playlists/kept.nvpls", "keep");
        var bytes = f.Get("UserSetting.json");
        var preview = await PreviewAsync(Bundle(("Playlists/custom.nvpls", "{\"Format\":\"NeeView.Playlist/2.0.1\",\"Items\":[{\"Path\":\"/example/a.cbz\"}]}")));
        var state = new SaveData(f.Root); var result = await state.ApplyProfileImportAsync(preview.CreateRequest(Assets(playlists: true)), Token);
        var reloaded = new SaveData(f.Root); await reloaded.LoadAsync(Token); await reloaded.Playlists.InitializeAsync(Token);
        Assert.Equal(Path.Combine(f.Root, "Playlists"), Config.Current.Playlist.PlaylistFolder);
        Assert.Equal("/example/a.cbz", Assert.Single(reloaded.Playlists.Current!.Items).Path);
        Assert.Equal("keep", File.ReadAllText(Path.Combine(f.Root, "Playlists/kept.nvpls"))); Assert.Empty(Directory.GetFileSystemEntries(external.Root));
        await state.RestoreProfileImportAsync(result.BackupDirectory, Token);
        Assert.Equal(bytes, f.Get("UserSetting.json")); Assert.False(File.Exists(Path.Combine(f.Root, "Playlists/custom.nvpls")));
    }

    [Fact]
    public async Task BomPlaylistRemainsByteExactAndLoadsThroughExistingHub()
    {
        var bundle = Bundle(("Playlists/Default.nvpls", "{\"Format\":\"NeeView.Playlist/2.0.1\",\"Items\":[{\"Path\":\"/example/a.cbz\"}]}"));
        var original = new byte[] { 0xEF, 0xBB, 0xBF }.Concat(bundle.Assets!["Playlists/Default.nvpls"]).ToArray();
        bundle = bundle with { Assets = new Dictionary<string, byte[]> { ["Playlists/Default.nvpls"] = original } };
        var preview = await PreviewAsync(bundle); Assert.Null(Assert.Single(preview.Assets).Error);
        Assert.Equal(original, preview.GetAsset("Playlists/Default.nvpls"));
        using var f = new Fixture(); await new SaveData(f.Root).ApplyProfileImportAsync(preview.CreateRequest(Assets(playlists: true)), Token);
        var state = new SaveData(f.Root); await state.LoadAsync(Token); await state.Playlists.InitializeAsync(Token);
        Assert.Equal(original, f.Get("Playlists/Default.nvpls")); Assert.Equal("/example/a.cbz", Assert.Single(state.Playlists.Current!.Items).Path);
    }

    [Fact]
    public async Task WindowsDefaultAndPagemarkAliasesRestoreFixedNamesAndUppercaseListsRemainVisible()
    {
        var list = "{\"Format\":\"NeeView.Playlist/2.0.1\",\"Items\":[{\"Path\":\"/example/a.cbz\"}]}";
        var preview = await PreviewAsync(Bundle(("Playlists/DEFAULT.NVPLS", list), ("Playlists/pagemark.NVPLS", list), ("Playlists/OTHER.NVPLS", list)));
        Assert.Contains(preview.Assets, a => a.Path == "Playlists/Default.nvpls"); Assert.Contains(preview.Assets, a => a.Path == "Playlists/Pagemark.nvpls");
        using var f = new Fixture(); await new SaveData(f.Root).ApplyProfileImportAsync(preview.CreateRequest(Assets(playlists: true)), Token);
        var state = new SaveData(f.Root); await state.LoadAsync(Token); await state.Playlists.InitializeAsync(Token);
        Assert.Equal("/example/a.cbz", Assert.Single(state.Playlists.Current!.Items).Path);
        Assert.Contains(Path.Combine(f.Root, "Playlists/OTHER.NVPLS"), state.Playlists.PlaylistFiles);
        Assert.Contains(Path.Combine(f.Root, "Playlists/Pagemark.nvpls"), state.Playlists.PlaylistFiles);
    }

    [Fact]
    public async Task RestoreReturnsOriginalBytesAndDeletesOnlyPreviouslyMissingSelectedFiles()
    {
        using var f = new Fixture(); f.Put("Themes/x.json", new byte[] { 0xEF, 0xBB, 0xBF, 0, 10, 255 }); f.Put("Themes/kept.json", "kept");
        var original = f.Get("Themes/x.json"); var state = new SaveData(f.Root);
        var request = (await PreviewAsync(Bundle(("Themes/x.json", "new"), ("Scripts/OnStartup.nvjs", "throw 'never run'")))).CreateRequest(Assets(themes: true, scripts: true));
        var result = await state.ApplyProfileImportAsync(request, Token);
        var manifest = JsonNode.Parse(File.ReadAllText(Path.Combine(result.BackupDirectory, "manifest.json")))!;
        Assert.Null(manifest["Scripts/OnStartup.nvjs"]); Assert.NotNull(manifest["Themes/x.json"]);
        await state.RestoreProfileImportAsync(result.BackupDirectory, Token);
        Assert.Equal(original, f.Get("Themes/x.json")); Assert.Equal("kept", File.ReadAllText(Path.Combine(f.Root, "Themes/kept.json")));
        Assert.False(File.Exists(Path.Combine(f.Root, "Scripts/OnStartup.nvjs")));
        Assert.All(ProfileImportFiles.Names, n => Assert.False(File.Exists(Path.Combine(f.Root, n))));
    }

    [Theory]
    [InlineData("hash")]
    [InlineData("path")]
    [InlineData("syntax")]
    public async Task InvalidBackupCannotWriteAnyTarget(string damage)
    {
        using var f = new Fixture(); f.Put("Themes/x.json", "old"); var state = new SaveData(f.Root);
        var result = await state.ApplyProfileImportAsync((await PreviewAsync(Bundle(("Themes/x.json", "new")))).CreateRequest(Assets(themes: true)), Token);
        var manifestPath = Path.Combine(result.BackupDirectory, "manifest.json");
        if (damage == "hash") File.AppendAllText(Path.Combine(result.BackupDirectory, "Themes/x.json"), "corrupt");
        else if (damage == "syntax") File.AppendAllText(manifestPath, "corrupt");
        else { var manifest = JsonNode.Parse(File.ReadAllText(manifestPath))!.AsObject(); manifest["../outside.nvjs"] = null; File.WriteAllText(manifestPath, manifest.ToJsonString()); }
        await Assert.ThrowsAsync<InvalidDataException>(() => state.RestoreProfileImportAsync(result.BackupDirectory, Token));
        Assert.Equal("new", File.ReadAllText(Path.Combine(f.Root, "Themes/x.json"))); Assert.False(File.Exists(Path.Combine(f.Root, ".save-pending.json")));
        Assert.True(File.Exists(manifestPath));
    }

    [Fact]
    public async Task PartialAssetCommitRollsBackEarlierOverwrite()
    {
        using var f = new Fixture(); f.Put("Themes/x.json", "old"); Directory.CreateDirectory(Path.Combine(f.Root, "Scripts/blocked.nvjs"));
        var request = (await PreviewAsync(Bundle(("Themes/x.json", "new"), ("Scripts/blocked.nvjs", "blocked")))).CreateRequest(Assets(themes: true, scripts: true));
        await Assert.ThrowsAnyAsync<IOException>(() => new SaveData(f.Root).ApplyProfileImportAsync(request, Token));
        Assert.Equal("old", File.ReadAllText(Path.Combine(f.Root, "Themes/x.json")));
        Assert.False(File.Exists(Path.Combine(f.Root, ".save-pending.json"))); Assert.False(File.Exists(Path.Combine(f.Root, "Themes/x.json.tmp")));
        Assert.True(File.Exists(Path.Combine(Assert.Single(Directory.GetDirectories(Path.Combine(f.Root, "ImportBackups"))), "manifest.json")));
    }

    [Fact]
    public async Task StartupRecoversDynamicMarkerAndKeepsFailedRollbackMaterialsForRetry()
    {
        using var f = new Fixture(); f.Put("Themes/x.json", "new"); f.Put("Scripts/OnStartup.nvjs", "never execute");
        f.Put(".save-pending.json", "{\"Themes/x.json\":true,\"Scripts/OnStartup.nvjs\":false}");
        await Assert.ThrowsAsync<FileNotFoundException>(() => new SaveData(f.Root).LoadAsync(Token));
        Assert.True(File.Exists(Path.Combine(f.Root, ".save-pending.json")));
        f.Put("Themes/x.json.save-backup", "old"); await new SaveData(f.Root).LoadAsync(Token);
        Assert.Equal("old", File.ReadAllText(Path.Combine(f.Root, "Themes/x.json")));
        Assert.False(File.Exists(Path.Combine(f.Root, "Scripts/OnStartup.nvjs"))); Assert.False(File.Exists(Path.Combine(f.Root, ".save-pending.json")));
    }

    [Fact]
    public async Task InvalidMarkerIsRejectedBeforeAnyRecoveryMutation()
    {
        using var f = new Fixture(); f.Put("UserSetting.json", "{\"Imported\":true}"); f.Put("UserSetting.json.save-backup", "{\"Old\":true}");
        f.Put(".save-pending.json", "{\"UserSetting.json\":true,\"Themes/../other.json\":false}");
        await Assert.ThrowsAsync<InvalidDataException>(() => new SaveData(f.Root).LoadAsync(Token));
        Assert.True(JsonNode.Parse(f.Get("UserSetting.json"))!["Imported"]!.GetValue<bool>());
        Assert.True(File.Exists(Path.Combine(f.Root, ".save-pending.json")));
    }

    [Fact]
    public async Task FailedRebuildRestoresBothJsonAndAssetsBeforeReopening()
    {
        using var f = new Fixture(); f.Put("UserSetting.json", "{\"Config\":{\"View\":{\"IsKeepAngle\":true}}}"); f.Put("Themes/x.json", "old");
        var json = f.Get("UserSetting.json"); var request = (await PreviewAsync(Bundle(("Themes/x.json", "new"), ("Scripts/OnStartup.nvjs", "throw 'never run'")))).CreateRequest(new(Themes: true, Scripts: true));
        int opened = 0; var coordinator = new ProfileImportCoordinator(new(f.Root), () => Task.CompletedTask, async () =>
        { if (++opened == 1) throw new InvalidOperationException("synthetic rebuild failure"); await new SaveData(f.Root).LoadAsync(Token); });
        await Assert.ThrowsAsync<InvalidOperationException>(() => coordinator.ApplyAsync(request, Token));
        Assert.Equal(2, opened); Assert.Equal(json, f.Get("UserSetting.json")); Assert.Equal("old", File.ReadAllText(Path.Combine(f.Root, "Themes/x.json")));
        Assert.False(File.Exists(Path.Combine(f.Root, "Scripts/OnStartup.nvjs"))); Assert.True(Config.Current.View.IsKeepAngle);
    }

    [Theory]
    [InlineData("Themes/../x.json")]
    [InlineData("/Themes/x.json")]
    [InlineData("C:/Themes/x.json")]
    [InlineData("Scripts/./OnStartup.nvjs")]
    public async Task UnsafeZipNamesAreRejectedWithoutExtraction(string name)
    {
        using var f = new Fixture(); var path = Path.Combine(f.Root, "import.nvzip");
        using (var zip = ZipFile.Open(path, ZipArchiveMode.Create)) zip.CreateEntry(name);
        await Assert.ThrowsAsync<InvalidDataException>(() => new ProfileImportReader().ReadAsync(new(path, ProfileImportSourceKind.Backup), Token));
        Assert.Equal(new[] { path }, Directory.GetFiles(f.Root));
    }

    [Theory]
    [InlineData("Themes/A.json", "themes/a.JSON")]
    [InlineData("Themes/café.json", "Themes/café.json")]
    public async Task ZipAliasesCannotOverwriteOneAnother(string first, string second)
    {
        using var f = new Fixture(); var path = Path.Combine(f.Root, "import.nvzip");
        using (var zip = ZipFile.Open(path, ZipArchiveMode.Create)) { zip.CreateEntry(first); zip.CreateEntry(second); }
        await Assert.ThrowsAsync<InvalidDataException>(() => new ProfileImportReader().ReadAsync(new(path, ProfileImportSourceKind.Backup), Token));
    }

    [Fact]
    public async Task SourceAndTargetLinksNeverReadOrWriteOutsideProfile()
    {
        using var f = new Fixture(); using var external = new Fixture(); external.Put("x.json", "outside");
        Directory.CreateSymbolicLink(Path.Combine(f.Root, "Themes"), external.Root);
        await Assert.ThrowsAsync<InvalidDataException>(() => new ProfileImportReader().ReadAsync(new(f.Root, ProfileImportSourceKind.Directory), Token));
        var request = (await PreviewAsync(Bundle(("Themes/x.json", "new")))).CreateRequest(Assets(themes: true));
        await Assert.ThrowsAsync<InvalidDataException>(() => new SaveData(f.Root).ValidateProfileImportAsync(request, Token));
        Assert.Equal("outside", File.ReadAllText(Path.Combine(external.Root, "x.json"))); Assert.False(Directory.Exists(Path.Combine(f.Root, "ImportBackups")));
        Directory.Delete(Path.Combine(f.Root, "Themes")); Directory.CreateDirectory(Path.Combine(f.Root, "Themes"));
        File.CreateSymbolicLink(Path.Combine(f.Root, "Themes/x.json"), Path.Combine(external.Root, "x.json"));
        await Assert.ThrowsAsync<InvalidDataException>(() => new ProfileImportReader().ReadAsync(new(f.Root, ProfileImportSourceKind.Directory), Token));
        await Assert.ThrowsAsync<InvalidDataException>(() => new SaveData(f.Root).ApplyProfileImportAsync(request, Token));
        Assert.Equal("outside", File.ReadAllText(Path.Combine(external.Root, "x.json")));
    }

    [Fact]
    public async Task ZipSymlinkAndOversizedAssetAreRejected()
    {
        using var f = new Fixture(); var path = Path.Combine(f.Root, "import.nvzip");
        using (var zip = ZipFile.Open(path, ZipArchiveMode.Create)) { var entry = zip.CreateEntry("Scripts/OnStartup.nvjs"); entry.ExternalAttributes = unchecked((int)0xA1FF0000); }
        await Assert.ThrowsAsync<InvalidDataException>(() => new ProfileImportReader().ReadAsync(new(path, ProfileImportSourceKind.Backup), Token));
        var large = f.Put("Scripts/large.nvjs", ""); using (var stream = File.OpenWrite(large)) stream.SetLength(ProfileImportFiles.MaxFileBytes + 1);
        await Assert.ThrowsAsync<InvalidDataException>(() => new ProfileImportReader().ReadAsync(new(f.Root, ProfileImportSourceKind.Directory), Token));
    }

    [Fact]
    public async Task CancellationBeforeCommitDoesNotCreateImportBackupOrModifyFiles()
    {
        using var f = new Fixture(); f.Put("Themes/x.json", "old"); using var canceled = new CancellationTokenSource(); canceled.Cancel();
        var request = (await PreviewAsync(Bundle(("Themes/x.json", "new")))).CreateRequest(Assets(themes: true));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new SaveData(f.Root).ApplyProfileImportAsync(request, canceled.Token));
        Assert.Equal("old", File.ReadAllText(Path.Combine(f.Root, "Themes/x.json"))); Assert.False(Directory.Exists(Path.Combine(f.Root, "ImportBackups")));
    }

    /// <summary>正式前端仅编辑选择快照，所有材料默认关闭，绑定不会执行脚本或写入状态。</summary>
    [AvaloniaFact]
    public async Task FormalViewSeparatesMaterialsFromRuntimeCapabilities()
    {
        var bundle = Bundle(("Playlists/one.nvpls", "{\"Format\":\"NeeView.Playlist/2.0.1\",\"Items\":[]}"), ("Themes/x.json", "theme"), ("Scripts/OnStartup.nvjs", "never execute"));
        using var model = new ProfileImportViewModel(new(new Reader(bundle), Definitions(), new HashSet<string>()));
        await model.SelectSourceAsync(new("synthetic", ProfileImportSourceKind.Directory)); var window = new ProfileImportWindow(model); window.Show();
        try
        {
            Dispatcher.UIThread.RunJobs(); window.UpdateLayout();
            var boxes = window.GetVisualDescendants().OfType<CheckBox>().ToDictionary(b => b.Content!.ToString()!);
            Assert.False(boxes["播放列表"].IsChecked); Assert.False(boxes["主题材料"].IsChecked); Assert.False(boxes["脚本材料（不执行）"].IsChecked);
            boxes["设置 / 命令"].IsChecked = false; boxes["目录参数"].IsChecked = false; boxes["快速访问"].IsChecked = false;
            Assert.False(model.CanApply); boxes["脚本材料（不执行）"].IsChecked = true; Assert.True(model.ImportScripts); Assert.True(model.CanApply);
            Assert.Equal(new[] { "Scripts/OnStartup.nvjs" }, (await model.CreateRequestAsync())!.AssetNames);
            var tabs = window.GetVisualDescendants().OfType<TabControl>().Single(); tabs.SelectedIndex = 1;
            Dispatcher.UIThread.RunJobs(); window.UpdateLayout();
            Assert.Equal(3, window.FindControl<ListBox>("ImportAssetList")!.ItemCount);
            if (System.Environment.GetEnvironmentVariable("NEEVIEW_P5_ASSETS_SCREENSHOT") is { } shot)
                using (var bitmap = window.CaptureRenderedFrame()!) bitmap.Save(shot, Avalonia.Media.Imaging.PngBitmapEncoderOptions.Default);
        }
        finally { window.Close(); }
    }
}
