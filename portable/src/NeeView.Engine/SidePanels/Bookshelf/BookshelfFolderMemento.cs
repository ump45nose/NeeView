// Copyright (c) NeeLaboratory. 原BookshelfFolderMemento的JSON值与参数登记关系。
namespace NeeView;
/// <summary>原启动列表位置，Select是目标路径而不是数组下标。</summary>
public sealed record BookshelfFolderMemento
{
    public string Path { get; init; } = "";
    public string? Select { get; init; }
    public FolderOrder? FolderOrder { get; init; }
    public bool? IsFolderRecursive { get; init; }
    public int Seed { get; init; }
    public void Register(FolderConfigCollection folders) => folders.SetFolderParameter(Path, new()
        { FolderOrder = FolderOrder, IsFolderRecursive = IsFolderRecursive, Seed = Seed });
    /// <summary>沿原字段创建目录参数快照，不保存窗口/控件引用。</summary>
    public static BookshelfFolderMemento Create(string path, string? selected, FolderConfigCollection folders)
    {
        var parameter = new FolderParameter(path, folders);
        return new() { Path = path, Select = selected, FolderOrder = parameter.FolderOrder, IsFolderRecursive = parameter.IsFolderRecursive, Seed = parameter.Seed };
    }
}
