// Copyright (c) NeeLaboratory. 原源码关系与业务规则沿用仓库 MIT；P1子集适配见 docs/source-migration.json。
namespace NeeView;

/// <summary>原 Page 的 P1 内容部分；缩略图、媒体及标记依照迁移表后续接入。</summary>
public sealed class Page(ArchiveEntry entry, string? entryName = null, IArchiveFactory? archives = null, FolderConfigCollection? folders = null)
{
    public ArchiveEntry ArchiveEntry { get; } = entry;
    public int Index { get; internal set; }
    public int EntryIndex => ArchiveEntry.Id;
    public string EntryName => entryName ?? ArchiveEntry.EntryName.TrimEnd('/');
    public string EntryFullName => ArchiveEntry.SystemPath;
    public bool IsMarked { get; internal set; }
    public PageType PageType => ArchiveEntry.IsDirectory ? PageType.Folder : ArchiveEntry.IsBook() ? PageType.Archive : PageType.File;
    public bool IsImage => ArchiveEntry.IsImage();
    public PageContent Content { get; } = new(!entry.IsImage(), archives, folders);
    /// <summary>供原 PageComparer 比较归档目录和文件名，逻辑路径使用 '/'。</summary>
    public string[] GetEntryNameTokens() => EntryName.Split('/');
    public override string ToString() => EntryName;
}

/// <summary>原内容数据入口，尺寸探测不持有解码像素。</summary>
public sealed class PageContent(bool isBook = false, IArchiveFactory? archives = null, FolderConfigCollection? folders = null)
{
    public PageDataSource PageDataSource { get; internal set; } = new(isBook ? new Size(480, 640) : new Size(1000, 1500));
    public bool HasSize { get; internal set; } = isBook;
    public bool IsFileContent { get; } = isBook;
    internal IArchiveFactory? Archives { get; } = archives;
    internal FolderConfigCollection? FolderConfigs { get; } = folders;
    public string? Error { get; internal set; }
}
/// <summary>移除 WPF 图像后保留页面尺寸数据。</summary>
public sealed record PageDataSource(Size Size);
