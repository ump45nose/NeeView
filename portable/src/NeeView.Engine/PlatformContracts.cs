namespace NeeView;

/// <summary>替代实际使用的 WPF 尺寸值，不包含控件或属性系统。</summary>
[System.Text.Json.Serialization.JsonConverter(typeof(JsonSizeConverter))]
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
    /// <summary>监视单一已加载目录的直接目录变更；空表示此来源不支持，所有者隐藏/关闭释放。</summary>
    IDisposable? WatchDirectory(string path, Action changed) => null;
    /// <summary>搜索结果监视，含文件及目录元数据；仅活动书架拥有一个监视。</summary>
    IDisposable? WatchBookSearch(string path, bool recursive, Action changed) => null;
    /// <summary>普通书架搜索快照，递归不进入归档或符号链接目录；来源可替换真实后台枚举。</summary>
    async Task<IReadOnlyList<FolderItem>> ListSearchBooksAsync(string path, bool recursive, CancellationToken token)
    {
        var result = new List<FolderItem>(); var pending = new Stack<string>(); var seen = new HashSet<string>(StringComparer.Ordinal); pending.Push(path);
        while (pending.TryPop(out var current))
        {
            token.ThrowIfCancellationRequested(); if (!seen.Add(current)) continue;
            var entries = await ListBooksAsync(current, token); result.AddRange(entries);
            if (recursive) foreach (var item in entries.Where(item => item.IsDirectory && !item.IsSymbolicLink)) pending.Push(item.Path);
        }
        return result;
    }
    /// <summary>打开目录、ZIP、RAR 或 7z。调用方负责释放返回来源。</summary>
    Task<Archive> OpenAsync(string path, CancellationToken token);
    /// <summary>明确的阅读打开允许口令输入；封面、历史存在检查和后台枚举仍走无交互入口。</summary>
    /// <param name="requestKey">本次窗口的可等待输入，切书/关闭时必须取消。</param>
    Task<Archive> OpenAsync(string path, CancellationToken token, Func<ArchiveKeyRequest, CancellationToken, Task<string?>> requestKey) => OpenAsync(path, token);
    /// <summary>原CreateArchiveAsync(entry)；保留父归档/重复条目ID，返回来源借用父来源且由调用方释放。</summary>
    /// <param name="entry">仍由父书拥有的真实来源条目。</param><param name="token">读取取消。</param>
    /// <returns>独立子来源；默认实现保持既有后端路径入口。</returns>
    Task<Archive> OpenAsync(ArchiveEntry entry, CancellationToken token) => OpenAsync(entry.TargetArchiveEntry.SystemPath, token);
    /// <summary>可靠检查真实或归档内部定位；仅确定缺失返回false，权限/断线/不支持传播。</summary>
    Task<bool> ExistsAsync(string path, CancellationToken token) => throw new NotSupportedException("来源存在检查尚未实现。");
    /// <summary>列出直接子目录，用于 P1 基础导航。</summary>
    Task<IReadOnlyList<FolderItem>> ListFoldersAsync(string path, CancellationToken token);
    /// <summary>列出普通书架的目录及已支持归档；只返回元数据，不打开每本书。</summary>
    Task<IReadOnlyList<FolderItem>> ListBooksAsync(string path, CancellationToken token);
    /// <summary>查询单一真实来源的时间/大小；不存在返回空，权限/超时传播，归档内部不解压。</summary>
    /// <param name="path">原书签真实来源路径。</param><param name="token">取消后台来源探测。</param>
    /// <returns>文件或目录元数据；未实现此能力的来源明确失败。</returns>
    Task<FolderItem?> GetFileMetadataAsync(string path, CancellationToken token) => throw new NotSupportedException("来源元数据查询尚未实现。");
    /// <summary>仅实体文件操作使用的实际路径检查；解析系统链接及真实大小写，不改变内容定位。</summary>
    /// <param name="path">目标或受保护目录；未创建目录解析到最近存在的祖先并保留末段。</param>
    /// <param name="token">有界后台检查的取消。</param><returns>文件系统确认的绝对路径；不能可靠解析时失败。</returns>
    Task<string> GetPhysicalPathAsync(string path, CancellationToken token) => throw new NotSupportedException("实体路径检查尚未实现。");
}

/// <summary>目录导航只读条目，使用真实文件系统路径。</summary>
public sealed record FolderItem(string Name, string Path, bool IsDirectory = true, long Length = -1, DateTime LastWriteTime = default)
{
    /// <summary>书签位置的原节点；普通文件系统条目为空，不复制书签树或建立新身份。</summary>
    public BookmarkNode? Bookmark { get; init; }
    public QuickAccessTreeNode? QuickAccess { get; init; }
    public bool IsSymbolicLink { get; init; }
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
public sealed record ImageInfo(Size Size, string Format)
{
    /// <summary>后端探测的 DPI 显示尺寸；无可靠元数据的格式保持像素尺寸。</summary>
    public Size AspectSize { get; init; } = Size;
}
/// <summary>解码目标尺寸，以设备像素计。</summary>
public sealed record DecodeRequest(int TargetWidth, int TargetHeight, bool IsThumbnail = false);
/// <summary>单个像素缓冲的所有者；显示端不能保留已释放缓冲。</summary>
public sealed class DecodedImageLease(Size size, byte[] pixels, ThemeRgba? sourceColor = null, Size? sourceSize = null) : IDisposable
{
    public Size Size { get; } = size;
    /// <summary>原GetOneColor首像素（忽略Alpha），在解码缩小前取得；其他后端默认使用输出首像素。</summary>
    public ThemeRgba Color => sourceColor ?? (Pixels.Length >= 4 ? new(255, Pixels[2], Pixels[1], Pixels[0]) : ThemeRgba.Parse("Black"));
    /// <summary>方向校正后的原始大小，用于设备像素平铺，不因安全降采样改变周期。</summary>
    public Size SourceSize { get; } = sourceSize ?? size;
    public byte[] Pixels { get; private set; } = pixels;
    public int Stride => checked((int)Size.Width * 4);
    public long ByteCount => Pixels.LongLength;
    /// <summary>释放托管缓冲引用；调用方应同时释放对应的显示资源。</summary>
    public void Dispose() => Pixels = [];
}

/// <summary>macOS 文件能力，系统失败必须传播给调用方。</summary>
public interface IPlatformService
{
    /// <summary>请求平台以参数数组启动外部应用；默认平台明确不支持。</summary>
    Task OpenExternalApplicationAsync(ExternalAppLaunchRequest request, CancellationToken token = default) => throw new NotSupportedException("当前平台尚未提供外部应用启动能力。");
    /// <summary>系统打开明确网页或本机许可文件；失败返回异常，不通过shell启动。</summary>
    Task OpenUriAsync(Uri uri, CancellationToken token = default) => throw new NotSupportedException("当前平台尚未提供链接打开能力。");
    /// <summary>按需取得系统文件图标PNG，未支持返回空；不返回AppKit/Avalonia对象。</summary>
    Task<byte[]?> ReadFileIconAsync(string path, CancellationToken token = default) => Task.FromResult<byte[]?>(null);
    /// <summary>在 Finder 中定位真实文件。</summary>
    Task RevealAsync(string path, CancellationToken token = default);
    /// <summary>打开目录内容；与在父目录中定位对象分开，失败必须传播。</summary>
    Task OpenFolderAsync(string path, CancellationToken token = default) => throw new NotSupportedException("当前平台尚未提供打开目录能力。");
    /// <summary>移入系统废纸篓，不以永久删除回退。</summary>
    Task TrashAsync(string path, CancellationToken token = default);
}
