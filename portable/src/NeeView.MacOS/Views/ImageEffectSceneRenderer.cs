using Avalonia;
using Avalonia.Media;
using Avalonia.Rendering.SceneGraph;
using Avalonia.Skia;
using NeeView.Effects;
using SkiaSharp;
namespace NeeView.MacOS.Views;

internal static partial class ImageEffectRenderer
{
    /// <summary>按原EffectPanel先合成整个阅读视口，再逆序执行层。记录绘图命令，不复制源像素。</summary>
    /// <param name="bounds">原视口或导出画布；几何已应用到记录中的页面。</param>
    /// <param name="record">UI线程同步记录，回调不留到渲染线程。</param>
    /// <param name="immediate">离屏绘制提交后立即归还记录；scene-graph由框架归还。</param>
    internal static bool DrawScene(DrawingContext context, Avalonia.Rect bounds, Action<SKCanvas> record, bool immediate = false, ImageEffectRenderResult? result = null)
    {
        var config = Config.Current.ImageEffect;
        if (!config.IsEnabled || !config.Layers.Any(x => x.IsEnabled && x.Effect is not null)) return false;
        var snapshot = Snapshot(config);
        SKPicture? picture = null;
        if (snapshot.Error is null)
        {
            try
            {
                using var recorder = new SKPictureRecorder();
                var canvas = recorder.BeginRecording(ReaderEffectScene.Rect(bounds));
                canvas.ClipRect(ReaderEffectScene.Rect(bounds));
                record(canvas);
                picture = recorder.EndRecording();
            }
            catch (Exception ex) { snapshot = new([], ex.Message); }
        }
        var operation = new SceneOperation(bounds, picture, snapshot, result);
        bool submitted = false;
        try { context.Custom(operation); submitted = true; }
        finally { if (!submitted || immediate) operation.Dispose(); }
        return true;
    }
    private sealed class SceneOperation(Avalonia.Rect bounds, SKPicture? picture, EffectSnapshot snapshot, ImageEffectRenderResult? result) : ICustomDrawOperation
    {
        // 原WPF GetRenderBounds扩张的是效果外层，不能把内层ScrollViewer clip误当作外层裁剪。
        public Avalonia.Rect Bounds => bounds.Inflate(snapshot.Effects.OfType<BlurEffectUnit>().Sum(x => Math.Floor(Math.Max(0, x.Radius))));
        public bool HitTest(Avalonia.Point point) => false;
        public bool Equals(ICustomDrawOperation? other) => ReferenceEquals(this, other);
        private int _disposed;
        public void Dispose() { if (Interlocked.Exchange(ref _disposed, 1) == 0) picture?.Dispose(); }
        public void Render(ImmediateDrawingContext context)
        {
            try
            {
                if (snapshot.Error is { } error) throw new InvalidDataException(error);
                if (context.TryGetFeature(typeof(ISkiaSharpApiLeaseFeature)) is not ISkiaSharpApiLeaseFeature feature)
                    throw new NotSupportedException("Skia backend required");
                using var lease = feature.Lease();
                RenderScene(lease, picture!, bounds, snapshot.Effects);
            }
            catch (Exception ex)
            {
                result?.Fail(ex.Message);
                context.FillRectangle(Brushes.DarkRed, bounds);
                System.Diagnostics.Trace.WriteLine("Image effect scene failed: " + ex.Message);
                if (context.TryGetFeature(typeof(ISkiaSharpApiLeaseFeature)) is ISkiaSharpApiLeaseFeature feature)
                {
                    using var lease = feature.Lease(); using var paint = new SKPaint { Color = SKColors.Orange }; using var font = new SKFont(SKTypeface.Default, 14);
                    lease.SkCanvas.DrawText("Image effect unavailable", (float)bounds.X + 8, (float)bounds.Y + 24, font, paint);
                    lease.SkCanvas.DrawText(ex.Message, (float)bounds.X + 8, (float)bounds.Y + 44, font, paint);
                }
            }
        }
    }
    /// <summary>每层有界设备像素表面，CPU/GPU共用同一算法；源图、页框、导出及状态不被改写。</summary>
    private static void RenderScene(ISkiaSharpApiLease lease, SKPicture picture, Avalonia.Rect bounds, EffectUnit[] effects)
    {
        if (bounds.Width <= 0 || bounds.Height <= 0) return;
        var m = lease.SkCanvas.TotalMatrix;
        double sx = Math.Sqrt(m.ScaleX * m.ScaleX + m.SkewY * m.SkewY), sy = Math.Sqrt(m.ScaleY * m.ScaleY + m.SkewX * m.SkewX);
        int width = Dimension(bounds.Width * sx), height = Dimension(bounds.Height * sy);
        CheckWork(width, height, width, height);
        double dx = width / bounds.Width, dy = height / bounds.Height;
        SKSurface? surface = Create(width, height);
        int margin = 0;
        try
        {
            surface.Canvas.Clear(SKColors.Transparent);
            surface.Canvas.Scale((float)dx, (float)dy); surface.Canvas.Translate((float)-bounds.X, (float)-bounds.Y);
            surface.Canvas.DrawPicture(picture); surface.Canvas.ResetMatrix();
            foreach (var effect in effects)
            {
                if (effect is BlurEffectUnit blur)
                {
                    int radius = ImageBlurKernel.DeviceRadius(blur.Radius, Math.Min(sx, sy));
                    if (radius == 0) continue;
                    PassBlur(radius, false); PassBlur(radius, true); margin += radius;
                }
                else
                {
                    using var image = surface.Snapshot();
                    using var input = image.ToShader(SKShaderTileMode.Clamp, SKShaderTileMode.Clamp, ShaderSampling(lease.GrContext is not null));
                    if (ImageSpatialEffectRenderer.IsSpatial(effect))
                    {
                        // 原空间shader输入是归一化的已合成表面，Ddx/Ddy使用完整表面设备尺寸。
                        using var normalized = input.WithLocalMatrix(SKMatrix.CreateScale(1f / width, 1f / height));
                        using var shader = ImageSpatialEffectRenderer.Create(effect, normalized, width, height);
                        using var pixels = shader.WithLocalMatrix(SKMatrix.CreateScale(width, height));
                        Pass(width, height, pixels);
                    }
                    else
                    {
                        using var filter = CreateFilter(effect); using var shader = input.WithColorFilter(filter);
                        Pass(width, height, shader);
                    }
                }
            }
            using var final = surface.Snapshot();
            using var paint = new SKPaint { Color = SKColors.White.WithAlpha((byte)Math.Clamp(Math.Round(lease.CurrentOpacity * 255), 0, 255)) };
            var target = new SKRect((float)(bounds.X - margin / dx), (float)(bounds.Y - margin / dy),
                (float)(bounds.Right + margin / dx), (float)(bounds.Bottom + margin / dy));
            lease.SkCanvas.DrawImage(final, target, new SKSamplingOptions(SKFilterMode.Linear), paint);
        }
        finally { surface?.Dispose(); }
        SKSurface Create(int w, int h) => (lease.GrContext is { } gpu ? SKSurface.Create(gpu, false, new SKImageInfo(w, h, SKColorType.Bgra8888, SKAlphaType.Premul)) :
            SKSurface.Create(new SKImageInfo(w, h, SKColorType.Bgra8888, SKAlphaType.Premul))) ?? throw new InvalidOperationException("无法分配效果工作表面。");
        void Pass(int w, int h, SKShader shader)
        {
            CheckWork(width, height, w, h);
            var next = Create(w, h);
            try
            {
                next.Canvas.Clear(SKColors.Transparent);
                using var paint = new SKPaint { Shader = shader }; next.Canvas.DrawRect(new SKRect(0, 0, w, h), paint);
            }
            catch { next.Dispose(); throw; }
            surface!.Dispose(); surface = next; width = w; height = h;
        }
        void PassBlur(int radius, bool horizontal)
        {
            using var image = surface!.Snapshot();
            using var input = image.ToShader(SKShaderTileMode.Decal, SKShaderTileMode.Decal, new SKSamplingOptions(SKFilterMode.Nearest));
            using var shader = ImageBlurKernel.CreateShader(input, radius, horizontal);
            Pass(width + (horizontal ? 2 * radius : 0), height + (horizontal ? 0 : 2 * radius), shader);
        }
    }
    /// <summary>原WPF ShaderEffect SamplingMode.Auto：GPU为bilinear，CPU为nearest；Blur独立采用整数采样。</summary>
    internal static SKSamplingOptions ShaderSampling(bool gpu) => new(gpu ? SKFilterMode.Linear : SKFilterMode.Nearest);
    private static int Dimension(double value) => double.IsFinite(value) && value >= 1 && value <= 32768 ? (int)Math.Ceiling(value) : throw new InvalidDataException("Invalid effect surface size");
    /// <summary>显式源/目标BGRA表面的字节合计；驱动/框架额外工作内存需另行测量。</summary>
    private static void CheckWork(int width, int height, int nextWidth, int nextHeight)
    {
        if (nextWidth > 32768 || nextHeight > 32768 || ((long)width * height + (long)nextWidth * nextHeight) * 4 > 128L * 1024 * 1024)
            throw new NotSupportedException("Effect work surface budget exceeded (128 MiB)");
    }
}
