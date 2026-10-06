// Copyright (c) NeeLaboratory. 原CommandTable.CreateDefaultMemento键位分支。
namespace NeeView;

/// <summary>原输入方案和配对键位转换；未配置命令仍使用同一基线默认值。</summary>
public static class DefaultInputScheme
{
    private static readonly Dictionary<string, string> Pairs = new()
    {
        ["NextPage"] = "PrevPage", ["PrevPage"] = "NextPage",
        ["NextOnePage"] = "PrevOnePage", ["PrevOnePage"] = "NextOnePage",
        ["NextScrollPage"] = "PrevScrollPage", ["PrevScrollPage"] = "NextScrollPage",
        ["NextSizePage"] = "PrevSizePage", ["PrevSizePage"] = "NextSizePage",
        ["NextFolderPage"] = "PrevFolderPage", ["PrevFolderPage"] = "NextFolderPage",
        ["FirstPage"] = "LastPage", ["LastPage"] = "FirstPage"
    };
    private static readonly IReadOnlyDictionary<string, CommandDefinition> Baseline = LoadBaseline();
    /// <summary>仅对固定原登记表中的命令做默认裁剪；未来命令保持原节点。</summary>
    public static bool IsKnownCommand(string name) => Baseline.ContainsKey(name);
    /// <summary>原默认触摸区域不随阅读方向或输入方案交换；本阶段只保留配置，不启用触摸执行。</summary>
    public static string GetTouchGesture(string name) => Baseline.GetValueOrDefault(name)?.TouchGesture ?? "";
    /// <summary>原命令构造器通知默认值；保存差分不改变当前执行入口的能力范围。</summary>
    public static bool GetShowMessage(string name) => Baseline.GetValueOrDefault(name)?.IsShowMessage ?? false;
    /// <summary>原SetShare参数关系；键位各自独立，参数仅保留一份。</summary>
    public static string GetParameterOwner(string name) => name switch
    {
        "NextPage" => "PrevPage", "NextOnePage" => "PrevOnePage", "NextScrollPage" => "PrevScrollPage",
        "NextSizePage" => "PrevSizePage", "NextFolderPage" => "PrevFolderPage", "LastPage" => "FirstPage",
        "ViewScrollDown" or "ViewScrollLeft" or "ViewScrollRight" => "ViewScrollUp",
        "ViewScaleDown" => "ViewScaleUp", "ViewBaseScaleDown" => "ViewBaseScaleUp", "ViewRotateRight" => "ViewRotateLeft",
        "ViewScrollNTypeDown" => "ViewScrollNTypeUp", "NextPlaylistItemInBook" => "PrevPlaylistItemInBook",
        "TogglePageModeReverse" => "TogglePageMode", "ToggleStretchModeReverse" => "ToggleStretchMode",
        "SetStretchModeUniformToFill" or "SetStretchModeUniformToSize" or "SetStretchModeUniformToVertical" or "SetStretchModeUniformToHorizontal" => "SetStretchModeUniform",
        _ => name
    };
    /// <summary>原CommandTools.ResolveCommand：菜单禁用反转，输入按滑条方向与方案方向比较。</summary>
    public static string ResolveCommand(string name, CommandConfig config, bool sliderLeftToRight, bool allowReverse, bool parameterReverse) =>
        allowReverse && parameterReverse && config.IsReversePageMove && sliderLeftToRight != (config.PresetPageReadOrder != PageReadOrder.RightToLeft)
            && Pairs.TryGetValue(name, out var partner) ? partner : name;
    /// <summary>只加载已有原命令清单，不增加平行登记表。</summary>
    private static IReadOnlyDictionary<string, CommandDefinition> LoadBaseline()
    {
        using var stream = typeof(CommandTable).Assembly.GetManifestResourceStream("NeeView.Command.command-manifest.json")!;
        return System.Text.Json.JsonSerializer.Deserialize<List<CommandDefinition>>(stream)!.ToDictionary(d => d.Name, d => d);
    }
    /// <summary>按原先方案覆写、再配对交换及滚轮交换的顺序返回默认键位。</summary>
    public static string GetShortcut(string name, string fallback, CommandConfig config)
    {
        var target = config.PresetPageReadOrder != PageReadOrder.RightToLeft && Pairs.TryGetValue(name, out var partner) ? partner : name;
        var value = GetSchemeShortcut(target, Baseline.GetValueOrDefault(target)?.Shortcut ?? fallback, config.PresetInputScheme);
        return target == name ? value : value.Replace("WheelUp", "****").Replace("WheelDown", "WheelUp").Replace("****", "WheelDown");
    }
    /// <summary>原默认方向只交换配对手势，不修改方向字母或自定义序列。</summary>
    public static string GetMouseGesture(string name, string fallback, CommandConfig config)
    {
        var target = config.PresetPageReadOrder != PageReadOrder.RightToLeft && Pairs.TryGetValue(name, out var partner) ? partner : name;
        return Baseline.GetValueOrDefault(target)?.MouseGesture ?? fallback;
    }
    /// <summary>原方案只改变对应命令，其他235项默认定义保持。</summary>
    private static string GetSchemeShortcut(string name, string fallback, InputScheme scheme) => (scheme, name) switch
    {
        (InputScheme.TypeB or InputScheme.TypeC, "NextScrollPage" or "PrevScrollPage") => "",
        (InputScheme.TypeB, "NextPage") => "Left,WheelDown",
        (InputScheme.TypeB, "PrevPage") => "Right,WheelUp",
        (InputScheme.TypeB, "OpenContextMenu") => "RightClick",
        (InputScheme.TypeC, "NextPage") => "Left,LeftClick",
        (InputScheme.TypeC, "PrevPage") => "Right,RightClick",
        (InputScheme.TypeC, "ViewScrollUp") => "WheelUp",
        (InputScheme.TypeC, "ViewScrollDown") => "WheelDown",
        _ => fallback
    };
}
