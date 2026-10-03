// Copyright (c) NeeLaboratory. MIT；原命令参数。
namespace NeeView;
public sealed class ViewRotateCommandParameter
{
    private int _angle = 45;
    public int Angle { get => _angle; set => _angle = Math.Clamp(value, 0, 180); }
    public bool IsStretch { get; set; }
}
