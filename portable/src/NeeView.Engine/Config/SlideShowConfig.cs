// Copyright (c) NeeLaboratory. 原 SlideShowConfig 字段、默认值和精度，MIT 许可。
namespace NeeView;

/// <summary>原幻灯片配置；定时、命令、页尾与表现的选项保持原 JSON 名称。</summary>
public sealed class SlideShowConfig
{
    private double _interval = 5, _duration = .5;
    public string NextPageCommandName { get; set; } = "NextPage";
    /// <summary>原至少0.1秒，五位精度；编辑范围不截断合法旧值。</summary>
    public double SlideShowInterval { get => _interval; set => _interval = double.IsFinite(value) ? Math.Round(Math.Max(value, .1), 5) : 5; }
    public SlideShowTimerResetGesture TimerResetGesture { get; set; } = SlideShowTimerResetGesture.InputAction;
    public bool IsTimerVisible { get; set; }
    public bool IsPrioritizeTime { get; set; }
    public bool IsWaitAnimation { get; set; }
    public bool IsAutoScroll { get; set; } = true;
    public PageEndAction PageEndAction { get; set; } = PageEndAction.Loop;
    public PageMoveType PageMoveType { get; set; } = PageMoveType.Fade;
    public double PageMoveDuration { get => _duration; set => _duration = double.IsFinite(value) ? Math.Round(Math.Max(value, 0), 5) : .5; }
    public void ResetNextPageCommandName() => NextPageCommandName = "NextPage";
}
public enum SlideShowTimerResetGesture { None, InputAction, MouseMove }
