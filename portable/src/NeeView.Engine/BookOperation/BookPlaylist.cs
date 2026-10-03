// Copyright (c) NeeLaboratory. 原 BookPlaylist 的普通目录/无密码归档子集。
namespace NeeView;
/// <summary>原书籍与全局列表的页面关系；媒体/临时来源尚未迁入。</summary>
public sealed class BookPlaylist(Book book, Playlist playlist)
{
    /// <summary>已支持后端拒绝密码/临时来源；只有书内真实图片可以登记。</summary>
    public bool CanRegister(Page page) => book.Pages.Contains(page) && !page.ArchiveEntry.IsDirectory;
    /// <summary>原 EntryFullName 精确匹配，不使用解压路径。</summary>
    public PlaylistItem? Find(Page page) => playlist.Items.FirstOrDefault(item => item.Path == page.EntryFullName);
    /// <summary>原 Collect 将全局登记映射到当前书的页面。</summary>
    public IEnumerable<Page> Collect()
    {
        var paths = playlist.Items.Select(item => item.Path).ToHashSet(StringComparer.Ordinal);
        return book.Pages.Where(page => paths.Contains(page.EntryFullName));
    }
}
