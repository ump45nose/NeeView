using CommunityToolkit.Mvvm.ComponentModel;
namespace NeeView.MacOS.ViewModels;
/// <summary>原PDF/源尺寸设置的独立草稿，不打开文件、不渲染、不保存JSON。</summary>
public sealed class PdfSettingsViewModel : ObservableObject
{
    private bool _enabled, _limitSource;
    private decimal _width, _height, _maximumWidth, _maximumHeight;
    public bool IsEnabled { get => _enabled; set => SetProperty(ref _enabled, value); }
    public bool IsLimitSourceSize { get => _limitSource; set => SetProperty(ref _limitSource, value); }
    public decimal RenderWidth { get => _width; set => SetProperty(ref _width, value); }
    public decimal RenderHeight { get => _height; set => SetProperty(ref _height, value); }
    public decimal MaximumWidth { get => _maximumWidth; set => SetProperty(ref _maximumWidth, value); }
    public decimal MaximumHeight { get => _maximumHeight; set => SetProperty(ref _maximumHeight, value); }
    public decimal RenderWidthMaximum { get; } public decimal RenderHeightMaximum { get; }
    public decimal MaximumWidthMaximum { get; } public decimal MaximumHeightMaximum { get; }
    /// <summary>打开只复制当前实际值，原有效范围外数值仍可无改动保存。</summary>
    public PdfSettingsViewModel(PdfArchiveConfig pdf, PerformanceConfig performance)
    {
        _enabled = pdf.IsEnabled; _limitSource = performance.IsLimitSourceSize;
        _width = (decimal)pdf.RenderSize.Width; _height = (decimal)pdf.RenderSize.Height;
        _maximumWidth = (decimal)performance.MaximumSize.Width; _maximumHeight = (decimal)performance.MaximumSize.Height;
        RenderWidthMaximum = Math.Max(16384, _width); RenderHeightMaximum = Math.Max(16384, _height);
        MaximumWidthMaximum = Math.Max(32768, _maximumWidth); MaximumHeightMaximum = Math.Max(32768, _maximumHeight);
    }
    /// <summary>只有原五JSON事务的应用回调执行此方法；取消保持当前设置。</summary>
    public void Apply(PdfArchiveConfig pdf, PerformanceConfig performance)
    {
        pdf.IsEnabled = IsEnabled; pdf.RenderSize = new((double)RenderWidth, (double)RenderHeight);
        performance.IsLimitSourceSize = IsLimitSourceSize; performance.MaximumSize = new((double)MaximumWidth, (double)MaximumHeight);
    }
}
