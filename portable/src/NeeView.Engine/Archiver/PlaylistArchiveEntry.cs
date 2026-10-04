// Copyright (c) NeeLaboratory. 原PlaylistArchiveEntry代理关系，基线c5c398d89。
namespace NeeView;

/// <summary>原列表显示名/连续Id归列表，真实读取/类型/定位归InnerEntry。</summary>
public sealed class PlaylistArchiveEntry : ArchiveEntry
{
    /// <summary>构造列表代理，不把别名当作图片扩展或物理文件路径。</summary>
    /// <param name="archive">拥有代理的列表来源。</param><param name="inner">列表来源持有的实际条目。</param>
    /// <param name="id">成功解析项的连续编号。</param><param name="name">原Name或默认叶文件名。</param>
    public PlaylistArchiveEntry(Archive archive, ArchiveEntry inner, int id, string name) : base(archive)
    {
        InnerEntry = inner; Id = id; RawEntryName = name; Length = inner.Length; LastWriteTime = inner.LastWriteTime;
        IsDirectory = inner.IsDirectory; IsShortcut = inner.IsShortcut; FilePath = inner.FilePath;
    }
    public ArchiveEntry InnerEntry { get; }
    public override ArchiveEntry TargetArchiveEntry => InnerEntry.TargetArchiveEntry;
    public override string SystemPath => InnerEntry.SystemPath;
}
