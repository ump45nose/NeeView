// Copyright (c) NeeLaboratory. 原源码关系与业务规则沿用仓库 MIT；P1子集适配见 docs/source-migration.json。
using NeeLaboratory.IO.Search;
namespace NeeView;

/// <summary>原 Page 的 P1 内容部分；缩略图、媒体及标记依照迁移表后续接入。</summary>
public sealed class Page(ArchiveEntry entry, string? entryName = null, IArchiveFactory? archives = null, FolderConfigCollection? folders = null) : ISearchItem
{
    public ArchiveEntry ArchiveEntry { get; } = entry;
    public int Index { get; internal set; }
    public int EntryIndex => ArchiveEntry.Id;
    public string EntryName => entryName ?? ArchiveEntry.EntryName.TrimEnd('/');
    public string EntryFullName => ArchiveEntry.SystemPath;
    public string Prefix { get; internal set; } = "";
    public string EntrySmartName => EntryName.StartsWith(Prefix, StringComparison.Ordinal) ? EntryName[Prefix.Length..] : EntryName;
    /// <summary>原Smart/NameOnly/Raw/PageNumber格式，逻辑归档分隔已规范为'/'。</summary>
    public string GetDisplayName(PageNameFormat format) => format switch
    { PageNameFormat.Smart => EntrySmartName.Replace("/", " > ", StringComparison.Ordinal), PageNameFormat.NameOnly => EntryName.Split('/')[^1], PageNameFormat.PageNumber => (Index + 1).ToString(), _ => EntryName };
    public string GetSmartDirectoryDisplayString() => BookTableOfContents.DirectoryName(EntrySmartName).Replace("/", " > ", StringComparison.Ordinal);
    /// <summary>保留原页面查询属性；未迁图片metadata/rating明确失败，不返回虚假值。</summary>
    public SearchValue GetValue(SearchPropertyProfile profile, string? parameter, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        return profile.Name switch
        { "text" => new StringSearchValue(GetDisplayName(Config.Current.PageList.Format)), "date" => new DateTimeSearchValue(ArchiveEntry.LastWriteTime),
          "size" => new IntegerSearchValue(ArchiveEntry.Length), "playlist" => new BooleanSearchValue(IsMarked),
          _ => throw new NotSupportedException($"页面属性尚未迁移：{profile.Name}") };
    }
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
public sealed record PageDataSource(Size Size)
{
    /// <summary>原 BitmapInfo.GetAspectSize：方向校正后 Pixel×96/DPI，缺省与像素尺寸相同。</summary>
    public Size AspectSize { get; init; } = Size;
}
