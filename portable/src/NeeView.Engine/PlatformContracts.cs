namespace NeeView;

/// <summary>替代实际使用的 WPF 尺寸值，不包含控件或属性系统。</summary>
public readonly record struct Size(double Width, double Height);
/// <summary>布局比例或偏移值。</summary>
public readonly record struct Vector(double X, double Y);
/// <summary>归一化裁剪区域，坐标属于图片而非窗口。</summary>
public readonly record struct Rect(double X, double Y, double Width, double Height);

/// <summary>macOS 精确滚动及捏合输入；坐标属于窗口内容区域，单位为 DIP。</summary>
public sealed record PlatformGesture(bool IsMagnify, double X, double Y, double DeltaX, double DeltaY, double Magnification, nint SourceWindow = 0);

/// <summary>平台事件桥接，处理器只消费查看器内事件；其余原生事件继续传播。</summary>
public interface IPlatformInput : IDisposable
{
    /// <summary>安装窗口处理器，返回 true 表示已消费；释放时移除原生监听。</summary>
    void Attach(Func<PlatformGesture, bool> handler);
}

/// <summary>内容来源替换点；沿用 Archive/ArchiveEntry 模型。</summary>
public interface IArchiveFactory
{
    /// <summary>打开目录、ZIP、RAR 或 7z。调用方负责释放返回来源。</summary>
    Task<Archive> OpenAsync(string path, CancellationToken token);
    /// <summary>列出直接子目录，用于 P1 基础导航。</summary>
    Task<IReadOnlyList<FolderItem>> ListFoldersAsync(string path, CancellationToken token);
}

/// <summary>目录导航只读条目，使用真实文件系统路径。</summary>
public sealed record FolderItem(string Name, string Path);

/// <summary>原图像工厂的解码替换点，返回可释放 BGRA8 预乘像素。</summary>
public interface IImageDecoder
{
    /// <summary>探测方向校正后的原始尺寸；输入流由调用方释放。</summary>
    Task<ImageInfo> ProbeAsync(Stream stream, CancellationToken token);
    /// <summary>按目标尺寸解码，取消后的原生结果不得提交到界面。</summary>
    Task<DecodedImageLease> DecodeAsync(Stream stream, DecodeRequest request, CancellationToken token);
}

/// <summary>图片尺寸及格式元数据。</summary>
public sealed record ImageInfo(Size Size, string Format);
/// <summary>解码目标尺寸，以设备像素计。</summary>
public sealed record DecodeRequest(int TargetWidth, int TargetHeight, bool IsThumbnail = false);
/// <summary>单个像素缓冲的所有者；显示端不能保留已释放缓冲。</summary>
public sealed class DecodedImageLease(Size size, byte[] pixels) : IDisposable
{
    public Size Size { get; } = size;
    public byte[] Pixels { get; private set; } = pixels;
    public int Stride => checked((int)Size.Width * 4);
    public long ByteCount => Pixels.LongLength;
    /// <summary>释放托管缓冲引用；调用方应同时释放对应的显示资源。</summary>
    public void Dispose() => Pixels = [];
}

/// <summary>macOS 文件能力，系统失败必须传播给调用方。</summary>
public interface IPlatformService
{
    /// <summary>在 Finder 中定位真实文件。</summary>
    Task RevealAsync(string path, CancellationToken token = default);
    /// <summary>移入系统废纸篓，不以永久删除回退。</summary>
    Task TrashAsync(string path, CancellationToken token = default);
}
