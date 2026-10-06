using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
namespace NeeView.Engine.Tests;

/// <summary>原主题算法和唯一 Profile 链路；仅操作隔离合成材料。</summary>
public sealed class ThemeTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;
    private static readonly SystemThemeState SystemDark = new(true, false, new(255, 17, 136, 221));
    private sealed class ThemeFixture : IDisposable
    {
        public string Root { get; } = Path.Combine(Path.GetTempPath(), "neeview-p5-theme-" + Guid.NewGuid().ToString("N"));
        public ThemeFixture() => Directory.CreateDirectory(Root);
        public string Put(string name, string text)
        { var path = Path.Combine(Root, name); Directory.CreateDirectory(Path.GetDirectoryName(path)!); File.WriteAllText(path, text, new UTF8Encoding(false)); return path; }
        public void Dispose() => Directory.Delete(Root, true);
    }
    /// <summary>原五个实际预设均可加载完整色表，System 是第六个选择而非另一个材料文件。</summary>
    [Theory]
    [InlineData(ThemeType.Dark, "#FF000000", true)]
    [InlineData(ThemeType.DarkMonochrome, "#FF000000", true)]
    [InlineData(ThemeType.Light, "#FFFFFFFF", false)]
    [InlineData(ThemeType.LightMonochrome, "#FFFFFFFF", false)]
    [InlineData(ThemeType.HighContrast, "#FF000000", true)]
    public async Task PresetsUseOriginalMaterialsAndCompleteKeys(ThemeType type, string background, bool dark)
    {
        var result = await new ThemeManager().LoadAsync(new(type), "", SystemDark, Token);
        Assert.Null(result.Error); Assert.Equal(type, result.EffectiveType); Assert.Equal(dark, result.IsDark);
        Assert.Equal(background, result.Profile.GetColor("Window.Background", 1).ToString());
        Assert.Equal(ThemeProfile.Keys.Count, result.Profile.Colors.Count);
        Assert.All(result.Profile.Colors.Values, color => Assert.Equal(ThemeColorType.Color, color.ThemeColorType));
    }
    /// <summary>原空值及Custom缺失文件名回Dark；带点文件名保留完整原标识。</summary>
    [Theory]
    [InlineData(null, "Dark")]
    [InlineData("", "Dark")]
    [InlineData("Custom", "Dark")]
    [InlineData("Custom.mine.v2.json", "Custom.mine.v2.json")]
    [InlineData("System", "System")]
    public void SourceWireFormatsAndDefaultEquality(string? input, string output)
    {
        var source = ThemeSource.Parse(input); Assert.Equal(output, source.ToString());
        Assert.Equal(source, JsonSerializer.Deserialize<ThemeSource>(JsonSerializer.Serialize(source)));
        Assert.Equal(source.GetHashCode(), ThemeSource.Parse(output).GetHashCode());
    }
    /// <summary>非法配置标识保持明确错误，不伪装成材料加载成功。</summary>
    [Theory]
    [InlineData("FutureTheme")]
    [InlineData("Custom.")]
    [InlineData("Dark.some.json")]
    [InlineData("999")]
    public void InvalidSourceIsRejected(string source) => Assert.ThrowsAny<ArgumentException>(() => ThemeSource.Parse(source));
    /// <summary>覆盖原十六进制、命名、透明及scRGB颜色令牌，保持ARGB输出。</summary>
    [Theory]
    [InlineData("#123456", "#FF123456")]
    [InlineData("#81234567", "#81234567")]
    [InlineData("#abc", "#FFAABBCC")]
    [InlineData("#8abc", "#88AABBCC")]
    [InlineData("Orange", "#FFFFA500")]
    [InlineData("Transparent", "#00FFFFFF")]
    [InlineData("sc#0,1,0.5", "#FF00FFBC")]
    [InlineData("sc#0.5,0,1,0.5", "#8000FFBC")]
    public void ColorTokensKeepOriginalRepresentation(string token, string expected)
    { Assert.Equal(expected, ThemeColor.Parse(token).ToString()); Assert.Equal(.5, ThemeColor.Parse(token + "/0.50").Opacity); }
    /// <summary>原空/default、递归引用、不透明度相乘及特殊默认色保持。</summary>
    [Fact]
    public void LinksOpacityAndRoleDefaultsKeepOriginalSemantics()
    {
        var profile = new ThemeProfile { Colors = new() { ["Window.Background"] = ThemeColor.Parse("#FF102030"),
            ["Window.Foreground"] = ThemeColor.Parse("#80ABCDEF"), ["Button.Foreground"] = ThemeColor.Parse("Window.Foreground/0.50"),
            ["IconButton.Foreground"] = ThemeColor.Parse("Button.Foreground/0.50") } };
        Assert.Equal("#20ABCDEF", profile.GetColor("IconButton.Foreground", 1).ToString());
        Assert.Equal("#FF102030", profile.GetColor("Panel.Border", 1).ToString());
        Assert.Equal("#FF1188DD", profile.GetColor("Control.Accent", 1).ToString());
        Assert.Equal(ThemeColorType.Default, ThemeColor.Parse("").ThemeColorType);
        Assert.Throws<FormatException>(() => profile.GetColor("NoSuch.Key", 1));
    }
    /// <summary>自定义三种父来源及覆盖优先级、UTF8 BOM读取保持，材料字节不被重写。</summary>
    [Theory]
    [InlineData("relative")]
    [InlineData("absolute")]
    [InlineData("embedded")]
    public async Task CustomInheritanceUsesOriginalLookupAndBom(string mode)
    {
        using var fixture = new ThemeFixture(); var parent = fixture.Put("parent.json", "{\"Colors\":{\"Window.Background\":\"#FF112233\"}}");
        var basedOn = mode switch { "relative" => "parent.json", "absolute" => parent, _ => "themes://LightTheme.json" };
        var path = fixture.Put("child.json", "\uFEFF" + JsonSerializer.Serialize(new { BasedOn = basedOn, Colors = new Dictionary<string, string> { ["Window.Foreground"] = "#FFAABBCC" } }));
        var bytes = File.ReadAllBytes(path); var result = await new ThemeManager().LoadAsync(new(ThemeType.Custom, "child.json"), fixture.Root, SystemDark, Token);
        Assert.Null(result.Error); Assert.Equal("#FFAABBCC", result.Profile.GetColor("Window.Foreground", 1).ToString());
        Assert.Equal(mode == "embedded" ? "#FFFFFFFF" : "#FF112233", result.Profile.GetColor("Window.Background", 1).ToString()); Assert.Equal(bytes, File.ReadAllBytes(path));
    }
    /// <summary>缺失/坏格式/颜色及继承循环/超限均沿原Dark回退，不修改Custom选择。</summary>
    [Theory]
    [InlineData("missing")]
    [InlineData("json")]
    [InlineData("null-colors")]
    [InlineData("bad-color")]
    [InlineData("parent-cycle")]
    [InlineData("color-cycle")]
    [InlineData("default-cycle")]
    [InlineData("deep")]
    [InlineData("large")]
    public async Task BadMaterialsFallbackAndLeaveSelection(string issue)
    {
        using var f = new ThemeFixture(); var text = issue switch
        {
            "json" => "{broken", "null-colors" => "{\"Colors\":null}", "bad-color" => "{\"Colors\":{\"Window.Background\":\"not-a-color\"}}",
            "parent-cycle" => "{\"BasedOn\":\"./a.json\",\"Colors\":{}}",
            "color-cycle" => "{\"Colors\":{\"Window.Background\":\"Window.Foreground\",\"Window.Foreground\":\"Window.Background\"}}",
            "default-cycle" => "{\"Colors\":{\"Window.Background\":\"Button.Border\",\"Button.Border\":\"\"}}",
            "deep" => "{\"BasedOn\":\"0.json\",\"Colors\":{}}", _ => "{}"
        };
        if (issue != "missing") f.Put("a.json", text);
        if (issue == "deep") for (var i = 0; i < 34; i++) f.Put(i + ".json", JsonSerializer.Serialize(new { BasedOn = (i + 1) + ".json", Colors = new Dictionary<string, string>() }));
        if (issue == "large") using (var stream = File.OpenWrite(Path.Combine(f.Root, "a.json"))) stream.SetLength(4 * 1024 * 1024 + 1);
        var source = new ThemeSource(ThemeType.Custom, "a.json"); var result = await new ThemeManager().LoadAsync(source, f.Root, SystemDark, Token);
        Assert.NotNull(result.Error); Assert.Equal(ThemeType.Dark, result.EffectiveType); Assert.Equal("#FF000000", result.Profile.GetColor("Window.Background", 1).ToString()); Assert.Equal("Custom.a.json", source.ToString());
    }
    /// <summary>系统选择按平台值快照走亮暗/高对比，accent覆盖不污染原预设。</summary>
    [Theory]
    [InlineData(true, false, ThemeType.Dark)]
    [InlineData(false, false, ThemeType.Light)]
    [InlineData(false, true, ThemeType.HighContrast)]
    public async Task SystemUsesPlatformSnapshot(bool dark, bool high, ThemeType expected)
    {
        var accent = new ThemeRgba(255, 9, 8, 7); var manager = new ThemeManager();
        var result = await manager.LoadAsync(new(ThemeType.System), "", new(dark, high, accent), Token);
        Assert.Null(result.Error); Assert.Equal(expected, result.EffectiveType);
        if (!high) Assert.Equal(accent, result.Profile.GetColor("Control.Accent", 1));
        else Assert.NotEqual(accent, result.Profile.GetColor("Control.Accent", 1));
        var original = await manager.LoadAsync(new(ThemeType.Light), "", new(dark, high, accent), Token); Assert.Equal("#FF1188DD", original.Profile.GetColor("Control.Accent", 1).ToString());
    }
    /// <summary>主题扫描只读一级JSON（含Windows扩展名大小写），缺目录不创建；取消不能回退成成功。</summary>
    [Fact]
    public async Task ScanAndCancellationAreReadOnly()
    {
        using var f = new ThemeFixture(); f.Put("one.json", "{}"); f.Put("two.JSON", "{}"); f.Put("text.txt", "{}"); f.Put("nested/three.json", "{}"); var manager = new ThemeManager();
        var result = await manager.CollectThemesAsync(f.Root, Token); Assert.Null(result.Error); Assert.Equal(8, result.Items.Count); Assert.DoesNotContain(result.Items, s => s.FileName == "three.json");
        var missing = Path.Combine(f.Root, "absent"); Assert.Equal(6, (await manager.CollectThemesAsync(missing, Token)).Items.Count); Assert.False(Directory.Exists(missing));
        using var canceled = new CancellationTokenSource(); canceled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => manager.CollectThemesAsync(f.Root, canceled.Token));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => manager.LoadAsync(new(ThemeType.Custom, "missing.json"), f.Root, SystemDark, canceled.Token));
    }
    /// <summary>经唯一SaveData读取/差分保存，现代字段优先，旧字段退役而未知数据保持。</summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ConfigAliasesAndDefaultDifferenceUseAuthoritativeJson(bool modern)
    {
        using var f = new ThemeFixture(); var theme = new JsonObject { ["PanelColor"] = "Light", ["FutureTheme"] = 7 };
        if (modern) theme["ThemeType"] = "Dark";
        f.Put("UserSetting.json", new JsonObject { ["Config"] = new JsonObject { ["Theme"] = theme } }.ToJsonString());
        var state = new SaveData(f.Root); await state.LoadAsync(Token); Assert.Equal(modern ? ThemeType.Dark : ThemeType.Light, Config.Current.Theme.ThemeType.Type);
        Assert.Equal(Path.Combine(f.Root, "Themes"), Config.Current.Theme.CustomThemeFolder);
        Config.Current.Theme.ThemeType = new(ThemeType.Dark); Config.Current.Theme.CustomThemeFolder = "  " + Path.Combine(f.Root, "Themes") + "  ";
        await state.SaveAsync(null, Token); var raw = JsonNode.Parse(File.ReadAllText(Path.Combine(f.Root, "UserSetting.json")))!;
        Assert.Equal(7, raw["Config"]!["Theme"]!["FutureTheme"]!.GetValue<int>()); Assert.Null(raw["Config"]!["Theme"]!["ThemeType"]); Assert.Null(raw["Config"]!["Theme"]!["PanelColor"]); Assert.Null(raw["Config"]!["Theme"]!["CustomThemeFolder"]);
    }
    /// <summary>主题编辑失败沿原设置快照恢复同一配置引用，重试后正确写回。</summary>
    [Fact]
    public async Task ThemeSaveFailureRestoresSameConfigAndCanRetry()
    {
        using var fixture = new Fixture(); var state = new SaveData(fixture.State); await state.LoadAsync(Token); await state.SaveAsync(null, Token);
        await using var operation = fixture.Operation(state); var before = Config.Current.Theme;
        var blocked = Path.Combine(fixture.State, "UserSetting.json.tmp"); Directory.CreateDirectory(blocked);
        try { await Assert.ThrowsAnyAsync<Exception>(() => operation.ApplyOptionsAsync(() => { before.ThemeType = new(ThemeType.Light); before.CustomThemeFolder = "/changed"; }, (-1, TimeSpan.Zero))); }
        finally { Directory.Delete(blocked); }
        Assert.Same(before, Config.Current.Theme); Assert.Equal(ThemeType.Dark, before.ThemeType.Type); Assert.Equal(Path.Combine(fixture.State, "Themes"), before.CustomThemeFolder);
        await operation.ApplyOptionsAsync(() => before.ThemeType = new(ThemeType.Light), (-1, TimeSpan.Zero));
        await new SaveData(fixture.State).LoadAsync(Token); Assert.Equal(ThemeType.Light, Config.Current.Theme.ThemeType.Type);
    }
    private sealed class Reader(ProfileImportBundle bundle) : IProfileImportReader
    { public Task<ProfileImportBundle> ReadAsync(ProfileImportSource source, CancellationToken token) => Task.FromResult(bundle); }
    private static async Task<ProfileImportPreview> Preview(string setting, Dictionary<string, byte[]> assets, params ProfilePathMapping[] maps)
    {
        using var stream = typeof(CommandTable).Assembly.GetManifestResourceStream("NeeView.Command.command-manifest.json")!;
        var defs = JsonSerializer.Deserialize<List<CommandDefinition>>(stream)!;
        return await new ProfileImportService(new Reader(new(new Dictionary<string, string> { ["UserSetting.json"] = setting }, [], assets)), defs, new HashSet<string>()).PreviewAsync(new("synthetic", ProfileImportSourceKind.Directory), maps, Token);
    }
    /// <summary>设置和同包主题一起导入后绑定Mac落点；只选材料保持原配置字节，备份恢复两者。</summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ThemeImportRespectsSelectionAndActualNames(bool settings)
    {
        using var f = new ThemeFixture(); f.Put("UserSetting.json", "{\"Config\":{\"Theme\":{\"ThemeType\":\"Light\"}},\"FutureRoot\":9}"); var before = File.ReadAllBytes(Path.Combine(f.Root, "UserSetting.json"));
        var source = "{\"Format\":\"NeeView.UserSetting/46.3.0\",\"Config\":{\"Theme\":{\"ThemeType\":\"Custom.sample.json\",\"CustomThemeFolder\":\"P:\\\\Themes\",\"FutureTheme\":7}}}";
        var preview = await Preview(source, new() { ["Themes/Sample.JSON"] = Encoding.UTF8.GetBytes("{\"BasedOn\":\"themes://LightTheme.json\",\"Colors\":{}}") });
        var state = new SaveData(f.Root); var result = await state.ApplyProfileImportAsync(preview.CreateRequest(new(settings, false, false, false, false, false, true)), Token);
        if (settings)
        {
            await state.LoadAsync(Token); Assert.Equal("Custom.Sample.JSON", Config.Current.Theme.ThemeString); Assert.Equal(Path.Combine(f.Root, "Themes"), Config.Current.Theme.CustomThemeFolder);
            Assert.Null((await new ThemeManager().LoadAsync(Config.Current.Theme.ThemeType, Config.Current.Theme.CustomThemeFolder, SystemDark, Token)).Error);
        }
        else Assert.Equal(before, File.ReadAllBytes(Path.Combine(f.Root, "UserSetting.json")));
        await state.RestoreProfileImportAsync(result.BackupDirectory, Token); Assert.Equal(before, File.ReadAllBytes(Path.Combine(f.Root, "UserSetting.json"))); Assert.False(File.Exists(Path.Combine(f.Root, "Themes/Sample.JSON")));
    }
    /// <summary>未选主题文件时仅执行显式Windows目录映射，未映射值继续报告和保留。</summary>
    [Fact]
    public async Task CustomFolderMappingUsesExistingLongestPrefixMapper()
    {
        var setting = "{\"Format\":\"NeeView.UserSetting/46.3.0\",\"Config\":{\"Theme\":{\"CustomThemeFolder\":\"P:\\\\Themes\"}}}";
        var mapped = await Preview(setting, new(), new ProfilePathMapping(@"P:\", "/Volumes/Picture"));
        Assert.Contains(mapped.Paths, p => p.Field == "Config.Theme.CustomThemeFolder" && p.Status == ProfilePathStatus.Mapped);
        Assert.Equal("/Volumes/Picture/Themes", mapped.GetDocument("UserSetting.json")!["Config"]!["Theme"]!["CustomThemeFolder"]!.GetValue<string>());
        var unmapped = await Preview(setting, new()); Assert.Contains(unmapped.Paths, p => p.Status == ProfilePathStatus.Unmapped);
    }
}
