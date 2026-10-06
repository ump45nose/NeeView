// Copyright (c) NeeLaboratory. Original Level/Hsv/ColorSelect/Colorize shader math, HLSL to SkSL.
using System.ComponentModel;
using System.Collections.Specialized;
using System.Runtime.CompilerServices;
using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Rendering.SceneGraph;
using Avalonia.Skia;
using NeeView.Effects;
using SkiaSharp;
namespace NeeView.MacOS.Views;

/// <summary>唯一图像绘制的效果边界；源 Bitmap 不改变，现有 Skia 的 CPU/GPU 路径共用算法。</summary>
internal sealed class ImageEffectRenderResult
{
    private string? _error;
    public string? Error => Volatile.Read(ref _error);
    public void Fail(string message) => Interlocked.CompareExchange(ref _error, message, null);
    public void ThrowIfFailed() { if (Error is { } message) throw new InvalidOperationException("图像效果未完成：" + message); }
}

internal static class ImageEffectRenderer
{
    private static readonly ConditionalWeakTable<ImageEffectConfig, SnapshotCache> Snapshots = new();
    public static bool IsSupported(EffectUnit? unit) => unit is null or LevelEffectUnit or HsvEffectUnit or ColorSelectEffectUnit or ColorizeEffectUnit
        or BloomEffectUnit or MonochromeEffectUnit or ColorToneEffectUnit;
    public static string? Unsupported(ImageEffectConfig config) => !config.IsEnabled ? null : string.Join("、", config.Layers
        .Where(x => x.IsEnabled && !IsSupported(x.Effect)).Select(x => x.Effect is UnknownEffectUnit u ? u.TypeName ?? "未知" : x.EffectType.ToString())) is { Length: > 0 } names ? names : null;
    public static void EnsureExportSupported()
    {
        if (Unsupported(Config.Current.ImageEffect) is { } names) throw new NotSupportedException("视图包含尚未迁入的效果：" + names + "。原图导出仍可用。");
        if (Config.Current.ImageEffect.IsEnabled && Snapshot(Config.Current.ImageEffect).Error is { } error)
            throw new InvalidDataException("图像效果参数无效：" + error);
    }
    private sealed record EffectSnapshot(EffectUnit[] Effects, string? Error);
    private static EffectSnapshot Snapshot(ImageEffectConfig config) => Snapshots.GetValue(config, static value => new(value)).Get();
    /// <summary>可见临时表面按设备像素限制，不分配超预算缓冲，也不能无提示跳过效果。</summary>
    internal static string? SurfaceError(double width, double height) => !double.IsFinite(width) || !double.IsFinite(height) || width < 0 || height < 0
        ? "Invalid surface size" : Math.Ceiling(width) * Math.Ceiling(height) * 4 > 128L * 1024 * 1024 ? "Surface budget exceeded (128 MiB)" : null;
    /// <summary>捕获纯参数和显示资源租约；scene-graph 延迟绘制/释放不会访问已经关闭的图片。</summary>
    public static bool Draw(DrawingContext context, Bitmap bitmap, Avalonia.Rect source, Avalonia.Rect target, Func<IDisposable>? retain, bool immediate = false, ImageEffectRenderResult? result = null)
    {
        var config = Config.Current.ImageEffect;
        if (!config.IsEnabled || retain is null || !config.Layers.Any(x => x.IsEnabled && x.Effect is not null)) return false;
        var snapshot = Snapshot(config);
        var operation = new DrawOperation(bitmap, source, target, snapshot, retain(), result);
        var submitted = false;
        try { context.Custom(operation); submitted = true; }
        // 提交失败时 scene-graph 没有接管所有权；离屏成功提交也由调用方立即释放。
        finally { if (!submitted || immediate) operation.Dispose(); }
        return true;
    }
    private sealed class SnapshotCache
    {
        private readonly ImageEffectConfig _source;
        private readonly List<Action> _detach = [];
        private EffectSnapshot? _snapshot;
        public SnapshotCache(ImageEffectConfig source) { _source = source; source.PropertyChanged += RootChanged; Wire(); }
        private void RootChanged(object? sender, PropertyChangedEventArgs args) { _snapshot = null; Wire(); }
        private void Changed(object? sender, PropertyChangedEventArgs args) { _snapshot = null; if (args.PropertyName is "Effect" or "Points") Wire(); }
        private void CollectionChanged(object? sender, NotifyCollectionChangedEventArgs args) { _snapshot = null; Wire(); }
        private void Wire()
        {
            foreach (var detach in _detach) detach(); _detach.Clear();
            var layers = _source.Layers; layers.CollectionChanged += CollectionChanged; _detach.Add(() => layers.CollectionChanged -= CollectionChanged);
            foreach (var layer in _source.Layers)
            {
                layer.PropertyChanged += Changed; _detach.Add(() => layer.PropertyChanged -= Changed);
                if (layer.Effect is { } effect) { effect.PropertyChanged += Changed; _detach.Add(() => effect.PropertyChanged -= Changed); }
                if (layer.Effect is ColorizeEffectUnit colorize)
                {
                    var points = colorize.Points; points.CollectionChanged += CollectionChanged; _detach.Add(() => points.CollectionChanged -= CollectionChanged);
                    foreach (var point in points) { point.PropertyChanged += Changed; _detach.Add(() => point.PropertyChanged -= Changed); }
                }
            }
        }
        public EffectSnapshot Get()
        {
            if (_snapshot is not null) return _snapshot;
            if (Unsupported(_source) is { } pending) return _snapshot = new([], "Effect backend pending: " + pending);
            try
            {
                var effects = _source.Layers.Reverse().Where(x => x.IsEnabled && x.Effect is not null).Select(x => x.Effect!.Clone()).ToArray();
                // 每份参数快照只预检一次，坏参数/编译失败在正式导出之前报告。
                foreach (var effect in effects.Where(IsSupported)) { using var filter = CreateFilter(effect); }
                return _snapshot = new(effects, null);
            }
            catch (Exception ex) { return _snapshot = new([], ex.Message); }
        }
    }
    private sealed class DrawOperation(Bitmap bitmap, Avalonia.Rect source, Avalonia.Rect target, EffectSnapshot snapshot, IDisposable resource, ImageEffectRenderResult? result) : ICustomDrawOperation
    {
        public Avalonia.Rect Bounds => target;
        public bool HitTest(Avalonia.Point point) => false;
        public bool Equals(ICustomDrawOperation? other) => ReferenceEquals(this, other);
        private int _disposed;
        public void Dispose() { if (Interlocked.Exchange(ref _disposed, 1) == 0) resource.Dispose(); }
        public void Render(ImmediateDrawingContext context)
        {
            try { RenderCore(context); }
            catch (Exception ex) { Failure(context, ex.Message); }
        }
        private void Failure(ImmediateDrawingContext context, string message)
        {
            result?.Fail(message);
            // 不能抛给会吞掉 Custom 异常的框架，更不能画无效果图冒充成功。
            using var clip = context.PushClip(target);
            context.FillRectangle(Brushes.DarkRed, target);
            if (context.TryGetFeature(typeof(ISkiaSharpApiLeaseFeature)) is ISkiaSharpApiLeaseFeature feature)
            {
                using var lease = feature.Lease();
                using var paint = new SKPaint { Color = SKColors.Orange, IsAntialias = true };
                using var font = new SKFont(SKTypeface.Default, 14);
                lease.SkCanvas.DrawText("Image effect unavailable", (float)target.X + 8, (float)target.Y + 24, font, paint);
                lease.SkCanvas.DrawText(message, (float)target.X + 8, (float)target.Y + 44, font, paint);
            }
        }
        private void RenderCore(ImmediateDrawingContext context)
        {
            if (snapshot.Error is { } invalid) { Failure(context, invalid); return; }
            var feature = context.TryGetFeature(typeof(ISkiaSharpApiLeaseFeature)) as ISkiaSharpApiLeaseFeature;
            if (feature is null) { Failure(context, "Skia backend required"); return; }
            SKRect bounds = new((float)target.X, (float)target.Y, (float)target.Right, (float)target.Bottom);
            string? error;
            using (var lease = feature.Lease())
            {
                var device = lease.SkCanvas.TotalMatrix.MapRect(bounds); var clip = lease.SkCanvas.DeviceClipBounds;
                double width = Math.Max(0, Math.Min(device.Right, clip.Right)-Math.Max(device.Left,clip.Left));
                double height = Math.Max(0, Math.Min(device.Bottom, clip.Bottom)-Math.Max(device.Top,clip.Top));
                error = SurfaceError(width, height);
            }
            if (error is not null) { Failure(context, error); return; }
            var filters = new List<SKColorFilter>(); SKColorFilter? combined = null;
            try
            {
                foreach (var effect in snapshot.Effects)
                {
                    if (!IsSupported(effect)) continue;
                    var filter = CreateFilter(effect); filters.Add(filter);
                    if (combined is null) combined = filter;
                    else { combined = SKColorFilter.CreateCompose(filter, combined); filters.Add(combined); }
                }
                if (combined is null) { context.DrawBitmap(bitmap, source, target); return; }
                using var paint = new SKPaint { ColorFilter = combined };
                int save;
                // Avalonia 12.1.3 ApiLease只恢复matrix与leased标记，不恢复canvas save stack。
                // PlatformDrawingContext.Custom同步Render且不Dispose；离屏入口负责immediate释放。
                // lease中禁止框架绘图，因此分别租用设置/恢复，中间沿唯一DrawBitmap。
                using (var lease = feature.Lease()) save = lease.SkCanvas.SaveLayer(bounds, paint);
                try { context.DrawBitmap(bitmap, source, target); }
                finally { using var lease = feature.Lease(); lease.SkCanvas.RestoreToCount(save); }
            }
            finally { foreach (var filter in filters) filter.Dispose(); }
        }
    }
    private static readonly string HsvMath = """
        float3 rgb2hsv(float3 c) {
          float4 K=float4(0,-1.0/3,2.0/3,-1);
          float4 p=mix(float4(c.bg,K.wz),float4(c.gb,K.xy),step(c.b,c.g));
          float4 q=mix(float4(p.xyw,c.r),float4(c.r,p.yzx),step(p.x,c.r));
          float d=q.x-min(q.w,q.y); return float3(abs(q.z+(q.w-q.y)/(6*d+1e-10)),d/(q.x+1e-10),q.x);
        }
        float3 hsv2rgb(float3 c) {
          float3 p=abs(fract(c.xxx+float3(1,2.0/3,1.0/3))*6-float3(3));
          return c.z*mix(float3(1),clamp(p-float3(1),0,1),c.y);
        }
        """;
    private static readonly Lazy<SKRuntimeEffect> Level = Compile("""
        uniform float black; uniform float white; uniform float center; uniform float minimum; uniform float maximum;
        float bias(float t) { if(center<=0)return 1; if(center>=1)return 0; return t<center?t*(1-center)/center:(t-center)*center/(1-center)+(1-center); }
        half4 main(half4 src) {
          if(src.a<=0)return half4(0); float3 c=float3(src.rgb)/src.a;
          c=white==black?step(float3(black),c):clamp((c-black)/(white-black),0,1);
          c=float3(bias(c.r),bias(c.g),bias(c.b)); return half4(clamp(mix(float3(minimum),float3(maximum),c),0,1)*src.a,src.a);
        }
        """);
    private static readonly Lazy<SKRuntimeEffect> Hsv = Compile(HsvMath + """
        uniform float hue; uniform float satulation; uniform float value;
        half4 main(half4 src) {
          if(src.a<=0)return half4(0); float3 hsv=rgb2hsv(float3(src.rgb)/src.a);
          hsv.x=fract(hsv.x+hue/360+1);
          hsv.y=clamp(satulation<0?mix(hsv.y,0,-satulation):mix(hsv.y,1,satulation*hsv.y),0,1);
          hsv.z=clamp(value<0?mix(hsv.z,0,-value):mix(hsv.z,1,value*hsv.z),0,1);
          return half4(hsv2rgb(hsv)*src.a,src.a);
        }
        """);
    private static readonly Lazy<SKRuntimeEffect> Select = Compile(HsvMath + """
        uniform float hue; uniform float range; uniform float curve;
        half4 main(half4 src) {
          if(src.a<=0)return half4(0); float3 hsv=rgb2hsv(float3(src.rgb)/src.a);
          float distance=abs(fract(fract(hue/360-hsv.x+1)+0.5)-0.5);
          float rad=curve==0?(distance<=range*0.5?0:3.14):clamp(3.14*(distance-range*0.5)/curve,0,3.14);
          hsv.y*=cos(rad)*0.5+0.5; return half4(hsv2rgb(hsv)*src.a,src.a);
        }
        """);
    private static readonly Lazy<SKRuntimeEffect> Colorize = Compile("uniform float luminanceWeight; uniform float4 lut[256];" + LutLookupSource(0, 256) + """
        half4 main(half4 src) {
          if(src.a<=0)return half4(0); float y=dot(float3(src.rgb)/src.a,float3(0.2126,0.7152,0.0722));
          float x=clamp(y*256-0.5,0,255); float4 target=lookup(x);
          float3 color=target.rgb; float a=src.a*target.a;
          float cb=dot(color,float3(-0.1146,-0.3854,0.5)); float cr=dot(color,float3(0.5,-0.4545,-0.0455));
          float3 preserved=clamp(float3(y+1.5748*cr,y-0.1873*cb-0.4681*cr,y+1.8556*cb),0,1);
          return half4(mix(color,preserved,luminanceWeight)*a,a);
        }
        """);
    // 固定原后端的颜色运算：0.30/0.59/0.11并非自有Colorize的Rec.709权重。
    // 保留原四分量Bloom和ColorTone末端alpha乘法，不能按效果名替换为常见近似算法。
    private static readonly Lazy<SKRuntimeEffect> Bloom = Compile("""
        uniform float2 intensity; uniform float2 saturation; uniform float threshold;
        half4 main(half4 src) {
          float4 b=threshold>=1?float4(0):clamp((float4(src)-threshold)/(1-threshold),0,1);
          float y=dot(b.rgb,float3(0.30,0.59,0.11));
          b=mix(float4(y),b,saturation.y)*intensity.y;
          float baseY=dot(float3(src.rgb),float3(0.30,0.59,0.11));
          float4 base=mix(float4(baseY),float4(src),saturation.x)*intensity.x;
          return half4(clamp(base*(1-clamp(b,0,1))+b,0,1));
        }
        """);
    private static readonly Lazy<SKRuntimeEffect> Monochrome = Compile("""
        uniform float4 filterColor;
        half4 main(half4 src) {
          float y=dot(float3(src.rgb),float3(0.30,0.59,0.11));
          return half4(float3(y)*filterColor.rgb,src.a*filterColor.a);
        }
        """);
    private static readonly Lazy<SKRuntimeEffect> ColorTone = Compile("""
        uniform float3 darkColor; uniform float3 lightColor; uniform float desaturation; uniform float toned;
        half4 main(half4 src) {
          float3 base=float3(src.rgb)*lightColor;
          float y=dot(base,float3(0.30,0.59,0.11));
          base=mix(base,float3(y),desaturation);
          float3 tone=mix(darkColor,lightColor,y);
          return half4(clamp(mix(base,tone,toned)*src.a,0,1),src.a);
        }
        """);
    // Skia 的 ES2 color-filter 路径要求常量数组索引；同一 256 点表用八层查找，CPU/GPU均可编译。
    private static string LutLookupSource(int start, int end)
    {
        string Branch(int low, int high)
        {
            if (high-low == 1) return low == 255 ? "return lut[255];" : $"return mix(lut[{low}],lut[{low+1}],x-{low});";
            int mid = (low+high)/2; return $"if(x<{mid}){{{Branch(low,mid)}}}else{{{Branch(mid,high)}}}";
        }
        return "float4 lookup(float x){" + Branch(start,end) + "}";
    }
    private static Lazy<SKRuntimeEffect> Compile(string source) => new(() => SKRuntimeEffect.CreateColorFilter(source, out var error) ?? throw new InvalidOperationException("图像 shader 编译失败：" + error));
    internal static SKColorFilter CreateFilter(EffectUnit effect)
    {
        var runtime = effect switch { LevelEffectUnit => Level.Value, HsvEffectUnit => Hsv.Value, ColorSelectEffectUnit => Select.Value, ColorizeEffectUnit => Colorize.Value,
            BloomEffectUnit => Bloom.Value, MonochromeEffectUnit => Monochrome.Value, ColorToneEffectUnit => ColorTone.Value, _ => throw new NotSupportedException() };
        using var uniforms = new SKRuntimeEffectUniforms(runtime);
        static float Number(double v) => double.IsFinite(v) && Math.Abs(v) <= float.MaxValue ? (float)v : throw new InvalidDataException("效果参数包含非有限值。");
        switch (effect)
        {
            case LevelEffectUnit level: uniforms["black"] = Number(level.Black); uniforms["white"] = Number(level.White); uniforms["center"] = Number(level.Center); uniforms["minimum"] = Number(level.Minimum); uniforms["maximum"] = Number(level.Maximum); break;
            case HsvEffectUnit hsv: uniforms["hue"] = Number(hsv.Hue); uniforms["satulation"] = Number(hsv.Saturation); uniforms["value"] = Number(hsv.Value); break;
            case ColorSelectEffectUnit select: uniforms["hue"] = Number(select.Hue); uniforms["range"] = Number(select.Range); uniforms["curve"] = Number(select.Curve); break;
            case ColorizeEffectUnit colorize: uniforms["luminanceWeight"] = Number(colorize.LuminanceWeight); uniforms["lut"] = CreateLut(colorize); break;
            case BloomEffectUnit bloom: uniforms["intensity"] = new[] { Number(bloom.BaseIntensity), Number(bloom.BloomIntensity) }; uniforms["saturation"] = new[] { Number(bloom.BaseSaturation), Number(bloom.BloomSaturation) }; uniforms["threshold"] = Number(bloom.Threshold); break;
            case MonochromeEffectUnit mono: uniforms["filterColor"] = new[] { mono.Color.R / 255f, mono.Color.G / 255f, mono.Color.B / 255f, mono.Color.A / 255f }; break;
            case ColorToneEffectUnit tone: uniforms["darkColor"] = new[] { tone.DarkColor.R / 255f, tone.DarkColor.G / 255f, tone.DarkColor.B / 255f }; uniforms["lightColor"] = new[] { tone.LightColor.R / 255f, tone.LightColor.G / 255f, tone.LightColor.B / 255f }; uniforms["desaturation"] = Number(tone.Desaturation); uniforms["toned"] = Number(tone.ToneAmount); break;
        }
        return runtime.ToColorFilter(uniforms);
    }
    /// <summary>原 ColorizeEffectTools 的 256 点二次插值；原8位截断和强度结点保留。</summary>
    private static float[] CreateLut(ColorizeEffectUnit effect)
    {
        var points = effect.Points; if (points.Count < 2) throw new InvalidDataException("色阶至少需要两个控制点。");
        var knots = new double[points.Count]; double sum = 0;
        for (int i = 0; i < points.Count - 1; i++) sum += Math.Max((points[i].Strength + points[i+1].Strength) * .5, .01);
        if (!double.IsFinite(sum)) throw new InvalidDataException("色阶强度无效。");
        for (int i = 0; i < points.Count - 1; i++) knots[i+1] = knots[i] + Math.Max((points[i].Strength + points[i+1].Strength) * .5, .01) / sum;
        var values = new float[1024];
        for (int x = 0; x < 256; x++)
        {
            double y = x / 255.0; int section = 0;
            while (section < points.Count - 2 && !(knots[section] <= y && y <= knots[section+1])) section++;
            double v = Math.Clamp((y-knots[section])/(knots[section+1]-knots[section]),0,1);
            var p = points[section]; var q = points[section+1]; double m = p.Strength+q.Strength > 0 ? q.Strength/(p.Strength+q.Strength) : .5;
            v = Math.Clamp((2-4*m)*v*v+(4*m-1)*v,0,1);
            byte Mix(byte a, byte b) => (byte)(a*(1-v)+b*v);
            values[x*4]=Mix(p.Color.R,q.Color.R)/255f; values[x*4+1]=Mix(p.Color.G,q.Color.G)/255f;
            values[x*4+2]=Mix(p.Color.B,q.Color.B)/255f; values[x*4+3]=Mix(p.Color.A,q.Color.A)/255f;
        }
        return values;
    }
}
