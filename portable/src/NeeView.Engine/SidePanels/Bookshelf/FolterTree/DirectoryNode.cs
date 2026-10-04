// Copyright (c) NeeLaboratory. 原 DirectoryNode 的父链路径、直接子目录和自然排序；系统图标由表现层替代。
namespace NeeView;

/// <summary>普通文件系统目录节点；不把压缩包内部地址当作真实目录。</summary>
public sealed class DirectoryNode : FolderTreeNodeDelayBase
{
    private readonly IArchiveFactory _archives;
    /// <summary>只登记父链和枚举契约；构造子节点不会扫描其目录。</summary>
    /// <param name="name">根节点为实际绝对路径，子节点为来源返回的名称。</param>
    /// <param name="parent">所属树的父节点；根节点为空。</param>
    /// <param name="archives">有界后台目录枚举的既有来源契约。</param>
    public DirectoryNode(string name, FolderTreeNodeBase? parent, IArchiveFactory archives) { Name = name; Parent = parent; _archives = archives; }
    public override string Name { get; }
    public override string Path => Parent is DirectoryNode directory ? System.IO.Path.Combine(directory.Path, Name) : Name;
    public override string DisplayName => Parent is null && Name == "/" ? "Mac" : Parent is null && Name == "/Volumes" ? "挂载卷" : Name;
    /// <summary>只调用有界后台直接子目录枚举；不探测每个子目录是否又有子目录。</summary>
    /// <param name="token">折叠、刷新、切换同步或关闭取消。</param>
    /// <returns>自然名称顺序的延迟节点，不递归创建后代。</returns>
    protected override async Task<IReadOnlyList<FolderTreeNodeBase>> LoadChildrenAsync(CancellationToken token)
    {
        var entries = await _archives.ListFoldersAsync(Path, token); token.ThrowIfCancellationRequested();
        return entries.Where(e => e.IsDirectory).OrderBy(e => e.Name, NaturalSort.Comparer)
            .Select(e => (FolderTreeNodeBase)new DirectoryNode(e.Name, this, _archives)).ToArray();
    }
}
