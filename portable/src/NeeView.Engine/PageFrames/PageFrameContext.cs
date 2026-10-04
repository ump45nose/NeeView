// Copyright (c) NeeLaboratory. 原源码关系与业务规则沿用仓库 MIT；P1子集适配见 docs/source-migration.json。
namespace NeeView.PageFrames;

/// <summary>原 PageFrameContext 的基础阅读/尺寸部分，排除控件、幻灯片和效果订阅。</summary>
public sealed class PageFrameContext(BookSettingConfig setting, Config config) : IContentSizeCalculatorProfile
{
    public PageMode PageMode => setting.PageMode;
    public int FramePageSize => PageMode == PageMode.WidePage ? 2 : 1;
    public PageReadOrder ReadOrder => setting.BookReadOrder;
    public bool IsSupportedDividePage => setting.IsSupportedDividePage && FramePageSize == 1;
    public bool IsSupportedWidePage => setting.IsSupportedWidePage && FramePageSize == 2;
    public bool IsSupportedSingleFirstPage => setting.IsSupportedSingleFirstPage && FramePageSize == 2;
    public bool IsSupportedSingleLastPage => setting.IsSupportedSingleLastPage && FramePageSize == 2;
    public bool IsLoopPage => config.Book.PageEndAction == PageEndAction.SeamlessLoop;
    public PageFrameOrientation FrameOrientation => config.Book.Orientation;
    public bool IsPanorama => config.Book.IsPanorama && config.Book.MacPanoramaLayout == BrowseLayoutMode.Panorama;
    public double FrameMargin => IsPanorama && double.IsFinite(config.Book.FrameSpace) ? config.Book.FrameSpace : 1;
    public TimeSpan ScrollDuration => SafeDuration(config.View.ScrollDuration);
    public TimeSpan PageChangeDuration => SafeDuration(config.View.PageMoveDuration);
    public PageMoveType PageChangeType => PageChangeDuration == TimeSpan.Zero ? PageMoveType.Scroll : config.View.PageMoveType;
    /// <summary>非有限/负损坏配置无动画；合法原数值不截断为编辑范围。</summary>
    public static TimeSpan SafeDuration(double seconds) => double.IsFinite(seconds) && seconds > 0 && seconds < TimeSpan.MaxValue.TotalSeconds ? TimeSpan.FromSeconds(seconds) : TimeSpan.Zero;
    public bool IsStaticWidePage => config.Book.IsStaticWidePage && FramePageSize == 2;
    public bool IsInsertDummyPage => config.Book.IsInsertDummyPage;
    public bool IsInsertDummyFirstPage => config.Book.IsInsertDummyFirstPage;
    public bool IsInsertDummyLastPage => config.Book.IsInsertDummyLastPage;
    public double DividePageRate => Math.Clamp(config.Book.DividePageRate, .01, 1);
    public Size CanvasSize { get; set; } = new(1000, 800);
    public Size ReferenceSize => CanvasSize;
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

/// <summary>原 PageSizeCalculator 的 P1 入口；高级固定尺寸和裁剪尚未启用。</summary>
public sealed class PageSizeCalculator(PageFrameContext context, PageDataSource source)
{
    /// <summary>返回方向校正后的图片尺寸。</summary>
    public Size GetPageSize() { _ = context; return source.Size; }
}

/// <summary>保留原 PageViewSizeCalculator 的分割尺寸与左右区域算法。</summary>
public sealed class PageViewSizeCalculator(PageFrameContext context, PageDataSource source, PageRange range, int direction)
{
    /// <summary>按原分割比例计算当前部分的逻辑尺寸。</summary>
    public Size GetViewSize() => new(source.Size.Width * (range.PartSize == 1 ? context.DividePageRate : 1), source.Size.Height);
    /// <summary>返回归一化裁剪区域，左右阅读方向决定分割先后。</summary>
    public Rect GetViewBox()
    {
        if (range.PartSize != 1) return new(0, 0, 1, 1);
        bool isLeftPart = range.Min.Part == 0;
        if (direction == -1) isLeftPart = !isLeftPart;
        double half = context.DividePageRate;
        return new(isLeftPart ? 0 : 1 - half, 0, half, 1);
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
