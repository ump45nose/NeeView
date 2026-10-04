// Copyright (c) NeeLaboratory. 迁自原 BookPageMarker；来源删除事件待文件操作批次迁入。
namespace NeeView;
/// <summary>原书内标记集合与导航算法；来源是当前全局播放列表。</summary>
public sealed class BookPageMarker(Book book)
{
    public List<Page> Markers { get; private set; } = [];
    /// <summary>按原页面对象身份判断登记，排序不丢失标记。</summary>
    public bool IsMarked(Page page) => Markers.Contains(page);
    /// <summary>原 SetMarkers 差集更新页面标记，不重建阅读帧。</summary>
    public void SetMarkers(IEnumerable<Page> pages)
    {
        var marks = pages.Distinct().ToList();
        foreach (var page in Markers.Except(marks)) page.IsMarked = false;
        foreach (var page in marks.Except(Markers)) page.IsMarked = true;
        Markers = marks;
    }
    /// <summary>原严格前后索引查找，保留首尾补项、循环和单页书边界。</summary>
    /// <param name="index">当前主图片排序索引。</param>
    /// <param name="direction">前一项 -1、后一项 +1。</param>
    /// <returns>符合参数的页面；无目标返回 null。</returns>
    public Page? GetNearMarkedPage(int index, int direction, bool isLoop, bool isIncludeTerminal)
    {
        if (book.Pages.Count < 2) return null;
        var list = Markers.Where(book.Pages.Contains).OrderBy(page => page.Index).ToList();
        if (isIncludeTerminal)
        {
            if (list.FirstOrDefault() != book.Pages.First()) list.Insert(0, book.Pages.First());
            if (list.LastOrDefault() != book.Pages.Last()) list.Add(book.Pages.Last());
        }
        if (list.Count == 0) return null;
        return direction > 0 ? list.FirstOrDefault(page => page.Index > index) ?? (isLoop ? list.First() : null)
            : list.LastOrDefault(page => page.Index < index) ?? (isLoop ? list.Last() : null);
    }
}
