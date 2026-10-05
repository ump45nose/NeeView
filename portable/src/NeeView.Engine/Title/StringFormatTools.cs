// Copyright (c) NeeLaboratory. MIT；迁入原 StringFormatTools 的数值、路径及字符串格式算法。
using System.Globalization;
using System.Text;
namespace NeeView.StringTemplate;

public static class StringFormatTools
{
    /// <summary>原数值格式使用InvariantCulture；字符串沿原#插入与路径分隔规则。</summary>
    /// <param name="format">占位符冒号后的格式。</param><param name="value">已知标题字段。</param><returns>原格式的显示结果。</returns>
    public static string FormatValue(string format, object value)
    {
        if (value is not string text || format == "")
            return string.Format(CultureInfo.InvariantCulture, format == "" ? "{0}" : "{0:" + format + "}", value);
        if (string.IsNullOrEmpty(text)) return "";
        if (format[0] == '/')
            // Mac逻辑条目统一为/；反斜杠是合法文件名，不能当Windows分隔符拆开。
            return string.Join(string.Concat(Walk(format[1..]).Select(e => e.Char)), text.Split('/', StringSplitOptions.RemoveEmptyEntries));
        var words = Walk(format).ToArray(); var count = words.Count(e => !e.Escaped && e.Char == '#');
        int index = 0; var result = new StringBuilder();
        foreach (var word in words)
        {
            if (!word.Escaped && word.Char == '#')
            {
                var p = text.Length - (count - index);
                if (p >= 0) result.Append(index == 0 ? text[..(p + 1)] : text[p].ToString());
                index++;
            }
            else result.Append(word.Char);
        }
        return result.ToString();
    }
    /// <summary>沿原EscapeStringWalker：反斜杠只标记下一字符，不转换n/t，末尾单独反斜杠不产出。</summary>
    /// <param name="text">原字符串占位符格式。</param><returns>字符及其转义标记。</returns>
    private static IEnumerable<(char Char, bool Escaped)> Walk(string text)
    {
        for (int i = 0; i < text.Length; i++)
            if (text[i] == '\\') { if (i + 1 < text.Length) yield return (text[++i], true); }
            else yield return (text[i], false);
    }
}
