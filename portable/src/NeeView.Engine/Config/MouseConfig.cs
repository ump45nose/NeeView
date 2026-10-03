// Copyright (c) NeeLaboratory. 原 MouseConfig 的方向手势分支。
namespace NeeView;
/// <summary>原鼠标设置；未迁字段由原 JSON 合并保留。</summary>
public sealed class MouseConfig
{
    public bool IsGestureEnabled { get; set; } = true;
    private double _gestureMinimumDistance = 30;
    /// <summary>原 5–200 DIP 编辑范围；不依赖 Windows SystemParameters。</summary>
    public double GestureMinimumDistance { get => _gestureMinimumDistance; set => _gestureMinimumDistance = double.IsFinite(value) ? Math.Round(Math.Max(5, value), 5) : 30; }
    public bool IsHoverScroll { get; set; }
    public double HoverScrollSensitivity { get; set; } = 2;
    public double HoverScrollDuration { get; set; } = .5;
    public bool IsMouseWheelScrollEnabled { get; set; }
    public double MouseWheelScrollSensitivity { get; set; } = 1;
    public double MouseWheelScrollDuration { get; set; } = .2;
}
