using ImageMagick;
using NeeView;
namespace NeeView.Backends;

/// <summary>原AnimatedPageSource后端替换：有界原生帧合成，按需输出当前帧。</summary>
public sealed class MagickAnimatedImageDecoder : IAnimatedImageDecoder
{
    /// <summary>先探测帧数/完整画布/原生准备峰值，再实际解码；静态图片返回空。</summary>
    public Task<IAnimatedImageSource?> OpenAnimationAsync(Stream stream, DecodeRequest request, long workingBudget, CancellationToken token) => Task.Run(() =>
    {
        if (!stream.CanSeek) throw new InvalidDataException("动画解码流必须可定位。");
        token.ThrowIfCancellationRequested(); stream.Position = 0;
        using var probe = new MagickImageCollection(); probe.Ping(stream);
        token.ThrowIfCancellationRequested();
        if (probe.Count <= 1) return null;
        var type = probe[0].Format switch { MagickFormat.Gif => AnimatedImageType.Gif, MagickFormat.Png or MagickFormat.APng => AnimatedImageType.Png, MagickFormat.WebP => AnimatedImageType.Webp, _ => AnimatedImageType.None };
        if (type == AnimatedImageType.None) return null;
        if (probe.Count > 4096) throw new NotSupportedException("动画帧数超过安全上限4096。");
        var width = probe.Max(i => Math.Max(i.Width, i.Page.Width)); var height = probe.Max(i => Math.Max(i.Height, i.Page.Height));
        if (width == 0 || height == 0) throw new InvalidDataException("动画画布无效。");
        // 原始集合与完整合成集合暂时共存；额外两帧覆盖clone/输出工作，不能只计算最终缓存。
        var frameBytes = checked((long)width * height * 8);
        var peak = checked(frameBytes * (probe.Count * 2L + 2));
        if (peak > workingBudget) throw new NotSupportedException("动画超过解码工作预算，请关闭对应格式动画后读取首帧。");
        var durations = probe.Select(i => TimeSpan.FromSeconds(i.AnimationDelay == 0 ? .1 : (double)i.AnimationDelay / Math.Max(1, i.AnimationTicksPerSecond))).ToArray();
        var frames = new MagickImageCollection();
        try
        {
            stream.Position = 0; frames.Read(stream); token.ThrowIfCancellationRequested(); frames.Coalesce(); token.ThrowIfCancellationRequested();
            if (frames.Count != probe.Count) throw new InvalidDataException("动画帧索引与实际读取不一致。");
            long resources = 0;
            foreach (var image in frames)
            {
                image.AutoOrient();
                if (image.GetColorProfile() is not null) image.TransformColorSpace(ColorProfiles.SRGB); else image.ColorSpace = ColorSpace.sRGB;
                var ratio = Math.Min(1, Math.Min((double)Math.Max(1, request.TargetWidth) / image.Width, (double)Math.Max(1, request.TargetHeight) / image.Height));
                if (ratio < 1) image.Resize((uint)Math.Max(1, image.Width * ratio), (uint)Math.Max(1, image.Height * ratio));
                image.Depth = 8; resources = checked(resources + (long)image.Width * image.Height * 8); token.ThrowIfCancellationRequested();
            }
            return (IAnimatedImageSource?)new Source(frames, new(new(frames[0].Width, frames[0].Height), type, durations, resources));
        }
        catch { frames.Dispose(); throw; }
    }, token);

    private sealed class Source(MagickImageCollection frames, AnimatedImageInfo info) : IAnimatedImageSource
    {
        private readonly object _gate = new();
        private bool _disposed;
        public AnimatedImageInfo Info => info;
        /// <summary>串行读取完整合成帧；不重复解压/合成、不保留全部托管输出。</summary>
        public Task<DecodedImageLease> ReadFrameAsync(int index, CancellationToken token) => Task.Run(() =>
        {
            lock (_gate)
            {
                ObjectDisposedException.ThrowIf(_disposed, this); token.ThrowIfCancellationRequested();
                if ((uint)index >= frames.Count) throw new ArgumentOutOfRangeException(nameof(index));
                var image = frames[index]; var pixels = image.ToByteArray(MagickFormat.Bgra);
                if (pixels.LongLength != checked((long)image.Width * image.Height * 4)) throw new InvalidDataException("动画像素格式与BGRA8不一致。");
                for (int p = 0; p < pixels.Length; p += 4)
                { var a = pixels[p + 3]; pixels[p] = (byte)((pixels[p] * a + 127) / 255); pixels[p + 1] = (byte)((pixels[p + 1] * a + 127) / 255); pixels[p + 2] = (byte)((pixels[p + 2] * a + 127) / 255); }
                token.ThrowIfCancellationRequested(); return new DecodedImageLease(new(image.Width, image.Height), pixels);
            }
        }, token);
        /// <summary>与帧读取同锁关闭，不能释放仍在原生调用中的集合。</summary>
        public void Dispose() { lock (_gate) { if (_disposed) return; _disposed = true; frames.Dispose(); } }
    }
}
