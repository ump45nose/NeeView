// Copyright (c) NeeLaboratory. 原 GridLine 显示目标，网格不写入源图片。
using Avalonia;
using Avalonia.Media;
namespace NeeView.MacOS.Views;
internal static class ImageGridRenderer
{
    public static void Draw(DrawingContext context, Avalonia.Rect bounds, ImageGridTarget target)
    {
        var config = Config.Current.ImageGrid;
        if (!config.IsEnabled || config.Target != target || bounds.Width <= 0 || bounds.Height <= 0) return;
        using var clip = context.PushClip(bounds);
        var pen = new Pen(CanvasBackgroundPresenter.Solid(config.Color), 1);
        double dx = config.DivX > 0 ? bounds.Width / config.DivX : bounds.Width, dy = config.DivY > 0 ? bounds.Height / config.DivY : bounds.Height;
        if (config.IsSquare) dx = dy = Math.Max(dx, dy);
        context.DrawRectangle(pen, bounds);
        // 原字段没有 clamp；显示端限制线数，损坏/未来配置不制造无界绘制循环。
        for (int i = 1; i <= 4096 && i * dx < bounds.Width - 1; i++) context.DrawLine(pen, new(bounds.X+i*dx,bounds.Y), new(bounds.X+i*dx,bounds.Bottom));
        for (int i = 1; i <= 4096 && i * dy < bounds.Height - 1; i++) context.DrawLine(pen, new(bounds.X,bounds.Y+i*dy), new(bounds.Right,bounds.Y+i*dy));
    }
}
