// Copyright (c) NeeLaboratory. MIT; migrated original effect parameter source.
using System.ComponentModel;

namespace NeeView.Effects
{
    /// <summary>原效果参数和通知；WPF Adapter 在 Mac 绘制边界替换。</summary>
    public partial class ColorToneEffectUnit : EffectUnit
    {
        private ThemeRgba _darkColor = ThemeRgba.FromArgb(0xFF, 0x33, 0x80, 0x00);
        private ThemeRgba _lightColor = ThemeRgba.FromArgb(0xFF, 0xFF, 0xE5, 0x80);
        private double _toneAmount = 0.5;
        private double _desaturation = 0.5;

        public ColorToneEffectUnit() : base(EffectSampleType.Luminance)
        {
        }

        [DefaultValue(typeof(ThemeRgba), "#FF338000")]
        public ThemeRgba DarkColor
        {
            get => _darkColor;
            set => SetProperty(ref _darkColor, value);
        }

        [DefaultValue(typeof(ThemeRgba), "#FFFFE580")]
        public ThemeRgba LightColor
        {
            get => _lightColor;
            set => SetProperty(ref _lightColor, value);
        }

        [DefaultValue(0.5)]
        public double ToneAmount
        {
            get => _toneAmount;
            set => SetProperty(ref _toneAmount, Round(value));
        }

        [DefaultValue(0.5)]
        public double Desaturation
        {
            get => _desaturation;
            set => SetProperty(ref _desaturation, Round(value));
        }
    }
}
