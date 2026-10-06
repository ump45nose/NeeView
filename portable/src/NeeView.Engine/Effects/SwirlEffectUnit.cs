// Copyright (c) NeeLaboratory. MIT; migrated original effect parameter source.
using System.ComponentModel;

namespace NeeView.Effects
{
    /// <summary>原效果参数和通知；WPF Adapter 在 Mac 绘制边界替换。</summary>
    public partial class SwirlEffectUnit : EffectUnit
    {
        private EffectPoint _center = new(0.5, 0.5);
        private double _twistAmount = 10.0;

        [DefaultValue(typeof(EffectPoint), "0.5,0.5")]
        public EffectPoint Center
        {
            get => _center;
            set => SetProperty(ref _center, Round(value));
        }

        [DefaultValue(10)]
        public double TwistAmount
        {
            get => _twistAmount;
            set => SetProperty(ref _twistAmount, Round(value));
        }
    }
}
