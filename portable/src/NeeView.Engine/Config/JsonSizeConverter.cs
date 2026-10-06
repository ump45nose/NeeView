// Copyright (c) NeeLaboratory. 原Text/Json/JsonSizeConverter，替换WPF SizeConverter的实际值解析。
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
namespace NeeView;
/// <summary>原尺寸字符串读写；兼容Mac早期对象值，不改其他几何字段。</summary>
public sealed class JsonSizeConverter : JsonConverter<Size>
{
    public override Size Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Null) return new();
        if (reader.TokenType == JsonTokenType.String)
        {
            if (reader.GetString() == "Empty") return new();
            var parts = (reader.GetString() ?? "").Split(',', StringSplitOptions.TrimEntries);
            if (parts.Length == 2 && double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var width)
                && double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var height) && double.IsFinite(width) && double.IsFinite(height)) return new(width, height);
            throw new JsonException("尺寸必须为原Width,Height格式。");
        }
        using var document = JsonDocument.ParseValue(ref reader);
        if (document.RootElement.ValueKind == JsonValueKind.Object)
        {
            double? width = null, height = null;
            foreach (var property in document.RootElement.EnumerateObject())
            {
                if (property.Name.Equals("Width", StringComparison.OrdinalIgnoreCase)) width = property.Value.GetDouble();
                if (property.Name.Equals("Height", StringComparison.OrdinalIgnoreCase)) height = property.Value.GetDouble();
            }
            if (width is { } w && height is { } h && double.IsFinite(w) && double.IsFinite(h)) return new(w, h);
        }
        throw new JsonException("无效尺寸配置。");
    }
    public override void Write(Utf8JsonWriter writer, Size value, JsonSerializerOptions options) =>
        writer.WriteStringValue(FormattableString.Invariant($"{value.Width},{value.Height}"));
}
