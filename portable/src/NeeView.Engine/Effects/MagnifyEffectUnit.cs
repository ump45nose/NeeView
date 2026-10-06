// Copyright (c) NeeLaboratory. MIT; migrated original effect parameter source.
using System.ComponentModel;

namespace NeeView.Effects
{
    /// <summary>原效果参数和通知；WPF Adapter 在 Mac 绘制边界替换。</summary>
    public partial class MagnifyEffectUnit : EffectUnit
    {
        private EffectPoint _center = new(0.5, 0.5);
        private double _amount = 0.5;
        private double _innerRadius = 0.2;
        private double _outerRadius = 0.4;


        [DefaultValue(typeof(EffectPoint), "0.5,0.5")]
        public EffectPoint Center
        {
            get => _center;
            set => SetProperty(ref _center, Round(value));
        }

        [DefaultValue(0.5)]
        public double Amount
        {
            get => _amount;
            set => SetProperty(ref _amount, Round(value));
        }

        [DefaultValue(0.2)]
        public double InnerRadius
        {
            get => _innerRadius;
            set => SetProperty(ref _innerRadius, Round(value));
        }

        [DefaultValue(0.4)]
        public double OuterRadius
        {
            get => _outerRadius;
            set => SetProperty(ref _outerRadius, Round(value));
        }
    }
}
