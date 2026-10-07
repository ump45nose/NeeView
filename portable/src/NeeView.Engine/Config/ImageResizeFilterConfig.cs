// Copyright (c) NeeLaboratory. MIT; adapted from original ImageResizeFilterConfig.
using System.Text.Json;
using System.Text.Json.Serialization;
using CommunityToolkit.Mvvm.ComponentModel;
namespace NeeView;
/// <summary>原滤镜配置及不可变解码快照；JSON保留未迁字段。</summary>
public sealed class ImageResizeFilterConfig : ObservableObject
{
    private bool _isEnabled;
    private ResizeInterpolation _resizeInterpolation = ResizeInterpolation.Lanczos;
    private bool _isUnsharpMaskEnabled = true;
    private UnsharpMaskConfig _unsharpMask = new();
    public bool IsEnabled { get => _isEnabled; set => SetProperty(ref _isEnabled, value); }
    public ResizeInterpolation ResizeInterpolation { get => _resizeInterpolation; set => SetProperty(ref _resizeInterpolation, value); }
    public bool IsUnsharpMaskEnabled { get => _isUnsharpMaskEnabled; set => SetProperty(ref _isUnsharpMaskEnabled, value); }
    public ImageResizeFilterConfig() { _unsharpMask.PropertyChanged += MaskChanged; }
    public UnsharpMaskConfig UnsharpMask
    {
        get => _unsharpMask;
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            if (ReferenceEquals(value, _unsharpMask)) return;
            _unsharpMask.PropertyChanged -= MaskChanged;
            _unsharpMask = value; _unsharpMask.PropertyChanged += MaskChanged; OnPropertyChanged();
        }
    }
    private void MaskChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs args) => OnPropertyChanged(nameof(UnsharpMask));
    [JsonExtensionData] [PropertyMapIgnore] public Dictionary<string, JsonElement>? ExtensionData { get; set; }
    /// <summary>后台工作只消费当前值快照；沿原byte阈值转换，不按表单范围改写导入值。</summary>
    public ImageResizeFilterParameters? CreateParameters() => IsEnabled ? new(ResizeInterpolation, IsUnsharpMaskEnabled, UnsharpMask.Amount, UnsharpMask.Radius, unchecked((byte)UnsharpMask.Threshold)) : null;
}
