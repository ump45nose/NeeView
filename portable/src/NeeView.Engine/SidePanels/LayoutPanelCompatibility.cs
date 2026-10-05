// Copyright (c) NeeLaboratory. 适配原 LayoutDockPanelContent.Restore / LayoutPanelValidator。
using System.Text.Json;
using System.Text.Json.Nodes;
namespace NeeView.Runtime.LayoutPanel;

/// <summary>原三代布局的读取适配；不依赖控件，不改变未知面板或附属元数据。</summary>
internal static class LayoutPanelCompatibility
{
    /// <summary>按原 V2 → V1 → V0 回退，V0 固定纵向，空但存在的 V1 允许继续回退。</summary>
    /// <param name="dock">原停靠栏 JSON。</param><returns>V2 已有项时返回 null，表示不转换。</returns>
    internal static List<LayoutDockPanelLayout>? ReadLegacy(JsonObject dock)
    {
        if (dock["PanelLayoutV2"] is not null and not JsonArray) throw new JsonException("PanelLayoutV2 必须是数组。");
        if (dock["PanelLayoutV2"] is JsonArray { Count: > 0 }) return null;
        if (dock["PanelLayout"] is not null and not JsonArray) throw new JsonException("旧 PanelLayout 必须是数组。");
        if (dock["PanelLayout"] is JsonArray { Count: > 0 } v1)
            return v1.Select(node =>
            {
                var item = node as JsonObject ?? throw new JsonException("旧 PanelLayout 项必须是对象。");
                var orientation = item["Orientation"] is null ? PanelOrientation.Vertical :
                    item["Orientation"]!.Deserialize<PanelOrientation>(new JsonSerializerOptions { Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() } });
                if (!Enum.IsDefined(orientation)) throw new JsonException("旧面板排列方向无效。");
                return new LayoutDockPanelLayout { Orientation = orientation, Panels = Strings(item["Panels"]) };
            }).ToList();
        if (dock["Panels"] is not null and not JsonArray) throw new JsonException("旧 Panels 必须是数组。");
        if (dock["Panels"] is JsonArray v0)
            return v0.Select(node => new LayoutDockPanelLayout { Panels = Strings(node) }).ToList();
        return null;
    }

    /// <summary>供导入预览/应用共用；只增加当前布局字段，旧字段保留以保存未知元数据。</summary>
    internal static void Upgrade(JsonObject? layout)
    {
        if (layout?["Docks"] is not JsonObject docks) return;
        foreach (var dock in docks.Select(pair => pair.Value).OfType<JsonObject>())
            if (ReadLegacy(dock) is { } groups)
                dock["PanelLayoutV2"] = new JsonArray(groups.Select(group => (JsonNode)JsonValue.Create(group.Orientation + ":" + string.Join(',', group.Panels))!).ToArray());
    }

    /// <summary>原 Pagemark → Playlist 改名涵盖面板字典、选择、三代分组和浮窗。</summary>
    internal static void Rename(JsonObject? layout, string oldName, string newName)
    {
        if (layout is null) return;
        if (layout["Panels"] is JsonObject panels && panels.ContainsKey(oldName))
        {
            // 已有新键优先，旧碰撞材料保留到扩展；不能静默覆盖新窗口位置。
            if (panels.ContainsKey(newName)) layout["MacImportedLegacyPanel"] = panels[oldName]?.DeepClone();
            else panels[newName] = panels[oldName]?.DeepClone();
            panels.Remove(oldName);
        }
        foreach (var dock in (layout["Docks"] as JsonObject)?.Select(pair => pair.Value).OfType<JsonObject>() ?? [])
        {
            if (dock["SelectedItem"]?.GetValue<string>() == oldName) dock["SelectedItem"] = newName;
            foreach (var group in (dock["Panels"] as JsonArray)?.OfType<JsonArray>() ?? []) RenameStrings(group);
            foreach (var group in (dock["PanelLayout"] as JsonArray)?.OfType<JsonObject>() ?? [])
                if (group["Panels"] is JsonArray members) RenameStrings(members);
            if (dock["PanelLayoutV2"] is JsonArray v2)
                for (var i = 0; i < v2.Count; i++)
                {
                    var parts = v2[i]!.GetValue<string>().Split(':', 2);
                    if (parts.Length == 2) v2[i] = parts[0] + ":" + string.Join(',', parts[1].Split(',').Select(key => key == oldName ? newName : key));
                }
        }
        if (layout["Windows"]?["Panels"] is JsonArray windows) RenameStrings(windows);
        void RenameStrings(JsonArray values)
        { for (var i = 0; i < values.Count; i++) if (values[i]?.GetValue<string>() == oldName) values[i] = newName; }
    }

    private static List<string> Strings(JsonNode? node) => node is null ? [] : node is JsonArray values ?
        values.Select(value => value?.GetValue<string>() ?? throw new JsonException("旧布局面板名不能为空。")).ToList() : throw new JsonException("旧布局 Panels 必须是数组。");
}
