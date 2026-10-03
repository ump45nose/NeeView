using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
namespace NeeView.MacOS.Views;
/// <summary>原 PageMarkers 绘制替换；只消费索引，不导航、不申请图像。</summary>
public sealed class PageMarkersView : Control
{
    public static readonly StyledProperty<IReadOnlyList<int>> IndicesProperty = AvaloniaProperty.Register<PageMarkersView, IReadOnlyList<int>>(nameof(Indices), Array.Empty<int>());
    public static readonly StyledProperty<int> MaximumProperty = AvaloniaProperty.Register<PageMarkersView, int>(nameof(Maximum));
    public static readonly StyledProperty<bool> IsReversedProperty = AvaloniaProperty.Register<PageMarkersView, bool>(nameof(IsReversed));
    public IReadOnlyList<int> Indices { get => GetValue(IndicesProperty); set => SetValue(IndicesProperty, value); }
    public int Maximum { get => GetValue(MaximumProperty); set => SetValue(MaximumProperty, value); }
    public bool IsReversed { get => GetValue(IsReversedProperty); set => SetValue(IsReversedProperty, value); }
    /// <summary>标记和方向只关联重绘，不生成正文刷新。</summary>
    static PageMarkersView() => AffectsRender<PageMarkersView>(IndicesProperty, MaximumProperty, IsReversedProperty);
    /// <summary>按滑条方向投影索引，单页避免除零，指针穿透由宿主配置。</summary>
    public override void Render(DrawingContext context)
    {
        base.Render(context);
        var brush = this.TryFindResource("PlaylistMarkBrush", out var resource) && resource is IBrush theme ? theme : Brushes.Gold;
        foreach (int index in Indices)
        {
            double ratio = Maximum > 0 ? Math.Clamp((double)index / Maximum, 0, 1) : 0;
            if (IsReversed) ratio = 1 - ratio;
            context.FillRectangle(brush, new Avalonia.Rect(Math.Clamp(ratio * Math.Max(0, Bounds.Width - 4), 0, Math.Max(0, Bounds.Width - 4)), Math.Max(0, Bounds.Height - 5), 4, Math.Min(5, Bounds.Height)));
        }
    }
}
