// Copyright (c) NeeLaboratory. Original ColorizeControlPoint; value notification adapted for retained drawing.
using CommunityToolkit.Mvvm.ComponentModel;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace NeeView.Effects;

[JsonConverter(typeof(JsonColorizeControlPointConverter))]
public sealed class ColorizeControlPoint : ObservableObject
{
    public ColorizeControlPoint() : this(ThemeRgba.FromArgb(255,0,0,0), 1) { }
    public ColorizeControlPoint(ThemeRgba color, double strength) { Color=color; Strength=strength; }
    public ColorizeControlPoint(ColorizeControlPoint other) : this(other.Color, other.Strength) { }
    private ThemeRgba _color;
    private double _strength;
    public ThemeRgba Color { get => _color; set => SetProperty(ref _color, value); }
    public double Strength { get => _strength; set => SetProperty(ref _strength, value); }
    public bool ValueEquals(ColorizeControlPoint other) => Color == other.Color && Strength == other.Strength;
}
public sealed class JsonColorizeControlPointConverter : JsonConverter<ColorizeControlPoint>
{
    public override ColorizeControlPoint Read(ref Utf8JsonReader reader, Type type, JsonSerializerOptions options)
    {
        var s=reader.GetString() ?? throw new JsonException("Invalid ColorizeControlPoint format"); var p=s.Split(',');
        if(p.Length!=2) throw new JsonException("Invalid ColorizeControlPoint format");
        return new(ThemeRgba.Parse(p[0]), double.Parse(p[1], CultureInfo.InvariantCulture));
    }
    public override void Write(Utf8JsonWriter writer, ColorizeControlPoint value, JsonSerializerOptions options) => writer.WriteStringValue(value.Color + "," + value.Strength.ToString(CultureInfo.InvariantCulture));
}
