using System.Text.Json;
using System.Text.Json.Nodes;
using NeeView.Runtime.LayoutPanel;
namespace NeeView.Engine.Tests;

/// <summary>原 UserSettingValidator 版本边界、真实多态形状、旧布局和完整导入往返。</summary>
public sealed class ProfileLegacyUpgradeTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;
    private static JsonObject Setting(string version, string body = "") => JsonNode.Parse("{\"Format\":\"NeeView/" + version + "\"" + body + "}")!.AsObject();
    private static JsonObject Upgrade(string version, string body = "") => ProfileImportCompatibility.Upgrade("UserSetting.json", Setting(version, body));

    [Theory]
    [InlineData("38.0.0")]
    [InlineData("39.0.0")]
    [InlineData("40.0.0")]
    [InlineData("41.0.0")]
    [InlineData("42.0.6")]
    [InlineData("43.0.3661")]
    [InlineData("44.0.0")]
    [InlineData("45.0.3973")]
    [InlineData("46.0.4065")]
    [InlineData("46.0.4134")]
    [InlineData("46.0.4176")]
    [InlineData("46.0.4209")]
    [InlineData("46.1.0")]
    [InlineData("46.2.0")]
    [InlineData("46.3.4340")]
    public void SupportedSettingsUpgradeIdempotentlyWithoutTouchingInput(string version)
    {
        var source = Setting(version, ",\"Config\":{\"Future\":{\"X\":42}},\"Commands\":{}"); var before = source.ToJsonString();
        var upgraded = ProfileImportCompatibility.Upgrade("UserSetting.json", source.DeepClone().AsObject());
        Assert.Equal(before, source.ToJsonString()); Assert.Equal(42, upgraded["Config"]!["Future"]!["X"]!.GetValue<int>());
        Assert.Equal("NeeView/46.3.0", upgraded["Format"]!.GetValue<string>());
        Assert.True(JsonNode.DeepEquals(upgraded, ProfileImportCompatibility.Upgrade("UserSetting.json", upgraded.DeepClone().AsObject())));
    }

    [Theory]
    [InlineData("37.9.0")]
    [InlineData("47.0.0")]
    [InlineData("46.4.0")]
    [InlineData("46.3.4341")]
    [InlineData("46.3.0.1")]
    public void UnreviewedVersionsRemainPreviewOnly(string version) => Assert.NotNull(ProfileImportCompatibility.BlockReason("UserSetting.json", Setting(version)));

    [Fact]
    public void OldFontsRequireWindowsBaselineButIndependentCollectionsKeepTheirOwnRange()
    {
        var font = Setting("38.0.0", ",\"Config\":{\"Panels\":{\"FontSize\":13}}");
        Assert.Contains("MessageFontSize", ProfileImportCompatibility.BlockReason("UserSetting.json", font));
        Assert.Throws<InvalidDataException>(() => ProfileImportCompatibility.Upgrade("UserSetting.json", font));
        var migrated = Upgrade("38.0.0", ",\"Config\":{\"Panels\":{\"FontName\":\"Meiryo\",\"FontSize\":0},\"System\":{\"Language\":\"English\"}}");
        Assert.Equal("Meiryo", migrated["Config"]!["Fonts"]!["FontName"]!.GetValue<string>());
        Assert.Equal("en", migrated["Config"]!["System"]!["Language"]!.GetValue<string>());
        Assert.NotNull(ProfileImportCompatibility.BlockReason("Foldres.json", JsonNode.Parse("""{"Format":"NeeView.Folders/45.0.4000"}""")!.AsObject()));
    }

    [Theory]
    [InlineData("38.9.0", "Entry", "EntryDescending")]
    [InlineData("39.0.0", "FileName", "FileNameDescending")]
    public void SortingAndPagemarkRenamesHaveExactVersionGate(string version, string first, string second)
    {
        var raw = Upgrade(version, """, "Config":{"BookSetting":{"SortMode":"FileName"},"Panels":{"Layout":{"Panels":{"PagemarkPanel":{"Future":9}},"Docks":{"Right":{"Panels":[["PagemarkPanel"]],"PanelLayout":[{"Panels":["PagemarkPanel"]}],"PanelLayoutV2":["Horizontal:PagemarkPanel"],"SelectedItem":"PagemarkPanel"}},"Windows":{"Panels":["PagemarkPanel"]}}}},"Commands":{"NextPagemark:2":{"ShortCutKey":"Old"},"NextPlaylistItem:2":{"ShortCutKey":"New"}},"ContextMenu":{"Children":[{"MenuElementType":"Command","CommandName":"NextPagemark:2"},{"MenuElementType":"Group","CommandName":"NextPagemark:2"}]}""");
        Assert.Equal(first, raw["Config"]!["BookSetting"]!["SortMode"]!.GetValue<string>());
        var descending = Upgrade(version, ",\"Config\":{\"BookSetting\":{\"SortMode\":1}}");
        var options = new JsonSerializerOptions { Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() } };
        Assert.Equal(Enum.Parse<PageSortMode>(second), descending["Config"]!["BookSetting"]!["SortMode"]!.Deserialize<PageSortMode>(options));
        if (Version.Parse(version).Major == 38)
        {
            var layout = raw["Config"]!["Panels"]!["Layout"]!;
            Assert.Null(layout["Panels"]!["PagemarkPanel"]); Assert.Equal(9, layout["Panels"]!["PlaylistPanel"]!["Future"]!.GetValue<int>());
            Assert.Equal("Horizontal:PlaylistPanel", layout["Docks"]!["Right"]!["PanelLayoutV2"]![0]!.GetValue<string>());
            Assert.Equal("PlaylistPanel", layout["Windows"]!["Panels"]![0]!.GetValue<string>());
            Assert.Equal("PlaylistPanel", layout["Docks"]!["Right"]!["SelectedItem"]!.GetValue<string>());
            Assert.Null(raw["Commands"]!["NextPagemark:2"]); Assert.Equal("New", raw["Commands"]!["NextPlaylistItem:2"]!["ShortCutKey"]!.GetValue<string>());
            Assert.Equal("NextPlaylistItem:2", raw["ContextMenu"]!["Children"]![0]!["CommandName"]!.GetValue<string>());
            Assert.Equal("NextPagemark:2", raw["ContextMenu"]!["Children"]![1]!["CommandName"]!.GetValue<string>());
        }
    }

    [Theory]
    [InlineData("39.9.0", "zh-Hant", false, "MoveScale")]
    [InlineData("40.0.0", "zh-TW", true, "Sensitive")]
    public void WheelLanguageF12AndDragSensitivityKeepOriginalGate(string version, string language, bool horizontal, string parameterType)
    {
        var raw = Upgrade(version, """, "Config":{"System":{"Language":"zh-TW"},"Command":{"IsReversePageMoveWheel":false,"IsReversePageMoveHorizontalWheel":true}},"Commands":{"ToggleMainViewFloating":{"ShortCutKey":""}},"DragActions":{"MoveScale":{"Parameter":{"Type":"SensitiveDragActionParameter","Value":{"Sensitivity":0.7,"Future":42}}}}""");
        Assert.Equal(language, raw["Config"]!["System"]!["Language"]!.GetValue<string>());
        Assert.Equal(horizontal, raw["Config"]!["Command"]!["IsReversePageMoveHorizontalWheel"]!.GetValue<bool>());
        Assert.Equal(version.StartsWith("39") ? "F12" : "", raw["Commands"]!["ToggleMainViewFloating"]!["ShortCutKey"]!.GetValue<string>());
        Assert.Equal(parameterType, raw["DragActions"]!["MoveScale"]!["Parameter"]!["$type"]!.GetValue<string>());
        Assert.Equal(.7, raw["DragActions"]!["MoveScale"]!["Parameter"]!["Sensitivity"]!.GetValue<double>());
        Assert.Equal(42, raw["DragActions"]!["MoveScale"]!["MacImportedLegacyParameter"]!["Value"]!["Future"]!.GetValue<int>());
        var conflict = Upgrade("39.0.0", """, "Commands":{"ToggleMainViewFloating":{},"Other":{"ShortCutKey":"X,F12"}}""");
        Assert.Null(conflict["Commands"]!["ToggleMainViewFloating"]!["ShortCutKey"]);
    }

    [Theory]
    [InlineData("39.9.0", false)]
    [InlineData("40.0.0", true)]
    [InlineData("40.2.0", true)]
    [InlineData("40.3.0", false)]
    public void DummyPageMigrationOnlyAppliesTo40Before403(string version, bool expected)
    {
        var raw = Upgrade(version, ",\"Config\":{\"Book\":{\"IsInsertDummyPage\":true}}");
        Assert.Equal(expected, raw["Config"]!["Book"]!["IsInsertDummyFirstPage"]?.GetValue<bool>() ?? false);
    }

    [Theory]
    [InlineData("39.9.0", true)]
    [InlineData("40.0.0", false)]
    [InlineData("40.4.0", false)]
    [InlineData("40.5.0", true)]
    public void PageModeResetAndNullParameterHaveDifferentUpgradeSemantics(string version, bool defaultLoop)
    {
        var raw = Upgrade(version, """, "Commands":{"TogglePageModeReverse":{"ShortCutKey":"Custom","Parameter":null}}""");
        var command = raw["Commands"]!["TogglePageModeReverse"]!;
        Assert.Equal(defaultLoop, command["Parameter"]?["IsLoop"]?.GetValue<bool>() ?? true);
        Assert.Equal(version.StartsWith("39") ? "" : "Custom", command["ShortCutKey"]!.GetValue<string>());
        Assert.Null(raw["Commands"]!["TogglePageMode"]); // 原版只处理存在的命令。
    }

    [Theory]
    [InlineData("40.9.0", "ToggleAutoScroll", "MiddleClick", true)]
    [InlineData("41.0.0", "ToggleAutoScroll", "MiddleClick", false)]
    [InlineData("43.0.3660", "CutFile", "Ctrl+X", true)]
    [InlineData("43.0.3661", "CutFile", "Ctrl+X", false)]
    [InlineData("46.0.4209", "ToggleFullDesktop", "Shift+F11", true)]
    [InlineData("46.0.4210", "ToggleFullDesktop", "Shift+F11", false)]
    public void NewlyAddedCommandsNeverStealExistingBindings(string version, string command, string gesture, bool added)
    {
        var raw = Upgrade(version, ",\"Commands\":{\"Other\":{\"ShortCutKey\":\"" + gesture + "\"}}");
        Assert.Equal(added, raw["Commands"]!.AsObject().ContainsKey(command));
        if (added) Assert.Equal("", raw["Commands"]![command]!["ShortCutKey"]!.GetValue<string>());
        var existing = Setting(version); existing["Commands"] = new JsonObject { [command] = new JsonObject { ["ShortCutKey"] = "Custom" } };
        Assert.Equal("Custom", ProfileImportCompatibility.Upgrade("UserSetting.json", existing)["Commands"]![command]!["ShortCutKey"]!.GetValue<string>());
    }

    [Theory]
    [InlineData("42.0.5", true)]
    [InlineData("42.0.6", false)]
    public void CopyPolicyMigrationRequiresActualParameterType(string version, bool expected)
    {
        var raw = Upgrade(version, """, "Commands":{"CopyFile":{"Parameter":{"Type":"CopyFileCommandParameter","Value":{"ArchivePolicy":"SendArchiveFile","TextCopyPolicy":"CopyFilePath","Future":8}}}}""");
        Assert.Equal(expected, raw["Config"]?["System"]?["ArchiveCopyPolicy"] is not null);
        Assert.Equal(8, raw["Commands"]!["CopyFile"]!["Parameter"]!["Future"]!.GetValue<int>());
        var untyped = Upgrade(version, """, "Commands":{"CopyFile":{"Parameter":{"ArchivePolicy":2}}}""");
        Assert.Null(untyped["Config"]?["System"]?["ArchiveCopyPolicy"]);
    }

    [Theory]
    [InlineData("45.0.3972", true)]
    [InlineData("45.0.3973", false)]
    public void CountDisplayUsesNullableLegacyValueAtExactBoundary(string version, bool migrated)
    {
        var raw = Upgrade(version, ",\"Config\":{\"Panels\":{\"IsVisibleItemsCount\":false}}");
        foreach (var branch in new[] { "Bookshelf", "Bookmark", "PageList", "History" })
            Assert.Equal(migrated, raw["Config"]?[branch]?["IsVisibleItemsCount"] is not null);
    }

    [Theory]
    [InlineData("46.0.4065", true)]
    [InlineData("46.0.4066", false)]
    public void TemplatesAndExportParametersUseAlpha1InclusiveBoundary(string version, bool migrated)
    {
        var raw = Upgrade(version, """, "Config":{"System":{"FileManagerFileArgs":"$File","ExternalAppCollection":[{"Command":"$NeeView","Parameter":"$File $Uri"}]},"Information":{"MapProgramFormat":"$LatDeg,$LonDeg,$Lat,$Lon"},"WindowTitle":{"WindowTitleFormat1":"$PageMax $PageL $Page $FullPathL $FullPath $SizeExL $SizeEx $ViewScale $Scale"}},"Commands":{"OpenExternalApp":{"Parameter":{"$type":"OpenExternalApp","Command":null,"Parameter":"$File $Uri"}},"ExportImageAs":{"Parameter":{"$type":"ExportImageAs","ExportFolder":"P:\\Exports","QualityLevel":82,"Future":42}}}""");
        Assert.Equal(migrated ? "{File}" : "$File", raw["Config"]!["System"]!["FileManagerFileArgs"]!.GetValue<string>());
        Assert.Equal(migrated ? "{LatDeg},{LonDeg},{Lat},{Lon}" : "$LatDeg,$LonDeg,$Lat,$Lon", raw["Config"]!["Information"]!["MapProgramFormat"]!.GetValue<string>());
        Assert.Null(raw["Commands"]!["OpenExternalApp"]!["Parameter"]!["Command"]);
        if (migrated)
        {
            Assert.Equal("{PageMax} {PageL} {Page}{Part: (#)} {FullPathL:/ > } {FullPath:/ > } {SizeL}{BitsL: x #} {Size}{Bits: x #} {ViewScale:#%} {Scale:#%}", raw["Config"]!["WindowTitle"]!["WindowTitleFormat1"]!.GetValue<string>());
            Assert.Equal(82, raw["Config"]!["Book"]!["ExportImageParameter"]!["QualityLevel"]!.GetValue<int>());
            Assert.Null(raw["Commands"]!["ExportImageAs"]!["Parameter"]);
            Assert.Equal(42, raw["Commands"]!["ExportImageAs"]!["MacImportedLegacyParameter"]!["Future"]!.GetValue<int>());
        }
    }

    [Theory]
    [InlineData("46.0.4134", true)]
    [InlineData("46.0.4135", false)]
    public void FilmStripAndMenuRenameUseAlpha2InclusiveBoundary(string version, bool migrated)
    {
        var raw = Upgrade(version, """, "Config":{"Slider":{"IsHidePageSliderInAutoHideMode":false},"FilmStrip":{"IsHideFilmStripInAutoHideMode":true}},"Commands":{"ToggleVisibleThumbnailList:3":{"ShortCutKey":"Ctrl+F"}}""");
        Assert.Equal(!migrated, raw["Config"]!["FilmStrip"]!["IsHideFilmStripInAutoHideMode"]!.GetValue<bool>());
        Assert.NotNull(raw["Commands"]![migrated ? "ToggleVisibleFilmStrip:3" : "ToggleVisibleThumbnailList:3"]);
    }

    [Theory]
    [InlineData("46.0.4176", true)]
    [InlineData("46.0.4177", false)]
    public void DragReplacementUsesDestinationDefaultAndPreservesRetiredMetadata(string version, bool migrated)
    {
        var raw = Upgrade(version, """, "Config":{"View":{"MovementConstraint":"Snap","IsLimitMove":false,"IsMoveLockStart":false}},"DragActions":{"ScaleSliderCentered":{"MouseButton":"Alt+LeftButton"},"BaseScaleSliderCentered":{"MouseButton":"RightButton","Future":42}}""");
        Assert.Equal(migrated ? "LockUntilResized" : "Snap", raw["Config"]!["View"]!["MovementConstraint"]!.GetValue<string>());
        if (migrated)
        {
            Assert.Null(raw["DragActions"]!["ScaleSlider"]); // 默认 Ctrl+LeftButton 非空，不替换。
            Assert.Equal("RightButton", raw["DragActions"]!["BaseScaleSlider"]!["MouseButton"]!.GetValue<string>());
            Assert.Equal(42, raw["MacImportedLegacyDragActions"]!["BaseScaleSliderCentered"]!["Future"]!.GetValue<int>());
        }
        var explicitEmpty = Upgrade(version, """, "DragActions":{"ScaleSliderCentered":{"MouseButton":"RightButton"},"ScaleSlider":{"MouseButton":"","Future":9}}""");
        Assert.Equal(migrated ? "RightButton" : "", explicitEmpty["DragActions"]!["ScaleSlider"]!["MouseButton"]!.GetValue<string>());
    }

    [Theory]
    [InlineData("46.0.4209", true)]
    [InlineData("46.0.4210", false)]
    public void FullDesktopThumbnailAndEffectDataUseAlpha5Boundary(string version, bool migrated)
    {
        var raw = Upgrade(version, """, "Config":{"Window":{"IsAutoHideInFullScreen":false},"Panels":{"ThumbnailItemProfile":{"IsTextVisible":false}},"ImageEffect":{"EffectType":"Sharpen","SharpenEffect":{"Amount":2,"Future":8}}}""");
        Assert.Equal(migrated, raw["Config"]!["Window"]!["IsAutoHideInFullDesktop"] is not null);
        Assert.Equal(migrated, raw["Config"]!["Panels"]!["ThumbnailItemProfile"]!["IsIconOverlay"]?.GetValue<bool>() ?? false);
        Assert.Equal(8, (migrated ? raw["MacImportedLegacyImageEffects"]!["ImageEffect"] : raw["Config"]!["ImageEffect"])!["SharpenEffect"]!["Future"]!.GetValue<int>());
        Assert.Equal(migrated, raw["MacImportedLegacyEffectFormat"] is not null);
        Assert.Equal(migrated, raw["Config"]!["ImageEffect"]!["Layers"] is not null);
        if (migrated) Assert.Equal("Sharpen", raw["Config"]!["ImageEffect"]!["Layers"]![0]!["Effect"]!["$type"]!.GetValue<string>());
    }

    [Theory]
    [InlineData("\"Panels\":[[\"FolderPanel\",\"HistoryPanel\"]]", PanelOrientation.Vertical)]
    [InlineData("\"PanelLayout\":[{\"Orientation\":\"Horizontal\",\"Panels\":[\"FolderPanel\",\"HistoryPanel\"],\"Future\":9}]", PanelOrientation.Horizontal)]
    [InlineData("\"PanelLayoutV2\":[],\"PanelLayout\":[],\"Panels\":[[\"FolderPanel\",\"HistoryPanel\"]]", PanelOrientation.Vertical)]
    [InlineData("\"PanelLayoutV2\":[\"Vertical:FolderPanel,HistoryPanel\"],\"PanelLayout\":[{\"Orientation\":\"Horizontal\",\"Panels\":[\"PageListPanel\"]}]", PanelOrientation.Vertical)]
    public void LayoutFallbackWorksForOrdinaryLoadAndPreservesGrouping(string fields, PanelOrientation orientation)
    {
        var raw = JsonNode.Parse("{\"Docks\":{\"Left\":{" + fields + ",\"SelectedItem\":\"HistoryPanel\",\"Future\":42}},\"AlternativePanelSource\":{\"Future\":9}}")!;
        var memento = raw.Deserialize<LayoutPanelManagerMemento>()!; var layout = new LayoutPanelManager(memento);
        var group = layout.Find("FolderPanel")!.Value.Group; Assert.Equal(orientation, group.Orientation);
        Assert.Equal(new[] { "FolderPanel", "HistoryPanel" }, group.Select(p => p.Key)); Assert.Same(group, layout.Docks["Left"].SelectedItem);
        var saved = JsonSerializer.SerializeToNode(layout.CreateMemento())!;
        Assert.Equal(42, saved["Docks"]!["Left"]!["Future"]!.GetValue<int>());
        Assert.Equal(9, saved["AlternativePanelSource"]!["Future"]!.GetValue<int>());
        var restored = new LayoutPanelManager(saved.Deserialize<LayoutPanelManagerMemento>()!);
        Assert.Equal(group.Select(p => p.Key), restored.Find("FolderPanel")!.Value.Group.Select(p => p.Key));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("UnknownPanel")]
    public void UnknownV2MembersDoNotCauseFallbackOrOpenFirstGroup(string? selected)
    {
        var raw = JsonNode.Parse("""{"Docks":{"Left":{"PanelLayoutV2":["Horizontal:UnknownPanel"],"Panels":[["FolderPanel","HistoryPanel"]],"SelectedItem":null}}}""")!;
        raw["Docks"]!["Left"]!["SelectedItem"] = selected;
        var layout = new LayoutPanelManager(raw.Deserialize<LayoutPanelManagerMemento>()!);
        Assert.Single(layout.Find("FolderPanel")!.Value.Group); Assert.Null(layout.Docks["Left"].SelectedItem);
        var saved = JsonSerializer.SerializeToNode(layout.CreateMemento())!;
        Assert.Equal("Horizontal:UnknownPanel", saved["Docks"]!["Left"]!["MacImportedUnrecognizedLayout"]![0]!.GetValue<string>());
        Assert.True(saved["Docks"]!["Left"]!.AsObject().ContainsKey("SelectedItem")); Assert.Null(saved["Docks"]!["Left"]!["SelectedItem"]);
    }

    [Fact]
    public void LegacyUncBooksMergeUsesOneRootCacheAndKeepsTailCase()
    {
        var raw = JsonNode.Parse("""{"Format":"NeeView.History/44.0.0","Items":[{"Path":"\\\\NAS\\Share\\Case.cbz"}],"Books":[{"Path":"\\\\nas\\share\\Case.cbz","Page":"内页\\003.png","BaseScale":1.5}]}""")!.AsObject();
        var upgraded = ProfileImportCompatibility.Upgrade("History.json", raw);
        Assert.Equal("内页\\003.png", upgraded["Items"]![0]!["Page"]!.GetValue<string>());
        Assert.Contains("Base=1.5", upgraded["Items"]![0]!["Props"]!.GetValue<string>());
        Assert.Equal(@"\\NAS\Share\Case.cbz", upgraded["Items"]![0]!["Path"]!.GetValue<string>());
    }

    [Theory]
    [InlineData("\"PanelLayoutV2\":42,\"Panels\":[[\"FolderPanel\"]]")]
    [InlineData("\"PanelLayout\":{},\"Panels\":[[\"FolderPanel\"]]")]
    [InlineData("\"PanelLayout\":[{\"Orientation\":9,\"Panels\":[\"FolderPanel\"]}]")]
    public void BrokenLayoutIsNotSilentlyReplacedWithOldFallback(string fields)
    {
        var raw = Setting("46.0.4209", ",\"Config\":{\"Panels\":{\"Layout\":{\"Docks\":{\"Left\":{" + fields + "}}}}}");
        Assert.Throws<JsonException>(() => ProfileImportCompatibility.Upgrade("UserSetting.json", raw));
    }

    private sealed class Reader(string text) : IProfileImportReader
    { public Task<ProfileImportBundle> ReadAsync(ProfileImportSource source, CancellationToken token) => Task.FromResult(new ProfileImportBundle(new Dictionary<string, string> { ["UserSetting.json"] = text }, [])); }
    private static Task<ProfileImportPreview> Preview(string text)
    {
        using var stream = typeof(CommandTable).Assembly.GetManifestResourceStream("NeeView.Command.command-manifest.json")!;
        return new ProfileImportService(new Reader(text), JsonSerializer.Deserialize<List<CommandDefinition>>(stream)!, new HashSet<string>()).PreviewAsync(new("synthetic", ProfileImportSourceKind.Directory), [new(@"P:\Exports", "/Exports")], Token);
    }

    [Fact]
    public async Task PreviewValidateApplyAndNormalSaveUseSameUpgradedCandidate()
    {
        var text = """{"Format":"NeeView/46.0.4065","Config":{"Panels":{"Layout":{"Panels":{"HistoryPanel":{"GridLength":".7*","WindowPlacement":"Normal,40,50,300,500","Future":7}},"Docks":{"Left":{"PanelLayout":[{"Orientation":"Horizontal","Panels":["FolderPanel","HistoryPanel"],"Future":8}],"SelectedItem":"HistoryPanel"}},"Windows":{"Panels":["HistoryPanel","UnknownPanel"]},"AlternativePanelSource":{"Future":9}}},"ImageEffect":{"EffectType":"Level","LevelEffect":{"Future":42}},"Future":{"X":[1,2]}},"Commands":{"ToggleVisibleThumbnailList":{"ShortCutKey":"Ctrl+F"},"PrevPage":{"Parameter":{"Type":"ReversibleCommandParameter","Value":{"IsReverse":false,"Future":42}}},"ExportImageAs":{"Parameter":{"$type":"ExportImageAs","ExportFolder":"P:\\Exports","QualityLevel":80}}}}""";
        var preview = await Preview(text); var candidate = preview.GetDocument("UserSetting.json")!;
        Assert.Equal("Ctrl+F", preview.Commands.Single(c => c.Name == "ToggleVisibleFilmStrip").Shortcut);
        Assert.DoesNotContain(preview.Commands, c => c.Name == "ToggleVisibleThumbnailList");
        Assert.Contains(preview.Notices, n => n.Contains("已按原规则转换") && n.Contains("十四类原效果已接入"));
        Assert.Equal("/Exports", candidate["Config"]!["Book"]!["ExportImageParameter"]!["ExportFolder"]!.GetValue<string>());
        var root = Path.Combine(Path.GetTempPath(), "NeeView-P5-Legacy-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
        try
        {
            var state = new SaveData(root); var request = preview.CreateRequest(new()); var original = Config.Current;
            await state.ValidateProfileImportAsync(request, Token); Assert.Same(original, Config.Current);
            var result = await state.ApplyProfileImportAsync(request, Token);
            var saved = JsonNode.Parse(await File.ReadAllTextAsync(Path.Combine(root, "UserSetting.json"), Token))!;
            Assert.True(JsonNode.DeepEquals(candidate["Commands"], saved["Commands"]));
            Assert.True(JsonNode.DeepEquals(candidate["Config"]!["Panels"]!["Layout"], saved["Config"]!["Panels"]!["Layout"]));
            var loaded = new SaveData(root); await loaded.LoadAsync(Token);
            Assert.False(loaded.GetCommandParameter<ReversibleCommandParameter>("PrevPage").IsReverse);
            var layout = new LayoutPanelManager(Config.Current.Panels.Layout); Assert.Contains("HistoryPanel", layout.Windows);
            Assert.Equal(.7, layout.Panels["HistoryPanel"].Weight); Assert.Equal(40, layout.Panels["HistoryPanel"].WindowPlacement.Left);
            Config.Current.Panels.Layout = layout.CreateMemento(); await loaded.SaveAsync(null, Token);
            saved = JsonNode.Parse(await File.ReadAllTextAsync(Path.Combine(root, "UserSetting.json"), Token))!;
            Assert.Equal(42, saved["Config"]!["ImageEffect"]!["Layers"]![0]!["Effect"]!["Future"]!.GetValue<int>());
            Assert.Equal(9, saved["Config"]!["Panels"]!["Layout"]!["AlternativePanelSource"]!["Future"]!.GetValue<int>());
            Assert.Contains("UnknownPanel", saved["Config"]!["Panels"]!["Layout"]!["Windows"]!["Panels"]!.AsArray().Select(n => n!.GetValue<string>()));
            await loaded.ApplyProfileImportAsync((await Preview(text)).CreateRequest(new()), Token);
            Assert.Equal(80, JsonNode.Parse(await File.ReadAllTextAsync(Path.Combine(root, "UserSetting.json"), Token))!["Config"]!["Book"]!["ExportImageParameter"]!["QualityLevel"]!.GetValue<int>());
            if (System.Environment.GetEnvironmentVariable("NEEVIEW_P5_LEGACY_ARTIFACT") is { } artifact)
                await File.WriteAllTextAsync(artifact, JsonSerializer.Serialize(new { source = JsonNode.Parse(text), upgraded = candidate, saved, scope = "合成 Profile；预览/验证/事务应用/普通布局保存，非真实用户导出或设备验收" }, new JsonSerializerOptions { WriteIndented = true }), Token);
            Assert.True(File.Exists(Path.Combine(result.BackupDirectory, "manifest.json")));
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public async Task MismatchedTypedParametersAreRejectedBeforeAnyWrite()
    {
        var preview = await Preview("""{"Format":"NeeView/46.3.0","Commands":{"PrevPage":{"Parameter":{"Type":"CopyFileCommandParameter","Value":{}}}}}""");
        var root = Path.Combine(Path.GetTempPath(), "NeeView-P5-Legacy-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
        try
        { await Assert.ThrowsAsync<InvalidDataException>(() => new SaveData(root).ValidateProfileImportAsync(preview.CreateRequest(new()), Token)); Assert.Empty(Directory.GetFileSystemEntries(root)); }
        finally { Directory.Delete(root, true); }
    }
}
