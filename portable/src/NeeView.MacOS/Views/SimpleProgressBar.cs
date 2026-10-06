// Copyright (c) NeeLaboratory. 原 SimpleProgressBar 宽度比例，MIT 许可。
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
namespace NeeView.MacOS.Views;

/// <summary>原4 DIP计时条；直接按Value绘制，不受普通ProgressBar模板内边距影响。</summary>
public sealed class SimpleProgressBar : Control
{
    public static readonly StyledProperty<double> ValueProperty = AvaloniaProperty.Register<SimpleProgressBar, double>(nameof(Value));
    public static readonly StyledProperty<IBrush?> ForegroundProperty = AvaloniaProperty.Register<SimpleProgressBar, IBrush?>(nameof(Foreground));
    public double Value { get => GetValue(ValueProperty); set => SetValue(ValueProperty, value); }
    public IBrush? Foreground { get => GetValue(ForegroundProperty); set => SetValue(ForegroundProperty, value); }
    static SimpleProgressBar() => AffectsRender<SimpleProgressBar>(ValueProperty, ForegroundProperty);
    /// <summary>保持原从左侧开始、宽度乘进度的表现；损坏数值不进入绘制。</summary>
    public override void Render(DrawingContext context)
    {
        base.Render(context);
        if (Foreground is not null && double.IsFinite(Value))
            context.FillRectangle(Foreground, new Avalonia.Rect(0, 0, Bounds.Width * Math.Clamp(Value, 0, 1), Bounds.Height));
    }
}
