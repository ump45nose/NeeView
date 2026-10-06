using System.Runtime.InteropServices;
using Avalonia.Media.Imaging;
using NeeView.Effects;
using SkiaSharp;
namespace NeeView.MacOS.Views;

/// <summary>原归一采样效果的Skia适配；仅借用已有解码像素，不解码、不修改源图。</summary>
internal static class ImageSpatialEffectRenderer
{
    internal static bool IsSpatial(EffectUnit effect) => effect is EmbossedEffectUnit or PixelateEffectUnit or SharpenEffectUnit
        or MagnifyEffectUnit or RippleEffectUnit or SwirlEffectUnit;
    private static Lazy<SKRuntimeEffect> Compile(string body) => new(() => SKRuntimeEffect.CreateShader("uniform shader inputImage;" + body, out var error)
        ?? throw new InvalidOperationException("采样shader编译失败：" + error));
    // 原ps_2_0的atan2多项式与象限判断。中心零向量显式取0，避免0*infinity传播。
    private const string Angle = """
        float angle(float2 d) {
          float2 a=abs(d); float hi=max(a.x,a.y); if(hi==0)return 0;
          float t=min(a.x,a.y)/hi; float s=t*t;
          float r=t*((((0.02083509974181652*s-0.08513300120830536)*s+0.18014100193977356)*s-0.3302994966506958)*s+0.9998660087585449);
          if(a.x<a.y)r=1.5707963705062866-r;
          if(d.x<0)r-=3.1415927410125732;
          if(min(d.x,d.y)<0 && max(d.x,d.y)>=0)r=-r;
          return r;
        }
        float phaseAngle(float p){return fract(p*0.15915493667125702+0.5)*6.2831854820251465-3.1415927410125732;}
        """;
    private static readonly Lazy<SKRuntimeEffect> Sharpen = Compile("""
        uniform float amount; uniform float height;
        half4 main(float2 uv){half4 c=inputImage.eval(uv);half4 low=inputImage.eval(uv-float2(height));half4 high=inputImage.eval(uv+float2(height));return half4(clamp(c.rgb+amount*(low.rgb-high.rgb),0,1),c.a);}
        """);
    private static readonly Lazy<SKRuntimeEffect> Embossed = Compile("""
        uniform float amount; uniform float height; uniform float3 color;
        half4 main(float2 uv){half4 c=inputImage.eval(uv);float3 low=inputImage.eval(uv-float2(height)).rgb*amount;float3 high=inputImage.eval(uv+float2(height)).rgb*amount;
          float a=low.r+low.g+low.b*0.3333333432674408;float b=high.r+high.g+high.b*0.3333333432674408;return half4(clamp((color+b-a)*c.a,0,1),c.a);}
        """);
    private static readonly Lazy<SKRuntimeEffect> Pixelate = Compile("""
        uniform float pixelation; uniform float2 pixelStep;
        half4 main(float2 uv){float p=pixelation>=1?0.9990000128746033:pixelation;float size=min(pixelStep.x,pixelStep.y)/(1-p);
          float row=floor(uv.y/size);float2 point=uv; if(fract(row*0.5)>=0.5)point.x+=size*0.5;
          return inputImage.eval((floor(point/size)+0.5)*size);}
        """);
    private static readonly Lazy<SKRuntimeEffect> Magnify = Compile("""
        uniform float2 center; uniform float amount; uniform float innerRadius; uniform float outerRadius;
        half4 main(float2 uv){float inner=innerRadius<=0?0.0001:innerRadius;float outer=outerRadius<=0?0.0001:outerRadius;
          if(outer<inner)outer=inner+0.0001;float2 d=uv-center;float r=length(d);float2 innerPoint=center+d*(1-amount);
          if(r>=outer)return inputImage.eval(uv);if(r<inner)return inputImage.eval(innerPoint);
          float weight=(cos((r-inner)/(outer-inner)*3.1415927410125732)+1)*0.5;return inputImage.eval(mix(uv,innerPoint,weight));}
        """);
    private static readonly Lazy<SKRuntimeEffect> Ripple = Compile(Angle + """
        uniform float2 center; uniform float frequency; uniform float magnitude; uniform float phase;
        half4 main(float2 uv){float2 d=uv-center;float r=length(d);float a=phaseAngle(angle(d));float p=phaseAngle(frequency*r+phase);
          float edge=clamp(1-r,0,1);edge*=edge;float radius=r+sin(p)*magnitude*edge;
          half4 c=inputImage.eval(center+radius*float2(cos(a),sin(a)));float shade=clamp(cos(p)*edge,0,1)*0.2+0.8;
          return half4(c.rgb*shade,c.a);}
        """);
    private static readonly Lazy<SKRuntimeEffect> Swirl = Compile(Angle + """
        uniform float2 center;uniform float twist;
        half4 main(float2 uv){float2 d=uv-center;float r=length(d);float a=phaseAngle(angle(d)+twist*r);return inputImage.eval(center+r*float2(cos(a),sin(a)));}
        """);
    private static float Number(double value) => double.IsFinite(value) && Math.Abs(value)<=float.MaxValue ? (float)value : throw new InvalidDataException("采样效果参数包含非有限值。");
    private static float[] Point(EffectPoint p) => [Number(p.X),Number(p.Y)];
    /// <summary>仅编译/校验参数，不持有真实来源或分配整图临时缓冲。</summary>
    internal static void Validate(EffectUnit effect)
    { using var input=SKShader.CreateColor(SKColors.White);using var shader=Create(effect,input,100,100); }
    internal static SKShader Create(EffectUnit effect, SKShader input, double deviceWidth, double deviceHeight)
    {
        var runtime=effect switch {SharpenEffectUnit=>Sharpen.Value,EmbossedEffectUnit=>Embossed.Value,PixelateEffectUnit=>Pixelate.Value,MagnifyEffectUnit=>Magnify.Value,RippleEffectUnit=>Ripple.Value,SwirlEffectUnit=>Swirl.Value,_=>throw new NotSupportedException()};
        using var uniforms=new SKRuntimeEffectUniforms(runtime);using var children=new SKRuntimeEffectChildren(runtime);children["inputImage"]=input;
        switch(effect)
        {
            case SharpenEffectUnit e:uniforms["amount"]=Number(e.Amount);uniforms["height"]=Number(e.Height*.001);break;
            case EmbossedEffectUnit e:uniforms["amount"]=Number(e.Amount);uniforms["height"]=Number(e.Height*.001);uniforms["color"]=new[]{e.Color.R/255f,e.Color.G/255f,e.Color.B/255f};break;
            case PixelateEffectUnit e:uniforms["pixelation"]=Number(e.Pixelation);uniforms["pixelStep"]=new[]{Number(1/Math.Max(deviceWidth,1)),Number(1/Math.Max(deviceHeight,1))};break;
            case MagnifyEffectUnit e:uniforms["center"]=Point(e.Center);uniforms["amount"]=Number(e.Amount);uniforms["innerRadius"]=Number(e.InnerRadius);uniforms["outerRadius"]=Number(e.OuterRadius);break;
            case RippleEffectUnit e:uniforms["center"]=Point(e.Center);uniforms["frequency"]=Number(e.Frequency);uniforms["magnitude"]=Number(e.Magnitude);uniforms["phase"]=Number(e.Phase);break;
            case SwirlEffectUnit e:uniforms["center"]=Point(e.Center);uniforms["twist"]=Number(e.TwistAmount);break;
        }
        return runtime.ToShader(uniforms,children);
    }
    /// <summary>借用像素的原生生命周期额外保留显示引用；GPU晚到释放仍计原工厂预算。</summary>
    internal static SKImage BorrowImage(DecodedImageLease pixels, Func<IDisposable> retain)
    {
        var handle=GCHandle.Alloc(pixels.Pixels,GCHandleType.Pinned);IDisposable? resource=null;int released=0;
        void Release(){if(Interlocked.Exchange(ref released,1)!=0)return;handle.Free();resource?.Dispose();}
        try
        {
            resource=retain();
            var info=new SKImageInfo((int)pixels.Size.Width,(int)pixels.Size.Height,SKColorType.Bgra8888,SKAlphaType.Premul);
            using var pixmap=new SKPixmap(info,handle.AddrOfPinnedObject(),pixels.Stride);
            return SKImage.FromPixels(pixmap,(_,_)=>Release())??throw new InvalidOperationException("无法借用效果像素。");
        }
        catch {Release();throw;}
    }
    /// <summary>Avalonia 12.1.3 的实际采样策略：上采样Mitchell，下采样linear mipmap。</summary>
    internal static SKSamplingOptions Sampling(BitmapInterpolationMode mode, bool upscaling) => mode switch
    {
        BitmapInterpolationMode.None => new(SKFilterMode.Nearest, SKMipmapMode.None),
        BitmapInterpolationMode.Unspecified or BitmapInterpolationMode.LowQuality => new(SKFilterMode.Linear, SKMipmapMode.None),
        BitmapInterpolationMode.MediumQuality => new(SKFilterMode.Linear, SKMipmapMode.Linear),
        BitmapInterpolationMode.HighQuality => upscaling ? new(SKCubicResampler.Mitchell) : new(SKFilterMode.Linear, SKMipmapMode.Linear),
        _ => throw new ArgumentOutOfRangeException(nameof(mode)),
    };
}
