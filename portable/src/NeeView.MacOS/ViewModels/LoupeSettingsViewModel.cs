using CommunityToolkit.Mvvm.ComponentModel;
using NeeView;
namespace NeeView.MacOS.ViewModels;

/// <summary>原 Loupe 十二字段的独立表单草稿；范围包含旧值，未编辑的大数原样保留。</summary>
public sealed class LoupeSettingsViewModel : ObservableObject
{
    private readonly Dictionary<string,double> _original = [];
    private readonly HashSet<string> _edited = [];
    private static decimal EditorValue(double value) => !double.IsFinite(value) ? 0 : value >= (double)decimal.MaxValue ? decimal.MaxValue : value <= (double)decimal.MinValue ? decimal.MinValue : (decimal)value;
    public LoupeSettingsViewModel(LoupeConfig config)
    {
        _original[nameof(DefaultScale)] = config.DefaultScale; _DefaultScale = EditorValue(config.DefaultScale);
        _original[nameof(MinimumScale)] = config.MinimumScale; _MinimumScale = EditorValue(config.MinimumScale);
        _original[nameof(MaximumScale)] = config.MaximumScale; _MaximumScale = EditorValue(config.MaximumScale);
        _original[nameof(ScaleStep)] = config.ScaleStep; _ScaleStep = EditorValue(config.ScaleStep);
        _original[nameof(Speed)] = config.Speed; _Speed = EditorValue(config.Speed);
        _IsLoupeCenter = config.IsLoupeCenter;
        _IsResetByRestart = config.IsResetByRestart;
        _IsResetByPageChanged = config.IsResetByPageChanged;
        _IsVisibleLoupeInfo = config.IsVisibleLoupeInfo;
        _IsWheelScalingEnabled = config.IsWheelScalingEnabled;
        _IsEscapeKeyEnabled = config.IsEscapeKeyEnabled;
        _IsBaseOnOriginal = config.IsBaseOnOriginal;
    }
    private decimal _DefaultScale;
    public decimal DefaultScale { get => _DefaultScale; set { if (SetProperty(ref _DefaultScale,value)) _edited.Add(nameof(DefaultScale)); } }
    public decimal DefaultScaleMinimum => Math.Min(1, DefaultScale);
    public decimal DefaultScaleMaximum => Math.Max(10, DefaultScale);
    private decimal _MinimumScale;
    public decimal MinimumScale { get => _MinimumScale; set { if (SetProperty(ref _MinimumScale,value)) _edited.Add(nameof(MinimumScale)); } }
    public decimal MinimumScaleMinimum => Math.Min(1, MinimumScale);
    public decimal MinimumScaleMaximum => Math.Max(10, MinimumScale);
    private decimal _MaximumScale;
    public decimal MaximumScale { get => _MaximumScale; set { if (SetProperty(ref _MaximumScale,value)) _edited.Add(nameof(MaximumScale)); } }
    public decimal MaximumScaleMinimum => Math.Min(1, MaximumScale);
    public decimal MaximumScaleMaximum => Math.Max(10, MaximumScale);
    private decimal _ScaleStep;
    public decimal ScaleStep { get => _ScaleStep; set { if (SetProperty(ref _ScaleStep,value)) _edited.Add(nameof(ScaleStep)); } }
    public decimal ScaleStepMinimum => Math.Min(0, ScaleStep);
    public decimal ScaleStepMaximum => Math.Max(5, ScaleStep);
    private decimal _Speed;
    public decimal Speed { get => _Speed; set { if (SetProperty(ref _Speed,value)) _edited.Add(nameof(Speed)); } }
    public decimal SpeedMinimum => Math.Min(0, Speed);
    public decimal SpeedMaximum => Math.Max(10, Speed);
    private bool _IsLoupeCenter;
    public bool IsLoupeCenter { get => _IsLoupeCenter; set => SetProperty(ref _IsLoupeCenter,value); }
    private bool _IsResetByRestart;
    public bool IsResetByRestart { get => _IsResetByRestart; set => SetProperty(ref _IsResetByRestart,value); }
    private bool _IsResetByPageChanged;
    public bool IsResetByPageChanged { get => _IsResetByPageChanged; set => SetProperty(ref _IsResetByPageChanged,value); }
    private bool _IsVisibleLoupeInfo;
    public bool IsVisibleLoupeInfo { get => _IsVisibleLoupeInfo; set => SetProperty(ref _IsVisibleLoupeInfo,value); }
    private bool _IsWheelScalingEnabled;
    public bool IsWheelScalingEnabled { get => _IsWheelScalingEnabled; set => SetProperty(ref _IsWheelScalingEnabled,value); }
    private bool _IsEscapeKeyEnabled;
    public bool IsEscapeKeyEnabled { get => _IsEscapeKeyEnabled; set => SetProperty(ref _IsEscapeKeyEnabled,value); }
    private bool _IsBaseOnOriginal;
    public bool IsBaseOnOriginal { get => _IsBaseOnOriginal; set => SetProperty(ref _IsBaseOnOriginal,value); }
    /// <summary>仅在唯一设置事务内写原分支，失败由 BookOperation 原地回滚。</summary>
    public void Apply(LoupeConfig config)
    {
        config.DefaultScale = _edited.Contains(nameof(DefaultScale)) ? (double)DefaultScale : _original[nameof(DefaultScale)];
        config.MinimumScale = _edited.Contains(nameof(MinimumScale)) ? (double)MinimumScale : _original[nameof(MinimumScale)];
        config.MaximumScale = _edited.Contains(nameof(MaximumScale)) ? (double)MaximumScale : _original[nameof(MaximumScale)];
        config.ScaleStep = _edited.Contains(nameof(ScaleStep)) ? (double)ScaleStep : _original[nameof(ScaleStep)];
        config.Speed = _edited.Contains(nameof(Speed)) ? (double)Speed : _original[nameof(Speed)];
        config.IsLoupeCenter = IsLoupeCenter;
        config.IsResetByRestart = IsResetByRestart;
        config.IsResetByPageChanged = IsResetByPageChanged;
        config.IsVisibleLoupeInfo = IsVisibleLoupeInfo;
        config.IsWheelScalingEnabled = IsWheelScalingEnabled;
        config.IsEscapeKeyEnabled = IsEscapeKeyEnabled;
        config.IsBaseOnOriginal = IsBaseOnOriginal;
    }
}
