// Copyright (c) NeeLaboratory. MIT；原固定/循环缩放模式参数。
namespace NeeView;
public sealed class StretchModeCommandParameter { public bool IsToggle { get; set; } }
public sealed class ToggleStretchModeCommandParameter
{
    public bool IsLoop { get; set; } = true;
    public bool IsEnableNone { get; set; } = true;
    public bool IsEnableUniform { get; set; } = true;
    public bool IsEnableUniformToFill { get; set; } = true;
    public bool IsEnableUniformToSize { get; set; } = true;
    public bool IsEnableUniformToVertical { get; set; } = true;
    public bool IsEnableUniformToHorizontal { get; set; } = true;
    /// <summary>原模式集合，关闭某项不改变枚举顺序。</summary>
    public IReadOnlyDictionary<PageStretchMode, bool> GetStretchModeDictionary() => new Dictionary<PageStretchMode, bool>
    {
        [PageStretchMode.None] = IsEnableNone, [PageStretchMode.Uniform] = IsEnableUniform,
        [PageStretchMode.UniformToFill] = IsEnableUniformToFill, [PageStretchMode.UniformToSize] = IsEnableUniformToSize,
        [PageStretchMode.UniformToVertical] = IsEnableUniformToVertical, [PageStretchMode.UniformToHorizontal] = IsEnableUniformToHorizontal
    };
}
