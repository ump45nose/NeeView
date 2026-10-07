using ImageMagick;
using NeeView;

namespace NeeView.Backends;

/// <summary>Q8 解码器：原生资源限额、首帧、EXIF、色彩和预乘 alpha。</summary>
public sealed class MagickImageDecoder(IAnimatedImageDecoder? pngAnimation = null) : IImageDecoder, IAnimatedImageDecoder
{
    private readonly MagickAnimatedImageDecoder _animated = new();
    /// <summary>原动画来源调用同一图片后端，由工厂共享解码槽和资源计费。</summary>
    public async Task<IAnimatedImageSource?> OpenAnimationAsync(Stream stream, DecodeRequest request, long workingBudget, CancellationToken token)
    {
        if (pngAnimation is not null && await pngAnimation.OpenAnimationAsync(stream, request, workingBudget, token).ConfigureAwait(false) is { } png) return png;
        return await _animated.OpenAnimationAsync(stream, request, workingBudget, token).ConfigureAwait(false);
    }
    private const long WorkingBudget = 256L * 1024 * 1024;
    /// <summary>设置原生解码工作限额；不将缓存预算误当作进程内存上限。</summary>
    private static void ConfigureLimits()
    {
        ResourceLimits.Memory = 256 * 1024 * 1024;
        ResourceLimits.MaxMemoryRequest = 256 * 1024 * 1024;
        ResourceLimits.Disk = 512 * 1024 * 1024;
        ResourceLimits.Thread = 1;
    }
    // 保持原静态/动画共享的原生工作限制，进程只设置一次。
    static MagickImageDecoder() => ConfigureLimits();
    /// <summary>输入可读流，只探测尺寸和方向，不分配完整像素。</summary>
    public Task<ImageInfo> ProbeAsync(Stream stream, CancellationToken token) => Task.Run(() =>
    {
        token.ThrowIfCancellationRequested();
        if (stream is PdfPageStream pdf) return pdf.Probe();
        using var image = new MagickImage(); image.Ping(stream);
        token.ThrowIfCancellationRequested();
        var swapped = image.Orientation is OrientationType.LeftTop or OrientationType.RightTop or OrientationType.RightBottom or OrientationType.LeftBottom;
        var size = new Size(swapped ? image.Height : image.Width, swapped ? image.Width : image.Height);
        // WIC 的 96DIP/DPI 规则；WebP 原专用探测器的缺省72DPI保留。
        var density = image.Density;
        double multiplier = density.Units == DensityUnit.PixelsPerCentimeter ? 2.54 : 1;
        double fallback = image.Format == MagickFormat.WebP ? 72 : 96;
        double Dpi(double value) => density.Units == DensityUnit.Undefined || !double.IsFinite(value) || value <= 0 ? fallback : value * multiplier;
        double x = Dpi(density.X), y = Dpi(density.Y);
        if (swapped) (x, y) = (y, x);
        return new ImageInfo(size, image.Format.ToString()) { AspectSize = new(size.Width * 96 / x, size.Height * 96 / y), DpiX = x, DpiY = y,
            BitsPerPixel = checked((int)image.Depth * (int)image.ChannelCount) };
    }, token);
    /// <summary>输入解码规格，返回 BGRA 8 位预乘像素；原生完成后再次检查取消。</summary>
    public Task<DecodedImageLease> DecodeAsync(Stream stream, DecodeRequest request, CancellationToken token) => stream is PdfPageStream pdf
        ? pdf.DecodeAsync(request, token) : Task.Run(() =>
    {
        token.ThrowIfCancellationRequested();
        using var image = new MagickImage();
        if (!stream.CanSeek) throw new InvalidDataException("解码流必须可定位。");
        image.Ping(stream); var width = image.Width; var height = image.Height; var format = image.Format;
        var estimate = checked((long)width * height * 8);
        var oriented = image.Orientation is OrientationType.LeftTop or OrientationType.RightTop or OrientationType.RightBottom or OrientationType.LeftBottom;
        var sourceSize = new Size(oriented ? height : width, oriented ? width : height);
        // JPEG 采样最多缩小 8 倍，每边缩小后仍须符合工作预算。
        var sampledEstimate = checked(((long)width + 7) / 8 * (((long)height + 7) / 8) * 8);
        if (width == 0 || height == 0 || (format == MagickFormat.Jpeg && sampledEstimate > WorkingBudget))
            throw new NotSupportedException("图片尺寸超过安全降采样能力。");
        if (estimate > WorkingBudget && format != MagickFormat.Jpeg)
            throw new NotSupportedException("图片超过解码工作预算，当前格式不能安全降采样。");
        stream.Position = 0;
        var settings = new MagickReadSettings { FrameIndex = 0, FrameCount = 1 };
        var filterEnabled = request.ResizeFilter is not null && !request.IsThumbnail && request.TargetWidth > 0 && request.TargetHeight > 0;
        if (format == MagickFormat.Jpeg && (!filterEnabled || estimate > WorkingBudget) && (width > request.TargetWidth || height > request.TargetHeight || estimate > WorkingBudget))
        {
            var factor = 1;
            while (estimate / (factor * factor) > WorkingBudget && factor < 8) factor *= 2;
            var hintWidth = Math.Min(Math.Max(1, request.TargetWidth), (int)width / factor);
            var hintHeight = Math.Min(Math.Max(1, request.TargetHeight), (int)height / factor);
            settings.SetDefine(MagickFormat.Jpeg, "size", $"{Math.Max(1, hintWidth)}x{Math.Max(1, hintHeight)}");
        }
        image.Read(stream, settings);
        if (checked((long)image.Width * image.Height * 8) > WorkingBudget)
            throw new NotSupportedException("解码后尺寸仍超出工作预算。");
        image.AutoOrient();
        // 有 ICC 时转换，未声明的图像按 sRGB 解释。
        if (image.GetColorProfile() is not null) image.TransformColorSpace(ColorProfiles.SRGB);
        else image.ColorSpace = ColorSpace.sRGB;
        ThemeRgba sourceColor;
        using (var sourcePixels = image.GetPixels())
        {
            var first = sourcePixels.GetPixel(0, 0).ToColor()!;
            // 原GetOneColor保留首像素RGB并强制不透明；显示像素稍后单独预乘。
            sourceColor = new(255, first.R, first.G, first.B);
        }
        var ratio = Math.Min(1, Math.Min((double)Math.Max(1, request.TargetWidth) / image.Width,
            (double)Math.Max(1, request.TargetHeight) / image.Height));
        if (filterEnabled)
        {
            var dw = Math.Max(1, (int)(image.Width * ratio));
            var dh = Math.Max(1, (int)(image.Height * ratio));
            image.Depth = 8;
            if (checked((long)image.Width * image.Height * 4 + (long)dw * dh * 4) >= 128L * 1024 * 1024)
                throw new NotSupportedException("缩放滤镜源及输出超过托管工作预算。");
            var input = image.ToByteArray(MagickFormat.Bgra);
            // Reserve a second output for native sharpening/encoding in addition to the resampler workspace.
            var budget = 128L * 1024 * 1024 - checked((long)dw * dh * 4);
            var filtered = ResizeKernel.Resize(input, (int)image.Width, (int)image.Height, dw, dh, request.ResizeFilter!.Interpolation, token, budget);
            if (request.ResizeFilter.Sharpen)
            {
                var mask = ResolveMask(request.ResizeFilter, Math.Min((double)image.Width / dw, (double)image.Height / dh));
                if (!double.IsFinite(mask.Radius) || (mask.Amount > 0 && mask.Radius <= 0) || mask.Radius > 32)
                    throw new NotSupportedException("锐化半径超出安全处理范围。");
                using var sharp = new MagickImage(filtered, new MagickReadSettings { Width = (uint)dw, Height = (uint)dh, Format = MagickFormat.Bgra });
                if (mask.Amount > 0)
                {
                    // Mature native sharpening replaces MagicScaler's luminance mask; alpha is untouched.
                    sharp.UnsharpMask(0, mask.Radius, mask.Amount / 100.0, mask.Threshold / 255.0, Channels.RGB);
                    filtered = sharp.ToByteArray(MagickFormat.Bgra);
                }
            }
            token.ThrowIfCancellationRequested();
            for (var offset = 0; offset < filtered.Length; offset += 4) { var a=filtered[offset+3]; filtered[offset]=(byte)((filtered[offset]*a+127)/255); filtered[offset+1]=(byte)((filtered[offset+1]*a+127)/255); filtered[offset+2]=(byte)((filtered[offset+2]*a+127)/255); }
            return new DecodedImageLease(new(dw, dh), filtered, sourceColor, sourceSize);
        }
        if (ratio < 1) image.Resize((uint)Math.Max(1, image.Width * ratio), (uint)Math.Max(1, image.Height * ratio));
        token.ThrowIfCancellationRequested();
        image.Depth = 8;
        var pixels = image.ToByteArray(MagickFormat.Bgra);
        if (pixels.LongLength != checked((long)image.Width * image.Height * 4)) throw new InvalidDataException("解码器像素格式与 BGRA8 契约不一致。");
        // 原始 BGRA 输出为直 alpha；Avalonia 显示契约使用预乘 alpha。
        for (var offset = 0; offset < pixels.Length; offset += 4)
        {
            var alpha = pixels[offset + 3];
            pixels[offset] = (byte)((pixels[offset] * alpha + 127) / 255);
            pixels[offset + 1] = (byte)((pixels[offset + 1] * alpha + 127) / 255);
            pixels[offset + 2] = (byte)((pixels[offset + 2] * alpha + 127) / 255);
        }
        token.ThrowIfCancellationRequested();
        return new DecodedImageLease(new((int)image.Width, (int)image.Height), pixels, sourceColor, sourceSize);
    }, token);

    /// <summary>保留0.15.0 Amount未正值时按原缩放率选择默认锐化参数的规则。</summary>
    private static (int Amount, double Radius, int Threshold) ResolveMask(ImageResizeFilterParameters settings, double ratio) => settings.Amount > 0
        ? (settings.Amount, settings.Radius, settings.Threshold) : ratio switch
        {
            1 => (0, 0, 0), < .5 => (40, 1.5, 0), < 1 => (30, 1, 0), < 2 => (30, .75, 4),
            < 4 => (75, .5, 2), < 6 => (50, .75, 2), < 8 => (100, .6, 1), < 10 => (125, .5, 0), _ => (150, .5, 0)
        };
}
