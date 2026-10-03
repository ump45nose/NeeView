using Avalonia.Input;
namespace NeeView.MacOS.Views;

/// <summary>原MouseGestureSource/MouseExGesture匹配适配；Avalonia事件类型只留在表现层。</summary>
internal static class MouseGestureSource
{
    private static readonly HashSet<string> Actions = new(StringComparer.OrdinalIgnoreCase)
    {
        "LeftClick", "RightClick", "MiddleClick", "LeftDoubleClick", "RightDoubleClick", "MiddleDoubleClick",
        "XButton1Click", "XButton2Click", "XButton1DoubleClick", "XButton2DoubleClick", "WheelUp", "WheelDown", "WheelLeft", "WheelRight"
    };
    /// <summary>按原动作和严格修饰集合解析；同义词及顺序归一，不接受未知按钮。</summary>
    public static bool TryNormalize(string value, out string normalized)
    {
        normalized = ""; var tokens = value.Trim().Split('+', StringSplitOptions.TrimEntries);
        var action = tokens[^1].Equals("WheelClick", StringComparison.OrdinalIgnoreCase) ? "MiddleClick" : tokens[^1];
        if (!Actions.Contains(action)) return false;
        var flags = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var token in tokens[..^1])
        {
            var canonical = token.ToUpperInvariant() switch
            {
                "CTRL" or "CONTROL" => "Ctrl", "COMMAND" or "META" or "WINDOWS" or "WIN" => "Meta",
                "SHIFT" => "Shift", "ALT" => "Alt", "LEFTBUTTON" => "LeftButton", "RIGHTBUTTON" => "RightButton",
                "MIDDLEBUTTON" => "MiddleButton", "XBUTTON1" => "XButton1", "XBUTTON2" => "XButton2", _ => null
            };
            if (canonical is null || !flags.Add(canonical)) return false;
        }
        normalized = string.Join('+', flags.Order(StringComparer.Ordinal).Append(Actions.First(a => a.Equals(action, StringComparison.OrdinalIgnoreCase)))); return true;
    }
    /// <summary>ChangedButton自身不进入按钮修饰；其余Pressed状态必须完全匹配原绑定。</summary>
    public static string Create(string action, KeyModifiers modifiers, PointerPointProperties properties, MouseButton changed = MouseButton.None)
    {
        var tokens = new List<string>();
        if (modifiers.HasFlag(KeyModifiers.Control)) tokens.Add("Ctrl");
        if (modifiers.HasFlag(KeyModifiers.Meta)) tokens.Add("Meta");
        if (modifiers.HasFlag(KeyModifiers.Shift)) tokens.Add("Shift");
        if (modifiers.HasFlag(KeyModifiers.Alt)) tokens.Add("Alt");
        if (properties.IsLeftButtonPressed && changed != MouseButton.Left) tokens.Add("LeftButton");
        if (properties.IsRightButtonPressed && changed != MouseButton.Right) tokens.Add("RightButton");
        if (properties.IsMiddleButtonPressed && changed != MouseButton.Middle) tokens.Add("MiddleButton");
        if (properties.IsXButton1Pressed && changed != MouseButton.XButton1) tokens.Add("XButton1");
        if (properties.IsXButton2Pressed && changed != MouseButton.XButton2) tokens.Add("XButton2");
        return string.Join('+', tokens.Append(action));
    }
    /// <summary>原ClickCount≥2与五按钮动作对应，按钮按下/释放类型由宿主提供。</summary>
    public static string? ClickAction(MouseButton button, int clickCount) => button switch
    {
        MouseButton.Left => clickCount >= 2 ? "LeftDoubleClick" : "LeftClick",
        MouseButton.Right => clickCount >= 2 ? "RightDoubleClick" : "RightClick",
        MouseButton.Middle => clickCount >= 2 ? "MiddleDoubleClick" : "MiddleClick",
        MouseButton.XButton1 => clickCount >= 2 ? "XButton1DoubleClick" : "XButton1Click",
        MouseButton.XButton2 => clickCount >= 2 ? "XButton2DoubleClick" : "XButton2Click", _ => null
    };
    /// <summary>只从本次更新种类识别changed button，不能用任意按下按钮冒充动作。</summary>
    public static MouseButton ChangedButton(PointerPointProperties properties) => properties.PointerUpdateKind switch
    {
        PointerUpdateKind.LeftButtonPressed => MouseButton.Left, PointerUpdateKind.RightButtonPressed => MouseButton.Right,
        PointerUpdateKind.MiddleButtonPressed => MouseButton.Middle, PointerUpdateKind.XButton1Pressed => MouseButton.XButton1,
        PointerUpdateKind.XButton2Pressed => MouseButton.XButton2, _ => MouseButton.None
    };
    /// <summary>仅用于一次输入的释放抑制，不能从动作字符串推测按钮状态。</summary>
    public static IEnumerable<MouseButton> HeldButtons(PointerPointProperties properties)
    {
        if (properties.IsLeftButtonPressed) yield return MouseButton.Left;
        if (properties.IsRightButtonPressed) yield return MouseButton.Right;
        if (properties.IsMiddleButtonPressed) yield return MouseButton.Middle;
        if (properties.IsXButton1Pressed) yield return MouseButton.XButton1;
        if (properties.IsXButton2Pressed) yield return MouseButton.XButton2;
    }
}
