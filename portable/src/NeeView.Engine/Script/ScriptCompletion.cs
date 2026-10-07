// Copyright (c) NeeLaboratory. 原WordNodeHelper/ConsoleHost补全关系的无控件适配，MIT。
using System.Reflection;
namespace NeeView;
/// <summary>只读取公开类型元数据和实际PropertyMap/命令快照，不调用业务属性或系统能力。</summary>
public static class ScriptCompletion
{
    public static IReadOnlyList<string> Create(Type hostType, ConfigMap config, CommandTable commands)
    {
        var words = new HashSet<string>(StringComparer.Ordinal) { "cls", "help", "exit", "log", "system", "include", "sleep", "nv" };
        void Members(Type type, string prefix, int depth)
        {
            if (depth > 5) return;
            foreach (var property in type.GetProperties(BindingFlags.Public | BindingFlags.Instance).Where(p => p.GetIndexParameters().Length == 0))
            {
                var word = prefix + "." + property.Name; words.Add(word);
                if (property.PropertyType.Name.EndsWith("Accessor", StringComparison.Ordinal) && property.PropertyType != type) Members(property.PropertyType, word, depth + 1);
            }
            foreach (var method in type.GetMethods(BindingFlags.Public | BindingFlags.Instance).Where(m => !m.IsSpecialName && m.DeclaringType != typeof(object))) words.Add(prefix + "." + method.Name);
        }
        void Map(PropertyMap map)
        { foreach (var pair in map) { if (pair.Value.IsObsolete) continue; words.Add(pair.Value.Name); if (pair.Value is PropertyMap child) Map(child); } }
        Members(hostType, "nv", 0); Map(config.Map);
        foreach (var command in commands.Definitions)
        {
            var prefix = "nv.Command." + command.Name; words.Add(prefix); Members(typeof(CommandAccessor), prefix, 0);
            if (CommandParameterTypes.Get(command.Name) is { } parameter) Members(parameter, prefix + ".Parameter", 0);
        }
        return words.Order(StringComparer.Ordinal).ToArray();
    }
    /// <summary>定位光标前的点分成员；不跨越空格、字符串和调用标点替换其它代码。</summary>
    public static (int Start, string Prefix) Prefix(string text, int caret)
    {
        caret = Math.Clamp(caret, 0, text.Length); int start = caret;
        while (start > 0 && (char.IsLetterOrDigit(text[start - 1]) || text[start - 1] is '_' or '.' or '$')) --start;
        return (start, text[start..caret]);
    }
}
