// Copyright (c) NeeLaboratory. MIT; original ColorizeEffectUnit parameter collection, without WPF LUT brush.
using System.Collections.ObjectModel;
namespace NeeView.Effects;
/// <summary>原五点色阶及亮度权重；LUT 由渲染边界管理，原参数不拥有纹理。</summary>
public sealed class ColorizeEffectUnit : EffectUnit
{
    public ColorizeEffectUnit() : base(EffectSampleType.Luminance) { }
    private ObservableCollection<ColorizeControlPoint> _points = new()
    {
        new(ThemeRgba.Parse("Black"), 3), new(ThemeRgba.Parse("Indigo"), 1), new(ThemeRgba.Parse("IndianRed"), 1),
        new(ThemeRgba.Parse("Wheat"), 1), new(ThemeRgba.Parse("AliceBlue"), 1)
    };
    public ObservableCollection<ColorizeControlPoint> Points { get => _points; set => SetProperty(ref _points, value); }
    private double _luminanceWeight = 1;
    public double LuminanceWeight { get => _luminanceWeight; set => SetProperty(ref _luminanceWeight, value); }
}
