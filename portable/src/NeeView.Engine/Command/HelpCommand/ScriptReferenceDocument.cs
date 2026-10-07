using System.Net;
using System.Reflection;
using System.Text;

namespace NeeView;

/// <summary>Portable equivalent of the upstream ScriptManual. It deliberately documents the
/// API actually present in this build and marks the remaining upstream surface explicitly.</summary>
public static class ScriptReferenceDocument
{
    /// <summary>从实际nv根/访问器、原配置映射与唯一命令表生成手册，不读取用户图片。</summary>
    /// <param name="rootType">装配层提供实际根对象类型，Engine不引用界面程序集。</param>
    public static string Create(ConfigMap config, CommandTable commands, Type? rootType = null)
    {
        ArgumentNullException.ThrowIfNull(config);
        ArgumentNullException.ThrowIfNull(commands);
        var b = new StringBuilder(HtmlHelpUtility.CreateHeader("NeeView script reference"));
        b.Append("<body><h1>NeeView script reference</h1><p>This reference is generated from the portable runtime.</p>");
        b.Append("<h2 id=\"nv\">nv root and classes</h2>");
        AppendRuntimeTypes(b, rootType);
        b.Append("<h2 id=\"ConfigList\">nv.Config</h2><table><tr><th>Name</th><th>Type</th></tr>");
        AppendMap(b, config.Map, 0);
        b.Append("</table>");
        b.Append("<h2 id=\"CommandList\">Commands</h2><table><tr><th>Name</th><th>Shortcut</th><th>Stage</th><th>Summary</th></tr>");
        foreach (var command in commands.Definitions.OrderBy(x => x.Name, StringComparer.Ordinal))
        {
            b.Append("<tr><td><code>").Append(E(command.Name)).Append("</code></td><td>")
                .Append(E(command.Shortcut)).Append("</td><td>").Append(E(command.Stage)).Append("</td><td>")
                .Append(E(command.Text)).Append("</td></tr>");
        }
        b.Append("</table><h2 id=\"Builtins\">Script built-ins</h2><table><tr><th>Name</th><th>Meaning</th></tr>");
        AppendBuiltin(b, "log(value)", "Writes a value to the script log.");
        AppendBuiltin(b, "include(path)", "Loads another script relative to the current script file.");
        AppendBuiltin(b, "sleep(milliseconds)", "Waits cooperatively and can be cancelled.");
        AppendBuiltin(b, "system(command, argument)", "Invokes the platform system callback.");
        b.Append("</table><h2 id=\"Gaps\">Migration status</h2><p>The upstream WPF document builder and localized resource tokens are not migrated; this page reports portable reflection and manifest data instead. Command execution still rejects commands whose implementation is absent.</p></body>");
        return b.Append(HtmlHelpUtility.CreateFooter()).ToString();
    }

    private static void AppendRuntimeTypes(StringBuilder b, Type? rootType)
    {
        var assembly = typeof(ScriptReferenceDocument).Assembly;
        var types = assembly.GetTypes().Where(t => t.IsPublic && (t.Name.EndsWith("Accessor", StringComparison.Ordinal) || t.Name is "CommandAccessorMap"))
            .Concat(rootType is null ? [] : rootType.Assembly.GetTypes().Where(t => t.IsPublic &&
                (t == rootType || t.Namespace == rootType.Namespace && t.Name.EndsWith("Accessor", StringComparison.Ordinal))))
            .Distinct().OrderBy(t => t.Name, StringComparer.Ordinal);
        foreach (var type in types)
        {
            b.Append("<h3>").Append(E(type == rootType ? "[Root Instance] nv" : type.Name)).Append("</h3><ul>");
            foreach (var p in type.GetProperties(BindingFlags.Public | BindingFlags.Instance).Where(p => p.GetIndexParameters().Length == 0).OrderBy(p => p.Name, StringComparer.Ordinal))
                b.Append("<li><code>").Append(E(p.Name)).Append("</code> : ").Append(E(p.PropertyType.Name)).Append("</li>");
            foreach (var m in type.GetMethods(BindingFlags.Public | BindingFlags.Instance)
                .Where(m => !m.IsSpecialName && m.DeclaringType != typeof(object)).OrderBy(m => m.Name, StringComparer.Ordinal))
                b.Append("<li><code>").Append(E(m.Name)).Append('(').Append(E(string.Join(", ", m.GetParameters().Select(p => p.ParameterType.Name + " " + p.Name))))
                    .Append(")</code> : ").Append(E(m.ReturnType.Name)).Append("</li>");
            b.Append("</ul>");
        }
    }

    private static void AppendMap(StringBuilder b, PropertyMap map, int depth)
    {
        foreach (var pair in map.OrderBy(x => x.Key, StringComparer.Ordinal))
        {
            if (pair.Value.IsObsolete) continue;
            var source = pair.Value as PropertyMapSource;
            b.Append("<tr><td style=\"padding-left:").Append(depth * 18 + 10).Append("px\">").Append(E(pair.Value.Name)).Append("</td><td>")
                .Append(E(source?.PropertyInfo.PropertyType.Name ?? "object")).Append(source?.IsReadOnly == true ? " (read only)" : "").Append("</td></tr>");
            if (pair.Value is PropertyMap child) AppendMap(b, child, depth + 1);
        }
    }

    private static void AppendBuiltin(StringBuilder b, string name, string meaning) => b.Append("<tr><td><code>").Append(E(name)).Append("</code></td><td>").Append(E(meaning)).Append("</td></tr>");
    private static string E(string? value) => WebUtility.HtmlEncode(value ?? "");
}
