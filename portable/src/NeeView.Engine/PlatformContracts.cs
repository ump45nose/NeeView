namespace NeeView;

/// <summary>替代实际使用的 WPF 尺寸值，不包含控件或属性系统。</summary>
public readonly record struct Size(double Width, double Height);
/// <summary>布局比例或偏移值。</summary>
public record struct Vector(double X, double Y)
{
    public readonly double LengthSquared => X * X + Y * Y;
    /// <summary>按原 Vector.IsZero 判断零位移。</summary>
    public readonly bool IsZero() => X == 0 && Y == 0;
}
/// <summary>矩形值；调用方明确其为图片归一化裁剪区域或显示坐标，不携带控件引用。</summary>
public readonly record struct Rect(double X, double Y, double Width, double Height)
{
    public double Left => X;
    public double Top => Y;
    public double Right => X + Width;
    public double Bottom => Y + Height;
}

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
    /// <summary>列出普通书架的目录及已支持归档；只返回元数据，不打开每本书。</summary>
    Task<IReadOnlyList<FolderItem>> ListBooksAsync(string path, CancellationToken token);
    /// <summary>查询单一真实来源的时间/大小；不存在返回空，权限/超时传播，归档内部不解压。</summary>
    /// <param name="path">原书签真实来源路径。</param><param name="token">取消后台来源探测。</param>
    /// <returns>文件或目录元数据；未实现此能力的来源明确失败。</returns>
    Task<FolderItem?> GetFileMetadataAsync(string path, CancellationToken token) => throw new NotSupportedException("来源元数据查询尚未实现。");
}

/// <summary>目录导航只读条目，使用真实文件系统路径。</summary>
public sealed record FolderItem(string Name, string Path, bool IsDirectory = true, long Length = -1, DateTime LastWriteTime = default)
{
    /// <summary>书签位置的原节点；普通文件系统条目为空，不复制书签树或建立新身份。</summary>
    public BookmarkNode? Bookmark { get; init; }
    public string DisplayName => (IsDirectory ? "▸ " : "") + Name;
}

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
