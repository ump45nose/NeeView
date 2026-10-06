// Copyright (c) NeeLaboratory. 原主题标识与 JSON 字符串转换，移除代码生成及全局路径依赖。
using System.Text.Json;
using System.Text.Json.Serialization;
namespace NeeView;

/// <summary>原 Type 或 Custom.FileName 标识；路径由本次 Profile 配置提供。</summary>
[JsonConverter(typeof(JsonThemeSourceConverter))]
public sealed class ThemeSource : IEquatable<ThemeSource>
{
    public ThemeSource(ThemeType type) : this(type, null) { }
    /// <summary>构造原主题标识，自定义类型必须携带文件名。</summary>
    public ThemeSource(ThemeType type, string? fileName)
    {
        if (!Enum.IsDefined(type)) throw new ArgumentOutOfRangeException(nameof(type));
        if (type == ThemeType.Custom && string.IsNullOrWhiteSpace(fileName)) throw new ArgumentException("Custom requires FileName.");
        if (type != ThemeType.Custom && fileName is not null) throw new ArgumentException("FileName requires Custom.");
        Type = type; FileName = fileName;
    }
    public ThemeType Type { get; }
    public string? FileName { get; }
    public override string ToString() => Type + (FileName is null ? "" : "." + FileName);
    /// <summary>保持空值/缺失 Custom 文件名回到 Dark 的原规则。</summary>
    public static ThemeSource Parse(string? value)
    {
        if (string.IsNullOrEmpty(value)) return new(ThemeType.Dark);
        var tokens = value.Split('.', 2); var type = Enum.Parse<ThemeType>(tokens[0]);
        var fileName = tokens.Length == 2 ? tokens[1] : null;
        if (type == ThemeType.Custom && fileName is null) type = ThemeType.Dark;
        return new(type, fileName);
    }
    public bool Equals(ThemeSource? other) => other is not null && Type == other.Type && FileName == other.FileName;
    public override bool Equals(object? obj) => obj is ThemeSource other && Equals(other);
    public override int GetHashCode() => HashCode.Combine(Type, FileName);
}

/// <summary>原配置只写字符串，不将主题标识展开为第二状态对象。</summary>
public sealed class JsonThemeSourceConverter : JsonConverter<ThemeSource>
{
    public override ThemeSource Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) => ThemeSource.Parse(reader.GetString());
    public override void Write(Utf8JsonWriter writer, ThemeSource value, JsonSerializerOptions options) => writer.WriteStringValue(value.ToString());
}
