// Copyright (c) NeeLaboratory.
namespace NeeView;
/// <summary>原颜色字符串契约，使用已迁入的纯ThemeRgba。</summary>
public sealed class PropertyMapColorConverter : PropertyMapConverter<ThemeRgba>
{
    public override string GetTypeName(Type typeToConvert) => "\"#AARRGGBB\"";
    public override object? Read(PropertyMapSource source, Type typeToConvert, PropertyMapOptions options) => source.GetValue()?.ToString();
    public override void Write(PropertyMapSource source, object? value, PropertyMapOptions options)
    {
        if (value is null) throw new NotSupportedException("Cannot convert from null");
        source.SetValue(value is ThemeRgba color ? color : value is string text ? ThemeRgba.Parse(text) : throw new NotSupportedException("Color accepts a string."));
    }
}
