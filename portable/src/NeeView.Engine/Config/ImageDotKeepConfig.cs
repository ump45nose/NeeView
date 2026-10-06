// Copyright (c) NeeLaboratory. 原 ImageDotKeepConfig，MIT 许可；WPF Size 换成既有值类型。
using CommunityToolkit.Mvvm.ComponentModel;
namespace NeeView;

/// <summary>保留原双轴、Threshold 和一像素容差；默认不启用。</summary>
public sealed class ImageDotKeepConfig : ObservableObject
{
    private bool _enabled;
    private double _threshold = 1;
    public bool IsEnabled { get => _enabled; set => SetProperty(ref _enabled, value); }
    public double Threshold { get => _threshold; set => SetProperty(ref _threshold, double.IsFinite(value) ? Math.Round(value, 5) : 1); }
    /// <param name="viewSize">实际显示设备像素尺寸。</param><param name="sourceSize">解码图片或裁剪源的像素尺寸。</param>
    /// <returns>是否沿原规则使用 nearest。</returns>
    public bool IsImageDotKeep(Size viewSize, Size sourceSize) => IsEnabled &&
        viewSize.Width >= sourceSize.Width * Threshold - 1 && viewSize.Height >= sourceSize.Height * Threshold - 1;
}
