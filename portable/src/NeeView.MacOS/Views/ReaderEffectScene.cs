using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using SkiaSharp;
namespace NeeView.MacOS.Views;

/// <summary>效果输入的原页框合成。只记录绘图，布局/图片需求/操作状态仍属于现有查看器。</summary>
internal static class ReaderEffectScene
{
    internal static SKRect Rect(Avalonia.Rect rect) => new((float)rect.X, (float)rect.Y, (float)rect.Right, (float)rect.Bottom);
    internal static SKMatrix Matrix(Avalonia.Matrix matrix) => new((float)matrix.M11, (float)matrix.M21, (float)matrix.M31,
        (float)matrix.M12, (float)matrix.M22, (float)matrix.M32, 0, 0, 1);
    internal static SKColor Color(ThemeRgba color) => new(color.R, color.G, color.B, color.A);
    internal static void Fill(SKCanvas canvas, Avalonia.Rect target, SKColor color)
    { using var paint = new SKPaint { Color = color }; canvas.DrawRect(Rect(target), paint); }
    /// <summary>借用原解码像素；picture及GPU命令持有的原生引用最终归还显示租约。</summary>
    internal static void Image(SKCanvas canvas, DecodedImageLease pixels, Avalonia.Rect crop, Avalonia.Rect target, Func<IDisposable> retain, BitmapInterpolationMode sampling)
    {
        using var image = ImageSpatialEffectRenderer.BorrowImage(pixels, retain);
        canvas.DrawImage(image, Rect(crop), Rect(target), ImageSpatialEffectRenderer.Sampling(sampling, target.Width > crop.Width || target.Height > crop.Height));
    }
    /// <summary>与原ImageContentControl相同的内缩页背景；先与透明像素合成后进入效果。</summary>
    internal static void PageBackground(SKCanvas canvas, Avalonia.Rect target)
    {
        var c = Config.Current.Background; var color = c.PageBackgroundColor;
        if (color.A == 0 || target.Width <= 2 || target.Height <= 2) return;
        target = target.Deflate(1);
        if (!c.IsPageBackgroundChecker) { Fill(canvas, target, Color(color)); return; }
        using var recorder = new SKPictureRecorder(); var tile = recorder.BeginRecording(new SKRect(0, 0, 16, 16));
        Fill(tile, new(0, 0, 16, 16), Color(color));
        var alternate = Color(ReaderImageRenderer.PageCheckerAlternate(color));
        Fill(tile, new(0, 0, 8, 8), alternate); Fill(tile, new(8, 8, 8, 8), alternate);
        using var picture = recorder.EndRecording(); using var shader = picture.ToShader(SKShaderTileMode.Repeat, SKShaderTileMode.Repeat);
        using var paint = new SKPaint { Shader = shader }; canvas.DrawRect(Rect(target), paint);
    }
    /// <summary>缺图提示也在合成面内；按系统字体逐字符匹配，中文不会变为空方块。</summary>
    internal static void Text(SKCanvas canvas, string text, Point point, float size = 13, SKColor? color = null)
    {
        using var paint = new SKPaint { Color = color ?? SKColors.LightGray, IsAntialias = true };
        float x = (float)point.X, y = (float)point.Y + size;
        foreach (var rune in text.EnumerateRunes())
        {
            if (rune.Value == '\n') { x = (float)point.X; y += size * 1.3f; continue; }
            using var face = SKFontManager.Default.MatchCharacter(rune.Value);
            using var font = new SKFont(face ?? SKTypeface.Default, size);
            var value = rune.ToString(); canvas.DrawText(value, x, y, font, paint); x += font.MeasureText(value);
        }
    }
    /// <summary>独立主题资源解析；卡片不承担来源/封面/文件业务。</summary>
    internal static void Card(Control owner, SKCanvas canvas, Page page, Avalonia.Rect card, DecodedImageLease? pixels, Func<IDisposable>? retain, string status, Avalonia.Matrix matrix)
    {
        SKColor Brush(string key, SKColor fallback) => owner.TryFindResource(key, out var value) && value is ISolidColorBrush brush ?
            new(brush.Color.R, brush.Color.G, brush.Color.B, brush.Color.A) : fallback;
        var background = Brush("ArchivePage.Background", SKColors.WhiteSmoke); var foreground = Brush("ArchivePage.Foreground", SKColors.Black); var border = Brush("ArchivePage.CoverBorder", SKColors.LightGray);
        Fill(canvas, card, background);
        var area = ArchivePageRenderer.CoverArea(card); var size = pixels is null ? new Avalonia.Size(256, 320) : new Avalonia.Size(pixels.Size.Width, pixels.Size.Height);
        var image = ArchivePageRenderer.Fit(area.Deflate(Math.Min(8, Math.Min(area.Width, area.Height) / 4)), size);
        Fill(canvas, image.Translate(new Avalonia.Vector(6, 6)), border); Fill(canvas, image, background);
        using (var pen = new SKPaint { Color = border, Style = SKPaintStyle.Stroke, StrokeWidth = 2 }) canvas.DrawRect(Rect(image), pen);
        if (pixels is not null && retain is not null)
        {
            var target = image.Deflate(Math.Min(2, Math.Min(image.Width, image.Height) / 4));
            PageBackground(canvas, target);
            Image(canvas, pixels, new(0, 0, pixels.Size.Width, pixels.Size.Height), target, retain, ReaderImageRenderer.Interpolation(owner, new(0, 0, pixels.Size.Width, pixels.Size.Height), target, matrix));
        }
        else
        {
            var icon = ArchivePageRenderer.Fit(image.Deflate(Math.Min(image.Width, image.Height) / 4), new(16, 18));
            int save = canvas.Save(); canvas.Translate((float)icon.X, (float)icon.Y); canvas.Scale((float)icon.Width / 16, (float)icon.Height / 18);
            using var path = SKPath.ParseSvgPathData("M0,0 L14,0 14,10 16,12 16,18 0,18 Z M14,10 L13,12 13,18");
            using var pen = new SKPaint { Color = border, Style = SKPaintStyle.Stroke, StrokeWidth = 1 }; canvas.DrawPath(path, pen); canvas.RestoreToCount(save);
        }
        int clip = canvas.Save(); canvas.ClipRect(Rect(card));
        try
        {
            var info = new Point(card.X + 12, card.Y + card.Height * .77);
            Text(canvas, page.EntryName, info, 14, foreground); Text(canvas, (page.PageType == PageType.Folder ? "文件夹" : "压缩包") + " · 双击封面打开", info + new Avalonia.Vector(0, 25), 11, foreground);
            if (status.Length > 0 && pixels is null) Text(canvas, status, info + new Avalonia.Vector(0, 46), 11, foreground);
        }
        finally { canvas.RestoreToCount(clip); }
    }
    /// <summary>原image目标网格属于页框合成面；screen网格仍由查看器在效果外绘制。</summary>
    internal static void Grid(SKCanvas canvas, Avalonia.Rect bounds)
    {
        var c = Config.Current.ImageGrid;
        if (!c.IsEnabled || c.Target != ImageGridTarget.Image || bounds.Width <= 0 || bounds.Height <= 0) return;
        int save = canvas.Save(); canvas.ClipRect(Rect(bounds));
        try
        {
            using var pen = new SKPaint { Color = Color(c.Color), Style = SKPaintStyle.Stroke, StrokeWidth = 1 };
            double dx = c.DivX > 0 ? bounds.Width / c.DivX : bounds.Width, dy = c.DivY > 0 ? bounds.Height / c.DivY : bounds.Height;
            if (c.IsSquare) dx = dy = Math.Max(dx, dy);
            canvas.DrawRect(Rect(bounds), pen);
            for (int i = 1; i <= 4096 && i * dx < bounds.Width - 1; i++) canvas.DrawLine((float)(bounds.X + i * dx), (float)bounds.Y, (float)(bounds.X + i * dx), (float)bounds.Bottom, pen);
            for (int i = 1; i <= 4096 && i * dy < bounds.Height - 1; i++) canvas.DrawLine((float)bounds.X, (float)(bounds.Y + i * dy), (float)bounds.Right, (float)(bounds.Y + i * dy), pen);
        }
        finally { canvas.RestoreToCount(save); }
    }
}
