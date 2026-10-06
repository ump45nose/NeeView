using Avalonia.Media.Imaging;
namespace NeeView.MacOS.Views;

/// <summary>在后台编码已保留的图像源；不应用查看器变换，也不读取来源或配置。</summary>
internal static class ImageCopyEncoder
{
    /// <summary>限制每次编码的实际输出增长，超限或取消不返回半份图像。</summary>
    /// <param name="bitmap">由调用方保留至任务结束的不可变图像源。</param><param name="token">编码前后取消；原生编码中途不能即时中断。</param>
    /// <returns>独立PNG字节快照。</returns>
    internal static Task<byte[]> EncodeAsync(Bitmap bitmap, CancellationToken token) => Task.Run(() =>
    {
        token.ThrowIfCancellationRequested(); using var stream = new LimitedPngStream();
        bitmap.Save(stream, PngBitmapEncoderOptions.Default); token.ThrowIfCancellationRequested();
        var png = stream.ToArray(); ImageClipboardCodec.ValidatePng(png); return png;
    }, token);

    /// <summary>MemoryStream的实际写入上限；编码器分块写入也不能先增长到预算之外。</summary>
    internal sealed class LimitedPngStream(int maximumBytes = ImageClipboardCodec.MaximumBytes) : MemoryStream
    {
        private void Check(long end) { if (end > maximumBytes) throw new NotSupportedException("复制图像超过64MiB编码预算。"); }
        public override void Write(byte[] buffer, int offset, int count) { Check(checked(Position + count)); base.Write(buffer, offset, count); }
        public override void Write(ReadOnlySpan<byte> buffer) { Check(checked(Position + buffer.Length)); base.Write(buffer); }
        public override void WriteByte(byte value) { Check(checked(Position + 1)); base.WriteByte(value); }
        public override void SetLength(long value) { Check(value); base.SetLength(value); }
    }
}
