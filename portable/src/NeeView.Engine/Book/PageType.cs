// Copyright (c) NeeLaboratory. 原PageType枚举与IsFolder，保留数值/排序。
namespace NeeView;
public enum PageType { Folder, Archive, File, Empty }
public static class PageTypeExtensions
{
    /// <summary>原可进入的书籍页既包括目录，也包括归档。</summary>
    public static bool IsFolder(this PageType pageType) => pageType is PageType.Folder or PageType.Archive;
}
