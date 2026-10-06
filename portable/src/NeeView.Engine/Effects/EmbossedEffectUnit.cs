// Copyright (c) NeeLaboratory. MIT; migrated original effect parameter source.
using System.ComponentModel;

namespace NeeView.Effects
{
    /// <summary>原效果参数和通知；WPF Adapter 在 Mac 绘制边界替换。</summary>
    public partial class EmbossedEffectUnit : EffectUnit
    {
        private ThemeRgba _color = ThemeRgba.FromArgb(0xFF, 0x80, 0x80, 0x80);
        private double _amount = 3.0;
        private double _height = 1.0;

        [DefaultValue(typeof(ThemeRgba), "#FF808080")]
        public ThemeRgba Color
        {
            get => _color;
            set => SetProperty(ref _color, value);
        }

        [DefaultValue(3)]
        public double Amount
        {
            get => _amount;
            set => SetProperty(ref _amount, Round(value));
        }

        [DefaultValue(1)]
        public double Height
        {
            get => _height;
            set => SetProperty(ref _height, Round(value));
        }
    }
}
