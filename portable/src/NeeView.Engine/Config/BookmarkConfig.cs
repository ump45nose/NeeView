// Copyright (c) NeeLaboratory. 来源：BookmarkConfig / FolderListConfig；仅迁入本批使用的字段。
namespace NeeView;

/// <summary>原书签列表默认排序和树/数量显示；其他字段继续由原 JSON 保留。</summary>
public sealed class BookmarkConfig
{
    public FolderOrder BookmarkFolderOrder { get; set; }
    public bool IsFolderTreeVisible { get; set; }
    public bool IsVisibleItemsCount { get; set; } = true;
    public bool IsVisibleSearchBox { get; set; } = true;
    public bool IsSearchIncludeSubdirectories { get; set; } = true;
}
