// Copyright (c) NeeLaboratory. 原CommandConfig/InputScheme的无WPF适配。
namespace NeeView;

/// <summary>原默认输入方案及数值：标准、滚轮翻页、点击翻页。</summary>
public enum InputScheme { TypeA, TypeB, TypeC }

/// <summary>原输入默认值；Control与Mac Command保持独立。</summary>
public sealed class CommandConfig
{
    [PropertyMapIgnore] public InputScheme PresetInputScheme { get; set; }
    [PropertyMapIgnore] public PageReadOrder PresetPageReadOrder { get; set; }
    public bool IsAccessKeyEnabled { get; set; } = true;
    public bool IsReversePageMove { get; set; } = true;
    public bool IsReversePageMoveWheel { get; set; }
    public bool IsReversePageMoveHorizontalWheel { get; set; } = true;
    public bool IsHorizontalWheelLimitedOnce { get; set; } = true;
}
