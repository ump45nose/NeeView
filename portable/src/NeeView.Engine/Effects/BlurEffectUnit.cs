// Copyright (c) NeeLaboratory. MIT; migrated original effect parameter source.
using System.ComponentModel;

namespace NeeView.Effects
{
    /// <summary>原效果参数和通知；WPF Adapter 在 Mac 绘制边界替换。</summary>
    public partial class BlurEffectUnit : EffectUnit
    {
        private double _radius = 5.0;

        [DefaultValue(5.0)]
        public double Radius
        {
            get => _radius;
            set => SetProperty(ref _radius, Round(value));
        }
    }
}
