// Copyright (c) NeeLaboratory. Adapted from NeeView/MouseInput/LoupeContext.cs.
namespace NeeView;

/// <summary>放大镜运行状态，平台层只负责输入和渲染。</summary>
public sealed class LoupeContext(LoupeConfig loupeConfig)
{
    private double _scale = loupeConfig.DefaultScale;
    public LoupeConfig Config { get; } = loupeConfig;
    public bool IsEnabled { get; set; }
    public double Scale { get => _scale; set => _scale = value; }
    /// <summary>原固定步长递增，只应用 MaximumScale，不排序或裁剪导入范围。</summary>
    public void ZoomIn() => Scale = Math.Min(Scale + Config.ScaleStep, Config.MaximumScale);
    /// <summary>原固定步长递减，只应用 MinimumScale。</summary>
    public void ZoomOut() => Scale = Math.Max(Scale - Config.ScaleStep, Config.MinimumScale);
    /// <summary>重置倍率到原初始配置，不改变普通阅读变换。</summary>
    public void Reset() => Scale = Config.DefaultScale;
    /// <summary>原图基准开关；非法运行倍率只回退显示，不改写原 JSON。</summary>
    /// <param name="originalScale">原图像素到设备像素的当前倍率。</param><returns>独立的显示乘数。</returns>
    public double GetFixedScale(double originalScale)
    {
        var scale = double.IsFinite(originalScale) && originalScale > 0 ? originalScale : 1.0;
        var value = Config.IsBaseOnOriginal ? Scale / scale : Scale;
        return double.IsFinite(value) && value > 0 ? value : 1.0;
    }
}
