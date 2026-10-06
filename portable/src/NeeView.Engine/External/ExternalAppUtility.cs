// Copyright (c) NeeLaboratory. 原占位符、缺省参数及保存资格，Mac按字面参数替换系统启动。
using System.Text;
using System.Text.RegularExpressions;
namespace NeeView;

/// <summary>已解析的系统启动请求；平台不得再次按shell解释参数。</summary>
public sealed record ExternalAppLaunchRequest(string? Command, IReadOnlyList<string> Arguments, string? WorkingDirectory);

/// <summary>原ExternalAppUtility的纯规则；实体化与真实提交在BookOperation和平台。</summary>
public static class ExternalAppUtility
{
    private static readonly Regex Keywords = new(@"\{File\}|\$File|\{Uri\}|\$Uri", RegexOptions.CultureInvariant);
    /// <summary>原NeeView占位符指向本次实际可执行文件，提交前保存Profile。</summary>
    public static bool RequiresSave(IExternalApp options) => options.Command?.Contains("{NeeView}", StringComparison.Ordinal) == true || options.Command?.Contains("$NeeView", StringComparison.Ordinal) == true;
    /// <summary>沿原缺文件占位符时在末尾附加默认参数，不改变已有参数文字。</summary>
    public static string ValidateApplicationParam(string? source)
    { var value = source?.Trim() ?? ""; return Keywords.IsMatch(value) ? value : value + " \"{File}\""; }
    /// <summary>先解析模板后替换路径，使文件名中的引号、空格和shell字符保持一个字面参数。</summary>
    /// <param name="options">已捕获的原配置。</param><param name="path">来源提供的原路径或临时实体。</param><param name="executablePath">唯一Mac入口实际地址。</param>
    public static ExternalAppLaunchRequest CreateLaunchRequest(IExternalApp options, string path, string executablePath)
    {
        var command = string.IsNullOrWhiteSpace(options.Command) ? null : Regex.Replace(options.Command, @"\{NeeView\}|\$NeeView", _ => executablePath);
        var args = ParseArguments(ValidateApplicationParam(options.Parameter), path);
        if (command is null && args.Count != 1) throw new InvalidOperationException("系统关联应用只支持单个文件参数；多参数请配置应用命令。");
        return new(command, args, options.WorkingDirectory);
    }
    /// <summary>支持双/单引号和明确转义；损坏模板返回错误，不执行shell或递归替换注入路径。</summary>
    /// <param name="template">用户配置文字，尚未替换路径。</param><param name="path">按原File/Uri两种方式替换的完整地址。</param>
    public static IReadOnlyList<string> ParseArguments(string template, string path)
    {
        var result = new List<string>(); var current = new StringBuilder(); char quote = '\0'; bool started = false;
        for (var i = 0; i < template.Length; i++)
        {
            var ch = template[i];
            if (ch == '\\' && quote != '\'' && i + 1 < template.Length && (template[i + 1] is '\\' or '"' or '\'' || quote == '\0' && char.IsWhiteSpace(template[i + 1])))
            { current.Append(template[++i]); started = true; continue; }
            if (quote != '\0') { if (ch == quote) quote = '\0'; else current.Append(ch); started = true; continue; }
            if (ch is '\'' or '"') { quote = ch; started = true; continue; }
            if (char.IsWhiteSpace(ch)) { if (started) { result.Add(current.ToString()); current.Clear(); started = false; } continue; }
            current.Append(ch); started = true;
        }
        if (quote != '\0') throw new FormatException("外部应用参数包含未闭合引号。");
        if (started) result.Add(current.ToString());
        var uri = Uri.EscapeDataString(path);
        return result.Select(value => Keywords.Replace(value, match => match.Value is "{File}" or "$File" ? path : uri)).ToArray();
    }
}
