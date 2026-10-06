// Copyright (c) NeeLaboratory. 原PdfArchiveConfig/PerformanceConfig/PdfArchiveProfile已使用字段。
namespace NeeView;

/// <summary>原PDF开关与渲染规格；其他配置继续由原JSON保留。</summary>
public sealed class PdfArchiveConfig
{
    private Size _renderSize = new(1920, 1080);
    public bool IsEnabled { get; set; } = true;
    /// <summary>原默认1920×1080，每轴最低256；非有限导入值不能进入原生分配。</summary>
    [System.Text.Json.Serialization.JsonConverter(typeof(JsonSizeConverter))]
    public Size RenderSize { get => _renderSize; set => _renderSize = new(Clamp(value.Width, 256), Clamp(value.Height, 256)); }
    internal static double Clamp(double value, double minimum) => double.IsFinite(value) ? Math.Max(value, minimum) : minimum;
}
/// <summary>原页面最大尺寸规则；与缓存/解码任务预算分开。</summary>
public sealed class PerformanceConfig
{
    private Size _maximumSize = new(4096, 4096);
    public bool IsLimitSourceSize { get; set; }
    [System.Text.Json.Serialization.JsonConverter(typeof(JsonSizeConverter))]
    public Size MaximumSize { get => _maximumSize; set => _maximumSize = new(PdfArchiveConfig.Clamp(value.Width, 1024), PdfArchiveConfig.Clamp(value.Height, 1024)); }
}
/// <summary>原PdfArchiveProfile与尺寸算法；默认导出和按需渲染分别调用。</summary>
public static class PdfArchiveProfile
{
    public static Size SizeLimitedRenderSize => new(Math.Min(Config.Current.Archive.Pdf.RenderSize.Width, Config.Current.Performance.MaximumSize.Width),
        Math.Min(Config.Current.Archive.Pdf.RenderSize.Height, Config.Current.Performance.MaximumSize.Height));
    /// <summary>原PictureInfo.Size：默认保留源尺寸，仅按原启用开关缩小。</summary>
    public static Size GetDisplaySize(Size source) => Config.Current.Performance.IsLimitSourceSize ? Fit(source, Config.Current.Performance.MaximumSize, false) : source;
    /// <summary>沿固定Pdfium默认流的判断顺序：小页等比放大至规格，大页保持；安全预算另行拒绝。</summary>
    public static Size GetExportSize(Size source) => source.Width <= SizeLimitedRenderSize.Width && source.Height <= SizeLimitedRenderSize.Height
        ? Fit(source, SizeLimitedRenderSize, true) : source;
    /// <summary>原PdfPictureSource.FixedSize：正文有最小渲染规格，最大值每轴取源尺寸与MaximumSize较大者。</summary>
    public static Size GetRenderSize(Size source, Size requested, bool thumbnail)
    {
        var size = Fit(source, requested, true);
        if (thumbnail) return size;
        var minimum = Config.Current.Archive.Pdf.RenderSize;
        if (size.Width <= minimum.Width && size.Height <= minimum.Height) size = Fit(size, minimum, true);
        var maximum = Config.Current.Performance.MaximumSize;
        return Fit(size, new(Math.Max(source.Width, maximum.Width), Math.Max(source.Height, maximum.Height)), false);
    }
    /// <summary>保持宽高比，不改变原页框/导航规则。</summary>
    public static Size Fit(Size source, Size target, bool allowUpscale)
    {
        var scale = Math.Min(target.Width / source.Width, target.Height / source.Height); if (!allowUpscale) scale = Math.Min(1, scale);
        return new(Math.Max(1, source.Width * scale), Math.Max(1, source.Height * scale));
    }
}
