// Copyright (c) NeeLaboratory. MIT；原命令参数，移除WPF属性编辑注解。
namespace NeeView;
public sealed class ViewScaleCommandParameter
{
    private double _scale = .2;
    public double Scale { get => _scale; set => _scale = double.IsFinite(value) ? Math.Round(Math.Clamp(value, 0, 1), 5) : .2; }
    public bool IsSnapDefaultScale { get; set; } = true;
}
