using System.Globalization;
namespace NeeView;

/// <summary>替换实际使用的 WPF Color 值；只有 ARGB 数据，没有显示资源或原生句柄。</summary>
[System.Text.Json.Serialization.JsonConverter(typeof(BackgroundColorConverter))]
public readonly record struct ThemeRgba(byte A, byte R, byte G, byte B)
{
    /// <summary>按原 Color.FromArgb 次序构造主题颜色。</summary>
    public static ThemeRgba FromArgb(byte a, byte r, byte g, byte b) => new(a, r, g, b);
    /// <summary>保持原 JSON 的 #AARRGGBB 输出。</summary>
    public override string ToString() => $"#{A:X2}{R:X2}{G:X2}{B:X2}";
    /// <summary>读取原十六进制、标准命名和 scRGB 颜色；不接受系统动态色名称。</summary>
    /// <param name="text">原 ThemeColor 中去除不透明度后的颜色令牌。</param><returns>8 位 sRGB ARGB。</returns>
    public static ThemeRgba Parse(string text)
    {
        var value = text.Trim();
        if (value.StartsWith('#'))
        {
            var hex = value[1..];
            if (hex.Length is 3 or 4) hex = string.Concat(hex.Select(c => new string(c, 2)));
            if (hex.Length == 6) hex = "FF" + hex;
            if (hex.Length == 8 && uint.TryParse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var number))
                return new((byte)(number >> 24), (byte)(number >> 16), (byte)(number >> 8), (byte)number);
        }
        else if (value.StartsWith("sc#", StringComparison.OrdinalIgnoreCase))
        {
            var parts = value[3..].Split(',').Select(s => double.Parse(s, CultureInfo.InvariantCulture)).ToArray();
            if (parts.Length is 3 or 4 && parts.All(double.IsFinite))
            {
                var offset = parts.Length == 4 ? 1 : 0;
                static byte Channel(double v) => v <= 0 ? (byte)0 : v >= 1 ? (byte)255 :
                    (byte)(255 * (v <= .0031308 ? 12.92 * v : 1.055 * Math.Pow(v, 1 / 2.4) - .055) + .5);
                return new(parts.Length == 4 ? (byte)(Math.Clamp(parts[0], 0, 1) * 255 + .5) : (byte)255,
                    Channel(parts[offset]), Channel(parts[offset + 1]), Channel(parts[offset + 2]));
            }
        }
        else
        {
            // System.Drawing.Primitives 只是 .NET 标准色值表；不引用 System.Drawing.Common 或图像后端。
            var named = System.Drawing.Color.FromName(value);
            if (named.IsKnownColor && !named.IsSystemColor) return new(named.A, named.R, named.G, named.B);
        }
        throw new FormatException("Unknown theme color: " + text);
    }
}
