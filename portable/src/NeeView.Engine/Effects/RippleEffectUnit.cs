// Copyright (c) NeeLaboratory. MIT; migrated original effect parameter source.
using System.ComponentModel;

namespace NeeView.Effects
{
    /// <summary>原效果参数和通知；WPF Adapter 在 Mac 绘制边界替换。</summary>
    public partial class RippleEffectUnit : EffectUnit
    {
        private EffectPoint _center = new(0.5, 0.5);
        private double _frequency = 40.0;
        private double _magnitude = 0.1;
        private double _phase = 10.0;

        [DefaultValue(typeof(EffectPoint), "0.5,0.5")]
        public EffectPoint Center
        {
            get => _center;
            set => SetProperty(ref _center, Round(value));
        }

        [DefaultValue(40)]
        public double Frequency
        {
            get => _frequency;
            set => SetProperty(ref _frequency, Round(value));
        }

        [DefaultValue(0.1)]
        public double Magnitude
        {
            get => _magnitude;
            set => SetProperty(ref _magnitude, Round(value));
        }

        [DefaultValue(10)]
        public double Phase
        {
            get => _phase;
            set => SetProperty(ref _phase, Round(value));
        }
    }
}
