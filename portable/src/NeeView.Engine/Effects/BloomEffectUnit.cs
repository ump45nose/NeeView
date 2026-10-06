// Copyright (c) NeeLaboratory. MIT; migrated original effect parameter source.
using System;
using System.ComponentModel;

namespace NeeView.Effects
{
    /// <summary>原效果参数和通知；WPF Adapter 在 Mac 绘制边界替换。</summary>
    public partial class BloomEffectUnit : EffectUnit
    {
        private double _baseIntensity = 1.0;
        private double _baseSaturation = 1.0;
        private double _bloomIntensity = 1.25;
        private double _bloomSaturation = 1.0;
        private double _threshold = 0.25;

        [DefaultValue(1.0)]
        public double BaseIntensity
        {
            get => _baseIntensity;
            set => SetProperty(ref _baseIntensity, Round(value));
        }

        [DefaultValue(1.0)]
        public double BaseSaturation
        {
            get => _baseSaturation;
            set => SetProperty(ref _baseSaturation, Round(value));
        }

        [DefaultValue(1.25)]
        public double BloomIntensity
        {
            get => _bloomIntensity;
            set => SetProperty(ref _bloomIntensity, Round(value));
        }

        [DefaultValue(1.0)]
        public double BloomSaturation
        {
            get => _bloomSaturation;
            set => SetProperty(ref _bloomSaturation, Round(value));
        }

        [DefaultValue(0.25)]
        public double Threshold
        {
            get => _threshold;
            set => SetProperty(ref _threshold, Round(Math.Min(value, 1.0)));
        }
    }
}
