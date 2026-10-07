// Copyright (c) NeeLaboratory. 原StartUpConfig恢复分支，MIT。
namespace NeeView;
public sealed class StartUpConfig
{
    /// <summary>沿原默认关闭；显式打开优先，关闭本选项不会清除上次书籍快照。</summary>
    public bool IsOpenLastBook { get; set; }
    public bool IsAutoPlaySlideShow { get; set; }
    public bool IsOpenLastFolder { get; set; }
    public bool IsOpenLastBookmarkFolder { get; set; }
    [PropertyMapIgnore] public BookshelfFolderMemento? LastFolder { get; set; }
    [PropertyMapIgnore] public BookshelfFolderMemento? LastBookmarkFolder { get; set; }
}
