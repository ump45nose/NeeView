using System.Text.Json;
using System.Text.Json.Nodes;
namespace NeeView.Engine.Tests;

/// <summary>原目录validator、内嵌后备和差分编辑回归；仅使用隔离合成Profile。</summary>
public sealed class ProfileFolderCompatibilityTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;
    private sealed class Reader(Dictionary<string, string> files) : IProfileImportReader
    { public Task<ProfileImportBundle> ReadAsync(ProfileImportSource source, CancellationToken token) => Task.FromResult(new ProfileImportBundle(files, [])); }
    private static Task<ProfileImportPreview> Preview(Dictionary<string, string> files, params ProfilePathMapping[] mappings) =>
        new ProfileImportService(new Reader(files), [], new HashSet<string>()).PreviewAsync(new("synthetic", ProfileImportSourceKind.Directory), mappings, Token);
    private static JsonObject Document(string format, string field, JsonNode? value) => new() { ["Format"] = format, [field] = value };
    private static JsonObject Unit(string place, bool? recursive) => new() { ["Place"] = place, ["Parameter"] = new JsonObject { ["IsFolderRecursive"] = recursive, ["Future"] = 7 } };
    private static ProfileImportSelection FoldersOnly => new(false, true, false, false, false);
    private sealed class Fixture : IDisposable
    {
        public string Root { get; } = Path.Combine(Path.GetTempPath(), "NeeView-P5-Folders-" + Guid.NewGuid().ToString("N"));
        public Fixture() => Directory.CreateDirectory(Root);
        public void Put(string name, JsonObject data) => File.WriteAllText(Path.Combine(Root, name), data.ToJsonString());
        public JsonObject Get(string name) => JsonNode.Parse(File.ReadAllText(Path.Combine(Root, name)))!.AsObject();
        public void Dispose() => Directory.Delete(Root, true);
    }

    [Theory]
    [InlineData("46.0.0", true)]
    [InlineData("46.0.4065", true)]
    [InlineData("46.0.4209", true)]
    [InlineData("46.0.4210", false)]
    [InlineData("46.1.0", false)]
    [InlineData("46.3.4340", false)]
    public void RecursiveConversionKeepsExactInclusiveBoundaryAndIsIdempotent(string version, bool converted)
    {
        var source = Document("NeeView.Folders/" + version, "Folders", new JsonArray(Unit(@"P:\Books", true), Unit(@"P:\Books\child", false)));
        var before = source.ToJsonString();
        var raw = ProfileImportCompatibility.Upgrade("Foldres.json", source.DeepClone().AsObject());
        var child = raw["Folders"]![1]!["Parameter"]!;
        if (converted) Assert.Null(child["IsFolderRecursive"]); else Assert.False(child["IsFolderRecursive"]!.GetValue<bool>());
        Assert.True(raw["Folders"]![0]!["Parameter"]!["IsFolderRecursive"]!.GetValue<bool>());
        Assert.Equal(7, child["Future"]!.GetValue<int>()); Assert.Equal(before, source.ToJsonString());
        Assert.Equal("NeeView.Folders/" + version, raw["MacImportedSourceFormat"]!.GetValue<string>());
        var once = raw.ToJsonString(); ProfileImportCompatibility.Upgrade("Foldres.json", raw); Assert.Equal(once, raw.ToJsonString());
    }

    [Theory]
    [InlineData(@"P:\Books", @"P:\Books\mid\leaf", true)]
    [InlineData(@"P:\", @"P:\Books\leaf", true)]
    [InlineData(@"\\NAS\Share", @"\\NAS\Share\mid\leaf", true)]
    [InlineData(@"bookmark:1", @"bookmark:1\2\3", true)]
    [InlineData(@"quickaccess:Group", @"quickaccess:Group\Child", true)]
    [InlineData(@"file:P:\Books", @"P:\Books\leaf", false)]
    [InlineData(@"P:\Books", @"file:P:\Books\leaf?search=cat", true)]
    [InlineData("/books", "/books/mid/leaf", true)]
    [InlineData("/", "/books/leaf", true)]
    [InlineData(@"P:\Books", @"P:\books\leaf", false)]
    [InlineData(@"P:\Books", @"P:\Books2\leaf", false)]
    public void ParentLookupPreservesOriginalSimplePathBoundariesAndCase(string parent, string child, bool inherited)
    {
        var raw = Document("NeeView.Folders/46.0.4209", "Folders", new JsonArray(Unit(parent, true), Unit(child, true)));
        ProfileImportCompatibility.Upgrade("Foldres.json", raw);
        var value = raw["Folders"]![1]!["Parameter"]!["IsFolderRecursive"];
        if (inherited) Assert.Null(value); else Assert.True(value!.GetValue<bool>());
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void OriginalListOrderUsesMutatingParentsAndFalseAncestorsDoNotStopLookup(bool descendantsFirst)
    {
        var root = Unit(@"P:\Books", true); var middle = Unit(@"P:\Books\mid", false); var leaf = Unit(@"P:\Books\mid\leaf", true);
        var units = descendantsFirst ? new JsonArray(leaf, middle, root) : new JsonArray(root, middle, leaf);
        var raw = Document("NeeView.Folders/46.0.4209", "Folders", units); ProfileImportCompatibility.Upgrade("Foldres.json", raw);
        Assert.Null(middle["Parameter"]!["IsFolderRecursive"]); Assert.Null(leaf["Parameter"]!["IsFolderRecursive"]);
        Assert.True(root["Parameter"]!["IsFolderRecursive"]!.GetValue<bool>());
    }

    [Theory]
    [InlineData("46.0.4065", true)]
    [InlineData("46.0.4066", false)]
    public async Task OrderNormalizationUsesFinalSelectedConfigWithoutChangingCurrent(string version, bool normalize)
    {
        using var fixture = new Fixture();
        var current = new Config(); current.Bookshelf.DefaultFolderOrder = FolderOrder.Random; Config.SetCurrent(current);
        var raw = Document("NeeView.Folders/" + version, "Folders", new JsonArray(new JsonObject
            { ["Place"] = @"P:\Books", ["Parameter"] = new JsonObject { ["FolderOrder"] = "FileNameDescending", ["Seed"] = 7, ["Future"] = 42 }, ["Thumbs"] = new JsonObject() }));
        var files = new Dictionary<string, string>
        {
            ["Foldres.json"] = raw.ToJsonString(),
            ["UserSetting.json"] = """{"Format":"NeeView/46.3.0","Config":{"Bookshelf":{"DefaultFolderOrder":"FileNameDescending"}}}"""
        };
        var preview = await Preview(files, new ProfilePathMapping(@"P:\Books", "/mapped/books"));
        var request = preview.CreateRequest(new(true, true, false, false, false)); var state = new SaveData(fixture.Root);
        await state.ValidateProfileImportAsync(request, Token); Assert.Same(current, Config.Current);
        Assert.Empty(Directory.GetFileSystemEntries(fixture.Root));
        await state.ApplyProfileImportAsync(request, Token); Assert.Same(current, Config.Current);
        var applied = fixture.Get("Foldres.json"); var parameter = applied["Folders"]![0]!["Parameter"]!;
        if (normalize) { Assert.Null(parameter["FolderOrder"]); Assert.Null(applied["Folders"]![0]!["Thumbs"]); }
        else Assert.Equal("FileNameDescending", parameter["FolderOrder"]!.GetValue<string>());
        Assert.Equal(7, parameter["Seed"]!.GetValue<int>()); Assert.Equal(42, parameter["Future"]!.GetValue<int>());
        Assert.Null(applied[LegacyFolderConfigUpgrade.NormalizeOrderField]);
        Assert.Equal("/mapped/books", applied["Folders"]![0]!["Place"]!.GetValue<string>());
        Assert.Equal(raw.ToJsonString(), files["Foldres.json"]);
    }

    [Theory]
    [InlineData("/books", "DefaultFolderOrder")]
    [InlineData("/books/list.nvpls", "PlaylistFolderOrder")]
    [InlineData("bookmark:1", "BookmarkFolderOrder")]
    public async Task EmbeddedDictionaryOnlyNormalizesOrderAndKeepsExplicitFalse(string place, string configField)
    {
        using var fixture = new Fixture();
        var setting = new JsonObject { ["Config"] = new JsonObject { [configField == "BookmarkFolderOrder" ? "Bookmark" : "Bookshelf"] = new JsonObject { [configField] = "Random" } } };
        fixture.Put("UserSetting.json", setting); // 本次不选择来源设置，原 importer 使用现有默认值。
        var history = Document("NeeView.History/44.0.0", "Folders", new JsonObject
            { [place] = new JsonObject { ["FolderOrder"] = "Random", ["IsFolderRecursive"] = false, ["Seed"] = 9, ["Future"] = 42 } });
        var preview = await Preview(new() { ["History.json"] = history.ToJsonString(), ["UserSetting.json"] = """{"Format":"NeeView/46.3.0"}""" });
        var state = new SaveData(fixture.Root); await state.ApplyProfileImportAsync(preview.CreateRequest(FoldersOnly), Token);
        var folders = fixture.Get("Foldres.json"); var parameter = folders["Folders"]![0]!["Parameter"]!;
        Assert.Null(parameter["FolderOrder"]); Assert.False(parameter["IsFolderRecursive"]!.GetValue<bool>());
        Assert.Equal(9, parameter["Seed"]!.GetValue<int>()); Assert.Equal(42, parameter["Future"]!.GetValue<int>());
        Assert.Equal("NeeView.History/44.0.0", folders["MacImportedSourceFormat"]!.GetValue<string>());
        Assert.Equal(setting.ToJsonString(), fixture.Get("UserSetting.json").ToJsonString());
        Assert.False(File.Exists(Path.Combine(fixture.Root, "History.json")));
    }

    [Fact]
    public async Task MissingSettingsUsesDefaultConfigAndIndependentFilesWinOverEmbeddedData()
    {
        using var fixture = new Fixture(); Config.SetCurrent(new()); Config.Current.Bookshelf.DefaultFolderOrder = FolderOrder.Random;
        var files = new Dictionary<string, string>
        {
            ["History.json"] = """{"Format":"NeeView.History/44.0.0","Folders":{"/embedded":{"FolderOrder":"Random"}}}""",
            ["Bookmark.json"] = """{"Format":"NeeView.Bookmark/44.0.0","QuickAccess":{"Format":"future/99.0.0","Items":[{"Path":"/embedded"}]}}""",
            ["Foldres.json"] = """{"Format":"NeeView.Folders/46.0.4065","Folders":[{"Place":"/independent","Parameter":{"FolderOrder":"FileName","Future":1}}]}""",
            ["QuicAccess.json"] = """{"Format":"NeeView.QuickAccess/44.0.0","Items":[{"Path":"/independent"}]}"""
        };
        var state = new SaveData(fixture.Root); await state.ApplyProfileImportAsync((await Preview(files)).CreateRequest(new(false, true, true, false, false)), Token);
        Assert.Null(fixture.Get("Foldres.json")["Folders"]![0]!["Parameter"]!["FolderOrder"]); // 磁盘缺省为FileName，而非Config.Current的Random。
        Assert.Equal("/independent", fixture.Get("Foldres.json")["Folders"]![0]!["Place"]!.GetValue<string>());
        Assert.Equal("/independent", fixture.Get("QuicAccess.json")["Items"]![0]!["Path"]!.GetValue<string>());
        Assert.False(File.Exists(Path.Combine(fixture.Root, "UserSetting.json")));
    }

    [Theory]
    [InlineData("NeeView.QuickAccess/44.0.0")]
    [InlineData("NeeView.QuickAccess/45.0.4000")]
    [InlineData("NeeView.QuickAccess/46.0.4209")]
    public async Task QuickAccessKeepsTreeOrderUnknownFieldsAndNestedVersion(string format)
    {
        using var fixture = new Fixture();
        var quick = JsonNode.Parse("""{"Items":[{"Name":"组","Children":[{"Name":"first","Path":"P:\\Books\\One","Future":7},{"Name":"last","Path":"bookmark:1"}]},{"Name":"empty","Children":[]}],"Future":42}""")!.AsObject(); quick["Format"] = format;
        var bookmark = Document("NeeView.Bookmark/44.0.0", "QuickAccess", quick);
        var preview = await Preview(new() { ["Bookmark.json"] = bookmark.ToJsonString() }, new ProfilePathMapping(@"P:\Books", "/mapped/books"));
        var state = new SaveData(fixture.Root); var result = await state.ApplyProfileImportAsync(preview.CreateRequest(new(false, false, true, false, false)), Token);
        await state.LoadAsync(Token); Assert.Equal("first", state.QuickAccess.Root.Children![0].Children![0].Name);
        Assert.Equal("/mapped/books/One", state.QuickAccess.Root.Children[0].Children![0].Path);
        await state.SaveAsync(null, Token); var applied = fixture.Get("QuicAccess.json");
        Assert.Equal(format, applied["MacImportedSourceFormat"]!.GetValue<string>()); Assert.Equal(42, applied["Future"]!.GetValue<int>());
        Assert.Equal(7, applied["Items"]![0]!["Children"]![0]!["Future"]!.GetValue<int>());
        Assert.Equal("last", applied["Items"]![0]!["Children"]![1]!["Name"]!.GetValue<string>());
        Assert.Empty(applied["Items"]![1]!["Children"]!.AsArray());
        await state.RestoreProfileImportAsync(result.BackupDirectory, Token); Assert.Empty(Directory.GetFiles(fixture.Root));
    }

    [Theory]
    [InlineData("NeeView.QuickAccess/47.0.0")]
    [InlineData("NeeView.QuickAccess/46.3.4341")]
    [InlineData("NeeView.QuickAccess/43.0.0")]
    [InlineData("wrong/46.3.0")]
    [InlineData(null)]
    public async Task ExplicitUnsupportedEmbeddedQuickFormatIsNeverHiddenByParentUpgrade(string? format)
    {
        using var fixture = new Fixture();
        var bookmark = Document("NeeView.Bookmark/44.0.0", "QuickAccess", Document(format!, "Items", new JsonArray()));
        var preview = await Preview(new() { ["Bookmark.json"] = bookmark.ToJsonString() });
        Assert.Equal(format, preview.GetDocument("Bookmark.json")!["QuickAccess"]!["Format"]?.GetValue<string>());
        Assert.Throws<InvalidDataException>(() => preview.CreateRequest(new(false, false, true, false, false)));
        Assert.Empty(Directory.GetFileSystemEntries(fixture.Root));
    }

    [Fact]
    public async Task ThumbnailMappingKeepsRelativeArchiveTargetsAndOriginalNames()
    {
        var folders = """{"Format":"NeeView.Folders/46.0.4209","Folders":[{"Place":"P:\\Books","Thumbs":{"One.cbz":"P:\\Covers\\One.png","Two.cbz":"内页\\001.png","Future.cbz":"Q:\\Other.png"}}]}""";
        var preview = await Preview(new() { ["Foldres.json"] = folders }, new ProfilePathMapping(@"P:\Books", "/mapped/books"), new ProfilePathMapping(@"P:\Covers", "/mapped/covers"));
        var thumbs = preview.GetDocument("Foldres.json")!["Folders"]![0]!["Thumbs"]!;
        Assert.Equal("/mapped/covers/One.png", thumbs["One.cbz"]!.GetValue<string>());
        Assert.Equal(@"内页\001.png", thumbs["Two.cbz"]!.GetValue<string>()); Assert.Equal(@"Q:\Other.png", thumbs["Future.cbz"]!.GetValue<string>());
        Assert.Single(preview.Paths, p => p.Status == ProfilePathStatus.Unmapped);
    }

    [Fact]
    public async Task DuplicateMappedPlacesFailBeforeBackupOrAnyFileWrite()
    {
        using var fixture = new Fixture();
        var folders = Document("NeeView.Folders/46.0.4209", "Folders", new JsonArray(Unit(@"P:\Books", true), Unit(@"Q:\Books", false)));
        var preview = await Preview(new() { ["Foldres.json"] = folders.ToJsonString() }, new ProfilePathMapping(@"P:\Books", "/mapped/books"), new ProfilePathMapping(@"Q:\Books", "/mapped/books"));
        var state = new SaveData(fixture.Root); var request = preview.CreateRequest(FoldersOnly);
        await Assert.ThrowsAsync<JsonException>(() => state.ApplyProfileImportAsync(request, Token));
        Assert.Empty(Directory.GetFileSystemEntries(fixture.Root));
    }

    [Fact]
    public async Task ImportedRecursiveStateAndUnknownFieldsSurviveNormalSaveAndReload()
    {
        using var fixture = new Fixture();
        var raw = Document("NeeView.Folders/46.0.4209", "Folders", new JsonArray(Unit(@"P:\Books", true), Unit(@"P:\Books\mid\leaf", false)));
        raw["Future"] = 42;
        var state = new SaveData(fixture.Root); var preview = await Preview(new() { ["Foldres.json"] = raw.ToJsonString() }, new ProfilePathMapping(@"P:\Books", "/mapped/books"));
        var result = await state.ApplyProfileImportAsync(preview.CreateRequest(FoldersOnly), Token);
        await state.LoadAsync(Token); Assert.True(new FolderParameter("/mapped/books/mid/leaf", state.FolderConfigs).IsFolderRecursive);
        await state.SaveAsync(null, Token); await state.LoadAsync(Token);
        Assert.True(new FolderParameter("/mapped/books/mid/leaf", state.FolderConfigs).IsFolderRecursive);
        Assert.Equal(7, fixture.Get("Foldres.json")["Folders"]![1]!["Parameter"]!["Future"]!.GetValue<int>());
        Assert.Equal(42, fixture.Get("Foldres.json")["Future"]!.GetValue<int>());
        if (Environment.GetEnvironmentVariable("NEEVIEW_P5_FOLDER_ARTIFACT") is { } artifact)
            await File.WriteAllTextAsync(artifact, JsonSerializer.Serialize(new
            {
                scope = "隔离合成Profile，原46.0.4209目录validator、映射、五文件事务、普通保存/重载",
                source_format = raw["Format"]!.GetValue<string>(), applied = fixture.Get("Foldres.json"),
                inherited_recursive = new FolderParameter("/mapped/books/mid/leaf", state.FolderConfigs).IsFolderRecursive,
                exclusions = new[] { "真实用户导出", "真机/Windows动态", "签名公证与安装" }
            }, new JsonSerializerOptions { WriteIndented = true }), Token);
        await state.RestoreProfileImportAsync(result.BackupDirectory, Token); Assert.Empty(Directory.GetFiles(fixture.Root));
    }

    [Fact]
    public async Task EditingScrollDifferenceRetiresLegacySettersAndKeepsUnknownCommandFields()
    {
        using var fixture = new Fixture(); fixture.Put("UserSetting.json", JsonNode.Parse("""{"Commands":{"ScrollPage":{"ShortCutKey":"Ctrl+J","Future":9,"Parameter":{"ScrollType":"NType","IsNScroll":true,"PageMoveMargin":4,"Future":42}}}}""")!.AsObject());
        var state = new SaveData(fixture.Root); await state.LoadAsync(Token);
        var parameter = state.GetScrollParameter("ScrollPage"); Assert.Equal(4, parameter.LineBreakStopTime);
        parameter.ScrollType = NScrollType.Horizontal; parameter.LineBreakStopTime = 1; parameter.LineBreakStopMode = LineBreakStopMode.Line;
        state.SetCommandParameter("ScrollPage", parameter); await state.SaveAsync(null, Token);
        await state.LoadAsync(Token); var restored = state.GetScrollParameter("ScrollPage");
        Assert.Equal(NScrollType.Horizontal, restored.ScrollType); Assert.Equal(1, restored.LineBreakStopTime); Assert.Equal(LineBreakStopMode.Line, restored.LineBreakStopMode);
        var command = fixture.Get("UserSetting.json")["Commands"]!["ScrollPage"]!;
        Assert.Null(command["Parameter"]!["IsNScroll"]); Assert.Null(command["Parameter"]!["PageMoveMargin"]);
        Assert.True(command["MacImportedLegacyParameterFields"]!["IsNScroll"]!.GetValue<bool>());
        Assert.Equal(42, command["Parameter"]!["Future"]!.GetValue<int>()); Assert.Equal(9, command["Future"]!.GetValue<int>());
        Assert.Equal("Ctrl+J", state.GetShortcut("ScrollPage", ""));
    }
}
