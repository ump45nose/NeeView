// Copyright (c) NeeLaboratory. Adapted from NeeView/Config/LoupeConfig.cs.
namespace NeeView;

/// <summary>原 LoupeConfig；属性名和默认值保持 UserSetting.json 兼容。</summary>
public sealed class LoupeConfig
{
    private double _defaultScale = 2, _minimumScale = 1, _maximumScale = 10, _scaleStep = 1, _speed = 1;
    public double DefaultScale { get => _defaultScale; set => _defaultScale = Round(value); }
    public bool IsLoupeCenter { get; set; }
    public double MinimumScale { get => _minimumScale; set => _minimumScale = Round(value); }
    public double MaximumScale { get => _maximumScale; set => _maximumScale = Round(value); }
    public double ScaleStep { get => _scaleStep; set => _scaleStep = Round(Math.Max(value, 0)); }
    public bool IsResetByRestart { get; set; }
    public bool IsResetByPageChanged { get; set; } = true;
    public bool IsVisibleLoupeInfo { get; set; } = true;
    public bool IsWheelScalingEnabled { get; set; } = true;
    public double Speed { get => _speed; set => _speed = Round(value); }
    public bool IsEscapeKeyEnabled { get; set; } = true;
    public bool IsBaseOnOriginal { get; set; }
    // Original AppMath.Round; editor ranges must not clip imported values.
    private static double Round(double value) => Math.Round(value, 5);
}
