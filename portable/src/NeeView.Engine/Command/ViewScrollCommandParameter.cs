// Copyright (c) NeeLaboratory. 原ViewScrollCommandParameter无WPF参数。
namespace NeeView;
/// <summary>原四向滚动步幅与跨轴开关，不到页尾翻页。</summary>
public sealed class ViewScrollCommandParameter
{
    private double _scroll = .25;
    public double Scroll { get => _scroll; set => _scroll = double.IsFinite(value) ? Math.Round(Math.Clamp(value, 0, 1), 5) : .25; }
    public bool AllowCrossScroll { get; set; } = true;
}
