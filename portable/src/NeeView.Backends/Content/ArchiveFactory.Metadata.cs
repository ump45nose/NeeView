using NeeView.Media.Imaging.Metadata;
namespace NeeView.Backends;

public sealed partial class ArchiveFactory
{
    private static readonly SemaphoreSlim MetadataSlots = new(2);
    private const int MetadataInputLimit = 32 * 1024 * 1024;
    /// <summary>读取原条目的完整EXIF/XMP映射；两槽、32MiB读取字节限制，探测不分配显示像素。</summary>
    /// <param name="entry">同一书籍拥有的真实条目。</param><param name="token">请求取消，晚到工作仍持槽并负责关闭流。</param>
    /// <returns>纯图片信息；格式不支持元数据时保留尺寸并明确警告。</returns>
    public async Task<PagePictureInfo> ReadImageMetadataAsync(ArchiveEntry entry, CancellationToken token)
    {
        if (!await MetadataSlots.WaitAsync(TimeSpan.FromSeconds(15), token)) throw new TimeoutException("元数据队列暂不可访问。");
        var cancellation = CancellationTokenSource.CreateLinkedTokenSource(token);
        var work = Task.Run(async () =>
        {
            try
            {
                var ct = cancellation.Token; ct.ThrowIfCancellationRequested();
                await using var source = await entry.Archive.OpenEntryAsync(entry, ct).ConfigureAwait(false);
                using var data = new MetadataReadStream(source, MetadataInputLimit, ct);
                var image = await new MagickImageDecoder().ProbeAsync(data, ct).ConfigureAwait(false);
                data.Position = 0; ct.ThrowIfCancellationRequested();
                BitmapMetadataDatabase metadata; string? warning = null;
                try { metadata = new(new MetadataExtractorAccessor(data)); }
                catch (MetadataExtractor.ImageProcessingException ex) { metadata = BitmapMetadataDatabase.Default; warning = "元数据读取不支持或损坏：" + ex.Message; }
                ct.ThrowIfCancellationRequested();
                return new PagePictureInfo(image, metadata, "Magick.NET Q8 / MetadataExtractor", warning);
            }
            finally { MetadataSlots.Release(); cancellation.Dispose(); }
        });
        try { return await work.WaitAsync(TimeSpan.FromSeconds(15), token).ConfigureAwait(false); }
        catch
        {
            // 原生读取可能仍在执行；源和槽由真实工作结束时释放。
            try { cancellation.Cancel(); } catch (ObjectDisposedException) { }
            _ = ObserveMetadataAsync(work); throw;
        }
    }
    private static async Task ObserveMetadataAsync(Task<PagePictureInfo> work) { try { await work.ConfigureAwait(false); } catch { } }
}

/// <summary>仅对读取计费，跳过像素/随机定位不复制整个图片；请求流仍由外层所有者关闭。</summary>
internal sealed class MetadataReadStream(Stream source, long limit, CancellationToken token) : Stream
{
    private long _read;
    public override bool CanRead => source.CanRead;
    public override bool CanSeek => source.CanSeek;
    public override bool CanWrite => false;
    public override long Length => source.Length;
    public override long Position { get => source.Position; set { token.ThrowIfCancellationRequested(); source.Position = value; } }
    private int Allow(int count)
    {
        token.ThrowIfCancellationRequested();
        if (count == 0) return 0;
        if (_read >= limit) throw new NotSupportedException("图片元数据超过32MiB读取字节预算。");
        return (int)Math.Min(count, limit - _read);
    }
    public override int Read(byte[] buffer, int offset, int count) { var n = source.Read(buffer, offset, Allow(count)); _read += n; return n; }
    public override int Read(Span<byte> buffer) { var n = source.Read(buffer[..Allow(buffer.Length)]); _read += n; return n; }
    public override long Seek(long offset, SeekOrigin origin) { token.ThrowIfCancellationRequested(); return source.Seek(offset, origin); }
    public override void Flush() { }
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
}
