// 来源：NeeView/Config/AutoHideConfig.cs、WindowConfig.cs、MenuBarConfig.cs（子集适配）。
namespace NeeView;

/// <summary>保留原焦点锁枚举的数值顺序；界面负责将实际/逻辑焦点转换为资格。</summary>
public enum AutoHideFocusLockMode { None, LogicalFocusLock, LogicalTextBoxFocusLock, FocusLock, TextBoxFocusLock }
/// <summary>边角触发冲突：允许、仅最边缘 1.5 DIP、禁止。</summary>
public enum AutoHideConflictMode { Allow, AllowPixel, Deny }
/// <summary>原自动隐藏默认值与精度；不含窗口、控件或定时器。</summary>
public sealed class AutoHideConfig
{
    private double _delay = 1, _visibleDelay, _horizontal = 32, _vertical = 32;
    public double AutoHideDelayTime { get => _delay; set => _delay = Round(value, 1); }
    public double AutoHideDelayVisibleTime { get => _visibleDelay; set => _visibleDelay = Round(value, 0); }
    public double AutoHideHitTestHorizontalMargin { get => _horizontal; set => _horizontal = Round(value, 32); }
    public double AutoHideHitTestVerticalMargin { get => _vertical; set => _vertical = Round(value, 32); }
    public AutoHideFocusLockMode AutoHideFocusLockMode { get; set; } = AutoHideFocusLockMode.LogicalTextBoxFocusLock;
    public bool IsAutoHideKeyDownDelay { get; set; } = true;
    public AutoHideConflictMode AutoHideConflictTopMargin { get; set; } = AutoHideConflictMode.AllowPixel;
    public AutoHideConflictMode AutoHideConflictBottomMargin { get; set; } = AutoHideConflictMode.Allow;
    /// <summary>保留原五位精度；非有限损坏输入回退默认，负延迟在显示端按零处理。</summary>
    private static double Round(double value, double fallback) => double.IsFinite(value) ? Math.Round(value, 5) : fallback;
}
/// <summary>原 Window 配置的 Mac 能力子集；FullDesktop 等未迁入字段由 SaveData 保留。</summary>
public sealed class WindowConfig
{
    public bool IsTopmost { get; set; }
    public bool IsAutoHideInNormal { get; set; }
    public bool IsAutoHideInMaximized { get; set; }
    public bool IsAutoHideInFullScreen { get; set; } = true;
    public bool IsAutoHideInFullDesktop { get; set; } = true;
}
/// <summary>原菜单/地址栏显隐资格；运行时可见状态不作为配置保存。</summary>
public sealed class MenuBarConfig
{
    public bool IsHideMenu { get; set; }
    public bool IsHideMenuInAutoHideMode { get; set; } = true;
    public bool IsAddressBarEnabled { get; set; } = true;
}
