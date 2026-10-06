// Copyright (c) NeeLaboratory. 原StartUpConfig列表恢复分支；LastBook等未迁字段仍由SaveData合并保留。
namespace NeeView;
public sealed class StartUpConfig
{
    public bool IsAutoPlaySlideShow { get; set; }
    public bool IsOpenLastFolder { get; set; }
    public bool IsOpenLastBookmarkFolder { get; set; }
    public BookshelfFolderMemento? LastFolder { get; set; }
    public BookshelfFolderMemento? LastBookmarkFolder { get; set; }
}
