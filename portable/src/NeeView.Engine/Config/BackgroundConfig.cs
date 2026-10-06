// Copyright (c) NeeLaboratory. 原 BackgroundConfig/BrushSource/BackgroundType，MIT 许可。
using CommunityToolkit.Mvvm.ComponentModel;
using System.Text.Json;
using System.Text.Json.Serialization;
namespace NeeView;

/// <summary>原六种画布背景；数值和循环顺序保持，不能与透明页背景合并。</summary>
public enum BackgroundType { Black, White, Auto, Check, CheckDark, Custom }
public static class BackgroundTypeExtension
{
    /// <summary>沿原六种背景顺序循环。</summary>
    public static BackgroundType GetToggle(this BackgroundType value) => (BackgroundType)(((int)value + 1) % 6);
}
/// <summary>原自定义刷五种模式，实际刷和 Bitmap 归表现端。</summary>
public enum BrushType { SolidColor, ImageTile, ImageFill, ImageUniform, ImageUniformToFill }
public sealed class BrushSource : ObservableObject
{
    private BrushType _type;
    private ThemeRgba _color = ThemeRgba.Parse("LightGray");
    private string? _file;
    public BrushType Type { get => _type; set => SetProperty(ref _type, value); }
    [JsonConverter(typeof(BackgroundColorConverter))]
    public ThemeRgba Color { get => _color; set => SetProperty(ref _color, value); }
    public string? ImageFileName { get => _file; set => SetProperty(ref _file, value); }
}
/// <summary>原画布/自定义/透明页四字段；JSON 颜色仍为原 #AARRGGBB 字符串。</summary>
public sealed class BackgroundConfig : ObservableObject
{
    private BackgroundType _type;
    private BrushSource _custom = new();
    private ThemeRgba _page = ThemeRgba.Parse("Transparent");
    private bool _checker;
    public BackgroundType BackgroundType { get => _type; set => SetProperty(ref _type, value); }
    public BrushSource CustomBackground { get => _custom; set => SetProperty(ref _custom, value ?? new()); }
    [JsonConverter(typeof(BackgroundColorConverter))]
    public ThemeRgba PageBackgroundColor { get => _page; set => SetProperty(ref _page, value); }
    public bool IsPageBackgroundChecker { get => _checker; set => SetProperty(ref _checker, value); }
}
/// <summary>只替换 WPF Color 值，保留原命名/scRGB/Alpha 输入与原字符串输出。</summary>
public sealed class BackgroundColorConverter : JsonConverter<ThemeRgba>
{
    public override ThemeRgba Read(ref Utf8JsonReader reader, Type type, JsonSerializerOptions options) => ThemeRgba.Parse(reader.GetString() ?? "Transparent");
    public override void Write(Utf8JsonWriter writer, ThemeRgba value, JsonSerializerOptions options) => writer.WriteStringValue(value.ToString());
}
