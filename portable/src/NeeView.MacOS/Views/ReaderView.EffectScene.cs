using Avalonia;
using Avalonia.Media.Imaging;
using NeeView.PageFrames;
using SkiaSharp;
namespace NeeView.MacOS.Views;

public sealed partial class ReaderView
{
    /// <summary>原页框绘图记录，只有实际可见资源进入整视口效果；不新建页面或解码需求。</summary>
    private void RecordFrame(SKCanvas canvas, IEnumerable<(PageFrameElement Source, Avalonia.Rect Target)> targets, Matrix matrix,
        Dictionary<Page, Display> images, Dictionary<Page, string> errors, double opacity, BitmapInterpolationMode? sampling = null, bool export = false)
    {
        if (opacity <= 0) return;
        int save = canvas.Save(); canvas.Concat(ReaderEffectScene.Matrix(matrix));
        if (opacity < 1) { using var alpha = new SKPaint { Color = SKColors.White.WithAlpha((byte)Math.Clamp(Math.Round(opacity * 255), 0, 255)) }; canvas.SaveLayer(alpha); }
        try
        {
            Avalonia.Rect? frameBounds = null;
            foreach (var (source, target) in targets)
            {
                frameBounds = frameBounds is { } previous ? previous.Union(target) : target;
                if (source.IsDummy) ReaderEffectScene.Fill(canvas, target, SKColors.White);
                else if (source.Page.PageType.IsFolder())
                {
                    var cover = images.GetValueOrDefault(source.Page);
                    ReaderEffectScene.Card(this, canvas, source.Page, target, cover?.Pixels, cover is null ? null : cover.Retain,
                        errors.GetValueOrDefault(source.Page) ?? "正在加载…", matrix);
                }
                else if (images.TryGetValue(source.Page, out var image))
                {
                    if (ReferenceEquals(images, _images)) image = GetMediaDisplay(source.Page, image);
                    var crop = source.ViewSizeCalculator.GetViewBox(); var size = image.Pixels.Size;
                    var rect = new Avalonia.Rect(crop.X * size.Width, crop.Y * size.Height, crop.Width * size.Width, crop.Height * size.Height);
                    ReaderEffectScene.PageBackground(canvas, target);
                    ReaderEffectScene.Image(canvas, image.Pixels, rect, target, image.Retain, sampling ?? ReaderImageRenderer.Interpolation(this, rect, target, matrix));
                    if (errors.GetValueOrDefault(source.Page) is { Length: > 0 } error)
                    {
                        int clip = canvas.Save(); canvas.ClipRect(ReaderEffectScene.Rect(target));
                        ReaderEffectScene.Fill(canvas, new(target.TopLeft, new Avalonia.Size(target.Width, 36)), new SKColor(32, 32, 32, 217));
                        ReaderEffectScene.Text(canvas, "动画未播放：" + error, target.TopLeft + new Avalonia.Vector(8, 8)); canvas.RestoreToCount(clip);
                    }
                }
                else
                {
                    ReaderEffectScene.Fill(canvas, target, new SKColor(32, 32, 32));
                    ReaderEffectScene.Text(canvas, source.Page.Content.Error ?? errors.GetValueOrDefault(source.Page) ?? "正在加载…", target.TopLeft + new Avalonia.Vector(12, 12));
                }
            }
            if (!export && frameBounds is { } bounds) ReaderEffectScene.Grid(canvas, bounds);
        }
        finally { canvas.RestoreToCount(save); }
    }
    private void RecordPanorama(SKCanvas canvas)
    {
        RebuildPanorama();
        foreach (var placement in _panorama?.Frames ?? [])
            RecordFrame(canvas, ReaderTransformPresenter.GetTargets(placement.Frame), PanoramaMatrix(placement), _images, _pageErrors, 1);
    }
}
