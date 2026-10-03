// Copyright (c) NeeLaboratory. MIT；原 TogglePageModeCommandParameter 字段及默认值。
namespace NeeView;

/// <summary>正反向页面模式切换共用的原参数；关闭循环时在首末模式停止。</summary>
public sealed class TogglePageModeCommandParameter
{
    public bool IsLoop { get; set; } = true;
}
