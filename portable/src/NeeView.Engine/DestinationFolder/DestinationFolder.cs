// Copyright (c) NeeLaboratory. 原 DestinationFolder/Collection，基线 c5c398d89；Shell 操作分离至后端。
namespace NeeView;

/// <summary>原目标目录名称/路径及克隆语义，执行能力由移动服务接入。</summary>
public sealed class DestinationFolder : ICloneable, IEquatable<DestinationFolder>
{
    private string _name = "", _path = "";
    public DestinationFolder() { }
    public DestinationFolder(string name, string path) { Name = name; Path = path; }
    public string Name { get => string.IsNullOrWhiteSpace(_name) ? System.IO.Path.GetFileName(System.IO.Path.TrimEndingDirectorySeparator(_path)) : _name; set => _name = value?.Trim() ?? ""; }
    public string Path { get => _path; set => _path = value?.Trim() ?? ""; }
    public bool IsValid() => !string.IsNullOrWhiteSpace(_path);
    public object Clone() => MemberwiseClone();
    public bool Equals(DestinationFolder? other) => other is not null && _name == other._name && _path == other._path;
    public override bool Equals(object? obj) => Equals(obj as DestinationFolder);
    public override int GetHashCode() => HashCode.Combine(_name, _path);
}

/// <summary>原无限手动集合；数字命令只访问前九项，不限制面板数量。</summary>
public sealed class DestinationFolderCollection : List<DestinationFolder>
{
    public DestinationFolderCollection() { }
    public DestinationFolderCollection(IEnumerable<DestinationFolder> collection) : base(collection) { }
    public DestinationFolder CreateNew() { var item = new DestinationFolder(); Add(item); return item; }
    public bool IsValidIndex(int index) => index >= 0 && index < Count;
}
