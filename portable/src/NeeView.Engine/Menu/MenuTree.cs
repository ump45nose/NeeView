// Copyright (c) NeeLaboratory. 默认树逐项来自 NeeView/Menu/MenuTree.cs:CreateDefault。
using System.Reflection;
using System.Text.Json;
namespace NeeView;

/// <summary>原八组菜单的跨平台数据入口；呈现由界面适配，未实现能力仍保留节点。</summary>
public static class MenuTree
{
    /// <summary>返回原默认菜单树的新副本，顺序、分隔线和命令名均保留。</summary>
    public static MenuNode CreateDefault()
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("NeeView.Menu.default-menu.json")!;
        return JsonSerializer.Deserialize<MenuNode>(stream)!;
    }
}
