using Avalonia.Controls;
using Avalonia.Input;
using NeeView.Application;
using NeeView.Core;

namespace NeeView.Desktop;

/// <summary>独立输入映射组件，页面结构和阅读业务不参与键位解析。</summary>
public sealed class ReaderInputRouter(Func<AppSettings> settings, Func<ReaderSnapshot> snapshot,
    Func<string, string?, Task> execute, Action<string> message)
{
    private double _wheelRemainder;
    /// <summary>文本编辑拥有独立作用域；冲突键位明确显示所有命令而不执行。</summary>
    public async void KeyDown(Window owner, KeyEventArgs e)
    {
        if (e.Source is TextBox || owner.FocusManager?.GetFocusedElement() is TextBox) return;
        foreach (var binding in settings().Shortcuts)
        {
            if (!TryGesture(binding.Gesture, out var gesture) || !gesture.Matches(e)) continue;
            var conflicts = CommandCatalog.Conflicts(settings().Shortcuts).Where(c => c.StartsWith(CommandCatalog.NormalizeGesture(binding.Gesture) + ":", StringComparison.OrdinalIgnoreCase)).ToArray();
            e.Handled = true;
            if (conflicts.Length > 0) { message(string.Join("；", conflicts)); return; }
            await execute(binding.Command, binding.Parameter); return;
        }
    }
    /// <summary>高精度滚轮保留残余量；连续模式交给 ScrollViewer，分页模式按绑定步进。</summary>
    public async void Wheel(PointerWheelEventArgs e)
    {
        if (e.KeyModifiers.HasFlag(KeyModifiers.Meta) || e.KeyModifiers.HasFlag(KeyModifiers.Control))
        { e.Handled = true; await execute(e.Delta.Y > 0 ? "ZoomIn" : "ZoomOut", null); return; }
        var prefix = e.GetCurrentPoint(null).Properties.IsRightButtonPressed ? "RightButton+" : "";
        if (snapshot().Options.Mode != ReaderMode.Paged && prefix.Length == 0) return;
        _wheelRemainder += e.Delta.Y; e.Handled = true;
        if (Math.Abs(_wheelRemainder) < 1) return;
        var wheel = _wheelRemainder; _wheelRemainder -= Math.Sign(wheel);
        await GestureAsync(prefix + (wheel < 0 ? "WheelDown" : "WheelUp"));
    }
    /// <summary>处理查看器已选中对象后的鼠标手势；未绑定时返回 false 以执行默认交互。</summary>
    public async Task<bool> GestureAsync(string gesture)
    {
        var matches = settings().Shortcuts.Where(b => CommandCatalog.NormalizeGesture(b.Gesture) == CommandCatalog.NormalizeGesture(gesture)).ToArray();
        if (matches.Length == 0) return false;
        if (CommandCatalog.Conflicts(matches).Count > 0) { message(string.Join("；", CommandCatalog.Conflicts(matches))); return true; }
        await execute(matches[0].Command, matches[0].Parameter); return true;
    }
    /// <summary>原 Control/Command 和数字名称保持保存值，只在匹配时归一。</summary>
    public static bool TryGesture(string text, out KeyGesture gesture)
    {
        try
        {
            var normalized = CommandCatalog.NormalizeGesture(text);
            if (normalized.StartsWith("Wheel", StringComparison.OrdinalIgnoreCase) || normalized.EndsWith("Click", StringComparison.OrdinalIgnoreCase)) { gesture = null!; return false; }
            var parts = normalized.Split('+'); if (parts[^1].Length == 1 && char.IsAsciiDigit(parts[^1][0])) parts[^1] = "D" + parts[^1];
            gesture = KeyGesture.Parse(string.Join('+', parts)); return true;
        }
        catch (ArgumentException) { gesture = null!; return false; }
    }
}
