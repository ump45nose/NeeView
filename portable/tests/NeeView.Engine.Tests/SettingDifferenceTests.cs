using System.Text.Json;
using System.Text.Json.Nodes;
using NeeView.Runtime.LayoutPanel;
namespace NeeView.Engine.Tests;

/// <summary>原差分保存的重启行为；隔离 Profile，不触碰用户数据或前台窗口。</summary>
public sealed class SettingDifferenceTests
{
    public sealed class ComparisonProbe
    {
        public int Value { get; set; } = 7;
        [System.Text.Json.Serialization.JsonPropertyName("renamed")] public int Renamed { get; set; } = 8;
        [DiffJsonDefault(typeof(FalseDefaultable))] public int Forced { get; set; }
        [System.Text.Json.Serialization.JsonIgnore] public int Ignored { get; set; }
        [System.Text.Json.Serialization.JsonExtensionData] public Dictionary<string, JsonElement>? Extra { get; set; }
    }
    private static CancellationToken Token => TestContext.Current.CancellationToken;
    private sealed class Fixture : IDisposable
    {
        public string Root { get; } = Path.Combine(Path.GetTempPath(), "NeeView-P5-Difference-" + Guid.NewGuid().ToString("N"));
        public Fixture() => Directory.CreateDirectory(Root);
        public void Put(string json) => File.WriteAllText(Path.Combine(Root, "UserSetting.json"), json);
        public JsonObject Get() => JsonNode.Parse(File.ReadAllText(Path.Combine(Root, "UserSetting.json")))!.AsObject();
        public async Task<SaveData> Load() { var state = new SaveData(Root); await state.LoadAsync(Token); return state; }
        public void Dispose() => Directory.Delete(Root, true);
    }

    [Fact]
    public void OriginalConverterKeepsEqualityForcedDefaultsAndJsonNamesWithoutExtraWrapper()
    {
        var options = new JsonSerializerOptions(); options.Converters.Add(new DiffJsonConverter<ComparisonProbe>());
        var probe = new ComparisonProbe { Ignored = 99, Extra = new() { ["Future"] = JsonSerializer.SerializeToElement(42) } };
        var raw = JsonSerializer.SerializeToNode(probe, options)!.AsObject(); Assert.Single(raw); Assert.Equal(0, raw["Forced"]!.GetValue<int>());
        probe.Renamed = 9; raw = JsonSerializer.SerializeToNode(probe, options)!.AsObject(); Assert.Equal(9, raw["renamed"]!.GetValue<int>()); Assert.Null(raw["Extra"]);
        var restored = JsonSerializer.Deserialize<ComparisonProbe>("{\"renamed\":9,\"Future\":42}", options)!;
        Assert.Equal(7, restored.Value); Assert.Equal(9, restored.Renamed);
    }

    [Fact]
    public async Task DefaultsOfAllMigratedBranchesAreOmittedAndRestored()
    {
        using var fixture = new Fixture(); var state = await fixture.Load();
        var expected = JsonSerializer.SerializeToNode(Config.Current);
        await state.SaveAsync(null, Token);
        Assert.Empty(fixture.Get()["Config"]!.AsObject()); Assert.Null(fixture.Get()["Commands"]);
        await fixture.Load(); Assert.True(JsonNode.DeepEquals(expected, JsonSerializer.SerializeToNode(Config.Current)));
    }

    [Fact]
    public async Task DefaultEffectCollectionsWithFutureFieldsAreRetainedAcrossRepeatedSaves()
    {
        using var fixture = new Fixture(); fixture.Put("""{"Config":{"ImageEffect":{"Layers":[{"FutureLayer":7}]},"EffectProfiles":{"Profiles":[{"Id":0,"FutureProfile":8}],"FutureCollection":9},"ImageResizeFilter":{"FutureFilter":{"Value":10}},"ImageEffectCache":[{"$type":"FutureEffect","Value":11}]}}""");
        var state = await fixture.Load();
        for (var i = 0; i < 2; i++)
        {
            await state.SaveAsync(null, Token); state = await fixture.Load(); var raw = fixture.Get()["Config"]!;
            Assert.Equal(7, raw["ImageEffect"]!["Layers"]![0]!["FutureLayer"]!.GetValue<int>());
            Assert.Equal(8, raw["EffectProfiles"]!["Profiles"]![0]!["FutureProfile"]!.GetValue<int>());
            Assert.Equal(9, raw["EffectProfiles"]!["FutureCollection"]!.GetValue<int>());
            Assert.Equal(10, raw["ImageResizeFilter"]!["FutureFilter"]!["Value"]!.GetValue<int>());
            Assert.Equal(11, raw["ImageEffectCache"]![0]!["Value"]!.GetValue<int>());
        }
    }

    [Theory]
    [InlineData("Book", "IsPanorama", true)]
    [InlineData("BookSetting", "IsSupportedWidePage", false)]
    [InlineData("FilmStrip", "IsDetailPopupEnabled", false)]
    [InlineData("Slider", "IsEnabled", false)]
    [InlineData("System", "IsRemoveConfirmed", false)]
    [InlineData("History", "IsSaveHistory", false)]
    public async Task RevertingFieldToDefaultRemovesStaleValueAndKeepsUnknown(string branch, string field, bool changed)
    {
        using var fixture = new Fixture(); fixture.Put(new JsonObject { ["Future"] = 1, ["Config"] = new JsonObject
            { [branch] = new JsonObject { ["Future"] = new JsonObject { ["Nested"] = 42 }, [field] = changed } } }.ToJsonString());
        var state = await fixture.Load(); await state.SaveAsync(null, Token);
        var configProperty = typeof(Config).GetProperty(branch)!;
        var value = configProperty.GetValue(Config.Current)!;
        var property = value.GetType().GetProperty(field)!;
        property.SetValue(value, property.GetValue(configProperty.GetValue(new Config())));
        await state.SaveAsync(null, Token);
        var saved = fixture.Get(); Assert.Null(saved["Config"]![branch]![field]);
        Assert.Equal(42, saved["Config"]![branch]!["Future"]!["Nested"]!.GetValue<int>()); Assert.Equal(1, saved["Future"]!.GetValue<int>());
        await fixture.Load(); Assert.Equal(property.GetValue(configProperty.GetValue(new Config())), property.GetValue(configProperty.GetValue(Config.Current)));
    }

    [Theory]
    [InlineData(PanelListItemStyle.Normal, "NormalItemProfile")]
    [InlineData(PanelListItemStyle.Content, "ContentItemProfile")]
    [InlineData(PanelListItemStyle.Banner, "BannerItemProfile")]
    [InlineData(PanelListItemStyle.Thumbnail, "ThumbnailItemProfile")]
    public async Task ProfileDiffUsesEachOriginalTemplateDefaults(PanelListItemStyle style, string name)
    {
        using var fixture = new Fixture(); fixture.Put(new JsonObject { ["Config"] = new JsonObject { ["Panels"] = new JsonObject
            { [name] = new JsonObject { ["Future"] = 99 } } } }.ToJsonString());
        var state = await fixture.Load(); var profile = Config.Current.Panels.GetProfile(style); var defaults = PanelListItemProfile.Create(style);
        profile.ImageWidth = 501; await state.SaveAsync(null, Token); Assert.Equal(501, fixture.Get()["Config"]!["Panels"]![name]!["ImageWidth"]!.GetValue<int>());
        profile.ImageWidth = defaults.ImageWidth; await state.SaveAsync(null, Token);
        var raw = fixture.Get()["Config"]!["Panels"]![name]!.AsObject(); Assert.Single(raw); Assert.Equal(99, raw["Future"]!.GetValue<int>());
        await fixture.Load(); Assert.Equal(defaults.ImageWidth, Config.Current.Panels.GetProfile(style).ImageWidth);
        Assert.Equal(defaults.ImageShape, Config.Current.Panels.GetProfile(style).ImageShape);
        Assert.Equal(defaults.IsImagePopupEnabled, Config.Current.Panels.GetProfile(style).IsImagePopupEnabled);
    }

    [Theory]
    [InlineData(InputScheme.TypeA, PageReadOrder.RightToLeft)]
    [InlineData(InputScheme.TypeA, PageReadOrder.LeftToRight)]
    [InlineData(InputScheme.TypeB, PageReadOrder.RightToLeft)]
    [InlineData(InputScheme.TypeB, PageReadOrder.LeftToRight)]
    [InlineData(InputScheme.TypeC, PageReadOrder.RightToLeft)]
    [InlineData(InputScheme.TypeC, PageReadOrder.LeftToRight)]
    public async Task CommandsUseFinalInputSchemeAndDirectionDefaults(InputScheme scheme, PageReadOrder direction)
    {
        using var fixture = new Fixture(); var state = await fixture.Load(); Config.Current.Command.PresetInputScheme = scheme; Config.Current.Command.PresetPageReadOrder = direction;
        var shortcut = DefaultInputScheme.GetShortcut("NextPage", "", Config.Current.Command);
        state.SetShortcut("NextPage", shortcut); state.SetMouseGestureDifference("NextPage", DefaultInputScheme.GetMouseGesture("NextPage", "", Config.Current.Command), "");
        await state.SaveAsync(null, Token); Assert.Null(fixture.Get()["Commands"]);
        var fresh = await fixture.Load(); Assert.Equal(shortcut, fresh.GetShortcut("NextPage", ""));
        fresh.SetShortcut("NextPage", ""); await fresh.SaveAsync(null, Token);
        Assert.Equal("", fixture.Get()["Commands"]!["NextPage"]!["ShortCutKey"]!.GetValue<string>());
        fresh = await fixture.Load(); Assert.Equal("", fresh.GetShortcut("NextPage", ""));
    }

    [Theory]
    [InlineData(1)] [InlineData(2)] [InlineData(3)] [InlineData(4)] [InlineData(5)]
    [InlineData(6)] [InlineData(7)] [InlineData(8)] [InlineData(9)]
    public async Task NumberCommandDefaultIndexIsInstanceSpecificAndZeroRemainsOverride(int number)
    {
        using var fixture = new Fixture(); var state = await fixture.Load(); var name = "MoveToDestinationFolder" + number;
        state.SetCommandParameter(name, new MoveToFolderAsCommandParameter { Index = number }); await state.SaveAsync(null, Token); Assert.Null(fixture.Get()["Commands"]);
        state = await fixture.Load(); Assert.Equal(number, state.GetDestinationParameter(name).Index);
        state.SetCommandParameter(name, new MoveToFolderAsCommandParameter { Index = 0 }); await state.SaveAsync(null, Token);
        Assert.Equal(0, fixture.Get()["Commands"]![name]!["Parameter"]!["Index"]!.GetValue<int>());
        state = await fixture.Load(); Assert.Equal(0, state.GetDestinationParameter(name).Index);
    }

    [Fact]
    public async Task NondefaultTypedParameterPlacesDiscriminatorFirstAndPreservesUnknown()
    {
        using var fixture = new Fixture(); fixture.Put("""{"Commands":{"ViewScaleUp":{"Parameter":{"Future":{"Value":7},"$type":"ViewScale","Scale":0.4,"IsSnapDefaultScale":true}}}}""");
        var state = await fixture.Load(); await state.SaveAsync(null, Token); var raw = fixture.Get()["Commands"]!["ViewScaleUp"]!["Parameter"]!.AsObject();
        Assert.Equal("$type", raw.First().Key); Assert.Equal("ViewScale", raw["$type"]!.GetValue<string>()); Assert.Null(raw["IsSnapDefaultScale"]); Assert.Equal(7, raw["Future"]!["Value"]!.GetValue<int>());
        state = await fixture.Load(); Assert.Equal(.4, state.GetCommandParameter<ViewScaleCommandParameter>("ViewScaleDown").Scale);
        state.SetCommandParameter("ViewScaleDown", new ViewScaleCommandParameter()); await state.SaveAsync(null, Token);
        raw = fixture.Get()["Commands"]!["ViewScaleUp"]!["Parameter"]!.AsObject(); Assert.Null(raw["Scale"]); Assert.Equal(2, raw.Count);
    }

    [Theory]
    [InlineData(true)] [InlineData(false)]
    public async Task SharedParametersWriteOwnerAndArchiveAliasWithoutChangingRuntime(bool ownerExists)
    {
        using var fixture = new Fixture(); fixture.Put(ownerExists
            ? """{"Commands":{"PrevScrollPage":{"Parameter":{"Scroll":0.75,"FutureOwner":8}},"NextScrollPage":{"Parameter":{"Scroll":0.5,"FutureAlias":9}}}}"""
            : """{"Commands":{"NextScrollPage":{"Parameter":{"Scroll":0.5,"FutureAlias":9}}}}""");
        var state = await fixture.Load(); await state.SaveAsync(null, Token); Assert.Equal(ownerExists ? .75 : .5, state.GetScrollParameter("NextScrollPage").Scroll);
        var commands = fixture.Get()["Commands"]!; Assert.Null(commands["NextScrollPage"]!["Parameter"]);
        Assert.Equal(9, commands["NextScrollPage"]!["MacImportedSharedParameter"]!["Parameter"]!["FutureAlias"]!.GetValue<int>());
        if (ownerExists) Assert.Equal(8, commands["PrevScrollPage"]!["Parameter"]!["FutureOwner"]!.GetValue<int>());
        state = await fixture.Load(); Assert.Equal(ownerExists ? .75 : .5, state.GetScrollParameter("NextScrollPage").Scroll);
    }

    [Fact]
    public async Task UnknownCommandsFutureDiscriminatorsAndMigratedParameterExtensionsArePreserved()
    {
        using var fixture = new Fixture(); const string input = """{"Config":{"FutureBranch":{"Data":[1,2]},"ImageEffect":{"Future":true}},"Commands":{"FutureCommand":{"Parameter":{"Value":0}},"SetEffectProfile":{"Parameter":{"Future":42}},"ViewScaleUp":{"Parameter":{"$type":"FutureScale","Scale":0.2}}}}""";
        fixture.Put(input); var state = await fixture.Load(); await state.SaveAsync(null, Token);
        var expected = JsonNode.Parse(input)!["Commands"]!; var saved = fixture.Get()["Commands"]!;
        foreach (var name in new[] { "FutureCommand", "ViewScaleUp" }) Assert.True(JsonNode.DeepEquals(expected[name], saved[name]));
        Assert.Equal("SetEffectProfile", saved["SetEffectProfile"]!["Parameter"]!["$type"]!.GetValue<string>());
        Assert.Equal(42, saved["SetEffectProfile"]!["Parameter"]!["Future"]!.GetValue<int>());
        Assert.Null(saved["SetEffectProfile"]!["Parameter"]!["Id"]);
        Assert.True(fixture.Get()["Config"]!["ImageEffect"]!["Future"]!.GetValue<bool>());
    }

    [Theory]
    [InlineData(true)] [InlineData(false)]
    public async Task NullOwnerUsesSameAliasFallbackRegardlessOfJsonOrder(bool ownerFirst)
    {
        using var fixture = new Fixture(); fixture.Put(ownerFirst
            ? """{"Commands":{"PrevScrollPage":{"Parameter":null},"NextScrollPage":{"Parameter":{"Scroll":0.5}}}}"""
            : """{"Commands":{"NextScrollPage":{"Parameter":{"Scroll":0.5}},"PrevScrollPage":{"Parameter":null}}}""");
        var state = await fixture.Load(); Assert.Equal(.5, state.GetScrollParameter("NextScrollPage").Scroll);
        await state.SaveAsync(null, Token); state = await fixture.Load(); Assert.Equal(.5, state.GetScrollParameter("NextScrollPage").Scroll);
        Assert.Equal(.5, fixture.Get()["Commands"]!["PrevScrollPage"]!["Parameter"]!["Scroll"]!.GetValue<double>());
    }

    [Theory]
    [InlineData(true)] [InlineData(false)]
    public async Task SharedAliasCannotRetypeUnknownFutureOwnerParameter(bool ownerFirst)
    {
        using var fixture = new Fixture(); fixture.Put(ownerFirst
            ? """{"Commands":{"ViewScaleUp":{"Parameter":{"$type":"FutureScale","Scale":0.2,"Future":9}},"ViewScaleDown":{"Parameter":{"$type":"ViewScale","Scale":0.4}}}}"""
            : """{"Commands":{"ViewScaleDown":{"Parameter":{"$type":"ViewScale","Scale":0.4}},"ViewScaleUp":{"Parameter":{"$type":"FutureScale","Scale":0.2,"Future":9}}}}""");
        var expected = JsonNode.Parse("""{"$type":"FutureScale","Scale":0.2,"Future":9}""");
        var state = await fixture.Load(); await state.SaveAsync(null, Token);
        Assert.True(JsonNode.DeepEquals(expected, fixture.Get()["Commands"]!["ViewScaleUp"]!["Parameter"]));
        Assert.Null(fixture.Get()["Commands"]!["ViewScaleDown"]!["Parameter"]);
    }

    [Theory]
    [InlineData("NextPage", "TouchL1,TouchL2", false)]
    [InlineData("ShowHiddenPanels", "TouchCenter", false)]
    [InlineData("PrevPlaylist", "", true)]
    [InlineData("NextEffectProfile", "", true)]
    [InlineData("PrevEffectProfile", "", true)]
    public async Task TouchAndNotificationDefaultsComeFromActualConstructors(string name, string touch, bool show)
    {
        using var fixture = new Fixture(); fixture.Put(new JsonObject { ["Commands"] = new JsonObject
            { [name] = new JsonObject { ["TouchGesture"] = touch, ["IsShowMessage"] = show } } }.ToJsonString());
        var state = await fixture.Load(); Config.Current.Command.PresetPageReadOrder = PageReadOrder.LeftToRight;
        await state.SaveAsync(null, Token); Assert.Null(fixture.Get()["Commands"]);
        Assert.Equal(touch, DefaultInputScheme.GetTouchGesture(name)); Assert.Equal(show, DefaultInputScheme.GetShowMessage(name));
        fixture.Put(new JsonObject { ["Commands"] = new JsonObject
            { [name] = new JsonObject { ["TouchGesture"] = "FutureTouch", ["IsShowMessage"] = !show } } }.ToJsonString());
        state = await fixture.Load(); await state.SaveAsync(null, Token);
        Assert.Equal("FutureTouch", fixture.Get()["Commands"]![name]!["TouchGesture"]!.GetValue<string>()); Assert.Equal(!show, fixture.Get()["Commands"]![name]!["IsShowMessage"]!.GetValue<bool>());
    }

    [Fact]
    public async Task DiscriminatorOnlyDefaultParameterAndNullMementosAreOmitted()
    {
        using var fixture = new Fixture(); fixture.Put("""{"Commands":{"ViewScaleUp":{"Parameter":{"$type":"ViewScale"},"TouchGesture":null,"IsShowMessage":null},"UnknownEmpty":{}}}""");
        var state = await fixture.Load(); await state.SaveAsync(null, Token);
        Assert.Null(fixture.Get()["Commands"]?["ViewScaleUp"]); Assert.NotNull(fixture.Get()["Commands"]?["UnknownEmpty"]);
        state = await fixture.Load(); Assert.Equal(.2, state.GetCommandParameter<ViewScaleCommandParameter>("ViewScaleUp").Scale);
    }

    [Theory]
    [InlineData(true, 0)] [InlineData(false, 0)] [InlineData(false, 4)]
    public async Task LegacyParameterSettersAreMaterializedBeforeTrimming(bool nscroll, double margin)
    {
        using var fixture = new Fixture(); fixture.Put(new JsonObject { ["Commands"] = new JsonObject { ["PrevScrollPage"] = new JsonObject
            { ["Parameter"] = new JsonObject { ["$type"] = "ScrollPage", ["ScrollType"] = "NType", ["LineBreakStopTime"] = 6,
                ["IsNScroll"] = nscroll, ["PageMoveMargin"] = margin, ["Future"] = 7 } } } }.ToJsonString());
        var state = await fixture.Load(); var expected = state.GetScrollParameter("PrevScrollPage");
        // 非设置编辑也会写出：必须将旧setter实际生效值保存为新字段，不能裁剪后变回默认。
        await state.EditBookmarkSearchHistoryAsync("cat", token: Token);
        var raw = fixture.Get()["Commands"]!["PrevScrollPage"]!; Assert.Null(raw["Parameter"]!["IsNScroll"]); Assert.Null(raw["Parameter"]!["PageMoveMargin"]);
        Assert.Equal(nscroll, raw["MacImportedLegacyParameterFields"]!["IsNScroll"]!.GetValue<bool>());
        state = await fixture.Load(); var actual = state.GetScrollParameter("PrevScrollPage");
        Assert.Equal(expected.ScrollType, actual.ScrollType); Assert.Equal(expected.LineBreakStopMode, actual.LineBreakStopMode); Assert.Equal(margin, actual.LineBreakStopTime);
        Assert.Equal(7, raw["Parameter"]!["Future"]!.GetValue<int>());
    }

    [Theory]
    [InlineData(true)] [InlineData(false)]
    public async Task LegacyAutoHideAliasesCannotResurrectAfterDefaultReset(bool reset)
    {
        using var fixture = new Fixture(); fixture.Put("""{"Config":{"AutoHide":{"AutoHideHitTestMargin":17,"AutoHideConfrictTopMargin":"Deny","AutoHideConfrictBottomMargin":"Deny","Future":99}}}""");
        var state = await fixture.Load(); Assert.Equal(17, Config.Current.AutoHide.AutoHideHitTestHorizontalMargin);
        if (reset) Config.Current.AutoHide = new();
        await state.SaveAsync(null, Token); var saved = fixture.Get(); var raw = saved["Config"]!["AutoHide"]!;
        Assert.Null(raw["AutoHideHitTestMargin"]); Assert.Null(raw["AutoHideConfrictTopMargin"]);
        Assert.Equal(17, saved["MacImportedLegacyConfigFields"]!["AutoHide"]!["AutoHideHitTestMargin"]!.GetValue<int>());
        await fixture.Load(); Assert.Equal(reset ? 32 : 17, Config.Current.AutoHide.AutoHideHitTestHorizontalMargin);
        Assert.Equal(reset ? AutoHideConflictMode.AllowPixel : AutoHideConflictMode.Deny, Config.Current.AutoHide.AutoHideConflictTopMargin);
        Assert.Equal(99, raw["Future"]!.GetValue<int>());
    }

    [Fact]
    public async Task CaseInsensitiveKnownFieldsCannotOverrideDefaultResetButDynamicKeysStayDistinct()
    {
        using var fixture = new Fixture(); fixture.Put("""{"Config":{"Book":{"ispanorama":true,"Future":9,"future":8}},"Commands":{"ViewScaleUp":{"Parameter":{"scale":0.4,"Future":7}}}}""");
        var state = await fixture.Load(); Assert.True(Config.Current.Book.IsPanorama); Assert.Equal(.4, state.GetCommandParameter<ViewScaleCommandParameter>("ViewScaleUp").Scale);
        Config.Current.Book.IsPanorama = false; state.SetCommandParameter("ViewScaleUp", new ViewScaleCommandParameter()); await state.SaveAsync(null, Token);
        var saved = fixture.Get(); Assert.Null(saved["Config"]!["Book"]!["ispanorama"]); Assert.Null(saved["Commands"]!["ViewScaleUp"]!["Parameter"]!["scale"]);
        Assert.Equal(9, saved["Config"]!["Book"]!["Future"]!.GetValue<int>()); Assert.Equal(8, saved["Config"]!["Book"]!["future"]!.GetValue<int>());
        state = await fixture.Load(); Assert.False(Config.Current.Book.IsPanorama); Assert.Equal(.2, state.GetCommandParameter<ViewScaleCommandParameter>("ViewScaleUp").Scale);
    }

    [Fact]
    public async Task ClosedLayoutAndUnknownDockDataSurviveRestart()
    {
        using var fixture = new Fixture(); fixture.Put("""{"Config":{"Panels":{"Layout":{"Docks":{"Left":{"PanelLayoutV2":["Vertical:FolderPanel"],"SelectedItem":null,"Future":42},"Right":{"PanelLayoutV2":["Vertical:PageListPanel"],"SelectedItem":null}},"Future":9}}}}""");
        var state = await fixture.Load(); Config.Current.Panels.Layout = new LayoutPanelManager(Config.Current.Panels.Layout).CreateMemento(); await state.SaveAsync(null, Token);
        var layout = fixture.Get()["Config"]!["Panels"]!["Layout"]!; Assert.True(layout["Docks"]!["Left"]!.AsObject().ContainsKey("SelectedItem")); Assert.Null(layout["Docks"]!["Left"]!["SelectedItem"]);
        Assert.Equal(42, layout["Docks"]!["Left"]!["Future"]!.GetValue<int>()); Assert.Equal(9, layout["Future"]!.GetValue<int>());
        await fixture.Load(); var restored = new LayoutPanelManager(Config.Current.Panels.Layout); Assert.Null(restored.Docks["Left"].SelectedItem); Assert.Null(restored.Docks["Right"].SelectedItem);
    }

    [Fact]
    public async Task DestinationArrayUnknownFieldsSurviveTypedEditAndCopy()
    {
        using var fixture = new Fixture(); fixture.Put("""{"Config":{"System":{"DestinationFolderCollection":[{"Name":"A","Path":"/tmp/a","Future":{"Nested":99}}]}}}""");
        var state = await fixture.Load(); var destination = Config.Current.System.DestinationFolderCollection[0]; destination.Name = "B";
        Config.Current.System.DestinationFolderCollection[0] = (DestinationFolder)destination.Clone(); await state.SaveAsync(null, Token);
        var saved = fixture.Get()["Config"]!["System"]!["DestinationFolderCollection"]![0]!; Assert.Equal("B", saved["Name"]!.GetValue<string>()); Assert.Equal(99, saved["Future"]!["Nested"]!.GetValue<int>()); Assert.Null(saved["Extra"]);
        await fixture.Load(); Assert.Equal(99, Config.Current.System.DestinationFolderCollection[0].Extra!["Future"].GetProperty("Nested").GetInt32());
    }

    [Fact]
    public async Task WritesUsePreparedSnapshotNotUnsavedCurrentConfigAndKeepRetry()
    {
        using var fixture = new Fixture(); fixture.Put("""{"Config":{"History":{"LimitSize":13,"Future":9},"Book":{"IsPanorama":true}}}"""); var state = await fixture.Load();
        Config.Current.Book.IsPanorama = false; Config.Current.History.LimitSize = -1;
        await state.EditBookmarkSearchHistoryAsync("cat", token: Token);
        Assert.True(fixture.Get()["Config"]!["Book"]!["IsPanorama"]!.GetValue<bool>()); Assert.Equal(13, fixture.Get()["Config"]!["History"]!["LimitSize"]!.GetValue<int>());
        Directory.CreateDirectory(Path.Combine(fixture.Root, "UserSetting.json.tmp"));
        await Assert.ThrowsAnyAsync<Exception>(() => state.SaveAsync(null, Token));
        Assert.True(fixture.Get()["Config"]!["Book"]!["IsPanorama"]!.GetValue<bool>());
        Directory.Delete(Path.Combine(fixture.Root, "UserSetting.json.tmp"));
        await state.SaveAsync(null, Token, historyLimits: (31, TimeSpan.FromDays(2)));
        Assert.Null(fixture.Get()["Config"]?["Book"]); Assert.Equal(31, fixture.Get()["Config"]!["History"]!["LimitSize"]!.GetValue<int>());
        await fixture.Load(); Assert.False(Config.Current.Book.IsPanorama); Assert.Equal(31, Config.Current.History.LimitSize); Assert.Equal(TimeSpan.FromDays(2), Config.Current.History.LimitSpan);
    }

    [Fact]
    public async Task SavedDifferenceIsAcceptedByExistingImporterAndLeavesInspectableEvidence()
    {
        using var fixture = new Fixture(); var original = JsonNode.Parse("""{"Format":"NeeView.UserSetting/46.3.0","Config":{"Book":{"IsPanorama":true,"Future":9},"Panels":{"BannerItemProfile":{"ImageWidth":200,"Future":7}},"AutoHide":{"AutoHideHitTestMargin":17}},"Commands":{"NextScrollPage":{"Parameter":{"$type":"ScrollPage","IsNScroll":false,"PageMoveMargin":4,"Future":42}},"MoveToDestinationFolder9":{"Parameter":{"Index":9}}}}""")!.AsObject();
        fixture.Put(original.ToJsonString()); var state = await fixture.Load(); Config.Current.Book.IsPanorama = false; await state.SaveAsync(null, Token);
        var saved = fixture.Get(); var files = new Dictionary<string, string> { ["UserSetting.json"] = saved.ToJsonString() };
        var service = new ProfileImportService(new Reader(files), [], new HashSet<string>());
        var preview = await service.PreviewAsync(new("synthetic", ProfileImportSourceKind.Directory), [], Token);
        var request = preview.CreateRequest(new(true, false, false, false, false)); await state.ValidateProfileImportAsync(request, Token);
        state = await fixture.Load(); Assert.False(Config.Current.Book.IsPanorama); Assert.Equal(200, Config.Current.Panels.BannerItemProfile.ImageWidth);
        Assert.Equal(9, state.GetDestinationParameter("MoveToDestinationFolder9").Index); Assert.Equal(NScrollType.Diagonal, state.GetScrollParameter("NextScrollPage").ScrollType);
        var evidence = new JsonObject
        {
            ["scope"] = "isolated synthetic Profile; no desktop activation or Windows run",
            ["baseline"] = "c5c398d89 / build4340", ["source"] = original, ["savedDifference"] = saved,
            ["restored"] = new JsonObject { ["panorama"] = Config.Current.Book.IsPanorama, ["bannerWidth"] = Config.Current.Panels.BannerItemProfile.ImageWidth,
                ["digit9Index"] = state.GetDestinationParameter("MoveToDestinationFolder9").Index, ["scrollType"] = state.GetScrollParameter("NextScrollPage").ScrollType.ToString(),
                ["stopTime"] = state.GetScrollParameter("NextScrollPage").LineBreakStopTime, ["importValidation"] = true }
        };
        var output = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../acceptance/p5-difference-settings-evidence.json"));
        await File.WriteAllTextAsync(output, evidence.ToJsonString(new() { WriteIndented = true }) + "\n", Token);
    }
    private sealed class Reader(Dictionary<string, string> files) : IProfileImportReader
    {
        public Task<ProfileImportBundle> ReadAsync(ProfileImportSource source, CancellationToken token) => Task.FromResult(new ProfileImportBundle(files, []));
    }
}
