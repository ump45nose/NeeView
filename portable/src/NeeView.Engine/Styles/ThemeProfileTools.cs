// Copyright (c) NeeLaboratory. 原 ThemeProfileTools 的读取/覆盖算法，嵌入流替换 WPF 资源 URI。
using System.Text.Json;
namespace NeeView;

public static class ThemeProfileTools
{
    private static readonly JsonSerializerOptions Options = new()
    { PropertyNameCaseInsensitive = true, AllowTrailingCommas = true, ReadCommentHandling = JsonCommentHandling.Skip };
    /// <summary>读取原嵌入预设；文件名与原 Libraries/Themes 完全对应。</summary>
    public static ThemeProfile LoadFromContent(string name)
    {
        using var stream = typeof(ThemeProfileTools).Assembly.GetManifestResourceStream("NeeView.Styles.Themes." + name)
            ?? throw new FileNotFoundException("No such theme: " + name);
        return Load(stream, name);
    }
    /// <summary>原模板按内嵌字节写出；仅允许新文件，不能覆盖同时出现的用户主题。</summary>
    /// <param name="name">原内嵌主题材料名。</param><param name="path">调用方新建目录中的样例路径。</param>
    public static void SaveFromContent(string name, string path)
    {
        using var source = typeof(ThemeProfileTools).Assembly.GetManifestResourceStream("NeeView.Styles.Themes." + name)
            ?? throw new FileNotFoundException("No such theme: " + name);
        using var output = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        source.CopyTo(output);
        output.Flush(true);
    }
    /// <summary>后台读取自定义原 JSON；4 MiB 上限避免设置材料形成无界内存。</summary>
    public static ThemeProfile LoadFromFile(string path)
    {
        using var stream = File.OpenRead(path);
        if (stream.Length > 4 * 1024 * 1024) throw new InvalidDataException("Theme profile exceeds 4 MiB.");
        return Load(stream, path);
    }
    /// <summary>UTF-8 BOM、原注释与末尾逗号保持兼容；不写回或执行主题材料。</summary>
    private static ThemeProfile Load(Stream stream, string source)
    {
        using var reader = new StreamReader(stream);
        var profile = JsonSerializer.Deserialize<ThemeProfile>(reader.ReadToEnd(), Options);
        if (profile?.Colors is null || profile.Colors.Values.Any(v => v is null)) throw new FormatException("Wrong theme profile format: " + source);
        return profile;
    }
    /// <summary>原浅拷贝颜色覆盖算法；不会修改缓存中的父主题。</summary>
    public static ThemeProfile Merge(ThemeProfile baseProfile, ThemeProfile overwriteProfile)
    {
        var profile = (ThemeProfile)baseProfile.Clone();
        foreach (var pair in overwriteProfile.Colors) profile[pair.Key] = pair.Value;
        return profile;
    }
}
