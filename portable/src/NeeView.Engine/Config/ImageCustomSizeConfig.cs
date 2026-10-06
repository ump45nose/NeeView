// Copyright (c) NeeLaboratory. 原 ImageCustomSizeConfig；移除 WPF/UI 属性依赖。
using CommunityToolkit.Mvvm.ComponentModel;
using NeeLaboratory;
using System.Text.Json.Serialization;

namespace NeeView;

public sealed class ImageCustomSizeConfig : ObservableObject
{
    private bool _isEnabled;
    private Size _size = new(256, 256);
    private CustomSizeAspectRatio _aspectRatio;
    private double _applicabilityRate = 1.0;
    private bool _isAlignLongSide;
    public bool IsEnabled { get => _isEnabled; set => SetProperty(ref _isEnabled, value); }
    [JsonConverter(typeof(JsonSizeConverter))]
    public Size Size { get => _size; set { if (SetProperty(ref _size, value)) { OnPropertyChanged(nameof(Width)); OnPropertyChanged(nameof(Height)); } } }
    [JsonIgnore] public int Width { get => (int)_size.Width; set { if (value != _size.Width) Size = new(value, _size.Height); } }
    [JsonIgnore] public int Height { get => (int)_size.Height; set { if (value != _size.Height) Size = new(_size.Width, value); } }
    public CustomSizeAspectRatio AspectRatio { get => _aspectRatio; set => SetProperty(ref _aspectRatio, value); }
    public double ApplicabilityRate { get => _applicabilityRate; set => SetProperty(ref _applicabilityRate, Math.Round(MathUtility.Clamp(value, 0.0, 1.0), 5)); }
    public bool IsAlignLongSide { get => _isAlignLongSide; set => SetProperty(ref _isAlignLongSide, value); }
    // Legacy IsUniformed is read-only compatibility input and is never written.
    [Obsolete("no used"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool IsUniformed { get => false; set => AspectRatio = value ? CustomSizeAspectRatio.Origin : CustomSizeAspectRatio.None; }
    [System.Text.Json.Serialization.JsonExtensionData] public Dictionary<string, System.Text.Json.JsonElement>? ExtensionData { get; set; }
}

public enum CustomSizeAspectRatio { None, Origin, Ratio_1_1, Ratio_2_3, Ratio_4_3, Ratio_8_9, Ratio_16_9, HalfView, View }
