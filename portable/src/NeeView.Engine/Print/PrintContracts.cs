namespace NeeView;
/// <summary>原查看器/原图尺寸与坐标，单位96DPI；不含控件或系统打印机对象。</summary>
public sealed record PrintRequest(PrintParameters Parameters,double ContentWidth,double ContentHeight,
    double ViewWidth=0,double ViewHeight=0,double ContentX=0,double ContentY=0);
public sealed record PrintSystemCapabilities(double PrintableWidth,double PrintableHeight,PrintMargin HardwareMargin=default);
public readonly record struct PrintPageRect(double X,double Y,double Width,double Height);
public sealed record PrintLayoutPage(int Index,PrintPageRect ContentRect,PrintPageRect CellRect);
public sealed record PrintLayoutResult(PrintMargin EffectiveMargin,double CellWidth,double CellHeight,IReadOnlyList<PrintLayoutPage> Pages);
/// <summary>请求级独立PNG及原几何，调用方在系统操作返回前保留。</summary>
public sealed record PrintImage(byte[] Png,double Width,double Height,double ViewWidth,double ViewHeight,double X=0,double Y=0)
{
    /// <summary>独立画布背景覆盖整铺纸区域，不能只填图像本身的矩形。</summary>
    public byte[]? BackgroundPng { get; init; }
}
public interface IPrintService
{
    /// <summary>系统打印对话框与真实结果；取消返回false，不能误报已打印。</summary>
    Task<bool> PrintAsync(PrintImage image,PrintParameters parameters,CancellationToken token=default);
}
