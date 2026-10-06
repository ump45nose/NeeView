namespace NeeView;

/// <summary>原 ClipboardUtility.CopyImage 的系统替换点；传递独立PNG快照，不包含界面/像素租约。</summary>
public interface IImageClipboard
{
    /// <summary>写入已编码图像；调用方在完成前保持字节不变。排队/提交前可取消，提交后返回真实结果。</summary>
    /// <param name="png">查看器图像源编码的PNG，不是文件地址或窗口截图。</param>
    /// <param name="token">原生提交前的取消令牌。</param><returns>系统接受完整图像的任务。</returns>
    Task WritePngAsync(byte[] png, CancellationToken token);
}

/// <summary>图像剪贴板有限协议；生成端和平台端共用限额，不探测/修改内容来源。</summary>
public static class ImageClipboardCodec
{
    public const int MaximumBytes = 64 * 1024 * 1024;
    public const string PngType = "public.png";
    /// <summary>验证有限PNG快照的签名；实际图像编码由表现端负责。</summary>
    /// <param name="png">拥有独立生命周期的编码字节。</param>
    public static void ValidatePng(ReadOnlySpan<byte> png)
    {
        if (png.Length > MaximumBytes) throw new NotSupportedException("复制图像超过64MiB编码预算。");
        if (png.Length < 8 || !png[..8].SequenceEqual(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }))
            throw new ArgumentException("剪贴板图像不是PNG快照。", nameof(png));
    }
}
