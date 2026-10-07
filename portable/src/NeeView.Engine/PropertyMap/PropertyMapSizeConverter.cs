// Copyright (c) NeeLaboratory.
using System.Globalization;
namespace NeeView;
/// <summary>原尺寸字符串契约，替换实际Size值，不模拟WPF转换器。</summary>
public sealed class PropertyMapSizeConverter : PropertyMapConverter<Size>
{
    public override string GetTypeName(Type typeToConvert) => "\"width,height\"";
    public override object? Read(PropertyMapSource source, Type typeToConvert, PropertyMapOptions options) => source.GetValue() is Size size ? string.Create(CultureInfo.CurrentCulture, $"{size.Width},{size.Height}") : null;
    public override void Write(PropertyMapSource source, object? value, PropertyMapOptions options)
    {
        if (value is null) throw new NotSupportedException("Cannot convert from null");
        if (value is Size size) { source.SetValue(size); return; }
        if (value is not string text) throw new NotSupportedException("Size accepts a width,height string.");
        if (text == "Empty") { source.SetValue(default(Size)); return; }
        var parts = text.Split(',');
        if (parts.Length != 2) throw new FormatException("Size requires width,height.");
        double width = double.Parse(parts[0], CultureInfo.CurrentCulture), height = double.Parse(parts[1], CultureInfo.CurrentCulture);
        if (width < 0 || height < 0) throw new ArgumentException("Size dimensions cannot be negative.");
        source.SetValue(new Size(width, height));
    }
}
