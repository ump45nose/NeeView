// Copyright (c) NeeLaboratory. MIT; adapted from original UnsharpMaskConfig.
using CommunityToolkit.Mvvm.ComponentModel;
using System.Text.Json;
using System.Text.Json.Serialization;
namespace NeeView;
/// <summary>原锐化参数默认、五位精度和通知；范围只属于编辑器。</summary>
public sealed class UnsharpMaskConfig : ObservableObject
{
    private int _amount = 40, _threshold;
    private double _radius = 1.5;
    public int Amount { get => _amount; set => SetProperty(ref _amount, value); }
    public double Radius { get => _radius; set => SetProperty(ref _radius, Math.Round(value, 5)); }
    public int Threshold { get => _threshold; set => SetProperty(ref _threshold, value); }
    [JsonExtensionData] public Dictionary<string, JsonElement>? ExtensionData { get; set; }
}
