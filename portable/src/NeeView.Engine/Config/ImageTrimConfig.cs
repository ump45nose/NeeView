// Copyright (c) NeeLaboratory. 原 ImageTrimConfig；保留对边联动及五位舍入。
using CommunityToolkit.Mvvm.ComponentModel;
using NeeLaboratory;
namespace NeeView;
public sealed class ImageTrimConfig : ObservableObject
{
    private const double MaxRate = .9;
    private bool _isEnabled; private double _top, _bottom, _left, _right;
    public bool IsEnabled { get => _isEnabled; set => SetProperty(ref _isEnabled, value); }
    public double Left { get => _left; set => SetSide(ref _left, value, ref _right, nameof(Right)); }
    public double Right { get => _right; set => SetSide(ref _right, value, ref _left, nameof(Left)); }
    public double Top { get => _top; set => SetSide(ref _top, value, ref _bottom, nameof(Bottom)); }
    public double Bottom { get => _bottom; set => SetSide(ref _bottom, value, ref _top, nameof(Top)); }
    private void SetSide(ref double field, double value, ref double opposite, string oppositeName, [System.Runtime.CompilerServices.CallerMemberName] string? name = null)
    {
        if (!SetProperty(ref field, Math.Round(MathUtility.Clamp(value, 0, MaxRate), 5), name)) return;
        if (field + opposite > MaxRate) { opposite = Math.Round(MaxRate - field, 5); OnPropertyChanged(oppositeName); }
    }
    [System.Text.Json.Serialization.JsonExtensionData] [PropertyMapIgnore] public Dictionary<string, System.Text.Json.JsonElement>? ExtensionData { get; set; }
}
