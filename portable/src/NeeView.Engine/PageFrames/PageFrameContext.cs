// Copyright (c) NeeLaboratory. 原源码关系与业务规则沿用仓库 MIT；P1子集适配见 docs/source-migration.json。
namespace NeeView.PageFrames;

/// <summary>原 PageFrameContext 阅读/尺寸与幻灯覆盖，排除控件和效果订阅。</summary>
public sealed class PageFrameContext(BookSettingConfig setting, Config config, SlideShow? slideShow = null) : IContentSizeCalculatorProfile
{
    public PageMode PageMode => setting.PageMode;
    public int FramePageSize => PageMode == PageMode.WidePage ? 2 : 1;
    public PageReadOrder ReadOrder => setting.BookReadOrder;
    public bool IsSupportedDividePage => setting.IsSupportedDividePage && FramePageSize == 1;
    public bool IsSupportedWidePage => setting.IsSupportedWidePage && FramePageSize == 2;
    public bool IsSupportedSingleFirstPage => setting.IsSupportedSingleFirstPage && FramePageSize == 2;
    public bool IsSupportedSingleLastPage => setting.IsSupportedSingleLastPage && FramePageSize == 2;
    public PageEndAction PageEndAction => slideShow?.IsPlaying == true ? config.SlideShow.PageEndAction : config.Book.PageEndAction;
    public bool IsLoopPage => PageEndAction == PageEndAction.SeamlessLoop;
    public PageFrameOrientation FrameOrientation => config.Book.Orientation;
    public bool IsPanorama => config.Book.IsPanorama && config.Book.MacPanoramaLayout == BrowseLayoutMode.Panorama;
    public double FrameMargin => IsPanorama && double.IsFinite(config.Book.FrameSpace) ? config.Book.FrameSpace : 1;
    public TimeSpan ScrollDuration => SafeDuration(config.View.ScrollDuration);
    public bool IsAutoScroll => slideShow?.IsPlayingAutoScroll == true;
    public TimeSpan AutoScrollDuration => IsAutoScroll ? SafeDuration(slideShow!.Interval / 1000) : TimeSpan.Zero;
    public TimeSpan PageChangeDuration => IsPanorama ? ScrollDuration : SafeDuration(slideShow?.IsPlaying == true ? config.SlideShow.PageMoveDuration : config.View.PageMoveDuration);
    public PageMoveType PageChangeType => PageChangeDuration == TimeSpan.Zero || IsPanorama ? PageMoveType.Scroll : slideShow?.IsPlaying == true ? config.SlideShow.PageMoveType : config.View.PageMoveType;
    /// <summary>非有限/负损坏配置无动画；合法原数值不截断为编辑范围。</summary>
    public static TimeSpan SafeDuration(double seconds) => double.IsFinite(seconds) && seconds > 0 && seconds < TimeSpan.MaxValue.TotalSeconds ? TimeSpan.FromSeconds(seconds) : TimeSpan.Zero;
    public bool IsStaticWidePage => config.Book.IsStaticWidePage && FramePageSize == 2;
    public bool IsInsertDummyPage => config.Book.IsInsertDummyPage;
    public bool IsInsertDummyFirstPage => config.Book.IsInsertDummyFirstPage;
    public bool IsInsertDummyLastPage => config.Book.IsInsertDummyLastPage;
    public double DividePageRate => Math.Clamp(config.Book.DividePageRate, .01, 1);
    public Size CanvasSize { get; set; } = new(1000, 800);
    public Size ReferenceSize => CanvasSize;
    public ImageCustomSizeConfig ImageCustomSizeConfig => config.ImageCustomSize;
    public ImageTrimConfig ImageTrimConfig => config.ImageTrim;
    public bool IsAspectRatioEnabled => config.Image.Standard.IsAspectRatioEnabled;
    public double DeviceScale { get; set; } = 1;
    public AutoRotateType AutoRotate => setting.AutoRotate;
    public AutoRotatePolicy AutoRotatePolicy => AutoRotatePolicy.FitToViewArea;
    public bool AllowFileContentAutoRotate => true;
    public PageStretchMode StretchMode => IsPanorama && config.View.StretchMode == PageStretchMode.Uniform
        ? FrameOrientation == PageFrameOrientation.Horizontal ? PageStretchMode.UniformToVertical : PageStretchMode.UniformToHorizontal : config.View.StretchMode;
    public double ContentsSpace => Math.Max(0, config.Book.ContentsSpace);
    public bool AllowEnlarge => config.View.AllowStretchScaleUp;
    public bool AllowReduce => config.View.AllowStretchScaleDown;
    public WidePageStretch WidePageStretch => config.Book.WidePageStretch;
}

/// <summary>原 BookContext 的读取部分，复用 BookPageAccessor。</summary>
public sealed class BookContext(IReadOnlyList<Page> pages) : BookPageAccessor(pages)
{
    public bool IsMedia => false;
}

/// <summary>原 PageSizeCalculator 顺序：自定义尺寸、裁剪；分割由下一层处理。</summary>
public sealed class PageSizeCalculator(PageFrameContext context, PageDataSource source)
{
    public Size GetPageSize()
    {
        var original = context.IsAspectRatioEnabled ? source.AspectSize : source.Size;
        var size = new PageCustomSize(context.ImageCustomSizeConfig, () => context.CanvasSize).TransformToCustomSize(original);
        var trim = context.ImageTrimConfig;
        // 保留原运算顺序；逐次相减会在整像素边界留下浮点尾差，使导出额外增加一行。
        return trim.IsEnabled ? new(Math.Max(size.Width - size.Width * (trim.Left + trim.Right), 0),
            Math.Max(size.Height - size.Height * (trim.Top + trim.Bottom), 0)) : size;
    }
}
/// <summary>原 PageViewSizeCalculator；裁剪后再按阅读方向分割，无 WPF 微小像素偏移。</summary>
public sealed class PageViewSizeCalculator(PageFrameContext context, PageDataSource source, PageRange range, int direction)
{
    public Size GetViewSize()
    {
        var size = new PageSizeCalculator(context, source).GetPageSize();
        return new(size.Width * (range.PartSize == 0 ? 0 : range.PartSize == 1 ? context.DividePageRate : 1), size.Height);
    }
    public Size GetSourceSize(Size viewSize)
    {
        double width = viewSize.Width / (range.PartSize == 1 ? context.DividePageRate : 1), height = viewSize.Height;
        var trim = context.ImageTrimConfig;
        if (trim.IsEnabled)
        {
            var widthRate = Math.Max(1 - (trim.Left + trim.Right), 0); var heightRate = Math.Max(1 - (trim.Top + trim.Bottom), 0);
            if (widthRate > 0 && heightRate > 0) return new(width / widthRate, height / heightRate);
        }
        return new(width, height);
    }
    public Rect GetViewBox()
    {
        var trim = context.ImageTrimConfig;
        var rect = trim.IsEnabled ? new Rect(trim.Left, trim.Top, Math.Max(1 - (trim.Left + trim.Right), 0), Math.Max(1 - (trim.Top + trim.Bottom), 0)) : new(0, 0, 1, 1);
        if (range.PartSize == 0) return new(rect.X, rect.Y, 0, rect.Height);
        if (range.PartSize != 1) return rect;
        bool left = (range.Min.Part == 0) != (direction == -1);
        double width = rect.Width * context.DividePageRate;
        return new(left ? rect.X : rect.X + rect.Width - width, rect.Y, width, rect.Height);
    }
}

/// <summary>数值几何取代 WPF Transform；引擎没有显示对象。</summary>
public static class GeometryMath
{
    /// <summary>计算旋转后轴对齐包围尺寸。</summary>
    public static Size RotateSize(Size size, double angle)
    {
        var radian = angle * Math.PI / 180;
        var c = Math.Abs(Math.Cos(radian)); var s = Math.Abs(Math.Sin(radian));
        return new(size.Width * c + size.Height * s, size.Width * s + size.Height * c);
    }
}
