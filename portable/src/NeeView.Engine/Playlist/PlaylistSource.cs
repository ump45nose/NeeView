// Copyright (c) NeeLaboratory. 迁自原 PlaylistSource/PlaylistSourceItem；见 source-migration.json。
using System.Text.Json;
using System.Text.Json.Serialization;
namespace NeeView;

/// <summary>原全局 .nvpls 源；保留未知字段，不把标记写成每本书的私有状态。</summary>
public sealed class PlaylistSource
{
    public const string CurrentFormat = "NeeView.Playlist/2.0.1";
    public string Format { get; set; } = CurrentFormat;
    public List<PlaylistSourceItem> Items { get; set; } = [];
    [JsonExtensionData] public Dictionary<string, JsonElement>? ExtensionData { get; set; }
}

/// <summary>原 Path/Name/Invalid 字段与名称省略规则；逻辑归档路径从不指向解压缓存。</summary>
public sealed class PlaylistSourceItem
{
    private string _path = "";
    private string? _name;
    public string Path { get => _path; set { if (_path != value) { _path = value; Invalid = false; } } }
    [JsonIgnore] public string Name { get => _name ?? System.IO.Path.GetFileName(Path); set => _name = string.IsNullOrEmpty(value) || value.Trim() == System.IO.Path.GetFileName(Path) ? null : value.Trim(); }
    [JsonPropertyName("Name"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? NameRaw { get => _name; set => _name = value; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] public bool Invalid { get; set; }
    [JsonExtensionData] public Dictionary<string, JsonElement>? ExtensionData { get; set; }
}

/// <summary>原 PlaylistItem 的业务子集；显示位置由加载时后台计算，不在绘制时访问文件。</summary>
public sealed class PlaylistItem(PlaylistSourceItem source)
{
    public PlaylistSourceItem Source { get; } = source;
    public string Path => Source.Path;
    public string Name { get => Source.Name; set => Source.Name = value; }
    public string Place { get; internal set; } = "";
}

/// <summary>原删除时的即时索引和条目引用，恢复按逆序插回。</summary>
public sealed record PlaylistItemMemento(PlaylistItem Item, int Index);

/// <summary>原 Playlist 集合编辑子集；磁盘提交与回滚由 PlaylistHub 串行协调。</summary>
public sealed class Playlist(string path, PlaylistSource source)
{
    private readonly List<PlaylistItem> _items = source.Items.Select(item => new PlaylistItem(item)).ToList();
    public string Path { get; internal set; } = path;
    public PlaylistSource Source { get; } = source;
    public IReadOnlyList<PlaylistItem> Items => _items;
    internal List<PlaylistItemMemento> Removed { get; set; } = [];
    public bool CanRestore => Removed.Count > 0;
    /// <summary>按原 Path 查重；重复注册返回原对象，不替换名称和未知字段。</summary>
    internal PlaylistItem Insert(int index, PlaylistItem item)
    {
        var exists = _items.FirstOrDefault(entry => entry.Path == item.Path);
        if (exists is not null) return exists;
        _items.Insert(Math.Clamp(index, 0, _items.Count), item); return item;
    }
    /// <summary>迁自 RemoveWithRecoverable：记录每次删除后的索引，逆序恢复多选批次。</summary>
    internal void Remove(IEnumerable<PlaylistItem> items)
    {
        var removed = new List<PlaylistItemMemento>();
        foreach (var item in items.Distinct())
        { int index = _items.IndexOf(item); if (index >= 0) { _items.RemoveAt(index); removed.Add(new(item, index)); } }
        if (removed.Count > 0) { removed.Reverse(); Removed = removed; }
    }
    /// <summary>原 Restore 插入规则保留去重；只恢复最近一个成功删除批次。</summary>
    internal void Restore() { foreach (var item in Removed) Insert(item.Index, item.Item); Removed = []; }
    /// <summary>原 Move 的目标索引语义；null 目标移动到末项。</summary>
    internal void Move(PlaylistItem item, PlaylistItem? target)
    {
        int oldIndex = _items.IndexOf(item), newIndex = target is null ? _items.Count - 1 : _items.IndexOf(target);
        if (oldIndex < 0 || newIndex < 0 || oldIndex == newIndex) return;
        _items.RemoveAt(oldIndex); _items.Insert(newIndex, item);
    }
    /// <summary>原 Sort 按 Path 自然排序，不按用户别名排序。</summary>
    internal void Sort() => Replace(_items.OrderBy(item => item.Path, NaturalSort.Comparer).ToArray());
    /// <summary>提交或回滚保持条目身份，避免列表选择指向被替换的副本。</summary>
    internal void Replace(IEnumerable<PlaylistItem> items) { var copy = items.ToArray(); _items.Clear(); _items.AddRange(copy); }
}
