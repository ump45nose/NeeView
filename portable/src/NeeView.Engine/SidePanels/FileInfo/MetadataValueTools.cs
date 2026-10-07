// Copyright (c) NeeLaboratory. MIT. 原MetadataValueTools显示规则；别名来自固定原文，不依赖WPF转换器。
using System.Globalization;
namespace NeeView;
public static class MetadataValueTools
{
    /// <summary>原数组、日期、枚举和FormatValue格式；默认日期不显示，坏自定义日期格式明确失败。</summary>
    public static string? ToDisplayString(object? value) => value switch
    {
        null => null,
        IEnumerable<string> strings => string.Join("; ", strings),
        DateTime date => date != default ? date.ToString(Config.Current.Information.DateTimeFormat, CultureInfo.CurrentCulture) : null,
        Enum e => HelpText.GetString(e.GetType().Name + "." + e) is { Length: > 0 } text ? text : e.ToString(),
        _ => value.ToString()
    };
}
