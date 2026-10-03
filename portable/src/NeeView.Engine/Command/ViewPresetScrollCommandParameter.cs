// Copyright (c) NeeLaboratory. MIT；原枚举数值与参数保留。
namespace NeeView;
public enum LimitedHorizontalAlignment { Left, Center, Right }
public enum LimitedVerticalAlignment { Top, Center, Bottom }
public sealed class ViewPresetScrollCommandParameter
{
    public LimitedHorizontalAlignment Horizontal { get; set; } = LimitedHorizontalAlignment.Center;
    public LimitedVerticalAlignment Vertical { get; set; } = LimitedVerticalAlignment.Center;
    public bool IsSnap { get; set; }
}
