using System.Text.Json;
using System.Text.Json.Nodes;
namespace NeeView.Engine.Tests;

/// <summary>原 Alpha.5 纯数据升级；合成 JSON 不证明效果执行、真实导出或 Windows 动态一致。</summary>
public sealed class ProfileEffectUpgradeTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;
    private static JsonObject Parse(string text) => JsonNode.Parse(text)!.AsObject();
    private static JsonObject Setting(string effect, string version = "46.0.4209") => Parse("{\"Format\":\"NeeView/" + version + "\",\"Config\":{\"ImageEffect\":" + effect + "}}");
    private static JsonObject Upgrade(JsonObject raw) => ProfileImportCompatibility.Upgrade("UserSetting.json", raw.DeepClone().AsObject());
    private static JsonNode? CurrentEffect(JsonObject raw) => raw["Config"]!["ImageEffect"]!["Layers"]![0]!["Effect"];

    [Theory]
    [InlineData("Level", """{"Black":0,"White":1,"Center":0.5,"Minimum":0,"Maximum":1}""")]
    [InlineData("Hsv", """{"Hue":0,"Saturation":0,"Value":0}""")]
    [InlineData("ColorSelect", """{"Hue":15,"Range":0.1,"Curve":0.1}""")]
    [InlineData("Blur", """{"Radius":5}""")]
    [InlineData("Bloom", """{"BaseIntensity":1,"BaseSaturation":1,"BloomIntensity":1.25,"BloomSaturation":1,"Threshold":0.25}""")]
    [InlineData("Monochrome", """{"Color":"White"}""")]
    [InlineData("ColorTone", """{"DarkColor":"#ff338000","LightColor":"#ffffe580","ToneAmount":0.5,"Desaturation":0.5}""")]
    [InlineData("Sharpen", """{"Amount":2,"Height":0.5}""")]
    [InlineData("Embossed", """{"Color":"Gray","Amount":3,"Height":1}""")]
    [InlineData("Pixelate", """{"Pixelation":0.75}""")]
    [InlineData("Magnify", """{"Center":"0.5 0.5","Amount":0.5,"InnerRadius":0.2,"OuterRadius":0.4}""")]
    [InlineData("Ripple", """{"Center":"0.5, 0.5","Frequency":40,"Magnitude":0.1,"Phase":10}""")]
    [InlineData("Swirl", """{"Center":"0.5,0.5","TwistAmount":10}""")]
    public void OriginalDefaultsSelectKnownTypeWithoutOccupyingCache(string type, string body)
    {
        var raw = Upgrade(Setting("{\"EffectType\":\"" + type + "\",\"" + type + "Effect\":" + body + "}"));
        Assert.Equal(type, CurrentEffect(raw)!["$type"]!.GetValue<string>());
        Assert.Single(CurrentEffect(raw)!.AsObject());
        Assert.Empty(raw["Config"]!["ImageEffectCache"]!.AsArray());
        Assert.False(raw["Config"]!["ImageEffect"]!["IsEnabled"]!.GetValue<bool>());
        Assert.True(raw["Config"]!["ImageEffect"]!["Layers"]![0]!["IsEnabled"]!.GetValue<bool>());
    }

    [Theory]
    [InlineData(0, null)] [InlineData(1, "Level")] [InlineData(2, "Hsv")] [InlineData(3, "ColorSelect")]
    [InlineData(4, "Colorize")] [InlineData(5, "Blur")] [InlineData(6, "Bloom")] [InlineData(7, "Monochrome")]
    [InlineData(8, "ColorTone")] [InlineData(9, "Sharpen")] [InlineData(10, "Embossed")] [InlineData(11, "Pixelate")]
    [InlineData(12, "Magnify")] [InlineData(13, "Ripple")] [InlineData(14, "Swirl")]
    public void OriginalEnumOrderIncludingColorizeIsPreserved(int number, string? type)
    {
        var raw = Upgrade(Setting("{\"IsEnabled\":true,\"EffectType\":" + number + "}"));
        Assert.Equal(type, CurrentEffect(raw)?["$type"]?.GetValue<string>());
        Assert.True(raw["Config"]!["ImageEffect"]!["IsEnabled"]!.GetValue<bool>());
    }

    [Fact]
    public void LevelRawEndpointsDoNotRoundOrMoveCenterAndBloomOnlyClampsUpperBound()
    {
        var raw = Upgrade(Setting("""{"EffectType":"Level","LevelEffect":{"Black":0.12345678,"White":0.98765432,"Center":0.23456789},"BloomEffect":{"Threshold":2.1,"BaseIntensity":-1.23456789},"HsvEffect":{"Hue":721.12345678}}"""));
        var effect = CurrentEffect(raw)!;
        Assert.Equal(0.12345678, effect["Black"]!.GetValue<double>());
        Assert.Equal(0.98765432, effect["White"]!.GetValue<double>());
        Assert.Equal(0.23457, effect["Center"]!.GetValue<double>());
        var bloom = raw["Config"]!["ImageEffectCache"]!.AsArray().Single(n => n!["$type"]!.GetValue<string>() == "Bloom")!;
        Assert.Equal(1, bloom["Threshold"]!.GetValue<double>()); Assert.Equal(-1.23457, bloom["BaseIntensity"]!.GetValue<double>());
        var negative = Upgrade(Setting("""{"EffectType":"Bloom","BloomEffect":{"Threshold":-2.12345678}}"""));
        Assert.Equal(-2.12346, CurrentEffect(negative)!["Threshold"]!.GetValue<double>());
        Assert.Equal(721.12346, raw["Config"]!["ImageEffectCache"]!.AsArray().Single(n => n!["$type"]!.GetValue<string>() == "Hsv")!["Hue"]!.GetValue<double>());
    }

    [Theory]
    [InlineData("#123", "#FF112233")] [InlineData("#8123", "#88112233")]
    [InlineData("#123456", "#FF123456")] [InlineData("#a0123456", "#A0123456")]
    public void OriginalColorStringFormsBecomeArgbWithoutUiTypes(string color, string expected)
    {
        var raw = Upgrade(Setting("{\"EffectType\":\"Monochrome\",\"MonochromeEffect\":{\"Color\":\"" + color + "\"}}"));
        Assert.Equal(expected, CurrentEffect(raw)!["Color"]!.GetValue<string>());
    }

    [Fact]
    public void PointCoordinatesRoundSeparatelyAndUnknownNestedDataSurvives()
    {
        var source = Setting("""{"EffectType":"Magnify","MagnifyEffect":{"Center":"0.123456789,0.87654321","Future":{"Values":[null,42]}},"FutureRoot":7}""");
        var raw = Upgrade(source);
        Assert.Equal("0.12346,0.87654", CurrentEffect(raw)!["Center"]!.GetValue<string>());
        Assert.Equal(42, CurrentEffect(raw)!["Future"]!["Values"]![1]!.GetValue<int>());
        Assert.Equal(7, raw["Config"]!["ImageEffect"]!["FutureRoot"]!.GetValue<int>());
        Assert.True(JsonNode.DeepEquals(source["Config"]!["ImageEffect"], raw["MacImportedLegacyImageEffects"]!["ImageEffect"]));
        Assert.DoesNotContain("Layers", source["Config"]!["ImageEffect"]!.AsObject().Select(p => p.Key));
        Assert.Equal("$type", CurrentEffect(raw)!.AsObject().First().Key);
    }

    [Fact]
    public void CacheUsesInputOrderNonDefaultReplacementAndKeepsFutureEntries()
    {
        var source = Setting("""{"EffectType":"Hsv","BlurEffect":{"Radius":7},"HsvEffect":{"Hue":90},"SharpenEffect":{"Amount":2}}""");
        source["Config"]!["ImageEffectCache"] = JsonNode.Parse("""[{"$type":"Hsv","Hue":1},{"$type":"Future","Metadata":42},{"$type":"Hsv","Hue":2},{"$type":"Sharpen","Amount":3}]""");
        var raw = Upgrade(source); var cache = raw["Config"]!["ImageEffectCache"]!.AsArray();
        Assert.Equal(new[] { "Hsv", "Future", "Sharpen", "Blur" }, cache.Select(n => n!["$type"]!.GetValue<string>()));
        Assert.Equal(90, cache[0]!["Hue"]!.GetValue<double>());
        Assert.Equal(42, cache[1]!["Metadata"]!.GetValue<int>());
        Assert.Equal(3, cache[2]!["Amount"]!.GetValue<int>()); // 默认旧参数未进入本地缓存，不清除已有全局缓存。
        Assert.Equal(7, cache[3]!["Radius"]!.GetValue<double>());
    }

    [Fact]
    public void ProfileSnapshotsAllSixOriginalBranchesAndRetiresOldProfiles()
    {
        var source = Setting("""{"IsEnabled":false,"EffectType":"Sharpen","SharpenEffect":{"Height":1.2},"Layers":[{"Effect":{"$type":"Blur","Radius":99}}]}""");
        foreach (var branch in new[] { "ImageCustomSize", "ImageTrim", "ImageDotKeep", "ImageResizeFilter", "ImageGrid" })
            source["Config"]![branch] = Parse("""{"IsEnabled":true,"Future":42}""");
        source["Config"]!["EffectProfiles"] = Parse("""{"IdCounter":9,"Profiles":[{"Id":7,"Name":"old","Future":8}],"Future":6}""");
        var raw = Upgrade(source); var profiles = raw["Config"]!["EffectProfiles"]!;
        Assert.Equal(9, profiles["IdCounter"]!.GetValue<int>()); Assert.Equal(6, profiles["Future"]!.GetValue<int>());
        var profile = Assert.Single(profiles["Profiles"]!.AsArray())!;
        Assert.Equal(0, profile["Id"]!.GetValue<int>()); Assert.Equal("", profile["Name"]!.GetValue<string>());
        foreach (var branch in new[] { "ImageCustomSize", "ImageTrim", "ImageDotKeep", "ImageResizeFilter", "ImageGrid", "ImageEffect" })
            Assert.True(JsonNode.DeepEquals(raw["Config"]![branch], profile[branch]));
        Assert.Equal(7, raw["MacImportedLegacyImageEffects"]!["EffectProfiles"]!["Profiles"]![0]!["Id"]!.GetValue<int>());
        Assert.Equal("Sharpen", CurrentEffect(raw)!["$type"]!.GetValue<string>()); // 旧版本 validator 覆盖旧 Layers，原材料归档。
    }

    [Theory]
    [InlineData("46.0.4209", true)] [InlineData("46.0.4210", false)]
    public void MissingLegacyBranchUsesOriginalNoneDefaultOnlyAtInclusiveBoundary(string version, bool converted)
    {
        var source = Parse("{\"Format\":\"NeeView/" + version + "\",\"Config\":{}}");
        var raw = Upgrade(source);
        Assert.Equal(converted, raw["MacImportedLegacyEffectUpgrade"] is not null);
        if (converted) { Assert.Null(CurrentEffect(raw)); Assert.Single(raw["Config"]!["EffectProfiles"]!["Profiles"]!.AsArray()); }
        else Assert.Null(raw["Config"]!["ImageEffect"]);
    }

    [Theory]
    [InlineData("""{"EffectType":"Future","HsvEffect":{"Hue":90}}""")]
    [InlineData("""{"EffectType":99}""")]
    [InlineData("""{"EffectType":"Monochrome","MonochromeEffect":{"Color":"sc#1,0.5,0.5,0.5"}}""")]
    [InlineData("""{"EffectType":"Magnify","MagnifyEffect":{"Center":"NaN,0.5"}}""")]
    [InlineData("""{"EffectType":"Hsv","HsvEffect":{"$type":"Future","Hue":90}}""")]
    public void UnknownLegacyTypeOrUnverifiedValuePreservesWholeEffectWithoutPartialUpgrade(string effect)
    {
        var source = Setting(effect); var raw = Upgrade(source);
        Assert.True(JsonNode.DeepEquals(source["Config"]!["ImageEffect"], raw["Config"]!["ImageEffect"]));
        Assert.NotNull(raw["MacImportedLegacyEffectIssue"]); Assert.Null(raw["MacImportedLegacyEffectUpgrade"]);
        Assert.Null(raw["Config"]!["EffectProfiles"]); Assert.Null(raw["Config"]!["ImageEffectCache"]);
    }

    [Theory]
    [InlineData("""{"EffectType":"Blur","BlurEffect":42}""")]
    [InlineData("""{"EffectType":"Hsv","HsvEffect":{"Hue":"bad"}}""")]
    [InlineData("""{"EffectType":"None","IsEnabled":"bad"}""")]
    [InlineData("""{"EffectType":null}""")]
    [InlineData("""{"IsEnabled":null}""")]
    public async Task MalformedKnownDataIsRejectedDuringReadonlyPreview(string effect)
    {
        var source = Setting(effect); var before = source.ToJsonString();
        await Assert.ThrowsAsync<InvalidDataException>(() => Preview(source));
        Assert.Equal(before, source.ToJsonString());
    }

    [Fact]
    public void ModernFutureTypesAndLayerOrderAreNotReinterpreted()
    {
        var source = Setting("""{"IsEnabled":true,"Layers":[{"IsEnabled":false,"Effect":{"$type":"Future","Values":[1,2]}},{"Effect":{"$type":"Hsv","Hue":17}}]}""", "46.3.0");
        source["Config"]!["EffectProfiles"] = Parse("""{"Profiles":[{"Id":5,"Name":"future","Future":null}]}""");
        Assert.True(JsonNode.DeepEquals(source, Upgrade(source)));
    }

    [Fact]
    public void EarlyMacPreservedMaterialContinuesOnceAndNeverOverwritesEditedLayers()
    {
        var source = Setting("""{"EffectType":"Blur","BlurEffect":{"Radius":7}}""", "46.3.0");
        source["MacImportedLegacyEffectFormat"] = "NeeView/46.0.4209";
        var raw = Upgrade(source); Assert.Equal(7, CurrentEffect(raw)!["Radius"]!.GetValue<double>());
        CurrentEffect(raw)!["Radius"] = 11;
        Assert.True(JsonNode.DeepEquals(raw, Upgrade(raw)));
        source["MacImportedLegacyEffectUpgrade"] = "Layers/2";
        Assert.True(JsonNode.DeepEquals(source, Upgrade(source))); // 未来升级契约不能被本版降级。
    }

    [Fact]
    public void LastCaseInsensitiveSetterRestoringDefaultRemovesEarlierCachedDifference()
    {
        var raw = Upgrade(Setting("""{"EffectType":"Hsv","HsvEffect":{"Hue":90,"hue":0},"SharpenEffect":{"Amount":7},"sharpeneffect":{"Amount":2}}"""));
        Assert.Single(CurrentEffect(raw)!.AsObject()); Assert.Empty(raw["Config"]!["ImageEffectCache"]!.AsArray());
    }

    [Fact]
    public void NullLegacySetterDoesNotClearEarlierDifferenceAndDefaultSelectionStillWorks()
    {
        var raw = Upgrade(Setting("""{"EffectType":"Hsv","HsvEffect":{"Hue":90},"hsveffect":null,"BlurEffect":null}"""));
        Assert.Equal(90, CurrentEffect(raw)!["Hue"]!.GetValue<double>());
        Assert.Single(raw["Config"]!["ImageEffectCache"]!.AsArray());
    }

    [Fact]
    public async Task OldPreservationMarkerCannotOverrideModernUserLayers()
    {
        var source = Setting("""{"Layers":[{"Effect":{"$type":"Future","X":42}}],"EffectType":"Blur","BlurEffect":{"Radius":7}}""", "46.3.0");
        source["MacImportedLegacyEffectFormat"] = "NeeView/46.0.4209";
        Assert.True(JsonNode.DeepEquals(source, Upgrade(source)));
        Assert.Contains((await Preview(source)).Notices, n => n.Contains("现代效果层") && n.Contains("Level/Hsv/ColorSelect/Colorize 已接入"));
    }

    [Fact]
    public async Task PreviewApplySaveReloadAndReimportPreserveConvertedAndOriginalMaterial()
    {
        var source = Setting("""{"IsEnabled":true,"EffectType":"Level","LevelEffect":{"Black":0.12345678,"Center":0.23456789,"Future":{"X":42}},"BlurEffect":{"Radius":7}}""");
        source["Config"]!["ImageGrid"] = Parse("""{"DivX":11,"Future":8}""");
        var preview = await Preview(source); var candidate = preview.GetDocument("UserSetting.json")!;
        Assert.Contains(preview.Notices, n => n.Contains("已按原规则转换") && n.Contains("Level/Hsv/ColorSelect/Colorize 已接入"));
        var root = NewRoot();
        try
        {
            var state = new SaveData(root); await state.LoadAsync(Token);
            var request = preview.CreateRequest(new()); var config = Config.Current;
            await state.ValidateProfileImportAsync(request, Token); Assert.Same(config, Config.Current);
            var result = await state.ApplyProfileImportAsync(request, Token);
            var loaded = new SaveData(root); await loaded.LoadAsync(Token); Config.Current.Panels.LeftWidth = 321;
            await loaded.SaveAsync(null, Token);
            var saved = Parse(await File.ReadAllTextAsync(Path.Combine(root, "UserSetting.json"), Token));
            // 默认差分会改变 JSON 形状；比较重新加载后的有效值，原材料仍须逐节点不变。
            await loaded.LoadAsync(Token);
            var readOptions = new JsonSerializerOptions(); readOptions.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter());
            foreach (var branch in new[] { "ImageCustomSize", "ImageTrim", "ImageDotKeep", "ImageResizeFilter", "ImageGrid", "ImageEffect", "ImageEffectCache" })
            {
                var property = typeof(Config).GetProperty(branch)!;
                var expected = candidate["Config"]![branch]?.Deserialize(property.PropertyType, readOptions) ?? property.GetValue(new Config());
                Assert.True(JsonNode.DeepEquals(JsonSerializer.SerializeToNode(expected, property.PropertyType),
                    JsonSerializer.SerializeToNode(property.GetValue(Config.Current), property.PropertyType)), branch);
            }
            var profile = new EffectProfileCollection(Config.Current).SelectedProfile;
            foreach (var branch in new[] { "ImageCustomSize", "ImageTrim", "ImageDotKeep", "ImageResizeFilter", "ImageGrid", "ImageEffect" })
                Assert.True(JsonNode.DeepEquals(JsonSerializer.SerializeToNode(typeof(Config).GetProperty(branch)!.GetValue(Config.Current)),
                    JsonSerializer.SerializeToNode(typeof(EffectProfile).GetProperty(branch)!.GetValue(profile))), "选中预设同步：" + branch);
            Assert.True(JsonNode.DeepEquals(candidate["MacImportedLegacyImageEffects"], saved["MacImportedLegacyImageEffects"]));
            Assert.True(JsonNode.DeepEquals(saved, (await Preview(saved)).GetDocument("UserSetting.json")));
            Assert.NotNull((await Preview(saved)).CreateRequest(new()));
            await loaded.ApplyProfileImportAsync((await Preview(source)).CreateRequest(new()), Token);
            saved = Parse(await File.ReadAllTextAsync(Path.Combine(root, "UserSetting.json"), Token));
            Assert.Equal(2, saved["Config"]!["ImageEffectCache"]!.AsArray().Count);
            Assert.True(File.Exists(Path.Combine(result.BackupDirectory, "manifest.json")));
            if (System.Environment.GetEnvironmentVariable("NEEVIEW_P5_EFFECT_ARTIFACT") is { } artifact)
                await File.WriteAllTextAsync(artifact, JsonSerializer.Serialize(new { source, candidate, saved, notices = preview.Notices, scope = "合成效果数据；预览/事务/普通差分保存，非效果执行或Windows动态" }, new JsonSerializerOptions { WriteIndented = true }), Token);
        }
        finally { Directory.Delete(root, true); }
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task ImportRestoresSourceDefaultsInsteadOfMergingStaleCurrentEffects(bool sourceHasEffects)
    {
        var root = NewRoot();
        try
        {
            var current = Setting("""{"Layers":[{"Effect":{"$type":"Blur","Radius":99}}],"FutureOld":7}""", "46.3.0");
            current["Config"]!["EffectProfiles"] = Parse("""{"Profiles":[{"Id":9}],"FutureOld":8}""");
            current["Config"]!["ImageEffectCache"] = JsonNode.Parse("""[{"$type":"Hsv","Hue":90}]""");
            current["MacImportedLegacyEffectFormat"] = "NeeView/46.0.4209";
            current["MacImportedLegacyEffectUpgrade"] = "Layers/1";
            current["MacImportedLegacyImageEffects"] = Parse("""{"ImageEffect":{"EffectType":"Blur"}}""");
            current["MacImportedLegacyEffectIssue"] = "old issue";
            await File.WriteAllTextAsync(Path.Combine(root, "UserSetting.json"), current.ToJsonString(), Token);
            var source = Parse("""{"Format":"NeeView/46.3.0","Config":{}}""");
            if (sourceHasEffects) source["Config"]!["ImageEffect"] = Parse("""{"Layers":[{"Effect":{"$type":"Future","X":42}}]}""");
            await new SaveData(root).ApplyProfileImportAsync((await Preview(source)).CreateRequest(new()), Token);
            var saved = Parse(await File.ReadAllTextAsync(Path.Combine(root, "UserSetting.json"), Token));
            Assert.True(JsonNode.DeepEquals(source["Config"]!["ImageEffect"], saved["Config"]!["ImageEffect"]));
            Assert.Null(saved["Config"]!["ImageEffectCache"]); Assert.Null(saved["Config"]!["EffectProfiles"]);
            foreach (var field in new[] { "MacImportedLegacyEffectFormat", "MacImportedLegacyEffectUpgrade", "MacImportedLegacyImageEffects", "MacImportedLegacyEffectIssue" }) Assert.Null(saved[field]);
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public async Task UnknownSourceCannotInheritCurrentSuccessfulConversionMarker()
    {
        var root = NewRoot();
        try
        {
            var current = Upgrade(Setting("""{"EffectType":"Blur","BlurEffect":{"Radius":7}}"""));
            await File.WriteAllTextAsync(Path.Combine(root, "UserSetting.json"), current.ToJsonString(), Token);
            var source = Setting("""{"EffectType":99,"Future":42}""");
            await new SaveData(root).ApplyProfileImportAsync((await Preview(source)).CreateRequest(new()), Token);
            var saved = Parse(await File.ReadAllTextAsync(Path.Combine(root, "UserSetting.json"), Token));
            Assert.Null(saved["MacImportedLegacyEffectUpgrade"]); Assert.Null(saved["MacImportedLegacyImageEffects"]);
            Assert.NotNull(saved["MacImportedLegacyEffectIssue"]);
            Assert.Contains((await Preview(saved)).Notices, n => n.Contains("尚未转换") && n.Contains("Level/Hsv/ColorSelect/Colorize 已接入"));
            Assert.True(JsonNode.DeepEquals(source["Config"]!["ImageEffect"], saved["Config"]!["ImageEffect"]));
        }
        finally { Directory.Delete(root, true); }
    }

    private static string NewRoot()
    { var root = Path.Combine(Path.GetTempPath(), "NeeView-P5-Effect-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root); return root; }
    private sealed class Reader(string text) : IProfileImportReader
    { public Task<ProfileImportBundle> ReadAsync(ProfileImportSource source, CancellationToken token) => Task.FromResult(new ProfileImportBundle(new Dictionary<string, string> { ["UserSetting.json"] = text }, [])); }
    private static Task<ProfileImportPreview> Preview(JsonObject source)
    {
        using var stream = typeof(CommandTable).Assembly.GetManifestResourceStream("NeeView.Command.command-manifest.json")!;
        return new ProfileImportService(new Reader(source.ToJsonString()), JsonSerializer.Deserialize<List<CommandDefinition>>(stream)!, new HashSet<string>()).PreviewAsync(new("synthetic", ProfileImportSourceKind.Directory), [], Token);
    }
}
