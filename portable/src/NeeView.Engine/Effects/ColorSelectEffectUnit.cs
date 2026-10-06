// Copyright (c) NeeLaboratory. MIT; migrated original effect parameter source.
using System.ComponentModel;

namespace NeeView.Effects
{
    /// <summary>原效果参数和通知；WPF Adapter 在 Mac 绘制边界替换。</summary>
    public partial class ColorSelectEffectUnit : EffectUnit
    {
        private double _hue = 15.0;
        private double _range = 0.1;
        private double _curve = 0.1;

        public ColorSelectEffectUnit() : base(EffectSampleType.Tone)
        {
        }

        [DefaultValue(15.0)]
        public double Hue
        {
            get => _hue;
            set => SetProperty(ref _hue, Round(value));
        }

        [DefaultValue(0.1)]
        public double Range
        {
            get => _range;
            set => SetProperty(ref _range, Round(value));
        }

        [DefaultValue(0.1)]
        public double Curve
        {
            get => _curve;
            set => SetProperty(ref _curve, Round(value));
        }
    }
}
