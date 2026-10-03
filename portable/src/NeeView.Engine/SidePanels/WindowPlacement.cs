// Copyright (c) NeeLaboratory. 迁入原 Windows/WindowPlacement 与 WindowStateEx，遵循仓库 MIT。
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
namespace NeeView.Windows;

public enum WindowStateEx { None, Normal, Minimized, Maximized, FullScreen, FullDesktop }

/// <summary>原屏幕像素位置与字符串 JSON；不持有 WPF 或 Avalonia 类型。</summary>
[JsonConverter(typeof(JsonWindowPlaceConverter))]
public sealed record WindowPlacement
{
    public static WindowPlacement None { get; } = new();
    public WindowStateEx WindowStateEx { get; init; }
    public int Left { get; init; }
    public int Top { get; init; }
    public int Width { get; init; }
    public int Height { get; init; }
    private string? Unparsed { get; init; }
    public WindowPlacement() { }
    /// <summary>使用原物理像素单位；None 状态的有效位置归为普通窗口。</summary>
    public WindowPlacement(WindowStateEx state, int left, int top, int width, int height)
    { WindowStateEx = state == WindowStateEx.None ? WindowStateEx.Normal : state; Left = left; Top = top; Width = width; Height = height; }
    /// <summary>保留原有效判断，平台恢复时另行检查安全尺寸。</summary>
    public bool IsValid() => Width > 0 || Height > 0;
    /// <summary>沿原五段字段输出；未知旧值只读保留，不用于窗口坐标。</summary>
    public override string ToString() => Unparsed ?? (IsValid() ? string.Create(CultureInfo.InvariantCulture, $"{WindowStateEx},{Left},{Top},{Width},{Height}") : "");
    /// <summary>读取原格式，未知或损坏值保留到下一次保存，不能使整个 Profile 失效。</summary>
    public static WindowPlacement Parse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return None;
        var tokens = text.Split(',');
        if (tokens.Length == 5 && Enum.TryParse<WindowStateEx>(tokens[0], out var state) && Enum.IsDefined(state) &&
            int.TryParse(tokens[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var left) &&
            int.TryParse(tokens[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out var top) &&
            int.TryParse(tokens[3], NumberStyles.Integer, CultureInfo.InvariantCulture, out var width) &&
            int.TryParse(tokens[4], NumberStyles.Integer, CultureInfo.InvariantCulture, out var height)) return new(state, left, top, width, height);
        return new() { Unparsed = text };
    }
}
public sealed class JsonWindowPlaceConverter : JsonConverter<WindowPlacement>
{
    /// <summary>原 JSON 为字符串；不改变旧字段单位或值。</summary>
    public override WindowPlacement Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) => WindowPlacement.Parse(reader.GetString());
    /// <summary>保存未知字符串和原五段位置格式。</summary>
    public override void Write(Utf8JsonWriter writer, WindowPlacement value, JsonSerializerOptions options) => writer.WriteStringValue(value.ToString());
}
