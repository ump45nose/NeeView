using NeeView.Application;
using NeeView.Core;
using NeeView.Persistence;
using Xunit;

#pragma warning disable xUnit1051
namespace NeeView.Portable.Tests;

/// <summary>固定 Windows 46.3 的数字枚举与命令差分契约，防止跨平台默认值污染导入。</summary>
public sealed class LegacyCompatibilityTests
{
    /// <summary>原配置 0 是 FileName、8 是 Entry；不能直接转换新枚举整数。</summary>
    [Theory]
    [InlineData(0, SortMode.FileName)]
    [InlineData(8, SortMode.Entry)]
    [InlineData(10, SortMode.Random)]
    public async Task LegacySortNumbersUseWindowsOrder(int legacy, SortMode expected)
    {
        await using var workspace = new TestWorkspace(); var profile = Path.Combine(workspace.Root, "profile"); Directory.CreateDirectory(profile);
        await File.WriteAllTextAsync(Path.Combine(profile, "UserSetting.json"), System.Text.Json.JsonSerializer.Serialize(new { Config = new { BookSetting = new { SortMode = legacy } } }));
        var plan = await new LegacyImporter(workspace.States, workspace.Settings).PlanImportAsync(profile, []);
        Assert.Equal(expected, plan.Settings.Defaults.Sort);
        Assert.Equal(expected, LegacyImporter.ParseProps("Sort=" + legacy).Sort);
    }
    /// <summary>缺省/null 使用原默认且保留 Parameter，空字符串清空，别名仍可执行。</summary>
    [Fact]
    public async Task DiffBindingsRestoreWindowsDefaultsAndPreserveDisabledKeys()
    {
        await using var workspace = new TestWorkspace(); var profile = Path.Combine(workspace.Root, "profile"); Directory.CreateDirectory(profile);
        await File.WriteAllTextAsync(Path.Combine(profile, "UserSetting.json"), """
            {"Commands":{"NextPage":{"ShortCutKey":null},"PrevPage":{"ShortCutKey":""},
            "MoveToDestinationFolder1":{"Parameter":{"Index":2}},"SetBookReadOrderLeft":{"ShortCutKey":"Ctrl+L"}}}
            """);
        var plan = await new LegacyImporter(workspace.States, workspace.Settings).PlanImportAsync(profile, []);
        Assert.Contains(plan.Settings.Shortcuts, b => b.Command == "NextPage" && b.Gesture == "LeftClick");
        Assert.Contains(plan.Settings.Shortcuts, b => b.Command == "FirstPage" && b.Gesture == "Ctrl+Right");
        Assert.Contains(plan.Settings.Shortcuts, b => b.Command == "LoadAs" && b.Gesture == "Ctrl+O");
        Assert.Contains(plan.Settings.Shortcuts, b => b.Command == "SetPageModeTwo" && b.Gesture == "Ctrl+2");
        Assert.DoesNotContain(plan.Settings.Shortcuts, b => b.Command == "PrevPage");
        Assert.Equal("{\"Index\":2}", Assert.Single(plan.Settings.Shortcuts, b => b.Command == "MoveToDestinationFolder1").Parameter);
        Assert.DoesNotContain(plan.Warnings, w => w.Contains("未支持命令：SetBookReadOrderLeft"));
    }
    /// <summary>输入方案 B 与左向右预设按原配对顺序恢复，显式键位不交换。</summary>
    [Fact]
    public async Task InputPresetRestoresDirectionBeforeCustomOverrides()
    {
        await using var workspace = new TestWorkspace(); var profile = Path.Combine(workspace.Root, "profile"); Directory.CreateDirectory(profile);
        await File.WriteAllTextAsync(Path.Combine(profile, "UserSetting.json"), """
            {"Config":{"Command":{"PresetInputScheme":"TypeB","PresetPageReadOrder":"LeftToRight"}},
             "Commands":{"FirstPage":{"ShortCutKey":"Ctrl+Home"}}}
            """);
        var bindings = (await new LegacyImporter(workspace.States, workspace.Settings).PlanImportAsync(profile, [])).Settings.Shortcuts;
        Assert.Contains(bindings, b => b.Command == "NextPage" && b.Gesture == "Right");
        Assert.Contains(bindings, b => b.Command == "NextPage" && b.Gesture == "WheelDown");
        Assert.Contains(bindings, b => b.Command == "FirstPage" && b.Gesture == "Ctrl+Home");
        Assert.Contains(bindings, b => b.Command == "LastPage" && b.Gesture == "Ctrl+Right");
    }
}
