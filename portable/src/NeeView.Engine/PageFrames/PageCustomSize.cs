// Copyright (c) NeeLaboratory. 原 PageCustomSize；IStaticFrame 替换为 viewport 委托。
using NeeLaboratory;
namespace NeeView;
public sealed class PageCustomSize
{
    private readonly ImageCustomSizeConfig _customSize; private readonly Func<Size> _viewport;
    public PageCustomSize(ImageCustomSizeConfig config, Func<Size> viewport) { _customSize = config; _viewport = viewport; }
    public Size TransformToCustomSize(Size originalSize) => !_customSize.IsEnabled || originalSize.IsEmptyOrZero() ? originalSize : Lerp(originalSize, ApplyAspectRatio(originalSize, _customSize.Size, _customSize.AspectRatio, _customSize.IsAlignLongSide), _customSize.ApplicabilityRate);
    private static Size Lerp(Size a, Size b, double rate) => new(a.Width + (b.Width - a.Width) * rate, a.Height + (b.Height - a.Height) * rate);
    private Size ApplyAspectRatio(Size source, Size target, CustomSizeAspectRatio ratio, bool transpose)
    {
        if (source.IsEmptyOrZero()) return source; if (transpose) target = target.Transpose();
        var reference = ratio switch { CustomSizeAspectRatio.None => target, CustomSizeAspectRatio.Origin => target, CustomSizeAspectRatio.Ratio_1_1 => target.AspectRatioUniformed(1,1), CustomSizeAspectRatio.Ratio_2_3 => target.AspectRatioUniformed(2,3), CustomSizeAspectRatio.Ratio_4_3 => target.AspectRatioUniformed(4,3), CustomSizeAspectRatio.Ratio_8_9 => target.AspectRatioUniformed(8,9), CustomSizeAspectRatio.Ratio_16_9 => target.AspectRatioUniformed(16,9), CustomSizeAspectRatio.HalfView => target.AspectRatioUniformed(_viewport().Width*.5, _viewport().Height), CustomSizeAspectRatio.View => target.AspectRatioUniformed(_viewport().Width, _viewport().Height), _ => throw new NotImplementedException() };
        var result = ratio == CustomSizeAspectRatio.Origin ? source.Uniformed(ApplyLongSide(reference, source, transpose)) : ApplyLongSide(reference, source, transpose); return result;
    }
    private static Size ApplyLongSide(Size reference, Size source, bool transpose) => transpose && reference.IsHorizontally() != source.IsHorizontally() ? reference.Transpose() : reference;
}
internal static class ImageGeometryExtensions
{
    public static bool IsEmptyOrZero(this Size s) => s.Width <= 0 || s.Height <= 0;
    public static bool IsHorizontally(this Size s) => s.Width > s.Height;
    public static Size Transpose(this Size s) => new(s.Height, s.Width);
    public static Size Uniformed(this Size s, Size target) => new(s.Width * Math.Min(target.Width / s.Width, target.Height / s.Height), s.Height * Math.Min(target.Width / s.Width, target.Height / s.Height));
    public static Size AspectRatioUniformed(this Size s, double width, double height) { var r = width / height; return s.Width / s.Height >= r ? new(s.Height * r, s.Height) : new(s.Width, s.Width / r); }
}
