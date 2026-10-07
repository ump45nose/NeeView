// Copyright (c) NeeLaboratory. 原 MouseConfig 的方向手势分支。
namespace NeeView;
/// <summary>原鼠标设置；未迁字段由原 JSON 合并保留。</summary>
public sealed class MouseConfig
{
    public bool IsGestureEnabled { get; set; } = true;
    private double _gestureMinimumDistance = 30;
    /// <summary>原 5–200 DIP 编辑范围；不依赖 Windows SystemParameters。</summary>
    public double GestureMinimumDistance { get => _gestureMinimumDistance; set => _gestureMinimumDistance = double.IsFinite(value) ? Math.Round(Math.Max(5, value), 5) : 30; }
    public LongButtonDownMode LongButtonDownMode { get; set; } = LongButtonDownMode.Loupe;
    public LongButtonMask LongButtonMask { get; set; } = LongButtonMask.Left;
    public double LongButtonDownTime { get; set; } = 1;
    public double LongButtonRepeatTime { get; set; } = .1;
    public bool IsHoverScroll { get; set; }
    public double HoverScrollSensitivity { get; set; } = 2;
    public double HoverScrollDuration { get; set; } = .5;
    public bool IsMouseWheelScrollEnabled { get; set; }
    public double MouseWheelScrollSensitivity { get; set; } = 1;
    public double MouseWheelScrollDuration { get; set; } = .2;
    /// <summary>原指针自动滚动速度倍率。</summary>
    public double AutoScrollSensitivity { get; set; } = 1;
    /// <summary>原自动滚动时交互是否停止。</summary>
    public bool IsStopAutoScrollUponInteraction { get; set; }
}

// 原枚举顺序用于JSON兼容；平台鼠标位由表现适配。
public enum LongButtonDownMode { None, Loupe, AutoScroll, Repeat }
public enum LongButtonMask { Left, Right, All }
