// Copyright (c) NeeLaboratory. MIT; migrated original effect parameter source.
using System.ComponentModel;

namespace NeeView.Effects
{
    /// <summary>原效果参数和通知；WPF Adapter 在 Mac 绘制边界替换。</summary>
    public partial class PixelateEffectUnit : EffectUnit
    {
        private double _pixelation = 0.75;

        [DefaultValue(0.75)]
        public double Pixelation
        {
            get => _pixelation;
            set => SetProperty(ref _pixelation, Round(value));
        }
    }
}
