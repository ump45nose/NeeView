// Copyright (c) NeeLaboratory. 原MainMenuManual/CreateDefault/GetMenuTable递归适配。
using System.Net;
using System.Text;
using NeeView.Text.SimpleHtmlBuilder;
namespace NeeView;

public static class MainMenuManual
{
    /// <summary>原默认八组及组内递归菜单、命令备注；未迁能力按用户要求保留说明。</summary>
    /// <param name="definitions">同一原命令登记元数据。</param><param name="implemented">执行入口的迁移状态，不是当前可用性。</param>
    /// <param name="version">原开发导出版本说明开关。</param><param name="applicationVersion">实际Mac构建版本。</param>
    /// <param name="menuTree">默认使用固定原菜单；测试可传入纯节点。</param><returns>没有外部脚本依赖的本地HTML。</returns>
    public static string CreateMainMenuManual(IReadOnlyList<CommandDefinition> definitions, Func<string, bool> implemented,
        bool version = false, string? applicationVersion = null, MenuNode? menuTree = null)
    {
        var title = "NeeView " + HelpText.GetString("Word.MainMenu");
        var body = new TagNode("body").AddNode(Literal("h1", title));
        if (version) body.AddNode(Literal("p", "Version " + applicationVersion));
        var commands = definitions.ToDictionary(d => d.Name, StringComparer.Ordinal);
        foreach (var group in (menuTree ?? MenuTree.CreateDefault()).Children ?? [])
        {
            body.AddNode(Literal("h3", Display(group.Name ?? "")));
            var table = new TagNode("table");
            foreach (var item in Rows(group))
            {
                commands.TryGetValue(item.CommandName ?? "", out var command);
                var label = item.Name ?? command?.MenuText ?? command?.Text ?? item.CommandName ?? "";
                var remarks = command is null ? "" : HelpText.GetString(Path.GetFileNameWithoutExtension(command.Source) + ".Remarks");
                var note = new TagNode("td");
                // 备注是固定可信原文，可保留原参考链接；用户/菜单标签和未知命令只作为文本。
                if (remarks.Length > 0) note.AddNode(new TextNode(remarks));
                if (item.CommandName is { } name && !implemented(name)) note.AddNode(new TextNode(" · 尚未迁移"));
                table.AddNode(new TagNode("tr").AddNode(Literal("td", Display(label))).AddNode(note));
            }
            body.AddNode(table);
        }
        return new StringBuilder().AppendLine(HtmlHelpUtility.CreateHeader(title)).AppendLine(body.ToIndentString())
            .AppendLine(HtmlHelpUtility.CreateFooter()).ToString();
    }
    private static string Display(string label) => label.Replace("_", "", StringComparison.Ordinal);
    private static TagNode Literal(string tag, string text) => new TagNode(tag).AddNode(new TextNode(WebUtility.HtmlEncode(text)));
    /// <summary>沿原GetMenuTable跳过None/分隔，组节点先输出，再递归子节点。</summary>
    private static IEnumerable<MenuNode> Rows(MenuNode group)
    {
        foreach (var child in group.Children ?? [])
        {
            if (child.MenuElementType is MenuElementType.None or MenuElementType.Separator) continue;
            yield return child;
            if (child.MenuElementType == MenuElementType.Group) foreach (var descendant in Rows(child)) yield return descendant;
        }
    }
}
