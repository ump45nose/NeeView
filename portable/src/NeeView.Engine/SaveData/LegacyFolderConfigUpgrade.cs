// Copyright (c) NeeLaboratory. 原 FolderConfigCollectionValidator / FolderConfigUnit.Validate / Restore(Dictionary) 适配。
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
namespace NeeView;

/// <summary>只升级原目录 JSON 候选；不访问文件系统、不修改 Config.Current。</summary>
internal static class LegacyFolderConfigUpgrade
{
    internal const string NormalizeOrderField = "MacImportedFolderOrderNormalization";
    private static readonly JsonSerializerOptions Options = new() { Converters = { new JsonStringEnumConverter() } };

    /// <summary>按原列表顺序升级独立文件的旧递归继承，默认排序等最终配置确定后再归一。</summary>
    /// <param name="raw">独立 Foldres 文件副本。</param><param name="version">原来源版本。</param>
    internal static void Upgrade(JsonObject raw, Version version)
    {
        var folders = Units(raw).ToArray();
        if (version <= new Version(46, 0, 4065))
        {
            raw[NormalizeOrderField] = true;
            foreach (var folder in folders)
                if (folder["Thumbs"] is JsonObject { Count: 0 }) folder["Thumbs"] = null;
        }
        if (version <= new Version(46, 0, 4209))
        {
            // 原 FirstOrDefault 精确匹配首项；索引仍指向正在逐项修改的原节点，不能拍参数快照。
            var places = new Dictionary<string, JsonObject>(StringComparer.Ordinal);
            foreach (var folder in folders) places.TryAdd(folder["Place"]!.GetValue<string>(), folder);
            foreach (var folder in folders)
            {
                if (folder["Parameter"] is not JsonObject parameter) continue;
                var currentRecursive = parameter["IsFolderRecursive"]?.GetValue<bool>() ?? false;
                var defaultRecursive = Parents(folder["Place"]!.GetValue<string>())
                    .Any(parent => places.GetValueOrDefault(parent)?["Parameter"]?["IsFolderRecursive"]?.GetValue<bool>() == true);
                // 原转换特例：旧 false 无效。此规则只用于旧独立文件，不用于 History.Folders 字典。
                if (currentRecursive == defaultRecursive || !currentRecursive) parameter["IsFolderRecursive"] = null;
            }
        }
        raw["MacImportedSourceFormat"] ??= raw["Format"]!.DeepClone();
        raw["Format"] = "NeeView.Folders/46.3.0";
    }

    /// <summary>沿原 Restore(Dictionary) 生成当前集合；旧字典没有独立文件的递归 validator。</summary>
    /// <param name="history">已校验版本的历史候选，来源版本仍保留。</param><returns>新集合候选，不修改内嵌原数据。</returns>
    internal static JsonObject FromHistory(JsonObject history)
    {
        var folders = history["Folders"]!.AsObject();
        return new()
        {
            ["Format"] = "NeeView.Folders/46.3.0",
            ["MacImportedSourceFormat"] = (history["MacImportedSourceFormat"] ?? history["Format"])?.DeepClone(),
            [NormalizeOrderField] = true,
            ["Folders"] = new JsonArray(folders.Select(pair => (JsonNode)new JsonObject
                { ["Place"] = pair.Key, ["Parameter"] = pair.Value?.DeepClone() }).ToArray())
        };
    }

    /// <summary>用实际选项合并后的默认值执行原 FolderOrder 归一；验证及提交使用同一候选。</summary>
    /// <param name="raw">已经递归升级或从旧字典转换的候选。</param><param name="config">事务中的独立最终配置。</param>
    internal static void NormalizeOrders(JsonObject raw, Config config)
    {
        if (raw[NormalizeOrderField]?.GetValue<bool>() != true) return;
        foreach (var folder in Units(raw))
        {
            if (folder["Parameter"] is not JsonObject parameter || parameter["FolderOrder"] is null) continue;
            var path = folder["Place"]!.GetValue<string>();
            var defaultOrder = path.StartsWith("bookmark:", StringComparison.OrdinalIgnoreCase) ? config.Bookmark.BookmarkFolderOrder
                : path.EndsWith(".nvpls", StringComparison.OrdinalIgnoreCase) ? config.Bookshelf.PlaylistFolderOrder : config.Bookshelf.DefaultFolderOrder;
            if (parameter["FolderOrder"]!.Deserialize<FolderOrder>(Options) == defaultOrder) parameter["FolderOrder"] = null;
        }
        // 仅消费本次准备标记。已应用文件再导入时不按另一套默认值重新执行旧升级。
        raw.Remove(NormalizeOrderField);
    }

    /// <summary>检查真实 Folders 数组形状，避免旧字典或损坏节点被当作空集合写出。</summary>
    private static IEnumerable<JsonObject> Units(JsonObject raw)
    {
        if (raw["Folders"] is null) yield break;
        if (raw["Folders"] is not JsonArray folders) throw new InvalidDataException("Foldres.Folders 必须是数组。");
        foreach (var node in folders)
        {
            if (node is not JsonObject folder || folder["Place"] is not JsonValue value || !value.TryGetValue<string>(out _))
                throw new InvalidDataException("Foldres.Folders 包含无效 Place。");
            if (folder["Parameter"] is not null and not JsonObject) throw new InvalidDataException("Foldres.Parameter 必须是对象。");
            yield return folder;
        }
    }

    /// <summary>原 QueryPath.GetParent/SimplePath 的逻辑子集；在 Mac 上也能处理尚未映射的盘符、UNC 和虚拟路径。</summary>
    /// <param name="source">原 Place，包括可选 search 查询。</param><returns>最近祖先优先；匹配不统一大小写。</returns>
    private static IEnumerable<string> Parents(string source)
    {
        var search = source.IndexOf("?search=", StringComparison.Ordinal);
        if (search >= 0) source = source[..search];
        var colon = source.IndexOf(':'); var scheme = colon > 1 ? source[..(colon + 1)] : "";
        var path = scheme.Length > 0 ? source[scheme.Length..] : source;
        bool logical = scheme.Length > 0 && scheme != "file:";
        bool mac = !logical && path.StartsWith('/') && !path.StartsWith("//", StringComparison.Ordinal);
        if (mac)
        {
            for (var parent = System.IO.Path.GetDirectoryName(System.IO.Path.TrimEndingDirectorySeparator(path)); parent is not null;
                parent = System.IO.Path.GetDirectoryName(parent)) yield return parent;
            yield break;
        }
        path = path.Replace('/', '\\').Trim().TrimEnd('\\');
        if (logical) path = path.TrimStart('\\');
        else if (path.Length >= 2 && path[1] == ':') path = char.ToUpperInvariant(path[0]) + path[1..];
        var head = new string('\\', path.TakeWhile(c => c == '\\').Count());
        var parts = path.Split('\\', StringSplitOptions.RemoveEmptyEntries).ToList();
        while (parts.Count > 1)
        {
            parts.RemoveAt(parts.Count - 1);
            var parent = head + string.Join('\\', parts);
            if (parts.Count == 1 && parent.EndsWith(':')) parent += '\\';
            yield return (logical ? scheme : "") + parent;
        }
    }
}
