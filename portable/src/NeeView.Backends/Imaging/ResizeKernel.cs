// Kernel equations adapted from PhotoSauce.MagicScaler 0.15.0 Interpolators.cs.
// Copyright (c) Clinton Ingram and Contributors. MIT. See licenses/photosauce.LICENSE.txt.
using System.Numerics;
namespace NeeView.Backends;

/// <summary>原十一种插值核的受限替换；只持有滑动行，不分配完整浮点中间画布。</summary>
internal static class ResizeKernel
{
    private static readonly float[] Linear = Enumerable.Range(0, 256).Select(i =>
    {
        var c = i / 255f;
        return c <= .04045f ? c / 12.92f : MathF.Pow((c + .055f) / 1.055f, 2.4f);
    }).ToArray();
    private sealed record Weights(int First, float[] Values);
    private static double Support(ResizeInterpolation kernel) => kernel switch
    {
        ResizeInterpolation.NearestNeighbor => 0,
        ResizeInterpolation.Average => .5,
        ResizeInterpolation.Linear or ResizeInterpolation.Hermite => 1,
        ResizeInterpolation.Quadratic => 1.5,
        ResizeInterpolation.Lanczos or ResizeInterpolation.Spline36 => 3,
        ResizeInterpolation.Mitchell or ResizeInterpolation.CatmullRom or ResizeInterpolation.Cubic => 2,
        ResizeInterpolation.CubicSmoother => 2.3,
        _ => throw new NotSupportedException("未知的原缩放插值类型。")
    };
    /// <summary>固定0.15.0核方程；CubicSmoother另外将输入窗口扩大1.15倍。</summary>
    internal static double Value(ResizeInterpolation kernel, double distance)
    {
        var d = Math.Abs(distance);
        return kernel switch
        {
            ResizeInterpolation.NearestNeighbor => 1,
            ResizeInterpolation.Average => d <= .5 ? 1 : 0,
            ResizeInterpolation.Linear => d < 1 ? 1 - d : 0,
            ResizeInterpolation.Quadratic => d < .5 ? 1 - 2 * d * d : d < 1.5 ? d * d - 2.5 * d + 1.5 : 0,
            ResizeInterpolation.Hermite => Cubic(d, 0, 0),
            ResizeInterpolation.Mitchell => Cubic(d, 1d / 3, 1d / 3),
            ResizeInterpolation.CatmullRom => Cubic(d, 0, .5),
            ResizeInterpolation.Cubic => Cubic(d, 0, 1),
            ResizeInterpolation.CubicSmoother => Cubic(d / 1.15, 0, .625),
            ResizeInterpolation.Lanczos => d <= 5e-10 ? 1 : d < 3 ? 3 * Math.Sin(d * Math.PI) * Math.Sin(d * Math.PI / 3) / (d * d * Math.PI * Math.PI) : 0,
            ResizeInterpolation.Spline36 => Spline36(d),
            _ => throw new NotSupportedException("未知的原缩放插值类型。")
        };
    }
    private static double Cubic(double d, double b, double c) => d < 1
        ? (6 - 2*b + d*d*(-18 + 12*b + 6*c + d*(12 - 9*b - 6*c))) / 6
        : d < 2 ? (8*b + 24*c + d*(-12*b - 48*c + d*(6*b + 30*c + d*(-b - 6*c)))) / 6 : 0;
    private static double Spline36(double d)
    {
        if (d < 1) return ((13d/11*d - 453d/209)*d - 3d/209)*d + 1;
        if (d < 2) { d -= 1; return ((-6d/11*d + 270d/209)*d - 156d/209)*d; }
        if (d < 3) { d -= 2; return ((1d/11*d - 45d/209)*d + 26d/209)*d; }
        return 0;
    }
    private static Weights Make(int source, int destination, ResizeInterpolation kernel, int at)
    {
        double scale = (double)source / destination, center = (at + .5) * scale - .5;
        if (kernel == ResizeInterpolation.NearestNeighbor) return new(Math.Clamp((int)Math.Floor(center + .5), 0, source-1), [1]);
        double widen = Math.Max(1, scale), radius = Support(kernel) * widen;
        int first = Math.Max(0, (int)Math.Ceiling(center-radius)), last = Math.Min(source-1, (int)Math.Floor(center+radius));
        if (last < first) return new(Math.Clamp((int)Math.Floor(center+.5),0,source-1),[1]);
        var values = new float[last-first+1]; double sum = 0;
        for (int i=0;i<values.Length;i++) { var v=Value(kernel,(first+i-center)/widen); values[i]=(float)v; sum+=v; }
        if (Math.Abs(sum)<1e-12) return new(Math.Clamp((int)Math.Floor(center+.5),0,source-1),[1]);
        for (int i=0;i<values.Length;i++) values[i]=(float)(values[i]/sum);
        return new(first,values);
    }
    /// <summary>输入/输出为直alpha BGRA8；在线性sRGB预乘空间过滤，避免透明边缘隐藏RGB污染。</summary>
    /// <param name="source">方向/ICC已处理的源缓冲。</param><param name="sw">源宽。</param><param name="sh">源高。</param>
    /// <param name="dw">目标宽。</param><param name="dh">目标高。</param><param name="kernel">原枚举。</param>
    /// <param name="token">每行及权重建立时检查取消。</param><param name="budget">源、输出、权重与滑动行合计预算。</param>
    /// <returns>唯一目标缓冲；超预算明确失败。</returns>
    public static byte[] Resize(byte[] source, int sw, int sh, int dw, int dh, ResizeInterpolation kernel, CancellationToken token, long budget)
    {
        token.ThrowIfCancellationRequested(); var support = Support(kernel);
        if (sw<=0 || sh<=0 || dw<=0 || dh<=0 || source.LongLength!=checked((long)sw*sh*4)) throw new InvalidDataException("缩放像素尺寸不一致。");
        long outputBytes=checked((long)dw*dh*4), rowBytes=checked((long)dw*16);
        // Each axis has at most 2*support*source + two endpoints per destination, plus object overhead.
        long weightBytes=checked((long)Math.Ceiling((2*support*(sw+(long)sh)+2*(dw+(long)dh))*4)+(dw+(long)dh)*64);
        long remaining=budget-source.LongLength-outputBytes-weightBytes-rowBytes-checked((long)sw*16);
        if (outputBytes>int.MaxValue || remaining<rowBytes) throw new NotSupportedException("缩放滤镜超过托管工作预算。");
        var xweights=new Weights[dw]; var yweights=new Weights[dh];
        for (int x=0;x<dw;x++) { token.ThrowIfCancellationRequested(); xweights[x]=Make(sw,dw,kernel,x); }
        for (int y=0;y<dh;y++) { token.ThrowIfCancellationRequested(); yweights[y]=Make(sh,dh,kernel,y); }
        int maximumRows=(int)Math.Min(yweights.Max(w=>w.Values.Length),remaining/(rowBytes+128));
        if (maximumRows<1) throw new NotSupportedException("缩放滤镜超过滑动行预算。");
        var output=new byte[(int)outputBytes]; var accumulator=new Vector4[dw]; var linearRow=new Vector4[sw];
        var rows=new Dictionary<int,Vector4[]>(); var order=new Queue<int>();
        Vector4[] Row(int sy)
        {
            if (rows.TryGetValue(sy,out var existing)) return existing;
            token.ThrowIfCancellationRequested();
            Vector4[] row;
            if (rows.Count>=maximumRows) { var oldest=order.Dequeue(); row=rows[oldest]; rows.Remove(oldest); }
            else row=new Vector4[dw];
            // Convert each source pixel once for this row, rather than once per overlapping filter tap.
            for (int x=0;x<sw;x++)
            {
                int offset=(sy*sw+x)*4; float alpha=source[offset+3]/255f;
                linearRow[x]=new Vector4(Linear[source[offset]]*alpha,Linear[source[offset+1]]*alpha,Linear[source[offset+2]]*alpha,alpha);
            }
            for (int x=0;x<dw;x++)
            {
                var taps=xweights[x]; var sum=Vector4.Zero;
                for (int i=0;i<taps.Values.Length;i++)
                {
                    sum+=linearRow[taps.First+i]*taps.Values[i];
                }
                row[x]=sum;
            }
            rows.Add(sy,row); order.Enqueue(sy); return row;
        }
        for (int y=0;y<dh;y++)
        {
            token.ThrowIfCancellationRequested(); Array.Clear(accumulator); var taps=yweights[y];
            for (int i=0;i<taps.Values.Length;i++)
            {
                var row=Row(taps.First+i); float weight=taps.Values[i];
                for (int x=0;x<dw;x++) accumulator[x]+=row[x]*weight;
            }
            for (int x=0;x<dw;x++)
            {
                var pixel=accumulator[x]; float alpha=Math.Clamp(pixel.W,0,1); int offset=(y*dw+x)*4;
                output[offset]=Encode(alpha<1e-6 ? 0 : pixel.X/alpha);
                output[offset+1]=Encode(alpha<1e-6 ? 0 : pixel.Y/alpha);
                output[offset+2]=Encode(alpha<1e-6 ? 0 : pixel.Z/alpha);
                output[offset+3]=(byte)Math.Clamp((int)MathF.Round(alpha*255),0,255);
            }
        }
        return output;
    }
    private static byte Encode(float value)
    {
        value=Math.Clamp(value,0,1); float srgb=value<=.0031308f ? 12.92f*value : 1.055f*MathF.Pow(value,1/2.4f)-.055f;
        return (byte)Math.Clamp((int)MathF.Round(srgb*255),0,255);
    }
}
