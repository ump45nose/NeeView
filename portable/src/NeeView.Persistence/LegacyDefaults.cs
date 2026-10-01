using System.Text.Json;
using NeeView.Application;
using NeeView.Core;

namespace NeeView.Persistence;

/// <summary>固定 Windows 46.3 的差分恢复表；不依赖新工程枚举顺序或产品默认键位。</summary>
internal static class LegacyDefaults
{
    private static readonly SortMode[] Sorts = [SortMode.FileName, SortMode.FileNameDescending, SortMode.FileType,
        SortMode.FileTypeDescending, SortMode.TimeStamp, SortMode.TimeStampDescending, SortMode.Size,
        SortMode.SizeDescending, SortMode.Entry, SortMode.EntryDescending, SortMode.Random];

    /// <summary>输入旧名称、别名或数值，返回固定基线的排序含义；非法值返回空。</summary>
    public static SortMode? Sort(string? value)
    {
        if (int.TryParse(value, out var index)) return index >= 0 && index < Sorts.Length ? Sorts[index] : null;
        if (value?.StartsWith("SortOrder.", StringComparison.Ordinal) == true) value = value[10..];
        return Enum.TryParse<SortMode>(value, out var sort) && Enum.IsDefined(sort) ? sort : null;
    }

    /// <summary>读取旧 Config.Command 的输入方案和方向，返回支持命令的原默认键位。</summary>
    public static List<ShortcutBinding> Bindings(JsonElement user)
    {
        var scheme = "TypeA"; var leftToRight = false;
        if (user.TryGetProperty("Config", out var config) && config.TryGetProperty("Command", out var command))
        {
            if (command.TryGetProperty("PresetInputScheme", out var input)) scheme = input.ValueKind == JsonValueKind.Number
                ? input.GetInt32() switch { 1 => "TypeB", 2 => "TypeC", _ => "TypeA" } : input.GetString() ?? "TypeA";
            if (command.TryGetProperty("PresetPageReadOrder", out var direction)) leftToRight = direction.ValueKind == JsonValueKind.Number ? direction.GetInt32() == 1 : direction.GetString() == "LeftToRight";
        }
        var defaults = new Dictionary<string, string>
        {
            ["NextPage"] = scheme == "TypeB" ? "Left,WheelDown" : "Left,LeftClick",
            ["PrevPage"] = scheme == "TypeB" ? "Right,WheelUp" : "Right,RightClick",
            ["FirstPage"] = "Ctrl+Right", ["LastPage"] = "Ctrl+Left", ["LoadAs"] = "Ctrl+O",
            ["SetPageModeOne"] = "Ctrl+1", ["SetPageModeTwo"] = "Ctrl+2",
            ["UndoDestinationMove"] = "Ctrl+Z", ["RedoDestinationMove"] = "Ctrl+Y",
            ["ViewScaleUp"] = "RightButton+WheelUp", ["ViewScaleDown"] = "RightButton+WheelDown"
        };
        // 原 CommandTable 先交换配对命令，再交换 WheelUp/Down；不把 Control 改成 Command。
        if (leftToRight)
            foreach (var (first, last) in new[] { ("NextPage", "PrevPage"), ("FirstPage", "LastPage") })
                (defaults[first], defaults[last]) = (SwapWheel(defaults[last]), SwapWheel(defaults[first]));
        for (var i = 1; i <= 9; i++) defaults[$"MoveToDestinationFolder{i}"] = i.ToString();
        return defaults.SelectMany(pair => pair.Value.Split(',').Select(gesture => new ShortcutBinding(gesture, pair.Key))).ToList();
    }

    /// <summary>只在恢复原默认方案时交换滚轮方向，保留显式自定义键位。</summary>
    private static string SwapWheel(string value) => value.Replace("WheelUp", "@wheel@", StringComparison.Ordinal)
        .Replace("WheelDown", "WheelUp", StringComparison.Ordinal).Replace("@wheel@", "WheelDown", StringComparison.Ordinal);
}
