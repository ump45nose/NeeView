// Copyright (c) NeeLaboratory. 原 ImageGridConfig；范围约束属于 UI 元数据，业务值保持原样。
using CommunityToolkit.Mvvm.ComponentModel;
namespace NeeView;
public sealed class ImageGridConfig : ObservableObject
{
    private bool _isEnabled; private ThemeRgba _color = ThemeRgba.FromArgb(0x80, 0x80, 0x80, 0x80); private int _divX = 8, _divY = 8; private bool _isSquare; private ImageGridTarget _target;
    public bool IsEnabled { get => _isEnabled; set => SetProperty(ref _isEnabled, value); }
    public ImageGridTarget Target { get => _target; set => SetProperty(ref _target, value); }
    public ThemeRgba Color { get => _color; set => SetProperty(ref _color, value); }
    public int DivX { get => _divX; set => SetProperty(ref _divX, value); }
    public int DivY { get => _divY; set => SetProperty(ref _divY, value); }
    public bool IsSquare { get => _isSquare; set => SetProperty(ref _isSquare, value); }
    [System.Text.Json.Serialization.JsonExtensionData] public Dictionary<string, System.Text.Json.JsonElement>? ExtensionData { get; set; }
}
public enum ImageGridTarget { Image, Screen }
