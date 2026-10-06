// Copyright (c) NeeView. 原 AnimatedImageType / 动画来源关系的跨平台适配，遵循仓库 MIT 许可。
namespace NeeView;

/// <summary>保留原 GIF、PNG、WebP 动图类型；None 为静态图片。</summary>
public enum AnimatedImageType { None, Gif, Png, Webp }

/// <summary>完整画布和逐帧时间；ResourceBytes 为后端持有像素的保守计费，不包含显示端缓冲。</summary>
public sealed record AnimatedImageInfo(Size Size, AnimatedImageType Type, IReadOnlyList<TimeSpan> FrameDurations, long ResourceBytes)
{
    public int FrameCount => FrameDurations.Count;
    public TimeSpan Duration => TimeSpan.FromTicks(FrameDurations.Sum(value => value.Ticks));
}

/// <summary>替换原 WPF 动图后端；静态图片返回空，来源流由调用方释放。</summary>
public interface IAnimatedImageDecoder
{
    /// <param name="stream">可定位的原归档条目流，不转为独立文件路径。</param>
    /// <param name="request">设备像素显示规格，缩略图仍使用原静态解码链。</param>
    /// <param name="workingBudget">本次原生准备峰值的保守字节上限。</param>
    /// <param name="token">切页/切书取消；晚到原生资源必须释放。</param>
    /// <returns>已正确合成且拥有原生资源的来源；只有真实多帧图片返回来源。</returns>
    Task<IAnimatedImageSource?> OpenAnimationAsync(Stream stream, DecodeRequest request, long workingBudget, CancellationToken token);
}

/// <summary>原 AnimatedPageSource 的后端资源替换点，不包含控件或具体图片库。</summary>
public interface IAnimatedImageSource : IDisposable
{
    AnimatedImageInfo Info { get; }
    /// <summary>取得完整合成帧的 BGRA8 预乘像素；每次结果由调用方释放。</summary>
    Task<DecodedImageLease> ReadFrameAsync(int index, CancellationToken token);
}
