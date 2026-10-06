using Avalonia;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using ImageMagick;
using NeeView.Effects;
using NeeView.MacOS.Views;
namespace NeeView.Engine.Tests;
public sealed class ImageEffectRenderTests
{
    private sealed class Lease(Action release) : IDisposable { public void Dispose() => release(); }
    private static Color Draw(Bitmap source, ImageEffectConfig effect, double opacity = 1)
    {
        Config.SetCurrent(new() { ImageEffect = effect });
        using var target = new RenderTargetBitmap(new PixelSize(4,4));
        using (var context = target.CreateDrawingContext())
        {
            using var alpha = context.PushOpacity(opacity);
            Assert.True(ImageEffectRenderer.Draw(context,source,new(0,0,4,4),new(0,0,4,4),()=>new Lease(()=>{}), immediate:true));
        }
        using var stream = new MemoryStream(); target.Save(stream, PngBitmapEncoderOptions.Default); stream.Position=0; using var decoded=new MagickImage(stream); using var pixels=decoded.GetPixels(); var c=pixels.GetPixel(2,2).ToColor()!;
        return Color.FromArgb(c.A,c.R,c.G,c.B);
    }
    private static Bitmap Source(MagickColor color)
    { using var image=new MagickImage(color,4,4); using var stream=new MemoryStream(); image.Write(stream,MagickFormat.Png); stream.Position=0; return new Bitmap(stream); }
    [AvaloniaTheory] [InlineData(1)] [InlineData(.5)]
    public void RealSkiaChangesHueAndPreservesOpacity(double opacity)
    {
        using var source=Source(MagickColors.Red);
        var result=Draw(source,new(){IsEnabled=true,Layers=new(){new(){Effect=new HsvEffectUnit{Hue=120}}}},opacity);
        Assert.InRange(result.R,0,2); Assert.InRange(result.G,253,255); Assert.InRange(result.B,0,2); Assert.InRange(result.A,(int)(255*opacity)-1,(int)(255*opacity)+1);
    }
    [AvaloniaFact] public void TransparentPixelsNeverAcquireHalo()
    {
        using var source=Source(MagickColors.Transparent);
        var result=Draw(source,new(){IsEnabled=true,Layers=new(){new(){Effect=new LevelEffectUnit{Minimum=.5}}}});
        Assert.Equal(0,result.A);
    }
    [AvaloniaFact] public void OriginalOuterToInnerCollectionExecutesInReverse()
    {
        using var source=Source(new MagickColor("#808080"));
        var inner=new LevelEffectUnit{Minimum=.5,Maximum=.5}; var outer=new HsvEffectUnit{Value=-.5};
        var result=Draw(source,new(){IsEnabled=true,Layers=new(){new(){Effect=outer},new(){Effect=inner}}});
        Assert.InRange(result.R,62,66); Assert.InRange(result.G,62,66); Assert.InRange(result.B,62,66);
    }
    [AvaloniaFact] public void ColorizeUsesOriginalLuminanceTableAndAlpha()
    {
        using var source=Source(MagickColors.White); var unit=new ColorizeEffectUnit{LuminanceWeight=0}; unit.Points.Clear(); unit.Points.Add(new(ThemeRgba.Parse("#800000FF"),1));unit.Points.Add(new(ThemeRgba.Parse("#800000FF"),1));
        var result=Draw(source,new(){IsEnabled=true,Layers=new(){new(){Effect=unit}}});
        Assert.InRange(result.B,253,255);Assert.InRange(result.R,0,2);Assert.InRange(result.A,127,129);
    }
    [AvaloniaFact] public void ChangingExistingColorizePointInvalidatesRetainedSnapshot()
    {
        using var source=Source(MagickColors.White);var unit=new ColorizeEffectUnit{LuminanceWeight=0};unit.Points.Clear();unit.Points.Add(new(ThemeRgba.Parse("Red"),1));unit.Points.Add(new(ThemeRgba.Parse("Red"),1));
        var effect=new ImageEffectConfig{IsEnabled=true,Layers=new(){new(){Effect=unit}}};Assert.InRange(Draw(source,effect).R,253,255);
        foreach(var point in unit.Points)point.Color=ThemeRgba.Parse("Blue");Assert.InRange(Draw(source,effect).B,253,255);
    }
    [AvaloniaFact] public void MonochromeUsesOriginalThirtyFiftyNineElevenWeightsAndTint()
    {
        using var source=Source(MagickColors.Red);
        var result=Draw(source,new(){IsEnabled=true,Layers=new(){new(){Effect=new MonochromeEffectUnit{Color=ThemeRgba.Parse("Blue")}}}});
        Assert.Equal(0,result.R);Assert.Equal(0,result.G);Assert.InRange(result.B,75,77);Assert.Equal(255,result.A);
    }
    [AvaloniaFact] public void ColorTonePreservesDesaturationBeforeToneInterpolation()
    {
        using var source=Source(MagickColors.Red);
        var tone=new ColorToneEffectUnit{LightColor=ThemeRgba.Parse("White"),DarkColor=ThemeRgba.Parse("Black"),Desaturation=.25,ToneAmount=.5};
        var result=Draw(source,new(){IsEnabled=true,Layers=new(){new(){Effect=tone}}});
        // 原shader: (.825,.075,.075)与(.3,.3,.3)各半；不是先tone再去色。
        Assert.InRange(result.R,142,144);Assert.InRange(result.G,47,49);Assert.InRange(result.B,47,49);Assert.Equal(255,result.A);
    }
    [AvaloniaFact] public void BloomPreservesPackedBaseAndBloomComponents()
    {
        using var source=Source(new MagickColor("#CC6633"));
        var bloom=new BloomEffectUnit();var effect=new ImageEffectConfig{IsEnabled=true,Layers=new(){new(){Effect=bloom}}};
        var result=Draw(source,effect);Assert.InRange(result.R,250,252);Assert.InRange(result.G,139,141);Assert.InRange(result.B,50,52);Assert.Equal(255,result.A);
        bloom.BloomIntensity=0;result=Draw(source,effect);Assert.InRange(result.R,203,205);Assert.InRange(result.G,101,103);
        bloom.BaseIntensity=0;result=Draw(source,effect);Assert.Equal(0,result.A);
    }
    [AvaloniaTheory] [InlineData(-1)] [InlineData(1)]
    public void BloomThresholdEndpointsRemainFiniteAndExportable(double threshold)
    {
        using var source=Source(MagickColors.White);
        var effect=new ImageEffectConfig{IsEnabled=true,Layers=new(){new(){Effect=new BloomEffectUnit{Threshold=threshold}}}};
        var result=Draw(source,effect);Assert.Equal(255,result.A);ImageEffectRenderer.EnsureExportSupported();
    }
    [AvaloniaFact] public void BloomUsesOriginalPremultipliedFourChannelInput()
    {
        using var source=Source(new MagickColor("#66000080"));
        var result=Draw(source,new(){IsEnabled=true,Layers=new(){new(){Effect=new BloomEffectUnit()}}});
        // 原输入(.20078,0,0,.50196)，RGB未越过阈值，alpha的bloom仍参与合成。
        Assert.InRange(result.A,180,182);Assert.InRange(result.R,71,73);Assert.Equal(0,result.G);Assert.Equal(0,result.B);
    }
    [AvaloniaFact] public void UnsupportedEffectsAreReportedAndBlockViewExport()
    {
        Config.SetCurrent(new(){ImageEffect=new(){IsEnabled=true,Layers=new(){new(){Effect=new RippleEffectUnit()}}}});
        Assert.Equal("Ripple",ImageEffectRenderer.Unsupported(Config.Current.ImageEffect)); Assert.Throws<NotSupportedException>(ImageEffectRenderer.EnsureExportSupported);
        Config.Current.ImageEffect.Layers[0].IsEnabled=false; Assert.Null(ImageEffectRenderer.Unsupported(Config.Current.ImageEffect)); ImageEffectRenderer.EnsureExportSupported();
    }
}
