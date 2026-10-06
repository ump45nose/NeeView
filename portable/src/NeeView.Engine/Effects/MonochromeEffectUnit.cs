// Copyright (c) NeeLaboratory. MIT; migrated original effect parameter source.
using System.ComponentModel;

namespace NeeView.Effects
{
    /// <summary>原效果参数和通知；WPF Adapter 在 Mac 绘制边界替换。</summary>
    public partial class MonochromeEffectUnit : EffectUnit
    {
        private ThemeRgba _color = ThemeRgba.FromArgb(0xFF, 0xFF, 0xFF, 0xFF);

        [DefaultValue(typeof(ThemeRgba), "#FFFFFFFF")]
        public ThemeRgba Color
        {
            get => _color;
            set => SetProperty(ref _color, value);
        }
    }
}
