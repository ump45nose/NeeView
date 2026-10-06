// Copyright (c) NeeLaboratory. MIT; original EffectUnit, replacing generated equality and WPF values.
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using CommunityToolkit.Mvvm.ComponentModel;
namespace NeeView.Effects;

/// <summary>原效果参数基类；序列化仅包含业务值，比较和克隆不包含显示资源。</summary>
[JsonConverter(typeof(JsonEffectUnitConverter))]
public abstract class EffectUnit : ObservableObject
{
    protected EffectUnit(EffectSampleType sampleType = EffectSampleType.None) => SampleType = sampleType;
    [JsonIgnore] public EffectSampleType SampleType { get; }
    [JsonExtensionData] public Dictionary<string, JsonElement>? ExtensionData { get; set; }
    [JsonIgnore] public bool IsDefault => this is not UnknownEffectUnit && JsonNode.DeepEquals(Data(this), Data(CreateInstance(GetType())));
    /// <summary>按具体类型克隆原参数，未知类型原样保留；不会共享可修改的参数集合。</summary>
    public EffectUnit Clone() => this is UnknownEffectUnit unknown ? new UnknownEffectUnit { TypeName = unknown.TypeName,
        ExtensionData = unknown.ExtensionData?.ToDictionary(x => x.Key, x => x.Value.Clone()) } :
        (EffectUnit)JsonSerializer.Deserialize(Data(this), GetType(), JsonEffectUnitConverter.PlainOptions)!;
    public bool ValueEquals(EffectUnit? other) => other is not null && GetType() == other.GetType() &&
        (this is not UnknownEffectUnit a || other is UnknownEffectUnit b && a.TypeName == b.TypeName) && JsonNode.DeepEquals(Data(this), Data(other));
    public static EffectUnit CreateInstance(Type type) => (EffectUnit)Activator.CreateInstance(type)!;
    internal static JsonObject Data(EffectUnit value) => JsonSerializer.SerializeToNode(value, value.GetType(), JsonEffectUnitConverter.PlainOptions)!.AsObject();
    public void RaisePropertyChangedAll() => OnPropertyChanged("");
    protected static double Round(double value) => Math.Round(value, 5, MidpointRounding.ToEven);
    protected static EffectPoint Round(EffectPoint value) => new(Round(value.X), Round(value.Y));
}

/// <summary>原短 $type 多态格式；未知效果及未来字段保持而不解释成 None。</summary>
public sealed class JsonEffectUnitConverter : JsonConverter<EffectUnit>
{
    internal static readonly JsonSerializerOptions PlainOptions = new() { Converters = { new JsonStringEnumConverter() } };
    public static readonly IReadOnlyDictionary<string, Type> Types = new Dictionary<string, Type>(StringComparer.Ordinal)
    {
        ["Level"] = typeof(LevelEffectUnit), ["Hsv"] = typeof(HsvEffectUnit), ["ColorSelect"] = typeof(ColorSelectEffectUnit),
        ["Colorize"] = typeof(ColorizeEffectUnit), ["Blur"] = typeof(BlurEffectUnit), ["Bloom"] = typeof(BloomEffectUnit),
        ["Monochrome"] = typeof(MonochromeEffectUnit), ["ColorTone"] = typeof(ColorToneEffectUnit), ["Sharpen"] = typeof(SharpenEffectUnit),
        ["Embossed"] = typeof(EmbossedEffectUnit), ["Pixelate"] = typeof(PixelateEffectUnit), ["Magnify"] = typeof(MagnifyEffectUnit),
        ["Ripple"] = typeof(RippleEffectUnit), ["Swirl"] = typeof(SwirlEffectUnit)
    };
    public override EffectUnit Read(ref Utf8JsonReader reader, Type type, JsonSerializerOptions options)
    {
        var raw = JsonNode.Parse(ref reader)?.AsObject() ?? throw new JsonException("效果需要对象。");
        string? name = raw["$type"]?.GetValue<string>(); raw.Remove("$type");
        var result = name is not null && Types.TryGetValue(name, out var known) ?
            (EffectUnit)raw.Deserialize(known, PlainOptions)! : raw.Deserialize<UnknownEffectUnit>(PlainOptions)!;
        if (result is UnknownEffectUnit unknown) unknown.TypeName = name;
        return result;
    }
    public override void Write(Utf8JsonWriter writer, EffectUnit value, JsonSerializerOptions options)
    {
        writer.WriteStartObject(); writer.WriteString("$type", value is UnknownEffectUnit u ? u.TypeName : value.GetType().Name[..^"EffectUnit".Length]);
        var defaults = value is UnknownEffectUnit ? null : EffectUnit.Data(EffectUnit.CreateInstance(value.GetType()));
        foreach (var field in EffectUnit.Data(value))
        {
            // 原参数默认差分：未知类型/扩展字段不能因缺少默认定义而被抹去。
            if (defaults is not null && defaults.TryGetPropertyValue(field.Key, out var initial) && JsonNode.DeepEquals(initial, field.Value)) continue;
            writer.WritePropertyName(field.Key); if (field.Value is null) writer.WriteNullValue(); else field.Value.WriteTo(writer, options);
        }
        writer.WriteEndObject();
    }
}
/// <summary>未来效果材料；禁止把未知算法伪装成已经执行。</summary>
public sealed class UnknownEffectUnit : EffectUnit { [JsonIgnore] public string? TypeName { get; set; } }

[JsonConverter(typeof(EffectPointConverter))]
public readonly record struct EffectPoint(double X, double Y);
public sealed class EffectPointConverter : JsonConverter<EffectPoint>
{
    public override EffectPoint Read(ref Utf8JsonReader reader, Type type, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.String)
        {
            var parts = reader.GetString()!.Split(',');
            if (parts.Length == 2 && double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var x) && double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var y) && double.IsFinite(x) && double.IsFinite(y)) return new(x, y);
        }
        throw new JsonException("效果坐标需要原 X,Y 格式。");
    }
    public override void Write(Utf8JsonWriter writer, EffectPoint value, JsonSerializerOptions options) => writer.WriteStringValue(FormattableString.Invariant($"{value.X},{value.Y}"));
}
