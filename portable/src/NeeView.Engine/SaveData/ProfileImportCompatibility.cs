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
    /// <param name="name">原五文件名。</param><param name="raw">未升级的候选。</param>
    /// <param name="schema">显式来源确认；默认不推断fork版本。</param><returns>null 表示本批允许应用。</returns>
    internal static string? BlockReason(string name, JsonObject raw, ProfileImportSchema schema = ProfileImportSchema.DeclaredVersion)
    {
        var format = raw["Format"]?.GetValue<string>() ?? "";
        var parts = format.Split('/');
        string expected = name switch { "UserSetting.json" => "NeeView", "History.json" => "NeeView.History", "Bookmark.json" => "NeeView.Bookmark", "Foldres.json" => "NeeView.Folders", _ => "NeeView.QuickAccess" };
        if (parts.Length != 2 || !(parts[0] == expected || parts[0] == "NeeView" || name == "UserSetting.json" && parts[0] == "NeeView.UserSetting") || !Version.TryParse(parts[1], out var version))
            return name + " 版本缺失或格式类型不匹配，只允许预览。";
        if (!Enum.IsDefined(schema)) return "未识别的来源格式选择，只允许预览。";
        if (schema == ProfileImportSchema.ConfirmedNeeView46_3Fork && version == new Version(1, 0, 0))
        {
            // 不能从字段或 MacImportedSourceFormat 猜测来源；仅显式确认的 fork 使用已核验的现代 schema。
            if (!(parts[0] == expected || name == "UserSetting.json" && parts[0] == "NeeView.UserSetting"))
                return name + " fork 格式类型不匹配，只允许预览。";
            bool valid = name switch
            {
                "UserSetting.json" => raw["Config"] is JsonObject && (!raw.ContainsKey("Commands") || raw["Commands"] is JsonObject),
                "History.json" => raw["Items"] is JsonArray,
                "Bookmark.json" => raw["Nodes"] is JsonObject,
                "Foldres.json" => raw["Folders"] is JsonArray,
                "QuicAccess.json" => raw["Items"] is JsonArray,
                _ => false
            };
            return valid ? null : name + " 与已确认的46.3 fork数据结构不符，只允许预览。";
        }
        int minimum = name == "UserSetting.json" ? 38 : name == "Foldres.json" ? 46 : 44;
        // 独立 Folders 仅核对过46数组格式；QuickAccess沿原树格式及无版本分支的validator。
        if (version.Major < minimum || version.Major > 46 || version.Major == 46 && version.Minor > 3 || version.Build > ProfileImportFiles.BaselineBuild || version.Revision > 0)
            return $"{name} 版本 {parts[1]} 的旧版本升级或未来兼容尚未完成，只允许预览。";
        // <39 的原字体缩放依赖来源 Windows MessageFontSize，不能猜测为当前 Mac 字体。
        if (name == "UserSetting.json" && version < new Version(39, 0, 0) && raw["Config"]?["Panels"] is JsonObject panels &&
            new[] { "FontSize", "FolderTreeFontSize" }.Any(field => panels[field] is { } value && value.GetValue<double>() != 0))
            return "UserSetting.json 旧字体尺寸需要来源 Windows 的 MessageFontSize，暂只允许预览；原值保留。";
        return null;
    }
    /// <summary>在原 JSON 副本上复用明确的旧 Books 合并、UNC 根规范和日期更新；未知元数据留在兼容扩展。</summary>
    /// <param name="name">原文件名。</param><param name="raw">独立候选副本。</param>
    /// <param name="schema">只读来源的明确schema选择。</param><returns>原版本标识留在元数据的现代候选。</returns>
    internal static JsonObject Upgrade(string name, JsonObject raw, ProfileImportSchema schema = ProfileImportSchema.DeclaredVersion)
    {
        if (BlockReason(name, raw, schema) is { } reason) throw new InvalidDataException(reason);
        var version = Version.Parse(raw["Format"]!.GetValue<string>().Split('/')[1]);
        if (schema == ProfileImportSchema.ConfirmedNeeView46_3Fork && version == new Version(1, 0, 0))
        {
            // 原独立文件优先，旧Bookmark内嵌后备仍单独校验；确认外层不放行未知内嵌格式。
            if (name == "Bookmark.json" && raw["QuickAccess"] is JsonObject embedded)
            {
                var quick = embedded.DeepClone().AsObject();
                if (!quick.ContainsKey("Format")) quick["Format"] = "NeeView.QuickAccess/1.0.0";
                if (BlockReason("QuicAccess.json", quick, schema) is null)
                    raw["QuickAccess"] = Upgrade("QuicAccess.json", quick, schema);
            }
            raw["MacImportedSourceFormat"] ??= raw["Format"]!.DeepClone();
            raw["MacImportedSourceSchema"] = "NeeView/46.3." + ProfileImportFiles.BaselineBuild;
            version = new Version(46, 3, ProfileImportFiles.BaselineBuild);
            // 候选规范化，应用时再次验证现代版本；原标识作为元数据保留，源文件保持只读。
            raw["Format"] = raw["Format"]!.GetValue<string>().Split('/')[0] + "/" + version;
        }
        if (name == "UserSetting.json") { LegacyUserSettingUpgrade.Upgrade(raw, version); return raw; }
        if (name == "Foldres.json") { LegacyFolderConfigUpgrade.Upgrade(raw, version); return raw; }
        if (name == "QuicAccess.json")
        { raw["MacImportedSourceFormat"] ??= raw["Format"]!.DeepClone(); raw["Format"] = "NeeView.QuickAccess/46.3.0"; return raw; }
        if (name is not ("History.json" or "Bookmark.json")) return raw;
        raw["MacImportedSourceFormat"] ??= raw["Format"]!.DeepClone();
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
