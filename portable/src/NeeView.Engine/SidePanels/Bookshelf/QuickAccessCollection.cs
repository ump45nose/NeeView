// Copyright (c) NeeLaboratory. 原QuickAccessCollection/QuickAccessTreeNode的树、名称与JSON规则，移除WPF宿主。
using System.Collections.ObjectModel;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
namespace NeeView;

/// <summary>原Name/Path/Children节点即权威数据，不另建身份或书签树。</summary>
public sealed class QuickAccessTreeNode
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? Name { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? Path { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public ObservableCollection<QuickAccessTreeNode>? Children { get; set; }
    [JsonExtensionData] public Dictionary<string, JsonElement>? Extra { get; set; }
    [JsonIgnore] public bool IsFolder => Children is not null;
    [JsonIgnore] public string DisplayName => Name ?? (Path?.StartsWith("bookmark:", StringComparison.Ordinal) == true ? "书签" : System.IO.Path.GetFileName(Path?.TrimEnd('/')) is { Length: > 0 } name ? name : Path ?? "");
    /// <summary>仅遍历已有轻量节点，不访问文件系统。</summary>
    public IEnumerable<QuickAccessTreeNode> Walk() { yield return this; foreach (var child in Children ?? []) foreach (var node in child.Walk()) yield return node; }
}

/// <summary>原快速访问集合的小范围树适配，文件名保留原QuicAccess拼写。</summary>
public sealed class QuickAccessCollection
{
    public const string FileName = "QuicAccess.json";
    public QuickAccessTreeNode Root { get; } = new() { Name = "快速访问", Children = [] };
    /// <summary>原父子关系查找，拒绝已删除节点。</summary>
    public QuickAccessTreeNode? ParentOf(QuickAccessTreeNode node) => Root.Walk().FirstOrDefault(parent => parent.Children?.Contains(node) == true);
    /// <summary>沿原quickaccess逻辑路径按名称查找；普通目标路径不当作树身份。</summary>
    public QuickAccessTreeNode? FindNode(string path)
    {
        if (!path.StartsWith("quickaccess:", StringComparison.Ordinal)) return null;
        var node = Root;
        foreach (var name in path[12..].Trim('/').Split('/', StringSplitOptions.RemoveEmptyEntries))
        { node = node.Children?.FirstOrDefault(child => child.DisplayName == name); if (node is null) return null; }
        return node;
    }
    public string GetPath(QuickAccessTreeNode node) => ReferenceEquals(node, Root) ? "quickaccess:" :
        ParentOf(node) is { } parent ? GetPath(parent).TrimEnd('/') + "/" + node.DisplayName : throw new InvalidOperationException("快速访问节点已不存在。");
    /// <summary>沿原文件夹名称去重规则，新目录默认“新建文件夹”，同名递增(2)。</summary>
    public QuickAccessTreeNode AddFolder(QuickAccessTreeNode parent, string? name) => Insert(parent, new() { Name = UniqueName(parent, ValidateName(name) is { Length: > 0 } value ? value : "新建文件夹"), Children = [] });
    /// <summary>登记原真实/虚拟目标，默认名省略保存，不限制条目数量。</summary>
    public QuickAccessTreeNode Add(QuickAccessTreeNode parent, string path, string? name = null)
    { if (string.IsNullOrWhiteSpace(path)) throw new ArgumentException("目标路径不能为空。", nameof(path)); return Insert(parent, new() { Path = path, Name = string.IsNullOrWhiteSpace(name) ? null : ValidateName(name) }); }
    private QuickAccessTreeNode Insert(QuickAccessTreeNode parent, QuickAccessTreeNode node)
    { if (!Root.Walk().Contains(parent) || parent.Children is null) throw new InvalidOperationException("请选择快速访问文件夹。"); parent.Children.Add(node); return node; }
    /// <summary>原名称仅去除路径分隔符；根不可编辑，叶项默认名写为null。</summary>
    public void Rename(QuickAccessTreeNode node, string name)
    {
        if (ReferenceEquals(node, Root) || !Root.Walk().Contains(node)) throw new InvalidOperationException("此节点不能重命名。");
        name = ValidateName(name); if (node.IsFolder && name.Length == 0) return;
        node.Name = name.Length == 0 ? null : name;
    }
    public void Remove(QuickAccessTreeNode node) => (ParentOf(node)?.Children ?? throw new InvalidOperationException("根节点不能删除。")).Remove(node);
    /// <summary>原delta=0移入文件夹，负/正值插入前/后；禁止移入自身后代。</summary>
    public void Move(QuickAccessTreeNode node, QuickAccessTreeNode target, int delta)
    {
        if (ReferenceEquals(node, target)) return;
        var source = ParentOf(node) ?? throw new InvalidOperationException("根节点不能移动。");
        var parent = delta == 0 ? target : ParentOf(target);
        if (parent?.Children is null || !Root.Walk().Contains(parent) || node.Walk().Contains(parent)) throw new InvalidOperationException("不能移动到此位置。");
        source.Children!.Remove(node);
        int index = delta == 0 ? parent.Children.Count : parent.Children.IndexOf(target) + (delta > 0 ? 1 : 0);
        parent.Children.Insert(Math.Clamp(index, 0, parent.Children.Count), node);
    }
    public static string ValidateName(string? name) => (name ?? "").Trim().Replace('/', '_').Replace('\\', '_');
    private static string UniqueName(QuickAccessTreeNode parent, string name)
    { var names = parent.Children?.Select(child => child.DisplayName).ToHashSet() ?? []; var value = name; for (int n = 2; names.Contains(value); n++) value = $"{name} ({n})"; return value; }
    /// <summary>保留原格式及未知字段；空白叶目标按原转换规则忽略。</summary>
    public void Restore(JsonObject raw)
    {
        if (raw["Format"] is JsonValue format && !(format.GetValue<string>().StartsWith("NeeView.QuickAccess", StringComparison.Ordinal))) throw new JsonException("不是NeeView快速访问文件。");
        Root.Children!.Clear();
        foreach (var node in raw["Items"]?.Deserialize<QuickAccessTreeNode[]>() ?? []) { Clean(node); if (node.IsFolder || !string.IsNullOrWhiteSpace(node.Path)) Root.Children.Add(node); }
        static void Clean(QuickAccessTreeNode node) { if (node.Children is null) return; foreach (var child in node.Children.ToArray()) { Clean(child); if (!child.IsFolder && string.IsNullOrWhiteSpace(child.Path)) node.Children.Remove(child); } }
    }
    /// <summary>将同一权威节点序列写回原Items，顶层及逐节点未知字段继续保留。</summary>
    public JsonObject CreateMemento(JsonObject raw)
    { var result = raw.DeepClone().AsObject(); result["Format"] ??= "NeeView.QuickAccess/46.3.0"; result["Items"] = JsonSerializer.SerializeToNode(Root.Children); return result; }
}

/// <summary>原QuickAccessDirectoryNode表现适配，包装相同权威节点，不拥有另一套保存树。</summary>
public sealed class QuickAccessDirectoryNode : FolderTreeNodeBase
{
    private readonly QuickAccessCollection _collection;
    public QuickAccessTreeNode Value { get; }
    public QuickAccessDirectoryNode(QuickAccessCollection collection, QuickAccessTreeNode value, FolderTreeNodeBase? parent = null)
    { _collection = collection; Value = value; Parent = parent; _children = new((value.Children ?? []).Select(child => new QuickAccessDirectoryNode(collection, child, this))); }
    public override string Name => Value.DisplayName;
    public override string Path => Value.IsFolder ? _collection.GetPath(Value) : Value.Path ?? "";
}
