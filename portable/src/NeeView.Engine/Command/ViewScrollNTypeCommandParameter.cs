// Copyright (c) NeeLaboratory. MIT；原纯NType滚动参数。
namespace NeeView;
public sealed class ViewScrollNTypeCommandParameter : IScrollNTypeParameter
{
    private double _scroll = 1, _time;
    public NScrollType ScrollType { get; set; } = NScrollType.NType;
    public double Scroll { get => _scroll; set => _scroll = double.IsFinite(value) ? Math.Round(Math.Clamp(value, .1, 1), 5) : 1; }
    public double LineBreakStopTime { get => _time; set => _time = double.IsFinite(value) ? Math.Round(value, 5) : 0; }
    public bool PagesAsOne { get; set; }
    public bool IsReverse { get; set; } = true;
}
