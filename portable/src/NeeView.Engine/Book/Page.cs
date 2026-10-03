// Copyright (c) NeeLaboratory. 原源码关系与业务规则沿用仓库 MIT；P1子集适配见 docs/source-migration.json。
namespace NeeView;

/// <summary>原 Page 的 P1 内容部分；缩略图、媒体及标记依照迁移表后续接入。</summary>
public sealed class Page(ArchiveEntry entry)
{
    public ArchiveEntry ArchiveEntry { get; } = entry;
    public int Index { get; internal set; }
    public int EntryIndex => ArchiveEntry.Id;
    public string EntryName => ArchiveEntry.EntryName;
    public string EntryFullName => ArchiveEntry.SystemPath;
    public bool IsMarked { get; internal set; }
    public int PageType => ArchiveEntry.IsDirectory ? 1 : 0;
    public PageContent Content { get; } = new();
    /// <summary>供原 PageComparer 比较归档目录和文件名，逻辑路径使用 '/'。</summary>
    public string[] GetEntryNameTokens() => EntryName.Split('/');
    public override string ToString() => EntryName;
}

/// <summary>原内容数据入口，尺寸探测不持有解码像素。</summary>
public sealed class PageContent
{
    public PageDataSource PageDataSource { get; internal set; } = new(new Size(1000, 1500));
    public bool HasSize { get; internal set; }
    public bool IsFileContent => false;
    public string? Error { get; internal set; }
}
/// <summary>移除 WPF 图像后保留页面尺寸数据。</summary>
public sealed record PageDataSource(Size Size);
