// Copyright (c) NeeLaboratory. 原 MouseInputNormal 长按模式；沿唯一输入/查看器，MIT。
using Avalonia.Input;
using Avalonia.Threading;
namespace NeeView.MacOS.Views;
public sealed partial class ReaderView
{
    private DispatcherTimer? _longPressTimer;
    private string? _longGesture;
    private bool _longLoupe;
    private PointerPointProperties _longPressProperties;
    /// <summary>普通待确认点击才启动长按；组合、拖动、方向手势和失去捕获取消。</summary>
    private void StartLongPress(PointerPointProperties properties, string? gesture)
    {
        CancelLongPress();
        var config = Config.Current.Mouse;
        if (gesture is null || config.LongButtonDownMode == LongButtonDownMode.None || IsLoupeEnabled || IsAutoScrollMode) return;
        var buttons = MouseGestureSource.HeldButtons(properties);
        if (!buttons.Any(b => config.LongButtonMask == LongButtonMask.All
            || b == (config.LongButtonMask == LongButtonMask.Right ? MouseButton.Right : MouseButton.Left))) return;
        _longGesture = gesture;
        _longPressProperties = properties;
        _longPressTimer ??= new DispatcherTimer(); _longPressTimer.Tick -= LongPressTick; _longPressTimer.Tick += LongPressTick;
        _longPressTimer.Interval = TimeSpan.FromSeconds(SafeLongInterval(config.LongButtonDownTime, 1)); _longPressTimer.Start();
    }
    private static double SafeLongInterval(double value, double fallback) => double.IsFinite(value) && value > 0 ? Math.Clamp(value, .01, 60) : fallback;
    private void LongPressTick(object? sender, EventArgs e)
    {
        if (_disposed || _longGesture is null || _dragged || CanStartMouseSequence?.Invoke() == false
            || !MouseGestureSource.HeldButtons(_longPressProperties).Any(b => Config.Current.Mouse.LongButtonMask == LongButtonMask.All
                || b == (Config.Current.Mouse.LongButtonMask == LongButtonMask.Right ? MouseButton.Right : MouseButton.Left))) { CancelLongPress(); return; }
        var gesture = _longGesture;
        switch (Config.Current.Mouse.LongButtonDownMode)
        {
            case LongButtonDownMode.Loupe:
                CancelLongPress(); CancelMouseSequence(); _pendingClick = null; _pressed = null;
                SetLoupe(true); _longLoupe = IsLoupeEnabled; break;
            case LongButtonDownMode.AutoScroll:
                BeginAutoScroll(_pointer ?? default, true); break;
            case LongButtonDownMode.Repeat:
                _pendingClick = null; TryGestureRequested?.Invoke(gesture);
                _longPressTimer!.Interval = TimeSpan.FromSeconds(SafeLongInterval(Config.Current.Mouse.LongButtonRepeatTime, .1)); break;
            default: CancelLongPress(); break;
        }
    }
    private void CancelLongPress() { _longPressTimer?.Stop(); _longGesture = null; }
    private void DisposeLongPress() { CancelLongPress(); if (_longPressTimer is not null) _longPressTimer.Tick -= LongPressTick; _longPressTimer = null; }
}
