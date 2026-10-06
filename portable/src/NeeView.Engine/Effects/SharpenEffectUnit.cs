// Copyright (c) NeeLaboratory. MIT; migrated original effect parameter source.
using System.ComponentModel;

namespace NeeView.Effects
{
    /// <summary>原效果参数和通知；WPF Adapter 在 Mac 绘制边界替换。</summary>
    public partial class SharpenEffectUnit : EffectUnit
    {
        private double _amount = 2.0;
        private double _height = 0.5;


        [DefaultValue(2.0)]
        public double Amount
        {
            get => _amount;
            set => SetProperty(ref _amount, Round(value));
        }

        [DefaultValue(0.5)]
        public double Height
        {
            get => _height;
            set => SetProperty(ref _height, Round(value));
        }
    }
}
