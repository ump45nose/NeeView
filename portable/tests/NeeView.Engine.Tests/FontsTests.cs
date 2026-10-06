using System.Text.Json;
using System.Text.Json.Nodes;

namespace NeeView.Engine.Tests;

/// <summary>字体配置、纯值尺寸计算及 UserSetting 字段迁移的回归边界。</summary>
public sealed class FontsTests
{
    [Fact]
    public void DefaultsAndScaleAccessorsKeepOriginalSemantics()
    {
        var fonts = new FontsConfig { DefaultFontName = "System UI" };
        Assert.Equal(1.25, fonts.FontScale); Assert.Equal(1, fonts.MenuFontScale);
        Assert.Equal(1, fonts.FolderTreeFontScale); Assert.Equal(1.25, fonts.PanelFontScale);
        Assert.True(fonts.IsClearTypeEnabled); Assert.Equal("System UI", fonts.FontName);
        fonts.FontScale = 1.234567; fonts.MenuFontScale = 2.5; fonts.FolderTreeFontScale = 3; fonts.PanelFontScale = 0;
        Assert.Equal(1.23457, fonts.FontScale); Assert.Equal(2.5, fonts.MenuFontScale);
        Assert.Equal(3, fonts.FolderTreeFontScale); Assert.Equal(1.25, fonts.PanelFontScale);
        fonts.FontScale = -2; fonts.MenuFontScale = 0; fonts.FolderTreeFontScale = -1;
        Assert.Equal(1.25, fonts.FontScale); Assert.Equal(1, fonts.MenuFontScale); Assert.Equal(1, fonts.FolderTreeFontScale);
        fonts.FontScale = 9; Assert.Equal(9, fonts.FontScale);
        fonts.FontName = "System UI"; Assert.Null(fonts.FontNameRaw);
        fonts.FontName = "Noto Sans"; Assert.Equal("Noto Sans", fonts.FontNameRaw); Assert.Equal("Noto Sans", fonts.FontName);
        fonts.FontName = " "; Assert.Null(fonts.FontNameRaw); Assert.Equal("System UI", fonts.FontName);
        var json = JsonSerializer.Serialize(fonts); Assert.DoesNotContain("DefaultFontName", json); Assert.Contains("\"FontScale\":9", json);
        Assert.DoesNotContain("Noto Sans", json);
    }

    [Fact]
    public void CalculateUsesMessageAndMenuBaselinesAndCapsOnlySystemSizes()
    {
        var config = new FontsConfig { FontScale = 2.5, MenuFontScale = 1.5, FolderTreeFontScale = 1.25, PanelFontScale = 3 };
        var p = FontParameters.Calculate(config, new FontEnvironment("System", 10, 14));
        Assert.Equal(new FontParameters(10, 25, 21, 12.5, 30, 40), p);
        Assert.Equal(12.5, p.SystemFontSizeNormal); Assert.Equal(15, p.SystemFontSizeLarge); Assert.Equal(20, p.SystemFontSizeHuge);
        var large = FontParameters.Calculate(new FontsConfig(), new FontEnvironment("System", 30, 20));
        Assert.Equal(30, large.SystemFontSizeNormal); Assert.Equal(30, large.SystemFontSizeLarge); Assert.Equal(30, large.SystemFontSizeHuge);
        var tiny = FontParameters.Calculate(new FontsConfig(), new FontEnvironment("System", 1, 1));
        Assert.Equal(28, tiny.FontIconSize);
    }

    [Theory]
    [InlineData(double.NaN, 10)]
    [InlineData(double.PositiveInfinity, 10)]
    [InlineData(10, double.NaN)]
    [InlineData(10, double.PositiveInfinity)]
    [InlineData(10, -1)]
    public void CalculateRejectsInvalidPlatformMetrics(double message, double menu)
        => Assert.Throws<ArgumentOutOfRangeException>(() => FontParameters.Calculate(new FontsConfig(), new FontEnvironment("System", message, menu)));

    [Fact]
    public async Task LoadMergesFontsBranchDefaultsAndPreservesUnknownJson()
    {
        var root = Path.Combine(Path.GetTempPath(), "neeview-fonts-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
        try
        {
            await File.WriteAllTextAsync(Path.Combine(root, "UserSetting.json"), """{"Config":{"Fonts":{"FontScale":1.234567,"MenuFontScale":0,"FontName":"Noto Sans","Future":42},"FutureBranch":{"x":1}},"Future":9}""", TestContext.Current.CancellationToken);
            var save = new SaveData(root); await save.LoadAsync(TestContext.Current.CancellationToken);
            Assert.Equal(1.23457, Config.Current.Fonts.FontScale); Assert.Equal(1, Config.Current.Fonts.MenuFontScale);
            Assert.Equal("Noto Sans", Config.Current.Fonts.FontNameRaw); Assert.Equal("Noto Sans", Config.Current.Fonts.FontName);
            Assert.Equal(1.25, Config.Current.Fonts.PanelFontScale); Assert.True(Config.Current.Fonts.IsClearTypeEnabled);
            Config.Current.Fonts.FontScale = 1.25; Config.Current.Fonts.FontName = "";
            await save.SaveAsync(null, TestContext.Current.CancellationToken);
            var raw = JsonNode.Parse(await File.ReadAllTextAsync(Path.Combine(root, "UserSetting.json"), TestContext.Current.CancellationToken))!;
            Assert.Equal(42, raw["Config"]!["Fonts"]!["Future"]!.GetValue<int>()); Assert.Equal(9, raw["Future"]!.GetValue<int>());
            Assert.Equal(1, raw["Config"]!["FutureBranch"]!["x"]!.GetValue<int>());
            Assert.Null(raw["Config"]!["Fonts"]!["FontScale"]); Assert.Null(raw["Config"]!["Fonts"]!["FontName"]);
        }
        finally { Directory.Delete(root, true); }
    }

    /// <summary>字体保存失败保持原分支/运行默认/Windows偏好，重试及重启沿唯一JSON。</summary>
    [Fact]
    public async Task FailedSaveRollsBackFontsInPlaceAndRetryPersists()
    {
        using var f = new Fixture(); var state = new SaveData(f.State); await state.LoadAsync(TestContext.Current.CancellationToken); await state.SaveAsync(null, TestContext.Current.CancellationToken);
        await using var operation = f.Operation(state); var fonts = Config.Current.Fonts; fonts.DefaultFontName = "System";
        var blocked = Path.Combine(f.State, "UserSetting.json.tmp"); Directory.CreateDirectory(blocked);
        try
        {
            await Assert.ThrowsAnyAsync<Exception>(() => operation.ApplyOptionsAsync(() => { fonts.FontName = "Legacy Font"; fonts.FontScale = 1.75; fonts.IsClearTypeEnabled = false; }, (-1, TimeSpan.Zero)));
            Assert.Same(fonts, Config.Current.Fonts); Assert.Equal("System", fonts.DefaultFontName); Assert.Null(fonts.FontNameRaw);
            Assert.Equal(1.25, fonts.FontScale); Assert.True(fonts.IsClearTypeEnabled);
        }
        finally { Directory.Delete(blocked); }
        await operation.ApplyOptionsAsync(() => { fonts.FontName = "Legacy Font"; fonts.FontScale = 1.75; fonts.IsClearTypeEnabled = false; }, (-1, TimeSpan.Zero));
        await new SaveData(f.State).LoadAsync(TestContext.Current.CancellationToken);
        Assert.Equal("Legacy Font", Config.Current.Fonts.FontNameRaw); Assert.Equal(1.75, Config.Current.Fonts.FontScale); Assert.False(Config.Current.Fonts.IsClearTypeEnabled);
    }
    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.MaxValue)]
    public void CalculateRejectsInvalidOrOverflowingScale(double value)
        => Assert.Throws<ArgumentOutOfRangeException>(() => FontParameters.Calculate(new FontsConfig { FontScale = value }, new("System", 12, 12)));
    [Fact]
    public void Legacy38FontUpgradeBlocksNonZeroSizeButMigratesNameAndZeroSize()
    {
        var sized = JsonNode.Parse("""{"Format":"NeeView/38.0.0","Config":{"Panels":{"FontName":"Meiryo","FontSize":13}}}""")!.AsObject();
        Assert.Contains("MessageFontSize", ProfileImportCompatibility.BlockReason("UserSetting.json", sized));
        Assert.Throws<InvalidDataException>(() => ProfileImportCompatibility.Upgrade("UserSetting.json", sized));
        var nameOnly = JsonNode.Parse("""{"Format":"NeeView/38.0.0","Config":{"Panels":{"FontName":"Meiryo","FontSize":0}}}""")!.AsObject();
        var upgraded = ProfileImportCompatibility.Upgrade("UserSetting.json", nameOnly);
        Assert.Equal("Meiryo", upgraded["Config"]!["Fonts"]!["FontName"]!.GetValue<string>());
        Assert.Null(upgraded["Config"]!["Fonts"]!["FontSize"]);
    }
}
