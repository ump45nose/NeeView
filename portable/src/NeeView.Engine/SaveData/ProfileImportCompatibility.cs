// Copyright (c) NeeLaboratory. 适配原 BookHistoryCollectionValidator / BookmarkCollectionValidator 的明确版本分支。
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
namespace NeeView;

/// <summary>按文件限制可应用版本；未迁入全部 UserSettingValidator 时不能声称任意旧版本兼容。</summary>
internal static class ProfileImportCompatibility
{
    private static readonly JsonSerializerOptions Options = CreateOptions();
    /// <summary>按原文件类型及已迁入版本分支给出阻止实际应用的原因。</summary>
    /// <param name="name">原五文件名。</param><param name="raw">未升级的候选。</param><returns>null 表示本批允许应用。</returns>
    internal static string? BlockReason(string name, JsonObject raw)
    {
        var format = raw["Format"]?.GetValue<string>() ?? "";
        var parts = format.Split('/');
        string expected = name switch { "UserSetting.json" => "NeeView", "History.json" => "NeeView.History", "Bookmark.json" => "NeeView.Bookmark", "Foldres.json" => "NeeView.Folders", _ => "NeeView.QuickAccess" };
        if (parts.Length != 2 || !(parts[0] == expected || parts[0] == "NeeView" || name == "UserSetting.json" && parts[0] == "NeeView.UserSetting") || !Version.TryParse(parts[1], out var version))
            return name + " 版本缺失或格式类型不匹配，只允许预览。";
        bool settings = name is not ("History.json" or "Bookmark.json");
        int minimum = settings ? 46 : 44;
        // 原 Ver46_Alpha5=(46,0,4209)；本批设置/目录仅放行 46.1+，46.0 留待完整 validator。
        if (version.Major < minimum || version.Major > 46 || version.Major == 46 && version.Minor > 3 || version.Build > ProfileImportFiles.BaselineBuild || version.Revision > 0 ||
            settings && version.Major == 46 && version.Minor == 0)
            return $"{name} 版本 {parts[1]} 的旧版本升级或未来兼容尚未完成，只允许预览。";
        return null;
    }
    /// <summary>在原 JSON 副本上复用明确的旧 Books 合并、UNC 根规范和日期更新；未知元数据留在兼容扩展。</summary>
    internal static JsonObject Upgrade(string name, JsonObject raw)
    {
        if (BlockReason(name, raw) is { } reason) throw new InvalidDataException(reason);
        if (name is not ("History.json" or "Bookmark.json")) return raw;
        var version = Version.Parse(raw["Format"]!.GetValue<string>().Split('/')[1]);
        var roots = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var targets = name == "History.json" ? (raw["Items"] as JsonArray)?.OfType<JsonObject>() ?? [] : Walk(raw["Nodes"] as JsonObject);
        foreach (var item in targets.Concat((raw["Books"] as JsonArray)?.OfType<JsonObject>() ?? []))
            if (item["Path"]?.GetValue<string>() is { } path) item["Path"] = NormalizeUnc(path, roots);
        if (name == "History.json" && (version.Major < 45 || version.Major == 45 && version.Minor == 0 && version.Build <= 3981) && raw["Items"] is JsonArray history)
        {
            var unique = history.OfType<JsonObject>().DistinctBy(n => n["Path"]?.GetValue<string>(), StringComparer.Ordinal).ToArray();
            if (unique.Length != history.Count)
            { raw["MacImportedLegacyItems"] = history.DeepClone(); raw["Items"] = new JsonArray(unique.Select(n => n.DeepClone()).ToArray()); }
        }
        if (raw["Books"] is JsonArray books && (name == "History.json" || name == "Bookmark.json" && (version.Major < 46 || version.Major == 46 && version.Minor == 0 && version.Build <= 4065)))
        {
            var map = new Dictionary<string, BookMemento>(StringComparer.Ordinal);
            foreach (var node in books)
            {
                var book = node?.Deserialize<BookMemento>(Options) ?? throw new InvalidDataException("旧 Books 包含无效记录。");
                if (string.IsNullOrWhiteSpace(book.Path) || !map.TryAdd(book.Path, book)) throw new InvalidDataException("旧 Books 缺少或重复 Path。");
            }
            var records = name == "History.json" ? (raw["Items"] as JsonArray)?.OfType<JsonObject>() ?? [] : Walk(raw["Nodes"] as JsonObject).Where(n => n["Children"] is null);
            foreach (var target in records)
                if (target["Path"]?.GetValue<string>() is { } path && map.TryGetValue(path, out var book))
                { target["Page"] = book.Page; target["Props"] = book.ToPropertiesString(); }
            // 原版移除活动 Books；保留原节点为兼容扩展，避免未知字段丢失或下次导入覆盖新进度。
            raw["MacImportedLegacyBooks"] = books.DeepClone(); raw.Remove("Books");
        }
        if (name == "Bookmark.json" && (version.Major < 46 || version.Major == 46 && version.Minor == 0 && version.Build <= 4065))
            foreach (var folder in Walk(raw["Nodes"] as JsonObject).Where(n => n["Children"] is JsonArray))
                folder["EntryTime"] = JsonSerializer.SerializeToNode(((JsonArray)folder["Children"]!).OfType<JsonObject>()
                    .Where(n => n["Children"] is null).Select(n => n["EntryTime"]?.GetValue<DateTime>() ?? default).DefaultIfEmpty().Max());
        raw["Format"] = name switch { "History.json" => "NeeView.History/46.3.0", "Bookmark.json" => "NeeView.Bookmark/46.3.0", _ => raw["Format"]!.GetValue<string>() };
        return raw;
    }
    internal static IEnumerable<JsonObject> Walk(JsonObject? node)
    { if (node is null) yield break; yield return node; foreach (var child in (node["Children"] as JsonArray)?.OfType<JsonObject>() ?? []) foreach (var item in Walk(child)) yield return item; }
    // 原 UncPathTools 只统一共享根的拼写，不能把整条路径转换成小写。
    private static string NormalizeUnc(string path, Dictionary<string, string> roots)
    {
        if (!path.StartsWith(@"\\", StringComparison.Ordinal)) return path;
        var parts = path.TrimStart('\\').Split('\\');
        if (parts.Length < 2) return path;
        var root = @"\\" + parts[0] + @"\" + parts[1];
        if (!roots.TryGetValue(root, out var normalized)) roots[root] = normalized = root;
        return normalized + path[root.Length..];
    }
    private static JsonSerializerOptions CreateOptions()
    { var options = new JsonSerializerOptions(); options.Converters.Add(new JsonStringEnumConverter()); return options; }
}
