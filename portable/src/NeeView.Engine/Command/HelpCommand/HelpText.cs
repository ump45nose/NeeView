// Copyright (c) NeeLaboratory. 帮助所需原中文/共享资源的固定展开快照，不引入WPF资源管理器。
using System.Globalization;
using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
namespace NeeView;

/// <summary>只服务原帮助模板；资源是固定可信原文，动态文本先HTML转义。</summary>
public static partial class HelpText
{
    private static readonly IReadOnlyDictionary<string, string> Texts = Read<Dictionary<string, string>>("help-text.json");
    public static IReadOnlyList<string> MetadataKeys { get; } = Array.AsReadOnly(Read<string[]>("metadata-keys.json"));
    [GeneratedRegex(@"@[a-zA-Z0-9_\.\-#]+[a-zA-Z0-9]")]
    private static partial Regex Keys();
    /// <summary>读取固定展开原文，未定义的可选备注返回空文本。</summary>
    public static string GetString(string key) => Texts.TryGetValue(key, out var value) ? value : "";
    /// <summary>仅展开固定帮助资源；未知可选备注省略，必要模板键缺失时报错。</summary>
    public static string Replace(string text, bool fallback) => Keys().Replace(text, match => Texts.TryGetValue(match.Value[1..], out var value)
        ? value : fallback ? throw new InvalidDataException("帮助文案缺少原资源：" + match.Value) : "");
    /// <summary>先转义节点文字，再展开固定原文中的合法标记与参考链接。</summary>
    public static string ToHtml(string text) => Replace(WebUtility.HtmlEncode(text), true);
    /// <summary>原搜索profile名称转资源键，只将首字母按固定文化大写。</summary>
    public static string ToTitleCase(this string value) => value.Length == 0 ? value : char.ToUpper(value[0], CultureInfo.InvariantCulture) + value[1..];
    private static T Read<T>(string name)
    {
        using var stream = typeof(HelpText).Assembly.GetManifestResourceStream("NeeView.Command.HelpCommand." + name)
            ?? throw new InvalidDataException("缺少帮助资源：" + name);
        return JsonSerializer.Deserialize<T>(stream) ?? throw new InvalidDataException("无效帮助资源：" + name);
    }
}
