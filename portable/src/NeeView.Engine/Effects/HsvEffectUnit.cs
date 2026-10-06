// Copyright (c) NeeLaboratory. MIT; migrated original effect parameter source.
using System.ComponentModel;

namespace NeeView.Effects
{
    /// <summary>原效果参数和通知；WPF Adapter 在 Mac 绘制边界替换。</summary>
    public partial class HsvEffectUnit : EffectUnit
    {
        private double _hue;
        private double _saturation;
        private double _value;

        public HsvEffectUnit() : base(EffectSampleType.Tone)
        {
        }

        [DefaultValue(0.0)]
        public double Hue
        {
            get => _hue;
            set => SetProperty(ref _hue, Round(value));
        }

        [DefaultValue(0.0)]
        public double Saturation
        {
            get => _saturation;
            set => SetProperty(ref _saturation, Round(value));
        }

        [DefaultValue(0.0)]
        public double Value
        {
            get => _value;
            set => SetProperty(ref _value, Round(value));
        }
    }
}
