// Copyright (c) NeeLaboratory. MIT; migrated original effect parameter source.
using System;
using System.ComponentModel;
using System.Text.Json.Serialization;

namespace NeeView.Effects
{
    /// <summary>原效果参数和通知；WPF Adapter 在 Mac 绘制边界替换。</summary>
    public partial class LevelEffectUnit : EffectUnit
    {
        private double _black = 0.0;
        private double _white = 1.0;
        private double _center = 0.5;
        private double _minimum = 0.0;
        private double _maximum = 1.0;

        public LevelEffectUnit() : base(EffectSampleType.Luminance)
        {
        }

        [DefaultValue(0.0)]
        [JsonIgnore]
        public double Black
        {
            get => _black;
            set
            {
                var centerRate = _white - _black != 0 ? (_center - _black) / (_white - _black) : 0.5;
                if (SetProperty(ref _black, Round(value)))
                {
                    Center = _black + centerRate * (_white - _black);
                }
            }
        }

        [JsonPropertyName(nameof(Black))]
        public double BlackRaw
        {
            get => _black;
            set => _black = value;
        }

        [DefaultValue(1.0)]
        [JsonIgnore]
        public double White
        {
            get => _white;
            set
            {
                var centerRate = _white - _black != 0 ? (_center - _black) / (_white - _black) : 0.5;
                if (SetProperty(ref _white, Round(value)))
                {
                    Center = _black + centerRate * (_white - _black);
                }
            }
        }

        [JsonPropertyName(nameof(White))]
        public double WhiteRaw
        {
            get => _white;
            set => _white = value;
        }

        [DefaultValue(0.5)]
        public double Center
        {
            get => _center;
            set => SetProperty(ref _center, Round(value));
        }

        [DefaultValue(0.0)]
        public double Minimum
        {
            get => _minimum;
            set => SetProperty(ref _minimum, Round(value));
        }

        [DefaultValue(1.0)]
        public double Maximum
        {
            get => _maximum;
            set => SetProperty(ref _maximum, Round(value));
        }
    }
}
