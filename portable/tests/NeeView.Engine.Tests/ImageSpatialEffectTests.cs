using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Headless.XUnit;
using ImageMagick;
using NeeView.Effects;
using NeeView.MacOS.Views;
namespace NeeView.Engine.Tests;

/// <summary>原shader采样位置/通道的可复核夹具，不依靠屏幕截图判断算法。</summary>
public sealed class ImageSpatialEffectTests
{
    private sealed class Release(Action action):IDisposable{private int _released;public void Dispose(){if(Interlocked.Exchange(ref _released,1)==0)action();}}
    private static MagickImage Draw(EffectUnit effect, out int references, int width=100, int height=100, bool transparent=false, EffectUnit? outer=null)
    {
        var bytes=new byte[100*100*4];
        for(int y=0;y<100;y++)for(int x=0;x<100;x++){int i=(y*100+x)*4;bytes[i]=0;bytes[i+1]=(byte)(y*255/99);bytes[i+2]=(byte)(x*255/99);bytes[i+3]=255;if(transparent){bytes[i+1]/=2;bytes[i+2]/=2;bytes[i+3]=128;}}
        using var pixels=new DecodedImageLease(new(100,100),bytes);var pin=GCHandle.Alloc(bytes,GCHandleType.Pinned);Bitmap bitmap;
        try{bitmap=new(PixelFormat.Bgra8888,AlphaFormat.Premul,pin.AddrOfPinnedObject(),new(100,100),new(96,96),400);}finally{pin.Free();}
        using(bitmap)
        using(var target=new RenderTargetBitmap(new PixelSize(width,height)))
        {
            Config.SetCurrent(new(){ImageEffect=new(){IsEnabled=true,Layers=new(){new(){Effect=effect}}}});
            if(outer is not null)Config.Current.ImageEffect.Layers.Insert(0,new(){Effect=outer});
            ImageEffectRenderer.EnsureExportSupported();int active=0;var result=new ImageEffectRenderResult();
            using(var context=target.CreateDrawingContext())Assert.True(ImageEffectRenderer.Draw(context,bitmap,new(0,0,100,100),new(0,0,width,height),()=>{active++;return new Release(()=>active--);},true,result,pixels,BitmapInterpolationMode.None));
            result.ThrowIfFailed();references=active;Assert.Equal(0,active);
            using var stream=new MemoryStream();target.Save(stream,PngBitmapEncoderOptions.Default);return new MagickImage(stream.ToArray());
        }
    }
    private static IMagickColor<byte> Pixel(MagickImage image,int x,int y)=>image.GetPixels().GetPixel(x,y).ToColor()!;
    [AvaloniaTheory][InlineData(EffectType.Sharpen)][InlineData(EffectType.Embossed)][InlineData(EffectType.Pixelate)][InlineData(EffectType.Magnify)][InlineData(EffectType.Ripple)][InlineData(EffectType.Swirl)]
    public void EveryMigratedSamplingEffectCompilesRendersAndReturnsNativeReferences(EffectType type)
    {
        var config=new ImageEffectConfig();config.Layers[0].ChangeType(type,config,new());
        using var image=Draw(config.Layers[0].Effect!,out var references);Assert.Equal(0,references);Assert.Equal((byte)255,Pixel(image,50,50).A);
    }
    [AvaloniaFact]public void SharpenUsesOppositeDiagonalAndKeepsCenterAlpha()
    {
        using var image=Draw(new SharpenEffectUnit{Height=100,Amount=1},out _,transparent:true);
        var p=Pixel(image,50,50);Assert.InRange(p.R,73,81);Assert.InRange(p.G,73,81);Assert.Equal((byte)128,p.A);
    }
    [AvaloniaFact]public void EmbossedPreservesRedPlusGreenPlusOneThirdBlueWeight()
    {
        using var image=Draw(new EmbossedEffectUnit{Height=100,Amount=1,Color=ThemeRgba.Parse("Black")},out _);
        var p=Pixel(image,50,50);Assert.InRange(p.R,99,107);Assert.InRange(p.G,99,107);Assert.InRange(p.B,99,107);
    }
    [AvaloniaFact]public void PixelateUsesDevicePixelPitchAndOffsetsOddRows()
    {
        using var image=Draw(new PixelateEffectUnit{Pixelation=.75},out _);
        var even=Pixel(image,12,8);var odd=Pixel(image,12,12);Assert.InRange(even.R,33,36);Assert.InRange(odd.R,33,36);
        var shifted=Pixel(image,14,12);Assert.InRange(shifted.R,43,46);Assert.InRange(shifted.G,33,36);
        // block中心恰好落在原像素边界，nearest允许浮点实现选到相邻一个texel。
        using var doubleWidth=Draw(new PixelateEffectUnit{Pixelation=.75},out _,width:200);Assert.InRange(Pixel(doubleWidth,28,24).R,36,39);
    }
    [AvaloniaFact]public void MagnifyUsesInnerScaleTransitionAndOriginalOuterPixels()
    {
        using var image=Draw(new MagnifyEffectUnit{Amount=.5},out _);
        Assert.InRange(Pixel(image,60,50).R,140,144);Assert.InRange(Pixel(image,95,50).R,242,247);
        using var equal=Draw(new MagnifyEffectUnit{InnerRadius=.2,OuterRadius=.2},out _);Assert.Equal((byte)255,Pixel(equal,50,50).A);
    }
    [AvaloniaFact]public void RippleRetainsRadiusFalloffAndSeparateRgbShading()
    {
        using var image=Draw(new RippleEffectUnit{Frequency=0,Magnitude=0,Phase=Math.PI},out _,transparent:true);
        var p=Pixel(image,75,25);Assert.InRange(p.R,150,157);Assert.InRange(p.G,48,54);Assert.Equal((byte)128,p.A);
    }
    [AvaloniaFact]public void SwirlUsesDistanceTimesTwistAndNormalizedCenterInRectangularTarget()
    {
        using var image=Draw(new SwirlEffectUnit{TwistAmount=10},out _,width:200);
        var p=Pixel(image,150,50);Assert.InRange(p.R,72,80);Assert.InRange(p.G,161,168);
    }
    [AvaloniaFact]public void SpatialAndColorLayersKeepOriginalInnerThenOuterSequence()
    {
        using var image=Draw(new MagnifyEffectUnit{Amount=.5},out _,outer:new MonochromeEffectUnit{Color=ThemeRgba.Parse("Blue")});
        var p=Pixel(image,60,50);Assert.Equal((byte)0,p.R);Assert.Equal((byte)0,p.G);Assert.InRange(p.B,116,121);
    }
    [AvaloniaFact]public void InvalidSpatialCoordinatesRejectViewExportBeforeDrawing()
    {
        Config.SetCurrent(new(){ImageEffect=new(){IsEnabled=true,Layers=new(){new(){Effect=new SwirlEffectUnit{Center=new(double.NaN,.5)}}}}});
        Assert.Throws<InvalidDataException>(ImageEffectRenderer.EnsureExportSupported);
    }
}
