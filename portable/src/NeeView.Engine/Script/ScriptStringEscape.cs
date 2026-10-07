// Copyright (c) NeeLaboratory.
namespace NeeView;

/// <summary>脚本文档注释使用的窄转义集；未知转义保持原文。</summary>
public static class ScriptStringEscape
{
    public static string Unescape(string value)
    {
        var result = new System.Text.StringBuilder(value.Length);
        for (var i = 0; i < value.Length; i++)
        {
            if (value[i] != '\\' || i + 1 == value.Length) { result.Append(value[i]); continue; }
            var next = value[++i];
            result.Append(next switch { 't' => '\t', 'n' => '\n', 'r' => '\r', '\\' => '\\', '"' => '"', _ => '\0' });
            if (next is not ('t' or 'n' or 'r' or '\\' or '"')) { result.Length--; result.Append('\\').Append(next); }
        }
        return result.ToString();
    }
}
