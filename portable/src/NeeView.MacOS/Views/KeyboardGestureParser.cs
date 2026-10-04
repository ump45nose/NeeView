using Avalonia.Input;

namespace NeeView.MacOS.Views;

/// <summary>原快捷键到 Avalonia 的单一转换；菜单提示与真实输入使用相同键名。</summary>
internal static class KeyboardGestureParser
{
    /// <summary>规范原数字键及修饰键别名；鼠标手势或无效键位不作为键盘手势。</summary>
    /// <param name="value">原命令或差分配置中的单个快捷键字符串。</param>
    /// <returns>有效的 Avalonia 手势；空值、鼠标手势及无法解析的键位返回 null。</returns>
    internal static KeyGesture? TryParse(string value)
    {
        try
        {
            var tokens = value.Trim().Split('+');
            for (int i = 0; i < tokens.Length - 1; i++)
                tokens[i] = tokens[i] == "Control" ? "Ctrl" : tokens[i] == "Command" ? "Meta" : tokens[i];
            // KeyGesture.Parse("2") 会将数字解释为 Key 的枚举值，导致菜单显示 Back。
            // 原数字键表示键盘上排数字，必须与输入事件的 D0-D9 相同；不修改原 JSON。
            if (tokens[^1].Length == 1 && char.IsAsciiDigit(tokens[^1][0])) tokens[^1] = "D" + tokens[^1];
            return KeyGesture.Parse(string.Join('+', tokens));
        }
        catch (ArgumentException) { return null; }
    }
}
