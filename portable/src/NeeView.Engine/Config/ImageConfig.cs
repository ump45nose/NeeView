// Copyright (c) NeeView. 原 ImageConfig/ImageStandardConfig/MediaArchiveConfig 子集，MIT 许可。
using CommunityToolkit.Mvvm.ComponentModel;
namespace NeeView;

/// <summary>原动图配置关系；未迁入的 SVG/图像字段由唯一 JSON 保留。</summary>
public sealed class ImageConfig : ObservableObject
{
    private bool _isMediaRepeat = true;
    public ImageStandardConfig Standard { get; set; } = new();
    /// <summary>原图像动画循环开关，与视频 Archive.Media.IsRepeat 分开。</summary>
    public bool IsMediaRepeat { get => _isMediaRepeat; set => SetProperty(ref _isMediaRepeat, value); }
}
/// <summary>原三种格式开关及默认值，不包含 WIC 或窗口属性。</summary>
public sealed class ImageStandardConfig
{
    public bool IsAnimatedGifEnabled { get; set; } = true;
    public bool IsAnimatedPngEnabled { get; set; } = true;
    public bool IsAnimatedWebpEnabled { get; set; } = true;
    /// <summary>按原 PictureProfile 开关及扩展名决定是否尝试动画；静态判定由来源后端完成。</summary>
    public bool IsAnimationEnabled(string name) => System.IO.Path.GetExtension(name).ToLowerInvariant() switch
    { ".gif" => IsAnimatedGifEnabled, ".png" or ".apng" => IsAnimatedPngEnabled, ".webp" => IsAnimatedWebpEnabled, _ => false };
}
/// <summary>原媒体命令时间步长子集；视频字段保留到后续实际后端迁移。</summary>
public sealed class MediaArchiveConfig
{
    private double _pageSeconds = 10;
    public double PageSeconds { get => _pageSeconds; set => _pageSeconds = double.IsFinite(value) ? Math.Round(value, 5) : 10; }
}
