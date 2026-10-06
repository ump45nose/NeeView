using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using NeeView;
namespace NeeView.MacOS.Views;

/// <summary>分页、全景、浏览及封面共用原页背景/插值规则，不拥有像素或阅读状态。</summary>
internal static class ReaderImageRenderer
{
    private static (ThemeRgba Color, IBrush Brush)? _pageChecker;
    /// <summary>原透明页背景的颜色/HSV明暗格；一DIP内缩沿原ImageContentControl。</summary>
    public static void PageBackground(DrawingContext context, Avalonia.Rect target)
    {
        var config = Config.Current.Background; var color = config.PageBackgroundColor;
        if (color.A == 0 || target.Width <= 2 || target.Height <= 2) return;
        IBrush brush = CanvasBackgroundPresenter.Solid(color);
        if (config.IsPageBackgroundChecker)
        {
            if (_pageChecker?.Color != color)
            {
                double max = Math.Max(color.R, Math.Max(color.G, color.B));
                double v = max / 255; double next = Math.Clamp(v + (v < .1 ? .1 : -.1), 0, 1);
                byte Channel(byte component) => (byte)Math.Clamp((int)(max == 0 ? next * 255 : component * next / v), 0, 255);
                var alt = Color.FromArgb(color.A, Channel(color.R), Channel(color.G), Channel(color.B));
                _pageChecker = (color, CanvasBackgroundPresenter.Checker(CanvasBackgroundPresenter.ToColor(color), alt, 16));
            }
            brush = _pageChecker.Value.Brush;
        }
        context.FillRectangle(brush, target.Deflate(1));
    }
    /// <summary>实际设备像素双轴判定；插值只作用于图像，不改变其他文字/卡片。</summary>
    public static BitmapInterpolationMode Interpolation(Control owner, Avalonia.Rect source, Avalonia.Rect target, Matrix matrix)
    {
        double dpi = TopLevel.GetTopLevel(owner)?.RenderScaling ?? 1;
        var size = new NeeView.Size(target.Width * Math.Sqrt(matrix.M11 * matrix.M11 + matrix.M12 * matrix.M12) * dpi,
            target.Height * Math.Sqrt(matrix.M21 * matrix.M21 + matrix.M22 * matrix.M22) * dpi);
        return Config.Current.ImageDotKeep.IsImageDotKeep(size, new(source.Width, source.Height)) ? BitmapInterpolationMode.None : BitmapInterpolationMode.HighQuality;
    }
    /// <summary>绘图调用共享实际矩阵和裁剪源；显示资源仍由各查看器的租约拥有。</summary>
    public static void Draw(Control owner, DrawingContext context, Bitmap bitmap, Avalonia.Rect source, Avalonia.Rect target, Matrix matrix, BitmapInterpolationMode? interpolation = null)
    {
        PageBackground(context, target);
        using var options = context.PushRenderOptions(new RenderOptions { BitmapInterpolationMode = interpolation ?? Interpolation(owner, source, target, matrix) });
        context.DrawImage(bitmap, source, target);
    }
}
