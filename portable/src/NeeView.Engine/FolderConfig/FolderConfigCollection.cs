// Copyright (c) NeeLaboratory. 原 FolderConfigCollection 的参数/继承/CreateMemento 子集适配。
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
namespace NeeView;

/// <summary>原 Foldres.json 的唯一目录参数集合；缩略配置及未来字段只读保留。</summary>
public sealed class FolderConfigCollection
{
    // 保留原 SaveDataProfile 的特殊拼写，旧 Profile 也使用此文件名。
    public const string FileName = "Foldres.json";
    private static readonly JsonSerializerOptions Options = CreateOptions();
    private JsonObject _root = new();
    private Dictionary<string, JsonObject> _folders = new(StringComparer.Ordinal);
    /// <summary>解析原 Folders 数组，保留根、条目、缩略图和未知参数字段；重复路径拒绝加载。</summary>
    public void Restore(JsonObject root)
    {
        if (root["Folders"] is not null and not JsonArray) throw new JsonException("Folders 必须是数组。");
        if (root["Folders"] is JsonArray array && array.Any(e => e is not JsonObject)) throw new JsonException("Folders 包含无效目录记录。");
        var folders = new Dictionary<string, JsonObject>(StringComparer.Ordinal);
        foreach (var unit in (root["Folders"] as JsonArray)?.OfType<JsonObject>() ?? [])
        {
            var place = unit["Place"]?.GetValue<string>() ?? throw new JsonException("Folders.Place 缺失。");
            // 提前检查参数，损坏原文件不能被稍后保存静默覆盖。
            _ = unit["Parameter"]?.Deserialize<FolderParameterMemento>(Options);
            if (!folders.TryAdd(place, unit.DeepClone().AsObject())) throw new JsonException("重复目录参数：" + place);
        }
        _root = root.DeepClone().AsObject(); _folders = folders;
    }
    /// <summary>读取精确路径记录；路径不统一转换大小写，不扫描或猜测外部移动。</summary>
    public FolderParameterMemento GetFolderParameter(string path) => _folders.GetValueOrDefault(path)?["Parameter"]?.Deserialize<FolderParameterMemento>(Options) ?? new();
    /// <summary>登记原参数并保留未迁字段；默认省略仅影响已识别字段。</summary>
    public void SetFolderParameter(string path, FolderParameterMemento parameter)
    {
        var old = GetFolderParameter(path);
        var normalized = (parameter with { ExtensionData = old.ExtensionData }).Normalize(path);
        if (!_folders.TryGetValue(path, out var unit)) _folders[path] = unit = new() { ["Place"] = path };
        unit["Parameter"] = normalized.IsDefault ? null : JsonSerializer.SerializeToNode(normalized);
    }
    /// <summary>原递归默认取最近祖先的显式值，未登记的中间层不阻止继承。</summary>
    public bool GetDefaultFolderRecursive(string path)
    {
        // 原QueryPath的虚拟书签父级仍使用逻辑分隔符，不交给Mac文件系统。
        if (path.StartsWith("bookmark:", StringComparison.Ordinal))
        {
            var logical = path["bookmark:".Length..].Replace('/', '\\').Trim('\\');
            while (logical.LastIndexOf('\\') is var separator && separator > 0)
            {
                logical = logical[..separator];
                if (GetFolderParameter("bookmark:" + logical).IsFolderRecursive is { } value) return value;
            }
            return false;
        }
        if (!System.IO.Path.IsPathFullyQualified(path)) return false;
        for (var parent = System.IO.Path.GetDirectoryName(System.IO.Path.TrimEndingDirectorySeparator(path)); parent is not null;
            parent = System.IO.Path.GetDirectoryName(parent))
            if (GetFolderParameter(parent).IsFolderRecursive is { } recursive) return recursive;
        return false;
    }
    /// <summary>生成原 Folders 文件副本；关闭保留仅移除已识别参数，未知和缩略配置不丢失。</summary>
    /// <param name="forSave">false生成完整运行快照供失败回滚，true应用原保存开关/默认归一。</param>
    public JsonObject CreateMemento(bool forSave = true)
    {
        var root = _root.DeepClone().AsObject(); if (forSave) root["Format"] ??= "NeeView.Folders/46.3.0";
        var units = new JsonArray();
        foreach (var pair in _folders.OrderBy(e => e.Key, StringComparer.Ordinal))
        {
            var unit = pair.Value.DeepClone().AsObject();
            var parameter = GetFolderParameter(pair.Key);
            if (forSave)
            {
                parameter = Config.Current.History.IsKeepFolderStatus ? parameter.Normalize(pair.Key)
                    : new() { ExtensionData = parameter.ExtensionData };
                unit["Parameter"] = parameter.IsDefault ? null : JsonSerializer.SerializeToNode(parameter);
            }
            if (forSave && unit["Parameter"] is null) unit.Remove("Parameter");
            if (forSave && (unit["Thumbs"] is JsonObject { Count: 0 } || unit["Thumbs"] is null)) unit.Remove("Thumbs");
            if (forSave && unit.Count == 1 && unit.ContainsKey("Place")) continue;
            units.Add(unit);
        }
        root["Folders"] = units; return root;
    }
    /// <summary>兼容原数值与字符串枚举；序列化继续使用原数值格式。</summary>
    private static JsonSerializerOptions CreateOptions()
    {
        var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        options.Converters.Add(new JsonStringEnumConverter()); return options;
    }
}
