using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using ImageMagick;
using NeeView.Effects;
using NeeView.MacOS.Views;
using SkiaSharp;
namespace NeeView.Engine.Tests;

/// <summary>整视口、离散模糊、真实设备几何和记录的所有权；期望来自原作用范围而非实现代码。</summary>
public sealed class ImageEffectSceneTests
{
    private sealed class Release(Action action) : IDisposable { private int _released; public void Dispose() { if (Interlocked.Exchange(ref _released, 1) == 0) action(); } }
    private static MagickImage Draw(EffectUnit effect, Action<SKCanvas> source, int width = 100, int height = 100, int scale = 1, Avalonia.Rect? bounds = null, double opacity = 1)
    {
        Config.SetCurrent(new() { ImageEffect = new() { IsEnabled = true, Layers = new() { new() { Effect = effect } } } });
        var result = new ImageEffectRenderResult();
        using var bitmap = new RenderTargetBitmap(new(width * scale, height * scale));
        using (var context = bitmap.CreateDrawingContext())
        {
            using var matrix = context.PushTransform(Avalonia.Matrix.CreateScale(scale, scale));
            using var alpha = context.PushOpacity(opacity);
            Assert.True(ImageEffectRenderer.DrawScene(context, bounds ?? new(0, 0, width, height), source, true, result));
        }
        result.ThrowIfFailed(); using var stream = new MemoryStream(); bitmap.Save(stream, PngBitmapEncoderOptions.Default);
        return new MagickImage(stream.ToArray());
    }
    private static IMagickColor<byte> Pixel(MagickImage image, int x, int y) => image.GetPixels().GetPixel(x, y).ToColor()!;
    [Theory]
    [InlineData(5.9, 2, 10)] [InlineData(5.9, .5, 2)] [InlineData(.9, 4, 0)] [InlineData(1000, 2, 100)] [InlineData(-1, 2, 0)]
    public void BlurTruncatesBeforeAndAfterDeviceScale(double value, double scale, int expected) => Assert.Equal(expected, ImageBlurKernel.DeviceRadius(value, scale));
    [Fact] public void GaussianAddsOriginalCorrectionRatherThanDividingWeights()
    {
        var kernel = ImageBlurKernel.Weights(1);
        Assert.InRange(kernel[0], -.061178f, -.061176f); Assert.InRange(kernel[1], 1.122353f, 1.122355f);
        Assert.Equal(kernel[0], kernel[2]); Assert.InRange(kernel.Sum(), .999999f, 1.000001f);
        // 原半径1均匀修正会产生负边权；直接除总和约.010868，是不同的核。
        Assert.Equal(new float[] { 1 }, ImageBlurKernel.Weights(0));
    }
    [Theory] [InlineData(false,85)] [InlineData(true,51)]
    public void ShaderAutoSamplerPreservesCpuGpuSubpixelDifference(bool gpu, int expected)
    {
        // 软件表面分别验证原两种采样策略，不冒充真实GPU验收。
        using var source=SKSurface.Create(new SKImageInfo(4,1));
        for(int x=0;x<4;x++)ReaderEffectScene.Fill(source.Canvas,new(x,0,1,1),new SKColor((byte)(x*85),0,0));
        using var image=source.Snapshot();
        using var sample=image.ToShader(SKShaderTileMode.Clamp,SKShaderTileMode.Clamp,ImageEffectRenderer.ShaderSampling(gpu));
        using var normalized=sample.WithLocalMatrix(SKMatrix.CreateScale(.25f,1));
        using var effect=ImageSpatialEffectRenderer.Create(new SharpenEffectUnit{Height=50,Amount=1},normalized,4,1);
        using var mapped=effect.WithLocalMatrix(SKMatrix.CreateScale(4,1));
        using var target=SKSurface.Create(new SKImageInfo(4,1));using var paint=new SKPaint{Shader=mapped};
        target.Canvas.DrawRect(new SKRect(0,0,4,1),paint);
        using var result=target.Snapshot();using var encoded=result.Encode();using var pixels=new MagickImage(encoded.ToArray());
        Assert.InRange(Pixel(pixels,1,0).R,expected-1,expected+1);
    }
    [AvaloniaFact] public void PixelateSamplesAcrossTheTwoPageSeamInOneViewport()
    {
        using var image = Draw(new PixelateEffectUnit { Pixelation = .93 }, canvas =>
        {
            ReaderEffectScene.Fill(canvas, new(0, 0, 47, 100), SKColors.Red);
            ReaderEffectScene.Fill(canvas, new(47, 0, 53, 100), SKColors.Blue);
        });
        var seam = Pixel(image, 45, 20); Assert.InRange(seam.B, 253, 255); Assert.InRange(seam.R, 0, 2);
    }
    [AvaloniaFact] public void BlurSpreadsBetweenPagesThroughTransparentGap()
    {
        using var image = Draw(new BlurEffectUnit { Radius = 5 }, canvas =>
        {
            ReaderEffectScene.Fill(canvas, new(10, 10, 38, 80), SKColors.Red);
            ReaderEffectScene.Fill(canvas, new(52, 10, 38, 80), SKColors.Blue);
        });
        var gap = Pixel(image, 50, 50); Assert.True(gap.A > 10); Assert.True(gap.R > 10 && gap.B > 10);
        Assert.Equal((byte)0, Pixel(image, 0, 0).A);
        Assert.InRange(Pixel(image, 30, 50).R, 254, 255);
    }
    [AvaloniaFact] public void BlurExtendsOutsideInputBoundsAndCumulativeMarginsKeepCenter()
    {
        using var image = Draw(new BlurEffectUnit { Radius = 5 }, canvas => ReaderEffectScene.Fill(canvas, new(30, 30, 40, 40), SKColors.Red), bounds: new(30, 30, 40, 40));
        Assert.True(Pixel(image, 28, 50).A > 5); Assert.Equal((byte)0, Pixel(image, 20, 50).A);
        Assert.InRange(Pixel(image, 50, 50).A, 254, 255);
        // 在两次模糊之后中心不移动，外扩边界累计；颜色层必须在模糊后运行。
        Config.SetCurrent(new() { ImageEffect = new() { IsEnabled = true, Layers = new() {
            new() { Effect = new HsvEffectUnit { Hue = 120 } }, new() { Effect = new BlurEffectUnit { Radius = 5 } }, new() { Effect = new BlurEffectUnit { Radius = 5 } } } } });
        using var bitmap = new RenderTargetBitmap(new(100, 100)); var result = new ImageEffectRenderResult();
        using (var context = bitmap.CreateDrawingContext()) ImageEffectRenderer.DrawScene(context, new(30, 30, 40, 40), canvas => ReaderEffectScene.Fill(canvas, new(30, 30, 40, 40), SKColors.Red), true, result);
        result.ThrowIfFailed(); using var stream = new MemoryStream(); bitmap.Save(stream, PngBitmapEncoderOptions.Default); using var multiple = new MagickImage(stream.ToArray());
        Assert.InRange(Pixel(multiple, 50, 50).G, 253, 255); Assert.True(Pixel(multiple, 27, 50).A > 5);
    }
    [AvaloniaFact] public void FailedRecordingReleasesBorrowedPixelsAndReportsError()
    {
        using var pixels = new DecodedImageLease(new(1, 1), new byte[] { 0, 0, 255, 255 }); int active = 0;
        Config.SetCurrent(new() { ImageEffect = new() { IsEnabled = true, Layers = new() { new() { Effect = new SwirlEffectUnit() } } } });
        using var bitmap = new RenderTargetBitmap(new(100, 100)); var result = new ImageEffectRenderResult();
        using (var context = bitmap.CreateDrawingContext()) ImageEffectRenderer.DrawScene(context, new(0, 0, 100, 100), canvas =>
        {
            ReaderEffectScene.Image(canvas, pixels, new(0, 0, 1, 1), new(0, 0, 100, 100), () => { active++; return new Release(() => active--); }, BitmapInterpolationMode.None);
            throw new IOException("scene recording failure");
        }, true, result);
        Assert.Equal("scene recording failure", result.Error); Assert.Equal(0, active);
    }
    [AvaloniaTheory] [InlineData(1)] [InlineData(2)]
    public void CroppedHalfPageAndRotationUseTheActualRetinaGeometry(int scale)
    {
        var bytes = new byte[100 * 100 * 4];
        for (int y = 0; y < 100; y++) for (int x = 0; x < 100; x++) { int i = (y * 100 + x) * 4; bytes[i + (x < 50 ? 2 : 0)] = 255; bytes[i + 3] = 255; }
        using var pixels = new DecodedImageLease(new(100, 100), bytes); int active = 0;
        using var image = Draw(new MagnifyEffectUnit { Amount = 0 }, canvas =>
        {
            int save = canvas.Save(); canvas.Translate(80, 20); canvas.RotateDegrees(90);
            ReaderEffectScene.Image(canvas, pixels, new(50, 0, 50, 100), new(0, 0, 50, 60), () => { active++; return new Release(() => active--); }, BitmapInterpolationMode.None);
            canvas.RestoreToCount(save);
        }, scale: scale);
        Assert.Equal(0, active); var color = Pixel(image, 50 * scale, 40 * scale);
        Assert.Equal((byte)255, color.B); Assert.Equal((byte)0, color.R); Assert.Equal((byte)0, Pixel(image, 10 * scale, 10 * scale).A);
    }
    [AvaloniaFact] public void NonzeroSceneOriginAndOuterOpacityArePreserved()
    {
        using var image = Draw(new SwirlEffectUnit { TwistAmount = 0 }, canvas => ReaderEffectScene.Fill(canvas, new(20, 10, 50, 60), SKColors.Red), bounds: new(20, 10, 50, 60), opacity: .5);
        Assert.Equal((byte)255, Pixel(image, 40, 40).R); Assert.InRange(Pixel(image, 40, 40).A, 127, 129); Assert.Equal((byte)0, Pixel(image, 5, 5).A);
    }
    [AvaloniaFact] public void PageBackgroundIsIncludedButWindowBackgroundIsOutsideEffects()
    {
        Config.SetCurrent(new() { Background = new() { PageBackgroundColor = ThemeRgba.Parse("Red") } });
        using var pixels = new DecodedImageLease(new(1, 1), new byte[4]);
        // Draw初始化Config，背景在同步记录回调中设置，模拟正式独立背景配置。
        using var image = Draw(new HsvEffectUnit { Hue = 120 }, canvas =>
        {
            Config.Current.Background.PageBackgroundColor = ThemeRgba.Parse("Red");
            ReaderEffectScene.PageBackground(canvas, new(10, 10, 80, 80));
            ReaderEffectScene.Image(canvas, pixels, new(0, 0, 1, 1), new(10, 10, 80, 80), () => new Release(() => { }), BitmapInterpolationMode.None);
        });
        Assert.InRange(Pixel(image, 50, 50).G, 253, 255); Assert.Equal((byte)0, Pixel(image, 0, 0).A);
    }
    [AvaloniaFact] public void HighQualityUsesFrameworkMitchellAndDownsamplingMipmap()
    {
        var high = ImageSpatialEffectRenderer.Sampling(BitmapInterpolationMode.HighQuality, true);
        Assert.True(high.UseCubic); Assert.Equal(SKCubicResampler.Mitchell.B, high.Cubic.B); Assert.Equal(SKCubicResampler.Mitchell.C, high.Cubic.C);
        Assert.Equal(SKMipmapMode.Linear, ImageSpatialEffectRenderer.Sampling(BitmapInterpolationMode.HighQuality, false).Mipmap);
        using var image = Draw(new MagnifyEffectUnit { Amount = 0 }, canvas =>
        {
            using var pixels = new DecodedImageLease(new(2, 1), new byte[] { 0, 0, 255, 255, 255, 0, 0, 255 });
            ReaderEffectScene.Image(canvas, pixels, new(0, 0, 2, 1), new(0, 0, 100, 100), () => new Release(() => { }), BitmapInterpolationMode.HighQuality);
        });
        var edge = Pixel(image, 48, 50); Assert.True(edge.R > 0 && edge.B > 0);
    }
    [AvaloniaFact] public void OversizeSceneFailsBeforeSurfaceAllocationAndReturnsSourceReference()
    {
        using var pixels = new DecodedImageLease(new(1, 1), new byte[] { 0, 0, 255, 255 }); int acquired = 0, released = 0;
        Config.SetCurrent(new() { ImageEffect = new() { IsEnabled = true, Layers = new() { new() { Effect = new SwirlEffectUnit() } } } });
        using var bitmap = new RenderTargetBitmap(new(4, 4)); var result = new ImageEffectRenderResult();
        using (var context = bitmap.CreateDrawingContext()) ImageEffectRenderer.DrawScene(context, new(0, 0, 8192, 8192), canvas =>
            ReaderEffectScene.Image(canvas, pixels, new(0, 0, 1, 1), new(0, 0, 8192, 8192), () => { acquired++; return new Release(() => released++); }, BitmapInterpolationMode.None), true, result);
        Assert.Contains("budget", result.Error); Assert.Equal(acquired, released); Assert.True(acquired > 0);
    }
    private sealed class Probe(DecodedImageLease pixels, Action acquired, Action released) : Control
    {
        public override void Render(DrawingContext context) => ImageEffectRenderer.DrawScene(context, new(Bounds.Size), canvas => ReaderEffectScene.Image(canvas, pixels, new(0, 0, 1, 1), new(Bounds.Size), () => { acquired(); return new Release(released); }, BitmapInterpolationMode.None));
    }
    [AvaloniaFact] public async Task PictureSceneKeepsPixelsUntilRedrawAndWindowClose()
    {
        using var pixels = new DecodedImageLease(new(1, 1), new byte[] { 0, 0, 255, 255 }); int acquired = 0, released = 0;
        Config.SetCurrent(new() { ImageEffect = new() { IsEnabled = true, Layers = new() { new() { Effect = new RippleEffectUnit() } } } });
        var probe = new Probe(pixels, () => acquired++, () => released++); var window = new Window { Width = 40, Height = 40, Content = probe }; window.Show();
        try
        {
            using (var frame = window.CaptureRenderedFrame()) Assert.NotNull(frame);
            Assert.True(acquired > released);
            probe.InvalidateVisual(); using (var frame = window.CaptureRenderedFrame()) Assert.NotNull(frame); Assert.True(released > 0);
        }
        finally { window.Close(); }
        for (int i = 0; i < 100 && acquired != released; i++) { Dispatcher.UIThread.RunJobs(); await Task.Delay(10, TestContext.Current.CancellationToken); }
        Assert.Equal(acquired, released);
    }
}
