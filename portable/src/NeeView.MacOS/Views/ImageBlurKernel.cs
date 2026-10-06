// Blur weight calculation adapted from dotnet/wpf BlurEffect.cpp.
// Copyright .NET Foundation. Licensed under MIT (see portable/licenses/upstream/dotnet-wpf/LICENSE.TXT).
using System.Collections.Concurrent;
using System.Globalization;
using SkiaSharp;
namespace NeeView.MacOS.Views;

/// <summary>按.NET WPF官方BlurEffect.cpp的离散Gaussian及修正归一化迁入；不以Skia近似blur替换。</summary>
internal static class ImageBlurKernel
{
    private static readonly ConcurrentDictionary<(int Radius, bool Horizontal), Lazy<SKRuntimeEffect>> Kernels = new();
    /// <summary>原半径先非负并截整数，再乘较小设备缩放再截整数；设备半径最大100。</summary>
    internal static int DeviceRadius(double radius, double scale)
    {
        if (!double.IsFinite(radius) || Math.Abs(radius) > float.MaxValue || !double.IsFinite(scale) || scale < 0) throw new InvalidDataException("模糊半径/缩放包含非有限值或超出后端范围。");
        return (int)Math.Min(100, Math.Floor(Math.Floor(Math.Max(0, radius)) * scale));
    }
    /// <summary>原权重先转float再累加double，每项加相同修正，不使用除总和归一化。</summary>
    internal static float[] Weights(int radius)
    {
        if (radius is < 0 or > 100) throw new ArgumentOutOfRangeException(nameof(radius));
        if (radius == 0) return [1];
        var half = new float[radius + 1]; double sum = 0, sigma = radius / 3.0;
        for (int i = 0; i <= radius; i++) { half[i] = (float)(Math.Exp(-i * (double)i / (2 * sigma * sigma)) / (sigma * Math.Sqrt(2 * Math.PI))); sum += half[i] * (i == 0 ? 1.0 : 2.0); }
        float correction = (float)((1 - sum) / (2 * radius + 1));
        var values = new float[2 * radius + 1];
        for (int i = 0; i < values.Length; i++) values[i] = half[Math.Abs(i - radius)] + correction;
        return values;
    }
    /// <summary>两次独立的一维采样，透明扩边；半径缓存最多101种，不增加产品算法框架。</summary>
    internal static SKShader CreateShader(SKShader input, int radius, bool horizontal)
    {
        var effect = Kernels.GetOrAdd((radius, horizontal), key => new Lazy<SKRuntimeEffect>(() =>
        {
            var weights = Weights(key.Radius);
            string F(float number) => number.ToString("R", CultureInfo.InvariantCulture);
            string body = string.Join("", weights.Select((weight, i) => $"c+=float4(inputImage.eval(p+float2({(key.Horizontal ? i - 2 * key.Radius : 0)},{(key.Horizontal ? 0 : i - 2 * key.Radius)})))*{F(weight)};"));
            return SKRuntimeEffect.CreateShader("uniform shader inputImage;half4 main(float2 p){float4 c=float4(0);" + body + "return half4(c);}", out var error)
                ?? throw new InvalidOperationException("模糊核编译失败：" + error);
        })).Value;
        using var children = new SKRuntimeEffectChildren(effect); children["inputImage"] = input;
        using var uniforms = new SKRuntimeEffectUniforms(effect);
        return effect.ToShader(uniforms, children);
    }
}
