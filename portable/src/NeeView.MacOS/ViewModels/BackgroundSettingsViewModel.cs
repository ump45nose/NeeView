using CommunityToolkit.Mvvm.ComponentModel;
using NeeView;
namespace NeeView.MacOS.ViewModels;

/// <summary>原背景及像素保持字段的独立草稿；不读图片或创建刷，保存交给原JSON事务。</summary>
public sealed class BackgroundSettingsViewModel : ObservableObject
{
    public IReadOnlyList<BackgroundChoice> Backgrounds { get; }
    public IReadOnlyList<BrushChoice> Brushes { get; }
    private BackgroundType _background;
    private BrushType _brush;
    private string _color, _pageColor, _image;
    private bool _checker, _nearest;
    private decimal _threshold;
    private readonly double _originalThreshold;
    private bool _thresholdEdited;
    public BackgroundSettingsViewModel(BackgroundConfig background, ImageDotKeepConfig dot)
    {
        _background = background.BackgroundType; _brush = background.CustomBackground.Type;
        _color = background.CustomBackground.Color.ToString(); _pageColor = background.PageBackgroundColor.ToString();
        _image = background.CustomBackground.ImageFileName ?? ""; _checker = background.IsPageBackgroundChecker;
        _nearest = dot.IsEnabled; _originalThreshold = dot.Threshold;
        // 原JSON允许任意有限double；控件范围只限制草稿表示，不截断无关保存的原值。
        _threshold = dot.Threshold >= (double)decimal.MaxValue ? decimal.MaxValue :
            dot.Threshold <= (double)decimal.MinValue ? decimal.MinValue : (decimal)dot.Threshold;
        Backgrounds = Enum.GetValues<BackgroundType>().Append(_background).Distinct().Select(value => new BackgroundChoice(value, value switch
        { BackgroundType.Black => "黑色", BackgroundType.White => "白色", BackgroundType.Auto => "自动（图片首像素）", BackgroundType.Check => "浅色棋盘格", BackgroundType.CheckDark => "深色棋盘格", BackgroundType.Custom => "自定义", _ => "未知背景（保留）" })).ToArray();
        Brushes = Enum.GetValues<BrushType>().Append(_brush).Distinct().Select(value => new BrushChoice(value, value switch
        { BrushType.SolidColor => "单色", BrushType.ImageTile => "图像平铺", BrushType.ImageFill => "图像拉伸", BrushType.ImageUniform => "图像等比包含", BrushType.ImageUniformToFill => "图像等比裁满", _ => "未知刷（保留）" })).ToArray();
    }
    public BackgroundType Background { get => _background; set => SetProperty(ref _background, value); }
    public BrushType Brush { get => _brush; set => SetProperty(ref _brush, value); }
    public string Color { get => _color; set => SetProperty(ref _color, value); }
    public string PageColor { get => _pageColor; set => SetProperty(ref _pageColor, value); }
    public string Image { get => _image; set => SetProperty(ref _image, value); }
    public bool Checker { get => _checker; set => SetProperty(ref _checker, value); }
    public bool Nearest { get => _nearest; set => SetProperty(ref _nearest, value); }
    public decimal Threshold { get => _threshold; set { if (SetProperty(ref _threshold, value)) _thresholdEdited = true; } }
    public decimal ThresholdMinimum => Math.Min(0, Threshold);
    public decimal ThresholdMaximum => Math.Max(5, Threshold);
    /// <summary>先验证两种颜色再写原分支；原事务负责失败回滚，未知模式不静默替换。</summary>
    public void Apply(BackgroundConfig background, ImageDotKeepConfig dot)
    {
        var color = ThemeRgba.Parse(Color); var page = ThemeRgba.Parse(PageColor);
        background.BackgroundType = Background; background.CustomBackground.Type = Brush;
        background.CustomBackground.Color = color; background.CustomBackground.ImageFileName = string.IsNullOrWhiteSpace(Image) ? null : Image;
        background.PageBackgroundColor = page; background.IsPageBackgroundChecker = Checker;
        dot.IsEnabled = Nearest; dot.Threshold = _thresholdEdited ? (double)Threshold : _originalThreshold;
    }
}
public sealed record BackgroundChoice(BackgroundType Value, string Label);
public sealed record BrushChoice(BrushType Value, string Label);
