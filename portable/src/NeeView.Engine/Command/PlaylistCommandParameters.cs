// Copyright (c) NeeLaboratory. 原字段/默认值，WPF 属性编辑注解移入表现端。
namespace NeeView;
public enum ToggleMode { Toggle, On, Off }
/// <summary>原 Toggle 参数；快捷键支持固定开关，菜单忽略参数。</summary>
public sealed class ToggleCommandParameter
{
    public ToggleMode ToggleMode { get; set; }
    /// <summary>原菜单使用切换语义，输入允许参数固定On/Off。</summary>
    public bool GetState(bool current, bool fromMenu) => fromMenu ? !current : ToggleMode switch { ToggleMode.On => true, ToggleMode.Off => false, _ => !current };
}
/// <summary>原书内导航参数，两个默认值均为 false。</summary>
public sealed class MovePlaylistItemInBookCommandParameter
{
    public bool IsLoop { get; set; }
    public bool IsIncludeTerminal { get; set; }
}
